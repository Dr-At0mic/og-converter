using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using OgConverter.Services;

namespace OgConverter.Platform.Windows
{
    /// <summary>Clipboard, Explorer, Windows Terminal, the wallpaper and Mark of the Web.</summary>
    public sealed class WindowsDesktopServices : IDesktopServices
    {
        public void SetClipboardText(string text) => RunOnSta(() => Clipboard.SetText(text));

        public void SetClipboardImageAndFile(string picturePath, string filePath)
        {
            RunOnSta(() =>
            {
                var image = ImageCodec.TryLoad(picturePath, applyOrientation: true, maxDimension: 0)
                            ?? throw new InvalidOperationException("Windows can't open this picture.");
                // The picture for apps that paste images, and the file for apps that paste attachments (chats, mail)
                var data = new DataObject();
                data.SetImage(image.Bitmap.ToBitmapSource());
                data.SetFileDropList(new System.Collections.Specialized.StringCollection { filePath });
                Clipboard.SetDataObject(data, copy: true);
            });
        }

        public void RevealInFileManager(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"");

        /// <summary>
        /// Opens a new terminal window in the given directory. Prefers Windows Terminal, falls back to PowerShell.
        /// UseShellExecute is required: the app attaches to its parent console on startup, so a plain
        /// CreateProcess would make PowerShell share that console instead of opening its own window.
        /// </summary>
        public void OpenTerminal(string dir)
        {
            // Launch directly, not via the shell: ShellExecute on wt.exe (a Store app alias) "succeeds" without opening a window
            try
            {
                Process.Start(new ProcessStartInfo("wt.exe")
                {
                    ArgumentList = { "-d", dir },
                    UseShellExecute = false,
                    WorkingDirectory = dir
                });
                return;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Windows Terminal not installed
            }

            // A console app started from a GUI app gets its own new console window
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoExit", "-NoLogo" },
                UseShellExecute = false,
                WorkingDirectory = dir
            });
        }

        public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        public bool IsBlocked(string path) => WindowsShellService.IsBlocked(path);

        public void Unblock(string path) => WindowsShellService.Unblock(path);

        public void SetWallpaper(string imagePath) => WindowsShellService.SetWallpaper(imagePath);

        /// <summary>
        /// Runs clipboard work on an STA thread. Uses a short-lived thread rather than the UI dispatcher,
        /// which would deadlock if the UI thread is itself waiting on this job.
        /// </summary>
        private static void RunOnSta(Action action)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                action();
                return;
            }

            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) throw new InvalidOperationException("Couldn't copy to the clipboard: " + error.Message, error);
        }
    }
}
