using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Tests
{
    /// <summary>Self-test for the Archive action: every format from a folder and a file, round-tripped, plus options and passwords.</summary>
    internal static class ArchiveVerification
    {
        public static async Task RunAsync(ConversionEngine engine, AppSettings settings, string tempDir, string testPng)
        {
            Console.WriteLine("[21] Archives");
            var action = FormatRegistry.GetWheelItems(tempDir, WheelMode.Actions).Find(a => a.Id == "act_zip") ?? throw new Exception("Archive action missing for folders");
            var unlock = new WheelItem { Id = "act_unlock", Title = "Unlock", Mode = WheelMode.Actions };

            // A folder with text, a picture, a subfolder, an empty folder and a non-ASCII name
            string folder = Path.Combine(tempDir, "Photo Album");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            Directory.CreateDirectory(Path.Combine(folder, "empty"));
            string text = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 4000));
            await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), text);
            await File.WriteAllTextAsync(Path.Combine(folder, "sub", "naïve ü.csv"), "a,b\n1,2");
            string jpeg = Path.Combine(folder, "photo.jpg");
            var image = ImageCodec.TryLoad(testPng, applyOrientation: false, maxDimension: 0) ?? throw new Exception("test image");
            ImageCodec.SaveJpeg(ImageCodec.FlattenIfTransparent(image), jpeg, 97, null);
            byte[] jpegBytes = await File.ReadAllBytesAsync(jpeg);

            async Task<string> Pack(string source, ArchiveOptions o, string label, bool expectSuccess = true)
            {
                var job = await engine.ExecuteAsync(source, action, settings, new ToolParameters { Archive = o });
                long size = File.Exists(job.TargetPath) ? new FileInfo(job.TargetPath).Length : 0;
                Console.WriteLine($"     {label,-30} Success={job.IsSuccess} {Path.GetFileName(job.TargetPath)} {size:N0} B {job.ErrorMessage}");
                if (job.IsSuccess != expectSuccess) throw new Exception($"Archive {label}: expected success={expectSuccess} ({job.ErrorMessage})");
                return job.TargetPath;
            }

            async Task<string> Extract(string archive)
            {
                string dest = Path.Combine(tempDir, "x-" + Guid.NewGuid().ToString("N")[..6]);
                await ArchiveService.ExtractAsync(archive, dest);
                return dest;
            }

            // --- Every container format, from the folder
            foreach (var f in ArchiveWriter.FormatsFor(folder))
            {
                var o = new ArchiveOptions { Format = f.Key };
                if (ArchiveWriter.MissingTool(f, o) is { } missing)
                {
                    Console.WriteLine($"     .{f.Key,-29} skipped ({DependencyService.DisplayName(missing)} not installed)");
                    continue;
                }
                string archive = await Pack(folder, o, $".{f.Key} from folder");
                if (!archive.EndsWith("Photo Album." + f.Key, StringComparison.Ordinal)) throw new Exception($".{f.Key}: unexpected name {archive}");

                string extracted = await Extract(archive);
                // Disc images hold the folder's contents at their root
                string root = f.Key == "iso" ? extracted : Path.Combine(extracted, "Photo Album");
                if (await File.ReadAllTextAsync(Path.Combine(root, "notes.txt")) != text) throw new Exception($".{f.Key}: notes.txt differs after extraction");
                if (await File.ReadAllTextAsync(Path.Combine(root, "sub", "naïve ü.csv")) != "a,b\n1,2") throw new Exception($".{f.Key}: non-ASCII file differs");
                if (!(await File.ReadAllBytesAsync(Path.Combine(root, "photo.jpg"))).SequenceEqual(jpegBytes)) throw new Exception($".{f.Key}: picture changed");
                ListWithWindowsTar(archive, f.Key);
                File.Delete(archive);
            }

            // --- Every format from a single file
            string single = Path.Combine(tempDir, "report.txt");
            await File.WriteAllTextAsync(single, text);
            foreach (var f in ArchiveWriter.Formats.Where(f => f.SingleFile))
            {
                string archive = await Pack(single, new ArchiveOptions { Format = f.Key }, $".{f.Key} from file");
                string expected = f.KeepsExtension ? "report.txt." + f.Key : "report." + f.Key;
                if (Path.GetFileName(archive) != expected) throw new Exception($".{f.Key}: expected {expected}, got {Path.GetFileName(archive)}");
                string extracted = await Extract(archive);
                if (await File.ReadAllTextAsync(Path.Combine(extracted, "report.txt")) != text) throw new Exception($".{f.Key}: content differs after extraction");
                ListWithWindowsTar(archive, f.Key);
                File.Delete(archive);
            }

            // --- Levels and options change the result
            long Size(string p) => new FileInfo(p).Length;
            string stored = await Pack(folder, new ArchiveOptions { Format = "zip", Method = "store" }, "ZIP store");
            string max = await Pack(folder, new ArchiveOptions { Format = "zip", Level = 9 }, "ZIP level 9");
            if (Size(max) >= Size(stored)) throw new Exception("ZIP level 9 isn't smaller than stored");
            string zstdFast = await Pack(folder, new ArchiveOptions { Format = "tar.zst", Level = 1 }, "tar.zst level 1");
            string zstdMax = await Pack(folder, new ArchiveOptions { Format = "tar.zst", Level = 19, LongRange = true }, "tar.zst level 19 long");
            if (Size(zstdMax) > Size(zstdFast)) throw new Exception("zstd 19 is bigger than zstd 1");
            if (await File.ReadAllTextAsync(Path.Combine(await Extract(zstdMax), "Photo Album", "notes.txt")) != text) throw new Exception("zstd long-range round-trip failed");
            await Pack(single, new ArchiveOptions { Format = "br", Level = 5, BrotliWindow = 16 }, "Brotli q5 window 16");
            if (ArchiveWriter.MissingTool(ArchiveWriter.Find("7z"), new ArchiveOptions { Format = "7z", Method = "lzma" }) == null)
                await Pack(folder, new ArchiveOptions { Format = "7z", Method = "lzma", Level = 9, Solid = false }, "7z LZMA non-solid");
            if (DependencyService.RarPath != null)
                await Pack(folder, new ArchiveOptions { Format = "rar", Level = 5, RecoveryPercent = 3 }, "RAR m5 + 3% recovery");
            foreach (string p in new[] { stored, max, zstdFast, zstdMax }) File.Delete(p);

            // --- Passwords from the Archive dialog's Advanced section
            foreach (string format in new[] { "zip", "7z", "rar" })
            {
                var o = new ArchiveOptions { Format = format, Password = "pack-pass" };
                if (ArchiveWriter.MissingTool(ArchiveWriter.Find(format), o) != null)
                {
                    Console.WriteLine($"     .{format,-29} password skipped (tool not installed)");
                    continue;
                }
                string archive = await Pack(folder, o, $".{format} with password");
                if (ProtectionService.DetectLock(archive) == ProtectionService.LockKind.None) throw new Exception($".{format}: not detected as protected");
                var job = await engine.ExecuteAsync(archive, unlock, settings, new ToolParameters { Password = "pack-pass" });
                if (!job.IsSuccess || await File.ReadAllTextAsync(Path.Combine(job.TargetPath, "Photo Album", "notes.txt")) != text)
                    throw new Exception($".{format}: password round-trip failed ({job.ErrorMessage})");
                File.Delete(archive);
            }

            // --- Refused choices
            await Pack(folder, new ArchiveOptions { Format = "gz" }, "gz on a folder (refused)", expectSuccess: false);
            await Pack(folder, new ArchiveOptions { Format = "tar.gz", Password = "nope" }, "tar.gz + password (refused)", expectSuccess: false);
            if (Directory.GetFiles(tempDir, "*.partial").Length > 0) throw new Exception("A failed archive left a .partial file behind");

            // --- Pictures re-encoded inside the archive; the originals stay untouched
            string lossless = await Pack(folder, new ArchiveOptions { Format = "zip", Images = ImageRecompression.Lossless }, "ZIP + lossless pictures");
            long optimized = new FileInfo(Path.Combine(await Extract(lossless), "Photo Album", "photo.jpg")).Length;
            if (optimized > jpegBytes.Length) throw new Exception("Lossless picture optimization grew the JPEG");
            if (GuetzliService.IsAvailable)
            {
                string guetzli = await Pack(folder, new ArchiveOptions { Format = "tar.zst", Images = ImageRecompression.Guetzli, GuetzliQuality = 90 }, "tar.zst + Guetzli q90");
                long reencoded = new FileInfo(Path.Combine(await Extract(guetzli), "Photo Album", "photo.jpg")).Length;
                Console.WriteLine($"     Guetzli: {jpegBytes.Length:N0} B → {reencoded:N0} B");
                if (reencoded >= jpegBytes.Length) throw new Exception("Guetzli didn't make the JPEG smaller");

                // A photo bigger than this PC's Guetzli limit is packed unchanged, right away, instead of running for half an hour
                string bigFolder = Path.Combine(tempDir, "Big Photos");
                Directory.CreateDirectory(bigFolder);
                string big = Path.Combine(bigFolder, "huge.jpg");
                double safeMp = GuetzliService.SafeMegapixels(GuetzliService.Specs());
                int side = (int)Math.Ceiling(Math.Sqrt((safeMp + 1) * 1e6));
                var pixels = Platform.Images.FromPixels(side, side, 96, RasterFormat.Gray8, new byte[side * side], side);
                ImageCodec.SaveJpeg(pixels, big, 90, null);
                byte[] bigBytes = await File.ReadAllBytesAsync(big);
                var clock = Stopwatch.StartNew();
                string bigArchive = await Pack(bigFolder, new ArchiveOptions { Format = "zip", Images = ImageRecompression.Guetzli }, $"Guetzli skips {side * (double)side / 1e6:0.#} MP");
                if (clock.Elapsed.TotalSeconds > 30) throw new Exception($"An over-limit picture still went to Guetzli ({clock.Elapsed.TotalSeconds:0} s)");
                if (!(await File.ReadAllBytesAsync(Path.Combine(await Extract(bigArchive), "Big Photos", "huge.jpg"))).SequenceEqual(bigBytes))
                    throw new Exception("The over-limit picture was changed");
                Console.WriteLine($"     Guetzli safe size with the memory free now: {safeMp:0.#} MP (skipped in {clock.Elapsed.TotalSeconds:0.#} s)");

                // Planner: jobs split the work, memory is the largest pictures running together, "do it anyway" includes everything
                var specs = new SystemSpecs("Test CPU", 16, 32L << 30, 16L << 30);
                var two = GuetzliService.Plan(new[] { 4.0, 4.0, 4.0, 4.0 }, 2, false, specs);
                var one = GuetzliService.Plan(new[] { 4.0, 4.0, 4.0, 4.0 }, 1, false, specs);
                if (two.Jobs != 2 || two.Seconds >= one.Seconds || Math.Abs(two.PeakBytes - 2 * GuetzliService.BytesFor(4)) > 1)
                    throw new Exception($"Guetzli plan is wrong: 2 jobs {two.Seconds:0} s / {two.PeakBytes:0} B vs 1 job {one.Seconds:0} s");
                var huge = new[] { 200.0 };
                if (GuetzliService.Plan(huge, 0, false, specs).Included.Count != 0 || GuetzliService.Plan(huge, 0, true, specs).Included.Count != 1)
                    throw new Exception("\"Do it anyway\" doesn't include the over-size picture");
                if (GuetzliService.Plan(new[] { 30.0, 30.0, 30.0 }, 0, false, specs).Jobs != 1) throw new Exception("Auto jobs ignore free memory");

                // Several pictures in parallel, for real
                string many = Path.Combine(tempDir, "Many Photos");
                Directory.CreateDirectory(many);
                for (int n = 0; n < 3; n++) File.Copy(jpeg, Path.Combine(many, $"p{n}.jpg"));
                string parallelArchive = await Pack(many, new ArchiveOptions { Format = "zip", Images = ImageRecompression.Guetzli, GuetzliJobs = 3 }, "Guetzli 3 jobs at once");
                string manyOut = Path.Combine(await Extract(parallelArchive), "Many Photos");
                if (Directory.GetFiles(manyOut).Any(p => new FileInfo(p).Length >= jpegBytes.Length)) throw new Exception("A picture wasn't re-encoded in the parallel run");

                // The same encoder as a Compress format for single pictures
                var compress = new WheelItem { Id = ConversionEngine.CompressToolId, Title = "Compress", Mode = WheelMode.Tools };
                var compressed = await engine.ExecuteAsync(jpeg, compress, settings, new ToolParameters
                {
                    Compression = new CompressionOptions { Mode = CompressionMode.Lossy, Format = "guetzli", Quality = 90 }
                });
                Console.WriteLine($"     Compress JPEG via Guetzli: Success={compressed.IsSuccess} {Path.GetFileName(compressed.TargetPath)} {compressed.ConvertedSizeBytes:N0} B {compressed.ErrorMessage}");
                if (!compressed.IsSuccess || !compressed.TargetPath.EndsWith(".jpg", StringComparison.Ordinal)) throw new Exception("Guetzli compression failed");
            }
            else
            {
                Console.WriteLine("     (Guetzli not installed: re-encoding skipped)");
            }
            if (!(await File.ReadAllBytesAsync(jpeg)).SequenceEqual(jpegBytes)) throw new Exception("Archiving modified the original picture");
        }

        /// <summary>Cross-checks our output with an independent reader: libarchive (Windows' tar.exe, bsdtar elsewhere) must list it.</summary>
        private static void ListWithWindowsTar(string archive, string format)
        {
            // Windows tar has no LZ4 or Brotli; raw single-file streams aren't archives to it
            if (ArchiveService.BsdTarPath == null || format is "lz4" or "tar.lz4" or "br" or "tar.br" or "gz" or "bz2" or "xz" or "zst" or "lz" or "rar") return;
            if (format is "tar.xz" or "tar.lz" && !ArchiveService.BsdTarHas("liblzma")) return;
            if (format == "tar.zst" && !ArchiveService.BsdTarHas("libzstd")) return;
            var psi = new ProcessStartInfo(ArchiveService.BsdTarPath, $"-tf \"{archive}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            string errors = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception($".{format}: Windows tar can't read it: {errors.Trim()}");
        }
    }
}
