using System;
using System.Threading;
using System.Threading.Tasks;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>
    /// Limits how many jobs run at once, so dropping many files doesn't start one FFmpeg per file. One x264 encode
    /// already keeps most cores busy (4 × 1080p on 24 threads: 2 at once is 25% faster than one by one, 4 at once no
    /// faster than 2); VP9 threads less well and keeps gaining (2 at once 1.6×, 4 at once 2.2×). Video therefore gets
    /// 2 slots, 3 on 16+ threads, which also stays within the 3 encode sessions older GeForce drivers allow.
    /// Lighter work (images, audio, documents, PDFs) gets more slots.
    /// </summary>
    public static class JobQueue
    {
        /// <summary>Video sources running at once.</summary>
        public static int VideoSlots { get; } = Environment.ProcessorCount >= 16 ? 3 : 2;

        /// <summary>Everything else running at once.</summary>
        public static int OtherSlots { get; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

        private static readonly SemaphoreSlim Video = new(VideoSlots, VideoSlots);
        private static readonly SemaphoreSlim Other = new(OtherSlots, OtherSlots);

        /// <summary>
        /// Waits for a free slot for <paramref name="sourcePath"/>'s kind of work; dispose the result to release it.
        /// Throws <see cref="OperationCanceledException"/> if cancelled while waiting.
        /// </summary>
        public static async Task<IDisposable> EnterAsync(string sourcePath, CancellationToken cancellationToken = default)
        {
            var gate = FormatRegistry.DetectCategory(sourcePath) == FileCategory.Video ? Video : Other;
            await gate.WaitAsync(cancellationToken);
            return new Slot(gate);
        }

        private sealed class Slot : IDisposable
        {
            private SemaphoreSlim? _gate;
            public Slot(SemaphoreSlim gate) => _gate = gate;
            public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}
