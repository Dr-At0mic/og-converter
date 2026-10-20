using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OgConverter.Services
{
    /// <summary>Linux package-manager families, from /etc/os-release.</summary>
    public enum LinuxPackageManager
    {
        Apt,
        Dnf,
        Pacman,
        Zypper,
        Unknown
    }

    /// <summary>
    /// The command that installs a helper program, for messages ("sudo apt install ffmpeg"). On Windows the winget
    /// command; on Linux the distribution's own package manager, read from /etc/os-release.
    /// </summary>
    public static class InstallHints
    {
        private static readonly Lazy<LinuxPackageManager> Manager = new(DetectManager);

        /// <summary>The package manager of this Linux distribution (<see cref="LinuxPackageManager.Unknown"/> elsewhere).</summary>
        public static LinuxPackageManager PackageManager => Manager.Value;

        /// <summary>
        /// Package names per tool: apt, dnf, pacman, zypper. A program missing from a distribution's own repositories keeps
        /// its usual name; the user's package manager says where to find it.
        /// </summary>
        private static readonly Dictionary<string, string[]> Packages = new()
        {
            ["ffmpeg"] = new[] { "ffmpeg", "ffmpeg-free", "ffmpeg", "ffmpeg" },
            ["libreoffice"] = new[] { "libreoffice", "libreoffice", "libreoffice-fresh", "libreoffice" },
            ["7zip"] = new[] { "7zip", "7zip", "7zip", "7zip" },
            ["rar"] = new[] { "rar", "rar", "rar", "rar" },
            ["guetzli"] = new[] { "guetzli", "guetzli", "guetzli", "guetzli" },
            ["tesseract"] = new[] { "tesseract-ocr", "tesseract", "tesseract tesseract-data-eng", "tesseract-ocr" },
            ["tesseract-eng"] = new[] { "tesseract-ocr-eng", "tesseract-langpack-eng", "tesseract-data-eng", "tesseract-ocr-traineddata-english" },
            ["chromium"] = new[] { "chromium", "chromium", "chromium", "chromium" },
            ["bsdtar"] = new[] { "libarchive-tools", "bsdtar", "libarchive", "bsdtar" },
            ["wl-clipboard"] = new[] { "wl-clipboard", "wl-clipboard", "wl-clipboard", "wl-clipboard" },
            ["xclip"] = new[] { "xclip", "xclip", "xclip", "xclip" },
            ["poppler"] = new[] { "poppler-utils", "poppler-utils", "poppler", "poppler-tools" },
            ["libheif-examples"] = new[] { "libheif-examples", "libheif-tools", "libheif", "libheif1" }
        };

        /// <summary>
        /// "sudo apt install ffmpeg" for a tool key ("ffmpeg", "libreoffice", "7zip", "rar", "guetzli", "tesseract",
        /// "tesseract-eng", "chromium", "bsdtar", "wl-clipboard", "xclip", "poppler", "libheif-examples"). Linux only.
        /// </summary>
        public static string Command(string tool)
        {
            string[] names = Packages.TryGetValue(tool, out var known) ? known : new[] { tool, tool, tool, tool };
            return PackageManager switch
            {
                LinuxPackageManager.Dnf => "sudo dnf install " + names[1],
                LinuxPackageManager.Pacman => "sudo pacman -S " + names[2],
                LinuxPackageManager.Zypper => "sudo zypper install " + names[3],
                _ => "sudo apt install " + names[0]
            };
        }

        /// <summary>The install command for one of the optional helpers: winget on Windows, the package manager elsewhere.</summary>
        public static string Command(Dependency d) => OperatingSystem.IsWindows()
            ? "winget install " + DependencyService.WingetId(d)
            : Command(d switch
            {
                Dependency.FFmpeg => "ffmpeg",
                Dependency.SevenZip => "7zip",
                Dependency.WinRar => "rar",
                Dependency.Guetzli => "guetzli",
                _ => "libreoffice"
            });

        private static LinuxPackageManager DetectManager()
        {
            if (!OperatingSystem.IsLinux()) return LinuxPackageManager.Unknown;
            try
            {
                foreach (string file in new[] { "/etc/os-release", "/usr/lib/os-release" })
                {
                    if (!File.Exists(file)) continue;
                    // ID=fedora, ID_LIKE="rhel centos fedora": the family decides the package manager
                    var ids = File.ReadLines(file)
                        .Where(l => l.StartsWith("ID=", StringComparison.Ordinal) || l.StartsWith("ID_LIKE=", StringComparison.Ordinal))
                        .SelectMany(l => l[(l.IndexOf('=') + 1)..].Trim('"', '\'').Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        .ToList();
                    if (ids.Any(i => i is "debian" or "ubuntu")) return LinuxPackageManager.Apt;
                    if (ids.Any(i => i is "fedora" or "rhel" or "centos")) return LinuxPackageManager.Dnf;
                    if (ids.Any(i => i is "arch" or "manjaro")) return LinuxPackageManager.Pacman;
                    if (ids.Any(i => i.StartsWith("opensuse", StringComparison.Ordinal) || i is "suse" or "sles")) return LinuxPackageManager.Zypper;
                    return LinuxPackageManager.Unknown;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            return LinuxPackageManager.Unknown;
        }
    }
}
