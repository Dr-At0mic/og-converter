using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using OgConverter.Services;

namespace OgConverter
{
    /// <summary>Settings › System (install, uninstall, start with Windows) and the Home install banner.</summary>
    public partial class MainWindow
    {
        private bool _syncingStartup;

        private void RefreshInstallState()
        {
            bool installed = InstallService.IsInstalled;
            bool runningInstalled = InstallService.IsRunningInstalled;
            string here = Path.GetDirectoryName(InstallService.CurrentExePath ?? string.Empty) ?? string.Empty;

            if (runningInstalled)
            {
                InstallTitleText.Text = "Installed";
                InstallStatusText.Text = $"Version {InstallService.CurrentVersion} in {InstallService.InstallDirectory}. Also listed in Windows Settings › Apps.";
                InstallButton.Visibility = Visibility.Collapsed;
            }
            else if (installed)
            {
                InstallTitleText.Text = "Installed copy found";
                InstallStatusText.Text = $"Version {InstallService.InstalledVersion ?? "?"} is installed, but you're running a portable copy ({InstallService.CurrentVersion}) from {here}. Update replaces the installed copy with this one.";
                InstallButton.Content = "Update installed copy";
                InstallButton.Visibility = Visibility.Visible;
            }
            else
            {
                InstallTitleText.Text = "Install OG Converter";
                InstallStatusText.Text = $"Running portable from {here}. Installing copies it to your user folder and adds it to Start and Windows Settings › Apps. No admin rights needed.";
                InstallButton.Content = "Install";
                InstallButton.Visibility = Visibility.Visible;
            }
            UninstallButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            InstallDesktopShortcutCheck.Visibility = runningInstalled ? Visibility.Collapsed : Visibility.Visible;

            _syncingStartup = true;
            StartWithWindowsCheck.IsChecked = InstallService.IsStartWithWindowsEnabled;
            _syncingStartup = false;
            StartupDescText.Text = installed || runningInstalled
                ? "Opens quietly in the system tray when you sign in, so the wheel is always ready"
                : "Opens quietly in the system tray when you sign in. Points to this copy: install first if you might move or delete it.";

            InstallBanner.Visibility = !installed && !runningInstalled && !_settingsService.Current.HideInstallBanner
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OnStartWithWindowsChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingStartup || _settingsService == null) return;
            bool enabled = StartWithWindowsCheck.IsChecked == true;
            try
            {
                InstallService.SetStartWithWindows(enabled);
                _settingsService.Current.StartWithWindows = enabled;
                _settingsService.SaveSettings(_settingsService.Current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                MessageBox.Show(this, $"Couldn't change the startup setting:\n{ex.Message}", "Start with Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            RefreshInstallState();
        }

        private void OnInstallClick(object sender, RoutedEventArgs e) =>
            RunInstall(startWithWindows: StartWithWindowsCheck.IsChecked == true || !InstallService.IsInstalled,
                       desktopShortcut: InstallDesktopShortcutCheck.IsChecked == true,
                       confirm: false);

        private void OnInstallBannerClick(object sender, RoutedEventArgs e) =>
            RunInstall(startWithWindows: true, desktopShortcut: false, confirm: true);

        private void OnDismissInstallBannerClick(object sender, RoutedEventArgs e)
        {
            _settingsService.Current.HideInstallBanner = true;
            _settingsService.SaveSettings(_settingsService.Current);
            InstallBanner.Visibility = Visibility.Collapsed;
        }

        private void RunInstall(bool startWithWindows, bool desktopShortcut, bool confirm)
        {
            if (confirm)
            {
                var answer = MessageBox.Show(this,
                    $"Install OG Converter to\n{InstallService.InstallDirectory}\n\nIt will be added to Start, listed in Windows Settings › Apps, and start in the tray when you sign in (you can turn that off in Settings › System).",
                    "Install OG Converter", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK) return;
            }

            try
            {
                Cursor = System.Windows.Input.Cursors.Wait;
                _settingsService.Flush(); // the installed copy reads the same settings file
                InstallService.Install(startWithWindows, desktopShortcut);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
            {
                Cursor = null;
                MessageBox.Show(this, $"Installation didn't finish:\n{ex.Message}", "Install OG Converter", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshInstallState();
                return;
            }

            Cursor = null;
            MessageBox.Show(this, "OG Converter is installed. It will now restart from its new home.", "Install OG Converter", MessageBoxButton.OK, MessageBoxImage.Information);
            try
            {
                InstallService.LaunchInstalled();
                Application.Current.Shutdown();
            }
            catch (Win32Exception ex)
            {
                MessageBox.Show(this, $"Installed, but couldn't start it:\n{ex.Message}\n\nOpen OG Converter from the Start menu.", "Install OG Converter", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshInstallState();
            }
        }

        private void OnUninstallClick(object sender, RoutedEventArgs e)
        {
            bool self = InstallService.IsRunningInstalled;
            var answer = MessageBox.Show(this,
                "Remove the installed OG Converter, its Start menu entry and the startup entry?\n\nYour settings and wheel templates are kept." +
                (self ? "\n\nOG Converter will close." : string.Empty),
                "Uninstall OG Converter", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            try
            {
                InstallService.Uninstall();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                MessageBox.Show(this, $"Couldn't finish removing OG Converter:\n{ex.Message}", "Uninstall OG Converter", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (self) Application.Current.Shutdown();
            else RefreshInstallState();
        }
    }
}
