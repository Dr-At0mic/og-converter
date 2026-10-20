using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace OgConverter.Services
{
    public enum HelperState
    {
        /// <summary>Not a GNOME session: drag-and-tap isn't available; right-click menus are the way in.</summary>
        NotGnome,
        NotInstalled,
        /// <summary>The files are in place but GNOME Shell hasn't loaded them yet: log out and back in once.</summary>
        NeedsLogin,
        Disabled,
        Active,
        /// <summary>The Shell reported an error or an incompatible version.</summary>
        Error,
        /// <summary>Installed, but older than the copy this app ships.</summary>
        Outdated
    }

    /// <summary>
    /// The GNOME Shell extension that turns "drag a file, tap a key" into a D-Bus call to this app (GNOME on Wayland doesn't
    /// let apps see global keys or the pointer). Ships inside the app (linux/gnome-extension) and installs per user into
    /// <c>~/.local/share/gnome-shell/extensions</c>.
    /// </summary>
    public static class GnomeExtensionService
    {
        public const string Uuid = "og-converter@dr-at0mic.github.io";

        private static readonly string[] Files = { "metadata.json", "extension.js" };

        private static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d
            ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        public static string InstallFolder => Path.Combine(DataHome, "gnome-shell", "extensions", Uuid);

        /// <summary>XDG_CURRENT_DESKTOP lists GNOME (also Ubuntu's "ubuntu:GNOME").</summary>
        public static bool IsGnomeSession =>
            (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty)
                .Split(':').Any(d => d.Equals("GNOME", StringComparison.OrdinalIgnoreCase));

        public static string DesktopName =>
            (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty).Split(':').LastOrDefault(d => d.Length > 0) ?? "this desktop";

        /// <summary>Asks GNOME Shell (gnome-extensions info) and compares the installed files with the bundled ones.</summary>
        public static async Task<HelperState> GetStateAsync()
        {
            if (!IsGnomeSession || Platform.FindOnPath("gnome-extensions") == null) return HelperState.NotGnome;

            bool filesPresent = File.Exists(Path.Combine(InstallFolder, "extension.js"));
            var (exit, output) = await Task.Run(() => LinuxInstallService.RunQuietly("gnome-extensions", "info", Uuid));

            // "State: ACTIVE" / "ENABLED" (older Shells) / "INACTIVE" / "DISABLED" / "ERROR" / "OUT OF DATE" / "INITIALIZED"
            string? state = output.Split('\n').Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("State:", StringComparison.OrdinalIgnoreCase))?.Split(':', 2)[1].Trim().ToUpperInvariant();

            if (exit != 0 || state == null) return filesPresent ? HelperState.NeedsLogin : HelperState.NotInstalled;
            if (filesPresent && !MatchesBundled()) return HelperState.Outdated;
            return state switch
            {
                "ACTIVE" or "ENABLED" => HelperState.Active,
                "ERROR" or "OUT OF DATE" => HelperState.Error,
                // Found by the Shell but not loaded: a new copy waiting for the next login, or turned off
                "INITIALIZED" => HelperState.NeedsLogin,
                _ => HelperState.Disabled
            };
        }

        /// <summary>
        /// Copies the bundled extension into place and enables it. Returns true when GNOME Shell can't load it until the
        /// user logs out and back in (always the case for a first install on Wayland).
        /// </summary>
        public static async Task<bool> InstallAsync()
        {
            Directory.CreateDirectory(InstallFolder);
            foreach (string name in Files)
            {
                using var source = OpenBundled(name) ?? throw new FileNotFoundException($"The bundled extension file {name} is missing.");
                using var target = File.Create(Path.Combine(InstallFolder, name));
                await source.CopyToAsync(target);
            }

            return await Task.Run(() =>
            {
                // User extensions may be switched off globally
                LinuxInstallService.RunQuietly("gsettings", "set", "org.gnome.shell", "disable-user-extensions", "false");
                if (LinuxInstallService.RunQuietly("gnome-extensions", "enable", Uuid).ExitCode == 0)
                {
                    // Enabled now, but a running Shell keeps the module it loaded at login: an update still needs a re-login
                    return LinuxInstallService.RunQuietly("gnome-extensions", "info", Uuid).Output.Contains("ACTIVE", StringComparison.OrdinalIgnoreCase) is false;
                }

                // The Shell hasn't scanned the new folder yet (normal on Wayland): mark it enabled for the next login
                var (exit, current) = LinuxInstallService.RunQuietly("gsettings", "get", "org.gnome.shell", "enabled-extensions");
                if (exit == 0 && !current.Contains($"'{Uuid}'", StringComparison.Ordinal))
                {
                    string list = current.Trim();
                    string updated = list is "@as []" or "[]" ? $"['{Uuid}']" : list.TrimEnd(']') + $", '{Uuid}']";
                    LinuxInstallService.RunQuietly("gsettings", "set", "org.gnome.shell", "enabled-extensions", updated);
                }
                return true;
            });
        }

        /// <summary>The installed files are the ones this build ships.</summary>
        private static bool MatchesBundled()
        {
            try
            {
                foreach (string name in Files)
                {
                    string installed = Path.Combine(InstallFolder, name);
                    if (!File.Exists(installed)) return false;
                    using var bundled = OpenBundled(name);
                    if (bundled == null) return true; // nothing to compare with
                    using var ms = new MemoryStream();
                    bundled.CopyTo(ms);
                    if (!ms.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(installed))) return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
        }

        /// <summary>The extension files are embedded in the app (see the csproj).</summary>
        private static Stream? OpenBundled(string name) =>
            Assembly.GetExecutingAssembly().GetManifestResourceStream("gnome-extension/" + name);
    }
}
