using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OgConverter.Services
{
    /// <summary>
    /// Per-user install (no admin): copies the app to %LocalAppData%\Programs\OG Converter, adds a Start menu
    /// shortcut and an Apps &amp; features entry, and manages the "start with Windows" Run entry.
    /// </summary>
    public static class InstallService
    {
        public const string AppName = "OG Converter";
        public const string BackgroundArg = "--background";
        public const string UninstallArg = "--uninstall";
        /// <summary>Passed to the installed copy on its first start, so it can offer the recommended extras.</summary>
        public const string JustInstalledArg = "--just-installed";
        private const string ExeName = "og-converter.exe";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "OGConverter";
        private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OGConverter";

        public static string InstallDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

        public static string InstalledExePath => Path.Combine(InstallDirectory, ExeName);

        private static string StartMenuShortcut => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

        private static string DesktopShortcut => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

        public static string? CurrentExePath => Environment.ProcessPath;

        public static bool IsInstalled => File.Exists(InstalledExePath);

        /// <summary>True when this process is the installed copy (not a portable one from Downloads, a build folder…).</summary>
        public static bool IsRunningInstalled =>
            CurrentExePath != null && PathEquals(Path.GetDirectoryName(CurrentExePath), InstallDirectory);

        /// <summary>Release builds are one self-contained exe; dev builds are an exe plus its DLLs.</summary>
        internal static bool IsSingleFile => !File.Exists(Path.Combine(AppContext.BaseDirectory, "og-converter.dll"));

        public static string CurrentVersion =>
            typeof(InstallService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
            ?? typeof(InstallService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        public static string? InstalledVersion
        {
            get
            {
                try
                {
                    return IsInstalled ? FileVersionInfo.GetVersionInfo(InstalledExePath).ProductVersion?.Split('+')[0] : null;
                }
                catch (FileNotFoundException) { return null; }
            }
        }

        // ------------------------------------------------------------------ start with Windows

        public static bool IsStartWithWindowsEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(RunValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
        }

        /// <summary>Points the Run entry at the installed copy if there is one, otherwise at this exe.</summary>
        public static void SetStartWithWindows(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                string exe = IsInstalled ? InstalledExePath : CurrentExePath ?? InstalledExePath;
                key.SetValue(RunValueName, $"\"{exe}\" {BackgroundArg}");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }

        // ------------------------------------------------------------------ install / uninstall

        /// <summary>
        /// Copies the running app into the install folder (replacing an older copy), creates shortcuts and the
        /// uninstall entry. Run <see cref="LaunchInstalled"/> afterwards and exit this copy.
        /// </summary>
        public static void Install(bool startWithWindows, bool desktopShortcut)
        {
            string source = CurrentExePath ?? throw new InvalidOperationException("Can't find this program's file");
            if (IsRunningInstalled) throw new InvalidOperationException("This copy is already the installed one");

            StopInstalledInstances();
            Directory.CreateDirectory(InstallDirectory);

            if (IsSingleFile)
            {
                CopyWithRetry(source, InstalledExePath);
            }
            else
            {
                // Framework-dependent build: the exe needs every file beside it
                string from = AppContext.BaseDirectory;
                foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(from, file);
                    string dst = Path.Combine(InstallDirectory, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    CopyWithRetry(file, dst);
                }
                if (!string.Equals(Path.GetFileName(source), ExeName, StringComparison.OrdinalIgnoreCase))
                {
                    CopyWithRetry(source, InstalledExePath);
                }
            }

            CreateShortcut(StartMenuShortcut, InstalledExePath);
            if (desktopShortcut) CreateShortcut(DesktopShortcut, InstalledExePath);
            RegisterUninstallEntry();
            SetStartWithWindows(startWithWindows);
        }

        public static void LaunchInstalled()
        {
            Process.Start(new ProcessStartInfo(InstalledExePath, JustInstalledArg) { UseShellExecute = true, WorkingDirectory = InstallDirectory });
        }

        /// <summary>
        /// Removes shortcuts, registry entries and the install folder. Settings and templates in %AppData% are kept.
        /// When this process is the installed copy, the folder is deleted a moment after it exits.
        /// </summary>
        public static void Uninstall()
        {
            SetStartWithWindows(false);
            TryDelete(StartMenuShortcut);
            TryDelete(DesktopShortcut);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);

            if (!Directory.Exists(InstallDirectory)) return;

            if (IsRunningInstalled)
            {
                // A running exe can't delete itself: a hidden shell waits for us to exit, then removes the folder
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{InstallDirectory}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            else
            {
                StopInstalledInstances();
                Directory.Delete(InstallDirectory, true);
            }
        }

        private static void RegisterUninstallEntry()
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", CurrentVersion);
            key.SetValue("Publisher", "OG Converter");
            key.SetValue("DisplayIcon", $"{InstalledExePath},0");
            key.SetValue("InstallLocation", InstallDirectory);
            key.SetValue("UninstallString", $"\"{InstalledExePath}\" {UninstallArg}");
            key.SetValue("QuietUninstallString", $"\"{InstalledExePath}\" {UninstallArg} --quiet");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            long kb = Directory.EnumerateFiles(InstallDirectory, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024;
            key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, kb), RegistryValueKind.DWord);
        }

        /// <summary>After an in-app update the installed exe is newer than its Apps &amp; features entry.</summary>
        public static void RefreshUninstallEntryVersion()
        {
            if (!IsRunningInstalled) return;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, writable: true);
                if (key != null && key.GetValue("DisplayVersion") as string != CurrentVersion) key.SetValue("DisplayVersion", CurrentVersion);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }

        /// <summary>The installed copy may be running in the tray and holding its exe open.</summary>
        private static void StopInstalledInstances()
        {
            int self = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try
                {
                    if (p.Id == self) continue;
                    if (!PathEquals(Path.GetDirectoryName(p.MainModule?.FileName), InstallDirectory)) continue;
                    p.Kill();
                    p.WaitForExit(2000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                finally { p.Dispose(); }
            }
        }

        private static void CopyWithRetry(string from, string to)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Copy(from, to, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    System.Threading.Thread.Sleep(300); // a just-killed instance can take a moment to release its file
                }
            }
        }

        private static void CreateShortcut(string path, string target)
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object? shell = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                dynamic link = ((dynamic)shell!).CreateShortcut(path);
                link.TargetPath = target;
                link.WorkingDirectory = Path.GetDirectoryName(target);
                link.IconLocation = target + ",0";
                link.Description = "Drag a file, tap a key, convert";
                link.Save();
                Marshal.FinalReleaseComObject(link);
            }
            finally
            {
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        private static bool PathEquals(string? a, string? b) =>
            a != null && b != null &&
            string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}
