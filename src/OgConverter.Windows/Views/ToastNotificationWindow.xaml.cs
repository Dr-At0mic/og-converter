using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OgConverter.Models;

namespace OgConverter.Views
{
    public partial class ToastNotificationWindow : Window
    {
        private string? _targetPath;
        private DispatcherTimer? _autoCloseTimer;
        private Action? _onCancel;

        /// <summary>True once dismissed; a closed window can't be shown again.</summary>
        public bool IsClosed { get; private set; }

        public ToastNotificationWindow()
        {
            InitializeComponent();
            Closed += (_, _) => IsClosed = true;
        }

        private void SetStatus(string glyph, string brushKey, string backgroundKey)
        {
            StatusIcon.Text = glyph;
            StatusIcon.Foreground = (System.Windows.Media.Brush)FindResource(brushKey);
            StatusBg.Background = (System.Windows.Media.Brush)FindResource(backgroundKey);
        }

        /// <summary>
        /// Shows a "working" toast that stays up until <see cref="ShowJob"/> or <see cref="ShowBatchResult"/>
        /// replaces it with the result. With <paramref name="onCancel"/>, it has a Cancel button.
        /// </summary>
        public void ShowProgress(string title, string sourcePath, Action? onCancel = null)
        {
            _onCancel = onCancel;
            CancelBtn.Visibility = onCancel != null ? Visibility.Visible : Visibility.Collapsed;
            CancelBtn.IsEnabled = true;
            SetStatus("", "ModeToolsBrush", "AccentSubtleBrush");
            TitleText.Text = title;
            SubtitleText.Text = Path.GetFileName(sourcePath);
            MetricsText.Text = "Working…";
            OpenFileBtn.Visibility = Visibility.Collapsed;
            ProgressTrack.Visibility = Visibility.Visible;
            ProgressScale.ScaleX = 0;
            SetSweep(true);
            PlaceAndShow();
        }

        private void SetSweep(bool on)
        {
            ProgressSweep.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            SweepShift.BeginAnimation(TranslateTransform.XProperty, on
                ? new DoubleAnimation(-1, 1, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = RepeatBehavior.Forever }
                : null);
        }

        /// <summary>0–1. Safe to call from any thread.</summary>
        public void ReportProgress(double fraction)
        {
            Dispatcher.BeginInvoke(() =>
            {
                // A report queued just before the result arrived must not overwrite it
                if (!IsVisible || ProgressTrack.Visibility != Visibility.Visible) return;
                if (ProgressSweep.Visibility == Visibility.Visible) SetSweep(false);
                ProgressScale.ScaleX = Math.Clamp(fraction, 0, 1);
                MetricsText.Text = $"{fraction * 100:0}%";
            });
        }

        /// <summary>Batch progress: the subtitle carries the counts, the bar the overall fraction (0–1).</summary>
        public void ReportBatch(string title, string subtitle, double fraction)
        {
            if (!IsVisible || ProgressTrack.Visibility != Visibility.Visible) return;
            TitleText.Text = title;
            SubtitleText.Text = subtitle;
            ReportProgress(fraction);
        }

        /// <summary>Replaces a batch's progress toast with its summary; Open shows <paramref name="folder"/>.</summary>
        public void ShowBatchResult(bool allSucceeded, string title, string subtitle, string metrics, string? folder)
        {
            _targetPath = folder;
            HideRunningParts();
            if (allSucceeded) SetStatus("", "SuccessBrush", "SuccessSubtleBrush");
            else SetStatus("", "WarningBrush", "WarningSubtleBrush");
            TitleText.Text = title;
            SubtitleText.Text = subtitle;
            SubtitleText.ToolTip = subtitle;
            MetricsText.Text = metrics;
            MetricsText.ToolTip = metrics;
            OpenFileBtn.Content = "Open folder";
            OpenFileBtn.Visibility = folder != null && Directory.Exists(folder) ? Visibility.Visible : Visibility.Collapsed;
            PlaceAndShow();
            StartAutoClose(6);
        }

        private void HideRunningParts()
        {
            SetSweep(false);
            ProgressTrack.Visibility = Visibility.Collapsed;
            CancelBtn.Visibility = Visibility.Collapsed;
            _onCancel = null;
        }

        private void StartAutoClose(double seconds)
        {
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            _autoCloseTimer.Tick += (s, e) =>
            {
                _autoCloseTimer.Stop();
                Close();
            };
            _autoCloseTimer.Start();
        }

        private void PlaceAndShow()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 16;
            Top = workArea.Bottom - Height - 16;
            if (!IsVisible) Show();
        }

        public void ShowJob(ConversionJob job)
        {
            _targetPath = job.TargetPath;
            HideRunningParts();

            if (job.IsSuccess)
            {
                SetStatus("\uE73E", "SuccessBrush", "SuccessSubtleBrush");
                TitleText.Text = job.Option.Mode switch
                {
                    WheelMode.Convert => $"Converted to {job.Option.Title}",
                    WheelMode.Tools => $"{job.Option.Title} finished",
                    _ => job.Option.Title
                };
                SubtitleText.Text = job.Option.Mode == WheelMode.Actions || job.ResultText != null
                    ? Path.GetFileName(job.SourcePath)
                    : $"{Path.GetFileName(job.SourcePath)}  →  {Path.GetFileName(job.TargetPath)}";

                string detail = job.DetailText;
                MetricsText.Text = string.IsNullOrEmpty(detail) ? job.DurationText : $"{detail}  ·  {job.DurationText}";
                MetricsText.ToolTip = job.ResultText;
                // Nothing new to open when the result went to the clipboard
                OpenFileBtn.Visibility = File.Exists(job.TargetPath) && job.ResultText == null ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                SetStatus("\uE711", "DangerBrush", "DangerSubtleBrush");
                TitleText.Text = $"{job.Option.Title} failed";
                SubtitleText.Text = job.ErrorMessage ?? "Unknown error";
                SubtitleText.ToolTip = job.ErrorMessage;
                MetricsText.Text = Path.GetFileName(job.SourcePath);
                OpenFileBtn.Visibility = Visibility.Collapsed;
            }

            // Position at bottom-right of primary screen
            PlaceAndShow();
            StartAutoClose(4.5);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _onCancel?.Invoke();
            CancelBtn.IsEnabled = false;
            MetricsText.Text = "Cancelling…";
        }

        private void OnOpenFileClick(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_targetPath))
            {
                if (File.Exists(_targetPath))
                {
                    Process.Start(new ProcessStartInfo(_targetPath) { UseShellExecute = true });
                }
                else if (Directory.Exists(_targetPath))
                {
                    Process.Start(new ProcessStartInfo(_targetPath) { UseShellExecute = true });
                }
            }
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
