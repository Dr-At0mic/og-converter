using System;
using System.IO;
using System.Text.Json;

namespace OgConverter.Services
{
    /// <summary>
    /// Linux-only choices that have no place in the shared <c>settings.json</c> (AppSettings is shared with Windows):
    /// kept next to it in <c>~/.config/OGConverter/linux.json</c>.
    /// </summary>
    public sealed class LinuxPreferences
    {
        /// <summary>Start in the tray (no window) even when launched from the app menu.</summary>
        public bool StartHidden { get; set; }

        /// <summary>The "still running in the tray" notification was shown once after the window was first closed.</summary>
        public bool TrayHintShown { get; set; }

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OGConverter", "linux.json");

        public static LinuxPreferences Current { get; } = Load();

        private static LinuxPreferences Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonSerializer.Deserialize<LinuxPreferences>(File.ReadAllText(FilePath)) ?? new();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            return new();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
