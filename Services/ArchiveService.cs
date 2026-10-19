using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace OgConverter.Services
{
    /// <summary>
    /// Unpacks and re-packs archives. ZIP, TAR and GZip are handled in .NET; 7-Zip, RAR, bzip2, xz and CAB go through
    /// the tar.exe that ships with Windows 10 1803+ (bsdtar/libarchive, which reads them all).
    /// </summary>
    public static class ArchiveService
    {
        private static readonly Lazy<string?> BsdTar = new(() =>
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            return File.Exists(path) ? path : null;
        });

        /// <summary>"backup.tar.gz" → "backup", "photos.zip" → "photos".</summary>
        public static string BaseName(string path)
        {
            string name = Path.GetFileName(path);
            foreach (string ext in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst", ".tgz", ".tbz2", ".txz" })
            {
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return name[..^ext.Length];
            }
            return Path.GetFileNameWithoutExtension(name);
        }

        /// <summary>A .gz that holds a single compressed file (log.txt.gz) rather than a TAR of many.</summary>
        private static bool IsSingleFileGzip(string path)
        {
            string name = Path.GetFileName(path).ToLowerInvariant();
            if (!name.EndsWith(".gz") || name.EndsWith(".tar.gz")) return false;
            try
            {
                // A TAR header carries "ustar" at offset 257
                using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
                byte[] head = new byte[262];
                int read = 0, n;
                while (read < head.Length && (n = gz.Read(head, read, head.Length - read)) > 0) read += n;
                return !(read >= 262 && head[257] == 'u' && head[258] == 's' && head[259] == 't' && head[260] == 'a' && head[261] == 'r');
            }
            catch (InvalidDataException)
            {
                return false;
            }
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

            if (IsSingleFileGzip(src))
            {
                string target = Path.Combine(destDir, Path.GetFileNameWithoutExtension(src));
                await using var input = new GZipStream(File.OpenRead(src), CompressionMode.Decompress);
                await using var output = File.Create(target);
                await input.CopyToAsync(output);
                return;
            }

            if (name.EndsWith(".tar"))
            {
                await Task.Run(() => TarFile.ExtractToDirectory(src, destDir, overwriteFiles: true));
                return;
            }

            if (name.EndsWith(".tar.gz") || name.EndsWith(".tgz") || name.EndsWith(".gz"))
            {
                await Task.Run(() =>
                {
                    using var gz = new GZipStream(File.OpenRead(src), CompressionMode.Decompress);
                    TarFile.ExtractToDirectory(gz, destDir, overwriteFiles: true);
                });
                return;
            }

            // 7z, rar, bz2, xz, cab…
            await RunBsdTarAsync("-xf", src, "-C", destDir);
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
