using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace OgConverter.Services
{
    /// <summary>
    /// Reads file and folder paths from drag-and-drop data. Explorer provides a real file list; editors such as
    /// VS Code, Visual Studio and JetBrains IDEs drag their file tree items as file:// URLs or plain path text instead.
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
                    foreach (string f in files) Add(f, paths);
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

                    foreach (string line in text.Split(new[] { '\r', '\n', '\0' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        Add(ToLocalPath(line), paths);
                    }
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

        /// <summary>"file:///e%3A/dir/a%20b.png", "E:\dir\a b.png" or a quoted path → a local path, else null.</summary>
        private static string? ToLocalPath(string line)
        {
            string s = line.Trim().Trim('"', '\'');
            if (s.Length == 0 || s.StartsWith('#')) return null; // uri-list comment

            if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                // VS Code escapes the drive colon (file:///e%3A/...), which Uri doesn't read as a drive
                s = System.Text.RegularExpressions.Regex.Replace(s, @"^file:///([a-zA-Z])%3[aA]", "file:///$1:");
                return Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : null;
            }
            return Path.IsPathFullyQualified(s) ? s : null;
        }

        private static string ReadText(MemoryStream ms, string format)
        {
            byte[] bytes = ms.ToArray();
            return format.EndsWith("W", StringComparison.Ordinal)
                ? System.Text.Encoding.Unicode.GetString(bytes)
                : System.Text.Encoding.UTF8.GetString(bytes);
        }

        private static void Add(string? path, List<string> paths)
        {
            if (string.IsNullOrEmpty(path)) return;
            path = path.TrimEnd('\\', '/');
            if (path.EndsWith(':')) path += "\\"; // drive root
            if ((File.Exists(path) || Directory.Exists(path)) && !paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
        }
    }
}
