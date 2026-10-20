using System;
using System.Threading.Tasks;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>
    /// Opens the radial wheel: from the GNOME Shell extension's key taps during a drag, from <c>--wheel &lt;file&gt;</c>
    /// (file-manager menus, a desktop shortcut) and from the dashboard's test area. The dashboard runs the chosen job.
    /// </summary>
    /// <remarks>TODO(merge): stub with the wheel branch's public API; take the wheel branch's file.</remarks>
    public sealed class WheelController : IDisposable
    {
        private readonly SettingsService _settings;

        public WheelController(SettingsService settings)
        {
            _settings = settings;
        }

        /// <summary>The user picked a slice for a file. Runs on the UI thread.</summary>
#pragma warning disable CS0067 // raised by the wheel branch's implementation
        public event Func<string, WheelItem, Task>? FileActionRequested;

        /// <summary>A second launch asked this one to come forward (open the dashboard).</summary>
        public event Action? ActivateRequested;
#pragma warning restore CS0067

        /// <summary>This process was started by <c>--wheel</c> (a file-manager menu), so it shows a wheel instead of the dashboard.</summary>
        public static bool StartedForWheel => false;

        /// <summary>The app owns its D-Bus name, so the GNOME helper's key taps reach it.</summary>
        public static bool ListeningForTriggers => false;

        /// <summary>Starts listening for wheel triggers (D-Bus). Call once, on the UI thread.</summary>
        public void Start() { }

        /// <summary>Opens the wheel for a known file, centred on the screen or at the cursor when it's known.</summary>
        public void ShowFor(string path, WheelMode mode) { }

        public void Dispose() { }
    }
}
