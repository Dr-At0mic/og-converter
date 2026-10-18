using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Tests
{
    /// <summary>Self-test for every protection method and cipher option, including failure cases.</summary>
    internal static class ProtectionVerification
    {
        public static async Task RunAsync(ConversionEngine engine, AppSettings settings, string tempDir, string testPdf)
        {
            Console.WriteLine("[20] Protection");
            var protect = new WheelItem { Id = "act_protect", Title = "Protect", Mode = WheelMode.Actions };
            var unlock = new WheelItem { Id = "act_unlock", Title = "Unlock", Mode = WheelMode.Actions };

            async Task<string> Protect(string path, ProtectionOptions o, string label)
            {
                var job = await engine.ExecuteAsync(path, protect, settings, new ToolParameters { Protection = o });
                Console.WriteLine($"     Protect {label,-34} Success={job.IsSuccess} {Path.GetFileName(job.TargetPath)} {job.ErrorMessage}");
                if (!job.IsSuccess) throw new Exception($"Protect {label} failed: {job.ErrorMessage}");
                if (ProtectionService.DetectLock(job.TargetPath) == ProtectionService.LockKind.None)
                    throw new Exception($"{label}: output is not detected as protected");
                return job.TargetPath;
            }

            async Task<string> Unlock(string path, string password, string label, bool expectSuccess = true)
            {
                var job = await engine.ExecuteAsync(path, unlock, settings, new ToolParameters { Password = password });
                Console.WriteLine($"     Unlock  {label,-34} Success={job.IsSuccess} {Path.GetFileName(job.TargetPath)} {job.ErrorMessage}");
                if (job.IsSuccess != expectSuccess) throw new Exception($"Unlock {label}: expected success={expectSuccess}, got {job.IsSuccess} ({job.ErrorMessage})");
                return job.TargetPath;
            }

            // --- PDF: each cipher, plus restrictions
            foreach (var cipher in new[] { PdfCipher.Aes256, PdfCipher.Aes128, PdfCipher.Rc4_128 })
            {
                string locked = await Protect(testPdf, new ProtectionOptions { Method = ProtectionMethod.PdfPassword, Password = "pdf-pass", PdfCipher = cipher }, $"PDF {cipher}");
                if (ProtectionService.DetectLock(locked) != ProtectionService.LockKind.Pdf) throw new Exception("PDF not detected as locked");
                if (ProtectionService.CheckPassword(locked, "nope")) throw new Exception("Wrong PDF password accepted");
                string open = await Unlock(locked, "pdf-pass", $"PDF {cipher}");
                if (PdfToolsService.GetPageCount(open) != 2) throw new Exception("Unlocked PDF lost pages");
            }
            string restricted = await Protect(testPdf, new ProtectionOptions { Method = ProtectionMethod.PdfPassword, Password = "reader", AllowCopying = false, AllowEditing = false }, "PDF AES-256, no copy/edit");
            if (!PdfToolsService.CheckPassword(restricted, "reader")) throw new Exception("Restricted PDF won't open with the user password");

            // --- Encrypted file (.ogx): both ciphers, both KDFs, multi-chunk, empty, tamper, truncate
            string big = Path.Combine(tempDir, "dataset.bin");
            byte[] bigBytes = RandomNumberGenerator.GetBytes(2_600_000); // spans 3 chunks
            await File.WriteAllBytesAsync(big, bigBytes);
            string empty = Path.Combine(tempDir, "empty.txt");
            await File.WriteAllBytesAsync(empty, Array.Empty<byte>());

            var vaultCombos = new[] { (FileCipher.Aes256Gcm, KeyDerivation.Argon2id), (FileCipher.Aes256Gcm, KeyDerivation.Pbkdf2Sha512) }.ToList();
            if (FileVault.IsChaChaSupported) vaultCombos.Add((FileCipher.ChaCha20Poly1305, KeyDerivation.Argon2id));
            else Console.WriteLine("     (ChaCha20-Poly1305 not supported on this Windows build; skipped)");

            foreach (var (cipher, kdf) in vaultCombos)
            {
                var started = DateTime.Now;
                string vault = await Protect(big, new ProtectionOptions { Method = ProtectionMethod.EncryptedFile, Password = "vault-pass", FileCipher = cipher, KeyDerivation = kdf }, $"OGX {cipher}+{kdf}");
                Console.WriteLine($"       ({(DateTime.Now - started).TotalSeconds:0.0}s, {FileVault.Describe(vault)})");
                if (ProtectionService.CheckPassword(vault, "wrong")) throw new Exception("Wrong vault password accepted");

                File.Move(big, big + ".orig");
                string restored = await Unlock(vault, "vault-pass", $"OGX {cipher}+{kdf}");
                if (Path.GetFileName(restored) != "dataset.bin") throw new Exception($"Original name not restored: {restored}");
                if (!(await File.ReadAllBytesAsync(restored)).SequenceEqual(bigBytes)) throw new Exception("Decrypted bytes differ");
                File.Delete(restored);
                File.Move(big + ".orig", big);
                await Unlock(vault, "wrong", $"OGX wrong password", expectSuccess: false);
                File.Delete(vault);
            }

            string emptyVault = await Protect(empty, new ProtectionOptions { Method = ProtectionMethod.EncryptedFile, Password = "e" }, "OGX empty file");
            File.Delete(empty);
            string emptyBack = await Unlock(emptyVault, "e", "OGX empty file");
            if (new FileInfo(emptyBack).Length != 0) throw new Exception("Empty file round-trip failed");

            string tamper = await Protect(big, new ProtectionOptions { Method = ProtectionMethod.EncryptedFile, Password = "t" }, "OGX for tamper test");
            byte[] raw = await File.ReadAllBytesAsync(tamper);
            raw[raw.Length / 2] ^= 0x01;
            string tampered = Path.Combine(tempDir, "tampered.bin.ogx");
            await File.WriteAllBytesAsync(tampered, raw);
            await Unlock(tampered, "t", "OGX tampered (must fail)", expectSuccess: false);
            raw[raw.Length / 2] ^= 0x01;
            string truncated = Path.Combine(tempDir, "truncated.bin.ogx");
            await File.WriteAllBytesAsync(truncated, raw.AsSpan(0, 45 + 1024 * 1024 + 16).ToArray()); // 45-byte header + first chunk only
            await Unlock(truncated, "t", "OGX truncated (must fail)", expectSuccess: false);
            if (Directory.GetFiles(tempDir, "*.partial").Length > 0) throw new Exception("Failed decryption left a partial file behind");

            // --- Password ZIP: AES-256 and ZipCrypto, file and folder
            string folder = Path.Combine(tempDir, "Project Files");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            await File.WriteAllTextAsync(Path.Combine(folder, "readme.txt"), "top secret");
            await File.WriteAllTextAsync(Path.Combine(folder, "sub", "data.csv"), "a,b\n1,2");
            foreach (var cipher in new[] { ZipCipher.Aes256, ZipCipher.ZipCrypto })
            {
                string zip = await Protect(folder, new ProtectionOptions { Method = ProtectionMethod.PasswordZip, Password = "zip-pass", ZipCipher = cipher }, $"ZIP folder {cipher}");
                if (!ProtectionService.IsEncryptedZip(zip)) throw new Exception("Zip entries are not encrypted");
                if (ProtectionService.CheckPassword(zip, "wrong")) throw new Exception("Wrong zip password accepted");
                string extracted = await Unlock(zip, "zip-pass", $"ZIP folder {cipher}");
                string readme = Path.Combine(extracted, "Project Files", "readme.txt");
                if (await File.ReadAllTextAsync(readme) != "top secret") throw new Exception("Zip round-trip content differs");
                File.Delete(zip);
            }
            await Protect(testPdf, new ProtectionOptions { Method = ProtectionMethod.PasswordZip, Password = "z" }, "ZIP single file AES-256");

            // --- Wheel shows Unlock for protected files
            string anyVault = await Protect(big, new ProtectionOptions { Method = ProtectionMethod.EncryptedFile, Password = "w" }, "OGX for wheel check");
            bool wheelOk = FormatRegistry.GetWheelItems(anyVault, WheelMode.Actions).Exists(i => i.Id == "act_unlock")
                        && FormatRegistry.GetWheelItems(big, WheelMode.Actions).Exists(i => i.Id == "act_protect")
                        && FormatRegistry.GetWheelItems(folder, WheelMode.Actions).Exists(i => i.Id == "act_protect");
            Console.WriteLine($"     Wheel shows Protect/Unlock correctly: {wheelOk}");
            if (!wheelOk) throw new Exception("Protect/Unlock wheel items are wrong");

            // --- Office
            if (!DocumentEngine.HasWord && !DocumentEngine.HasExcel) return;

            if (DocumentEngine.HasWord)
            {
                string rtf = Path.Combine(tempDir, "memo.rtf");
                await File.WriteAllTextAsync(rtf, @"{\rtf1\ansi\pard Confidential memo text.\par}");
                var convert = new WheelItem { Id = "docx", Title = "DOCX", Extension = "docx", Mode = WheelMode.Convert };
                string docx = (await engine.ExecuteAsync(rtf, convert, settings)).TargetPath;

                string lockedDocx = await Protect(docx, new ProtectionOptions { Method = ProtectionMethod.OfficePassword, Password = "office-pass", ModifyPassword = "edit-pass" }, "Word DOCX (AES-256 agile)");
                if (!DocumentEngine.IsEncryptedOfficeFile(lockedDocx)) throw new Exception("Protected DOCX is not encrypted");
                await Unlock(lockedDocx, "wrong", "Word DOCX wrong password", expectSuccess: false);
                string openDocx = await Unlock(lockedDocx, "office-pass", "Word DOCX");
                if (DocumentEngine.IsEncryptedOfficeFile(openDocx)) throw new Exception("Unlocked DOCX is still encrypted");
                var toTxt = new WheelItem { Id = "txt", Title = "TXT", Extension = "txt", Mode = WheelMode.Convert };
                string text = await File.ReadAllTextAsync((await engine.ExecuteAsync(openDocx, toTxt, settings)).TargetPath);
                if (!text.Contains("Confidential memo text")) throw new Exception("Unlocked DOCX lost its content");
            }

            if (DocumentEngine.HasExcel)
            {
                string csv = Path.Combine(tempDir, "ledger.csv");
                await File.WriteAllTextAsync(csv, "Item,Cost\nLaptop,1200\n");
                var convert = new WheelItem { Id = "xlsx", Title = "XLSX", Extension = "xlsx", Mode = WheelMode.Convert };
                string xlsx = (await engine.ExecuteAsync(csv, convert, settings)).TargetPath;
                string lockedXlsx = await Protect(xlsx, new ProtectionOptions { Method = ProtectionMethod.OfficePassword, Password = "sheet-pass", ModifyPassword = "sheet-edit" }, "Excel XLSX (AES-256 agile)");
                if (!DocumentEngine.IsEncryptedOfficeFile(lockedXlsx)) throw new Exception("Protected XLSX is not encrypted");
                string openXlsx = await Unlock(lockedXlsx, "sheet-pass", "Excel XLSX");
                if (DocumentEngine.IsEncryptedOfficeFile(openXlsx)) throw new Exception("Unlocked XLSX is still encrypted");
            }

            if (DocumentEngine.HasPowerPoint)
            {
                string deck = Directory.GetFiles(tempDir, "deck.pptx").FirstOrDefault() ?? "";
                if (File.Exists(deck))
                {
                    string lockedPptx = await Protect(deck, new ProtectionOptions { Method = ProtectionMethod.OfficePassword, Password = "deck-pass" }, "PowerPoint PPTX (AES-256 agile)");
                    if (!DocumentEngine.IsEncryptedOfficeFile(lockedPptx)) throw new Exception("Protected PPTX is not encrypted");
                    string openPptx = await Unlock(lockedPptx, "deck-pass", "PowerPoint PPTX");
                    if (DocumentEngine.IsEncryptedOfficeFile(openPptx)) throw new Exception("Unlocked PPTX is still encrypted");
                }
            }
        }
    }
}
