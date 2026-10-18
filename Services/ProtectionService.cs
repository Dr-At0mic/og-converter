using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>
    /// Password protection for any file or folder. Picks the methods a file supports, applies them, and reverses them.
    /// The original file is never modified: Protect and Unlock always write a new file.
    /// </summary>
    public static class ProtectionService
    {
        public enum LockKind { None, Pdf, Office, Vault, Zip }

        /// <summary>Methods that apply to this path, best first.</summary>
        public static List<ProtectionMethod> AvailableMethods(string path)
        {
            if (Directory.Exists(path)) return new() { ProtectionMethod.PasswordZip };

            string ext = Path.GetExtension(path).ToLowerInvariant();
            var methods = new List<ProtectionMethod>();
            if (ext == ".pdf") methods.Add(ProtectionMethod.PdfPassword);
            if (DocumentEngine.CanProtectOffice(ext)) methods.Add(ProtectionMethod.OfficePassword);
            methods.Add(ProtectionMethod.EncryptedFile);
            methods.Add(ProtectionMethod.PasswordZip);
            return methods;
        }

        /// <summary>What kind of protection this file currently has (decides Protect vs Unlock in the wheel).</summary>
        public static LockKind DetectLock(string path)
        {
            if (!File.Exists(path)) return LockKind.None;
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == FileVault.Extension && FileVault.IsVaultFile(path)) return LockKind.Vault;
            if (ext == ".pdf" && PdfToolsService.IsEncrypted(path)) return LockKind.Pdf;
            if (DocumentEngine.IsEncryptedOfficeFile(path)) return LockKind.Office;
            if (ext == ".zip" && IsEncryptedZip(path)) return LockKind.Zip;
            return LockKind.None;
        }

        public static bool IsEncryptedZip(string path)
        {
            try
            {
                using var zip = new ZipFile(path);
                return zip.Cast<ZipEntry>().Any(e => e.IsCrypted);
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ Protect

        /// <param name="targetFor">Builds a unique output path from (base path, extension, suffix).</param>
        public static async Task<string> ProtectAsync(string path, ProtectionOptions o, Func<string, string, string?, string> targetFor)
        {
            if (string.IsNullOrEmpty(o.Password)) throw new InvalidOperationException("Enter a password.");
            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

            switch (o.Method)
            {
                case ProtectionMethod.PdfPassword:
                {
                    string target = targetFor(path, "pdf", "_protected");
                    await Task.Run(() => PdfToolsService.Protect(path, target, o));
                    return target;
                }

                case ProtectionMethod.OfficePassword:
                {
                    string target = targetFor(path, ext, "_protected");
                    await DocumentEngine.ProtectOfficeAsync(path, target, o.Password, o.ModifyPassword);
                    return target;
                }

                case ProtectionMethod.EncryptedFile:
                {
                    // "report.pdf" → "report.pdf.ogx" (the ".keep" makes the target builder keep the real extension)
                    string target = targetFor(path + ".keep", "ogx", null);
                    await Task.Run(() => FileVault.Encrypt(path, target, o.Password, o.FileCipher, o.KeyDerivation));
                    return target;
                }

                case ProtectionMethod.PasswordZip:
                {
                    bool folder = Directory.Exists(path);
                    string clean = path.TrimEnd('\\', '/');
                    string target = targetFor(folder ? clean + ".keep" : clean, "zip", folder ? null : "_protected");
                    await Task.Run(() => CreatePasswordZip(clean, target, o.Password, o.ZipCipher));
                    return target;
                }

                default:
                    throw new InvalidOperationException("Unknown protection method.");
            }
        }

        private static void CreatePasswordZip(string source, string target, string password, ZipCipher cipher)
        {
            string temp = target + ".partial";
            try
            {
                using (var fs = File.Create(temp))
                using (var zip = new ZipOutputStream(fs))
                {
                    zip.SetLevel(9);
                    zip.Password = password;

                    IEnumerable<(string File, string EntryName)> items = Directory.Exists(source)
                        ? Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                            .Select(f => (f, Path.Combine(Path.GetFileName(source), Path.GetRelativePath(source, f)).Replace('\\', '/')))
                        : new[] { (source, Path.GetFileName(source)) };

                    foreach (var (file, entryName) in items)
                    {
                        var info = new FileInfo(file);
                        var entry = new ZipEntry(ZipEntry.CleanName(entryName))
                        {
                            DateTime = info.LastWriteTime,
                            Size = info.Length,
                            // WinZip AES-256 (AE-2); 0 = traditional PKWARE ZipCrypto
                            AESKeySize = cipher == ZipCipher.Aes256 ? 256 : 0
                        };
                        zip.PutNextEntry(entry);
                        using (var input = File.OpenRead(file)) input.CopyTo(zip);
                        zip.CloseEntry();
                    }
                    zip.Finish();
                }
                File.Move(temp, target, overwrite: true);
            }
            catch
            {
                try { File.Delete(temp); } catch { }
                throw;
            }
        }

        // ------------------------------------------------------------------ Unlock

        public static bool CheckPassword(string path, string password) => DetectLock(path) switch
        {
            LockKind.Pdf => PdfToolsService.CheckPassword(path, password),
            LockKind.Vault => FileVault.CheckPassword(path, password),
            LockKind.Zip => CheckZipPassword(path, password),
            _ => true // Office can only be checked by opening it; the job reports a wrong password
        };

        public static async Task<string> UnlockAsync(string path, string password, Func<string, string, string?, string> targetFor, Func<string, string> uniquePath, string outputDirectory)
        {
            switch (DetectLock(path))
            {
                case LockKind.Pdf:
                {
                    string target = targetFor(path, "pdf", "_unlocked");
                    await Task.Run(() => PdfToolsService.Unlock(path, target, password));
                    return target;
                }

                case LockKind.Office:
                {
                    string ext = Path.GetExtension(path).TrimStart('.');
                    string target = targetFor(path, ext, "_unlocked");
                    await DocumentEngine.UnlockOfficeAsync(path, target, password);
                    return target;
                }

                case LockKind.Vault:
                    return await Task.Run(() => FileVault.Decrypt(path, password, outputDirectory, uniquePath));

                case LockKind.Zip:
                {
                    string folder = uniquePath(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(path)));
                    await Task.Run(() => ExtractPasswordZip(path, folder, password));
                    return folder;
                }

                default:
                    throw new InvalidOperationException("This file isn't password-protected.");
            }
        }

        private static bool CheckZipPassword(string path, string password)
        {
            try
            {
                using var zip = new ZipFile(path) { Password = password };
                var entry = zip.Cast<ZipEntry>().FirstOrDefault(e => e.IsFile && e.IsCrypted);
                if (entry == null) return true;
                using var s = zip.GetInputStream(entry);
                var buffer = new byte[81920];
                while (s.Read(buffer, 0, buffer.Length) > 0) { } // reading to the end verifies the AES MAC / CRC
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ExtractPasswordZip(string path, string folder, string password)
        {
            if (!CheckZipPassword(path, password)) throw new InvalidOperationException("That password is incorrect.");

            string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(folder);
            using var zip = new ZipFile(path) { Password = password };
            foreach (ZipEntry entry in zip)
            {
                // Zip-slip guard: never write outside the target folder
                string dest = Path.GetFullPath(Path.Combine(folder, entry.Name));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(dest);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var input = zip.GetInputStream(entry);
                using var output = File.Create(dest);
                input.CopyTo(output);
            }
        }

        // ------------------------------------------------------------------ Helpers

        /// <summary>A 20-character password from a 66-symbol alphabet without look-alikes (I, O, l, 0, 1): ~120 bits.</summary>
        public static string GeneratePassword(int length = 20)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%&*?";
            return new string(Enumerable.Range(0, length).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        }

        /// <summary>Rough strength estimate: 0 weak … 3 very strong, from length and character variety.</summary>
        public static int EstimateStrength(string password)
        {
            if (string.IsNullOrEmpty(password)) return -1;
            int pool = 0;
            if (password.Any(char.IsLower)) pool += 26;
            if (password.Any(char.IsUpper)) pool += 26;
            if (password.Any(char.IsDigit)) pool += 10;
            if (password.Any(c => !char.IsLetterOrDigit(c))) pool += 32;
            double bits = password.Distinct().Count() < 3 ? password.Length : password.Length * Math.Log2(Math.Max(pool, 2));
            return bits switch { < 40 => 0, < 60 => 1, < 80 => 2, _ => 3 };
        }
    }
}
