using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Tmds.DBus.Protocol;

namespace OgConverter.Services
{
    /// <summary>
    /// Opening files, folders and links from the UI. Uses <see cref="Platform.Desktop"/> where it has the call and falls
    /// back to xdg-open / the FileManager1 D-Bus interface, so the UI works before a desktop backend is registered.
    /// </summary>
    public static class DesktopActions
    {
        /// <summary>Opens a file or folder with its default app.</summary>
        public static void OpenFile(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return;
            Launch("xdg-open", path);
        }

        /// <summary>Opens the file manager with <paramref name="path"/> selected.</summary>
        public static void RevealInFileManager(string path)
        {
            try
            {
                Platform.Desktop.RevealInFileManager(path);
                return;
            }
            catch (InvalidOperationException) { } // no desktop backend registered
            catch (Exception ex) when (ex is Win32Exception or IOException) { }

            _ = RevealWithDbusAsync(path);
        }

        /// <summary>Nautilus, Dolphin, Nemo and Thunar implement org.freedesktop.FileManager1; otherwise open the folder.</summary>
        private static async Task RevealWithDbusAsync(string path)
        {
            var connection = SessionBus.Connection;
            try
            {
                if (connection != null)
                {
                    MessageBuffer message;
                    using (var w = connection.GetMessageWriter())
                    {
                        w.WriteMethodCallHeader("org.freedesktop.FileManager1", "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1", "ShowItems", "ass", MessageFlags.None);
                        w.WriteArray(new[] { new Uri(path).AbsoluteUri });
                        w.WriteString(string.Empty);
                        message = w.CreateMessage();
                    }
                    await connection.CallMethodAsync(message);
                    return;
                }
            }
            catch (Exception ex) when (ex is DBusExceptionBase or InvalidOperationException or UriFormatException) { }

            string? folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (folder != null) Launch("xdg-open", folder);
        }

        /// <summary>Opens a web address in the default browser.</summary>
        public static void OpenUrl(string url)
        {
            try
            {
                Platform.Desktop.OpenUrl(url);
                return;
            }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            Launch("xdg-open", url);
        }

        /// <summary>Puts text on the clipboard (the desktop backend's, else the window's).</summary>
        public static async Task CopyTextAsync(TopLevel? owner, string text)
        {
            try
            {
                Platform.Desktop.SetClipboardText(text);
                return;
            }
            catch (InvalidOperationException) { }
            if (owner?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        }

        private static void Launch(string program, string argument)
        {
            try
            {
                var psi = new ProcessStartInfo(program) { UseShellExecute = false };
                psi.ArgumentList.Add(argument);
                using var _ = Process.Start(psi);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
        }
    }
}
