using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OgConverter.Services;

namespace OgConverter.Platform.Windows
{
    /// <summary>Installs the optional helpers with winget, the package manager that comes with App Installer.</summary>
    public sealed class WingetInstaller : IPackageInstaller
    {
        /// <summary>The App Installer alias. Missing on some LTSC / Server / older Windows 10 setups.</summary>
        private static string? Winget
        {
            get
            {
                string alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
                return File.Exists(alias) ? alias : null;
            }
        }

        public bool IsAvailable => Winget != null;

        public string UnavailableHint => "winget isn't available on this PC.";

        /// <summary>
        /// Runs winget for <paramref name="d"/>. LibreOffice installs for all users, so Windows shows its admin prompt.
        /// Returns winget's exit code and its last line of output; callers check success by detecting the program again.
        /// </summary>
        public async Task<(int ExitCode, string Output)> InstallAsync(Dependency d)
        {
            string winget = Winget ?? throw new InvalidOperationException(UnavailableHint);
            var psi = new ProcessStartInfo(winget)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (string arg in new[] { "install", "--id", DependencyService.WingetId(d), "--exact", "--silent", "--source", "winget",
                                           "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" })
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start winget.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Progress bars are redrawn with \r; keep the last real line for error messages
            string last = (await stdout + "\n" + await stderr)
                .Split('\n', '\r')
                .Select(line => line.Trim())
                .LastOrDefault(line => line.Length > 0 && line.Any(char.IsLetter)) ?? string.Empty;
            return (process.ExitCode, last);
        }
    }
}
