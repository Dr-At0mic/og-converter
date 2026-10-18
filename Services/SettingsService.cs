using System;
using System.IO;
using System.Text.Json;
using OgConverter.Models;

namespace OgConverter.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private AppSettings _currentSettings;

        public SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "OGConverter");
            Directory.CreateDirectory(folder);
            _settingsPath = Path.Combine(folder, "settings.json");
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
            catch { }

            var def = new AppSettings();
            SaveSettings(def);
            return def;
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                _currentSettings = settings;
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }
    }
}
