using System;
using OgConverter.Tests;

namespace OgConverter.Platform.Linux
{
    /// <summary>Plugs the Linux implementations into <see cref="Services.Platform"/>.</summary>
    public static class LinuxPlatform
    {
        /// <summary>Linux-only self-test checks (clipboard, HEIC test files); empty ones are skipped.</summary>
        public static PlatformSelfTests SelfTests { get; } = LinuxSelfTests.Create();

        /// <summary>Call first thing at startup, before any service, the UI or a command-line mode runs.</summary>
        public static void Register()
        {
            Services.Platform.Images = new SkiaImageBackend();
            Services.Platform.Desktop = new LinuxDesktopServices();
            Services.Platform.Ocr = new TesseractOcrEngine();
            Services.Platform.Pdf = new PdfiumRasterizer();
            // No Microsoft Office on Linux: documents go through LibreOffice (DocumentEngine finds it itself)
            Services.Platform.Office = null;
            Services.Platform.Packages = new LinuxPackageInstaller();
            Services.Platform.Ui = new AvaloniaUiDispatcher();
            Services.Platform.AppVersion = typeof(LinuxPlatform).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            // An AppImage is one file that can replace itself; APPIMAGE is set by its runtime
            Services.Platform.IsSingleFileBuild = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"));
            Services.Platform.UpdateAssetSuffix = "-linux-x64.AppImage";
            Services.PdfFonts.EnsureConfigured();
        }
    }
}
