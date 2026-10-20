using System.Threading.Tasks;

namespace OgConverter.Services
{
    /// <summary>The system's package manager (winget on Windows), for the optional helpers in <see cref="Dependency"/>.</summary>
    public interface IPackageInstaller
    {
        /// <summary>False when the package manager is missing; the download page is offered instead.</summary>
        bool IsAvailable { get; }

        /// <summary>Shown when <see cref="IsAvailable"/> is false ("winget isn't available on this PC.").</summary>
        string UnavailableHint { get; }

        /// <summary>
        /// Installs <paramref name="dependency"/>. Returns the installer's exit code and its last line of output; callers check
        /// success by looking for the program again.
        /// </summary>
        Task<(int ExitCode, string Output)> InstallAsync(Dependency dependency);
    }
}
