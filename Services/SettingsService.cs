using System;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>Loads and saves settings.json. Create and use it on the UI thread.</summary>
    public class SettingsService
    {
        private readonly string _settingsPath;
        private AppSettings _currentSettings;
        private readonly DispatcherTimer _saveTimer;
        private bool _dirty;

        public SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "OGConverter");
            Directory.CreateDirectory(folder);
            _settingsPath = Path.Combine(folder, "settings.json");
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (_, _) => Flush();
            _currentSettings = LoadSettings();
        }

        public AppSettings Current => _currentSettings;

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        if (settings.EnsureUniqueKeyBindings()) SaveSettings(settings);
                        return settings;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }

            var def = new AppSettings();
            SaveSettings(def);
            return def;
        }

        /// <summary>
        /// Saves shortly after the last change, so dragging a slider writes the file once instead of on every tick.
        /// Call <see cref="Flush"/> before exiting.
        /// </summary>
        public void SaveSettings(AppSettings settings)
        {
            _currentSettings = settings;
            _dirty = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Writes pending changes now.</summary>
        public void Flush()
        {
            _saveTimer.Stop();
            if (!_dirty) return;
            _dirty = false;

            try
            {
                string json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions { WriteIndented = true });
                // Write-then-swap so a crash mid-write can't leave a truncated settings file
                string tmp = _settingsPath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _settingsPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
