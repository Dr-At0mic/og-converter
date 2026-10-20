using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// First-run / post-install prompt for the free helpers that aren't on this PC: FFmpeg, and LibreOffice when
    /// neither Microsoft Office nor LibreOffice is installed. Installs only what the user ticks.
    /// </summary>
    public partial class ExtrasDialog : Window
    {
        /// <summary>Installs one helper; returns null when it's ready, otherwise what went wrong.</summary>
        private readonly Func<Dependency, Task<string?>> _install;
        private bool _busy;
        private bool _finished;

        public ExtrasDialog(bool offerFFmpeg, bool offerLibreOffice, Func<Dependency, Task<string?>> install)
        {
            InitializeComponent();
            _install = install;

            FFmpegRow.Visibility = offerFFmpeg ? Visibility.Visible : Visibility.Collapsed;
            LibreOfficeRow.Visibility = offerLibreOffice ? Visibility.Visible : Visibility.Collapsed;

            if (!DependencyService.CanInstall)
            {
                // No winget (some LTSC / older Windows 10 setups): open the official download pages instead
                FooterNote.Text = "winget isn't available on this PC, so the official download pages open in your browser instead.";
                ConfirmButton.Content = "Open download pages";
            }

            Closing += (_, e) => e.Cancel = _busy;
        }

        private async void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            if (_finished)
            {
                DialogResult = true;
                return;
            }

            var picked = new List<(Dependency Dep, TextBlock Status, CheckBox Check)>();
            if (FFmpegRow.Visibility == Visibility.Visible && FFmpegCheck.IsChecked == true) picked.Add((Dependency.FFmpeg, FFmpegStatus, FFmpegCheck));
            if (LibreOfficeRow.Visibility == Visibility.Visible && LibreOfficeCheck.IsChecked == true) picked.Add((Dependency.LibreOffice, LibreOfficeStatus, LibreOfficeCheck));
            if (picked.Count == 0)
            {
                DialogResult = false;
                return;
            }

            if (!DependencyService.CanInstall)
            {
                foreach (var p in picked)
                {
                    try { DependencyService.OpenDownloadPage(p.Dep); }
                    catch (Win32Exception) { }
                }
                DialogResult = true;
                return;
            }

            _busy = true;
            ConfirmButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            FFmpegCheck.IsEnabled = LibreOfficeCheck.IsEnabled = false;

            bool allOk = true;
            foreach (var (dep, status, _) in picked)
            {
                SetStatus(status, dep == Dependency.LibreOffice
                    ? "Installing… this is a large download and can take several minutes. Approve the Windows admin prompt when it appears."
                    : "Installing…", "TextSecondaryBrush");

                string? error = await _install(dep);
                if (error == null)
                {
                    SetStatus(status, "✓ Installed and ready", "SuccessBrush");
                }
                else
                {
                    allOk = false;
                    SetStatus(status, error, "WarningBrush");
                }
            }

            _busy = false;
            _finished = true;
            ConfirmButton.Content = "Done";
            ConfirmButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            FooterNote.Text = allOk
                ? "All set. The new formats show up on the wheel right away."
                : "You can try again any time from Settings › Engine.";
        }

        private static void SetStatus(TextBlock status, string text, string brushKey)
        {
            status.Text = text;
            status.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            status.Visibility = Visibility.Visible;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
