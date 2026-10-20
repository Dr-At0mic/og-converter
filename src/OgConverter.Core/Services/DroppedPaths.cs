using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OgConverter.Services
{
    /// <summary>
    /// Turns what drag sources hand over into existing local paths. File managers provide a real file list; editors such
    /// as VS Code, Visual Studio and JetBrains IDEs drag their file tree items as file:// URLs or plain path text instead.
    /// Reading the toolkit's drag data is the UI's job (Windows: <c>DroppedFiles</c>).
    /// </summary>
    public static class DroppedPaths
    {
        /// <summary>Adds each line of a uri-list or path text that names an existing file or folder.</summary>
        public static void AddLines(string text, List<string> paths)
        {
            foreach (string line in text.Split(new[] { '\r', '\n', '\0' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Add(ToLocalPath(line), paths);
            }
        }

        /// <summary>"file:///e%3A/dir/a%20b.png", "E:\dir\a b.png" or a quoted path → a local path, else null.</summary>
        public static string? ToLocalPath(string line)
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

        /// <summary>Adds <paramref name="path"/> once if it's an existing file or folder.</summary>
        public static void Add(string? path, List<string> paths)
        {
            if (string.IsNullOrEmpty(path)) return;
            path = path.TrimEnd('\\', '/');
            if (path.EndsWith(':')) path += "\\"; // drive root
            if ((File.Exists(path) || Directory.Exists(path)) && !paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
        }
    }
}
