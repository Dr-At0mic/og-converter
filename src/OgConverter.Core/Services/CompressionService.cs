using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using OgConverter.Models;
using CompressionMode = OgConverter.Models.CompressionMode;

namespace OgConverter.Services
{
    /// <summary>
    /// Lossless and lossy compression for every file kind the app handles, entirely offline:
    /// images (optimized PNG, palette PNG, WebP, AVIF, JPEG XL, metadata-only JPEG cleanup), animated GIFs,
    /// SVG, video (H.264/H.265/AV1/VP9, GPU encoders, target size, FFV1), audio (Opus/AAC/MP3/FLAC/ALAC),
    /// PDF (image downsampling, stream optimization), Office/OpenDocument packages, archives and anything else
    /// (ZIP, GZip, Brotli).
    /// </summary>
    public static class CompressionService
    {
        public static string RequiresFFmpeg => OperatingSystem.IsWindows()
            ? "FFmpeg is required for this. Install it (winget install Gyan.FFmpeg) or put ffmpeg.exe next to OG Converter, then restart OG Converter."
            : $"FFmpeg is required for this. Install it ({InstallHints.Command("ffmpeg")}), then restart OG Converter.";

        /// <summary>The pipeline a file goes through, or null when compressing it makes no sense (already-compressed archives, icons).</summary>
        public static CompressionKind? KindOf(string path)
        {
            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            return ext switch
            {
                "gif" => CompressionKind.AnimatedGif,
                "svg" => CompressionKind.Svg,
                "ico" or "heic" or "heif" => null,
                "jpg" or "jpeg" or "png" or "webp" or "bmp" or "tif" or "tiff" or "avif" => CompressionKind.Image,
                "pdf" => CompressionKind.Pdf,
                "docx" or "xlsx" or "pptx" or "ppsx" or "docm" or "xlsm" or "pptm" or "odt" or "ods" or "odp" => CompressionKind.Office,
                "zip" or "tar" => CompressionKind.Archive,
                "gz" or "tgz" or "7z" or "rar" or "bz2" or "xz" or "zst" or "br" or "cab" or "lz4" or "lz" or "tzst" or "txz" or "tbz2" => null,
                _ => FormatRegistry.DetectCategory(path) switch
                {
                    FileCategory.Video => CompressionKind.Video,
                    FileCategory.Audio => CompressionKind.Audio,
                    _ => CompressionKind.General
                }
            };
        }

        public static bool IsLossyAvailable(CompressionKind kind) => kind is not (CompressionKind.Svg or CompressionKind.Archive or CompressionKind.General);

        public static bool IsLosslessAudioSource(string path) =>
            Path.GetExtension(path).TrimStart('.').ToLowerInvariant() is "wav" or "flac" or "aiff" or "aif" or "alac";

        /// <summary>Sensible starting choices for a file (used when the dialog has no saved choices for its kind).</summary>
        public static CompressionOptions Defaults(CompressionKind kind, string path)
        {
            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            return kind switch
            {
                CompressionKind.Image => new CompressionOptions { Mode = CompressionMode.Lossy, Format = ext == "bmp" ? "jpg" : "keep", Quality = 80 },
                CompressionKind.AnimatedGif => new CompressionOptions { Mode = CompressionMode.Lossy, Format = "gif", Quality = 70 },
                CompressionKind.Svg => new CompressionOptions { Mode = CompressionMode.Lossless, Format = "min" },
                CompressionKind.Video => new CompressionOptions { Mode = CompressionMode.Lossy, Format = "h264", Quality = 60, Speed = 1, VideoAudioKbps = 128 },
                CompressionKind.Audio => IsLosslessAudioSource(path)
                    ? new CompressionOptions { Mode = CompressionMode.Lossless, Format = "flac", AudioKbps = 192 }
                    : new CompressionOptions { Mode = CompressionMode.Lossy, Format = ext == "mp3" ? "mp3" : ext is "opus" or "ogg" ? "opus" : "aac", AudioKbps = 128 },
                CompressionKind.Pdf => new CompressionOptions { Mode = CompressionMode.Lossy, PdfDpi = 150, Quality = 75 },
                CompressionKind.Office => new CompressionOptions { Mode = CompressionMode.Lossy, MaxDimension = 1920, Quality = 80 },
                CompressionKind.Archive => new CompressionOptions { Mode = CompressionMode.Lossless, Format = ext == "tar" ? "gz" : "zip" },
                _ => new CompressionOptions { Mode = CompressionMode.Lossless, Format = "zip" }
            };
        }

        /// <summary>
        /// Makes sure saved choices still fit this particular file (e.g. "keep" for a BMP, ALAC for an MP3) and
        /// fixes them up rather than failing later.
        /// </summary>
        public static CompressionOptions Normalize(CompressionKind kind, string path, CompressionOptions o)
        {
            o = o.Clone();
            if (!IsLossyAvailable(kind)) o.Mode = CompressionMode.Lossless;
            var allowed = FormatChoices(kind, path, o.Mode, hasAlpha: false).Select(c => c.Key).ToList();
            if (allowed.Count > 0 && !allowed.Contains(o.Format))
            {
                var def = Defaults(kind, path);
                o.Format = def.Mode == o.Mode && allowed.Contains(def.Format) ? def.Format : allowed[0];
            }
            o.Quality = Math.Clamp(o.Quality, 1, 100);
            return o;
        }

        /// <summary>Format / method choices for a file and mode, as (key, label) pairs in recommended order.</summary>
        public static List<(string Key, string Label)> FormatChoices(CompressionKind kind, string path, CompressionMode mode, bool hasAlpha)
        {
            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            var list = new List<(string, string)>();

            switch (kind)
            {
                case CompressionKind.Image when mode == CompressionMode.Lossless:
                    switch (ext)
                    {
                        case "jpg" or "jpeg":
                            list.Add(("keep", "Optimized JPEG: metadata removed, pixels untouched"));
                            break;
                        case "png":
                            list.Add(("keep", "Optimized PNG (maximum zlib, best filters)"));
                            list.Add(("webp", "WebP lossless (≈25% smaller than PNG)"));
                            list.Add(("jxl", "JPEG XL lossless (smallest, limited app support)"));
                            break;
                        case "tif" or "tiff":
                            list.Add(("keep", "TIFF with Deflate compression"));
                            list.Add(("png", "PNG (optimized)"));
                            list.Add(("webp", "WebP lossless"));
                            list.Add(("jxl", "JPEG XL lossless"));
                            break;
                        default: // bmp, webp, avif
                            list.Add(("png", "PNG (optimized)"));
                            list.Add(("webp", "WebP lossless"));
                            list.Add(("jxl", "JPEG XL lossless"));
                            if (ext == "bmp") list.Add(("tiff", "TIFF with Deflate compression"));
                            break;
                    }
                    break;

                case CompressionKind.Image:
                    string keepLabel = ext switch
                    {
                        "jpg" or "jpeg" => "JPEG (same format)",
                        "png" => "PNG with reduced colors (like TinyPNG)",
                        "webp" => "WebP (same format)",
                        "avif" => "AVIF (same format)",
                        "tif" or "tiff" => "TIFF, resized (Deflate)",
                        _ => ""
                    };
                    if (keepLabel.Length > 0) list.Add(("keep", keepLabel));
                    if (ext != "webp") list.Add(("webp", "WebP: ≈30% smaller than JPEG, opens everywhere"));
                    if (ext != "avif" && !hasAlpha) list.Add(("avif", "AVIF: ≈50% smaller than JPEG, modern apps"));
                    list.Add(("jxl", "JPEG XL: smallest at high quality, limited app support"));
                    if (ext is not ("jpg" or "jpeg") && !hasAlpha) list.Add(("jpg", "JPEG: opens in absolutely everything"));
                    if (!hasAlpha) list.Add(("guetzli", "JPEG via Guetzli: 20–30% smaller, opens everywhere (very slow)"));
                    break;

                case CompressionKind.AnimatedGif:
                    if (mode == CompressionMode.Lossless) list.Add(("webp", "Animated WebP, lossless"));
                    else
                    {
                        list.Add(("gif", "GIF with optimized palette (fewer colors)"));
                        list.Add(("webp", "Animated WebP (much smaller, browsers and chat apps)"));
                    }
                    break;

                case CompressionKind.Svg:
                    list.Add(("min", "Minified SVG (comments, editor data and whitespace removed)"));
                    list.Add(("svgz", "Minified and gzipped (.svgz)"));
                    break;

                case CompressionKind.Video when mode == CompressionMode.Lossless:
                    list.Add(("remux", "Remux and clean up: instant, no re-encoding"));
                    list.Add(("ffv1", "FFV1: archival-grade lossless (MKV)"));
                    list.Add(("x264", "H.264 lossless (MKV)"));
                    list.Add(("x265", "H.265 lossless (MKV)"));
                    break;

                case CompressionKind.Video:
                    list.Add(("h264", "H.264: plays on every device"));
                    list.Add(("h265", "H.265 / HEVC: ≈40% smaller, phones and modern PCs"));
                    list.Add(("av1", "AV1: smallest files, newer devices and browsers"));
                    list.Add(("vp9", "VP9 (WebM): web and Android"));
                    break;

                case CompressionKind.Audio when mode == CompressionMode.Lossless:
                    if (IsLosslessAudioSource(path))
                    {
                        list.Add(("flac", "FLAC, maximum compression"));
                        list.Add(("alac", "Apple Lossless (ALAC, .m4a)"));
                    }
                    else
                    {
                        list.Add(("keep", "Remove tags and cover art, no re-encoding"));
                    }
                    break;

                case CompressionKind.Audio:
                    list.Add(("opus", "Opus: best quality per kilobyte"));
                    list.Add(("aac", "AAC (.m4a): Apple devices, phones, browsers"));
                    list.Add(("mp3", "MP3 (VBR): plays everywhere"));
                    break;

                case CompressionKind.Archive:
                    if (ext == "zip") list.Add(("zip", "ZIP, re-packed at maximum compression"));
                    else
                    {
                        list.Add(("gz", "tar.gz (gzip, maximum)"));
                        list.Add(("br", "tar.br (Brotli, smaller)"));
                    }
                    break;

                case CompressionKind.General:
                    if (ext == "json") list.Add(("min", "Minified JSON (whitespace removed)"));
                    list.Add(("zip", "ZIP: opens anywhere"));
                    list.Add(("gz", "GZip (.gz)"));
                    list.Add(("br", "Brotli (.br): smallest, best for text"));
                    break;
            }
            return list;
        }

        // ---------- Quality mappings (1–100 → each encoder's scale) ----------

        public static int X264Crf(int q) => Math.Clamp((int)Math.Round(40 - q * 0.26), 0, 51);
        public static int X265Crf(int q) => Math.Clamp(X264Crf(q) + 4, 0, 51);
        public static int Av1Crf(int q) => Math.Clamp((int)Math.Round(55 - q * 0.33), 0, 63);
        public static int Vp9Crf(int q) => Math.Clamp((int)Math.Round(53 - q * 0.33), 0, 63);
        public static int AvifCrf(int q) => Math.Clamp((int)Math.Round(60 - q * 0.45), 0, 63);
        public static double JxlDistance(int q) => Math.Round(q >= 30 ? 0.1 + (100 - q) * 0.09 : 6.4 + (30 - q) * 0.15, 2);
        public static int PaletteColors(int q) => Math.Clamp((int)Math.Round(16 + q * 2.4), 16, 256);

        /// <summary>MP3 LAME VBR level closest to an average bitrate.</summary>
        public static int LameVbrLevel(int kbps)
        {
            int[] avg = { 245, 225, 190, 175, 165, 130, 115, 100, 85, 65 };
            int best = 0;
            for (int i = 1; i < avg.Length; i++) if (Math.Abs(avg[i] - kbps) < Math.Abs(avg[best] - kbps)) best = i;
            return best;
        }

        // ---------- Entry point ----------

        /// <summary>
        /// Compresses <paramref name="src"/>. <paramref name="targetFor"/>(extension, suffix) returns a free output path.
        /// Returns the output path. Throws if the result isn't smaller than the original (and deletes it).
        /// </summary>
        public static async Task<string> CompressAsync(string src, CompressionOptions? options, string? ffmpeg,
            Func<string, string, string> targetFor, IProgress<double>? progress = null)
        {
            var kind = KindOf(src) ?? throw new InvalidOperationException("This file type is already compressed; there's nothing more to squeeze out.");
            var o = Normalize(kind, src, options ?? Defaults(kind, src));

            string target = kind switch
            {
                CompressionKind.Image => await CompressImageAsync(src, o, ffmpeg, targetFor, progress),
                CompressionKind.AnimatedGif => await CompressGifAsync(src, o, RequireFFmpeg(ffmpeg), targetFor, progress),
                CompressionKind.Svg => await Task.Run(() => CompressSvg(src, o, targetFor)),
                CompressionKind.Video => await CompressVideoAsync(src, o, RequireFFmpeg(ffmpeg), targetFor, progress),
                CompressionKind.Audio => await CompressAudioAsync(src, o, RequireFFmpeg(ffmpeg), targetFor, progress),
                CompressionKind.Pdf => await CompressPdfAsync(src, o, targetFor),
                CompressionKind.Office => await OfficeCompressor.CompressAsync(src, targetFor(Ext(src), "_compressed"), o, ffmpeg),
                CompressionKind.Archive => await Task.Run(() => CompressArchive(src, o, targetFor)),
                _ => await Task.Run(() => CompressGeneral(src, o, targetFor))
            };

            long before = new FileInfo(src).Length;
            long after = new FileInfo(target).Length;
            if (after >= before)
            {
                TryDelete(target);
                throw new InvalidOperationException(o.Mode == CompressionMode.Lossless && IsLossyAvailable(kind)
                    ? "Already as small as it gets without losing quality. Try Lossy compression for a smaller file."
                    : "Already compact: this setting couldn't make it smaller. Try a lower quality or a smaller size.");
            }
            return target;
        }

        private static string RequireFFmpeg(string? ffmpeg) =>
            string.IsNullOrEmpty(ffmpeg) ? throw new InvalidOperationException(RequiresFFmpeg) : ffmpeg;

        private static string Ext(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string NewTempPath(string ext) =>
            Path.Combine(Path.GetTempPath(), $"og-converter-{Guid.NewGuid():N}.{ext}");

        // ---------- Images ----------

        private static async Task<string> CompressImageAsync(string src, CompressionOptions o, string? ffmpeg, Func<string, string, string> targetFor, IProgress<double>? progress = null)
        {
            string ext = Ext(src);
            bool lossless = o.Mode == CompressionMode.Lossless;
            string outExt = o.Format switch
            {
                "keep" => ext == "jpeg" ? "jpg" : ext == "tif" ? "tiff" : ext,
                "guetzli" => "jpg",
                _ => o.Format
            };
            string target = targetFor(outExt, "_compressed");

            if (o.Format == "guetzli")
            {
                // Decode here (EXIF rotation, resize, transparency flattened), then let Guetzli encode from a lossless PNG
                var picture = await Task.Run(() => ImageCodec.TryLoad(src, applyOrientation: true, o.MaxDimension))
                              ?? throw new InvalidOperationException(ImageCodec.CantOpenPicture);
                string png = NewTempPath("png");
                try
                {
                    await Task.Run(() => ImageCodec.SavePng(ImageCodec.FlattenIfTransparent(picture), png));
                    await GuetzliService.EncodeAsync(png, target, o.Quality, progress, o.GuetzliAllowLarge);
                }
                catch
                {
                    TryDelete(target);
                    throw;
                }
                finally
                {
                    TryDelete(png);
                }
                return target;
            }

            // JPEG lossless: drop metadata segments, keep the compressed image data byte-for-byte
            if (lossless && outExt == "jpg")
            {
                await Task.Run(() => ImageCodec.StripJpegMetadata(src, target));
                return target;
            }

            // Decode with Windows Imaging (applies EXIF rotation itself, so results don't depend on the FFmpeg version)
            var image = await Task.Run(() => ImageCodec.TryLoad(src, applyOrientation: !lossless, lossless ? 0 : o.MaxDimension));

            if (image == null)
            {
                // Formats Windows can't decode without extensions (AVIF, some WebP): let FFmpeg read the original
                string input = src;
                await EncodeImageWithFFmpegAsync(RequireFFmpeg(ffmpeg), input, target, outExt, o, lossless ? 0 : o.MaxDimension);
                return target;
            }

            switch (outExt)
            {
                case "jpg" when !lossless:
                    await Task.Run(() => ImageCodec.SaveJpeg(ImageCodec.FlattenIfTransparent(image), target, o.Quality, o.StripMetadata ? null : image.Metadata));
                    return target;

                case "tiff":
                    // Deflate is lossless; in lossy mode the saving comes from the resize
                    await Task.Run(() => ImageCodec.SaveTiff(image.Bitmap, target));
                    return target;

                case "png" when lossless:
                {
                    // Two candidates: Windows' encoder and FFmpeg's (max zlib, adaptive filters). Keep the smaller.
                    await Task.Run(() => ImageCodec.SavePng(image.Bitmap, target));
                    if (!string.IsNullOrEmpty(ffmpeg))
                    {
                        string alt = NewTempPath("png");
                        try
                        {
                            await MediaToolkit.RunAsync(ffmpeg, new[] { "-i", src, "-frames:v", "1", "-map_metadata", "-1", "-c:v", "png", "-compression_level", "9", "-pred", "mixed", alt });
                            if (File.Exists(alt) && new FileInfo(alt).Length < new FileInfo(target).Length) File.Copy(alt, target, true);
                        }
                        catch
                        {
                            // Keep the Windows-encoded PNG
                        }
                        finally
                        {
                            TryDelete(alt);
                        }
                    }
                    return target;
                }

                default:
                {
                    if (lossless && outExt == "webp" && image.BitsPerChannel > 8)
                    {
                        throw new InvalidOperationException("This is a 16-bit image and WebP stores 8 bits per channel, so it wouldn't be lossless. Choose PNG or JPEG XL.");
                    }

                    // WebP, AVIF, JPEG XL and palette PNG come from FFmpeg; feed it the oriented, resized pixels
                    string temp = NewTempPath("png");
                    try
                    {
                        // AVIF here is 4:2:0 without an alpha plane: put transparent areas on white instead of black
                        var pixels = outExt == "avif" ? ImageCodec.FlattenIfTransparent(image) : image.Bitmap;
                        await Task.Run(() => ImageCodec.SavePng(pixels, temp));
                        await EncodeImageWithFFmpegAsync(RequireFFmpeg(ffmpeg), temp, target, outExt, o, 0, image.HasPartialAlpha);
                    }
                    finally
                    {
                        TryDelete(temp);
                    }
                    return target;
                }
            }
        }

        private static async Task EncodeImageWithFFmpegAsync(string ffmpeg, string input, string target, string outExt, CompressionOptions o, int maxDimension, bool partialAlpha = false)
        {
            bool lossless = o.Mode == CompressionMode.Lossless;
            var encoders = await MediaToolkit.GetEncodersAsync(ffmpeg);
            var args = new List<string> { "-i", input, "-frames:v", "1", "-map_metadata", "-1" };
            var filters = new List<string>();
            if (maxDimension > 0)
            {
                filters.Add($"scale='if(gte(iw,ih),min(iw,{maxDimension}),-2)':'if(gte(iw,ih),-2,min(ih,{maxDimension}))':flags=lanczos");
            }

            switch (outExt)
            {
                case "webp":
                    Require(encoders, "libwebp", "WebP");
                    args.AddRange(lossless
                        ? new[] { "-c:v", "libwebp", "-lossless", "1", "-compression_level", "5", "-quality", "90" }
                        : new[] { "-c:v", "libwebp", "-quality", o.Quality.ToString(CultureInfo.InvariantCulture), "-compression_level", "6" });
                    break;

                case "avif":
                    filters.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
                    if (encoders.Contains("libaom-av1"))
                    {
                        args.AddRange(new[] { "-c:v", "libaom-av1", "-still-picture", "1", "-crf", AvifCrf(o.Quality).ToString(CultureInfo.InvariantCulture), "-b:v", "0",
                                              "-cpu-used", "4", "-row-mt", "1", "-pix_fmt", "yuv420p" });
                    }
                    else
                    {
                        Require(encoders, "libsvtav1", "AVIF");
                        args.AddRange(new[] { "-c:v", "libsvtav1", "-crf", AvifCrf(o.Quality).ToString(CultureInfo.InvariantCulture), "-preset", "6", "-pix_fmt", "yuv420p" });
                    }
                    break;

                case "jxl":
                    Require(encoders, "libjxl", "JPEG XL");
                    args.AddRange(lossless
                        ? new[] { "-c:v", "libjxl", "-distance", "0", "-effort", "8" }
                        : new[] { "-c:v", "libjxl", "-distance", JxlDistance(o.Quality).ToString(CultureInfo.InvariantCulture), "-effort", "7" });
                    break;

                case "png" when !lossless && !partialAlpha:
                    // pngquant-style: best palette for this image, error-diffusion dithering, 8-bit PNG
                    int colors = PaletteColors(o.Quality);
                    string pre = filters.Count > 0 ? string.Join(",", filters) + "," : "";
                    filters.Clear();
                    args.AddRange(new[] { "-filter_complex", $"[0:v]{pre}split[a][b];[a]palettegen=max_colors={colors}:reserve_transparent=1:stats_mode=full[p];[b][p]paletteuse=dither=sierra2_4a:alpha_threshold=128",
                                          "-c:v", "png", "-compression_level", "9" });
                    break;

                case "png":
                    // Lossless PNG, or a lossy PNG whose soft transparency a palette would ruin: optimize only
                    args.AddRange(new[] { "-c:v", "png", "-compression_level", "9", "-pred", "mixed" });
                    break;

                case "jpg":
                    args.AddRange(new[] { "-c:v", "mjpeg", "-q:v", Math.Clamp((int)Math.Round(2 + (100 - o.Quality) * 0.29), 2, 31).ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuvj420p" });
                    break;

                default:
                    throw new InvalidOperationException($"Can't write .{outExt} images.");
            }

            if (filters.Count > 0)
            {
                args.Insert(args.IndexOf("-frames:v"), "-vf");
                args.Insert(args.IndexOf("-vf") + 1, string.Join(",", filters));
            }
            args.Add(target);
            await MediaToolkit.RunAsync(ffmpeg, args);
        }

        private static void Require(HashSet<string> encoders, string name, string what)
        {
            if (encoders.Count > 0 && !encoders.Contains(name))
            {
                throw new InvalidOperationException($"Your FFmpeg build has no {what} encoder ({name}). " + (OperatingSystem.IsWindows()
                    ? "Install the full build: winget install Gyan.FFmpeg"
                    : "Install your distribution's full FFmpeg package (on Fedora: ffmpeg from RPM Fusion instead of ffmpeg-free)."));
            }
        }

        // ---------- Animated GIF ----------

        private static async Task<string> CompressGifAsync(string src, CompressionOptions o, string ffmpeg, Func<string, string, string> targetFor, IProgress<double>? progress)
        {
            var info = await MediaToolkit.ProbeAsync(ffmpeg, src);
            string target = targetFor(o.Format == "webp" ? "webp" : "gif", "_compressed");
            var filters = new List<string>();
            if (o.Mode == CompressionMode.Lossy)
            {
                if (o.MaxFps > 0 && (info.Fps <= 0 || info.Fps > o.MaxFps + 0.5)) filters.Add($"fps={o.MaxFps}");
                if (o.MaxDimension > 0) filters.Add($"scale='if(gte(iw,ih),min(iw,{o.MaxDimension}),-2)':'if(gte(iw,ih),-2,min(ih,{o.MaxDimension}))':flags=lanczos");
            }
            string pre = filters.Count > 0 ? string.Join(",", filters) + "," : "";

            var args = new List<string> { "-i", src, "-map_metadata", "-1" };
            if (o.Format == "webp")
            {
                var encoders = await MediaToolkit.GetEncodersAsync(ffmpeg);
                Require(encoders, "libwebp_anim", "animated WebP");
                if (pre.Length > 0) args.AddRange(new[] { "-vf", pre.TrimEnd(',') });
                args.AddRange(o.Mode == CompressionMode.Lossless
                    ? new[] { "-c:v", "libwebp_anim", "-lossless", "1", "-compression_level", "5", "-quality", "90", "-loop", "0" }
                    : new[] { "-c:v", "libwebp_anim", "-quality", o.Quality.ToString(CultureInfo.InvariantCulture), "-compression_level", "4", "-loop", "0" });
            }
            else
            {
                // One palette for the whole clip; only changed rectangles are re-encoded per frame
                int colors = PaletteColors(o.Quality);
                args.AddRange(new[] { "-filter_complex", $"[0:v]{pre}split[a][b];[a]palettegen=max_colors={colors}:stats_mode=diff:reserve_transparent=1[p];[b][p]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle",
                                      "-loop", "0" });
            }
            args.Add(target);
            await MediaToolkit.RunAsync(ffmpeg, args, info.DurationSeconds, progress);
            return target;
        }

        // ---------- SVG ----------

        private static string CompressSvg(string src, CompressionOptions o, Func<string, string, string> targetFor)
        {
            string minified = MinifySvg(File.ReadAllText(src));
            if (o.Format == "svgz")
            {
                string target = targetFor("svgz", "");
                using var fs = File.Create(target);
                using var gz = new GZipStream(fs, CompressionLevel.SmallestSize);
                var bytes = new System.Text.UTF8Encoding(false).GetBytes(minified);
                gz.Write(bytes);
                return target;
            }
            string min = targetFor("svg", ".min");
            File.WriteAllText(min, minified, new System.Text.UTF8Encoding(false));
            return min;
        }

        public static string MinifySvg(string svg)
        {
            var doc = XDocument.Parse(svg, LoadOptions.None);
            string[] editorNamespaces =
            {
                "http://www.inkscape.org/namespaces/inkscape",
                "http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd",
                "http://ns.adobe.com/AdobeIllustrator/10.0/",
                "http://ns.adobe.com/Graphs/1.0/",
                "http://ns.adobe.com/AdobeSVGViewerExtensions/3.0/",
                "http://ns.adobe.com/Variables/1.0/",
                "http://ns.adobe.com/SaveForWeb/1.0/",
                "http://ns.adobe.com/Extensibility/1.0/",
                "http://www.bohemiancoding.com/sketch/ns"
            };

            doc.DescendantNodes().OfType<XComment>().ToList().ForEach(c => c.Remove());
            doc.DescendantNodes().OfType<XProcessingInstruction>().ToList().ForEach(p => p.Remove());
            doc.DocumentType?.Remove();
            doc.Descendants().Where(e => e.Name.LocalName == "metadata" || editorNamespaces.Contains(e.Name.NamespaceName)).ToList().ForEach(e => e.Remove());
            foreach (var el in doc.Descendants())
            {
                el.Attributes().Where(a => editorNamespaces.Contains(a.Name.NamespaceName) ||
                                           (a.IsNamespaceDeclaration && editorNamespaces.Contains(a.Value))).ToList().ForEach(a => a.Remove());
            }

            using var sw = new StringWriter(CultureInfo.InvariantCulture);
            using (var xw = System.Xml.XmlWriter.Create(sw, new System.Xml.XmlWriterSettings { OmitXmlDeclaration = true, Indent = false, NewLineHandling = System.Xml.NewLineHandling.None }))
            {
                doc.Save(xw);
            }
            return sw.ToString();
        }

        // ---------- Video ----------

        private static async Task<string> CompressVideoAsync(string src, CompressionOptions o, string ffmpeg, Func<string, string, string> targetFor, IProgress<double>? progress)
        {
            var info = await MediaToolkit.ProbeAsync(ffmpeg, src);
            if (!info.HasVideo) throw new InvalidOperationException("This file has no video stream.");
            var encoders = await MediaToolkit.GetEncodersAsync(ffmpeg);
            string srcExt = Ext(src);
            var args = new List<string> { "-i", src };

            if (o.Mode == CompressionMode.Lossless)
            {
                if (o.Format == "remux")
                {
                    string remuxTarget = targetFor(srcExt, "_compressed");
                    args.AddRange(new[] { "-map", "0:v", "-map", "0:a?", "-map", "0:s?", "-c", "copy" });
                    if (o.StripMetadata) args.AddRange(new[] { "-map_metadata", "-1" });
                    if (srcExt is "mp4" or "m4v" or "mov") args.AddRange(new[] { "-movflags", "+faststart" });
                    args.Add(remuxTarget);
                    await MediaToolkit.RunAsync(ffmpeg, args, info.DurationSeconds, progress);
                    return remuxTarget;
                }

                string target = targetFor("mkv", "_lossless");
                args.AddRange(new[] { "-map", "0:V:0", "-map", "0:a?", "-map", "0:s?", "-c:s", "copy" });
                switch (o.Format)
                {
                    case "ffv1":
                        args.AddRange(new[] { "-c:v", "ffv1", "-level", "3", "-g", "1", "-slices", "16", "-slicecrc", "1" });
                        break;
                    case "x265":
                        Require(encoders, "libx265", "H.265");
                        args.AddRange(new[] { "-c:v", "libx265", "-preset", X26xPreset(o.Speed), "-x265-params", "lossless=1:log-level=error" });
                        break;
                    default:
                        Require(encoders, "libx264", "H.264");
                        args.AddRange(new[] { "-c:v", "libx264", "-preset", X26xPreset(o.Speed), "-qp", "0" });
                        break;
                }
                // Uncompressed PCM is the one audio worth shrinking losslessly; everything else is copied
                args.AddRange(info.HasLosslessAudio ? new[] { "-c:a", "flac", "-compression_level", "8" } : new[] { "-c:a", "copy" });
                if (o.StripMetadata) args.AddRange(new[] { "-map_metadata", "-1" });
                args.Add(target);
                await MediaToolkit.RunAsync(ffmpeg, args, info.DurationSeconds, progress);
                return target;
            }

            // ---- Lossy ----
            string codec = o.Format is "h265" or "av1" or "vp9" ? o.Format : "h264";
            string container = codec == "vp9" ? "webm" : srcExt == "mkv" ? "mkv" : "mp4";
            string outPath = targetFor(container, "_compressed");

            string? hw = null;
            if (o.UseHardwareEncoder && codec != "vp9")
            {
                var working = await MediaToolkit.GetWorkingHardwareEncodersAsync(ffmpeg);
                string prefix = codec switch { "h265" => "hevc", "av1" => "av1", _ => "h264" };
                hw = new[] { "_nvenc", "_qsv", "_amf" }.Select(s => prefix + s).FirstOrDefault(working.Contains);
            }

            string encoder = hw ?? codec switch
            {
                "h265" => "libx265",
                "av1" => encoders.Count == 0 || encoders.Contains("libsvtav1") ? "libsvtav1" : "libaom-av1",
                "vp9" => "libvpx-vp9",
                _ => "libx264"
            };
            if (hw == null) Require(encoders, encoder, codec.ToUpperInvariant());

            // Filters: cap the short edge (works for portrait and landscape) and the frame rate
            var filters = new List<string>();
            if (o.MaxHeight > 0 && Math.Min(info.Width, info.Height) > o.MaxHeight)
            {
                int h = o.MaxHeight;
                filters.Add($"scale='if(gte(iw,ih),-2,{h})':'if(gte(iw,ih),{h},-2)':flags=lanczos");
            }
            if (o.MaxFps > 0 && info.Fps > o.MaxFps + 0.5) filters.Add($"fps={o.MaxFps}");

            var common = new List<string> { "-map", "0:V:0" }; // V: real video, not cover art
            if (filters.Count > 0) common.AddRange(new[] { "-vf", string.Join(",", filters) });
            common.AddRange(new[] { "-c:v", encoder });
            common.AddRange(PixelFormatArgs(encoder, info));

            // Audio
            bool hasAudio = info.HasAudio && o.VideoAudioKbps >= 0;
            int audioKbps = !hasAudio ? 0 : o.VideoAudioKbps > 0 ? o.VideoAudioKbps : Math.Max(info.AudioKbps, 96);
            var audioArgs = new List<string>();
            if (!hasAudio) audioArgs.Add("-an");
            else
            {
                audioArgs.AddRange(new[] { "-map", "0:a" });
                bool canCopy = o.VideoAudioKbps == 0 && container switch
                {
                    "webm" => info.AudioCodec is "opus" or "vorbis",
                    "mp4" => info.AudioCodec is "aac" or "mp3" or "ac3" or "eac3" or "opus" or "alac",
                    _ => true
                };
                if (canCopy) audioArgs.AddRange(new[] { "-c:a", "copy" });
                else if (container == "webm") audioArgs.AddRange(new[] { "-c:a", "libopus", "-b:a", $"{audioKbps}k" });
                else audioArgs.AddRange(new[] { "-c:a", "aac", "-b:a", $"{audioKbps}k" });
            }
            if (container == "mkv") audioArgs.AddRange(new[] { "-map", "0:s?", "-c:s", "copy" });

            var tail = new List<string>();
            if (o.StripMetadata) tail.AddRange(new[] { "-map_metadata", "-1" });
            if (container == "mp4") tail.AddRange(new[] { "-movflags", "+faststart" });
            if (codec == "h265" && container == "mp4") tail.AddRange(new[] { "-tag:v", "hvc1" }); // so Apple devices play it

            if (o.TargetSizeMb > 0 && info.DurationSeconds > 0)
            {
                int videoKbps = TargetVideoKbps(o.TargetSizeMb, info.DurationSeconds, audioKbps);
                bool twoPass = encoder is "libx264" or "libx265" or "libvpx-vp9";
                var rate = new List<string> { "-b:v", $"{videoKbps}k" };
                if (hw != null || !twoPass) rate.AddRange(new[] { "-maxrate", $"{(int)(videoKbps * 1.5)}k", "-bufsize", $"{videoKbps * 2}k" });
                rate.AddRange(SpeedArgs(encoder, o.Speed));

                if (!twoPass)
                {
                    if (encoder == "libsvtav1") rate.AddRange(new[] { "-svtav1-params", "rc=1" });
                    await MediaToolkit.RunAsync(ffmpeg, args.Concat(common).Concat(rate).Concat(audioArgs).Concat(tail).Append(outPath), info.DurationSeconds, progress);
                    return outPath;
                }

                // Two-pass: the first pass measures the whole video so bits go where they're needed
                string workDir = Path.Combine(Path.GetTempPath(), "og-converter-2pass-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workDir);
                try
                {
                    List<string> Pass(int n) => encoder == "libx265"
                        ? new List<string> { "-x265-params", $"pass={n}:stats=x265.log:log-level=error" }
                        : new List<string> { "-pass", n.ToString(CultureInfo.InvariantCulture), "-passlogfile", "ffpass" };

                    var p1 = new Progress<double>(f => progress?.Report(f * 0.5));
                    var p2 = new Progress<double>(f => progress?.Report(0.5 + f * 0.5));
                    await MediaToolkit.RunAsync(ffmpeg, args.Concat(common).Concat(rate).Concat(Pass(1)).Concat(new[] { "-an", "-f", "null", "NUL" }),
                        info.DurationSeconds, p1, workDir);
                    await MediaToolkit.RunAsync(ffmpeg, args.Concat(common).Concat(rate).Concat(Pass(2)).Concat(audioArgs).Concat(tail).Append(outPath),
                        info.DurationSeconds, p2, workDir);
                }
                finally
                {
                    try { Directory.Delete(workDir, true); } catch { }
                }
                return outPath;
            }

            var quality = QualityArgs(encoder, o.Quality).Concat(SpeedArgs(encoder, o.Speed));
            await MediaToolkit.RunAsync(ffmpeg, args.Concat(common).Concat(quality).Concat(audioArgs).Concat(tail).Append(outPath), info.DurationSeconds, progress);
            return outPath;
        }

        /// <summary>Video bitrate that lands the file near the target size (4% container/overshoot margin).</summary>
        public static int TargetVideoKbps(double targetMb, double durationSeconds, int audioKbps)
        {
            double totalKbps = targetMb * 8 * 1024 * 0.96 / durationSeconds;
            return Math.Max(40, (int)(totalKbps - audioKbps));
        }

        private static string X26xPreset(int speed) => speed switch { 0 => "veryfast", 2 => "slow", 3 => "veryslow", _ => "medium" };

        private static IEnumerable<string> SpeedArgs(string encoder, int speed) => encoder switch
        {
            "libx264" or "libx265" => new[] { "-preset", X26xPreset(speed) },
            "libsvtav1" => new[] { "-preset", (speed switch { 0 => 10, 2 => 6, 3 => 4, _ => 8 }).ToString(CultureInfo.InvariantCulture) },
            "libaom-av1" => new[] { "-cpu-used", (speed switch { 0 => 8, 2 => 4, 3 => 3, _ => 6 }).ToString(CultureInfo.InvariantCulture), "-row-mt", "1" },
            "libvpx-vp9" => new[] { "-deadline", "good", "-cpu-used", (speed switch { 0 => 5, 2 => 2, 3 => 1, _ => 3 }).ToString(CultureInfo.InvariantCulture), "-row-mt", "1" },
            _ when encoder.EndsWith("_nvenc", StringComparison.Ordinal) => new[] { "-preset", speed switch { 0 => "p3", 2 => "p6", 3 => "p7", _ => "p5" } },
            _ when encoder.EndsWith("_qsv", StringComparison.Ordinal) => new[] { "-preset", X26xPreset(speed) },
            _ when encoder.EndsWith("_amf", StringComparison.Ordinal) => new[] { "-quality", speed == 0 ? "speed" : speed == 1 ? "balanced" : "quality" },
            _ => Array.Empty<string>()
        };

        private static IEnumerable<string> QualityArgs(string encoder, int q)
        {
            string S(int v) => v.ToString(CultureInfo.InvariantCulture);
            return encoder switch
            {
                "libx264" => new[] { "-crf", S(X264Crf(q)) },
                "libx265" => new[] { "-crf", S(X265Crf(q)), "-x265-params", "log-level=error" },
                "libsvtav1" => new[] { "-crf", S(Av1Crf(q)) },
                "libaom-av1" => new[] { "-crf", S(Av1Crf(q)), "-b:v", "0" },
                "libvpx-vp9" => new[] { "-crf", S(Vp9Crf(q)), "-b:v", "0" },
                _ when encoder.EndsWith("_nvenc", StringComparison.Ordinal) => new[] { "-rc", "vbr", "-cq", S(X264Crf(q) + 2), "-b:v", "0" },
                _ when encoder.EndsWith("_qsv", StringComparison.Ordinal) => new[] { "-global_quality", S(X264Crf(q) + 1) },
                _ when encoder.EndsWith("_amf", StringComparison.Ordinal) => new[] { "-rc", "cqp", "-qp_i", S(X264Crf(q) + 1), "-qp_p", S(X264Crf(q) + 3) },
                _ => Array.Empty<string>()
            };
        }

        private static IEnumerable<string> PixelFormatArgs(string encoder, MediaInfo info)
        {
            if (encoder.Contains("_nvenc") || encoder.Contains("_qsv") || encoder.Contains("_amf"))
            {
                return new[] { "-pix_fmt", "nv12" };
            }
            return encoder switch
            {
                "libx264" or "libvpx-vp9" => new[] { "-pix_fmt", "yuv420p" },   // 8-bit: plays on every device
                "libx265" => new[] { "-pix_fmt", info.Is10Bit ? "yuv420p10le" : "yuv420p" },
                _ => new[] { "-pix_fmt", "yuv420p10le" }                           // AV1 main profile: 10-bit compresses better
            };
        }

        // ---------- Audio ----------

        private static async Task<string> CompressAudioAsync(string src, CompressionOptions o, string ffmpeg, Func<string, string, string> targetFor, IProgress<double>? progress)
        {
            var info = await MediaToolkit.ProbeAsync(ffmpeg, src);
            if (!info.HasAudio) throw new InvalidOperationException("This file has no audio stream.");
            string srcExt = Ext(src);
            var args = new List<string> { "-i", src, "-map", "0:a:0" };
            string outExt;

            bool keepCover = !o.StripMetadata;
            switch (o.Mode == CompressionMode.Lossless ? o.Format : "lossy")
            {
                case "flac":
                    outExt = "flac";
                    args.AddRange(new[] { "-c:a", "flac", "-compression_level", "12", "-exact_rice_parameters", "1" });
                    break;
                case "alac":
                    outExt = "m4a";
                    args.AddRange(new[] { "-c:a", "alac" });
                    break;
                case "keep":
                    outExt = srcExt;
                    args.AddRange(new[] { "-c:a", "copy" });
                    keepCover = false;
                    o.StripMetadata = true;
                    break;
                default:
                    var encoders = await MediaToolkit.GetEncodersAsync(ffmpeg);
                    string kbps = $"{o.AudioKbps}k";
                    switch (o.Format)
                    {
                        case "opus":
                            Require(encoders, "libopus", "Opus");
                            outExt = "opus";
                            args.AddRange(new[] { "-c:a", "libopus", "-b:a", kbps, "-vbr", "on", "-compression_level", "10",
                                                  "-application", o.Mono && o.AudioKbps <= 48 ? "voip" : "audio" });
                            keepCover = false; // Ogg can't carry an attached picture stream
                            break;
                        case "mp3":
                            Require(encoders, "libmp3lame", "MP3");
                            outExt = "mp3";
                            args.AddRange(new[] { "-c:a", "libmp3lame", "-q:a", LameVbrLevel(o.AudioKbps).ToString(CultureInfo.InvariantCulture) });
                            break;
                        default:
                            outExt = "m4a";
                            args.AddRange(new[] { "-c:a", "aac", "-b:a", kbps });
                            break;
                    }
                    if (o.Mono) args.AddRange(new[] { "-ac", "1" });
                    if (o.SampleRate > 0 && o.Format != "opus") args.AddRange(new[] { "-ar", o.SampleRate.ToString(CultureInfo.InvariantCulture) });
                    break;
            }

            if (keepCover && outExt is "mp3" or "m4a" or "flac")
            {
                args.AddRange(new[] { "-map", "0:v?", "-c:v", "copy", "-disposition:v", "attached_pic" });
            }
            if (o.StripMetadata) args.AddRange(new[] { "-map_metadata", "-1" });
            if (outExt == "mp3") args.AddRange(new[] { "-id3v2_version", "3" });

            string target = targetFor(outExt, "_compressed");
            args.Add(target);
            await MediaToolkit.RunAsync(ffmpeg, args, info.DurationSeconds, progress);
            return target;
        }

        // ---------- PDF ----------

        private static async Task<string> CompressPdfAsync(string src, CompressionOptions o, Func<string, string, string> targetFor)
        {
            string target = targetFor("pdf", "_compressed");
            try
            {
                await Task.Run(() => PdfCompressor.Compress(src, target, o));
            }
            catch (Exception ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(target);
                throw new InvalidOperationException("This PDF is password-protected. Unlock it first, then compress.", ex);
            }
            return target;
        }

        // ---------- Archives ----------

        private static string CompressArchive(string src, CompressionOptions o, Func<string, string, string> targetFor)
        {
            if (Ext(src) == "zip")
            {
                string target = targetFor("zip", "_compressed");
                try
                {
                    using var input = ZipFile.OpenRead(src);
                    using var output = ZipFile.Open(target, ZipArchiveMode.Create);
                    foreach (var entry in input.Entries)
                    {
                        var e = output.CreateEntry(entry.FullName, entry.FullName.EndsWith('/') ? CompressionLevel.NoCompression : CompressionLevel.SmallestSize);
                        e.LastWriteTime = entry.LastWriteTime;
                        e.ExternalAttributes = entry.ExternalAttributes;
                        if (entry.FullName.EndsWith('/')) continue;
                        using var from = entry.Open();
                        using var to = e.Open();
                        from.CopyTo(to);
                    }
                }
                catch (InvalidDataException ex)
                {
                    TryDelete(target);
                    throw new InvalidOperationException(OperatingSystem.IsWindows()
                        ? "This ZIP is encrypted or uses a compression method Windows can't read, so it can't be re-packed."
                        : "This ZIP is encrypted or uses a compression method that can't be read, so it can't be re-packed.", ex);
                }
                return target;
            }

            // .tar → .tar.gz / .tar.br
            return CompressStream(src, o.Format == "br" ? "br" : "gz", targetFor);
        }

        // ---------- Anything else ----------

        private static string CompressGeneral(string src, CompressionOptions o, Func<string, string, string> targetFor)
        {
            switch (o.Format)
            {
                case "min":
                {
                    string target = targetFor("json", ".min");
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(src), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    using var fs = File.Create(target);
                    using var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                    doc.WriteTo(writer);
                    return target;
                }
                case "gz":
                case "br":
                    return CompressStream(src, o.Format, targetFor);
                default:
                {
                    string target = targetFor("zip", "");
                    using var archive = ZipFile.Open(target, ZipArchiveMode.Create);
                    archive.CreateEntryFromFile(src, Path.GetFileName(src), CompressionLevel.SmallestSize);
                    return target;
                }
            }
        }

        /// <summary>"report.txt" → "report.txt.gz" / "report.txt.br" (keeps the original extension, as gzip tools do).</summary>
        private static string CompressStream(string src, string kind, Func<string, string, string> targetFor)
        {
            // The target helper replaces the extension, so pass the full original name as the "base"
            string target = targetFor(Ext(src) + "." + kind, "");
            using var input = File.OpenRead(src);
            using var output = File.Create(target);
            using Stream compressor = kind == "br"
                ? new BrotliStream(output, CompressionLevel.SmallestSize)
                : new GZipStream(output, CompressionLevel.SmallestSize);
            input.CopyTo(compressor);
            return target;
        }

        /// <summary>One-line description of what will happen, for the dialog.</summary>
        public static string Describe(CompressionKind kind, string path, CompressionOptions o, MediaInfo? media = null, bool hardwareAvailable = true)
        {
            string ext = Ext(path);
            var parts = new List<string>();
            switch (kind)
            {
                case CompressionKind.Video when o.Mode == CompressionMode.Lossy:
                    string codec = o.Format switch { "h265" => "H.265", "av1" => "AV1", "vp9" => "VP9", _ => "H.264" };
                    parts.Add(codec + (o.UseHardwareEncoder && hardwareAvailable && o.Format != "vp9" ? " (GPU)" : ""));
                    if (o.TargetSizeMb > 0)
                    {
                        parts.Add($"target {o.TargetSizeMb:0.#} MB");
                        if (media?.DurationSeconds > 0)
                        {
                            int a = !media.HasAudio || o.VideoAudioKbps < 0 ? 0 : o.VideoAudioKbps > 0 ? o.VideoAudioKbps : Math.Max(media.AudioKbps, 96);
                            parts.Add($"≈{TargetVideoKbps(o.TargetSizeMb, media.DurationSeconds, a):N0} kbps video");
                        }
                    }
                    else
                    {
                        string crf = o.Format switch { "h265" => $"CRF {X265Crf(o.Quality)}", "av1" => $"CRF {Av1Crf(o.Quality)}", "vp9" => $"CRF {Vp9Crf(o.Quality)}", _ => $"CRF {X264Crf(o.Quality)}" };
                        parts.Add($"quality {o.Quality} ({crf})");
                    }
                    if (o.MaxHeight > 0) parts.Add($"max {o.MaxHeight}p");
                    if (o.MaxFps > 0) parts.Add($"max {o.MaxFps} fps");
                    parts.Add(o.VideoAudioKbps switch { < 0 => "no audio", 0 => "original audio", _ => $"{(o.Format == "vp9" ? "Opus" : "AAC")} {o.VideoAudioKbps} kbps" });
                    parts.Add("→ " + (o.Format == "vp9" ? "WEBM" : ext == "mkv" ? "MKV" : "MP4"));
                    break;

                case CompressionKind.Video:
                    parts.Add(o.Format switch { "ffv1" => "FFV1 lossless → MKV", "x264" => "H.264 lossless → MKV", "x265" => "H.265 lossless → MKV", _ => "Streams copied as-is, container cleaned" });
                    break;

                case CompressionKind.Audio when o.Mode == CompressionMode.Lossy:
                    parts.Add(o.Format switch
                    {
                        "opus" => $"Opus {o.AudioKbps} kbps VBR → .opus",
                        "mp3" => $"MP3 LAME V{LameVbrLevel(o.AudioKbps)} (≈{o.AudioKbps} kbps) → .mp3",
                        _ => $"AAC {o.AudioKbps} kbps → .m4a"
                    });
                    if (o.Mono) parts.Add("mono");
                    if (o.SampleRate > 0 && o.Format != "opus") parts.Add($"{o.SampleRate / 1000.0:0.##} kHz");
                    break;

                case CompressionKind.Audio:
                    parts.Add(o.Format switch { "flac" => "FLAC level 12 → .flac", "alac" => "Apple Lossless → .m4a", _ => "Audio copied bit-for-bit" });
                    break;

                case CompressionKind.Image or CompressionKind.AnimatedGif:
                    string fmt = o.Format == "keep" ? ext.ToUpperInvariant() : o.Format.ToUpperInvariant();
                    if (o.Format == "guetzli")
                    {
                        parts.Add($"JPEG (Guetzli), quality {Math.Max(o.Quality, GuetzliService.MinQuality)}");
                        if (o.MaxDimension > 0) parts.Add($"max {o.MaxDimension} px");
                        parts.Add("about a minute per megapixel");
                    }
                    else if (o.Mode == CompressionMode.Lossy)
                    {
                        string q = o.Format == "keep" && ext == "png" || o.Format == "gif" ? $"{PaletteColors(o.Quality)} colors" : $"quality {o.Quality}";
                        parts.Add($"{fmt}, {q}");
                        if (o.MaxDimension > 0) parts.Add($"max {o.MaxDimension} px");
                        if (kind == CompressionKind.AnimatedGif && o.MaxFps > 0) parts.Add($"max {o.MaxFps} fps");
                    }
                    else parts.Add($"{fmt}, pixel-identical");
                    break;

                case CompressionKind.Pdf:
                    parts.Add(o.Mode == CompressionMode.Lossy
                        ? $"Images ≤ {o.PdfDpi} dpi, JPEG quality {o.Quality}" + (o.Grayscale ? ", grayscale" : "")
                        : "Streams re-compressed, thumbnails and unused objects removed");
                    break;

                case CompressionKind.Office:
                    parts.Add(o.Mode == CompressionMode.Lossy
                        ? $"Pictures ≤ {o.MaxDimension} px, JPEG quality {o.Quality}"
                        : "Package re-zipped at maximum, PNGs optimized");
                    break;

                default:
                    parts.Add(o.Format switch
                    {
                        "gz" => "GZip, maximum compression",
                        "br" => "Brotli, maximum compression",
                        "min" => "Minified",
                        "svgz" => "Minified + gzip",
                        _ => "ZIP, maximum compression"
                    });
                    break;
            }
            if (o.StripMetadata && kind is not (CompressionKind.General or CompressionKind.Archive or CompressionKind.Svg))
            {
                parts.Add(kind == CompressionKind.Office ? "personal info removed" : "metadata removed");
            }
            return string.Join(" · ", parts);
        }
    }
}
