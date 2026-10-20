using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace OgConverter.Services
{
    /// <summary>What this PC has to offer Guetzli, read fresh each time (free memory changes).</summary>
    public sealed record SystemSpecs(string Cpu, int Threads, long TotalRamBytes, long FreeRamBytes)
    {
        public string Describe() => $"{Cpu} · {Threads} threads · {Gb(TotalRamBytes)} RAM ({Gb(FreeRamBytes)} free)";
        public static string Gb(double bytes) => $"{bytes / (1L << 30):0.#} GB";
    }

    /// <summary>
    /// How a set of pictures would be encoded: which ones, how many at once, how long and how much memory.
    /// </summary>
    /// <param name="Included">Megapixels of the pictures that will be encoded.</param>
    /// <param name="Skipped">Pictures over the safe size that will be packed unchanged.</param>
    /// <param name="PeakBytes">Worst case: the <paramref name="Jobs"/> largest pictures encoding at the same time.</param>
    public sealed record GuetzliPlan(IReadOnlyList<double> Included, int Skipped, int Jobs, double Seconds, double PeakBytes, SystemSpecs Specs)
    {
        /// <summary>0–1+ of the memory that's free now; above 1 Windows starts paging to disk and everything slows down.</summary>
        public double MemoryLoad => Specs.FreeRamBytes > 0 ? PeakBytes / Specs.FreeRamBytes : 1;
    }

    /// <summary>
    /// Google's Guetzli JPEG encoder: JPEGs 20–30% smaller than libjpeg at the same visual quality, compatible with every
    /// JPEG decoder. Single-threaded, slow (~22 s per megapixel) and memory-hungry (~240 MB per megapixel), so pictures
    /// run in parallel only as far as the free memory allows, and pictures bigger than the free memory are skipped
    /// unless the user asks for them anyway.
    /// </summary>
    public static class GuetzliService
    {
        /// <summary>Guetzli refuses anything lower: below 84 its psychovisual model isn't reliable.</summary>
        public const int MinQuality = 84;

        // Measured on a Ryzen 9 9900X: 1 MP 28 s / 253 MB, 4 MP 89 s / 952 MB. Rounded up for slower PCs.
        private const double SecondsPerMegapixel = 25;
        private const double BytesPerMegapixel = 250e6;
        private const double BaseBytes = 50e6;
        /// <summary>Parallel jobs share memory bandwidth, so each one runs a little slower.</summary>
        private const double ParallelSlowdown = 0.04;

        /// <summary>How much of the free memory Guetzli may plan to use without the user's say-so.</summary>
        private const double SafeShareOfFreeMemory = 0.8;

        public static bool IsAvailable => DependencyService.GuetzliPath != null;

        public static double BytesFor(double megapixels) => BaseBytes + megapixels * BytesPerMegapixel;

        // ------------------------------------------------------------------ PC specs

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

        private static readonly Lazy<string> CpuName = new(() =>
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return (key?.GetValue("ProcessorNameString") as string)?.Trim() is { Length: > 0 } name ? name : "CPU";
            }
            catch
            {
                return "CPU";
            }
        });

        public static SystemSpecs Specs()
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            long total, free;
            if (GlobalMemoryStatusEx(ref status))
            {
                total = (long)status.TotalPhys;
                free = (long)status.AvailPhys;
            }
            else
            {
                total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                free = total / 2;
            }
            return new SystemSpecs(CpuName.Value, Environment.ProcessorCount, total, free);
        }

        /// <summary>The largest picture that fits in the memory that's free right now.</summary>
        public static double SafeMegapixels(SystemSpecs specs) =>
            Math.Max(0, (specs.FreeRamBytes * SafeShareOfFreeMemory - BaseBytes) / BytesPerMegapixel);

        /// <summary>Most parallel jobs worth offering: one per physical core (Guetzli gains nothing from SMT), up to 16.</summary>
        public static int MaxJobs(SystemSpecs specs) => Math.Clamp(specs.Threads / 2, 1, 16);

        // ------------------------------------------------------------------ Planning

        /// <param name="jobs">0 = automatic: as many as the cores and the free memory allow.</param>
        public static GuetzliPlan Plan(IReadOnlyList<double> megapixels, int jobs, bool allowLarge, SystemSpecs? specs = null)
        {
            specs ??= Specs();
            double safe = SafeMegapixels(specs);
            var included = megapixels.Where(mp => mp > 0 && (allowLarge || mp <= safe)).OrderByDescending(mp => mp).ToList();
            int skipped = megapixels.Count(mp => mp > 0) - included.Count;

            int maxJobs = MaxJobs(specs);
            if (jobs <= 0)
            {
                // Automatic: the most jobs whose worst case (the largest pictures together) fits the safe share of free memory
                jobs = 1;
                for (int j = maxJobs; j > 1; j--)
                {
                    if (included.Take(j).Sum(BytesFor) <= specs.FreeRamBytes * SafeShareOfFreeMemory) { jobs = j; break; }
                }
            }
            jobs = Math.Clamp(Math.Min(jobs, Math.Max(1, included.Count)), 1, maxJobs);

            // Largest first onto the least busy job: close to how Parallel.ForEach spreads the work
            var load = new double[jobs];
            double perJob = SecondsPerMegapixel * (1 + ParallelSlowdown * (jobs - 1));
            foreach (double mp in included)
            {
                int idle = Array.IndexOf(load, load.Min());
                load[idle] += mp * perJob;
            }
            double peak = included.Take(jobs).Sum(BytesFor);
            return new GuetzliPlan(included, skipped, jobs, load.Length > 0 ? load.Max() : 0, peak, specs);
        }

        /// <summary>"about 4 min" for the dialogs.</summary>
        public static string DescribeTime(double seconds) =>
            seconds < 60 ? "under a minute"
            : seconds < 3600 ? $"about {Math.Ceiling(seconds / 60):0} min"
            : $"about {seconds / 3600:0.#} h";

        /// <summary>Width × height in megapixels, read from the header only; 0 if it can't be read.</summary>
        public static double Megapixels(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                return frame.PixelWidth * (double)frame.PixelHeight / 1e6;
            }
            catch
            {
                return 0;
            }
        }

        // ------------------------------------------------------------------ Encoding

        /// <summary>
        /// Encodes a JPEG or PNG (8-bit, no transparency) as a Guetzli JPEG. Guetzli prints no progress, so
        /// <paramref name="progress"/> is estimated from the picture's size and the time spent (never past 95%).
        /// </summary>
        /// <param name="allowLarge">Encode even if the picture needs more memory than is free (Windows will page to disk).</param>
        public static async Task EncodeAsync(string input, string output, int quality, IProgress<double>? progress = null, bool allowLarge = false, int parallelJobs = 1)
        {
            string exe = DependencyService.GuetzliPath ?? throw new InvalidOperationException("Guetzli isn't installed.");
            double megapixels = Megapixels(input);
            var specs = Specs();
            if (!allowLarge && megapixels > SafeMegapixels(specs))
            {
                throw new InvalidOperationException($"This picture is {megapixels:0.#} megapixels and needs about {SystemSpecs.Gb(BytesFor(megapixels))} of memory; " +
                                                    $"{SystemSpecs.Gb(specs.FreeRamBytes)} is free. Tick \"Do it anyway\" or resize it first.");
            }

            string q = Math.Clamp(quality, MinQuality, 100).ToString(CultureInfo.InvariantCulture);
            using var ticker = new CancellationTokenSource();
            Task? estimate = null;
            if (progress != null && megapixels > 0)
            {
                double expected = Math.Max(1, megapixels * SecondsPerMegapixel * (1 + ParallelSlowdown * (parallelJobs - 1)));
                var clock = Stopwatch.StartNew();
                estimate = Task.Run(async () =>
                {
                    try
                    {
                        while (true)
                        {
                            progress.Report(Math.Min(0.95, clock.Elapsed.TotalSeconds / expected));
                            await Task.Delay(1000, ticker.Token);
                        }
                    }
                    catch (OperationCanceledException) { }
                });
            }

            try
            {
                // OG Converter checks the memory itself, so Guetzli's own fixed 6000 MB cap (which fails ~24 MP photos) is lifted
                await ArchiveWriter.RunToolAsync(exe, new[] { "--nomemlimit", "--quality", q, input, output }, "Guetzli");
            }
            finally
            {
                ticker.Cancel();
                if (estimate != null) await estimate;
            }
            progress?.Report(1);
        }
    }
}
