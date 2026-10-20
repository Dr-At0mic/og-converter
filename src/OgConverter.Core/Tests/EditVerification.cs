using System;
using System.IO;
using System.Threading.Tasks;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Tests
{
    /// <summary>Crop (images, animated GIF, video), Trim (precise and fast) and SHA-256 results.</summary>
    public static class EditVerification
    {
        public static async Task RunAsync(ConversionEngine engine, AppSettings settings, string tempDir, string testPng, PlatformSelfTests platform)
        {
            Console.WriteLine("[20] Crop / Trim");

            // SHA-256 reports the hash instead of a fake size comparison
            var sha = await engine.ExecuteAsync(testPng, new WheelItem { Id = "tool_sha256", Title = "SHA-256", Mode = WheelMode.Tools }, settings);
            Console.WriteLine($"     SHA-256: Success={sha.IsSuccess} Detail='{sha.DetailText}'");
            if (!sha.IsSuccess || sha.ResultText?.Length != 64 || sha.SizeComparisonText != "") throw new Exception("SHA-256 result missing");

            // Images: every format that has a dedicated save path, plus WebP through FFmpeg
            foreach (string ext in new[] { "png", "jpg", "bmp", "tiff", "webp" })
            {
                string src = testPng;
                if (ext != "png")
                {
                    var conv = await engine.ExecuteAsync(testPng, new WheelItem { Id = $"conv_{ext}", Title = ext.ToUpperInvariant(), Extension = ext, Mode = WheelMode.Convert }, settings);
                    if (!conv.IsSuccess) { Console.WriteLine($"     (skip {ext}: {conv.ErrorMessage})"); continue; }
                    src = conv.TargetPath;
                }
                var job = await Crop(engine, settings, src, new PixelRect(10, 20, 50, 40));
                var size = ImageSize(job.TargetPath);
                Console.WriteLine($"     Crop {ext,-5} Success={job.IsSuccess} {Path.GetFileName(job.TargetPath)} {size.Width}x{size.Height} {job.ErrorMessage}");
                if (!job.IsSuccess || size != (50, 40) || !job.TargetPath.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Crop {ext} failed");
            }

            if (engine.FFmpegPath == null)
            {
                Console.WriteLine("     (FFmpeg missing: skipping GIF and video checks)");
                return;
            }

            // Animated GIF keeps its frames
            string gif = Path.Combine(tempDir, "anim.gif");
            await MediaToolkit.RunAsync(engine.FFmpegPath, new[] { "-f", "lavfi", "-i", "testsrc=size=160x120:rate=5:duration=1", gif });
            var gifJob = await Crop(engine, settings, gif, new PixelRect(20, 10, 80, 60));
            var gifInfo = await MediaToolkit.ProbeAsync(engine.FFmpegPath, gifJob.TargetPath);
            int frames = await CountFramesAsync(gifJob.TargetPath);
            Console.WriteLine($"     Crop gif   Success={gifJob.IsSuccess} {gifInfo.Width}x{gifInfo.Height}, {frames} frames {gifJob.ErrorMessage}");
            if (!gifJob.IsSuccess || gifInfo.Width != 80 || gifInfo.Height != 60 || frames < 2) throw new Exception("Animated GIF crop failed");

            // Video with sound, 6 s
            string video = Path.Combine(tempDir, "clip.mp4");
            await MediaToolkit.RunAsync(engine.FFmpegPath, new[] { "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=6",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=6", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", video });

            // Odd sizes are rounded down to even for 4:2:0
            var cropJob = await Crop(engine, settings, video, new PixelRect(11, 21, 101, 81));
            var cropInfo = await MediaToolkit.ProbeAsync(engine.FFmpegPath, cropJob.TargetPath);
            Console.WriteLine($"     Crop mp4   Success={cropJob.IsSuccess} {cropInfo.Width}x{cropInfo.Height} audio={cropInfo.HasAudio} {cropJob.ErrorMessage}");
            if (!cropJob.IsSuccess || cropInfo.Width != 100 || cropInfo.Height != 80 || !cropInfo.HasAudio) throw new Exception("Video crop failed");

            foreach (bool fast in new[] { false, true })
            {
                var p = new ToolParameters { TrimStart = 1.5, TrimEnd = 4, TrimFast = fast };
                var trim = await engine.ExecuteAsync(video, new WheelItem { Id = "tool_trim", Title = "Trim", Mode = WheelMode.Tools }, settings, p);
                var info = trim.IsSuccess ? await MediaToolkit.ProbeAsync(engine.FFmpegPath, trim.TargetPath) : new MediaInfo();
                Console.WriteLine($"     Trim {(fast ? "fast" : "exact"),-5} Success={trim.IsSuccess} {Path.GetFileName(trim.TargetPath)} {info.DurationSeconds:0.00}s audio={info.HasAudio} {trim.ErrorMessage}");
                // Stream copy snaps to a keyframe, so only the exact cut has a tight tolerance
                double tolerance = fast ? 2.0 : 0.15;
                if (!trim.IsSuccess || Math.Abs(info.DurationSeconds - 2.5) > tolerance || !info.HasAudio) throw new Exception($"Trim (fast={fast}) failed");
            }

            var bad = await engine.ExecuteAsync(video, new WheelItem { Id = "tool_trim", Title = "Trim", Mode = WheelMode.Tools }, settings,
                new ToolParameters { TrimStart = 3, TrimEnd = 3 });
            if (bad.IsSuccess) throw new Exception("An empty trim should be rejected");

            platform.EditDialogs?.Invoke();
        }

        private static Task<ConversionJob> Crop(ConversionEngine engine, AppSettings settings, string src, PixelRect rect) =>
            engine.ExecuteAsync(src, new WheelItem { Id = "tool_crop", Title = "Crop", Mode = WheelMode.Tools }, settings, new ToolParameters { Crop = rect });

        private static (int Width, int Height) ImageSize(string path)
        {
            var img = ImageCodec.TryLoad(path, applyOrientation: false, maxDimension: 0);
            return img == null ? (0, 0) : (img.Bitmap.Width, img.Bitmap.Height);
        }

        private static Task<int> CountFramesAsync(string gif) => Task.Run(() => Platform.Images.CountGifFrames(gif));
    }
}
