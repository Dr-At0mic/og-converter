using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace OgConverter.Services
{
    /// <summary>
    /// Per-user install on Linux, no root needed: copies the running AppImage (or the build folder) to
    /// <c>~/.local/share/og-converter</c>, links it as <c>~/.local/bin/og-converter</c>, adds the app-menu entry and the
    /// hicolor icons, and optionally an autostart entry that starts it in the tray. Settings and templates stay in
    /// <c>~/.config/OGConverter</c> and survive an uninstall. Port of the Windows InstallService.
    /// </summary>
    public static class LinuxInstallService
    {
        public const string AppId = NotificationService.DesktopEntryId;

        /// <summary>Start in the tray (autostart).</summary>
        public const string BackgroundArg = "--background";

        /// <summary>The freshly installed copy was started by Install (shows the extras prompt).</summary>
        public const string JustInstalledArg = "--just-installed";

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : Path.Combine(Home, ".local", "share");

        private static string ConfigHome => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(Home, ".config");

        /// <summary>~/.local/share/og-converter</summary>
        public static string InstallDirectory => Path.Combine(DataHome, "og-converter");

        /// <summary>~/.cache/og-converter (notification icon, scratch files)</summary>
        public static string CacheDirectory => Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } c ? c : Path.Combine(Home, ".cache"), "og-converter");

        public static string BinLink => Path.Combine(Home, ".local", "bin", "og-converter");

        public static string DesktopFile => Path.Combine(DataHome, "applications", AppId + ".desktop");

        public static string AutostartFile => Path.Combine(ConfigHome, "autostart", AppId + ".desktop");

        private static string IconsRoot => Path.Combine(DataHome, "icons", "hicolor");

        private static readonly int[] IconSizes = { 16, 24, 32, 48, 64, 128, 256 };

        /// <summary>The AppImage file when running as one (the mounted binary inside it can't be copied on its own).</summary>
        public static string? AppImagePath =>
            Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } p && File.Exists(p) ? p : null;

        /// <summary>What the menu entry, autostart and file-manager menus start: the AppImage, else this program.</summary>
        public static string? CurrentExePath => AppImagePath ?? Environment.ProcessPath;

        /// <summary>The installed program: the AppImage copy, or the launcher in the copied build folder.</summary>
        public static string InstalledExecutable =>
            File.Exists(Path.Combine(InstallDirectory, "og-converter.AppImage"))
                ? Path.Combine(InstallDirectory, "og-converter.AppImage")
                : Path.Combine(InstallDirectory, "app", "og-converter");

        public static string CurrentVersion => Platform.AppVersion;

        /// <summary>Version written into the installed menu entry, or null.</summary>
        public static string? InstalledVersion
        {
            get
            {
                try
                {
                    if (!File.Exists(DesktopFile)) return null;
                    return File.ReadLines(DesktopFile).FirstOrDefault(l => l.StartsWith("X-OgConverter-Version=", StringComparison.Ordinal))?.Split('=', 2)[1].Trim();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
            }
        }

        public static bool IsInstalled => File.Exists(DesktopFile) && File.Exists(InstalledExecutable);

        /// <summary>This copy is the installed one.</summary>
        public static bool IsRunningInstalled
        {
            get
            {
                string? here = AppImagePath ?? Environment.ProcessPath;
                if (here == null) return false;
                string dir = Path.GetFullPath(InstallDirectory) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(here).StartsWith(dir, StringComparison.Ordinal);
            }
        }

        /// <summary>The hicolor icon is installed (notifications can name it instead of a file).</summary>
        public static bool IsIconInstalled => File.Exists(Path.Combine(IconsRoot, "256x256", "apps", AppId + ".png"));

        public static bool IsStartAtLoginEnabled => File.Exists(AutostartFile);

        /// <summary>
        /// Copies this copy into place and writes the menu entry and icons; with <paramref name="startAtLogin"/> also the
        /// autostart entry. Replaces an older installed copy.
        /// </summary>
        public static void Install(bool startAtLogin, bool desktopShortcut)
        {
            Directory.CreateDirectory(InstallDirectory);
            string exe;
            if (AppImagePath is { } appImage)
            {
                exe = Path.Combine(InstallDirectory, "og-converter.AppImage");
                CopyReplacing(appImage, exe);
                TryDeleteDirectory(Path.Combine(InstallDirectory, "app"));
            }
            else
            {
                // Single-file builds are one file; dev builds are a folder of DLLs: copy the whole folder either way
                string source = AppContext.BaseDirectory;
                string target = Path.Combine(InstallDirectory, "app");
                string staging = target + ".new";
                TryDeleteDirectory(staging);
                CopyDirectory(source, staging);
                TryDeleteDirectory(target);
                Directory.Move(staging, target);
                exe = Path.Combine(target, "og-converter");
                TryDelete(Path.Combine(InstallDirectory, "og-converter.AppImage"));
            }
            MakeExecutable(exe);

            // ~/.local/bin is on PATH on most distributions (systemd's file-hierarchy)
            Directory.CreateDirectory(Path.GetDirectoryName(BinLink)!);
            TryDelete(BinLink);
            File.CreateSymbolicLink(BinLink, exe);

            InstallIcons();
            WriteDesktopEntry(DesktopFile, exe, background: false);
            if (startAtLogin) SetStartAtLogin(true, exe);
            if (desktopShortcut) WriteDesktopShortcut(exe);

            RefreshDesktopDatabase();
            // File-manager menus keep pointing at a working copy
            FileManagerIntegration.RewriteEnabled(exe);
        }

        /// <summary>Removes the installed copy, its menu entry, icons, autostart, desktop shortcut and file-manager menus.</summary>
        public static void Uninstall()
        {
            FileManagerIntegration.DisableAll();
            TryDelete(AutostartFile);
            TryDelete(DesktopFile);
            if (DesktopShortcutPath is { } shortcut) TryDelete(shortcut);
            foreach (int size in IconSizes) TryDelete(Path.Combine(IconsRoot, $"{size}x{size}", "apps", AppId + ".png"));
            if (IsLinkToInstall(BinLink)) TryDelete(BinLink);
            TryDelete(Path.Combine(InstallDirectory, "og-converter.AppImage"));
            TryDeleteDirectory(Path.Combine(InstallDirectory, "app"));
            RefreshDesktopDatabase();
        }

        /// <summary>Adds or removes the autostart entry; it points at the installed copy, else at this one.</summary>
        public static void SetStartAtLogin(bool enabled, string? exe = null)
        {
            if (!enabled)
            {
                TryDelete(AutostartFile);
                return;
            }
            exe ??= IsInstalled ? InstalledExecutable : CurrentExePath ?? throw new InvalidOperationException("Can't find this program's file.");
            WriteDesktopEntry(AutostartFile, exe, background: true);
        }

        /// <summary>Starts the installed copy once this one has exited (a second copy would hand over to this one and quit).</summary>
        public static void LaunchInstalledAfterExit()
        {
            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("sleep 1; exec \"$0\" \"$1\" >/dev/null 2>&1");
            psi.ArgumentList.Add(InstalledExecutable);
            psi.ArgumentList.Add(JustInstalledArg);
            using var _ = Process.Start(psi);
        }

        // ------------------------------------------------------------------ pieces

        private static void WriteDesktopEntry(string path, string exe, bool background)
        {
            var sb = new StringBuilder();
            sb.Append("[Desktop Entry]\n");
            sb.Append("Type=Application\n");
            sb.Append("Name=OG Converter\n");
            sb.Append("GenericName=File Converter\n");
            sb.Append("Comment=Convert, compress and edit files offline with a radial wheel\n");
            sb.Append($"Exec={QuoteExec(exe)}{(background ? " " + BackgroundArg : string.Empty)}\n");
            sb.Append($"Icon={AppId}\n");
            sb.Append("Terminal=false\n");
            sb.Append("Categories=Utility;\n");
            sb.Append("Keywords=convert;compress;image;video;pdf;archive;\n");
            sb.Append("StartupNotify=true\n");
            sb.Append("StartupWMClass=og-converter\n");
            sb.Append($"X-OgConverter-Version={CurrentVersion}\n");
            if (background)
            {
                sb.Append("X-GNOME-Autostart-enabled=true\n");
                sb.Append("X-GNOME-Autostart-Delay=3\n");
                sb.Append("NoDisplay=true\n");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sb.ToString());
        }

        private static string? DesktopShortcutPath
        {
            get
            {
                string? desktop = XdgUserDir("DESKTOP");
                return desktop == null ? null : Path.Combine(desktop, AppId + ".desktop");
            }
        }

        /// <summary>A launcher on the desktop (Ubuntu's desktop icons, KDE, Cinnamon); marked trusted so it runs on double-click.</summary>
        private static void WriteDesktopShortcut(string exe)
        {
            if (DesktopShortcutPath is not { } path || !Directory.Exists(Path.GetDirectoryName(path))) return;
            WriteDesktopEntry(path, exe, background: false);
            MakeExecutable(path);
            RunQuietly("gio", "set", path, "metadata::trusted", "true");
        }

        private static void InstallIcons()
        {
            foreach (int size in IconSizes)
            {
                string target = Path.Combine(IconsRoot, $"{size}x{size}", "apps", AppId + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, AppIcon.Png(size));
            }
        }

        private static void RefreshDesktopDatabase()
        {
            RunQuietly("update-desktop-database", Path.GetDirectoryName(DesktopFile)!);
            RunQuietly("gtk-update-icon-cache", "-f", "-t", IconsRoot);
        }

        /// <summary>Desktop-entry Exec quoting: quote paths with spaces, escape the reserved characters.</summary>
        internal static string QuoteExec(string path)
        {
            bool plain = path.All(ch => char.IsLetterOrDigit(ch) || ch is '/' or '.' or '-' or '_' or '+');
            if (plain) return path;
            var sb = new StringBuilder("\"");
            foreach (char ch in path)
            {
                if (ch is '"' or '`' or '$' or '\\') sb.Append('\\');
                sb.Append(ch == '%' ? "%%" : ch.ToString());
            }
            return sb.Append('"').ToString();
        }

        /// <summary>xdg-user-dir: ~/.config/user-dirs.dirs (XDG_DESKTOP_DIR="$HOME/Desktop").</summary>
        private static string? XdgUserDir(string name)
        {
            try
            {
                string file = Path.Combine(ConfigHome, "user-dirs.dirs");
                if (File.Exists(file))
                {
                    string? line = File.ReadLines(file).FirstOrDefault(l => l.StartsWith($"XDG_{name}_DIR=", StringComparison.Ordinal));
                    if (line != null) return line.Split('=', 2)[1].Trim().Trim('"').Replace("$HOME", Home);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return name == "DESKTOP" ? Path.Combine(Home, "Desktop") : null;
        }

        private static bool IsLinkToInstall(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.LinkTarget is { } target && Path.GetFullPath(target, Path.GetDirectoryName(path)!).StartsWith(InstallDirectory, StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }

        internal static void MakeExecutable(string path)
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        private static void CopyReplacing(string source, string target)
        {
            string staging = target + ".new";
            File.Copy(source, staging, overwrite: true);
            File.Move(staging, target, overwrite: true);
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
            }
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(target, Path.GetRelativePath(source, file));
                File.Copy(file, dest, overwrite: true);
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(dest, File.GetUnixFileMode(file));
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path) || new FileInfo(path).LinkTarget != null) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        /// <summary>Runs a helper and waits briefly; missing helpers are fine.</summary>
        internal static (int ExitCode, string Output) RunQuietly(string program, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p == null) return (-1, string.Empty);
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(10_000))
                {
                    try { p.Kill(); } catch (InvalidOperationException) { }
                    return (-1, string.Empty);
                }
                return (p.ExitCode, stdout.Result + stderr.Result);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return (-1, string.Empty); }
        }
    }
}
