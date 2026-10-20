using System;
using System.Threading.Tasks;
using OgConverter.Services;

namespace OgConverter.Platform.Linux
{
    /// <summary>
    /// Linux programs come from the distribution's package manager, which needs the user's password: OG Converter
    /// doesn't run it, it shows the command instead (apt, dnf, pacman or zypper, from /etc/os-release).
    /// </summary>
    public sealed class LinuxPackageInstaller : IPackageInstaller
    {
        public bool IsAvailable => false;

        public string UnavailableHint => InstallHints.PackageManager switch
        {
            LinuxPackageManager.Unknown => "Install it with your distribution's package manager.",
            var manager => $"Install it with {manager.ToString().ToLowerInvariant()} in a terminal."
        };

        public Task<(int ExitCode, string Output)> InstallAsync(Dependency dependency) =>
            throw new InvalidOperationException($"Install {DependencyService.DisplayName(dependency)} in a terminal: {InstallHints.Command(dependency)}");
    }
}
