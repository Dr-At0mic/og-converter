using System;

namespace OgConverter.Platform.Linux
{
    /// <summary>Details Skia's decoders don't report, read from the file's headers: stored resolution and palette use.</summary>
    internal static class ImageHeaders
    {
        /// <summary>Resolution stored in a PNG (pHYs), JPEG (JFIF) or BMP header; 96 when there's none.</summary>
        public static (double X, double Y) ReadDpi(byte[] b)
        {
            try
            {
                if (IsPng(b))
                {
                    // Chunks: length, type, data, CRC
                    for (int pos = 8; pos + 8 <= b.Length;)
                    {
                        int length = BigInt(b, pos);
                        string type = System.Text.Encoding.ASCII.GetString(b, pos + 4, 4);
                        if (type == "pHYs" && length >= 9 && pos + 17 <= b.Length && b[pos + 16] == 1)
                        {
                            // Pixels per metre
                            return (Dpi(BigInt(b, pos + 8) * 0.0254), Dpi(BigInt(b, pos + 12) * 0.0254));
                        }
                        if (type is "IDAT" or "IEND" || length < 0) break;
                        pos += 12 + length;
                    }
                }
                else if (b.Length > 20 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF && b[3] == 0xE0 &&
                         b[6] == 'J' && b[7] == 'F' && b[8] == 'I' && b[9] == 'F')
                {
                    int unit = b[13], x = (b[14] << 8) | b[15], y = (b[16] << 8) | b[17];
                    if (unit == 1) return (Dpi(x), Dpi(y));
                    if (unit == 2) return (Dpi(x * 2.54), Dpi(y * 2.54));
                }
                else if (b.Length > 46 && b[0] == 'B' && b[1] == 'M')
                {
                    int x = BitConverter.ToInt32(b, 38), y = BitConverter.ToInt32(b, 42);
                    if (x > 0 && y > 0) return (Dpi(x * 0.0254), Dpi(y * 0.0254));
                }
            }
            catch (ArgumentException)
            {
                // Truncated header
            }
            return (96, 96);
        }

        /// <summary>Indexed colors: palette PNGs, GIFs and BMPs with 8 bits per pixel or fewer.</summary>
        public static bool HasPalette(byte[] b)
        {
            if (IsPng(b) && b.Length > 25) return b[25] == 3; // IHDR color type 3
            if (b.Length > 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F') return true;
            if (b.Length > 30 && b[0] == 'B' && b[1] == 'M') return BitConverter.ToUInt16(b, 28) <= 8;
            return false;
        }

        private static bool IsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G';

        private static int BigInt(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

        /// <summary>Rounded like the stored value was meant (2834 px/m is 72 dpi, not 71.98); 96 for nonsense.</summary>
        private static double Dpi(double value) => value is > 1 and < 100_000 ? Math.Round(value, 2) : 96;
    }
}
