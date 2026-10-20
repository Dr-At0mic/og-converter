using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Zip;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using OgConverter.Models;
using ZstdSharp.Unsafe;

namespace OgConverter.Services
{
    /// <summary>One format the Archive action can write.</summary>
    /// <param name="Key">Also the file extension: "zip", "tar.zst", "gz"…</param>
    /// <param name="SingleFile">Compresses one file (gz, zst, ar…); folders need a tar.* or a container.</param>
    /// <param name="KeepsExtension">"report.pdf" → "report.pdf.gz", as the command-line tools name them.</param>
    public sealed record ArchiveFormat(string Key, string Title, string Description, bool SingleFile, bool KeepsExtension,
                                       bool CanEncrypt, int MinLevel, int MaxLevel, int DefaultLevel, string[] Methods)
    {
        public bool HasLevels => MaxLevel > MinLevel;

        /// <summary>The compressor inside: "gz" for both .gz and .tar.gz, "" for plain containers.</summary>
        public string Codec => Key.StartsWith("tar.", StringComparison.Ordinal) ? Key[4..] : Key is "gz" or "bz2" or "xz" or "zst" or "lz4" or "lz" or "br" ? Key : "";
    }

    /// <summary>
    /// Creates archives in every common format, for the Archive action and password-protected archives.
    /// ZIP, TAR and the gzip / bzip2 / zstd / LZ4 / Brotli / lzip codecs are written in-process; 7z, xz, cpio, ISO and ar
    /// go through the tar.exe in Windows (libarchive); 7-Zip is used for 7z when installed (passwords, all methods) and
    /// WinRAR's Rar.exe for RAR. Pictures can be re-encoded first (losslessly, or with Guetzli) on a staged copy.
    /// The source is never modified, and a failed run leaves no partial archive behind.
    /// </summary>
    public static class ArchiveWriter
    {
        private static readonly string[] NoMethods = Array.Empty<string>();

        public static readonly IReadOnlyList<ArchiveFormat> Formats = new List<ArchiveFormat>
        {
            new("zip", "ZIP", "Opens on every computer without extra software", false, false, true, 0, 9, 6, new[] { "deflate", "store" }),
            new("7z", "7z", "Usually the smallest; AES-256 passwords that hide file names", false, false, true, 0, 9, 5, new[] { "lzma2", "lzma", "ppmd", "bzip2", "deflate", "store" }),
            new("rar", "RAR", "WinRAR's format: recovery records, AES-256 passwords", false, false, true, 0, 5, 3, NoMethods),
            new("tar", "TAR", "Uncompressed bundle that keeps folders together (Linux, macOS)", false, false, false, 0, 0, 0, NoMethods),
            new("tar.gz", "tar.gz", "gzip: the standard on Linux and macOS", false, false, false, 1, 9, 6, NoMethods),
            new("tar.bz2", "tar.bz2", "bzip2: smaller than gzip, slower", false, false, false, 1, 9, 9, NoMethods),
            new("tar.xz", "tar.xz", "xz (LZMA2): very small, used for Linux software releases", false, false, false, 0, 9, 6, NoMethods),
            new("tar.zst", "tar.zst", "Zstandard: fast packing and unpacking with strong compression", false, false, false, 1, 22, 3, NoMethods),
            new("tar.lz4", "tar.lz4", "LZ4: the fastest, for backups and quick transfers", false, false, false, 1, 12, 1, NoMethods),
            new("tar.lz", "tar.lz", "lzip: LZMA in a format designed for long-term archiving", false, false, false, 0, 9, 6, NoMethods),
            new("tar.br", "tar.br", "Brotli: smallest for text, code and web files", false, false, false, 0, 11, 11, NoMethods),
            new("gz", "gz", "gzip one file (opens in 7-Zip, WinRAR, any Linux or macOS)", true, true, false, 1, 9, 6, NoMethods),
            new("bz2", "bz2", "bzip2 one file", true, true, false, 1, 9, 9, NoMethods),
            new("xz", "xz", "xz one file: high compression", true, true, false, 0, 9, 6, NoMethods),
            new("zst", "zst", "Zstandard one file: fast and strong", true, true, false, 1, 22, 3, NoMethods),
            new("lz4", "lz4", "LZ4 one file: the fastest", true, true, false, 1, 12, 1, NoMethods),
            new("lz", "lz", "lzip one file", true, true, false, 0, 9, 6, NoMethods),
            new("br", "br", "Brotli one file: best for text, code and data", true, true, false, 0, 11, 11, NoMethods),
            new("cpio", "cpio", "Unix cpio (newc), as used by Linux initramfs and RPM", false, false, false, 0, 0, 0, NoMethods),
            new("iso", "ISO", "Disc image (ISO 9660 + Joliet): opens in Windows with a double-click", false, false, false, 0, 0, 0, NoMethods),
            new("ar", "ar", "Unix ar archive of one file (the container of .deb packages)", true, false, false, 0, 0, 0, NoMethods),
        };

        public static ArchiveFormat Find(string key) =>
            Formats.FirstOrDefault(f => f.Key == key) ?? throw new InvalidOperationException($"Can't create .{key} archives.");

        /// <summary>Formats that can hold <paramref name="source"/> (folders need a container).</summary>
        public static IEnumerable<ArchiveFormat> FormatsFor(string source) =>
            Directory.Exists(source) ? Formats.Where(f => !f.SingleFile) : Formats;

        /// <summary>The level actually used: the saved one when it fits the format, otherwise the format's default.</summary>
        public static int LevelFor(ArchiveFormat f, ArchiveOptions o) =>
            o.Level >= f.MinLevel && o.Level <= f.MaxLevel ? o.Level : f.DefaultLevel;

        public static string MethodFor(ArchiveFormat f, ArchiveOptions o) =>
            f.Methods.Length == 0 ? "" : f.Methods.Contains(o.Method) ? o.Method : f.Methods[0];

        /// <summary>
        /// The path to hand to the output-name builder (which replaces the extension): folders and containers keep their
        /// name ("my.project" → "my.project.zip"), single-file codecs keep the original extension ("a.pdf" → "a.pdf.gz").
        /// </summary>
        public static string NamingPath(string source, ArchiveFormat f)
        {
            string clean = source.TrimEnd('\\', '/');
            return Directory.Exists(clean) ? clean + ".folder" : f.KeepsExtension ? clean + ".keep" : clean;
        }

        // ------------------------------------------------------------------ Tools

        /// <summary>The helper this format and these options need that isn't on this PC, or null when everything is there.</summary>
        public static Dependency? MissingTool(ArchiveFormat f, ArchiveOptions o)
        {
            if (o.Images == ImageRecompression.Guetzli && DependencyService.GuetzliPath == null) return Dependency.Guetzli;
            return f.Key switch
            {
                "rar" when DependencyService.RarPath == null => Dependency.WinRar,
                "7z" when DependencyService.SevenZipPath == null && (o.Password.Length > 0 || !ArchiveService.BsdTarCan7z(MethodFor(f, o))) => Dependency.SevenZip,
                "xz" or "tar.xz" when !ArchiveService.BsdTarHas("liblzma") && DependencyService.SevenZipPath == null => Dependency.SevenZip,
                _ => null
            };
        }

        /// <summary>Who does the work, for the dialog's summary line.</summary>
        public static string EngineName(ArchiveFormat f, ArchiveOptions o) => f.Key switch
        {
            "rar" => "WinRAR",
            "7z" => DependencyService.SevenZipPath != null ? "7-Zip" : "Windows tar",
            "xz" or "tar.xz" => ArchiveService.BsdTarHas("liblzma") ? "Windows tar" : "7-Zip",
            "lz" or "tar.lz" => ArchiveService.BsdTarHas("liblzma") ? "Windows tar" : "built in",
            "cpio" or "iso" or "ar" => "Windows tar",
            _ => "built in"
        };

        // ------------------------------------------------------------------ Create

        /// <summary>
        /// Packs <paramref name="source"/> (a file or a folder, which keeps its own name inside the archive) into
        /// <paramref name="target"/>. Reports 0–1 progress where the work can be measured.
        /// </summary>
        public static async Task CreateAsync(string source, string target, ArchiveOptions o, IProgress<double>? progress = null)
        {
            source = source.TrimEnd('\\', '/');
            var f = Find(o.Format);
            if (Directory.Exists(source) && f.SingleFile)
                throw new InvalidOperationException($".{f.Key} compresses a single file. For a folder choose tar.{(f.Key == "ar" ? "gz" : f.Key)}, ZIP or 7z.");
            if (o.Password.Length > 0 && !f.CanEncrypt)
                throw new InvalidOperationException($".{f.Key} can't hold a password. Choose ZIP, 7z or RAR.");
            if (MissingTool(f, o) is { } missing)
                throw new InvalidOperationException($"This needs {DependencyService.DisplayName(missing)}, which isn't installed.");

            string partial = target + ".partial";
            string? staging = null;
            try
            {
                if (o.Images != ImageRecompression.None)
                {
                    staging = Path.Combine(Path.GetTempPath(), "ogc-archive-" + Guid.NewGuid().ToString("N"));
                    var stageProgress = progress == null ? null : new Progress<double>(p => progress.Report(p * 0.8));
                    source = await StageWithOptimizedImagesAsync(source, staging, o, stageProgress);
                    progress = progress == null ? null : new Progress<double>(p => progress.Report(0.8 + p * 0.2));
                }

                TryDelete(partial);
                await WriteAsync(source, partial, f, o, progress);
                File.Move(partial, target, overwrite: true);
                progress?.Report(1);
            }
            finally
            {
                TryDelete(partial);
                if (staging != null) try { Directory.Delete(staging, true); } catch { }
            }
        }

        private static async Task WriteAsync(string source, string target, ArchiveFormat f, ArchiveOptions o, IProgress<double>? progress)
        {
            int level = LevelFor(f, o);
            bool lzmaInTar = ArchiveService.BsdTarHas("liblzma");
            switch (f.Key)
            {
                case "zip":
                    await Task.Run(() => WriteZip(source, target, o, level, MethodFor(f, o) == "store", Meter(source, progress)));
                    return;

                case "7z" when DependencyService.SevenZipPath is { } sevenZip:
                    await RunToolAsync(sevenZip, SevenZipArgs(source, target, o, level, MethodFor(f, o)), "7-Zip", okExitCodes: 1);
                    return;

                case "7z":
                {
                    string method = MethodFor(f, o) switch { "store" => "copy", "lzma" => "lzma1", var m => m };
                    await RunBsdTarAsync(source, target, "7zip", null, $"7zip:compression={method},7zip:compression-level={level}");
                    return;
                }

                case "rar":
                    await RunToolAsync(DependencyService.RarPath!, RarArgs(source, target, o, level), "WinRAR", okExitCodes: 1);
                    return;

                case "tar.xz" or "tar.lz" when lzmaInTar:
                    await RunBsdTarAsync(source, target, "pax", f.Codec == "xz" ? "--xz" : "--lzip", LzmaOptions(f.Codec, level, o));
                    return;

                case "xz" or "lz" when lzmaInTar:
                    await RunBsdTarAsync(source, target, "raw", f.Codec == "xz" ? "--xz" : "--lzip", LzmaOptions(f.Codec, level, o));
                    return;

                case "xz" or "tar.xz":
                {
                    // Older Windows tar has no xz: let 7-Zip do it (tar first, for folders)
                    string input = source;
                    string? tempTar = null;
                    if (f.Key == "tar.xz")
                    {
                        tempTar = Path.Combine(Path.GetTempPath(), $"ogc-{Guid.NewGuid():N}.tar");
                        await Task.Run(() => { using var fs = File.Create(tempTar); WriteTar(source, fs, Meter(source, progress)); });
                        input = tempTar;
                    }
                    try
                    {
                        var args = new List<string> { "a", "-txz", $"-mx={level}", o.MultiThreaded ? "-mmt=on" : "-mmt=off", "-bso0", "-bsp0", "-y", "--", target, input };
                        await RunToolAsync(DependencyService.SevenZipPath!, args, "7-Zip", okExitCodes: 1);
                    }
                    finally
                    {
                        if (tempTar != null) TryDelete(tempTar);
                    }
                    return;
                }

                case "cpio":
                    await RunBsdTarAsync(source, target, "newc", null, null);
                    return;

                case "iso":
                    await RunBsdTarAsync(source, target, "iso9660", null, $"iso9660:joliet,iso9660:rockridge,iso9660:volume-id={VolumeId(source)}", contentsAtRoot: true);
                    return;

                case "ar":
                    await RunBsdTarAsync(source, target, "ar", null, null);
                    return;

                case "tar":
                    await Task.Run(() => { using var fs = File.Create(target); WriteTar(source, fs, Meter(source, progress)); });
                    return;

                default:
                    // Managed codecs, as tar.* (any source) or on a single file
                    await Task.Run(() =>
                    {
                        using var fs = File.Create(target);
                        using (var codec = OpenCompressor(f.Codec, fs, o, level))
                        {
                            var meter = Meter(source, progress);
                            if (f.SingleFile)
                            {
                                using var input = File.OpenRead(source);
                                CopyWithProgress(input, codec, meter);
                            }
                            else
                            {
                                WriteTar(source, codec, meter);
                            }
                        }
                    });
                    return;
            }
        }

        // ------------------------------------------------------------------ ZIP and TAR (in-process)

        private readonly record struct Item(string Path, string Name, bool IsDirectory);

        /// <summary>Every file and folder, named relative to the source's parent so a folder keeps its own name.</summary>
        private static List<Item> Collect(string source)
        {
            if (!Directory.Exists(source)) return new() { new Item(source, Path.GetFileName(source), false) };

            string root = Path.GetFileName(source);
            var items = new List<Item> { new(source, root, true) };
            foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                items.Add(new Item(dir, Combine(root, Path.GetRelativePath(source, dir)), true));
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                items.Add(new Item(file, Combine(root, Path.GetRelativePath(source, file)), false));
            return items;

            static string Combine(string root, string relative) => root + "/" + relative.Replace('\\', '/');
        }

        private static void WriteZip(string source, string target, ArchiveOptions o, int level, bool store, Action<long> advance)
        {
            bool encrypt = o.Password.Length > 0;
            using var fs = File.Create(target);
            using var zip = new ZipOutputStream(fs);
            zip.SetLevel(store ? 0 : level);
            if (encrypt) zip.Password = o.Password;

            foreach (var item in Collect(source))
            {
                if (item.IsDirectory)
                {
                    // Folders are implied by the files in them; only empty ones need an entry (never encrypted ones,
                    // which would get a ZipCrypto header even in an AES archive)
                    if (!encrypt && !Directory.EnumerateFileSystemEntries(item.Path).Any())
                    {
                        zip.PutNextEntry(new ZipEntry(ZipEntry.CleanName(item.Name) + "/") { DateTime = Directory.GetLastWriteTime(item.Path), IsUnicodeText = true });
                        zip.CloseEntry();
                    }
                    continue;
                }
                var info = new FileInfo(item.Path);
                var entry = new ZipEntry(ZipEntry.CleanName(item.Name))
                {
                    DateTime = info.LastWriteTime,
                    Size = info.Length,
                    IsUnicodeText = true,
                    CompressionMethod = store ? CompressionMethod.Stored : CompressionMethod.Deflated,
                    // WinZip AES-256 (AE-2); 0 = traditional PKWARE ZipCrypto
                    AESKeySize = encrypt && o.ZipCipher == ZipCipher.Aes256 ? 256 : 0
                };
                zip.PutNextEntry(entry);
                using (var input = File.OpenRead(item.Path)) CopyWithProgress(input, zip, advance);
                zip.CloseEntry();
            }
            zip.Finish();
        }

        private static void WriteTar(string source, Stream output, Action<long> advance)
        {
            using var tar = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true);
            foreach (var item in Collect(source))
            {
                if (item.IsDirectory)
                {
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, item.Name + "/") { ModificationTime = Directory.GetLastWriteTimeUtc(item.Path) });
                    continue;
                }
                using var input = new CountingStream(File.OpenRead(item.Path), advance);
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, item.Name)
                {
                    ModificationTime = File.GetLastWriteTimeUtc(item.Path),
                    DataStream = input
                });
            }
        }

        private static Stream OpenCompressor(string codec, Stream output, ArchiveOptions o, int level)
        {
            switch (codec)
            {
                case "gz":
                    return new GZipStream(output, new ZLibCompressionOptions { CompressionLevel = level }, leaveOpen: true);
                case "bz2":
                    return new BZip2OutputStream(output, Math.Clamp(level, 1, 9)) { IsStreamOwner = false };
                case "zst":
                {
                    var zstd = new ZstdSharp.CompressionStream(output, level, 0, true);
                    if (o.MultiThreaded) TrySet(zstd, ZSTD_cParameter.ZSTD_c_nbWorkers, Environment.ProcessorCount);
                    if (o.LongRange)
                    {
                        // 128 MiB window: the largest every zstd decoder accepts without extra flags
                        zstd.SetParameter(ZSTD_cParameter.ZSTD_c_enableLongDistanceMatching, 1);
                        zstd.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, 27);
                    }
                    return zstd;
                }
                case "lz4":
                    return LZ4Stream.Encode(output, new LZ4EncoderSettings { CompressionLevel = level < 3 ? LZ4Level.L00_FAST : (LZ4Level)level }, leaveOpen: true);
                case "br":
                    return new BrotliWriteStream(output, level, Math.Clamp(o.BrotliWindow, 10, 24));
                case "lz":
                    // Only when Windows tar has no liblzma; the managed encoder has one fixed level
                    return SharpCompress.Compressors.LZMA.LZipStream.Create(output, SharpCompress.Compressors.CompressionMode.Compress, true);
                default:
                    throw new InvalidOperationException($"Unknown compressor {codec}.");
            }

            static void TrySet(ZstdSharp.CompressionStream s, ZSTD_cParameter p, int value)
            {
                try { s.SetParameter(p, value); } catch (ZstdSharp.ZstdException) { /* single-threaded build */ }
            }
        }

        // ------------------------------------------------------------------ External tools

        private static List<string> SevenZipArgs(string source, string target, ArchiveOptions o, int level, string method)
        {
            string m = method switch { "lzma" => "LZMA", "ppmd" => "PPMd", "bzip2" => "BZip2", "deflate" => "Deflate", "store" => "Copy", _ => "LZMA2" };
            var args = new List<string> { "a", "-t7z", $"-mx={level}", $"-m0={(level == 0 ? "Copy" : m)}", o.Solid ? "-ms=on" : "-ms=off", o.MultiThreaded ? "-mmt=on" : "-mmt=off" };
            if (o.Password.Length > 0)
            {
                args.Add("-p" + o.Password);
                args.Add(o.EncryptNames ? "-mhe=on" : "-mhe=off");
            }
            // -ssw: include files another app has open; -bso0/-bsp0: no console output
            args.AddRange(new[] { "-ssw", "-bso0", "-bsp0", "-y", "--", target, source });
            return args;
        }

        private static List<string> RarArgs(string source, string target, ArchiveOptions o, int level)
        {
            // -ep1: names start at the file or folder itself; -ma5: RAR5 (AES-256)
            var args = new List<string> { "a", "-ma5", $"-m{level}", "-ep1", "-r", o.Solid ? "-s" : "-s-", "-idq", "-y" };
            if (!o.MultiThreaded) args.Add("-mt1");
            if (o.RecoveryPercent > 0) args.Add($"-rr{o.RecoveryPercent}%");
            if (o.Password.Length > 0) args.Add((o.EncryptNames ? "-hp" : "-p") + o.Password);
            args.AddRange(new[] { "--", target, source });
            return args;
        }

        private static string LzmaOptions(string codec, int level, ArchiveOptions o)
        {
            string name = codec == "xz" ? "xz" : "lzip";
            string options = $"{name}:compression-level={level}";
            if (codec == "xz" && o.MultiThreaded) options += ",xz:threads=0";
            return options;
        }

        /// <summary>ISO 9660 volume labels: up to 32 of A–Z, 0–9 and underscore.</summary>
        private static string VolumeId(string source)
        {
            var sb = new StringBuilder();
            foreach (char c in Path.GetFileNameWithoutExtension(source).ToUpperInvariant())
            {
                if (sb.Length == 32) break;
                sb.Append(c is >= 'A' and <= 'Z' or >= '0' and <= '9' ? c : '_');
            }
            return sb.Length > 0 ? sb.ToString() : "ARCHIVE";
        }

        /// <param name="contentsAtRoot">Put a folder's contents at the root (disc images) instead of the folder itself.</param>
        private static async Task RunBsdTarAsync(string source, string target, string format, string? filter, string? options, bool contentsAtRoot = false)
        {
            string tar = ArchiveService.BsdTarPath ?? throw new InvalidOperationException("This format needs Windows 10 (1803) or later, which includes tar.exe.");
            var args = new List<string> { "-c", "-f", target, "--format", format };
            if (filter != null) args.Add(filter);
            if (options != null) args.AddRange(new[] { "--options", options });

            if (contentsAtRoot && Directory.Exists(source))
            {
                var children = Directory.EnumerateFileSystemEntries(source).Select(Path.GetFileName).ToList();
                if (children.Count == 0) throw new InvalidOperationException("The folder is empty.");
                args.AddRange(new[] { "-C", source, "--" });
                args.AddRange(children!);
            }
            else
            {
                args.AddRange(new[] { "-C", Path.GetDirectoryName(source)!, "--", Path.GetFileName(source) });
            }
            await RunToolAsync(tar, args, "tar");
        }

        /// <summary>Runs a console tool hidden; throws with its error output unless it exits with 0 (or ≤ <paramref name="okExitCodes"/>).</summary>
        /// <summary>Tools still working. Quitting OG Converter stops them, so a long Guetzli or 7-Zip run doesn't keep going unseen.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Process, byte> Running = new();

        static ArchiveWriter()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                foreach (var p in Running.Keys)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                }
            };
        }

        internal static async Task RunToolAsync(string exe, IEnumerable<string> args, string what, int okExitCodes = 0)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true // a tool that would prompt (overwrite, password) reads end-of-input instead of hanging
            };
            foreach (string a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Couldn't start {what}.");
            Running.TryAdd(p, 0);
            string errors, output;
            try
            {
                p.StandardInput.Close();
                var stdout = p.StandardOutput.ReadToEndAsync();
                errors = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                output = await stdout;
            }
            finally
            {
                Running.TryRemove(p, out _);
            }
            if (p.ExitCode > okExitCodes)
            {
                string reason = (errors.Trim().Length > 0 ? errors : output).Trim();
                string last = reason.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? $"exit code {p.ExitCode}";
                throw new InvalidOperationException($"{what} failed: {last}");
            }
        }

        // ------------------------------------------------------------------ Pictures

        /// <summary>Copies the source into <paramref name="staging"/> and re-encodes its pictures there. Returns the copy's path.</summary>
        private static async Task<string> StageWithOptimizedImagesAsync(string source, string staging, ArchiveOptions o, IProgress<double>? progress)
        {
            Directory.CreateDirectory(staging);
            string copy = Path.Combine(staging, Path.GetFileName(source));
            await Task.Run(() =>
            {
                if (!Directory.Exists(source))
                {
                    File.Copy(source, copy);
                    return;
                }
                foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                    Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(source, dir)));
                Directory.CreateDirectory(copy);
                foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    string dest = Path.Combine(copy, Path.GetRelativePath(source, file));
                    File.Copy(file, dest);
                    File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(file));
                }
            });

            var pictures = (Directory.Exists(copy) ? Directory.EnumerateFiles(copy, "*", SearchOption.AllDirectories) : new[] { copy })
                .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".jpg" or ".jpeg" || (o.Images == ImageRecompression.Lossless && Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (o.Images != ImageRecompression.Guetzli)
            {
                int done = 0;
                await Parallel.ForEachAsync(pictures, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (picture, _) =>
                {
                    await OptimizePictureAsync(picture, o, null);
                    progress?.Report((double)Interlocked.Increment(ref done) / pictures.Count);
                });
                return copy;
            }

            // Guetzli: the same plan the dialog showed (which pictures, how many at once). Largest first, so the long
            // ones don't end up alone at the end. Progress is weighted by size: one big photo can outlast a hundred small ones.
            var measured = pictures.Select(p => (Path: p, Megapixels: GuetzliService.Megapixels(p))).ToList();
            var plan = GuetzliService.Plan(measured.Select(p => p.Megapixels).ToList(), o.GuetzliJobs, o.GuetzliAllowLarge);
            double limit = plan.Included.Count > 0 ? plan.Included.Max() : 0;
            var work = measured.Where(p => p.Megapixels > 0 && p.Megapixels <= limit).OrderByDescending(p => p.Megapixels).ToList();
            if (work.Count == 0) return copy;
            double total = work.Sum(p => p.Megapixels);
            var share = new double[work.Count];
            void Report(int index, double fraction)
            {
                if (progress == null) return;
                lock (share)
                {
                    share[index] = fraction * work[index].Megapixels;
                    progress.Report(share.Sum() / total);
                }
            }

            await Parallel.ForEachAsync(Enumerable.Range(0, work.Count), new ParallelOptions { MaxDegreeOfParallelism = plan.Jobs }, async (i, _) =>
            {
                await OptimizePictureAsync(work[i].Path, o, new Progress<double>(f => Report(i, f)), plan.Jobs);
                Report(i, 1);
            });
            return copy;
        }

        /// <summary>Replaces <paramref name="path"/> with a smaller encoding when there is one. Pictures that fail stay as they were.</summary>
        private static async Task OptimizePictureAsync(string path, ArchiveOptions o, IProgress<double>? progress, int parallelJobs = 1)
        {
            string temp = path + ".ogc-tmp";
            try
            {
                bool jpeg = !Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase);
                if (o.Images == ImageRecompression.Guetzli)
                {
                    await GuetzliService.EncodeAsync(path, temp, o.GuetzliQuality, progress, allowLarge: true, parallelJobs);
                }
                else if (jpeg)
                {
                    ImageCodec.StripJpegMetadata(path, temp);
                }
                else
                {
                    var image = ImageCodec.TryLoad(path, applyOrientation: false, maxDimension: 0);
                    if (image == null) return;
                    ImageCodec.SavePng(image.Bitmap, temp);
                }

                if (File.Exists(temp) && new FileInfo(temp).Length < new FileInfo(path).Length)
                {
                    var modified = File.GetLastWriteTimeUtc(path);
                    File.Move(temp, path, overwrite: true);
                    File.SetLastWriteTimeUtc(path, modified);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException or ArgumentException or UnauthorizedAccessException)
            {
                // Unusual pictures (CMYK, damaged, huge) are packed unchanged
            }
            finally
            {
                TryDelete(temp);
            }
        }

        // ------------------------------------------------------------------ Helpers

        /// <summary>Turns bytes read into 0–1 progress over every file in the source.</summary>
        private static Action<long> Meter(string source, IProgress<double>? progress)
        {
            if (progress == null) return _ => { };
            long total = Directory.Exists(source)
                ? Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
                : new FileInfo(source).Length;
            long done = 0;
            int lastPercent = -1;
            return n =>
            {
                done += n;
                int percent = total > 0 ? (int)(done * 100 / total) : 100;
                if (percent == lastPercent) return;
                lastPercent = percent;
                progress.Report(Math.Min(percent, 99) / 100.0);
            };
        }

        private static void CopyWithProgress(Stream input, Stream output, Action<long> advance)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
            try
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    advance(read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>Read-through stream that reports how many bytes were read (TAR entries read their data themselves).</summary>
        private sealed class CountingStream : Stream
        {
            private readonly Stream _inner;
            private readonly Action<long> _advance;

            public CountingStream(Stream inner, Action<long> advance)
            {
                _inner = inner;
                _advance = advance;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = _inner.Read(buffer, offset, count);
                _advance(n);
                return n;
            }

            public override int Read(Span<byte> buffer)
            {
                int n = _inner.Read(buffer);
                _advance(n);
                return n;
            }

            public override bool CanRead => true;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => false;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>Brotli with a chosen window size, which BrotliStream doesn't expose.</summary>
        private sealed class BrotliWriteStream : Stream
        {
            private readonly Stream _output;
            private BrotliEncoder _encoder;
            private readonly byte[] _buffer = new byte[1 << 16];
            private bool _finished;

            public BrotliWriteStream(Stream output, int quality, int window)
            {
                _output = output;
                _encoder = new BrotliEncoder(quality, window);
            }

            public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

            public override void Write(ReadOnlySpan<byte> source)
            {
                while (true)
                {
                    var status = _encoder.Compress(source, _buffer, out int consumed, out int written, isFinalBlock: false);
                    if (status == System.Buffers.OperationStatus.InvalidData) throw new InvalidDataException("Brotli compression failed.");
                    _output.Write(_buffer, 0, written);
                    source = source[consumed..];
                    if (status != System.Buffers.OperationStatus.DestinationTooSmall && source.IsEmpty) return;
                }
            }

            private void Finish()
            {
                if (_finished) return;
                _finished = true;
                System.Buffers.OperationStatus status;
                do
                {
                    status = _encoder.Compress(ReadOnlySpan<byte>.Empty, _buffer, out _, out int written, isFinalBlock: true);
                    _output.Write(_buffer, 0, written);
                } while (status == System.Buffers.OperationStatus.DestinationTooSmall);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Finish();
                    _encoder.Dispose();
                }
                base.Dispose(disposing);
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
