using System;
using System.IO;

namespace OgConverter.Services
{
    /// <summary>
    /// The operating-system and UI-toolkit pieces the portable code can't do itself. Each app sets them once at startup,
    /// before anything else runs (the UI, <c>--convert</c>, <c>--test</c>, <c>--snapshot</c>).
    /// </summary>
    public static class Platform
    {
        private static IImageBackend? _images;
        private static IDesktopServices? _desktop;

        /// <summary>Decoding, pixel work and encoding of pictures. Required.</summary>
        public static IImageBackend Images
        {
            get => _images ?? throw new InvalidOperationException("No image backend is registered (Platform.Images).");
            set => _images = value;
        }

        /// <summary>Clipboard, file manager, terminal, wallpaper, downloaded-file blocks, opening links. Required.</summary>
        public static IDesktopServices Desktop
        {
            get => _desktop ?? throw new InvalidOperationException("No desktop integration is registered (Platform.Desktop).");
            set => _desktop = value;
        }

        /// <summary>Text recognition in pictures; null when the system has none.</summary>
        public static IOcrEngine? Ocr { get; set; }

        /// <summary>PDF pages to pictures; null when the system has no PDF renderer.</summary>
        public static IPdfRasterizer? Pdf { get; set; }

        /// <summary>Microsoft Office automation; null where there is no Office (LibreOffice is handled by <see cref="DocumentEngine"/> itself).</summary>
        public static IOfficeAutomation? Office { get; set; }

        /// <summary>Installs helper programs (FFmpeg, LibreOffice, 7-Zip, WinRAR); null when there's no package manager to drive.</summary>
        public static IPackageInstaller? Packages { get; set; }

        /// <summary>Wheel renderers and their plugin assemblies, for <see cref="TemplateManager"/>; null when the app draws no wheels.</summary>
        public static IWheelRendererHost? WheelRenderers { get; set; }

        /// <summary>Timers that tick on the UI thread. Without a UI they tick on the thread pool.</summary>
        public static IUiDispatcher Ui { get; set; } = new ThreadPoolUiDispatcher();

        /// <summary>CPU name, memory and installed-program lookups.</summary>
        public static ISystemInfo SystemInfo { get; set; } = new PortableSystemInfo();

        /// <summary>The app's version ("1.4.1"), shown to GitHub and compared with releases.</summary>
        public static string AppVersion { get; set; } = "1.0.0";

        /// <summary>Release builds are one self-contained file that can replace itself; dev builds are an exe plus its DLLs.</summary>
        public static bool IsSingleFileBuild { get; set; }

        /// <summary>End of the release asset this build updates from ("-win-x64.exe"); null when there's none for this platform.</summary>
        public static string? UpdateAssetSuffix { get; set; }

        /// <summary>
        /// How file paths compare: ignoring case on Windows, exactly elsewhere (on Linux "Photo.png" and "photo.png" are two
        /// different files).
        /// </summary>
        public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>The <see cref="PathComparison"/> rule for sets, dictionaries and sorting.</summary>
        public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        /// <summary>"ffmpeg" → "ffmpeg.exe" on Windows, unchanged elsewhere.</summary>
        public static string ExeName(string tool) => OperatingSystem.IsWindows() ? tool + ".exe" : tool;

        /// <summary>The first of <paramref name="names"/> found in a folder on PATH, or null.</summary>
        public static string? FindOnPath(params string[] names)
        {
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string name in names)
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim().Trim('"'), name);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException) { }
                }
            }
            return null;
        }
    }
}
