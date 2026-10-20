using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using OgConverter.Models;
using OgConverter.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace OgConverter.Tests
{
    /// <summary>
    /// Web pages and SVG drawn by the browser, HEIC photos, OCR, rotate/snapshot, every archive kind, and the
    /// clipboard / unblock actions.
    /// </summary>
    public static class EverydayVerification
    {
        public static async Task RunAsync(ConversionEngine engine, AppSettings settings, string tempDir, PlatformSelfTests platform)
        {
            Console.WriteLine("[23] Everyday tools");
            string dir = Path.Combine(tempDir, "everyday");
            Directory.CreateDirectory(dir);

            await WebPagesAsync(engine, settings, dir);
            await SvgAsync(engine, settings, dir);
            await HeicAsync(engine, settings, dir, platform);
            await OcrAsync(engine, settings, dir);
            await RotateAndSnapshotAsync(engine, settings, dir);
            await ArchivesAsync(engine, settings, dir);
            await ActionsAsync(engine, settings, dir, platform);
        }

        private static async Task<ConversionJob> RunAsync(ConversionEngine engine, AppSettings settings, string file, WheelMode mode, string idOrExt, string label)
        {
            var items = FormatRegistry.GetWheelItems(file, mode);
            var item = items.FirstOrDefault(i => mode == WheelMode.Convert ? i.Extension == idOrExt : i.Id == idOrExt)
                       ?? throw new Exception($"{label}: '{idOrExt}' isn't on the {mode} wheel ({string.Join(", ", items.Select(i => i.Title))})");
            var watch = Stopwatch.StartNew();
            var job = await engine.ExecuteAsync(file, item, settings);
            Console.WriteLine($"     {label,-34} Success={job.IsSuccess,-5} {watch.Elapsed.TotalSeconds,5:0.0}s {Path.GetFileName(job.TargetPath)} {job.ResultText} {job.ErrorMessage}");
            if (!job.IsSuccess) throw new Exception($"{label} failed: {job.ErrorMessage}");
            return job;
        }

        private static (int W, int H) Size(string png) => Platform.Images.ReadPixelSize(png);

        private static async Task WebPagesAsync(ConversionEngine engine, AppSettings settings, string dir)
        {
            if (!BrowserRenderService.IsAvailable)
            {
                Console.WriteLine("     (skip web pages: no Edge or Chrome)");
                return;
            }

            // A long page with a background, a local picture and a stylesheet next to it
            string site = Path.Combine(dir, "site");
            Directory.CreateDirectory(site);
            File.WriteAllText(Path.Combine(site, "style.css"), "body { background: #102030; color: #fff; font: 20px Segoe UI; } .box { height: 2400px; border: 4px solid #f80; }");
            SavePng(Path.Combine(site, "pic.png"), 120, 80, 0xFF4500); // orange red
            string page = Path.Combine(site, "page.html");
            File.WriteAllText(page, "<!DOCTYPE html><html><head><link rel=stylesheet href=style.css><title>T</title></head>" +
                                    "<body><h1>Hello page</h1><img src=pic.png><div class=box>tall</div><p>The end</p></body></html>");

            var png = await RunAsync(engine, settings, page, WheelMode.Convert, "png", "HTML → full-page PNG");
            var (w, h) = Size(png.TargetPath);
            if (w < 1280 || h < 2400) throw new Exception($"Full-page picture is only {w}x{h}");
            var jpg = await RunAsync(engine, settings, page, WheelMode.Convert, "jpg", "HTML → full-page JPG");
            if (Size(jpg.TargetPath).H != h) throw new Exception("JPG should be the same picture as the PNG");

            var pdf = await RunAsync(engine, settings, page, WheelMode.Convert, "pdf", "HTML → PDF (browser)");
            using (var doc = UglyToad.PdfPig.PdfDocument.Open(pdf.TargetPath))
            {
                string text = string.Join(" ", doc.GetPages().Select(p => p.Text));
                if (doc.NumberOfPages < 2 || !text.Contains("The end")) throw new Exception("HTML PDF is missing pages or text");
            }

            // README with a picture on a server that never answers: must still finish, well inside the time limit
            string repo = Path.Combine(dir, "offline repo");
            Directory.CreateDirectory(repo);
            string readme = Path.Combine(repo, "README.md");
            File.WriteAllText(readme, "# Offline\n\n![build](http://10.255.255.1/badge.svg) ![cov](http://10.255.255.2/cov.svg)\n\nBody text.\n");
            var watch = Stopwatch.StartNew();
            await RunAsync(engine, settings, readme, WheelMode.Convert, "pdf", "README with dead badges → PDF");
            if (watch.Elapsed.TotalSeconds > 45) throw new Exception($"README PDF took {watch.Elapsed.TotalSeconds:0}s with unreachable pictures");

            var mdPng = await RunAsync(engine, settings, readme, WheelMode.Convert, "png", "README → PNG");
            if (Size(mdPng.TargetPath).W < 980) throw new Exception("README picture is too narrow");
        }

        private static async Task SvgAsync(ConversionEngine engine, AppSettings settings, string dir)
        {
            string svg = Path.Combine(dir, "logo.svg");
            File.WriteAllText(svg, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"80\" viewBox=\"0 0 120 80\">" +
                                   "<circle cx=\"60\" cy=\"40\" r=\"30\" fill=\"#36c\"/><text x=\"10\" y=\"75\" font-size=\"12\">OG Logo</text></svg>");

            var sizes = new (string Markup, int W, int H)[]
            {
                ("width=\"2in\" height=\"1in\"", 192, 96),
                ("viewBox=\"0 0 24 12\"", 512, 256),
                ("viewBox=\"0 0 800 600\"", 800, 600),
                ("width=\"300\"", 300, 300),
            };
            foreach (var (markup, ew, eh) in sizes)
            {
                string f = Path.Combine(dir, "size.svg");
                File.WriteAllText(f, $"<svg xmlns=\"http://www.w3.org/2000/svg\" {markup}/>");
                if (BrowserRenderService.SvgSize(f) != (ew, eh)) throw new Exception($"SVG size for {markup}: {BrowserRenderService.SvgSize(f)}");
            }

            if (!BrowserRenderService.IsAvailable)
            {
                Console.WriteLine("     (skip SVG rendering: no Edge or Chrome)");
                return;
            }

            var png = await RunAsync(engine, settings, svg, WheelMode.Convert, "png", "SVG → PNG");
            if (Size(png.TargetPath) != (240, 160)) throw new Exception($"SVG PNG should be drawn at 2x: {Size(png.TargetPath)}");
            var pixels = Platform.Images.ToBgra(Platform.Images.Decode(File.ReadAllBytes(png.TargetPath)));
            if (pixels.Pixels[pixels.Stride + 4 + 3] != 0) throw new Exception("SVG background should stay transparent");
            await RunAsync(engine, settings, svg, WheelMode.Convert, "jpg", "SVG → JPG");
            await RunAsync(engine, settings, svg, WheelMode.Convert, "ico", "SVG → ICO");
            var pdf = await RunAsync(engine, settings, svg, WheelMode.Convert, "pdf", "SVG → PDF (vector)");
            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf.TargetPath);
            if (doc.NumberOfPages != 1 || !doc.GetPage(1).Text.Contains("OG Logo")) throw new Exception("SVG PDF should be one page with real text");
        }

        private static async Task HeicAsync(ConversionEngine engine, AppSettings settings, string dir, PlatformSelfTests platform)
        {
            string heic = Path.Combine(dir, "phone photo.heic");
            if (platform.WriteHeicAsync == null || !await platform.WriteHeicAsync(heic))
            {
                Console.WriteLine("     (skip HEIC: this PC can't write HEIC to test with)");
                return;
            }
            if (!FormatRegistry.GetWheelItems(heic, WheelMode.Tools).Any(i => i.Id == "tool_ocr")) throw new Exception("HEIC tools should offer Extract Text");
            var jpg = await RunAsync(engine, settings, heic, WheelMode.Convert, "jpg", "HEIC → JPG");
            if (Size(jpg.TargetPath) != (64, 48)) throw new Exception($"HEIC JPG size {Size(jpg.TargetPath)}");
            await RunAsync(engine, settings, heic, WheelMode.Convert, "png", "HEIC → PNG");
            await RunAsync(engine, settings, heic, WheelMode.Convert, "pdf", "HEIC → PDF");
        }

        private static async Task OcrAsync(ConversionEngine engine, AppSettings settings, string dir)
        {
            if (!OcrService.IsAvailable)
            {
                Console.WriteLine("     (skip OCR: no recognition language installed)");
                return;
            }

            string shot = Path.Combine(dir, "screenshot.png");
            if (!await SaveTextAsync(shot, "Invoice number 4821", "Total due 129.50 EUR"))
            {
                Console.WriteLine("     (skip OCR: no PDF renderer to draw the test picture)");
                return;
            }
            var job = await RunAsync(engine, settings, shot, WheelMode.Tools, "tool_ocr", "Picture → Extract Text (OCR)");
            string text = File.ReadAllText(job.TargetPath);
            if (!text.Contains("Invoice") || !text.Contains("4821")) throw new Exception($"OCR read: {text}");

            // A "scanned" PDF (only a picture of the page): PDF → TXT falls back to OCR
            string scan = Path.Combine(dir, "scan.pdf");
            PdfToolsService.ImageToPdf(shot, scan);
            var txt = await RunAsync(engine, settings, scan, WheelMode.Convert, "txt", "Scanned PDF → TXT (OCR)");
            if (!File.ReadAllText(txt.TargetPath).Contains("Invoice")) throw new Exception("Scanned PDF text wasn't read");
        }

        private static async Task RotateAndSnapshotAsync(ConversionEngine engine, AppSettings settings, string dir)
        {
            string photo = Path.Combine(dir, "wide.png");
            SavePng(photo, 90, 40, 0x2E8B57); // sea green
            var rotated = await RunAsync(engine, settings, photo, WheelMode.Tools, "tool_rotate", "Image → Rotate 90°");
            if (Size(rotated.TargetPath) != (40, 90)) throw new Exception($"Rotated image is {Size(rotated.TargetPath)}");

            if (!engine.IsFFmpegAvailable)
            {
                Console.WriteLine("     (skip video rotate/snapshot: no FFmpeg)");
                return;
            }
            string clip = Path.Combine(dir, "clip.mp4");
            await MediaToolkit.RunAsync(engine.FFmpegPath!, new[] { "-f", "lavfi", "-i", "testsrc=size=160x90:rate=25:duration=4", "-f", "lavfi", "-i", "sine=duration=4",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", clip });

            var turned = await RunAsync(engine, settings, clip, WheelMode.Tools, "tool_rotate", "Video → Rotate 90°");
            var info = await MediaToolkit.ProbeAsync(engine.FFmpegPath!, turned.TargetPath);
            if (info.Width != 90 || info.Height != 160 || !info.HasAudio) throw new Exception($"Rotated video is {info.Width}x{info.Height}, audio={info.HasAudio}");

            var shot = await RunAsync(engine, settings, clip, WheelMode.Tools, "tool_snapshot", "Video → Snapshot");
            if (Size(shot.TargetPath) != (160, 90)) throw new Exception($"Snapshot is {Size(shot.TargetPath)}");
        }

        private static async Task ArchivesAsync(ConversionEngine engine, AppSettings settings, string dir)
        {
            string content = Path.Combine(dir, "content");
            Directory.CreateDirectory(Path.Combine(content, "sub"));
            File.WriteAllText(Path.Combine(content, "a.txt"), "alpha");
            File.WriteAllText(Path.Combine(content, "sub", "b.txt"), "bravo");
            // libarchive's tar (tar.exe in Windows, bsdtar elsewhere) writes every kind from the file name
            string? tar = ArchiveService.BsdTarPath;

            var made = new System.Collections.Generic.List<string>();
            foreach (string name in new[] { "pack.7z", "pack.tar.gz", "pack.tar", "pack.tar.bz2", "pack.tar.xz" })
            {
                if (tar == null) break;
                string path = Path.Combine(dir, name);
                var psi = new ProcessStartInfo(tar) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = content };
                foreach (string a in new[] { "-a", "-cf", path, "a.txt", "sub" }) psi.ArgumentList.Add(a);
                using (var p = Process.Start(psi)!) await p.WaitForExitAsync();
                if (File.Exists(path)) made.Add(path);
            }

            string gz = Path.Combine(dir, "server.log.gz");
            await using (var fs = File.Create(gz))
            await using (var z = new GZipStream(fs, CompressionLevel.Optimal))
            {
                await z.WriteAsync(System.Text.Encoding.UTF8.GetBytes("log line"));
            }

            foreach (string archive in made)
            {
                // Every one unpacks to "pack_extracted": start empty so leftovers can't pass for a result
                string previous = Path.Combine(dir, "pack_extracted");
                if (Directory.Exists(previous)) Directory.Delete(previous, true);
                var job = await RunAsync(engine, settings, archive, WheelMode.Convert, "dir", $"Extract {Path.GetFileName(archive)}");
                if (File.ReadAllText(Path.Combine(job.TargetPath, "sub", "b.txt")) != "bravo") throw new Exception($"{archive} extracted wrong");
            }
            var single = await RunAsync(engine, settings, gz, WheelMode.Convert, "dir", "Extract server.log.gz");
            if (File.ReadAllText(Path.Combine(single.TargetPath, "server.log")) != "log line") throw new Exception("Single-file .gz extracted wrong");

            if (made.FirstOrDefault(p => p.EndsWith(".7z")) is { } sevenZip)
            {
                var zip = await RunAsync(engine, settings, sevenZip, WheelMode.Convert, "zip", "7z → ZIP");
                using (var z = ZipFile.OpenRead(zip.TargetPath))
                {
                    if (!z.Entries.Any(e => e.FullName.Replace('\\', '/') == "sub/b.txt")) throw new Exception("Repacked ZIP is missing files");
                }
                var targz = await RunAsync(engine, settings, sevenZip, WheelMode.Convert, "gz", "7z → TAR.GZ");
                if (!targz.TargetPath.EndsWith("pack.tar.gz") && !targz.TargetPath.Contains("pack_")) throw new Exception($"Odd name {targz.TargetPath}");
                var back = await RunAsync(engine, settings, targz.TargetPath, WheelMode.Convert, "dir", "Extract repacked TAR.GZ");
                if (!File.Exists(Path.Combine(back.TargetPath, "a.txt"))) throw new Exception("Repacked TAR.GZ is missing files");
            }
        }

        private static async Task ActionsAsync(ConversionEngine engine, AppSettings settings, string dir, PlatformSelfTests platform)
        {
            if (platform.ReadClipboardText == null || platform.ReadClipboardImageAndFile == null)
            {
                Console.WriteLine("     (skip clipboard: no desktop session to copy to)");
            }
            else
            {
                string notes = Path.Combine(dir, "notes.cs");
                File.WriteAllText(notes, "class Hello { }");
                await RunAsync(engine, settings, notes, WheelMode.Actions, "act_copy_text", "Code file → Copy Text");
                string clip = platform.ReadClipboardText();
                if (clip != "class Hello { }") throw new Exception($"Clipboard has '{clip}'");

                string photo = Path.Combine(dir, "copy me.png");
                SavePng(photo, 30, 20, 0xFFD700); // gold
                await RunAsync(engine, settings, photo, WheelMode.Actions, "act_copy_image", "Image → Copy Image");
                var (hasImage, hasFile) = platform.ReadClipboardImageAndFile();
                if (!hasImage || !hasFile) throw new Exception("Clipboard should hold the picture and the file");
            }

            // Mark of the Web, as a browser writes it
            if (platform.MarkAsDownloaded == null)
            {
                Console.WriteLine("     (skip unblock: this system doesn't mark downloaded files)");
                return;
            }
            string download = Path.Combine(dir, "download.txt");
            File.WriteAllText(download, "x");
            platform.MarkAsDownloaded(download);
            if (!Platform.Desktop.IsBlocked(download)) throw new Exception("Downloaded file should be detected as blocked");
            await RunAsync(engine, settings, download, WheelMode.Actions, "act_unblock", "Downloaded file → Unblock");
            if (Platform.Desktop.IsBlocked(download)) throw new Exception("File is still blocked");
            if (FormatRegistry.GetWheelItems(download, WheelMode.Actions).Any(i => i.Id == "act_unblock")) throw new Exception("Unblock shown for a file that isn't blocked");
        }

        /// <summary>A plain picture of one color (0xRRGGBB).</summary>
        private static void SavePng(string path, int w, int h, int rgb)
        {
            var px = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i] = (byte)rgb;
                px[i + 1] = (byte)(rgb >> 8);
                px[i + 2] = (byte)(rgb >> 16);
                px[i + 3] = 255;
            }
            using var fs = File.Create(path);
            Platform.Images.Encode(Platform.Images.FromPixels(w, h, 96, RasterFormat.Bgra32, px, w * 4), fs, ImageEncoding.Png());
        }

        /// <summary>
        /// Black text on white, like a screenshot of a document: drawn on a PDF page and rendered, so it only needs the
        /// PDF renderer. False when there's none.
        /// </summary>
        private static async Task<bool> SaveTextAsync(string path, params string[] lines)
        {
            if (Platform.Pdf == null) return false;
            PdfFonts.EnsureConfigured();
            string pdf = Path.ChangeExtension(path, ".source.pdf");
            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(675);
                page.Height = XUnit.FromPoint(165);
                using var gfx = XGraphics.FromPdfPage(page);
                var font = new XFont("Arial", 30);
                for (int i = 0; i < lines.Length; i++) gfx.DrawString(lines[i], font, XBrushes.Black, new XPoint(22, 52 + i * 42));
                doc.Save(pdf);
            }
            await PdfRenderService.RenderPagesToPngAsync(pdf, (_, _) => path);
            File.Delete(pdf);
            return File.Exists(path);
        }
    }
}
