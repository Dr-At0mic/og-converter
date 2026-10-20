using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.BZip2;
using K4os.Compression.LZ4.Streams;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace OgConverter.Services
{
    /// <summary>
    /// Unpacks and re-packs archives. ZIP, TAR and the gzip / bzip2 / xz / zstd / LZ4 / Brotli / lzip codecs (alone or as
    /// tar.*) are handled in-process, 7z and RAR with SharpCompress; cpio, ISO, ar and CAB go through the tar.exe that
    /// ships with Windows 10 1803+ (bsdtar/libarchive). Creating archives is <see cref="ArchiveWriter"/>'s job.
    /// </summary>
    public static class ArchiveService
    {
        private static readonly Lazy<string?> BsdTar = new(() =>
        {
            // Elsewhere "tar" is usually GNU tar; libarchive's own is bsdtar
            if (!OperatingSystem.IsWindows()) return Platform.FindOnPath("bsdtar");
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            return File.Exists(path) ? path : null;
        });

        /// <summary>"bsdtar 3.8.8 - libarchive 3.8.8 zlib/… liblzma/5.8.1 bz2lib/1.0.8 libzstd/1.5.7": which codecs it was built with.</summary>
        private static readonly Lazy<string> BsdTarVersion = new(() =>
        {
            if (BsdTar.Value == null) return "";
            try
            {
                var psi = new ProcessStartInfo(BsdTar.Value, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using var p = Process.Start(psi)!;
                string text = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return text;
            }
            catch
            {
                return "";
            }
        });

        public static string? BsdTarPath => BsdTar.Value;

        /// <summary>True when Windows' tar.exe includes <paramref name="library"/> ("liblzma", "bz2lib", "libzstd"). Windows 10's older tar has only zlib.</summary>
        public static bool BsdTarHas(string library) => BsdTarVersion.Value.Contains(library + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether Windows' tar can write a 7z with this method (LZMA needs liblzma, bzip2 needs bz2lib).</summary>
        public static bool BsdTarCan7z(string method) => BsdTar.Value != null && method switch
        {
            "lzma2" or "lzma" => BsdTarHas("liblzma"),
            "bzip2" => BsdTarHas("bz2lib"),
            _ => true
        };

        private static readonly string[] DoubleExtensions =
            { ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst", ".tar.lz4", ".tar.lz", ".tar.br", ".tgz", ".tbz2", ".tbz", ".txz", ".tzst" };

        /// <summary>"backup.tar.gz" → "backup", "photos.zip" → "photos".</summary>
        public static string BaseName(string path)
        {
            string name = Path.GetFileName(path);
            foreach (string ext in DoubleExtensions)
            {
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return name[..^ext.Length];
            }
            return Path.GetFileNameWithoutExtension(name);
        }

        /// <summary>
        /// The in-process decompressor for a .gz / .bz2 / .xz / .zst / .lz4 / .br / .lz (or tar.* / .tgz…) file, or null
        /// when the file isn't one of those.
        /// </summary>
        private static Stream? OpenCodec(string path)
        {
            string name = Path.GetFileName(path).ToLowerInvariant();
            string? codec = name switch
            {
                _ when name.EndsWith(".gz") || name.EndsWith(".tgz") => "gz",
                _ when name.EndsWith(".bz2") || name.EndsWith(".tbz2") || name.EndsWith(".tbz") => "bz2",
                _ when name.EndsWith(".xz") || name.EndsWith(".txz") => "xz",
                _ when name.EndsWith(".zst") || name.EndsWith(".tzst") => "zst",
                _ when name.EndsWith(".lz4") => "lz4",
                _ when name.EndsWith(".br") => "br",
                _ when name.EndsWith(".lz") => "lz",
                _ => null
            };
            if (codec == null) return null;

            var file = File.OpenRead(path);
            Stream decoder = codec switch
            {
                "gz" => new GZipStream(file, CompressionMode.Decompress),
                "bz2" => new BZip2InputStream(file),
                "xz" => new SharpCompress.Compressors.Xz.XZStream(file),
                "zst" => new ZstdSharp.DecompressionStream(file),
                "lz4" => LZ4Stream.Decode(file),
                "br" => new BrotliStream(file, CompressionMode.Decompress),
                _ => SharpCompress.Compressors.LZMA.LZipStream.Create(file, SharpCompress.Compressors.CompressionMode.Decompress, false)
            };
            return new Owning(decoder, file);
        }

        /// <summary>Some decoders leave their input open when disposed; this closes both.</summary>
        private sealed class Owning : Stream
        {
            private readonly Stream _decoder;
            private readonly Stream _file;

            public Owning(Stream decoder, Stream file)
            {
                _decoder = decoder;
                _file = file;
            }

            // TarFile issues zero-length reads; SharpCompress's lzip decoder takes them for the end and fails its CRC check
            public override int Read(byte[] buffer, int offset, int count) => count == 0 ? 0 : _decoder.Read(buffer, offset, count);
            public override int Read(Span<byte> buffer) => buffer.IsEmpty ? 0 : _decoder.Read(buffer);
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _decoder.Dispose();
                    _file.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>A compressed single file (log.txt.gz) rather than a TAR of many. A TAR header carries "ustar" at offset 257.</summary>
        private static bool IsTar(Stream decompressed)
        {
            byte[] head = new byte[262];
            int read = 0, n;
            while (read < head.Length && (n = decompressed.Read(head, read, head.Length - read)) > 0) read += n;
            return read >= 262 && head[257] == 'u' && head[258] == 's' && head[259] == 't' && head[260] == 'a' && head[261] == 'r';
        }

        /// <summary>Unpacks everything into <paramref name="destDir"/> (created if needed).</summary>
        public static async Task ExtractAsync(string src, string destDir)
        {
            Directory.CreateDirectory(destDir);
            string name = Path.GetFileName(src).ToLowerInvariant();

            if (name.EndsWith(".zip"))
            {
                await Task.Run(() => ZipFile.ExtractToDirectory(src, destDir, overwriteFiles: true));
                return;
            }

            if (name.EndsWith(".tar"))
            {
                await Task.Run(() => TarFile.ExtractToDirectory(src, destDir, overwriteFiles: true));
                return;
            }

            if (name.EndsWith(".7z") || name.EndsWith(".rar"))
            {
                await Task.Run(() => ExtractWithSharpCompress(src, destDir, password: null));
                return;
            }

            bool tar;
            try
            {
                using var probe = OpenCodec(src);
                if (probe == null)
                {
                    // cpio, iso, ar, cab…
                    await RunBsdTarAsync("-xf", src, "-C", destDir);
                    return;
                }
                tar = IsTar(probe);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or SharpCompress.Common.ArchiveException or ZstdSharp.ZstdException)
            {
                throw new InvalidOperationException($"Couldn't open the archive: {ex.Message}", ex);
            }

            await Task.Run(() =>
            {
                using var input = OpenCodec(src)!;
                if (tar)
                {
                    TarFile.ExtractToDirectory(input, destDir, overwriteFiles: true);
                    return;
                }
                // "notes.txt.zst" → "notes.txt"
                using var output = File.Create(Path.Combine(destDir, Path.GetFileNameWithoutExtension(src)));
                input.CopyTo(output);
            });
        }

        /// <summary>7z and RAR (any version, any compression), with a zip-slip guard. Throws if a password is needed but missing or wrong.</summary>
        public static void ExtractWithSharpCompress(string src, string destDir, string? password)
        {
            string root = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;
            try
            {
                using var archive = ArchiveFactory.OpenArchive(src, new ReaderOptions { Password = password });
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Key)) continue;
                    string dest = Path.GetFullPath(Path.Combine(destDir, entry.Key));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                    if (entry.IsDirectory)
                    {
                        Directory.CreateDirectory(dest);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using (var input = entry.OpenEntryStream())
                    using (var output = File.Create(dest))
                    {
                        input.CopyTo(output);
                    }
                    if (entry.LastModifiedTime is { } modified) File.SetLastWriteTime(dest, modified);
                }
            }
            catch (Exception ex) when (ex is SharpCompress.Common.CryptographicException or System.Security.Cryptography.CryptographicException)
            {
                throw new InvalidOperationException(password == null
                    ? "This archive is password-protected. Use Unlock (Actions wheel) and enter the password."
                    : "That password is incorrect.", ex);
            }
        }

        /// <summary>
        /// Re-packs an archive's contents as <paramref name="format"/>: "zip", "tar" or "gz" (a .tar.gz).
        /// </summary>
        public static async Task RepackAsync(string src, string target, string format)
        {
            string work = Path.Combine(Path.GetTempPath(), "ogc-repack-" + Guid.NewGuid().ToString("N"));
            try
            {
                await ExtractAsync(src, work);
                if (File.Exists(target)) File.Delete(target);
                await Task.Run(() =>
                {
                    switch (format)
                    {
                        case "zip":
                            ZipFile.CreateFromDirectory(work, target, CompressionLevel.SmallestSize, includeBaseDirectory: false);
                            break;
                        case "tar":
                            TarFile.CreateFromDirectory(work, target, includeBaseDirectory: false);
                            break;
                        case "gz":
                        {
                            using var file = File.Create(target);
                            using var gz = new GZipStream(file, CompressionLevel.SmallestSize);
                            TarFile.CreateFromDirectory(work, gz, includeBaseDirectory: false);
                            break;
                        }
                        default:
                            throw new InvalidOperationException($"Can't pack archives as .{format}.");
                    }
                });
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        private static async Task RunBsdTarAsync(params string[] args)
        {
            string tar = BsdTar.Value ?? throw new InvalidOperationException("Opening this kind of archive needs Windows 10 (1803) or later, which includes tar.exe.");
            var psi = new ProcessStartInfo(tar)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (string a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            string errors = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            await stdout;
            if (p.ExitCode != 0)
            {
                string reason = errors.Trim();
                if (reason.Contains("passphrase", StringComparison.OrdinalIgnoreCase) || reason.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("This archive is password-protected. Open it with 7-Zip or WinRAR to enter the password.");
                }
                throw new InvalidOperationException($"Couldn't open the archive: {reason}");
            }
        }
    }
}
