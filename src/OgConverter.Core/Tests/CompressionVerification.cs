using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using OgConverter.Models;
using OgConverter.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using CompressionMode = OgConverter.Models.CompressionMode;

namespace OgConverter.Tests
{
    /// <summary>
    /// Self-test for Compress across every file kind and mode: checks output validity, real size reduction,
    /// pixel-identical lossless results, EXIF orientation handling, metadata removal, target size accuracy,
    /// and package integrity for PDF / Office / archives.
    /// </summary>
    internal static class CompressionVerification
    {
        private static readonly WheelItem Compress = new() { Id = ConversionEngine.CompressToolId, Title = "Compress", Mode = WheelMode.Tools };

        public static async Task RunAsync(ConversionEngine engine, AppSettings settings, string tempDir)
        {
            Console.WriteLine("[30] Compression");
            string dir = Path.Combine(tempDir, "compress");
            Directory.CreateDirectory(dir);
            string? ffmpeg = engine.FFmpegPath;

            async Task<string?> Run(string path, CompressionOptions o, string label, bool mayRefuse = false)
            {
                var sw = Stopwatch.StartNew();
                var job = await engine.ExecuteAsync(path, Compress, settings, new ToolParameters { Compression = o });
                string pct = job.IsSuccess ? $"{(1 - (double)job.ConvertedSizeBytes / job.OriginalSizeBytes) * 100,5:0.0}% smaller" : "";
                Console.WriteLine($"     {label,-44} {(job.IsSuccess ? "OK " : "-- ")} {Path.GetFileName(job.TargetPath),-36} {pct} {sw.ElapsedMilliseconds,6} ms {job.ErrorMessage}");
                if (!job.IsSuccess)
                {
                    if (mayRefuse && job.ErrorMessage != null && job.ErrorMessage.StartsWith("Already", StringComparison.Ordinal)) return null;
                    throw new Exception($"Compress {label} failed: {job.ErrorMessage}");
                }
                if (job.ConvertedSizeBytes >= job.OriginalSizeBytes) throw new Exception($"{label}: output is not smaller");
                return job.TargetPath;
            }

            // Target size: under the limit, and (minShare) close enough to it that the quality wasn't given away
            async Task<string> RunToSize(string path, CompressionOptions o, string label, double minShare = 0)
            {
                var sw = Stopwatch.StartNew();
                var job = await engine.ExecuteAsync(path, Compress, settings, new ToolParameters { Compression = o });
                long limit = CompressionService.TargetBytes(o.TargetSizeMb);
                Console.WriteLine($"     {label,-44} {(job.IsSuccess ? "OK " : "-- ")} {Path.GetFileName(job.TargetPath),-36} {job.ConvertedSizeBytes,9:N0} of {limit,9:N0} bytes {sw.ElapsedMilliseconds,6} ms {job.ErrorMessage}");
                if (!job.IsSuccess) throw new Exception($"Compress {label} failed: {job.ErrorMessage}");
                if (job.ConvertedSizeBytes > limit || job.TargetFormat != "Compressed") throw new Exception($"{label}: {job.ConvertedSizeBytes} bytes, target {limit} ({job.TargetFormat})");
                if (job.ConvertedSizeBytes < limit * minShare) throw new Exception($"{label}: {job.ConvertedSizeBytes} bytes uses less than {minShare:P0} of the {limit}-byte target");
                return job.TargetPath;
            }

            // ---------- Wheel ----------
            foreach (var (name, expect) in new[] { ("a.png", true), ("a.mp4", true), ("a.flac", true), ("a.pdf", true), ("a.docx", true), ("a.pptx", true),
                                                   ("a.xlsx", true), ("a.csv", true), ("a.zip", true), ("a.svg", true), ("a.gif", true), ("a.bin", true),
                                                   ("a.ico", false), ("a.7z", false), ("a.gz", false) })
            {
                bool has = FormatRegistry.GetWheelItems(Path.Combine(dir, name), WheelMode.Tools).Exists(i => i.Id == ConversionEngine.CompressToolId);
                if (has != expect) throw new Exception($"Compress on the wheel for {name}: expected {expect}, got {has}");
            }
            Console.WriteLine("     Compress appears on the Tools wheel for every compressible type (and not for .ico/.7z/.gz)");

            // ---------- Images ----------
            string photoPng = Path.Combine(dir, "photo.png");
            SavePng(CreatePhoto(1600, 1200, alpha: false), photoPng);

            string? losslessPng = await Run(photoPng, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "keep" }, "PNG lossless (optimized PNG)", mayRefuse: true);
            if (losslessPng != null) AssertSamePixels(photoPng, losslessPng, "optimized PNG");

            if (ffmpeg != null)
            {
                string webpLossless = (await Run(photoPng, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "webp" }, "PNG lossless → WebP"))!;
                AssertSamePixels(photoPng, await DecodeWithFFmpeg(ffmpeg, webpLossless), "lossless WebP");
                string jxlLossless = (await Run(photoPng, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "jxl" }, "PNG lossless → JPEG XL"))!;
                AssertSamePixels(photoPng, await DecodeWithFFmpeg(ffmpeg, jxlLossless), "lossless JPEG XL");

                string palette = (await Run(photoPng, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "keep", Quality = 80 }, "PNG lossy (palette, 208 colors)"))!;
                var pal = Decode(palette);
                if (!pal.HasPalette) throw new Exception($"Palette PNG is {pal.Format}, expected 8-bit indexed");

                foreach (string fmt in new[] { "webp", "avif", "jxl", "jpg" })
                {
                    string outPath = (await Run(photoPng, new CompressionOptions { Mode = CompressionMode.Lossy, Format = fmt, Quality = 75, MaxDimension = 800 }, $"PNG lossy → {fmt.ToUpperInvariant()} q75, 800 px"))!;
                    var info = await MediaToolkit.ProbeAsync(ffmpeg, outPath);
                    if (Math.Max(info.Width, info.Height) != 800) throw new Exception($"{fmt}: expected longest edge 800, got {info.Width}x{info.Height}");
                }
            }

            // Target size: the best quality that fits at full size; smaller only when even a low quality doesn't fit
            string sizedJpg = await RunToSize(photoPng, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "jpg", TargetSizeMb = 0.4 }, "PNG → JPEG under 0.4 MB", minShare: 0.6);
            if (Decode(sizedJpg).Width != 1600) throw new Exception("JPEG under 0.4 MB should keep its full size");
            string tinyJpg = await RunToSize(photoPng, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "jpg", TargetSizeMb = 0.04 }, "PNG → JPEG under 40 KB (resized)", minShare: 0.4);
            if (Decode(tinyJpg).Width >= 1600) throw new Exception("JPEG under 40 KB should have been made smaller");
            if (ffmpeg != null)
            {
                await RunToSize(photoPng, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "keep", TargetSizeMb = 0.8 }, "PNG palette under 0.8 MB");
                await RunToSize(photoPng, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "webp", TargetSizeMb = 0.15 }, "PNG → WebP under 150 KB", minShare: 0.5);
            }

            // Transparency: palette PNG must not ruin soft edges; JPEG must flatten onto white, not black
            string alphaPng = Path.Combine(dir, "logo_alpha.png");
            SavePng(CreatePhoto(900, 600, alpha: true), alphaPng);
            if (ffmpeg != null)
            {
                await Run(alphaPng, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "keep", Quality = 70, MaxDimension = 640 }, "PNG with soft alpha, lossy (no palette)");
                string flat = (await Run(alphaPng, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "jpg", Quality = 85 }, "PNG with alpha → JPEG (flattened on white)"))!;
                var px = Bgra(Decode(flat));
                if (px[0] < 235 || px[1] < 235 || px[2] < 235) throw new Exception($"Transparent corner should be white, got {px[2]},{px[1]},{px[0]}");
            }
            var alphaChoices = CompressionService.FormatChoices(CompressionKind.Image, alphaPng, CompressionMode.Lossy, hasAlpha: true);
            if (alphaChoices.Any(c => c.Key is "avif" or "jpg")) throw new Exception("Formats without transparency must not be offered for transparent images");

            // JPEG with EXIF orientation 6 (rotated 90°) and camera/GPS-ish metadata
            string camJpg = Path.Combine(dir, "camera.jpg");
            SaveJpegWithExif(CreatePhoto(1200, 800, alpha: false), camJpg, orientation: 6);
            if (Orientation(camJpg) != 6) throw new Exception("Test JPEG should carry orientation 6");

            string stripped = (await Run(camJpg, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "keep", StripMetadata = true }, "JPEG lossless (metadata removed)"))!;
            AssertSamePixels(camJpg, stripped, "metadata-stripped JPEG");
            var strippedMeta = JpegExif.Read(File.ReadAllBytes(stripped));
            if (Orientation(stripped) != 6) throw new Exception("Lossless JPEG lost its orientation");
            if (JpegExif.ReadText(strippedMeta, JpegExif.MakeTag) != null || JpegExif.ReadText(strippedMeta, JpegExif.SoftwareTag) != null)
                throw new Exception("Lossless JPEG still contains camera make/software");

            string rotated = (await Run(camJpg, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "keep", Quality = 70 }, "JPEG lossy q70 (rotation applied)"))!;
            var rot = Decode(rotated);
            if (rot.Width != 800 || rot.Height != 1200) throw new Exception($"Lossy JPEG should be upright 800x1200, got {rot.Width}x{rot.Height}");
            if (Orientation(rotated) != 1) throw new Exception("Upright JPEG must not keep a rotation tag");

            string bmp = Path.Combine(dir, "scan.bmp");
            Save(ImageEncoding.Bmp(), CreatePhoto(640, 480, alpha: false, noise: false), bmp);
            string bmpPng = (await Run(bmp, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "png" }, "BMP lossless → PNG"))!;
            AssertSamePixels(bmp, bmpPng, "BMP → PNG");

            string tiff = Path.Combine(dir, "scan.tiff");
            Save(ImageEncoding.Tiff() with { Uncompressed = true }, CreatePhoto(640, 480, alpha: false, noise: false), tiff);
            string tiffZip = (await Run(tiff, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "keep" }, "TIFF lossless (Deflate)"))!;
            AssertSamePixels(tiff, tiffZip, "Deflate TIFF");

            // ---------- GIF ----------
            if (ffmpeg != null)
            {
                string gif = Path.Combine(dir, "clip.gif");
                await MediaToolkit.RunAsync(ffmpeg, new[] { "-f", "lavfi", "-i", "testsrc2=s=480x270:r=25:d=3", "-vf", "split[a][b];[a]palettegen[p];[b][p]paletteuse", gif });
                await Run(gif, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "gif", Quality = 40, MaxFps = 15, MaxDimension = 320 }, "GIF lossy (fewer colors, 15 fps, 320 px)");
                await Run(gif, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "webp", Quality = 70 }, "GIF lossy → animated WebP");
                await Run(gif, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "webp" }, "GIF lossless → animated WebP", mayRefuse: true);
                await RunToSize(gif, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "gif", TargetSizeMb = new FileInfo(gif).Length / 3.0 / 1_000_000 }, "GIF under a third of its size");
            }

            // ---------- SVG ----------
            string svg = Path.Combine(dir, "icon.svg");
            File.WriteAllText(svg, """
                <?xml version="1.0" encoding="UTF-8" standalone="no"?>
                <!-- Created with Inkscape (http://www.inkscape.org/) -->
                <svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape"
                     xmlns:sodipodi="http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd" width="100" height="100" viewBox="0 0 100 100"
                     inkscape:version="1.3" sodipodi:docname="icon.svg">
                  <sodipodi:namedview id="namedview1" pagecolor="#ffffff" inkscape:zoom="2.5" />
                  <metadata><rdf>Author info that nobody needs</rdf></metadata>
                  <!-- the circle -->
                  <g id="layer1" inkscape:label="Layer 1" inkscape:groupmode="layer">
                    <circle cx="50" cy="50" r="40" fill="#ff6b35" />
                    <text x="50" y="55" text-anchor="middle">OG</text>
                  </g>
                </svg>
                """);
            string minSvg = (await Run(svg, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "min" }, "SVG minify"))!;
            string minText = File.ReadAllText(minSvg);
            if (minText.Contains("inkscape") || minText.Contains("<!--") || minText.Contains("metadata") || !minText.Contains("<circle") || !minText.Contains(">OG<"))
                throw new Exception($"SVG minify output is wrong: {minText}");
            await Run(svg, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "svgz" }, "SVG → .svgz");

            // ---------- Video ----------
            if (ffmpeg != null)
            {
                string video = Path.Combine(dir, "clip.mp4");
                await MediaToolkit.RunAsync(ffmpeg, new[] { "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=6", "-f", "lavfi", "-i", "sine=f=440:d=6",
                                                            "-c:v", "libx264", "-crf", "8", "-preset", "ultrafast", "-c:a", "aac", "-b:a", "256k", "-metadata", "title=Secret", video });

                foreach (string codec in new[] { "h264", "h265", "av1", "vp9" })
                {
                    string outPath = (await Run(video, new CompressionOptions { Mode = CompressionMode.Lossy, Format = codec, Quality = 55, Speed = 0, MaxHeight = 480, VideoAudioKbps = 96 },
                        $"Video lossy {codec.ToUpperInvariant()} q55, 480p"))!;
                    var vi = await MediaToolkit.ProbeAsync(ffmpeg, outPath);
                    if (vi.Height != 480 || !vi.HasAudio) throw new Exception($"{codec}: expected 480p with audio, got {vi.Width}x{vi.Height} audio={vi.HasAudio}");
                }

                const double targetMb = 0.6;
                string sized = (await Run(video, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "h264", TargetSizeMb = targetMb, Speed = 0, VideoAudioKbps = 64 }, $"Video target size {targetMb} MB (two-pass)"))!;
                double mb = new FileInfo(sized).Length / 1024.0 / 1024.0;
                if (mb > targetMb * 1.15 || mb < targetMb * 0.5) throw new Exception($"Target {targetMb} MB, got {mb:0.00} MB");
                Console.WriteLine($"     → target {targetMb} MB, actual {mb:0.00} MB");

                string sizedX265 = (await Run(video, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "h265", TargetSizeMb = targetMb, Speed = 0, VideoAudioKbps = 64 }, $"Video target size {targetMb} MB H.265 (two-pass)"))!;
                double mb265 = new FileInfo(sizedX265).Length / 1024.0 / 1024.0;
                if (mb265 > targetMb * 1.15) throw new Exception($"H.265 target {targetMb} MB, got {mb265:0.00} MB");

                await Run(video, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "h264", Quality = 50, Speed = 0, VideoAudioKbps = -1, MaxFps = 24 }, "Video lossy, audio removed, 24 fps");

                var hw = await MediaToolkit.GetWorkingHardwareEncodersAsync(ffmpeg);
                Console.WriteLine($"     Working GPU encoders: {(hw.Count > 0 ? string.Join(", ", hw) : "none")}");
                if (hw.Any(h => h.StartsWith("h264_", StringComparison.Ordinal)))
                    await Run(video, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "h264", Quality = 55, UseHardwareEncoder = true, VideoAudioKbps = 96 }, "Video lossy H.264 on GPU");
                if (hw.Any(h => h.StartsWith("hevc_", StringComparison.Ordinal)))
                    await Run(video, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "h265", Quality = 55, UseHardwareEncoder = true, TargetSizeMb = targetMb, VideoAudioKbps = 64 }, "Video H.265 on GPU, target size");

                await Run(video, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "remux", StripMetadata = true }, "Video lossless remux", mayRefuse: true);
                // Lossless re-encodes of an already-compressed MP4 usually grow; refusing (and cleaning up) is correct
                await Run(video, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "x264", Speed = 0 }, "Video lossless H.264", mayRefuse: true);

                // A near-uncompressed capture is where lossless shines
                string raw = Path.Combine(dir, "capture.avi");
                await MediaToolkit.RunAsync(ffmpeg, new[] { "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=2", "-f", "lavfi", "-i", "sine=d=2", "-c:v", "rawvideo", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", raw });
                await Run(raw, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "ffv1" }, "Raw capture lossless → FFV1");
                await Run(raw, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "x265", Speed = 0 }, "Raw capture lossless → H.265");

                // ---------- Audio ----------
                string wav = Path.Combine(dir, "song.wav");
                await MediaToolkit.RunAsync(ffmpeg, new[] { "-f", "lavfi", "-i", "sine=f=330:d=8,aformat=channel_layouts=stereo", "-c:a", "pcm_s16le", "-ar", "44100", wav });
                foreach (string fmt in new[] { "flac", "alac" })
                    await Run(wav, new CompressionOptions { Mode = CompressionMode.Lossless, Format = fmt }, $"WAV lossless → {fmt.ToUpperInvariant()}");
                foreach (var (fmt, kbps) in new[] { ("opus", 96), ("aac", 128), ("mp3", 130) })
                    await Run(wav, new CompressionOptions { Mode = CompressionMode.Lossy, Format = fmt, AudioKbps = kbps }, $"WAV lossy → {fmt.ToUpperInvariant()} {kbps} kbps");
                string voice = (await Run(wav, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "aac", AudioKbps = 48, Mono = true, SampleRate = 22050 }, "WAV lossy → AAC 48 kbps mono 22 kHz"))!;
                var vinfo = await MediaToolkit.ProbeAsync(ffmpeg, voice);
                if (vinfo.AudioChannels != 1) throw new Exception("Mono option ignored");
                foreach (string fmt in new[] { "mp3", "aac", "opus" })
                    await RunToSize(wav, new CompressionOptions { Mode = CompressionMode.Lossy, Format = fmt, TargetSizeMb = 0.1 }, $"WAV → {fmt.ToUpperInvariant()} under 100 KB", minShare: 0.5);

                string mp3 = Path.Combine(dir, "track.mp3");
                await MediaToolkit.RunAsync(ffmpeg, new[] { "-f", "lavfi", "-i", "sine=d=8", "-f", "lavfi", "-i", "color=red:s=600x600:d=1", "-map", "0:a", "-map", "1:v", "-frames:v", "1",
                                                            "-c:a", "libmp3lame", "-b:a", "256k", "-c:v", "mjpeg", "-disposition:v", "attached_pic",
                                                            "-metadata", "artist=Someone", "-metadata", "comment=" + new string('x', 4000), mp3 });
                await Run(mp3, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "keep" }, "MP3 lossless (tags and cover removed)");
                await Run(mp3, new CompressionOptions { Mode = CompressionMode.Lossy, Format = "mp3", AudioKbps = 130, StripMetadata = false }, "MP3 lossy V5, cover kept");
            }

            // ---------- PDF ----------
            string bigJpg = Path.Combine(dir, "pdf_photo.jpg");
            SaveJpegWithExif(CreatePhoto(2400, 1800, alpha: false), bigJpg, orientation: 1, quality: 95);
            string bigPng = Path.Combine(dir, "pdf_chart.png");
            SavePng(CreatePhoto(1800, 1200, alpha: false), bigPng);
            string pdf = Path.Combine(dir, "brochure.pdf");
            PdfFonts.EnsureConfigured();
            using (var doc = new PdfDocument())
            {
                doc.Info.Author = "Jane Doe";
                foreach (string img in new[] { bigJpg, bigPng })
                {
                    var page = doc.AddPage();
                    using var gfx = XGraphics.FromPdfPage(page);
                    using var x = XImage.FromFile(img);
                    gfx.DrawImage(x, 36, 36, page.Width.Point - 72, (page.Width.Point - 72) * x.PixelHeight / x.PixelWidth);
                    gfx.DrawString("Hello compressed world", new XFont("Arial", 20), XBrushes.Black, new XPoint(40, page.Height.Point - 60));
                }
                doc.Save(pdf);
            }
            string pdfScreen = (await Run(pdf, new CompressionOptions { Mode = CompressionMode.Lossy, PdfDpi = 72, Quality = 60 }, "PDF lossy · screen 72 dpi"))!;
            string pdfEbook = (await Run(pdf, new CompressionOptions { Mode = CompressionMode.Lossy, PdfDpi = 150, Quality = 75, Grayscale = true }, "PDF lossy · ebook 150 dpi, grayscale"))!;
            await Run(pdf, new CompressionOptions { Mode = CompressionMode.Lossless }, "PDF lossless", mayRefuse: true);
            foreach (string p in new[] { pdfScreen, pdfEbook })
            {
                if (PdfToolsService.GetPageCount(p) != 2) throw new Exception("Compressed PDF lost pages");
                if (!PdfToolsService.ExtractText(p).Contains("Hello compressed world")) throw new Exception("Compressed PDF lost its text");
                var pages = await PdfRenderService.RenderPagesToPngAsync(p, (n, _) => Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(p)}_render_{n}.png"));
                if (pages.Count != 2 || pages.Any(f => new FileInfo(f).Length < 1000)) throw new Exception("Compressed PDF doesn't render");
            }
            using (var check = PdfSharp.Pdf.IO.PdfReader.Open(pdfScreen, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            {
                if (!string.IsNullOrEmpty(check.Info.Author)) throw new Exception("PDF metadata not removed");
            }
            // Between the 150 dpi and 72 dpi results: the search has to find a resolution and quality in between
            double pdfTargetMb = (new FileInfo(pdfScreen).Length + new FileInfo(pdfEbook).Length) / 2.0 / 1_000_000;
            string pdfSized = await RunToSize(pdf, new CompressionOptions { Mode = CompressionMode.Lossy, TargetSizeMb = pdfTargetMb }, $"PDF under {pdfTargetMb * 1000:0} KB");
            if (PdfToolsService.GetPageCount(pdfSized) != 2 || !PdfToolsService.ExtractText(pdfSized).Contains("Hello compressed world")) throw new Exception("Target-size PDF lost pages or text");

            // ---------- Office ----------
            string docx = Path.Combine(dir, "report.docx");
            CreateDocx(docx, bigJpg, bigPng);
            string docxSmall = (await Run(docx, new CompressionOptions { Mode = CompressionMode.Lossy, MaxDimension = 1280, Quality = 70 }, "DOCX lossy · email (1280 px)"))!;
            using (var z = ZipFile.OpenRead(docxSmall))
            {
                var original = ZipFile.OpenRead(docx);
                if (!original.Entries.Select(e => e.FullName).SequenceEqual(z.Entries.Select(e => e.FullName))) throw new Exception("DOCX entries changed");
                original.Dispose();
                using (var img = z.GetEntry("word/media/image1.jpg")!.Open())
                {
                    var ms = new MemoryStream(); img.CopyTo(ms);
                    var f = Platform.Images.Decode(ms.ToArray());
                    if (Math.Max(f.Width, f.Height) != 1280) throw new Exception($"DOCX picture not resized: {f.Width}x{f.Height}");
                }
                string core = new StreamReader(z.GetEntry("docProps/core.xml")!.Open()).ReadToEnd();
                if (core.Contains("Jane Doe")) throw new Exception("DOCX author not removed");
            }
            await Run(docx, new CompressionOptions { Mode = CompressionMode.Lossless, StripMetadata = false }, "DOCX lossless", mayRefuse: true);
            double docxTargetMb = new FileInfo(docxSmall).Length * 1.2 / 1_000_000;
            await RunToSize(docx, new CompressionOptions { Mode = CompressionMode.Lossy, TargetSizeMb = docxTargetMb }, $"DOCX under {docxTargetMb * 1000:0} KB");

            string odt = Path.Combine(dir, "letter.odt");
            CreateOdt(odt, bigJpg);
            string odtSmall = (await Run(odt, new CompressionOptions { Mode = CompressionMode.Lossy, MaxDimension = 1920, Quality = 80 }, "ODT lossy · web (1920 px)"))!;
            using (var z = ZipFile.OpenRead(odtSmall))
            {
                var first = z.Entries[0];
                if (first.FullName != "mimetype" || first.CompressedLength != first.Length) throw new Exception("ODT mimetype must be first and stored");
            }

            // ---------- Archives and other files ----------
            string text = Path.Combine(dir, "server.log");
            File.WriteAllText(text, string.Concat(Enumerable.Range(0, 20000).Select(i => $"2026-10-18 12:{i % 60:D2}:{i % 59:D2} INFO request {i} served in {i % 97} ms\n")));
            foreach (string fmt in new[] { "zip", "gz", "br" })
            {
                string outPath = (await Run(text, new CompressionOptions { Mode = CompressionMode.Lossless, Format = fmt }, $"Text → {fmt.ToUpperInvariant()}"))!;
                if (fmt == "gz") { using var gz = new GZipStream(File.OpenRead(outPath), System.IO.Compression.CompressionMode.Decompress); if (new StreamReader(gz).ReadToEnd() != File.ReadAllText(text)) throw new Exception("GZip round trip failed"); }
                if (fmt == "br") { using var br = new BrotliStream(File.OpenRead(outPath), System.IO.Compression.CompressionMode.Decompress); if (new StreamReader(br).ReadToEnd() != File.ReadAllText(text)) throw new Exception("Brotli round trip failed"); }
            }

            string json = Path.Combine(dir, "data.json");
            File.WriteAllText(json, System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0, 200).Select(i => new { id = i, name = $"Item {i}", tags = new[] { "a", "b" } }),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            string minJson = (await Run(json, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "min" }, "JSON minify"))!;
            using (var a = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json)))
            using (var b = System.Text.Json.JsonDocument.Parse(File.ReadAllText(minJson)))
            {
                if (System.Text.Json.JsonSerializer.Serialize(a.RootElement) != System.Text.Json.JsonSerializer.Serialize(b.RootElement) || File.ReadAllText(minJson).Contains('\n'))
                    throw new Exception("Minified JSON differs");
            }

            string looseZip = Path.Combine(dir, "bundle.zip");
            using (var z = ZipFile.Open(looseZip, ZipArchiveMode.Create))
            {
                z.CreateEntryFromFile(text, "logs/server.log", CompressionLevel.NoCompression);
                z.CreateEntryFromFile(json, "data.json", CompressionLevel.Fastest);
            }
            string repacked = (await Run(looseZip, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "zip" }, "ZIP re-pack (maximum)"))!;
            using (var z = ZipFile.OpenRead(repacked))
            {
                if (z.Entries.Count != 2 || new StreamReader(z.GetEntry("logs/server.log")!.Open()).ReadToEnd() != File.ReadAllText(text)) throw new Exception("Re-packed ZIP content differs");
            }

            string tar = Path.Combine(tempDir, "backup.tar");
            System.Formats.Tar.TarFile.CreateFromDirectory(Path.Combine(dir), tar, includeBaseDirectory: false);
            await Run(tar, new CompressionOptions { Mode = CompressionMode.Lossless, Format = "gz" }, "TAR → tar.gz");

            Console.WriteLine("     Compression: all checks passed");
        }

        // ---------- Fixtures ----------

        /// <summary>A photo-like image: smooth gradients plus fine noise (hard for lossless, easy for lossy).</summary>
        private static RasterImage CreatePhoto(int w, int h, bool alpha, bool noise = true)
        {
            var rnd = new Random(42);
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    int n = noise ? rnd.Next(-12, 13) : 0;
                    px[i] = (byte)Math.Clamp(60 + 120 * x / w + n, 0, 255);
                    px[i + 1] = (byte)Math.Clamp(90 + 100 * y / h + n, 0, 255);
                    px[i + 2] = (byte)Math.Clamp(200 - 80 * (x + y) / (w + h) + n, 0, 255);
                    if (alpha)
                    {
                        // Opaque disc with a soft edge, transparent corners
                        double d = Math.Sqrt(Math.Pow(x - w / 2.0, 2) + Math.Pow(y - h / 2.0, 2)) / (Math.Min(w, h) / 2.0);
                        px[i + 3] = (byte)Math.Clamp((1.0 - d) * 4 * 255, 0, 255);
                    }
                    else px[i + 3] = 255;
                }
            }
            var bmp = Platform.Images.FromPixels(w, h, 96, alpha ? RasterFormat.Bgra32 : RasterFormat.Bgr32, px, w * 4);
            return alpha ? bmp : Platform.Images.ConvertFormat(bmp, RasterFormat.Bgr24);
        }

        private static void SavePng(RasterImage bmp, string path) => Save(ImageEncoding.Png(), bmp, path);

        private static void Save(ImageEncoding encoding, RasterImage bmp, string path)
        {
            using var fs = File.Create(path);
            Platform.Images.Encode(bmp, fs, encoding);
        }

        /// <summary>A camera-like JPEG: EXIF orientation plus make, firmware and artist.</summary>
        private static void SaveJpegWithExif(RasterImage bmp, string path, int orientation, int quality = 95)
        {
            using var ms = new MemoryStream();
            Platform.Images.Encode(bmp, ms, ImageEncoding.Jpeg(quality));
            byte[] exif = JpegExif.Build(orientation, (JpegExif.MakeTag, "TestCam"), (JpegExif.SoftwareTag, "TestCam Firmware 1.0"), (JpegExif.ArtistTag, "Jane Doe"));
            File.WriteAllBytes(path, JpegExif.Insert(ms.ToArray(), exif));
        }

        private static int Orientation(string path) => Platform.Images.ReadOrientation(File.ReadAllBytes(path));

        private static RasterImage Decode(string path) => Platform.Images.Decode(File.ReadAllBytes(path));

        private static byte[] Bgra(RasterImage b) => Platform.Images.ToBgra(b).Pixels;

        private static void AssertSamePixels(string a, string b, string what)
        {
            var pa = Decode(a);
            var pb = Decode(b);
            if (pa.Width != pb.Width || pa.Height != pb.Height) throw new Exception($"{what}: size changed");
            if (!Bgra(pa).AsSpan().SequenceEqual(Bgra(pb))) throw new Exception($"{what}: pixels differ (not lossless)");
        }

        private static async Task<string> DecodeWithFFmpeg(string ffmpeg, string path)
        {
            string png = path + ".decoded.png";
            await MediaToolkit.RunAsync(ffmpeg, new[] { "-i", path, "-frames:v", "1", "-pix_fmt", "rgb24", png });
            return png;
        }

        private static void CreateDocx(string path, string jpg, string png)
        {
            using var z = ZipFile.Open(path, ZipArchiveMode.Create);
            void Text(string name, string content)
            {
                using var w = new StreamWriter(z.CreateEntry(name, CompressionLevel.Fastest).Open());
                w.Write(content);
            }
            Text("[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="jpg" ContentType="image/jpeg"/><Default Extension="png" ContentType="image/png"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/></Types>""");
            Text("_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/></Relationships>""");
            Text("word/document.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Quarterly report</w:t></w:r></w:p></w:body></w:document>""");
            Text("docProps/core.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>Report</dc:title><dc:creator>Jane Doe</dc:creator><cp:lastModifiedBy>Jane Doe</cp:lastModifiedBy></cp:coreProperties>""");
            z.CreateEntryFromFile(jpg, "word/media/image1.jpg", CompressionLevel.Fastest);
            z.CreateEntryFromFile(png, "word/media/image2.png", CompressionLevel.Fastest);
        }

        private static void CreateOdt(string path, string jpg)
        {
            using var z = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var w = new StreamWriter(z.CreateEntry("mimetype", CompressionLevel.NoCompression).Open())) w.Write("application/vnd.oasis.opendocument.text");
            using (var w = new StreamWriter(z.CreateEntry("content.xml", CompressionLevel.Fastest).Open())) w.Write("<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"/>");
            using (var w = new StreamWriter(z.CreateEntry("meta.xml", CompressionLevel.Fastest).Open()))
                w.Write("<office:document-meta xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:meta=\"urn:oasis:names:tc:opendocument:xmlns:meta:1.0\"><office:meta><meta:initial-creator>Jane Doe</meta:initial-creator></office:meta></office:document-meta>");
            z.CreateEntryFromFile(jpg, "Pictures/photo.jpg", CompressionLevel.Fastest);
        }
    }
}
