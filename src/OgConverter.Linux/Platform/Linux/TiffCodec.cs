using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using OgConverter.Services;
using SkiaSharp;

namespace OgConverter.Platform.Linux
{
    /// <summary>
    /// Baseline TIFF, which Skia doesn't read or write. Reads the first image: strips or tiles, uncompressed, LZW,
    /// Deflate or PackBits, with or without the horizontal predictor; 1-, 4-, 8- and 16-bit gray, palette, RGB(A) and
    /// CMYK. Writes 8-bit gray, RGB or RGBA, uncompressed or Deflate. Other variants (JPEG-in-TIFF, fax) return null so
    /// the caller can fall back to FFmpeg.
    /// </summary>
    internal static class TiffCodec
    {
        public static bool IsTiff(byte[] b) =>
            b.Length >= 8 && (b[0] == 'I' && b[1] == 'I' && b[2] == 42 && b[3] == 0 || b[0] == 'M' && b[1] == 'M' && b[2] == 0 && b[3] == 42);

        // ------------------------------------------------------------------ Reading

        private sealed class Ifd
        {
            public readonly Dictionary<int, long[]> Tags = new();
            public long One(int tag, long fallback = 0) => Tags.TryGetValue(tag, out var v) && v.Length > 0 ? v[0] : fallback;
            public long[] Many(int tag) => Tags.TryGetValue(tag, out var v) ? v : Array.Empty<long>();
        }

        private static Ifd ReadFirstIfd(byte[] b, out bool big)
        {
            big = b[0] == 'M';
            long offset = U32(b, 4, big);
            if (offset < 8 || offset + 2 > b.Length) throw new InvalidDataException("Damaged TIFF.");
            var ifd = new Ifd();
            int count = U16(b, (int)offset, big);
            for (int i = 0; i < count; i++)
            {
                int at = (int)offset + 2 + i * 12;
                if (at + 12 > b.Length) break;
                int tag = U16(b, at, big), type = U16(b, at + 2, big);
                long n = U32(b, at + 4, big);
                int size = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 => 4, 5 or 10 or 12 => 8, _ => 0 };
                if (size == 0 || n <= 0 || n > 1 << 24) continue;
                long dataAt = size * n <= 4 ? at + 8 : U32(b, at + 8, big);
                if (dataAt < 0 || dataAt + size * n > b.Length) continue;
                var values = new long[type is 5 or 10 ? n * 2 : n];
                for (int k = 0; k < n; k++)
                {
                    int p = (int)(dataAt + k * size);
                    switch (type)
                    {
                        case 3 or 8: values[k] = U16(b, p, big); break;
                        case 4 or 9: values[k] = U32(b, p, big); break;
                        case 5 or 10: values[2 * k] = U32(b, p, big); values[2 * k + 1] = U32(b, p + 4, big); break;
                        default: values[k] = b[p]; break;
                    }
                }
                ifd.Tags[tag] = values;
            }
            return ifd;
        }

        public static (int Width, int Height) ReadSize(byte[] b)
        {
            var ifd = ReadFirstIfd(b, out _);
            return ((int)ifd.One(256), (int)ifd.One(257));
        }

        public static int ReadOrientation(byte[] b)
        {
            try
            {
                return ReadFirstIfd(b, out _).One(274, 1) is long o and >= 1 and <= 8 ? (int)o : 1;
            }
            catch (InvalidDataException)
            {
                return 1;
            }
        }

        /// <summary>The first image as BGRA (or gray), or null for variants this reader doesn't handle.</summary>
        public static SkiaRasterImage? Decode(byte[] b)
        {
            var ifd = ReadFirstIfd(b, out bool big);
            int width = (int)ifd.One(256), height = (int)ifd.One(257);
            int compression = (int)ifd.One(259, 1), photometric = (int)ifd.One(262, 1);
            int samples = (int)ifd.One(277, 1), planar = (int)ifd.One(284, 1), predictor = (int)ifd.One(317, 1);
            long[] bitsPerSample = ifd.Many(258);
            int bits = bitsPerSample.Length > 0 ? (int)bitsPerSample[0] : 1;
            if (width <= 0 || height <= 0 || (long)width * height > 1L << 28) return null;
            if (compression is not (1 or 5 or 8 or 32946 or 32773)) return null;
            if (bits is not (1 or 2 or 4 or 8 or 16) || planar is not (1 or 2)) return null;
            if (photometric is not (0 or 1 or 2 or 3 or 5)) return null;

            // Rows of samples, each row padded to a byte
            int samplesPerPlane = planar == 1 ? samples : 1;
            int planes = planar == 1 ? 1 : samples;
            int rowBytes = (width * samplesPerPlane * bits + 7) / 8;
            var plane = new byte[planes][];
            for (int p = 0; p < planes; p++) plane[p] = new byte[rowBytes * height];

            bool tiled = ifd.Tags.ContainsKey(322);
            long[] offsets = tiled ? ifd.Many(324) : ifd.Many(273);
            long[] counts = tiled ? ifd.Many(325) : ifd.Many(279);
            if (offsets.Length == 0) return null;

            if (tiled)
            {
                int tw = (int)ifd.One(322), th = (int)ifd.One(323);
                if (tw <= 0 || th <= 0) return null;
                int across = (width + tw - 1) / tw, down = (height + th - 1) / th;
                int tileRow = (tw * samplesPerPlane * bits + 7) / 8;
                for (int i = 0; i < offsets.Length; i++)
                {
                    int p = i / (across * down), t = i % (across * down);
                    if (p >= planes) break;
                    byte[] tile = Unpack(b, offsets[i], i < counts.Length ? counts[i] : b.Length - offsets[i], compression, tileRow * th);
                    Unpredict(tile, tileRow, th, tw, samplesPerPlane, bits, predictor, big);
                    int tx = t % across * tw, ty = t / across * th;
                    int copyBytes = Math.Min(tileRow, rowBytes - tx * samplesPerPlane * bits / 8);
                    for (int y = 0; y < th && ty + y < height; y++)
                    {
                        Buffer.BlockCopy(tile, y * tileRow, plane[p], (ty + y) * rowBytes + tx * samplesPerPlane * bits / 8, copyBytes);
                    }
                }
            }
            else
            {
                int rowsPerStrip = (int)Math.Min(ifd.One(278, height), height);
                if (rowsPerStrip <= 0) rowsPerStrip = height;
                int stripsPerPlane = (height + rowsPerStrip - 1) / rowsPerStrip;
                for (int i = 0; i < offsets.Length; i++)
                {
                    int p = i / stripsPerPlane, s = i % stripsPerPlane;
                    if (p >= planes) break;
                    int rows = Math.Min(rowsPerStrip, height - s * rowsPerStrip);
                    byte[] strip = Unpack(b, offsets[i], i < counts.Length ? counts[i] : b.Length - offsets[i], compression, rowBytes * rows);
                    Unpredict(strip, rowBytes, rows, width, samplesPerPlane, bits, predictor, big);
                    Buffer.BlockCopy(strip, 0, plane[p], s * rowsPerStrip * rowBytes, Math.Min(strip.Length, rowBytes * rows));
                }
            }

            // Samples → 8-bit values per pixel
            byte Sample(int x, int y, int c)
            {
                byte[] data = planar == 1 ? plane[0] : plane[c];
                int index = planar == 1 ? x * samples + c : x;
                int row = y * rowBytes;
                switch (bits)
                {
                    case 8: return data[row + index];
                    case 16:
                    {
                        int at = row + index * 2;
                        return big ? data[at] : data[at + 1];
                    }
                    default:
                    {
                        int bit = index * bits;
                        int v = (data[row + bit / 8] >> (8 - bits - bit % 8)) & ((1 << bits) - 1);
                        return photometric == 3 ? (byte)v : (byte)(v * 255 / ((1 << bits) - 1));
                    }
                }
            }

            long[] extra = ifd.Many(338);
            int colorSamples = photometric switch { 2 => 3, 5 => 4, _ => 1 };
            bool alpha = samples > colorSamples;
            bool associated = alpha && extra.Length > 0 && extra[0] == 1;
            long[] colorMap = ifd.Many(320);
            if (photometric == 3 && colorMap.Length < 3 << bits) return null;

            if (!alpha && photometric is 0 or 1)
            {
                var gray = new byte[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte v = Sample(x, y, 0);
                        gray[y * width + x] = photometric == 0 ? (byte)(255 - v) : v;
                    }
                }
                var grayBitmap = SkiaImageBackend.Create(width, height, SKColorType.Gray8, SKAlphaType.Opaque, gray);
                return Result(grayBitmap, RasterFormat.Gray8, ifd, bits == 1);
            }

            var bgra = new byte[width * height * 4];
            int entries = 1 << bits;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int o = (y * width + x) * 4;
                    byte r, g, bl, a = alpha ? Sample(x, y, colorSamples) : (byte)255;
                    switch (photometric)
                    {
                        case 2:
                            r = Sample(x, y, 0); g = Sample(x, y, 1); bl = Sample(x, y, 2);
                            break;
                        case 3:
                        {
                            int i = Sample(x, y, 0);
                            r = (byte)(colorMap[i] >> 8); g = (byte)(colorMap[entries + i] >> 8); bl = (byte)(colorMap[2 * entries + i] >> 8);
                            break;
                        }
                        case 5:
                        {
                            int k = 255 - Sample(x, y, 3);
                            r = (byte)((255 - Sample(x, y, 0)) * k / 255); g = (byte)((255 - Sample(x, y, 1)) * k / 255); bl = (byte)((255 - Sample(x, y, 2)) * k / 255);
                            break;
                        }
                        default:
                        {
                            byte v = Sample(x, y, 0);
                            r = g = bl = photometric == 0 ? (byte)(255 - v) : v;
                            break;
                        }
                    }
                    if (associated && a is > 0 and < 255)
                    {
                        r = (byte)Math.Min(255, r * 255 / a);
                        g = (byte)Math.Min(255, g * 255 / a);
                        bl = (byte)Math.Min(255, bl * 255 / a);
                    }
                    bgra[o] = bl;
                    bgra[o + 1] = g;
                    bgra[o + 2] = r;
                    bgra[o + 3] = a;
                }
            }
            var bitmap = SkiaImageBackend.Create(width, height, SKColorType.Bgra8888, alpha ? SKAlphaType.Unpremul : SKAlphaType.Opaque, bgra);
            return Result(bitmap, alpha ? RasterFormat.Bgra32 : RasterFormat.Bgr24, ifd, photometric == 3);
        }

        private static SkiaRasterImage Result(SKBitmap bitmap, RasterFormat format, Ifd ifd, bool palette)
        {
            double Resolution(int tag)
            {
                long[] r = ifd.Many(tag);
                if (r.Length < 2 || r[1] == 0 || r[0] == 0) return 96;
                double value = (double)r[0] / r[1];
                return ifd.One(296, 2) switch { 3 => value * 2.54, 1 => 96, _ => value };
            }
            var metadata = new SkiaImageMetadata { Orientation = ifd.One(274, 1) is long o and >= 1 and <= 8 ? (int)o : 1 };
            return new SkiaRasterImage(bitmap, format, Resolution(282), Resolution(283), palette, metadata);
        }

        private static byte[] Unpack(byte[] b, long offset, long count, int compression, int expected)
        {
            if (offset < 0 || offset >= b.Length) return new byte[expected];
            int length = (int)Math.Min(count, b.Length - offset);
            var input = new ReadOnlySpan<byte>(b, (int)offset, length);
            switch (compression)
            {
                case 1:
                {
                    var raw = new byte[expected];
                    input[..Math.Min(length, expected)].CopyTo(raw);
                    return raw;
                }
                case 8 or 32946:
                {
                    var output = new byte[expected];
                    using var z = new ZLibStream(new MemoryStream(b, (int)offset, length), CompressionMode.Decompress);
                    int read = 0;
                    try
                    {
                        while (read < expected)
                        {
                            int n = z.Read(output, read, expected - read);
                            if (n == 0) break;
                            read += n;
                        }
                    }
                    catch (InvalidDataException)
                    {
                        // Keep what was decoded
                    }
                    return output;
                }
                case 32773:
                    return PackBits(input, expected);
                default:
                    return Lzw(input, expected);
            }
        }

        private static byte[] PackBits(ReadOnlySpan<byte> input, int expected)
        {
            var output = new byte[expected];
            int i = 0, o = 0;
            while (i < input.Length && o < expected)
            {
                int n = (sbyte)input[i++];
                if (n >= 0)
                {
                    int copy = Math.Min(n + 1, Math.Min(input.Length - i, expected - o));
                    input.Slice(i, copy).CopyTo(output.AsSpan(o));
                    i += n + 1;
                    o += copy;
                }
                else if (n != -128 && i < input.Length)
                {
                    byte v = input[i++];
                    for (int k = 0; k < 1 - n && o < expected; k++) output[o++] = v;
                }
            }
            return output;
        }

        /// <summary>TIFF LZW: codes MSB first, code width grows one code early.</summary>
        private static byte[] Lzw(ReadOnlySpan<byte> input, int expected)
        {
            var output = new byte[expected];
            var prefix = new int[4096];
            var suffix = new byte[4096];
            var length = new int[4096];
            for (int i = 0; i < 256; i++) { prefix[i] = -1; suffix[i] = (byte)i; length[i] = 1; }
            int next = 258, codeSize = 9, o = 0, old = -1;
            long bitPos = 0, totalBits = (long)input.Length * 8;
            var stack = new byte[4096];

            while (bitPos + codeSize <= totalBits && o < expected)
            {
                int code = 0;
                for (int k = 0; k < codeSize; k++, bitPos++) code = (code << 1) | ((input[(int)(bitPos >> 3)] >> (7 - (int)(bitPos & 7))) & 1);
                if (code == 257) break;
                if (code == 256)
                {
                    next = 258;
                    codeSize = 9;
                    old = -1;
                    continue;
                }

                int entry = code < next ? code : old;
                if (entry < 0 || (code > next)) break;
                // Write the entry's string (and for a code not yet in the table, the old string plus its first byte)
                int n = 0;
                for (int c = entry; c >= 0 && n < stack.Length; c = prefix[c]) stack[n++] = suffix[c];
                byte first = stack[n - 1];
                for (int k = n - 1; k >= 0 && o < expected; k--) output[o++] = stack[k];
                if (code >= next && o < expected) output[o++] = first;

                if (old >= 0 && next < 4096)
                {
                    prefix[next] = old;
                    suffix[next] = first;
                    length[next] = length[old] + 1;
                    next++;
                }
                old = code;
                if (next + 1 >= 1 << codeSize && codeSize < 12) codeSize++;
            }
            return output;
        }

        /// <summary>Undoes the horizontal differencing predictor (2) in place.</summary>
        private static void Unpredict(byte[] data, int rowBytes, int rows, int width, int samples, int bits, int predictor, bool big)
        {
            if (predictor != 2) return;
            for (int y = 0; y < rows; y++)
            {
                int row = y * rowBytes;
                if (bits == 8)
                {
                    for (int i = samples; i < width * samples && row + i < data.Length; i++) data[row + i] += data[row + i - samples];
                }
                else if (bits == 16)
                {
                    for (int i = samples; i < width * samples && row + i * 2 + 1 < data.Length; i++)
                    {
                        int at = row + i * 2, prev = at - samples * 2;
                        int v = (big ? (data[at] << 8) | data[at + 1] : data[at] | (data[at + 1] << 8)) +
                                (big ? (data[prev] << 8) | data[prev + 1] : data[prev] | (data[prev + 1] << 8));
                        if (big) { data[at] = (byte)(v >> 8); data[at + 1] = (byte)v; }
                        else { data[at] = (byte)v; data[at + 1] = (byte)(v >> 8); }
                    }
                }
            }
        }

        private static int U16(byte[] b, int at, bool big) => big ? (b[at] << 8) | b[at + 1] : b[at] | (b[at + 1] << 8);

        private static long U32(byte[] b, int at, bool big) =>
            (uint)(big ? (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3] : b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24));

        // ------------------------------------------------------------------ Writing

        /// <summary>
        /// Writes a little-endian baseline TIFF from 8-bit gray, BGR or BGRA samples. With <paramref name="deflate"/> each
        /// strip is zlib-compressed after the horizontal predictor (the usual lossless setting).
        /// </summary>
        public static void Encode(Stream output, byte[] samples, int width, int height, int channels, bool deflate, double dpiX, double dpiY)
        {
            int rowBytes = width * channels;
            int rowsPerStrip = Math.Max(1, Math.Min(height, 65536 / Math.Max(1, rowBytes)));
            int strips = (height + rowsPerStrip - 1) / rowsPerStrip;

            // TIFF stores RGB(A): swap from BGR(A)
            var data = (byte[])samples.Clone();
            if (channels >= 3)
            {
                for (int i = 0; i < data.Length; i += channels) (data[i], data[i + 2]) = (data[i + 2], data[i]);
            }

            var stripData = new List<byte[]>(strips);
            for (int s = 0; s < strips; s++)
            {
                int rows = Math.Min(rowsPerStrip, height - s * rowsPerStrip);
                var strip = new byte[rows * rowBytes];
                Buffer.BlockCopy(data, s * rowsPerStrip * rowBytes, strip, 0, strip.Length);
                if (deflate)
                {
                    for (int y = 0; y < rows; y++)
                    {
                        for (int i = rowBytes - 1; i >= channels; i--) strip[y * rowBytes + i] -= strip[y * rowBytes + i - channels];
                    }
                    using var ms = new MemoryStream();
                    using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(strip);
                    strip = ms.ToArray();
                }
                stripData.Add(strip);
            }

            // Tags in ascending order; values that don't fit in 4 bytes go after the directory
            var entries = new List<(int Tag, int Type, int Count, byte[] Value)>();
            byte[] Shorts(params int[] v) { var r = new byte[v.Length * 2]; for (int i = 0; i < v.Length; i++) BitConverter.TryWriteBytes(r.AsSpan(i * 2), (ushort)v[i]); return r; }
            byte[] Longs(params long[] v) { var r = new byte[v.Length * 4]; for (int i = 0; i < v.Length; i++) BitConverter.TryWriteBytes(r.AsSpan(i * 4), (uint)v[i]); return r; }
            byte[] Rational(double v) => Longs((long)Math.Round((v > 1 ? v : 96) * 100), 100);

            var bitsPerSample = new int[channels];
            Array.Fill(bitsPerSample, 8);
            entries.Add((256, 4, 1, Longs(width)));
            entries.Add((257, 4, 1, Longs(height)));
            entries.Add((258, 3, channels, Shorts(bitsPerSample)));
            entries.Add((259, 3, 1, Shorts(deflate ? 8 : 1)));
            entries.Add((262, 3, 1, Shorts(channels == 1 ? 1 : 2)));
            entries.Add((273, 4, strips, new byte[strips * 4])); // strip offsets, filled in below
            entries.Add((277, 3, 1, Shorts(channels)));
            entries.Add((278, 4, 1, Longs(rowsPerStrip)));
            entries.Add((279, 4, strips, Longs(stripData.ConvertAll(s => (long)s.Length).ToArray())));
            entries.Add((282, 5, 1, Rational(dpiX)));
            entries.Add((283, 5, 1, Rational(dpiY)));
            entries.Add((284, 3, 1, Shorts(1)));
            entries.Add((296, 3, 1, Shorts(2))); // inches
            if (deflate) entries.Add((317, 3, 1, Shorts(2)));
            if (channels == 4) entries.Add((338, 3, 1, Shorts(2))); // unassociated alpha

            int ifdOffset = 8;
            int ifdSize = 2 + entries.Count * 12 + 4;
            long extraOffset = ifdOffset + ifdSize;
            var extraOffsets = new long[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Value.Length <= 4) continue;
                extraOffsets[i] = extraOffset;
                extraOffset += entries[i].Value.Length + entries[i].Value.Length % 2;
            }
            long position = extraOffset;
            var stripOffsets = new long[strips];
            for (int s = 0; s < strips; s++)
            {
                stripOffsets[s] = position;
                position += stripData[s].Length;
            }
            int stripIndex = entries.FindIndex(e => e.Tag == 273);
            entries[stripIndex] = (273, 4, strips, Longs(stripOffsets));

            using var w = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
            w.Write((byte)'I');
            w.Write((byte)'I');
            w.Write((ushort)42);
            w.Write(ifdOffset);
            w.Write((ushort)entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                var (tag, type, count, value) = entries[i];
                w.Write((ushort)tag);
                w.Write((ushort)type);
                w.Write(count);
                if (value.Length <= 4)
                {
                    w.Write(value);
                    for (int k = value.Length; k < 4; k++) w.Write((byte)0);
                }
                else
                {
                    w.Write((uint)extraOffsets[i]);
                }
            }
            w.Write(0); // no next directory
            foreach (var e in entries)
            {
                if (e.Value.Length <= 4) continue;
                w.Write(e.Value);
                if (e.Value.Length % 2 == 1) w.Write((byte)0);
            }
            foreach (var strip in stripData) w.Write(strip);
        }
    }
}
