using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OgConverter.Services
{
    /// <summary>A helper program the app can use, as Settings › Engine lists it.</summary>
    public enum EngineTool
    {
        FFmpeg,
        LibreOffice,
        SevenZip,
        Rar,
        Browser,
        Tesseract,
        Guetzli
    }

    /// <summary>
    /// The command that installs a helper with this distribution's package manager. OG Converter never runs it (that needs
    /// root); Settings › Engine shows it with a copy button.
    /// </summary>
    public static class PackageHints
    {
        private enum Family { Debian, Fedora, Arch, Suse, Unknown }

        private static readonly Lazy<Family> Distro = new(Detect);

        /// <summary>From /etc/os-release (ID and ID_LIKE).</summary>
        private static Family Detect()
        {
            try
            {
                if (!File.Exists("/etc/os-release")) return Family.Unknown;
                string ids = string.Join(' ', File.ReadLines("/etc/os-release")
                    .Where(l => l.StartsWith("ID=", StringComparison.Ordinal) || l.StartsWith("ID_LIKE=", StringComparison.Ordinal))
                    .Select(l => l.Split('=', 2)[1].Trim('"').ToLowerInvariant()));
                if (ids.Contains("debian") || ids.Contains("ubuntu")) return Family.Debian;
                if (ids.Contains("fedora") || ids.Contains("rhel") || ids.Contains("centos")) return Family.Fedora;
                if (ids.Contains("arch")) return Family.Arch;
                if (ids.Contains("suse")) return Family.Suse;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return Family.Unknown;
        }

        /// <summary>"sudo apt install ffmpeg", or null when the distribution isn't known.</summary>
        public static string? InstallCommand(EngineTool tool)
        {
            var family = Distro.Value;
            string? package = (family, tool) switch
            {
                (_, EngineTool.FFmpeg) => "ffmpeg",
                (_, EngineTool.LibreOffice) => "libreoffice",
                (Family.Debian, EngineTool.SevenZip) => "7zip",
                (Family.Fedora, EngineTool.SevenZip) => "7zip",
                (Family.Arch, EngineTool.SevenZip) => "7zip",
                (Family.Suse, EngineTool.SevenZip) => "7zip",
                (_, EngineTool.Rar) => "rar",
                (Family.Debian, EngineTool.Browser) => "chromium",
                (Family.Fedora, EngineTool.Browser) => "chromium",
                (Family.Arch, EngineTool.Browser) => "chromium",
                (Family.Suse, EngineTool.Browser) => "chromium",
                (Family.Debian, EngineTool.Tesseract) => "tesseract-ocr",
                (Family.Arch, EngineTool.Tesseract) => "tesseract tesseract-data-eng",
                (_, EngineTool.Tesseract) => "tesseract",
                (_, EngineTool.Guetzli) => "guetzli",
                _ => null
            };
            if (package == null) return null;

            // Ubuntu ships Chromium as a snap
            if (tool == EngineTool.Browser && family == Family.Debian && IsUbuntu) return "sudo snap install chromium";

            return family switch
            {
                Family.Debian => $"sudo apt install {package}",
                Family.Fedora => $"sudo dnf install {package}",
                Family.Arch => $"sudo pacman -S {package}",
                Family.Suse => $"sudo zypper install {package}",
                _ => null
            };
        }

        /// <summary>The Settings › Engine entry for a helper Core asks for.</summary>
        public static EngineTool ToolFor(Dependency dependency) => dependency switch
        {
            Dependency.FFmpeg => EngineTool.FFmpeg,
            Dependency.SevenZip => EngineTool.SevenZip,
            Dependency.WinRar => EngineTool.Rar,
            Dependency.Guetzli => EngineTool.Guetzli,
            _ => EngineTool.LibreOffice
        };

        /// <summary>The helper's name on Linux (WinRAR's command-line tool is just "rar" here).</summary>
        public static string DisplayName(Dependency dependency) =>
            dependency == Dependency.WinRar ? "rar" : DependencyService.DisplayName(dependency);

        /// <summary>
        /// One command for several helpers ("sudo apt install ffmpeg libreoffice"); helpers from another source (the Chromium
        /// snap) go on their own line. Null when none has a known command.
        /// </summary>
        public static string? InstallCommand(IEnumerable<EngineTool> tools)
        {
            var lines = new List<string>();
            foreach (var tool in tools)
            {
                if (InstallCommand(tool) is not { } command) continue;
                int split = command.LastIndexOf(" install ", StringComparison.Ordinal) is var i and >= 0 ? i + " install ".Length
                          : command.LastIndexOf(" -S ", StringComparison.Ordinal) is var j and >= 0 ? j + " -S ".Length : -1;
                int same = split < 0 ? -1 : lines.FindIndex(l => l.StartsWith(command[..split], StringComparison.Ordinal));
                if (same >= 0) lines[same] += " " + command[split..];
                else lines.Add(command);
            }
            return lines.Count > 0 ? string.Join('\n', lines) : null;
        }

        private static bool IsUbuntu
        {
            get
            {
                try { return File.Exists("/etc/os-release") && File.ReadAllText("/etc/os-release").Contains("ubuntu", StringComparison.OrdinalIgnoreCase); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
            }
        }
    }
}
