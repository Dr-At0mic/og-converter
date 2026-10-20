using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>Lets the user draw the area to keep on a preview of the image (or a frame of the video), for CROP.</summary>
    public partial class CropDialog : DialogWindow
    {
        private readonly string _path = string.Empty;
        private readonly bool _isVideo;
        private readonly string? _ffmpeg;
        private readonly DispatcherTimer _frameTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
        private int _frameRequest;

        public ToolParameters? Result { get; private set; }

        /// <summary>For the XAML previewer.</summary>
        public CropDialog() => InitializeComponent();

        public CropDialog(string path, bool isVideo, string? ffmpeg)
        {
            InitializeComponent();
            _path = path;
            _isVideo = isVideo;
            _ffmpeg = ffmpeg;

            TitleText.Text = isVideo ? "Crop video" : "Crop image";
            FileInfoText.Text = Path.GetFileName(path);
            ToolTip.SetTip(FileInfoText, path);
            _frameTimer.Tick += async (_, _) =>
            {
                _frameTimer.Stop();
                await LoadFrameAsync(FrameSlider.Value);
            };

            // Arrow keys nudge the box, before the focused control (a chip, a button) moves focus with them
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

            Opened += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                if (!_isVideo)
                {
                    var image = await MediaEditService.LoadImageAsync(_path, _ffmpeg);
                    if (image == null)
                    {
                        ShowStatus("This image can't be previewed, so it can't be cropped here.");
                        return;
                    }
                    Cropper.Source = image.ToBitmap();
                }
                else
                {
                    if (_ffmpeg == null)
                    {
                        ShowStatus("Cropping a video needs FFmpeg, which wasn't found." +
                                   (PackageHints.InstallCommand(EngineTool.FFmpeg) is { } command ? $" Install it with: {command}" : " Install it with your package manager."));
                        return;
                    }

                    var info = await MediaToolkit.ProbeAsync(_ffmpeg, _path);
                    // Skip the first moments, which are often black
                    double at = info.DurationSeconds > 0 ? Math.Min(info.DurationSeconds * 0.1, 5) : 0;
                    var frame = await MediaEditService.GetVideoFrameAsync(_ffmpeg, _path, at);
                    if (frame == null)
                    {
                        ShowStatus("Couldn't read a frame from this video.");
                        return;
                    }
                    Cropper.Source = frame.ToBitmap();

                    if (info.DurationSeconds > 1)
                    {
                        FrameSlider.Maximum = info.DurationSeconds;
                        FrameSlider.SmallChange = 1;
                        FrameSlider.LargeChange = Math.Max(1, info.DurationSeconds / 10);
                        FrameSlider.Value = at;
                        FramePanel.IsVisible = true;
                    }
                }

                StatusText.IsVisible = false;
                FileInfoText.Text = $"{Path.GetFileName(_path)}  •  {Cropper.ImageWidth} × {Cropper.ImageHeight}";
                Cropper.Focus();
            }
            catch (Exception ex)
            {
                ShowStatus("Couldn't open the preview: " + ex.Message);
            }
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text;
            StatusText.IsVisible = true;
            ConfirmButton.IsEnabled = false;
        }

        private void OnFrameSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            FrameTimeText.Text = FormatTime(e.NewValue);
            if (!IsLoaded || Cropper.Source == null) return;
            _frameTimer.Stop();
            _frameTimer.Start();
        }

        private async Task LoadFrameAsync(double seconds)
        {
            int request = ++_frameRequest;
            BgraImage? frame;
            try { frame = await MediaEditService.GetVideoFrameAsync(_ffmpeg!, _path, seconds); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException) { return; }
            // A newer request may have finished first while the slider kept moving
            if (frame != null && request == _frameRequest) Cropper.ReplaceFrame(frame.ToBitmap());
        }

        private void OnRatioChecked(object? sender, RoutedEventArgs e)
        {
            if (!IsLoaded || Cropper.Source == null || sender is not RadioButton { IsChecked: true, Tag: string tag }) return;

            Cropper.AspectRatio = tag switch
            {
                "free" => null,
                "original" => (double)Cropper.ImageWidth / Cropper.ImageHeight,
                _ => ParseRatio(tag)
            };
        }

        private static double ParseRatio(string tag)
        {
            var parts = tag.Split(':');
            return double.Parse(parts[0], CultureInfo.InvariantCulture) / double.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        private void OnSelectionChanged(object? sender, EventArgs e)
        {
            if (Cropper.Source == null) return;
            var r = Cropper.PixelSelection;
            // Video frames are cut to even sizes, which 4:2:0 encoding requires
            int w = _isVideo ? Math.Max(2, r.Width & ~1) : r.Width, h = _isVideo ? Math.Max(2, r.Height & ~1) : r.Height;
            SizeText.Text = $"{w} × {h} px";
            ConfirmButton.IsEnabled = !Cropper.IsWholeImage;
        }

        private void OnResetClick(object? sender, RoutedEventArgs e) => Cropper.Reset();

        private void OnViewChanged(object? sender, EventArgs e)
        {
            ZoomText.Text = Cropper.Source == null ? "" : $"{Cropper.PixelScale * 100:0}%";
            ZoomInButton.IsEnabled = Cropper.CanZoomIn;
            ZoomOutButton.IsEnabled = Cropper.CanZoomOut;
        }

        private void OnZoomInClick(object? sender, RoutedEventArgs e) => Cropper.ZoomBy(1.5);
        private void OnZoomOutClick(object? sender, RoutedEventArgs e) => Cropper.ZoomBy(1 / 1.5);
        private void OnZoomFitClick(object? sender, RoutedEventArgs e) => Cropper.ZoomToFit();
        private void OnZoomWidthClick(object? sender, RoutedEventArgs e) => Cropper.ZoomToWidth();

        private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (Cropper.Source == null || FrameSlider.IsKeyboardFocusWithin) return;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                switch (e.Key)
                {
                    case Key.OemPlus or Key.Add: Cropper.ZoomBy(1.5); break;
                    case Key.OemMinus or Key.Subtract: Cropper.ZoomBy(1 / 1.5); break;
                    case Key.D0 or Key.NumPad0: Cropper.ZoomToFit(); break;
                    default: return;
                }
                e.Handled = true;
                return;
            }

            double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            (double dx, double dy) = e.Key switch
            {
                Key.Left => (-step, 0d),
                Key.Right => (step, 0d),
                Key.Up => (0d, -step),
                Key.Down => (0d, step),
                _ => (0d, 0d)
            };
            if (dx == 0 && dy == 0) return;
            Cropper.Nudge(dx, dy);
            e.Handled = true;
        }

        private void OnConfirmClick(object? sender, RoutedEventArgs e)
        {
            if (!ConfirmButton.IsEnabled || Cropper.Source == null) return;
            Result = new ToolParameters { Crop = Cropper.PixelSelection };
            Finish(true);
        }

        internal static string FormatTime(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.f", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);
        }
    }
}
