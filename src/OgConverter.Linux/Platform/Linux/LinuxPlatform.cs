using OgConverter.Tests;

namespace OgConverter.Platform.Linux
{
    /// <summary>Plugs the Linux implementations into <see cref="Services.Platform"/>.</summary>
    public static class LinuxPlatform
    {
        /// <summary>Linux-only self-test sections; empty ones are skipped.</summary>
        public static PlatformSelfTests SelfTests { get; } = new();

        /// <summary>Call first thing at startup, before any service, the UI or a command-line mode runs.</summary>
        public static void Register()
        {
            Services.Platform.AppVersion = typeof(LinuxPlatform).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            Services.Platform.UpdateAssetSuffix = "-linux-x64.AppImage";
        }
    }
}
