using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Tests
{
    /// <summary>Several files at once: the job queue's limit, distinct output names for parallel jobs, and cancelling FFmpeg.</summary>
    public static class BatchVerification
    {
        public static async Task RunAsync(ConversionEngine engine, AppSettings settings, string tempDir)
        {
            Console.WriteLine("[24] Batch queue / cancel");

            // Video jobs never run more than VideoSlots at once; a cancelled wait gives up its turn
            int inside = 0, most = 0;
            var gate = new object();
            var holders = Enumerable.Range(0, 6).Select(async i =>
            {
                using var slot = await JobQueue.EnterAsync($"clip{i}.mp4");
                int now = Interlocked.Increment(ref inside);
                lock (gate) most = Math.Max(most, now);
                await Task.Delay(150);
                Interlocked.Decrement(ref inside);
            }).ToArray();
            using (var cts = new CancellationTokenSource(50))
            {
                bool cancelled = false;
                try { using var slot = await JobQueue.EnterAsync("late.mp4", cts.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                if (!cancelled) throw new Exception("A cancelled queue wait still got a slot");
            }
            await Task.WhenAll(holders);
            Console.WriteLine($"     Queue: 6 video jobs, at most {most} at once (limit {JobQueue.VideoSlots}); other work {JobQueue.OtherSlots}");
            if (most != JobQueue.VideoSlots) throw new Exception($"Queue ran {most} video jobs at once");

            // Cancelled before it starts
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var pre = await engine.ExecuteAsync(Path.Combine(tempDir, "test_input.png"),
                    new WheelItem { Id = "conv_jpg", Title = "JPG", Extension = "jpg", Mode = WheelMode.Convert }, settings, null, null, cts.Token);
                Console.WriteLine($"     Cancelled before start: Success={pre.IsSuccess} '{pre.ErrorMessage}'");
                if (pre.IsSuccess || pre.ErrorMessage != ConversionEngine.CancelledMessage) throw new Exception("Pre-cancelled job ran");
            }

            if (engine.FFmpegPath == null)
            {
                Console.WriteLine("     (FFmpeg missing: skipping parallel naming and cancel checks)");
                return;
            }

            // "clip.mov" and "clip.mkv" → MP4 at the same time must not both write "clip.mp4"
            string dir = Path.Combine(tempDir, "batch");
            Directory.CreateDirectory(dir);
            string mov = Path.Combine(dir, "clip.mov"), mkv = Path.Combine(dir, "clip.mkv");
            foreach (string src in new[] { mov, mkv })
            {
                await MediaToolkit.RunAsync(engine.FFmpegPath, new[] { "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=2",
                    "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", src });
            }
            var mp4 = new WheelItem { Id = "conv_mp4", Title = "MP4", Extension = "mp4", Mode = WheelMode.Convert };
            var both = await Task.WhenAll(engine.ExecuteAsync(mov, mp4, settings), engine.ExecuteAsync(mkv, mp4, settings));
            Console.WriteLine($"     Parallel same name: {string.Join(", ", both.Select(j => $"{Path.GetFileName(j.TargetPath)} Success={j.IsSuccess}"))}");
            if (both.Any(j => !j.IsSuccess || !File.Exists(j.TargetPath)) ||
                string.Equals(both[0].TargetPath, both[1].TargetPath, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Parallel jobs wrote the same output");

            // Cancelling mid-encode stops FFmpeg quickly and leaves no half-written file
            string longClip = Path.Combine(dir, "long.mp4");
            await MediaToolkit.RunAsync(engine.FFmpegPath, new[] { "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30:duration=120",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", longClip });
            var webm = new WheelItem { Id = "conv_webm", Title = "WEBM", Extension = "webm", Mode = WheelMode.Convert };
            using (var cts = new CancellationTokenSource())
            {
                var started = new TaskCompletionSource();
                var running = engine.ExecuteAsync(longClip, webm, settings, null, new SyncProgress(f => { if (f > 0) started.TrySetResult(); }), cts.Token);
                await Task.WhenAny(started.Task, Task.Delay(15_000));
                var sw = Stopwatch.StartNew();
                cts.Cancel();
                var job = await running;
                Console.WriteLine($"     Cancel mid-encode: Success={job.IsSuccess} '{job.ErrorMessage}' stopped in {sw.ElapsedMilliseconds} ms, " +
                                  $"output left: {File.Exists(job.TargetPath)}");
                if (job.IsSuccess || job.ErrorMessage != ConversionEngine.CancelledMessage) throw new Exception("Cancelled encode didn't report cancellation");
                if (File.Exists(job.TargetPath)) throw new Exception("Cancelled encode left its partial output");
                if (sw.ElapsedMilliseconds > 5000) throw new Exception("Cancel took too long to stop FFmpeg");
            }
        }
    }
}
