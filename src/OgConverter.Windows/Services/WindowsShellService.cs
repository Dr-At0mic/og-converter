using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace OgConverter.Services
{
    /// <summary>Small Windows integrations: the "downloaded from the internet" block, the desktop wallpaper and folder icons.</summary>
    public static class WindowsShellService
    {
        // Browsers, mail and chat apps tag downloads with this alternate data stream ("Mark of the Web").
        // It's what makes Windows show "This file came from another computer", open Office files in Protected View
        // and block macros, and SmartScreen warn before running a program.
        private const string ZoneStream = ":Zone.Identifier";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFileW(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributesW(string path);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfoW(uint action, uint param, string value, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFOLDERCUSTOMSETTINGS
        {
            public uint dwSize;
            public uint dwMask;
            public IntPtr pvid;
            public IntPtr pszWebViewTemplate;
            public uint cchWebViewTemplate;
            public IntPtr pszWebViewTemplateVersion;
            public IntPtr pszInfoTip;
            public uint cchInfoTip;
            public IntPtr pclsid;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.LPWStr)] public string? pszIconFile;
            public uint cchIconFile;
            public int iIconIndex;
            public IntPtr pszLogo;
            public uint cchLogo;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetSetFolderCustomSettings(ref SHFOLDERCUSTOMSETTINGS settings, string path, uint readWrite);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetPrivateProfileStringW(string section, string key, string defaultValue, char[] buffer, uint size, string file);

        private const uint FCSM_ICONFILE = 0x10;
        private const uint FCS_FORCEWRITE = 0x02;
        private const int SHCNE_UPDATEDIR = 0x1000;
        private const int SHCNE_UPDATEITEM = 0x2000;
        private const uint SHCNF_PATHW = 0x0005;

        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        public static bool IsBlocked(string path) =>
            File.Exists(path) && GetFileAttributesW(path + ZoneStream) != uint.MaxValue;

        /// <summary>Same as ticking "Unblock" in the file's Properties.</summary>
        public static void Unblock(string path)
        {
            if (!IsBlocked(path)) return;
            if (!DeleteFileW(path + ZoneStream))
            {
                throw new InvalidOperationException("Couldn't unblock the file: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
        }

        /// <summary>
        /// Sets the desktop background. Windows reads the file whenever it redraws the desktop, so pass a copy that stays put
        /// (JPG, PNG or BMP).
        /// </summary>
        public static void SetWallpaper(string imagePath)
        {
            if (!SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            {
                throw new InvalidOperationException("Windows didn't accept the wallpaper: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
        }

        /// <summary>
        /// Same as Properties › Customize › Change Icon: Windows writes the folder's desktop.ini and marks the folder so
        /// Explorer reads it. The icon is named relative to the folder, so it still shows after the folder moves.
        /// <paramref name="iconFile"/> null goes back to the default icon.
        /// </summary>
        public static void SetFolderIcon(string folder, string? iconFile)
        {
            var settings = new SHFOLDERCUSTOMSETTINGS
            {
                dwSize = (uint)Marshal.SizeOf<SHFOLDERCUSTOMSETTINGS>(),
                dwMask = FCSM_ICONFILE,
                pszIconFile = iconFile == null ? null : Path.GetRelativePath(folder, iconFile),
            };
            int hr = SHGetSetFolderCustomSettings(ref settings, folder, FCS_FORCEWRITE);
            if (hr < 0) throw new InvalidOperationException("Windows didn't accept the folder icon: " + new Win32Exception(hr).Message);

            // Out of sight like desktop.ini itself
            if (iconFile != null) File.SetAttributes(iconFile, FileAttributes.Hidden | FileAttributes.System);
            SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, folder, IntPtr.Zero);
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, folder, IntPtr.Zero);
        }

        /// <summary>The icon file named in the folder's desktop.ini (made absolute), or null for the default icon.</summary>
        public static string? GetFolderIcon(string folder)
        {
            string ini = Path.Combine(folder, "desktop.ini");
            if (!File.Exists(ini)) return null;

            var buffer = new char[1024];
            foreach (string key in new[] { "IconResource", "IconFile" })
            {
                uint n = GetPrivateProfileStringW(".ShellClassInfo", key, "", buffer, (uint)buffer.Length, ini);
                if (n == 0) continue;
                // "IconResource=icons.dll,-12": the file, then the icon's index
                string value = new string(buffer, 0, (int)n);
                int comma = value.LastIndexOf(',');
                if (comma > 0 && int.TryParse(value[(comma + 1)..], out _)) value = value[..comma];
                value = Environment.ExpandEnvironmentVariables(value.Trim());
                return Path.GetFullPath(Path.Combine(folder, value));
            }
            return null;
        }
    }
}
