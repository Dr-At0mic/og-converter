using OgConverter.Services;
using OgConverter.Wheel;

namespace OgConverter.Platform.Windows
{
    /// <summary>Plugs the Windows implementations into <see cref="Services.Platform"/>.</summary>
    public static class WindowsPlatform
    {
        /// <summary>Call first thing at startup, before any service, the UI or a command-line mode runs.</summary>
        public static void Register()
        {
            Services.Platform.Images = new WpfImageBackend();
            Services.Platform.Desktop = new WindowsDesktopServices();
            Services.Platform.Ocr = new WindowsOcrEngine();
            Services.Platform.Pdf = new WindowsPdfRasterizer();
            Services.Platform.Office = new WindowsOfficeAutomation();
            Services.Platform.Packages = new WingetInstaller();
            Services.Platform.WheelRenderers = WheelRendererHost.Instance;
            Services.Platform.Ui = new WpfUiDispatcher();
            Services.Platform.SystemInfo = new WindowsSystemInfo();
            Services.Platform.AppVersion = InstallService.CurrentVersion;
            Services.Platform.IsSingleFileBuild = InstallService.IsSingleFile;
            Services.Platform.UpdateAssetSuffix = "-win-x64.exe";
        }
    }
}
