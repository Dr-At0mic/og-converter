using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Views;
using OgConverter.Wheel;

namespace OgConverter.Services
{
    /// <summary>
    /// Opens the radial wheel: from the GNOME Shell extension's key taps during a drag, from <c>--wheel &lt;file&gt;</c>
    /// (file-manager menus, a desktop shortcut) and from the dashboard's test area. The dashboard runs the chosen job.
    /// </summary>
    /// <remarks>
    /// Create it on the UI thread after the Avalonia app is initialized and call <see cref="Start"/> once. Settings are
    /// read each time the wheel opens (size, key bindings, hints, template, sounds, reduced motion).
    /// </remarks>
    public sealed class WheelController : IDisposable
    {
        private readonly SettingsService _settings;
        private WheelOverlay? _overlay;
        private DBusService? _bus;
        private bool _started;

        public WheelController(SettingsService settings)
        {
            _settings = settings;
            LinuxWheel.Register();
        }

        /// <summary>The user picked a slice for the dragged files (the one the wheel showed first). Runs on the UI thread.</summary>
        public event Func<IReadOnlyList<string>, WheelItem, Task>? FileActionRequested;

        /// <summary>
        /// Another launch of the app (a second start from the menu, or <c>og-converter</c> without <c>--wheel</c>) asked
        /// this one to come forward: show the dashboard. Runs on the UI thread.
        /// </summary>
        public event Action? ActivateRequested;

        /// <summary>
        /// This process was started with <c>--wheel &lt;file&gt;</c>: <see cref="Start"/> opens that wheel, so the dashboard may
        /// want to stay hidden (tray) instead of showing its window.
        /// </summary>
        public static bool StartedForWheel => DBusService.StartupWheel != null;

        /// <summary>True when this process owns the session-bus name, so the GNOME extension's gestures reach it.</summary>
        public static bool ListeningForTriggers => DBusService.Primary != null;

        /// <summary>Starts listening for wheel triggers (D-Bus). Call once, on the UI thread.</summary>
        public void Start()
        {
            if (_started) return;
            _started = true;

            _bus = DBusService.Primary;
            if (_bus != null)
            {
                _bus.KeyTapped += OnBusKeyTapped;
                _bus.KeyReleased += OnBusKeyReleased;
                _bus.ButtonReleased += OnBusButtonReleased;
                _bus.ShowWheelRequested += OnBusShowWheel;
                _bus.ActivateRequested += OnBusActivate;
            }

            if (DBusService.StartupWheel is { } wheel)
            {
                // Let the app finish starting up (and the dashboard decide about its window) first
                Dispatcher.UIThread.Post(() => ShowFor(wheel.Path, wheel.Mode), DispatcherPriority.Background);
            }
        }

        /// <summary>Opens the wheel for a known file, centred on the screen or at the cursor when it's known.</summary>
        public void ShowFor(string path, WheelMode mode)
        {
            var overlay = Overlay();
            ApplySettings(overlay);
            if (overlay.IsOpen) overlay.Dismiss();

            // The extension knows where the pointer is; without it the wheel opens in the middle of the screen
            if (_bus != null)
            {
                _ = ShowAtPointerAsync(overlay, path, mode);
            }
            else
            {
                overlay.ShowAt(null, mode, path, dragMode: false);
            }
        }

        private async Task ShowAtPointerAsync(WheelOverlay overlay, string path, WheelMode mode)
        {
            var pointer = _bus != null ? await _bus.GetPointerAsync() : null;
            Point? at = pointer is { } p ? new Point(p.X, p.Y) : null;
            overlay.ShowAt(at, mode, path, dragMode: false);
        }

        /// <summary>The wheel bound to a key the extension reports ("Shift", "Control", "Alt", "Super"), or null.</summary>
        public WheelMode? ModeForKey(string key)
        {
            var s = _settings.Current;
            // The settings call the Super key "Win", as on Windows
            string setting = key.Equals("Super", StringComparison.OrdinalIgnoreCase) ? "Win" : key;
            if (setting.Equals(s.ConvertKey, StringComparison.OrdinalIgnoreCase)) return WheelMode.Convert;
            if (setting.Equals(s.ToolsKey, StringComparison.OrdinalIgnoreCase)) return WheelMode.Tools;
            if (setting.Equals(s.ActionsKey, StringComparison.OrdinalIgnoreCase)) return WheelMode.Actions;
            return null;
        }

        // ------------------------------------------------------------------ triggers (D-Bus thread → UI thread)

        private void OnBusKeyTapped(string key, int x, int y) => Dispatcher.UIThread.Post(() => OnKeyTapped(key, x, y));
        private void OnBusKeyReleased(string key) => Dispatcher.UIThread.Post(() => OnKeyReleased(key));
        private void OnBusButtonReleased(int x, int y) => Dispatcher.UIThread.Post(() => _overlay?.HandleButtonReleased());
        private void OnBusShowWheel(string path, WheelMode mode) => Dispatcher.UIThread.Post(() => ShowFor(path, mode));
        private void OnBusActivate() => Dispatcher.UIThread.Post(() => ActivateRequested?.Invoke());

        private void OnKeyTapped(string key, int x, int y)
        {
            if (ModeForKey(key) is not { } mode) return;
            var overlay = Overlay();
            if (overlay.IsOpen)
            {
                // A second key during the same drag switches wheels
                if (overlay.CurrentMode != mode) overlay.SwitchMode(mode);
                return;
            }
            ApplySettings(overlay);
            overlay.ShowAt(new Point(x, y), mode, null, dragMode: true);
        }

        private void OnKeyReleased(string key)
        {
            // Hold-to-use mode: letting go of the active wheel's key cancels it
            if (_overlay is { IsDragMode: true } overlay && _settings.Current.CloseWheelOnKeyRelease && ModeForKey(key) == overlay.CurrentMode)
            {
                overlay.Dismiss();
            }
        }

        // ------------------------------------------------------------------ overlay

        private WheelOverlay Overlay()
        {
            if (_overlay != null) return _overlay;
            var overlay = new WheelOverlay();
            overlay.FileActionRequested += OnFileActionRequested;
            overlay.Closed += (_, _) =>
            {
                // Closed by the window manager or app shutdown: a fresh one is made next time
                if (ReferenceEquals(_overlay, overlay)) _overlay = null;
            };
            _overlay = overlay;
            return overlay;
        }

        private void ApplySettings(WheelOverlay overlay)
        {
            var s = _settings.Current;
            TemplateManager.Instance.Apply(s.WheelTemplateId);
            RadialWheelCanvas.ReduceMotion = s.ReduceMotion;
            SoundService.Instance.IsEnabled = s.SoundFeedbackEnabled;
            overlay.UpdateKeyLabels(s.ConvertKey, s.ToolsKey, s.ActionsKey);
            overlay.SetHoldToUse(s.CloseWheelOnKeyRelease);
            overlay.ApplyScale(s.WheelScale, s.ShowKeyHints);
        }

        private async void OnFileActionRequested(IReadOnlyList<string> paths, WheelItem item)
        {
            if (FileActionRequested is not { } handler) return;
            try
            {
                await handler(paths, item);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[OG Converter] {item.Title} on {paths[0]} failed: {ex}");
            }
        }

        public void Dispose()
        {
            if (_bus != null)
            {
                _bus.KeyTapped -= OnBusKeyTapped;
                _bus.KeyReleased -= OnBusKeyReleased;
                _bus.ButtonReleased -= OnBusButtonReleased;
                _bus.ShowWheelRequested -= OnBusShowWheel;
                _bus.ActivateRequested -= OnBusActivate;
                _bus = null;
            }
            var overlay = _overlay;
            _overlay = null;
            overlay?.Close();
        }
    }
}
