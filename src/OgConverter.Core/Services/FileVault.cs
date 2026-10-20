using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>
    /// Password-based file encryption into the ".ogx" container.
    ///
    /// Layout (all integers big-endian):
    /// <code>
    ///   "OGX" 0x01        magic + format version                  4 bytes
    ///   cipher            1 = AES-256-GCM, 2 = ChaCha20-Poly1305   1
    ///   kdf               1 = Argon2id, 2 = PBKDF2-HMAC-SHA512     1
    ///   kdf params        Argon2id: memory KiB, iterations, lanes  3 x uint32
    ///                     PBKDF2:   iterations, 0, 0
    ///   salt                                                       16
    ///   nonce prefix      random                                   7
    ///   chunk size        plaintext bytes per chunk                uint32
    ///   chunks...         ciphertext + 16-byte tag
    /// </code>
    /// Chunks use the STREAM construction: nonce = prefix(7) || counter(4) || last-chunk flag(1), and every chunk
    /// authenticates the whole header as associated data. This detects a wrong password, tampering, reordering,
    /// and truncation. The plaintext starts with the original file name (uint16 length + UTF-8) so decryption
    /// restores it.
    /// </summary>
    public static class FileVault
    {
        public const string Extension = ".ogx";

        private static readonly byte[] Magic = { (byte)'O', (byte)'G', (byte)'X', 0x01 };
        private const int HeaderSize = 4 + 1 + 1 + 12 + 16 + 7 + 4;
        private const int ChunkSize = 1024 * 1024;
        private const int TagSize = 16;
        private const int KeySize = 32;

        // Argon2id: 64 MiB, 3 passes, 4 lanes (above the OWASP minimum, ~0.5 s on a modern CPU)
        private const uint ArgonMemoryKiB = 64 * 1024;
        private const uint ArgonIterations = 3;
        private const uint ArgonLanes = 4;

        // PBKDF2-HMAC-SHA512: OWASP 2023 recommendation
        private const uint Pbkdf2Iterations = 210_000;

        public static bool IsChaChaSupported => ChaCha20Poly1305.IsSupported;

        public static bool IsVaultFile(string path)
        {
            if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
            try
            {
                using var fs = File.OpenRead(path);
                Span<byte> head = stackalloc byte[4];
                return fs.Read(head) == 4 && head.SequenceEqual(Magic);
            }
            catch
            {
                return false;
            }
        }

        public static void Encrypt(string sourcePath, string targetPath, string password, FileCipher cipher, KeyDerivation kdf)
        {
            if (cipher == FileCipher.ChaCha20Poly1305 && !IsChaChaSupported)
                throw new PlatformNotSupportedException("ChaCha20-Poly1305 isn't available on this version of Windows. Use AES-256-GCM.");

            byte[] header = new byte[HeaderSize];
            Magic.CopyTo(header, 0);
            header[4] = cipher == FileCipher.ChaCha20Poly1305 ? (byte)2 : (byte)1;
            header[5] = kdf == KeyDerivation.Pbkdf2Sha512 ? (byte)2 : (byte)1;
            if (kdf == KeyDerivation.Argon2id)
            {
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(6), ArgonMemoryKiB);
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(10), ArgonIterations);
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(14), ArgonLanes);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(6), Pbkdf2Iterations);
            }
            RandomNumberGenerator.Fill(header.AsSpan(18, 16));   // salt
            RandomNumberGenerator.Fill(header.AsSpan(34, 7));    // nonce prefix
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(41), ChunkSize);

            byte[] key = DeriveKey(password, header);
            string tempPath = targetPath + ".partial";
            try
            {
                using var aead = CreateAead(header[4], key);
                using var input = File.OpenRead(sourcePath);
                using (var output = File.Create(tempPath))
                {
                    output.Write(header);

                    // Plaintext stream = [name length][name][file bytes]
                    byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(sourcePath));
                    if (name.Length > ushort.MaxValue) throw new InvalidOperationException("File name is too long.");
                    var prefix = new byte[2 + name.Length];
                    BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)name.Length);
                    name.CopyTo(prefix, 2);

                    var plain = new byte[ChunkSize];
                    var cipherText = new byte[ChunkSize];
                    var tag = new byte[TagSize];
                    var nonce = new byte[12];

                    int filled = prefix.Length;
                    prefix.CopyTo(plain, 0);
                    uint counter = 0;

                    // Read ahead one byte so we know which chunk is the last
                    int pending = input.ReadByte();
                    while (true)
                    {
                        while (filled < ChunkSize && pending >= 0)
                        {
                            plain[filled++] = (byte)pending;
                            int read = input.Read(plain, filled, ChunkSize - filled);
                            filled += read;
                            pending = input.ReadByte();
                        }

                        bool last = pending < 0;
                        BuildNonce(header, counter, last, nonce);
                        aead.Encrypt(nonce, plain.AsSpan(0, filled), cipherText.AsSpan(0, filled), tag, header);
                        output.Write(cipherText, 0, filled);
                        output.Write(tag);

                        if (last) break;
                        counter = checked(counter + 1);
                        filled = 0;
                    }
                }
                File.Move(tempPath, targetPath, overwrite: true);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        /// <summary>Decrypts into <paramref name="outputDirectory"/>. Returns the restored file's path.</summary>
        public static string Decrypt(string vaultPath, string password, string outputDirectory, Func<string, string> uniquePath)
        {
            using var input = File.OpenRead(vaultPath);
            byte[] header = ReadHeader(input);
            byte[] key = DeriveKey(password, header);
            string tempPath = Path.Combine(outputDirectory, $".og-decrypt-{Guid.NewGuid():N}.partial");

            try
            {
                using var aead = CreateAead(header[4], key);
                string? originalName = null;

                using (var output = File.Create(tempPath))
                {
                    var buffer = new byte[ChunkSize + TagSize];
                    var plain = new byte[ChunkSize];
                    var nonce = new byte[12];
                    uint counter = 0;
                    var nameBuffer = new MemoryStream();
                    int nameLength = -1;

                    while (true)
                    {
                        int read = ReadFully(input, buffer);
                        if (read < TagSize) throw new CryptographicException("truncated");

                        // A chunk shorter than the maximum must be the last one; a full one is last if the file ends here
                        bool last = read < buffer.Length || input.Position == input.Length;
                        int dataLength = read - TagSize;
                        BuildNonce(header, counter, last, nonce);
                        aead.Decrypt(nonce, buffer.AsSpan(0, dataLength), buffer.AsSpan(dataLength, TagSize), plain.AsSpan(0, dataLength), header);

                        int offset = 0;
                        if (originalName == null)
                        {
                            // The name prefix may in theory span chunks; collect it first
                            nameBuffer.Write(plain, 0, dataLength);
                            var collected = nameBuffer.GetBuffer();
                            if (nameLength < 0 && nameBuffer.Length >= 2) nameLength = BinaryPrimitives.ReadUInt16BigEndian(collected);
                            if (nameLength >= 0 && nameBuffer.Length >= 2 + nameLength)
                            {
                                originalName = Encoding.UTF8.GetString(collected, 2, nameLength);
                                output.Write(collected, 2 + nameLength, (int)nameBuffer.Length - 2 - nameLength);
                            }
                            offset = dataLength;
                        }
                        if (offset < dataLength) output.Write(plain, offset, dataLength - offset);

                        if (last) break;
                        counter = checked(counter + 1);
                    }

                    if (originalName == null) throw new CryptographicException("missing name");
                }

                // Never trust a path from the file: keep only the bare name
                string safeName = Path.GetFileName(originalName);
                if (string.IsNullOrWhiteSpace(safeName)) safeName = Path.GetFileNameWithoutExtension(vaultPath);
                string target = uniquePath(Path.Combine(outputDirectory, safeName));
                File.Move(tempPath, target);
                return target;
            }
            catch (CryptographicException ex)
            {
                TryDelete(tempPath);
                throw new InvalidOperationException("Wrong password, or the file has been modified or damaged.", ex);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        /// <summary>Checks the password by authenticating the first chunk only (fast, used by the Unlock dialog).</summary>
        public static bool CheckPassword(string vaultPath, string password)
        {
            try
            {
                using var input = File.OpenRead(vaultPath);
                byte[] header = ReadHeader(input);
                byte[] key = DeriveKey(password, header);
                try
                {
                    using var aead = CreateAead(header[4], key);
                    var buffer = new byte[ChunkSize + TagSize];
                    int read = ReadFully(input, buffer);
                    if (read < TagSize) return false;
                    bool last = read < buffer.Length || input.Position == input.Length;
                    var nonce = new byte[12];
                    BuildNonce(header, 0, last, nonce);
                    var plain = new byte[read - TagSize];
                    aead.Decrypt(nonce, buffer.AsSpan(0, read - TagSize), buffer.AsSpan(read - TagSize, TagSize), plain, header);
                    return true;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>"AES-256-GCM · Argon2id" for display.</summary>
        public static string Describe(string vaultPath)
        {
            try
            {
                using var input = File.OpenRead(vaultPath);
                byte[] header = ReadHeader(input);
                return $"{(header[4] == 2 ? "ChaCha20-Poly1305" : "AES-256-GCM")} · {(header[5] == 2 ? "PBKDF2-SHA512" : "Argon2id")}";
            }
            catch
            {
                return "unknown";
            }
        }

        private static byte[] ReadHeader(Stream input)
        {
            byte[] header = new byte[HeaderSize];
            if (ReadFully(input, header) != HeaderSize || !header.AsSpan(0, 4).SequenceEqual(Magic))
                throw new InvalidOperationException("This isn't an OG Converter encrypted file, or it's from a newer version.");
            if (header[4] is not (1 or 2) || header[5] is not (1 or 2))
                throw new InvalidOperationException("This encrypted file uses settings this version doesn't support.");
            if (BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(41)) != ChunkSize)
                throw new InvalidOperationException("This encrypted file uses settings this version doesn't support.");
            return header;
        }

        private static byte[] DeriveKey(string password, byte[] header)
        {
            byte[] pw = Encoding.UTF8.GetBytes(password);
            byte[] salt = header.AsSpan(18, 16).ToArray();
            uint a = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(6));
            uint b = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(10));
            uint c = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(14));
            try
            {
                if (header[5] == 2)
                {
                    if (a is < 100_000 or > 10_000_000) throw new InvalidOperationException("Invalid key derivation settings.");
                    return Rfc2898DeriveBytes.Pbkdf2(pw, salt, (int)a, HashAlgorithmName.SHA512, KeySize);
                }

                // Bounds stop a crafted file from demanding absurd memory or time
                if (a is < 8 * 1024 or > 1024 * 1024 || b is < 1 or > 20 || c is < 1 or > 16)
                    throw new InvalidOperationException("Invalid key derivation settings.");
                using var argon = new Argon2id(pw)
                {
                    Salt = salt,
                    MemorySize = (int)a,
                    Iterations = (int)b,
                    DegreeOfParallelism = (int)c
                };
                return argon.GetBytes(KeySize);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pw);
            }
        }

        private interface IAead : IDisposable
        {
            void Encrypt(byte[] nonce, ReadOnlySpan<byte> plain, Span<byte> cipher, Span<byte> tag, byte[] aad);
            void Decrypt(byte[] nonce, ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> tag, Span<byte> plain, byte[] aad);
        }

        private sealed class GcmAead : IAead
        {
            private readonly AesGcm _gcm;
            public GcmAead(byte[] key) => _gcm = new AesGcm(key, TagSize);
            public void Encrypt(byte[] n, ReadOnlySpan<byte> p, Span<byte> c, Span<byte> t, byte[] aad) => _gcm.Encrypt(n, p, c, t, aad);
            public void Decrypt(byte[] n, ReadOnlySpan<byte> c, ReadOnlySpan<byte> t, Span<byte> p, byte[] aad) => _gcm.Decrypt(n, c, t, p, aad);
            public void Dispose() => _gcm.Dispose();
        }

        private sealed class ChaChaAead : IAead
        {
            private readonly ChaCha20Poly1305 _chacha;
            public ChaChaAead(byte[] key) => _chacha = new ChaCha20Poly1305(key);
            public void Encrypt(byte[] n, ReadOnlySpan<byte> p, Span<byte> c, Span<byte> t, byte[] aad) => _chacha.Encrypt(n, p, c, t, aad);
            public void Decrypt(byte[] n, ReadOnlySpan<byte> c, ReadOnlySpan<byte> t, Span<byte> p, byte[] aad) => _chacha.Decrypt(n, c, t, p, aad);
            public void Dispose() => _chacha.Dispose();
        }

        private static IAead CreateAead(byte cipherId, byte[] key) => cipherId == 2
            ? (IsChaChaSupported ? new ChaChaAead(key) : throw new PlatformNotSupportedException("This file uses ChaCha20-Poly1305, which isn't available on this version of Windows."))
            : new GcmAead(key);

        private static void BuildNonce(byte[] header, uint counter, bool last, byte[] nonce)
        {
            header.AsSpan(34, 7).CopyTo(nonce);
            BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(7), counter);
            nonce[11] = last ? (byte)1 : (byte)0;
        }

        private static int ReadFully(Stream s, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = s.Read(buffer, total, buffer.Length - total);
                if (n == 0) break;
                total += n;
            }
            return total;
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
