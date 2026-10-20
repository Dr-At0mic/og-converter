using System;
using System.Threading.Tasks;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>
    /// Opens the radial wheel: from the GNOME Shell extension's key taps during a drag, from <c>--wheel &lt;file&gt;</c>
    /// (file-manager menus, a desktop shortcut) and from the dashboard's test area. The dashboard runs the chosen job.
    /// </summary>
    public sealed class WheelController : IDisposable
    {
        private readonly SettingsService _settings;

        public WheelController(SettingsService settings)
        {
            _settings = settings;
        }

        /// <summary>The user picked a slice for a file. Runs on the UI thread.</summary>
        public event Func<string, WheelItem, Task>? FileActionRequested;

        /// <summary>Starts listening for wheel triggers (D-Bus). Call once, on the UI thread.</summary>
        public void Start() { }

        /// <summary>Opens the wheel for a known file, centred on the screen or at the cursor when it's known.</summary>
        public void ShowFor(string path, WheelMode mode) { }

        public void Dispose() { }
    }
}
