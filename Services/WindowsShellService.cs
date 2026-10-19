using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace OgConverter.Services
{
    /// <summary>Small Windows integrations: the "downloaded from the internet" block and the desktop wallpaper.</summary>
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
    }
}
