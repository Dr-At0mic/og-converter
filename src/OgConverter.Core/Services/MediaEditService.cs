using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>Crop (images and videos) and trim (videos), plus the preview frames their dialogs show.</summary>
    public static class MediaEditService
    {
        /// <summary>
        /// The image upright, exactly as Crop will cut it (crop rectangles are in these pixels), as plain pixels for the UI.
        /// Formats Windows can't decode go through FFmpeg. Null if neither can read it.
        /// </summary>
        public static Task<BgraImage?> LoadImageAsync(string path, string? ffmpeg) =>
            WithDecodedAsync<BgraImage>(path, ffmpeg, image => Platform.Images.ToBgra(image.Bitmap));

        /// <summary>
        /// One video frame at <paramref name="seconds"/>, rotated the way FFmpeg encodes it (so crop coordinates line up).
        /// <paramref name="maxWidth"/> &gt; 0 scales it down for quick previews.
        /// </summary>
        public static async Task<BgraImage?> GetVideoFrameAsync(string ffmpeg, string path, double seconds, int maxWidth = 0)
        {
            string temp = TempPng();
            try
            {
                var args = new List<string> { "-ss", Seconds(Math.Max(0, seconds)), "-i", path, "-frames:v", "1" };
                if (maxWidth > 0) args.AddRange(new[] { "-vf", $"scale='min({maxWidth},iw)':-2" });
                args.Add(temp);
                await MediaToolkit.RunAsync(ffmpeg, args);
                return await Task.Run(() => ImageCodec.TryLoad(temp, applyOrientation: false, maxDimension: 0) is { } image ? Platform.Images.ToBgra(image.Bitmap) : null);
            }
            catch
            {
                return null;
            }
            finally
            {
                TryDelete(temp);
            }
        }

        /// <summary>Crops an image and saves it in its own format (PNG when that format can't be written). Returns the output path.</summary>
        public static Task<string> CropImageAsync(string src, PixelRect rect, string? ffmpeg, Func<string, string, string> targetFor) =>
            EditImageAsync(src, ffmpeg, targetFor, "_cropped",
                bmp => Platform.Images.Crop(bmp, Clamp(rect, bmp.Width, bmp.Height)),
                // Animated GIFs have no EXIF rotation, so the coordinates are the stored pixels
                $"crop={rect.Width}:{rect.Height}:{rect.X}:{rect.Y}");

        /// <summary>Turns an image a quarter turn clockwise, in its own format. Phone photos turn from the way they're shown.</summary>
        public static Task<string> RotateImageAsync(string src, string? ffmpeg, Func<string, string, string> targetFor) =>
            EditImageAsync(src, ffmpeg, targetFor, "_rotated",
                bmp => Platform.Images.Rotate90(bmp),
                "transpose=1");

        /// <summary>
        /// Decodes the image upright, applies <paramref name="edit"/> and saves it in its own format (PNG when that format
        /// can't be written), keeping JPEG camera details. Animated GIFs go through FFmpeg with <paramref name="gifFilter"/>
        /// so every frame is kept.
        /// </summary>
        private static async Task<string> EditImageAsync(string src, string? ffmpeg, Func<string, string, string> targetFor, string suffix,
            Func<RasterImage, RasterImage> edit, string gifFilter)
        {
            string ext = Path.GetExtension(src).TrimStart('.').ToLowerInvariant();

            if (ext == "gif" && ffmpeg != null && await IsAnimatedGifAsync(src))
            {
                string gif = targetFor("gif", suffix);
                await MediaToolkit.RunAsync(ffmpeg, new[] { "-i", src, "-vf",
                    $"{gifFilter},split[a][b];[a]palettegen=reserve_transparent=1[p];[b][p]paletteuse", gif });
                return gif;
            }

            // Decode, edit and encode on one thread: decoded bitmaps can belong to the thread that made them
            // (WPF's do); the edited picture is ready to use anywhere
            string? intermediate = null;
            string saved = await WithDecodedAsync<string>(src, ffmpeg, image =>
            {
                var edited = edit(image.Bitmap);

                switch (ext)
                {
                    case "jpg" or "jpeg":
                    {
                        // Camera details are kept; the orientation tag is reset since the pixels are now upright
                        string target = targetFor(ext, suffix);
                        ImageCodec.SaveJpeg(edited, target, 95, image.Metadata);
                        return target;
                    }
                    case "png":
                    {
                        string target = targetFor(ext, suffix);
                        ImageCodec.SavePng(edited, target);
                        return target;
                    }
                    case "tif" or "tiff":
                    {
                        string target = targetFor(ext, suffix);
                        ImageCodec.SaveTiff(edited, target);
                        return target;
                    }
                    case "bmp" or "gif":
                    {
                        string target = targetFor(ext, suffix);
                        using var fs = File.Create(target);
                        Platform.Images.Encode(edited, fs, ext == "bmp" ? ImageEncoding.Bmp() : ImageEncoding.Gif());
                        return target;
                    }
                    default:
                        // WebP, AVIF, ICO…: Windows can't encode these, so FFmpeg re-encodes a lossless intermediate
                        intermediate = TempPng();
                        ImageCodec.SavePng(edited, intermediate);
                        return intermediate;
                }
            }) ?? throw new InvalidOperationException("This image format can't be read.");

            if (intermediate == null) return saved;
            try
            {
                if (ffmpeg != null)
                {
                    string target = targetFor(ext, suffix);
                    try
                    {
                        string[] codec = ext switch
                        {
                            "webp" => new[] { "-quality", "95" },
                            "avif" => new[] { "-c:v", "libsvtav1", "-crf", "20", "-pix_fmt", "yuv420p" },
                            _ => Array.Empty<string>()
                        };
                        await MediaToolkit.RunAsync(ffmpeg, new[] { "-i", intermediate }.Concat(codec).Append(target));
                        return target;
                    }
                    catch
                    {
                        TryDelete(target);
                    }
                }

                string png = targetFor("png", suffix);
                File.Copy(intermediate, png, true);
                return png;
            }
            finally
            {
                TryDelete(intermediate);
            }
        }

        /// <summary>
        /// Decodes the image upright and runs <paramref name="work"/> on it, on the decoding thread. Formats Windows
        /// can't read are first turned into a PNG by FFmpeg. Null if the image can't be read at all.
        /// </summary>
        private static async Task<T?> WithDecodedAsync<T>(string path, string? ffmpeg, Func<LoadedImage, T> work) where T : class
        {
            var result = await Task.Run(() => ImageCodec.TryLoad(path, applyOrientation: true, maxDimension: 0) is { } image ? work(image) : null);
            if (result != null || ffmpeg == null) return result;

            string temp = TempPng();
            try
            {
                try
                {
                    await MediaToolkit.RunAsync(ffmpeg, new[] { "-i", path, "-frames:v", "1", temp });
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
                return await Task.Run(() => ImageCodec.TryLoad(temp, applyOrientation: false, maxDimension: 0) is { } image ? work(image) : null);
            }
            finally
            {
                TryDelete(temp);
            }
        }

        /// <summary>Crops a video (re-encoded; audio is copied untouched). Returns the output path.</summary>
        public static async Task<string> CropVideoAsync(string ffmpeg, string src, PixelRect rect, int crf, Func<string, string, string> targetFor,
            IProgress<double>? progress)
        {
            string ext = Path.GetExtension(src).TrimStart('.').ToLowerInvariant();
            string target = targetFor(ext, "_cropped");
            var info = await MediaToolkit.ProbeAsync(ffmpeg, src);

            // 4:2:0 video needs even dimensions and offsets
            int w = Math.Max(2, rect.Width & ~1), h = Math.Max(2, rect.Height & ~1);
            int x = rect.X & ~1, y = rect.Y & ~1;

            var args = new List<string> { "-i", src, "-vf", $"crop={w}:{h}:{x}:{y}" };
            args.AddRange(VideoCodecArgs(ext, crf));
            args.AddRange(new[] { "-c:a", "copy", target });
            await MediaToolkit.RunAsync(ffmpeg, args, info.DurationSeconds, progress);
            return target;
        }

        /// <summary>Keeps <paramref name="start"/>–<paramref name="end"/> seconds of a video. Returns the output path.</summary>
        public static async Task<string> TrimVideoAsync(string ffmpeg, string src, double start, double end, bool fast, int crf,
            Func<string, string, string> targetFor, IProgress<double>? progress)
        {
            if (end - start < 0.05) throw new InvalidOperationException("The part to keep is empty: the end must come after the start.");

            string ext = Path.GetExtension(src).TrimStart('.').ToLowerInvariant();
            string target = targetFor(ext, $"_trim_{Stamp(start)}-{Stamp(end)}");
            double length = end - start;

            // Seeking before -i is frame-accurate when re-encoding, and keyframe-based with stream copy
            var args = new List<string> { "-ss", Seconds(start), "-i", src, "-t", Seconds(length), "-map", "0:v?", "-map", "0:a?" };
            if (fast)
            {
                args.AddRange(new[] { "-c", "copy", "-avoid_negative_ts", "make_zero" });
            }
            else
            {
                args.AddRange(VideoCodecArgs(ext, crf));
                args.AddRange(AudioCodecArgs(ext));
            }
            args.Add(target);

            await MediaToolkit.RunAsync(ffmpeg, args, length, progress);
            return target;
        }

        /// <summary>Turns a video a quarter turn clockwise (re-encoded, so every player shows it turned). Returns the output path.</summary>
        public static async Task<string> RotateVideoAsync(string ffmpeg, string src, int crf, Func<string, string, string> targetFor, IProgress<double>? progress)
        {
            string ext = Path.GetExtension(src).TrimStart('.').ToLowerInvariant();
            // Containers whose codecs FFmpeg writes well; anything else becomes an MP4
            if (ext is not ("mp4" or "mkv" or "mov" or "m4v" or "webm" or "avi" or "wmv")) ext = "mp4";
            string target = targetFor(ext, "_rotated");
            var info = await MediaToolkit.ProbeAsync(ffmpeg, src);

            // FFmpeg turns phone videos upright first (their rotation flag), so the turn starts from what the viewer sees
            var args = new List<string> { "-i", src, "-map", "0:v:0", "-map", "0:a?", "-vf", "transpose=1" };
            args.AddRange(VideoCodecArgs(ext, crf));
            args.AddRange(AudioCodecArgs(ext));
            args.Add(target);
            await MediaToolkit.RunAsync(ffmpeg, args, info.DurationSeconds, progress);
            return target;
        }

        /// <summary>Saves the frame from the middle of a video (or <paramref name="seconds"/> in) as a PNG. Returns the output path.</summary>
        public static async Task<string> SnapshotAsync(string ffmpeg, string src, Func<string, string, string> targetFor, double? seconds = null)
        {
            var info = await MediaToolkit.ProbeAsync(ffmpeg, src);
            double at = Math.Max(0, seconds ?? info.DurationSeconds / 2);
            string target = targetFor("png", $"_frame_{Stamp(at)}");
            await MediaToolkit.RunAsync(ffmpeg, new[] { "-ss", Seconds(at), "-i", src, "-frames:v", "1", "-update", "1", target });
            if (!File.Exists(target)) throw new InvalidOperationException("The video has no picture at that point.");
            return target;
        }

        private static string[] VideoCodecArgs(string ext, int crf) => ext switch
        {
            "webm" => new[] { "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-deadline", "good", "-cpu-used", "4", "-row-mt", "1", "-threads", "0" },
            "avi" => new[] { "-c:v", "mpeg4", "-q:v", "3" },
            "wmv" => new[] { "-c:v", "wmv2", "-q:v", "3" },
            "gif" => new[] { "-filter_complex", "split[a][b];[a]palettegen[p];[b][p]paletteuse" },
            _ => new[] { "-c:v", "libx264", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-preset", "fast", "-pix_fmt", "yuv420p" }
        };

        private static string[] AudioCodecArgs(string ext) => ext switch
        {
            "webm" => new[] { "-c:a", "libopus", "-b:a", "128k" },
            "avi" => new[] { "-c:a", "libmp3lame", "-b:a", "192k" },
            "wmv" => new[] { "-c:a", "wmav2", "-b:a", "192k" },
            "gif" => new[] { "-an" },
            _ => new[] { "-c:a", "aac", "-b:a", "192k" }
        };

        private static async Task<bool> IsAnimatedGifAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    return Platform.Images.CountGifFrames(path) > 1;
                }
                catch
                {
                    return false;
                }
            });
        }

        private static PixelRect Clamp(PixelRect r, int width, int height)
        {
            int x = Math.Clamp(r.X, 0, width - 1), y = Math.Clamp(r.Y, 0, height - 1);
            return new PixelRect(x, y, Math.Clamp(r.Width, 1, width - x), Math.Clamp(r.Height, 1, height - y));
        }

        private static string Seconds(double s) => s.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>"1m05.5s" style, for file names.</summary>
        private static string Stamp(double s)
        {
            var t = TimeSpan.FromSeconds(Math.Round(s, 1));
            string sec = (t.Seconds + t.Milliseconds / 1000.0).ToString(t.TotalMinutes >= 1 ? "00.#" : "0.#", CultureInfo.InvariantCulture);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}m{sec}s"
                 : t.TotalMinutes >= 1 ? $"{t.Minutes}m{sec}s"
                 : $"{sec}s";
        }

        private static string TempPng() => Path.Combine(Path.GetTempPath(), $"ogc_{Guid.NewGuid():N}.png");

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
