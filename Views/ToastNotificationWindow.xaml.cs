using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using OgConverter.Models;

namespace OgConverter.Views
{
    public partial class ToastNotificationWindow : Window
    {
        private string? _targetPath;
        private DispatcherTimer? _autoCloseTimer;

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

        /// <summary>Shows a "working" toast that stays up until <see cref="ShowJob"/> replaces it with the result.</summary>
        public void ShowProgress(string title, string sourcePath)
        {
            SetStatus("", "ModeToolsBrush", "AccentSubtleBrush");
            TitleText.Text = title;
            SubtitleText.Text = Path.GetFileName(sourcePath);
            MetricsText.Text = "Starting…";
            OpenFileBtn.Visibility = Visibility.Collapsed;
            ProgressTrack.Visibility = Visibility.Visible;
            ProgressScale.ScaleX = 0;
            PlaceAndShow();
        }

        /// <summary>0–1. Safe to call from any thread.</summary>
        public void ReportProgress(double fraction)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsVisible) return;
                ProgressScale.ScaleX = Math.Clamp(fraction, 0, 1);
                MetricsText.Text = $"{fraction * 100:0}%";
            });
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
            ProgressTrack.Visibility = Visibility.Collapsed;

            if (job.IsSuccess)
            {
                SetStatus("\uE73E", "SuccessBrush", "SuccessSubtleBrush");
                TitleText.Text = job.Option.Mode switch
                {
                    WheelMode.Convert => $"Converted to {job.Option.Title}",
                    WheelMode.Tools => $"{job.Option.Title} finished",
                    _ => job.Option.Title
                };
                SubtitleText.Text = job.Option.Mode == WheelMode.Actions
                    ? Path.GetFileName(job.SourcePath)
                    : $"{Path.GetFileName(job.SourcePath)}  →  {Path.GetFileName(job.TargetPath)}";

                string size = job.SizeComparisonText;
                MetricsText.Text = string.IsNullOrEmpty(size) ? job.DurationText : $"{size}  ·  {job.DurationText}";
                OpenFileBtn.Visibility = File.Exists(job.TargetPath) ? Visibility.Visible : Visibility.Collapsed;
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

            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
            _autoCloseTimer.Tick += (s, e) =>
            {
                _autoCloseTimer.Stop();
                Close();
            };
            _autoCloseTimer.Start();
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
