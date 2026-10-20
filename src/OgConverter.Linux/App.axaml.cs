using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter
{
    /// <summary>
    /// Starts the dashboard (shown, or hidden in the tray for autostart and file-manager menus), the tray icon and the
    /// wheel's listeners. Closing the window keeps the app running in the tray when there is one, because the GNOME
    /// helper's key taps only reach a running app.
    /// </summary>
    public partial class App : Application
    {
        private MainWindow? _main;
        private TrayService? _tray;
        private bool _hasTrayHost;
        private bool _quitting;

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        /// <summary>Ends the app (tray Quit, after Install or an update restarts it elsewhere).</summary>
        public static void Quit()
        {
            if (Current is App app) app._quitting = true;
            (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // TODO(merge): the engine branch registers Platform/Linux/AvaloniaUiDispatcher in LinuxPlatform.Register();
                // until then Core's settings timer ticks on the thread pool, which SettingsService tolerates.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                string[] args = desktop.Args ?? Array.Empty<string>();

                UpdateService.CleanupAfterUpdate();

                _main = new MainWindow { JustInstalled = args.Contains(LinuxInstallService.JustInstalledArg) };
                _main.Closing += OnMainClosing;
                // TODO(merge): WheelController.ActivateRequested is raised by the wheel branch when a second launch asks
                // this one to come forward.
                _main.Wheel.ActivateRequested += ShowDashboard;

                _tray = new TrayService();
                _tray.OpenRequested += ShowDashboard;
                _tray.PreviewWheelRequested += () => _main.PreviewWheel();
                _tray.QuitRequested += Quit;
                _tray.Initialize(this);

                // Autostart, "Start hidden", and a launch from a file-manager menu (it shows a wheel, not the dashboard)
                bool hidden = args.Contains(LinuxInstallService.BackgroundArg) || LinuxPreferences.Current.StartHidden || WheelController.StartedForWheel;
                if (!hidden) desktop.MainWindow = _main;
                _main.StartServices();

                desktop.ShutdownRequested += (_, _) =>
                {
                    _quitting = true;
                    _main.Shutdown();
                    _tray?.Dispose();
                };

                _ = CheckTrayHostAsync(hidden);
            }
            base.OnFrameworkInitializationCompleted();
        }

        private async System.Threading.Tasks.Task CheckTrayHostAsync(bool startedHidden)
        {
            _hasTrayHost = await TrayService.IsHostAvailableAsync();
            // Started hidden with nowhere to hide: nothing would show the app, so show the dashboard instead
            if (startedHidden && !_hasTrayHost && !WheelController.StartedForWheel) ShowDashboard();
        }

        private void ShowDashboard()
        {
            if (_main == null) return;
            _main.Show();
            if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
            _main.Activate();
        }

        /// <summary>Closing hides the window into the tray (the first time, a notification says so); without a tray it quits.</summary>
        private void OnMainClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_quitting || _main == null) return;
            if (!_hasTrayHost)
            {
                e.Cancel = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(Quit);
                return;
            }

            e.Cancel = true;
            _main.Hide();
            if (!LinuxPreferences.Current.TrayHintShown)
            {
                LinuxPreferences.Current.TrayHintShown = true;
                LinuxPreferences.Current.Save();
                _main.Notifications.ShowInfo("OG Converter is still running",
                    "The wheel keeps working from the tray. Use Quit in the tray menu to close it completely.");
            }
        }
    }
}
