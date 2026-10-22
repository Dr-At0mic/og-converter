using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OgConverter.Services;
using OgConverter.Views;

namespace OgConverter
{
    /// <summary>
    /// Optional helpers: the "Recommended extras" prompt (first run and right after Install) and the Install buttons in
    /// Settings › Engine. FFmpeg is offered when missing; LibreOffice only when neither Microsoft Office nor LibreOffice is
    /// found. The browser is only reported.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Set by App when this copy was just started by Install, so the prompt shows even if it was seen before.</summary>
        public bool JustInstalled { get; set; }

        /// <summary>Off for snapshots, which show the window off-screen and can't answer a dialog.</summary>
        public bool OfferExtras { get; set; } = true;

        private void SetupExtrasPrompt()
        {
            ContentRendered += async (_, _) =>
            {
                await Task.Delay(400); // let the window settle before a dialog appears over it
                ShowExtrasPromptIfNeeded();
            };
        }

        private void ShowExtrasPromptIfNeeded()
        {
            if (!OfferExtras || (!JustInstalled && _settingsService.Current.ExtrasPromptShown)) return;

            bool ffmpegMissing = !_engine.IsFFmpegAvailable;
            bool officeMissing = !DocumentEngine.HasAnyBackend;
            if (!ffmpegMissing && !officeMissing) return;

            _settingsService.Current.ExtrasPromptShown = true;
            _settingsService.SaveSettings(_settingsService.Current);

            var dialog = new ExtrasDialog(ffmpegMissing, officeMissing, InstallDependencyAsync) { Owner = this };
            dialog.ShowDialog();
        }

        private async void OnInstallFFmpegClick(object sender, RoutedEventArgs e) =>
            await InstallFromSettingsAsync(Dependency.FFmpeg, InstallFFmpegButton, FFmpegStatusText);

        private async void OnInstallLibreOfficeClick(object sender, RoutedEventArgs e) =>
            await InstallFromSettingsAsync(Dependency.LibreOffice, InstallLibreOfficeButton, DocEngineStatusText);

        private async Task InstallFromSettingsAsync(Dependency dep, Button button, TextBlock badgeText)
        {
            if (!DependencyService.CanInstall)
            {
                OpenDownloadPage(dep);
                return;
            }

            if (dep == Dependency.LibreOffice)
            {
                var answer = MessageBox.Show(this,
                    "Install LibreOffice, the free office suite?\n\nIt's about 350 MB and can take several minutes. Windows will ask for admin permission once.\n\n" +
                    "Prefer Microsoft Office? It isn't free, but if you install it, OG Converter uses it automatically.",
                    "Install LibreOffice", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK) return;
            }

            object content = button.Content;
            button.IsEnabled = false;
            button.Content = "Installing…";
            badgeText.Text = "Installing…";

            string? error = await InstallDependencyAsync(dep);

            button.Content = content;
            button.IsEnabled = true;
            if (error != null)
            {
                MessageBox.Show(this, error, $"Install {DependencyService.DisplayName(dep)}", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Installs <paramref name="dep"/> and looks for it again. Returns null when it's ready to use, otherwise why not
        /// (also when <paramref name="cancellationToken"/> stopped it).
        /// </summary>
        private async Task<string?> InstallDependencyAsync(Dependency dep, CancellationToken cancellationToken = default)
        {
            string name = DependencyService.DisplayName(dep);
            int exitCode;
            string output;
            try
            {
                (exitCode, output) = await DependencyService.InstallAsync(dep, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // It may have got far enough to be usable anyway
                if (dep == Dependency.FFmpeg) _engine.RefreshFFmpeg();
                else DocumentEngine.RefreshLibreOffice();
                CheckFFmpegStatus();
                bool installed = dep == Dependency.FFmpeg ? _engine.IsFFmpegAvailable : DocumentEngine.LibreOfficePath != null;
                return installed ? null : "Install cancelled. You can try again any time from Settings › Engine.";
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                CheckFFmpegStatus();
                return $"Couldn't start the installer: {ex.Message}\n\nYou can download {name} from {DependencyService.DownloadPage(dep)}";
            }

            // winget's exit code isn't enough ("already installed" is an error to it): success means we can find it now
            if (dep == Dependency.FFmpeg) _engine.RefreshFFmpeg();
            else DocumentEngine.RefreshLibreOffice();
            CheckFFmpegStatus();

            bool ready = dep == Dependency.FFmpeg ? _engine.IsFFmpegAvailable : DocumentEngine.LibreOfficePath != null;
            if (ready) return null;

            string detail = string.IsNullOrWhiteSpace(output) ? $"winget exit code 0x{exitCode:X8}" : output;
            return exitCode == 0
                ? $"{name} was installed, but OG Converter can't find it yet. Restart OG Converter to pick it up."
                : $"{name} wasn't installed ({detail}). If you cancelled the admin prompt, try again, or download it from {DependencyService.DownloadPage(dep)}";
        }

        private void OpenDownloadPage(Dependency dep)
        {
            try { DependencyService.OpenDownloadPage(dep); }
            catch (Win32Exception ex)
            {
                MessageBox.Show(this, $"Couldn't open the browser:\n{ex.Message}\n\n{DependencyService.DownloadPage(dep)}",
                    $"Install {DependencyService.DisplayName(dep)}", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
