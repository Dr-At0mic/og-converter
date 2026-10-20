using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using OgConverter.Services;

namespace OgConverter
{
    /// <summary>Settings › System › Updates, and the About page's version, release notes and source code links.</summary>
    public partial class MainWindow
    {
        private ReleaseInfo? _availableUpdate;
        private CancellationTokenSource? _updateCts;

        private void SetupUpdates()
        {
            AboutVersionText.Text = $"Version {InstallService.CurrentVersion}  ·  Offline file conversion for Windows";
            UpdateStatusText.Text = $"You're on version {InstallService.CurrentVersion}. OG Converter only contacts GitHub when you check.";
        }

        private async void OnUpdateClick(object sender, RoutedEventArgs e)
        {
            if (_updateCts != null)
            {
                _updateCts.Cancel(); // the button reads "Cancel" while busy
                return;
            }

            _updateCts = new CancellationTokenSource();
            try
            {
                if (_availableUpdate == null) await CheckForUpdateAsync(_updateCts.Token);
                else await InstallUpdateAsync(_availableUpdate, _updateCts.Token);
            }
            finally
            {
                _updateCts.Dispose();
                _updateCts = null;
                UpdateButton.IsEnabled = true;
            }
        }

        private async System.Threading.Tasks.Task CheckForUpdateAsync(CancellationToken ct)
        {
            UpdateButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking GitHub for a newer version…";
            try
            {
                var latest = await UpdateService.GetLatestAsync(ct);
                if (!UpdateService.IsNewer(latest.Version, InstallService.CurrentVersion))
                {
                    UpdateTitleText.Text = "You're up to date";
                    UpdateStatusText.Text = $"Version {InstallService.CurrentVersion} is the latest release.";
                    UpdateButton.Content = "Check again";
                    return;
                }

                _availableUpdate = latest;
                UpdateTitleText.Text = $"Version {latest.Version} is available";
                UpdateNotesTitle.Text = $"WHAT'S NEW IN {latest.Version}";
                UpdateNotesText.Text = latest.Notes;
                UpdateNotesPanel.Visibility = Visibility.Visible;
                UpdateReleasePageButton.Visibility = Visibility.Visible;

                if (UpdateService.CanSelfUpdate && latest.ExeUrl != null)
                {
                    string size = latest.ExeSize > 0 ? $" ({latest.ExeSize / (1024 * 1024)} MB)" : string.Empty;
                    UpdateStatusText.Text = $"You're on {InstallService.CurrentVersion}. Updating downloads the new version{size}, checks it, and restarts OG Converter. Settings and templates are kept.";
                    UpdateButton.Content = $"Update to {latest.Version}";
                }
                else
                {
                    UpdateStatusText.Text = UpdateService.CanSelfUpdate
                        ? $"You're on {InstallService.CurrentVersion}. This release has no Windows download yet; check the release page."
                        : $"You're on {InstallService.CurrentVersion}. This is a development build, so it can't update itself: download the new version from the release page.";
                    UpdateButton.Visibility = Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                UpdateStatusText.Text = "GitHub didn't answer in time. Check your connection and try again.";
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException)
            {
                UpdateStatusText.Text = ex is HttpRequestException
                    ? "Couldn't reach GitHub. Check your connection and try again."
                    : $"Couldn't check for updates: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task InstallUpdateAsync(ReleaseInfo release, CancellationToken ct)
        {
            UpdateButton.Content = "Cancel";
            UpdateProgress.Value = 0;
            UpdateProgress.Visibility = Visibility.Visible;
            UpdateStatusText.Text = $"Downloading version {release.Version}…";

            string downloaded;
            try
            {
                var progress = new Progress<double>(p =>
                {
                    UpdateProgress.Value = p;
                    UpdateStatusText.Text = $"Downloading version {release.Version}… {p:P0}";
                });
                downloaded = await UpdateService.DownloadAsync(release, progress, ct);
            }
            catch (OperationCanceledException)
            {
                ResetUpdateButton(release, "Download cancelled.");
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                ResetUpdateButton(release, ex switch
                {
                    UnauthorizedAccessException => $"Can't write to {Path.GetDirectoryName(InstallService.CurrentExePath)}. Download the new version from the release page instead.",
                    HttpRequestException => "The download stopped. Check your connection and try again.",
                    _ => ex.Message
                });
                return;
            }

            UpdateStatusText.Text = "Installing and restarting…";
            try
            {
                _settingsService.Flush(); // the new copy reads the same settings file
                UpdateService.ApplyAndRestart(downloaded);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
            {
                try { File.Delete(downloaded); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                ResetUpdateButton(release, $"Couldn't finish the update: {ex.Message}");
                return;
            }
            Application.Current.Shutdown();
        }

        private void ResetUpdateButton(ReleaseInfo release, string status)
        {
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateButton.Content = $"Update to {release.Version}";
            UpdateStatusText.Text = status;
        }

        private void OnUpdateReleasePageClick(object sender, RoutedEventArgs e) =>
            OpenUrl(_availableUpdate?.PageUrl ?? UpdateService.ReleasesUrl);

        private void OnOpenReleaseNotesClick(object sender, RoutedEventArgs e) =>
            OpenUrl(UpdateService.ReleasePageFor(InstallService.CurrentVersion));

        private void OnOpenRepoClick(object sender, RoutedEventArgs e) => OpenUrl(UpdateService.RepoUrl);

        private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
