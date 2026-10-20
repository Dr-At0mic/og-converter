using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace OgConverter.Services
{
    /// <summary>
    /// Reads file and folder paths from WPF drag-and-drop data. Explorer provides a real file list; editors such as
    /// VS Code, Visual Studio and JetBrains IDEs drag their file tree items as file:// URLs or plain path text instead
    /// (understood by <see cref="DroppedPaths"/>).
    /// </summary>
    public static class DroppedFiles
    {
        public static List<string> GetPaths(IDataObject? data)
        {
            var paths = new List<string>();
            if (data == null) return paths;

            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    foreach (string f in files) DroppedPaths.Add(f, paths);
                    if (paths.Count > 0) return paths;
                }

                foreach (string format in new[] { "text/uri-list", "UniformResourceLocatorW", "UniformResourceLocator", DataFormats.UnicodeText, DataFormats.Text })
                {
                    if (!data.GetDataPresent(format)) continue;
                    string? text = data.GetData(format) switch
                    {
                        string s => s,
                        MemoryStream ms => ReadText(ms, format),
                        _ => null
                    };
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    DroppedPaths.AddLines(text, paths);
                    if (paths.Count > 0) return paths;
                }
            }
            catch
            {
                // Some drag sources throw for formats they advertise but can't render
            }

            return paths;
        }

        public static bool HasPaths(IDataObject? data) => GetPaths(data).Count > 0;

        private static string ReadText(MemoryStream ms, string format)
        {
            byte[] bytes = ms.ToArray();
            return format.EndsWith("W", StringComparison.Ordinal)
                ? System.Text.Encoding.Unicode.GetString(bytes)
                : System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}
