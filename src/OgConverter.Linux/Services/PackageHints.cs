using System;
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
        Tesseract
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
