using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OgConverter.Services
{
    public enum FileManagerKind
    {
        /// <summary>GNOME Files: Scripts submenu.</summary>
        Nautilus,
        /// <summary>KDE: service menu.</summary>
        Dolphin,
        /// <summary>Cinnamon: Nemo actions.</summary>
        Nemo
    }

    /// <summary>
    /// "OG Converter › Convert / Tools / Actions" in the file managers' right-click menus. Each entry runs
    /// <c>og-converter --wheel &lt;file&gt; --mode &lt;mode&gt;</c> for every selected file, which opens the wheel for it.
    /// This is the way in on desktops where drag-and-tap isn't available.
    /// </summary>
    public static class FileManagerIntegration
    {
        private static readonly (string Mode, string Label)[] Modes = { ("convert", "Convert"), ("tools", "Tools"), ("actions", "Actions") };

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : Path.Combine(Home, ".local", "share");

        public static string DisplayName(FileManagerKind kind) => kind switch
        {
            FileManagerKind.Nautilus => "Files (Nautilus)",
            FileManagerKind.Dolphin => "Dolphin",
            _ => "Nemo"
        };

        public static string Description(FileManagerKind kind) => kind switch
        {
            FileManagerKind.Nautilus => "Right-click › Scripts › OG Converter › Convert, Tools or Actions",
            FileManagerKind.Dolphin => "Right-click › OG Converter › Convert, Tools or Actions",
            _ => "Right-click › OG Converter: Convert, Tools or Actions"
        };

        /// <summary>The file manager's program is on PATH.</summary>
        public static bool IsFileManagerPresent(FileManagerKind kind) =>
            Platform.FindOnPath(kind switch { FileManagerKind.Nautilus => "nautilus", FileManagerKind.Dolphin => "dolphin", _ => "nemo" }) != null;

        private static string NautilusFolder => Path.Combine(DataHome, "nautilus", "scripts", "OG Converter");
        private static string DolphinFile => Path.Combine(DataHome, "kio", "servicemenus", "og-converter.desktop");
        private static string NemoFolder => Path.Combine(DataHome, "nemo", "actions");
        private static string NemoFile(string mode) => Path.Combine(NemoFolder, $"og-converter-{mode}.nemo_action");
        private static string NemoScript => Path.Combine(NemoFolder, "og-converter-wheel.sh");

        /// <summary>The files this integration writes.</summary>
        private static IEnumerable<string> FilesOf(FileManagerKind kind) => kind switch
        {
            FileManagerKind.Nautilus => Modes.Select(m => Path.Combine(NautilusFolder, m.Label)),
            FileManagerKind.Dolphin => new[] { DolphinFile },
            _ => Modes.Select(m => NemoFile(m.Mode)).Append(NemoScript)
        };

        public static bool IsEnabled(FileManagerKind kind) => FilesOf(kind).Any(File.Exists);

        /// <summary>The program the menus start: the installed copy, else this one.</summary>
        public static string Executable =>
            LinuxInstallService.IsInstalled ? LinuxInstallService.InstalledExecutable
            : LinuxInstallService.CurrentExePath ?? throw new InvalidOperationException("Can't find this program's file.");

        public static void Enable(FileManagerKind kind, string? exe = null)
        {
            exe ??= Executable;
            switch (kind)
            {
                case FileManagerKind.Nautilus:
                    Directory.CreateDirectory(NautilusFolder);
                    foreach (var (mode, label) in Modes)
                    {
                        string script = Path.Combine(NautilusFolder, label);
                        File.WriteAllText(script, NautilusScript(exe, mode, label));
                        LinuxInstallService.MakeExecutable(script);
                    }
                    break;

                case FileManagerKind.Dolphin:
                    Directory.CreateDirectory(Path.GetDirectoryName(DolphinFile)!);
                    File.WriteAllText(DolphinFile, DolphinServiceMenu(exe));
                    // KDE 6 only runs service menus that are executable
                    LinuxInstallService.MakeExecutable(DolphinFile);
                    break;

                case FileManagerKind.Nemo:
                    Directory.CreateDirectory(NemoFolder);
                    File.WriteAllText(NemoScript, NemoHelperScript(exe));
                    LinuxInstallService.MakeExecutable(NemoScript);
                    foreach (var (mode, label) in Modes) File.WriteAllText(NemoFile(mode), NemoAction(mode, label));
                    break;
            }
        }

        public static void Disable(FileManagerKind kind)
        {
            foreach (string file in FilesOf(kind))
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (kind == FileManagerKind.Nautilus)
            {
                try { if (Directory.Exists(NautilusFolder) && !Directory.EnumerateFileSystemEntries(NautilusFolder).Any()) Directory.Delete(NautilusFolder); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        public static void DisableAll()
        {
            foreach (var kind in Enum.GetValues<FileManagerKind>()) Disable(kind);
        }

        /// <summary>After install or update: the enabled menus start <paramref name="exe"/>.</summary>
        public static void RewriteEnabled(string exe)
        {
            foreach (var kind in Enum.GetValues<FileManagerKind>())
            {
                if (IsEnabled(kind)) Enable(kind, exe);
            }
        }

        // ------------------------------------------------------------------ file contents

        /// <summary>Single quotes for sh: 'it'\''s'.</summary>
        private static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

        private static string NautilusScript(string exe, string mode, string label)
        {
            var sb = new StringBuilder();
            sb.Append("#!/bin/sh\n");
            sb.Append($"# OG Converter: opens the {label} wheel for each selected file. Written by OG Converter (Settings › Desktop integration).\n");
            sb.Append($"app={ShQuote(exe)}\n");
            sb.Append("if [ -n \"$NAUTILUS_SCRIPT_SELECTED_FILE_PATHS\" ]; then\n");
            sb.Append("    printf '%s\\n' \"$NAUTILUS_SCRIPT_SELECTED_FILE_PATHS\" | while IFS= read -r f; do\n");
            sb.Append($"        [ -n \"$f\" ] && \"$app\" --wheel \"$f\" --mode {mode}\n");
            sb.Append("    done\n");
            sb.Append("else\n");
            sb.Append("    for f in \"$@\"; do\n");
            sb.Append($"        \"$app\" --wheel \"$(realpath -- \"$f\")\" --mode {mode}\n");
            sb.Append("    done\n");
            sb.Append("fi\n");
            return sb.ToString();
        }

        private static string DolphinServiceMenu(string exe)
        {
            string quoted = LinuxInstallService.QuoteExec(exe);
            var sb = new StringBuilder();
            sb.Append("[Desktop Entry]\n");
            sb.Append("Type=Service\n");
            sb.Append("MimeType=all/allfiles;inode/directory;\n");
            sb.Append("X-KDE-ServiceTypes=KonqPopupMenu/Plugin\n");
            sb.Append("X-KDE-Submenu=OG Converter\n");
            sb.Append($"Icon={LinuxInstallService.AppId}\n");
            sb.Append("Actions=" + string.Join("", Modes.Select(m => m.Mode + ";")) + "\n");
            foreach (var (mode, label) in Modes)
            {
                sb.Append($"\n[Desktop Action {mode}]\n");
                sb.Append($"Name={label}\n");
                sb.Append($"Icon={LinuxInstallService.AppId}\n");
                // %f: Dolphin runs the command once per selected file
                sb.Append($"Exec={quoted} --wheel %f --mode {mode}\n");
            }
            return sb.ToString();
        }

        /// <summary>Nemo passes every selected path at once (%F); this opens the wheel for each.</summary>
        private static string NemoHelperScript(string exe) =>
            "#!/bin/sh\n" +
            "# OG Converter: opens a wheel for each selected file. Written by OG Converter (Settings › Desktop integration).\n" +
            $"app={ShQuote(exe)}\n" +
            "mode=\"$1\"\n" +
            "shift\n" +
            "for f in \"$@\"; do\n" +
            "    \"$app\" --wheel \"$f\" --mode \"$mode\"\n" +
            "done\n";

        private static string NemoAction(string mode, string label)
        {
            var sb = new StringBuilder();
            sb.Append("[Nemo Action]\n");
            sb.Append($"Name=OG Converter: {label}\n");
            sb.Append($"Comment=Open the OG Converter {label} wheel for the selection\n");
            // <script> is relative to the actions folder
            sb.Append($"Exec=<og-converter-wheel.sh {mode} %F>\n");
            sb.Append($"Icon-Name={LinuxInstallService.AppId}\n");
            sb.Append("Selection=notnone\n");
            sb.Append("Extensions=any;dir;\n");
            sb.Append("Quote=double\n");
            return sb.ToString();
        }
    }
}
