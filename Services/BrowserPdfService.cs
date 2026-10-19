using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace OgConverter.Services
{
    /// <summary>
    /// Prints a local HTML page to PDF with headless Microsoft Edge (or Chrome). Edge ships with Windows, and a
    /// browser renders web layouts (code blocks, tables, images, emoji) far more faithfully than Word's HTML import.
    /// </summary>
    public static class BrowserPdfService
    {
        private static readonly Lazy<string?> Browser = new(FindBrowser);

        public static bool IsAvailable => Browser.Value != null;

        public static async Task PrintAsync(string htmlPath, string pdfPath)
        {
            string browser = Browser.Value ?? throw new InvalidOperationException("Microsoft Edge or Google Chrome is needed to print to PDF.");

            // A private profile: otherwise the call is handed to an Edge window that's already open and returns at once
            string profile = Path.Combine(Path.GetTempPath(), "ogc-browser-" + Guid.NewGuid().ToString("N"));
            if (File.Exists(pdfPath)) File.Delete(pdfPath);

            var psi = new ProcessStartInfo(browser)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (string a in new[]
            {
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                "--disable-sync", "--user-data-dir=" + profile, "--no-pdf-header-footer", "--print-to-pdf=" + pdfPath,
                new Uri(htmlPath).AbsoluteUri
            }) psi.ArgumentList.Add(a);

            try
            {
                using var p = Process.Start(psi)!;
                var drainOut = p.StandardOutput.ReadToEndAsync();
                var drainErr = p.StandardError.ReadToEndAsync();
                var exited = p.WaitForExitAsync();
                if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(90))) != exited)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    throw new InvalidOperationException("The browser took too long to print the PDF.");
                }
                await Task.WhenAll(drainOut, drainErr);

                if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length == 0)
                {
                    throw new InvalidOperationException("The browser didn't produce a PDF.");
                }
            }
            finally
            {
                // The profile can stay locked for a moment after exit
                for (int i = 0; i < 5 && Directory.Exists(profile); i++)
                {
                    try { Directory.Delete(profile, true); }
                    catch { await Task.Delay(200); }
                }
            }
        }

        private static string? FindBrowser()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string path in new[]
            {
                Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(local, @"Google\Chrome\Application\chrome.exe")
            })
            {
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }
}
