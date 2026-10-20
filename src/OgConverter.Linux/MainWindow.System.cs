using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Input;
using Avalonia.Interactivity;
using OgConverter.Services;
using OgConverter.Views;

namespace OgConverter
{
    /// <summary>Settings › System (install, uninstall, start at login) and the Home install banner.</summary>
    public partial class MainWindow
    {
        private bool _syncingStartup;

        private void RefreshInstallState()
        {
            bool installed = LinuxInstallService.IsInstalled;
            bool runningInstalled = LinuxInstallService.IsRunningInstalled;
            string here = LinuxInstallService.AppImagePath ?? Path.GetDirectoryName(LinuxInstallService.CurrentExePath ?? string.Empty) ?? string.Empty;

            if (runningInstalled)
            {
                InstallTitleText.Text = "Installed";
                InstallStatusText.Text = $"Version {LinuxInstallService.CurrentVersion} in {LinuxInstallService.InstallDirectory}. Also in your app menu.";
                InstallButton.IsVisible = false;
            }
            else if (installed)
            {
                InstallTitleText.Text = "Installed copy found";
                InstallStatusText.Text = $"Version {LinuxInstallService.InstalledVersion ?? "?"} is installed, but you're running a portable copy ({LinuxInstallService.CurrentVersion}) from {here}. Update replaces the installed copy with this one.";
                InstallButton.Content = "Update installed copy";
                InstallButton.IsVisible = true;
            }
            else
            {
                InstallTitleText.Text = "Install OG Converter";
                InstallStatusText.Text = $"Running portable from {here}. Installing copies it to your home folder and adds it to the app menu. No admin rights needed.";
                InstallButton.Content = "Install";
                InstallButton.IsVisible = true;
            }
            UninstallButton.IsVisible = installed;
            InstallDesktopShortcutCheck.IsVisible = !runningInstalled;

            _syncingStartup = true;
            StartAtLoginCheck.IsChecked = LinuxInstallService.IsStartAtLoginEnabled;
            _syncingStartup = false;
            StartupDescText.Text = installed || runningInstalled
                ? "Opens quietly in the system tray when you log in, so the wheel is always ready"
                : "Opens quietly in the system tray when you log in. Points to this copy: install first if you might move or delete it.";

            InstallBanner.IsVisible = !installed && !runningInstalled && !_settingsService.Current.HideInstallBanner;
        }

        private async void OnStartAtLoginChanged(object? sender, RoutedEventArgs e)
        {
            if (_syncingStartup || _settingsService == null) return;
            bool enabled = StartAtLoginCheck.IsChecked == true;
            try
            {
                LinuxInstallService.SetStartAtLogin(enabled);
                _settingsService.Current.StartWithWindows = enabled; // the shared setting; the autostart file is the truth on Linux
                _settingsService.SaveSettings(_settingsService.Current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                await MessageDialog.ShowAsync(this, "Start when you log in", $"Couldn't change the startup setting:\n{ex.Message}", MessageKind.Warning);
            }
            RefreshInstallState();
        }

        private void OnInstallClick(object? sender, RoutedEventArgs e) =>
            RunInstall(startAtLogin: StartAtLoginCheck.IsChecked == true || !LinuxInstallService.IsInstalled,
                       desktopShortcut: InstallDesktopShortcutCheck.IsChecked == true,
                       confirm: false);

        private void OnInstallBannerClick(object? sender, RoutedEventArgs e) =>
            RunInstall(startAtLogin: true, desktopShortcut: false, confirm: true);

        private void OnDismissInstallBannerClick(object? sender, RoutedEventArgs e)
        {
            _settingsService.Current.HideInstallBanner = true;
            _settingsService.SaveSettings(_settingsService.Current);
            InstallBanner.IsVisible = false;
        }

        private async void RunInstall(bool startAtLogin, bool desktopShortcut, bool confirm)
        {
            if (confirm && !await MessageDialog.ConfirmAsync(this, "Install OG Converter",
                    $"Install OG Converter to\n{LinuxInstallService.InstallDirectory}\n\nIt will be added to your app menu and start in the tray when you log in (you can turn that off in Settings › System).",
                    "Install"))
            {
                return;
            }

            try
            {
                Cursor = new Cursor(StandardCursorType.Wait);
                _settingsService.Flush(); // the installed copy reads the same settings file
                LinuxInstallService.Install(startAtLogin, desktopShortcut);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
            {
                Cursor = null;
                await MessageDialog.ShowAsync(this, "Install OG Converter", $"Installation didn't finish:\n{ex.Message}", MessageKind.Warning);
                RefreshInstallState();
                return;
            }

            Cursor = null;
            await MessageDialog.ShowAsync(this, "Install OG Converter", "OG Converter is installed. It will now restart from its new home.");
            try
            {
                LinuxInstallService.LaunchInstalledAfterExit();
                App.Quit();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                await MessageDialog.ShowAsync(this, "Install OG Converter",
                    $"Installed, but couldn't start it:\n{ex.Message}\n\nOpen OG Converter from the app menu.", MessageKind.Warning);
                RefreshInstallState();
            }
        }

        private async void OnUninstallClick(object? sender, RoutedEventArgs e)
        {
            bool self = LinuxInstallService.IsRunningInstalled;
            if (!await MessageDialog.ConfirmAsync(this, "Uninstall OG Converter",
                    "Remove the installed OG Converter, its app menu entry, the startup entry and the file manager menus?\n\nYour settings and wheel templates are kept." +
                    (self ? "\n\nOG Converter will close." : string.Empty),
                    "Uninstall"))
            {
                return;
            }

            try
            {
                LinuxInstallService.Uninstall();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                await MessageDialog.ShowAsync(this, "Uninstall OG Converter", $"Couldn't finish removing OG Converter:\n{ex.Message}", MessageKind.Warning);
            }

            if (self) App.Quit();
            else
            {
                RefreshInstallState();
                RefreshIntegration();
            }
        }
    }
}
