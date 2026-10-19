using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>Picks the start and end of the part of a video to keep, with preview frames, for TRIM.</summary>
    public partial class TrimDialog : Window
    {
        private const int PreviewWidth = 640;

        private readonly string _path;
        private readonly string? _ffmpeg;
        private double _duration, _frameStep = 1 / 30.0;

        private readonly FramePreview _startPreview, _endPreview;
        private double _shownStart = -1, _shownEnd = -1;

        public ToolParameters? Result { get; private set; }

        public TrimDialog(string path, string? ffmpeg)
        {
            InitializeComponent();
            _path = path;
            _ffmpeg = ffmpeg;
            FileInfoText.Text = Path.GetFileName(path);
            FileInfoText.ToolTip = path;

            _startPreview = new FramePreview(this, StartImage, StartStatus);
            _endPreview = new FramePreview(this, EndImage, EndStatus);

            Loaded += async (_, _) =>
            {
                Activate();
                await LoadAsync();
            };
        }

        private async Task LoadAsync()
        {
            if (_ffmpeg == null)
            {
                StartStatus.Text = EndStatus.Text = "";
                FileInfoText.Text = "Trimming needs FFmpeg, which wasn't found. Install it with: winget install Gyan.FFmpeg";
                return;
            }

            var info = await MediaToolkit.ProbeAsync(_ffmpeg, _path);
            if (info.DurationSeconds <= 0)
            {
                StartStatus.Text = EndStatus.Text = "Couldn't read the length of this video.";
                return;
            }

            _duration = info.DurationSeconds;
            if (info.Fps > 0) _frameStep = 1 / info.Fps;
            FileInfoText.Text = $"{Path.GetFileName(_path)}  •  {CropDialog.FormatTime(_duration)}" + (info.Width > 0 ? $"  •  {info.Width} × {info.Height}" : "");
            DurationText.Text = CropDialog.FormatTime(_duration);

            Timeline.MinLength = Math.Min(0.1, _duration);
            Timeline.Duration = _duration;
            Timeline.SetRange(0, _duration);
            StartBox.Focus();
            StartBox.SelectAll();
        }

        private void OnRangeChanged(object? sender, EventArgs e)
        {
            if (_duration <= 0) return;

            StartBox.Text = CropDialog.FormatTime(Timeline.Start);
            EndBox.Text = CropDialog.FormatTime(Timeline.End);
            LengthText.Text = $"Keeps {CropDialog.FormatTime(Timeline.End - Timeline.Start)}";
            ConfirmButton.IsEnabled = Timeline.Start > 0.05 || Timeline.End < _duration - 0.05;

            if (Math.Abs(Timeline.Start - _shownStart) > 0.001)
            {
                _shownStart = Timeline.Start;
                _startPreview.Request(_ffmpeg!, _path, Timeline.Start);
            }
            if (Math.Abs(Timeline.End - _shownEnd) > 0.001)
            {
                _shownEnd = Timeline.End;
                // The frame at the very end time doesn't exist; show the last one before it
                _endPreview.Request(_ffmpeg!, _path, Math.Max(0, Timeline.End - _frameStep));
            }
        }

        private void OnTimeBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            Commit((TextBox)sender);
            e.Handled = true;
        }

        private void OnTimeBoxCommit(object sender, KeyboardFocusChangedEventArgs e) => Commit((TextBox)sender);

        private void Commit(TextBox box)
        {
            if (_duration <= 0) return;
            if (TryParseTime(box.Text, out double t))
            {
                if (box == StartBox) Timeline.SetRange(Math.Min(t, Timeline.End - Timeline.MinLength), Timeline.End);
                else Timeline.SetRange(Timeline.Start, Math.Max(t, Timeline.Start + Timeline.MinLength));
            }
            // Reformat (or undo an invalid entry)
            OnRangeChanged(null, EventArgs.Empty);
        }

        /// <summary>Accepts "65.5", "1:05", "1:05.5" and "1:02:03".</summary>
        internal static bool TryParseTime(string text, out double seconds)
        {
            seconds = 0;
            var parts = text.Trim().Split(':');
            if (parts.Length is < 1 or > 3) return false;
            foreach (string part in parts)
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0) return false;
                seconds = seconds * 60 + v;
            }
            return true;
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            if (!ConfirmButton.IsEnabled) return;
            // Typed values that haven't been committed yet still count
            Commit(StartBox);
            Commit(EndBox);
            Result = new ToolParameters { TrimStart = Timeline.Start, TrimEnd = Timeline.End, TrimFast = FastCheck.IsChecked == true };
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        /// <summary>Loads one preview frame at a time, skipping positions passed over while a handle is being dragged.</summary>
        private sealed class FramePreview
        {
            private readonly Image _image;
            private readonly TextBlock _status;
            private readonly DispatcherTimer _timer;
            private string _ffmpeg = "", _path = "";
            private double _pending;
            private int _version;

            public FramePreview(Window owner, Image image, TextBlock status)
            {
                _image = image;
                _status = status;
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, async (_, _) => await LoadAsync(), owner.Dispatcher);
                _timer.Stop();
            }

            public void Request(string ffmpeg, string path, double seconds)
            {
                _ffmpeg = ffmpeg;
                _path = path;
                _pending = seconds;
                _timer.Stop();
                _timer.Start();
            }

            private async Task LoadAsync()
            {
                _timer.Stop();
                int version = ++_version;
                var frame = await MediaEditService.GetVideoFrameAsync(_ffmpeg, _path, _pending, PreviewWidth);
                if (version != _version) return;
                _image.Source = frame;
                _status.Text = frame == null ? "No frame here" : "";
            }
        }
    }
}
