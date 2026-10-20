using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OgConverter.Platform.Linux
{
    /// <summary>BMP and GIF writers (Skia only encodes PNG, JPEG and WebP).</summary>
    internal static class LegacyEncoders
    {
        // ------------------------------------------------------------------ BMP

        /// <summary>
        /// Writes a bottom-up BMP: 8-bit gray with a gray palette, 24-bit BGR, or 32-bit BGRA with an alpha mask
        /// (BITMAPV4HEADER) so readers keep the transparency.
        /// </summary>
        public static void WriteBmp(Stream output, byte[] samples, int width, int height, int channels, double dpi)
        {
            int headerSize = channels == 4 ? 108 : 40;
            int paletteSize = channels == 1 ? 256 * 4 : 0;
            int rowBytes = (width * channels * 8 + 31) / 32 * 4;
            int pixelOffset = 14 + headerSize + paletteSize;
            int fileSize = pixelOffset + rowBytes * height;
            int ppm = (int)Math.Round((dpi > 1 ? dpi : 96) / 0.0254);

            using var w = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
            w.Write((byte)'B');
            w.Write((byte)'M');
            w.Write(fileSize);
            w.Write(0);
            w.Write(pixelOffset);

            w.Write(headerSize);
            w.Write(width);
            w.Write(height); // positive: bottom-up
            w.Write((short)1);
            w.Write((short)(channels * 8));
            w.Write(channels == 4 ? 3 : 0); // BI_BITFIELDS or BI_RGB
            w.Write(rowBytes * height);
            w.Write(ppm);
            w.Write(ppm);
            w.Write(channels == 1 ? 256 : 0);
            w.Write(0);
            if (channels == 4)
            {
                w.Write(0x00FF0000); // red
                w.Write(0x0000FF00); // green
                w.Write(0x000000FF); // blue
                w.Write(unchecked((int)0xFF000000)); // alpha
                w.Write(0x73524742); // LCS_sRGB
                w.Write(new byte[36 + 12]); // endpoints and gamma, unused with sRGB
            }
            if (channels == 1)
            {
                for (int i = 0; i < 256; i++)
                {
                    w.Write((byte)i);
                    w.Write((byte)i);
                    w.Write((byte)i);
                    w.Write((byte)0);
                }
            }

            var row = new byte[rowBytes];
            for (int y = height - 1; y >= 0; y--)
            {
                Buffer.BlockCopy(samples, y * width * channels, row, 0, width * channels);
                w.Write(row);
            }
        }

        // ------------------------------------------------------------------ GIF

        /// <summary>
        /// Writes a single-frame GIF from BGRA pixels. Pictures with up to 256 colors keep them exactly; others are reduced
        /// with a median cut. Pixels under half opacity become the transparent color.
        /// </summary>
        public static void WriteGif(Stream output, byte[] bgra, int width, int height)
        {
            int count = width * height;
            bool transparent = false;
            var histogram = new Dictionary<int, int>();
            for (int i = 0; i < count; i++)
            {
                if (bgra[i * 4 + 3] < 128) { transparent = true; continue; }
                int rgb = Rgb(bgra, i);
                histogram[rgb] = histogram.TryGetValue(rgb, out int n) ? n + 1 : 1;
            }

            int maxColors = transparent ? 255 : 256;
            List<int> palette = histogram.Count <= maxColors ? histogram.Keys.ToList() : MedianCut(histogram, maxColors);
            if (palette.Count == 0) palette.Add(0);
            int transparentIndex = transparent ? palette.Count : -1;

            // Index for every pixel: exact when the color is in the palette, otherwise the nearest one
            var exact = new Dictionary<int, byte>();
            for (int i = 0; i < palette.Count; i++) exact[palette[i]] = (byte)i;
            var indices = new byte[count];
            for (int i = 0; i < count; i++)
            {
                if (transparent && bgra[i * 4 + 3] < 128) { indices[i] = (byte)transparentIndex; continue; }
                int rgb = Rgb(bgra, i);
                if (!exact.TryGetValue(rgb, out byte index))
                {
                    index = Nearest(palette, rgb);
                    exact[rgb] = index;
                }
                indices[i] = index;
            }

            int used = palette.Count + (transparent ? 1 : 0);
            int tableBits = 1;
            while ((1 << tableBits) < used) tableBits++;

            using var w = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
            w.Write("GIF89a".ToCharArray());
            w.Write((ushort)width);
            w.Write((ushort)height);
            w.Write((byte)(0x80 | 0x70 | (tableBits - 1))); // global color table, 8-bit color resolution
            w.Write((byte)0); // background color index
            w.Write((byte)0); // no aspect ratio
            for (int i = 0; i < 1 << tableBits; i++)
            {
                int rgb = i < palette.Count ? palette[i] : 0;
                w.Write((byte)(rgb >> 16));
                w.Write((byte)(rgb >> 8));
                w.Write((byte)rgb);
            }

            if (transparent)
            {
                // Graphic control extension: transparent color index
                w.Write(new byte[] { 0x21, 0xF9, 4, 0x01, 0, 0, (byte)transparentIndex, 0 });
            }

            w.Write((byte)0x2C); // image descriptor
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)width);
            w.Write((ushort)height);
            w.Write((byte)0);

            int minCodeSize = Math.Max(2, tableBits);
            w.Write((byte)minCodeSize);
            byte[] lzw = Lzw(indices, minCodeSize);
            for (int pos = 0; pos < lzw.Length; pos += 255)
            {
                int n = Math.Min(255, lzw.Length - pos);
                w.Write((byte)n);
                w.Write(lzw, pos, n);
            }
            w.Write((byte)0); // end of image data
            w.Write((byte)0x3B); // trailer
        }

        private static int Rgb(byte[] bgra, int i) => (bgra[i * 4 + 2] << 16) | (bgra[i * 4 + 1] << 8) | bgra[i * 4];

        private static byte Nearest(List<int> palette, int rgb)
        {
            int r = rgb >> 16, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            int best = 0, bestDistance = int.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                int p = palette[i];
                int dr = (p >> 16) - r, dg = ((p >> 8) & 0xFF) - g, db = (p & 0xFF) - b;
                int d = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return (byte)best;
        }

        /// <summary>Median cut: splits the box with the widest channel range at its weighted median until there are enough.</summary>
        private static List<int> MedianCut(Dictionary<int, int> histogram, int maxColors)
        {
            var boxes = new List<List<KeyValuePair<int, int>>> { histogram.ToList() };
            while (boxes.Count < maxColors)
            {
                int pick = -1, pickRange = 0, pickShift = 0;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].Count < 2) continue;
                    foreach (int shift in new[] { 16, 8, 0 })
                    {
                        int min = 255, max = 0;
                        foreach (var c in boxes[i])
                        {
                            int v = (c.Key >> shift) & 0xFF;
                            if (v < min) min = v;
                            if (v > max) max = v;
                        }
                        if (max - min > pickRange) { pickRange = max - min; pick = i; pickShift = shift; }
                    }
                }
                if (pick < 0) break;

                var box = boxes[pick];
                box.Sort((a, b) => ((a.Key >> pickShift) & 0xFF).CompareTo((b.Key >> pickShift) & 0xFF));
                long total = box.Sum(c => (long)c.Value), half = 0;
                int split = 1;
                for (int i = 0; i < box.Count - 1; i++)
                {
                    half += box[i].Value;
                    if (half * 2 >= total) { split = i + 1; break; }
                }
                boxes[pick] = box.GetRange(0, split);
                boxes.Add(box.GetRange(split, box.Count - split));
            }

            // Each box's weighted average color
            return boxes.Select(box =>
            {
                long r = 0, g = 0, b = 0, n = 0;
                foreach (var c in box)
                {
                    r += (long)(c.Key >> 16) * c.Value;
                    g += (long)((c.Key >> 8) & 0xFF) * c.Value;
                    b += (long)(c.Key & 0xFF) * c.Value;
                    n += c.Value;
                }
                return (int)((r / n) << 16 | (g / n) << 8 | (b / n));
            }).Distinct().ToList();
        }

        /// <summary>GIF's variable-length LZW, codes packed least significant bit first.</summary>
        private static byte[] Lzw(byte[] indices, int minCodeSize)
        {
            int clear = 1 << minCodeSize, end = clear + 1;
            int codeSize = minCodeSize + 1, maxCode = end;
            var table = new Dictionary<int, int>();
            using var ms = new MemoryStream();
            int buffer = 0, bits = 0;

            void Emit(int code)
            {
                buffer |= code << bits;
                bits += codeSize;
                while (bits >= 8)
                {
                    ms.WriteByte((byte)buffer);
                    buffer >>= 8;
                    bits -= 8;
                }
            }

            Emit(clear);
            int current = -1;
            foreach (byte index in indices)
            {
                if (current < 0) { current = index; continue; }
                int key = (current << 8) | index;
                if (table.TryGetValue(key, out int code)) { current = code; continue; }

                Emit(current);
                table[key] = ++maxCode;
                if (maxCode >= 1 << codeSize) codeSize++;
                if (maxCode == 4095)
                {
                    Emit(clear);
                    table.Clear();
                    codeSize = minCodeSize + 1;
                    maxCode = end;
                }
                current = index;
            }
            if (current >= 0) Emit(current);
            Emit(end);
            if (bits > 0) ms.WriteByte((byte)buffer);
            return ms.ToArray();
        }
    }
}
