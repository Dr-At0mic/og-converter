using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace OgConverter.Services
{
    /// <summary>
    /// Renders local web pages, Markdown and SVG with headless Microsoft Edge (or another Chromium browser): print to
    /// PDF, or a full-page picture. Edge ships with Windows, and a browser lays out web pages (code blocks, tables,
    /// images, emoji, SVG) far more faithfully than Word's HTML import or FFmpeg, which can't read SVG at all.
    /// </summary>
    public static class BrowserRenderService
    {
        private static readonly Lazy<string?> Browser = new(FindBrowser);

        public static bool IsAvailable => Browser.Value != null;
        public static string? BrowserPath => Browser.Value;

        public const string MissingBrowserHint = "Microsoft Edge or Google Chrome is needed for this (Edge comes with Windows).";

        /// <summary>
        /// Longest the browser waits for a page's pictures and fonts before printing what has arrived. Without it, each
        /// unreachable remote picture (a README badge on an offline PC) holds the page for ~25 s and the job fails.
        /// </summary>
        private const int PageLoadLimitMs = 20_000;
        private static readonly TimeSpan ProcessLimit = TimeSpan.FromSeconds(90);

        /// <summary>Tallest page captured, in CSS pixels; longer pages are cut off there.</summary>
        private const int MaxPageHeight = 30_000;

        public static async Task PrintAsync(string htmlPath, string pdfPath)
        {
            if (File.Exists(pdfPath)) File.Delete(pdfPath);
            await RunAsync(new[] { "--no-pdf-header-footer", "--print-to-pdf=" + pdfPath, new Uri(htmlPath).AbsoluteUri });
            if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length == 0)
            {
                throw new InvalidOperationException("The browser didn't produce a PDF.");
            }
        }

        /// <summary>
        /// Saves the whole page, top to bottom, as one PNG, the way it looks on screen in a window
        /// <paramref name="width"/> CSS pixels wide (sharper on short pages: rendered at 2x).
        /// </summary>
        public static async Task ScreenshotAsync(string htmlPath, string pngPath, int width = 1280)
        {
            // A copy with a script that measures the page; <base> keeps its relative pictures and styles working
            string page = Path.Combine(Path.GetTempPath(), $"ogc-page-{Guid.NewGuid():N}.html");
            try
            {
                await File.WriteAllBytesAsync(page, WithMeasureScript(await File.ReadAllBytesAsync(htmlPath), Path.GetDirectoryName(Path.GetFullPath(htmlPath))!));
                var watch = Stopwatch.StartNew();
                var size = await MeasureAsync(page, width) ?? (width, 1600);
                int height = Math.Clamp(size.Height, 1, MaxPageHeight);

                // Whatever didn't arrive while measuring (an unreachable badge) won't arrive now either: don't wait for it twice
                int loadLimit = watch.ElapsedMilliseconds >= PageLoadLimitMs - 1000 ? 4000 : PageLoadLimitMs;
                await CaptureAsync(page, pngPath, Math.Max(width, Math.Min(size.Width, 4 * width)), height, height <= 8000 ? 2 : 1, transparent: false, loadLimit);
            }
            finally
            {
                TryDelete(page);
            }
        }

        /// <summary>
        /// Draws an SVG as a PNG with a transparent background, at twice its own size (at most 4096 px on the longest
        /// side, at least its own size) so icons and logos come out sharp.
        /// </summary>
        public static async Task RenderSvgAsync(string svgPath, string pngPath)
        {
            var (w, h) = SvgSize(svgPath);
            double scale = Math.Max(1, Math.Min(2, 4096.0 / Math.Max(w, h)));
            await WithSvgPageAsync(svgPath, w, h, "", page => CaptureAsync(page, pngPath, w, h, scale, transparent: true));
        }

        /// <summary>SVG → a one-page PDF the SVG's size. Stays vector: text remains selectable and lines sharp at any zoom.</summary>
        public static async Task SvgToPdfAsync(string svgPath, string pdfPath)
        {
            var (w, h) = SvgSize(svgPath);
            string pageCss = string.Create(CultureInfo.InvariantCulture, $"@page {{ size: {w}px {h}px; margin: 0; }}");
            await WithSvgPageAsync(svgPath, w, h, pageCss, page => PrintAsync(page, pdfPath));
        }

        private static async Task WithSvgPageAsync(string svgPath, int w, int h, string extraCss, Func<string, Task> render)
        {
            string page = Path.Combine(Path.GetTempPath(), $"ogc-svg-{Guid.NewGuid():N}.html");
            try
            {
                string html = $$"""
                    <!DOCTYPE html>
                    <html><head><meta charset="utf-8"><style>
                      html, body { margin: 0; padding: 0; background: transparent; overflow: hidden; }
                      img { display: block; width: {{w}}px; height: {{h}}px; }
                      {{extraCss}}
                    </style></head>
                    <body><img src="{{new Uri(Path.GetFullPath(svgPath)).AbsoluteUri}}"></body></html>
                    """;
                await File.WriteAllTextAsync(page, html, new UTF8Encoding(false));
                await render(page);
            }
            finally
            {
                TryDelete(page);
            }
        }

        /// <summary>
        /// Width and height the SVG asks for, in CSS pixels. Falls back to the viewBox, then to 512 px on the long side;
        /// percentages and missing sizes mean "fill the window", which has no natural size.
        /// </summary>
        public static (int Width, int Height) SvgSize(string svgPath)
        {
            double? w = null, h = null, vbW = null, vbH = null;
            try
            {
                using var reader = XmlReader.Create(svgPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
                if (reader.MoveToContent() == XmlNodeType.Element)
                {
                    w = Length(reader.GetAttribute("width"));
                    h = Length(reader.GetAttribute("height"));
                    string[] vb = (reader.GetAttribute("viewBox") ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (vb.Length == 4 && double.TryParse(vb[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double vw) &&
                        double.TryParse(vb[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double vh) && vw > 0 && vh > 0)
                    {
                        vbW = vw;
                        vbH = vh;
                    }
                }
            }
            catch (XmlException) { }

            double ratio = vbW is { } rw && vbH is { } rh ? rw / rh : w is { } ww && h is { } hh && hh > 0 ? ww / hh : 1;
            if (w == null && h != null) w = h * ratio;
            if (h == null && w != null) h = w / ratio;
            if (w == null || h == null)
            {
                (w, h) = (vbW, vbH) is ({ } a, { } b) && Math.Max(a, b) >= 64 ? (a, b) : ratio >= 1 ? (512, 512 / ratio) : (512 * ratio, 512);
            }
            return (Math.Clamp((int)Math.Round(w.Value), 1, 16384), Math.Clamp((int)Math.Round(h.Value), 1, 16384));

            static double? Length(string? value)
            {
                var m = Regex.Match(value ?? "", @"^\s*([0-9.]+)\s*(px|pt|pc|mm|cm|in|em|ex)?\s*$");
                if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || n <= 0) return null;
                return n * m.Groups[2].Value switch
                {
                    "pt" => 96 / 72.0,
                    "pc" => 16,
                    "mm" => 96 / 25.4,
                    "cm" => 96 / 2.54,
                    "in" => 96,
                    "em" or "ex" => 16,
                    _ => 1
                };
            }
        }

        /// <summary>
        /// Adds a &lt;base&gt; (unless the page has one) and a script that records the page's full size once it has
        /// loaded. Works on the raw bytes (Latin-1 maps each byte to one char), so the page's own encoding is untouched.
        /// </summary>
        private static byte[] WithMeasureScript(byte[] html, string folder)
        {
            string text = Encoding.Latin1.GetString(html);
            string inject = (Regex.IsMatch(text, @"<base[\s>]", RegexOptions.IgnoreCase) ? "" : $"<base href=\"{new Uri(folder.TrimEnd('\\') + "\\").AbsoluteUri}\">") +
                            "<script>addEventListener('load',function(){var d=document.documentElement,b=document.body||d;" +
                            "d.setAttribute('data-ogc-size',Math.ceil(Math.max(d.scrollWidth,b.scrollWidth))+'x'+Math.ceil(Math.max(d.scrollHeight,b.scrollHeight)))})</script>";

            var head = Regex.Match(text, @"<head(\s[^>]*)?>", RegexOptions.IgnoreCase);
            var root = Regex.Match(text, @"<html(\s[^>]*)?>", RegexOptions.IgnoreCase);
            int at = head.Success ? head.Index + head.Length : root.Success ? root.Index + root.Length : 0;
            return Encoding.Latin1.GetBytes(text.Insert(at, inject));
        }

        /// <summary>The page's full width and height in CSS pixels, or null if the page blocked the measuring script.</summary>
        private static async Task<(int Width, int Height)?> MeasureAsync(string page, int width)
        {
            string dom = await RunAsync(new[] { $"--window-size={width},800", "--dump-dom", new Uri(page).AbsoluteUri });
            var m = Regex.Match(dom, "data-ogc-size=\"(\\d+)x(\\d+)\"");
            return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;
        }

        private static async Task CaptureAsync(string page, string pngPath, int width, int height, double scale, bool transparent, int loadLimitMs = PageLoadLimitMs)
        {
            if (File.Exists(pngPath)) File.Delete(pngPath);
            var args = new List<string>
            {
                $"--window-size={width},{height}",
                "--force-device-scale-factor=" + scale.ToString("0.###", CultureInfo.InvariantCulture),
                "--screenshot=" + pngPath
            };
            if (transparent) args.Add("--default-background-color=00000000");
            args.Add(new Uri(page).AbsoluteUri);
            await RunAsync(args, loadLimitMs);

            if (!File.Exists(pngPath) || new FileInfo(pngPath).Length == 0)
            {
                throw new InvalidOperationException("The browser didn't produce a picture.");
            }
        }

        /// <summary>Runs the browser headless with a throwaway profile and returns what it printed.</summary>
        private static async Task<string> RunAsync(IEnumerable<string> args, int loadLimitMs = PageLoadLimitMs)
        {
            string browser = Browser.Value ?? throw new InvalidOperationException(MissingBrowserHint);

            // A private profile: otherwise the call is handed to a browser window that's already open and returns at once
            string profile = Path.Combine(Path.GetTempPath(), "ogc-browser-" + Guid.NewGuid().ToString("N"));
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
                "--disable-sync", "--disable-background-networking", "--disable-component-update", "--hide-scrollbars", "--mute-audio",
                "--user-data-dir=" + profile, "--timeout=" + loadLimitMs
            }) psi.ArgumentList.Add(a);
            foreach (string a in args) psi.ArgumentList.Add(a);

            try
            {
                using var p = Process.Start(psi)!;
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                var exited = p.WaitForExitAsync();
                if (await Task.WhenAny(exited, Task.Delay(ProcessLimit)) != exited)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    throw new InvalidOperationException("The browser took too long to render the page.");
                }

                // A helper process (the crash reporter) can hold the output pipes open after the browser itself has exited
                await Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(TimeSpan.FromSeconds(3)));
                return stdout.IsCompletedSuccessfully ? stdout.Result : "";
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
            var candidates = new List<string>
            {
                Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(local, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pf, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                Path.Combine(local, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                Path.Combine(local, @"Vivaldi\Application\vivaldi.exe"),
                Path.Combine(local, @"Chromium\Application\chrome.exe")
            };

            // Installs in other places register themselves under App Paths
            foreach (string exe in new[] { "msedge.exe", "chrome.exe", "brave.exe" })
            {
                foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                {
                    try
                    {
                        using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
                        if (key?.GetValue(null) is string path) candidates.Add(path.Trim('"'));
                    }
                    catch { }
                }
            }

            return candidates.Find(File.Exists);
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
