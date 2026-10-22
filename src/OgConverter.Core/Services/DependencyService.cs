using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace OgConverter.Services
{
    /// <summary>Optional helper programs OG Converter can install for the user.</summary>
    public enum Dependency
    {
        FFmpeg,
        LibreOffice,
        /// <summary>7z.exe: password-protected 7z, every 7z method, multi-core packing.</summary>
        SevenZip,
        /// <summary>Rar.exe from WinRAR (shareware): the only way to create .rar archives.</summary>
        WinRar,
        /// <summary>Google's Guetzli JPEG encoder: one small .exe, downloaded from its GitHub release.</summary>
        Guetzli
    }

    /// <summary>
    /// Installs FFmpeg, LibreOffice, 7-Zip or WinRAR with the system's package manager (<see cref="Platform.Packages"/>:
    /// winget on Windows) when the user asks (first-run prompt, Settings › Engine, the Archive and Protect dialogs), and
    /// downloads Guetzli for Windows, which has no installer. Nothing is bundled or installed silently. Without a package
    /// manager the download page opens instead. Edge is only reported, never installed: it ships with Windows.
    /// </summary>
    public static class DependencyService
    {
        public static string WingetId(Dependency d) => d switch
        {
            Dependency.FFmpeg => "Gyan.FFmpeg",
            Dependency.SevenZip => "7zip.7zip",
            Dependency.WinRar => "RARLab.WinRAR",
            _ => "TheDocumentFoundation.LibreOffice"
        };

        public static string DownloadPage(Dependency d) => d switch
        {
            Dependency.FFmpeg => "https://www.gyan.dev/ffmpeg/builds/",
            Dependency.SevenZip => "https://www.7-zip.org/download.html",
            Dependency.WinRar => "https://www.win-rar.com/download.html",
            Dependency.Guetzli => "https://github.com/google/guetzli/releases",
            _ => "https://www.libreoffice.org/download/download-libreoffice/"
        };

        public static string DisplayName(Dependency d) => d switch
        {
            Dependency.FFmpeg => "FFmpeg",
            Dependency.SevenZip => "7-Zip",
            Dependency.WinRar => "WinRAR",
            Dependency.Guetzli => "Guetzli",
            _ => "LibreOffice"
        };

        // ------------------------------------------------------------------ Archive and image tools

        /// <summary>Where downloaded helpers (Guetzli) live: %LocalAppData%\OGConverter\tools.</summary>
        public static string ToolsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OGConverter", "tools");

        private const string GuetzliUrl = "https://github.com/google/guetzli/releases/download/v1.0.1/guetzli_windows_x86-64.exe";
        // Pinned: the download is discarded unless it is exactly the official v1.0.1 build
        private const string GuetzliSha256 = "bbac41d606c96cc57a94c802e581591647d6e1cfc83a97522509edbd392faeca";

        /// <summary>7z.exe (or 7za.exe), or null. Looked up on every call so a fresh install is picked up without restarting.</summary>
        public static string? SevenZipPath => OperatingSystem.IsWindows()
            ? FindTool(new[] { "7z.exe", "7za.exe" }, @"7-Zip\7z.exe")
            : FindTool(new[] { "7zz", "7z", "7za" }, null);

        /// <summary>WinRAR's console Rar.exe (rar elsewhere), or null.</summary>
        public static string? RarPath => OperatingSystem.IsWindows() ? FindTool(new[] { "Rar.exe" }, @"WinRAR\Rar.exe") : FindTool(new[] { "rar" }, null);

        public static string? GuetzliPath => FindTool(new[] { Platform.ExeName("guetzli") }, null);

        public static bool IsAvailable(Dependency d) => d switch
        {
            Dependency.SevenZip => SevenZipPath != null,
            Dependency.WinRar => RarPath != null,
            Dependency.Guetzli => GuetzliPath != null,
            _ => throw new ArgumentOutOfRangeException(nameof(d), "FFmpeg and LibreOffice are located by their engines.")
        };

        /// <summary>Next to OG Converter, in the tools folder, under Program Files, then on PATH.</summary>
        private static string? FindTool(string[] names, string? programFilesPath)
        {
            foreach (string folder in new[] { AppContext.BaseDirectory, ToolsFolder })
            {
                foreach (string name in names)
                {
                    string candidate = Path.Combine(folder, name);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            if (programFilesPath != null)
            {
                foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                {
                    string candidate = Path.Combine(Environment.GetFolderPath(root), programFilesPath);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string name in names)
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim('"'), name);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException) { }
                }
            }
            return null;
        }

        /// <summary>Guetzli for Windows is a plain download, so it doesn't need winget. Elsewhere it comes from the package manager.</summary>
        public static bool CanInstallDependency(Dependency d) => d == Dependency.Guetzli && OperatingSystem.IsWindows() || CanInstall;

        /// <summary>
        /// Installs one of the tools this service locates itself (7-Zip, WinRAR, Guetzli). Returns null when it's ready
        /// to use, otherwise why not.
        /// </summary>
        public static async Task<string?> InstallToolAsync(Dependency d)
        {
            string name = DisplayName(d);
            if (!CanInstallDependency(d)) return $"{UnavailableHint} Download {name} from {DownloadPage(d)}";
            int exitCode;
            string output;
            try
            {
                (exitCode, output) = await InstallAsync(d);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return $"Couldn't start the installer: {ex.Message}\n\nYou can download {name} from {DownloadPage(d)}";
            }
            // winget's exit code isn't enough ("already installed" is an error to it): success means we can find it now
            if (IsAvailable(d)) return null;
            string detail = string.IsNullOrWhiteSpace(output) ? $"exit code 0x{exitCode:X8}" : output;
            return $"{name} wasn't installed ({detail}). You can download it from {DownloadPage(d)}";
        }

        /// <summary>Downloads the official Guetzli build into the tools folder, keeping it only if its SHA-256 matches.</summary>
        private static async Task<(int ExitCode, string Output)> DownloadGuetzliAsync()
        {
            Directory.CreateDirectory(ToolsFolder);
            string target = Path.Combine(ToolsFolder, Platform.ExeName("guetzli"));
            string partial = target + ".partial";
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("OG-Converter");
                byte[] data = await http.GetByteArrayAsync(GuetzliUrl);
                if (Convert.ToHexStringLower(SHA256.HashData(data)) != GuetzliSha256)
                {
                    return (1, "the downloaded file doesn't match the official Guetzli release, so it was discarded");
                }
                await File.WriteAllBytesAsync(partial, data);
                File.Move(partial, target, overwrite: true);
                return (0, "");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
            {
                try { File.Delete(partial); } catch { }
                return (1, ex.Message);
            }
        }

        /// <summary>False when there's no package manager (winget is missing on some LTSC / Server / older Windows 10 setups).</summary>
        public static bool CanInstall => Platform.Packages?.IsAvailable == true;

        private static string UnavailableHint => Platform.Packages?.UnavailableHint ?? "There's no package manager to install it with.";

        /// <summary>
        /// Installs <paramref name="d"/> with the package manager (on Windows LibreOffice installs for all users, so Windows
        /// shows its admin prompt). Returns the installer's exit code and its last line of output; callers check success by
        /// detecting the program again. Cancelling stops the package manager and throws <see cref="OperationCanceledException"/>.
        /// </summary>
        public static async Task<(int ExitCode, string Output)> InstallAsync(Dependency d, CancellationToken cancellationToken = default)
        {
            if (d == Dependency.Guetzli && OperatingSystem.IsWindows()) return await DownloadGuetzliAsync();
            var installer = Platform.Packages ?? throw new InvalidOperationException(UnavailableHint);
            return await installer.InstallAsync(d, cancellationToken);
        }

        public static void OpenDownloadPage(Dependency d) => Platform.Desktop.OpenUrl(DownloadPage(d));
    }
}
