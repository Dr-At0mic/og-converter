using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using OgConverter.Services;

namespace OgConverter.Platform.Linux
{
    /// <summary>
    /// The Linux desktop through its standard tools: wl-clipboard (Wayland) or xclip (X11) for the clipboard, the
    /// freedesktop FileManager1 D-Bus interface for "show in folder", xdg-open for links, gsettings or Plasma for the
    /// wallpaper. Linux has no "downloaded from the internet" mark, so nothing is ever blocked.
    /// </summary>
    public sealed class LinuxDesktopServices : IDesktopServices
    {
        private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

        private static bool IsWayland => Env("WAYLAND_DISPLAY") != null || Env("XDG_SESSION_TYPE") == "wayland";

        // ------------------------------------------------------------------ Clipboard

        public void SetClipboardText(string text) => CopyToClipboard(Encoding.UTF8.GetBytes(text), mimeType: null);

        /// <summary>
        /// Copies the picture as PNG, which chats, mail, documents and image editors paste. wl-copy and xclip offer one
        /// format per copy, so the file itself isn't offered alongside it.
        /// </summary>
        public void SetClipboardImageAndFile(string picturePath, string filePath)
        {
            var image = ImageCodec.TryLoad(picturePath, applyOrientation: true, maxDimension: 0)
                        ?? throw new InvalidOperationException("This picture can't be opened.");
            using var png = new MemoryStream();
            Services.Platform.Images.Encode(image.Bitmap, png, ImageEncoding.Png());
            CopyToClipboard(png.ToArray(), "image/png");
        }

        /// <summary>
        /// wl-copy on Wayland, else (or when it can't get the clipboard, e.g. while the screen is locked) xclip through
        /// XWayland, whose clipboard GNOME and KDE share with Wayland apps.
        /// </summary>
        private static void CopyToClipboard(byte[] data, string? mimeType)
        {
            string? wlCopy = IsWayland ? Services.Platform.FindOnPath("wl-copy") : null;
            string? xclip = Env("DISPLAY") != null ? Services.Platform.FindOnPath("xclip") : null;
            if (wlCopy == null && xclip == null)
            {
                if (Env("DISPLAY") == null && Env("WAYLAND_DISPLAY") == null) throw new InvalidOperationException("There's no desktop session to copy to.");
                throw new InvalidOperationException("Copying to the clipboard needs " + (IsWayland
                    ? $"wl-clipboard ({InstallHints.Command("wl-clipboard")})."
                    : $"xclip ({InstallHints.Command("xclip")})."));
            }

            InvalidOperationException? failed = null;
            if (wlCopy != null)
            {
                try
                {
                    Copy(wlCopy, mimeType == null ? Array.Empty<string>() : new[] { "--type", mimeType }, data, xclip != null ? 5_000 : 10_000);
                    return;
                }
                catch (InvalidOperationException ex) when (xclip != null)
                {
                    failed = ex;
                }
            }
            try
            {
                Copy(xclip!, mimeType == null ? new[] { "-selection", "clipboard" } : new[] { "-selection", "clipboard", "-t", mimeType }, data, 10_000);
            }
            catch (InvalidOperationException) when (failed != null)
            {
                throw failed;
            }
        }

        /// <summary>Hands <paramref name="data"/> to a clipboard tool, which then serves it from the background.</summary>
        private static void Copy(string tool, string[] args, byte[] data, int timeoutMs)
        {
            // Both tools stay in the background to serve the clipboard: wait for the foreground part only, and keep their
            // output away from ours (a background copy holding our stdout open would keep a pipe or terminal waiting)
            var psi = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Couldn't start {Path.GetFileName(tool)}.");
            _ = p.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var errors = p.StandardError.ReadLineAsync();
            p.StandardInput.BaseStream.Write(data);
            p.StandardInput.Close();
            if (!p.WaitForExit(timeoutMs))
            {
                // wl-copy waits forever when the compositor won't hand it the clipboard
                try { p.Kill(); } catch (InvalidOperationException) { }
                throw new InvalidOperationException($"{Path.GetFileName(tool)} didn't finish copying.");
            }
            if (p.ExitCode != 0)
            {
                string why = errors.Wait(500) ? errors.Result?.Trim() ?? "" : "";
                throw new InvalidOperationException($"Couldn't copy to the clipboard: {(why.Length > 0 ? why : $"{Path.GetFileName(tool)} failed")}");
            }
        }

        // ------------------------------------------------------------------ File manager, terminal, links

        /// <summary>Asks the file manager over D-Bus to select the item; opens its folder when no file manager answers.</summary>
        public void RevealInFileManager(string path)
        {
            string full = Path.GetFullPath(path);
            _ = Task.Run(() =>
            {
                string? gdbus = Services.Platform.FindOnPath("gdbus");
                if (gdbus != null && Run(gdbus, 10_000, "call", "--session", "--dest", "org.freedesktop.FileManager1",
                        "--object-path", "/org/freedesktop/FileManager1", "--method", "org.freedesktop.FileManager1.ShowItems",
                        $"['{GVariantEscape(new Uri(full).AbsoluteUri)}']", "''") == 0)
                {
                    return;
                }
                string folder = Directory.Exists(full) ? full : Path.GetDirectoryName(full) ?? full;
                try
                {
                    Launch("xdg-open", null, folder);
                }
                catch (InvalidOperationException)
                {
                    // Nothing on this system opens folders
                }
            });
        }

        /// <summary>The first terminal found, started in the folder with its own working-directory option.</summary>
        public void OpenTerminal(string directory)
        {
            foreach (string name in new[] { "x-terminal-emulator", "kgx", "gnome-console", "ptyxis", "gnome-terminal", "konsole", "xfce4-terminal",
                                            "mate-terminal", "tilix", "alacritty", "kitty", "foot", "xterm" })
            {
                string? path = Services.Platform.FindOnPath(name);
                if (path == null) continue;

                // x-terminal-emulator is Debian's link to the preferred terminal; its options are that terminal's
                string real = name;
                try
                {
                    if (File.ResolveLinkTarget(path, returnFinalTarget: true) is { } target) real = Path.GetFileName(target.FullName);
                }
                catch (IOException) { }

                string[] args = real switch
                {
                    "gnome-terminal" or "gnome-terminal.wrapper" or "kgx" or "gnome-console" or "xfce4-terminal" or "mate-terminal" or "tilix" or "foot"
                        => new[] { "--working-directory=" + directory },
                    "ptyxis" => new[] { "--new-window", "--working-directory=" + directory },
                    "konsole" => new[] { "--workdir", directory },
                    "alacritty" => new[] { "--working-directory", directory },
                    "kitty" => new[] { "--directory", directory },
                    _ => Array.Empty<string>()
                };
                Launch(path, directory, args);
                return;
            }
            throw new InvalidOperationException("No terminal app was found. Install one, for example GNOME Console or Konsole.");
        }

        public void OpenUrl(string url) => Launch("xdg-open", null, url);

        public bool IsBlocked(string path) => false;

        public void Unblock(string path)
        {
        }

        // ------------------------------------------------------------------ Wallpaper

        /// <summary>GNOME (and its relatives) through gsettings, KDE Plasma through its own tool.</summary>
        public void SetWallpaper(string imagePath)
        {
            string desktop = (Env("XDG_CURRENT_DESKTOP") ?? "").ToUpperInvariant();
            string uri = new Uri(Path.GetFullPath(imagePath)).AbsoluteUri;
            string? gsettings = Services.Platform.FindOnPath("gsettings");

            if (desktop.Contains("KDE") && Services.Platform.FindOnPath("plasma-apply-wallpaperimage") is { } plasma)
            {
                Require(Run(plasma, 15_000, Path.GetFullPath(imagePath)));
            }
            else if (gsettings != null && (desktop.Contains("GNOME") || desktop.Contains("UNITY") || desktop.Contains("BUDGIE") || desktop.Contains("POP")))
            {
                Require(Run(gsettings, 10_000, "set", "org.gnome.desktop.background", "picture-uri", uri));
                // GNOME 42+ has a separate picture for dark mode; older versions don't know the key
                Run(gsettings, 10_000, "set", "org.gnome.desktop.background", "picture-uri-dark", uri);
            }
            else if (gsettings != null && desktop.Contains("CINNAMON"))
            {
                Require(Run(gsettings, 10_000, "set", "org.cinnamon.desktop.background", "picture-uri", uri));
            }
            else if (gsettings != null && desktop.Contains("MATE"))
            {
                Require(Run(gsettings, 10_000, "set", "org.mate.background", "picture-filename", Path.GetFullPath(imagePath)));
            }
            else
            {
                throw new InvalidOperationException($"Setting the wallpaper isn't supported on this desktop ({(desktop.Length > 0 ? desktop : "unknown")}). Set it from your desktop's settings.");
            }

            static void Require(int exitCode)
            {
                if (exitCode != 0) throw new InvalidOperationException("The desktop didn't accept the new wallpaper.");
            }
        }

        // ------------------------------------------------------------------ Helpers

        /// <summary>Runs a tool to completion (output discarded); its exit code, or -1 when it can't start or takes too long.</summary>
        private static int Run(string tool, int timeoutMs, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p == null) return -1;
                _ = p.StandardOutput.ReadToEndAsync();
                _ = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs)) return -1;
                return p.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return -1;
            }
        }

        /// <summary>Starts a program and leaves it running.</summary>
        private static void Launch(string tool, string? workingDirectory, params string[] args)
        {
            var psi = new ProcessStartInfo(tool) { UseShellExecute = false };
            if (workingDirectory != null) psi.WorkingDirectory = workingDirectory;
            foreach (string a in args) psi.ArgumentList.Add(a);
            try
            {
                Process.Start(psi)?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException($"Couldn't start {Path.GetFileName(tool)}: {ex.Message}", ex);
            }
        }

        /// <summary>Escapes text for a single-quoted GVariant string.</summary>
        private static string GVariantEscape(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
