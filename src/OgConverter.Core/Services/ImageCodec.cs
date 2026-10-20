using System;
using System.IO;

namespace OgConverter.Services
{
    /// <summary>A decoded image ready for encoding: oriented, optionally resized, with what we learned about it.</summary>
    public class LoadedImage
    {
        public RasterImage Bitmap { get; init; } = null!;
        /// <summary>The decoder's metadata (camera details, orientation), opaque to everything but <see cref="Platform.Images"/>.</summary>
        public object? Metadata { get; init; }
        public bool HasAlpha { get; init; }
        /// <summary>Some pixels are neither fully opaque nor fully transparent (soft edges, shadows).</summary>
        public bool HasPartialAlpha { get; init; }
        public int BitsPerChannel { get; init; } = 8;
    }

    /// <summary>Image helpers for the compression pipeline. Decoding and encoding go through <see cref="Platform.Images"/>.</summary>
    public static class ImageCodec
    {
        /// <summary>
        /// Decodes the first frame. Returns null if there's no decoder for the format.
        /// With <paramref name="applyOrientation"/> the EXIF rotation is baked into the pixels;
        /// <paramref name="maxDimension"/> &gt; 0 shrinks the longest edge to at most that many pixels.
        /// Without either, pixels and pixel format are untouched (safe for lossless re-encoding).
        /// </summary>
        public static LoadedImage? TryLoad(string path, bool applyOrientation, int maxDimension) =>
            Platform.Images.TryLoad(path, applyOrientation, maxDimension);

        public static int ReadOrientation(object? metadata) => Platform.Images.ReadOrientation(metadata);

        /// <summary>For formats without transparency: composite onto white instead of letting transparent areas turn black.</summary>
        public static RasterImage FlattenIfTransparent(LoadedImage image)
        {
            if (!image.HasAlpha) return image.Bitmap;
            var src = Platform.Images.ToBgra(image.Bitmap);
            int w = src.Width, h = src.Height;
            var bgra = src.Pixels;
            var bgr = new byte[w * h * 3];
            for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
            {
                int a = bgra[i + 3];
                bgr[j] = (byte)((bgra[i] * a + 255 * (255 - a)) / 255);
                bgr[j + 1] = (byte)((bgra[i + 1] * a + 255 * (255 - a)) / 255);
                bgr[j + 2] = (byte)((bgra[i + 2] * a + 255 * (255 - a)) / 255);
            }
            return Platform.Images.FromPixels(w, h, 96, RasterFormat.Bgr24, bgr, w * 3);
        }

        public static void SaveJpeg(RasterImage bmp, string path, int quality, object? keepMetadata) =>
            Platform.Images.SaveJpeg(bmp, path, quality, keepMetadata);

        public static void SavePng(RasterImage bmp, string path)
        {
            using var fs = File.Create(path);
            Platform.Images.Encode(bmp, fs, ImageEncoding.Png(noInterlace: true));
        }

        public static void SaveTiff(RasterImage bmp, string path)
        {
            using var fs = File.Create(path);
            Platform.Images.Encode(bmp, fs, ImageEncoding.Tiff(deflate: true));
        }

        /// <summary>
        /// Lossless JPEG cleanup: copies the compressed image data byte-for-byte and drops metadata segments
        /// (EXIF incl. GPS, XMP, Photoshop, comments, MPF/trailer data). The ICC color profile and Adobe color-transform
        /// marker are kept so colors don't change, and a non-default EXIF orientation is re-added as a tiny EXIF block
        /// so the photo still displays upright.
        /// </summary>
        public static void StripJpegMetadata(string src, string dst)
        {
            byte[] b = File.ReadAllBytes(src);
            if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) throw new InvalidOperationException("This isn't a valid JPEG file.");

            int orientation = 1;
            try
            {
                orientation = Platform.Images.ReadOrientation(b);
            }
            catch { }

            using var output = new MemoryStream(b.Length);
            output.Write(b, 0, 2);
            bool exifWritten = orientation == 1;
            int pos = 2;

            while (pos + 4 <= b.Length)
            {
                if (b[pos] != 0xFF) throw new InvalidOperationException("This JPEG is damaged (unexpected data before the image).");
                byte marker = b[pos + 1];
                if (marker == 0xFF) { pos++; continue; } // fill byte

                if (marker == 0xDA) // start of scan: the rest is image data; stop at the end-of-image marker
                {
                    if (!exifWritten) { WriteOrientationExif(output, orientation); exifWritten = true; }
                    int end = FindEndOfImage(b, pos);
                    output.Write(b, pos, end - pos);
                    break;
                }

                int segLen = (b[pos + 2] << 8) | b[pos + 3];
                int segEnd = pos + 2 + segLen;
                if (segEnd > b.Length) throw new InvalidOperationException("This JPEG is damaged (truncated header).");

                bool keep = marker switch
                {
                    0xE0 => true,                                               // JFIF
                    0xE2 => StartsWith(b, pos + 4, "ICC_PROFILE\0"),            // color profile
                    0xEE => true,                                               // Adobe (YCbCr/CMYK transform)
                    >= 0xE1 and <= 0xEF => false,                               // EXIF, XMP, MPF, Photoshop, …
                    0xFE => false,                                              // comment
                    _ => true                                                   // tables, frame header, restart interval
                };
                // Orientation block goes right after JFIF, or straight after the start marker if there's no JFIF
                if (!exifWritten && marker != 0xE0)
                {
                    WriteOrientationExif(output, orientation);
                    exifWritten = true;
                }
                if (keep) output.Write(b, pos, segEnd - pos);
                pos = segEnd;
            }

            File.WriteAllBytes(dst, output.ToArray());
        }

        private static int FindEndOfImage(byte[] b, int from)
        {
            for (int i = from; i + 1 < b.Length; i++)
            {
                if (b[i] == 0xFF && b[i + 1] == 0xD9) return i + 2;
            }
            return b.Length;
        }

        private static bool StartsWith(byte[] b, int offset, string ascii)
        {
            if (offset + ascii.Length > b.Length) return false;
            for (int i = 0; i < ascii.Length; i++) if (b[offset + i] != ascii[i]) return false;
            return true;
        }

        /// <summary>Minimal APP1 EXIF segment containing only the orientation tag (big-endian TIFF).</summary>
        private static void WriteOrientationExif(Stream s, int orientation)
        {
            byte[] seg =
            {
                0xFF, 0xE1, 0x00, 34,
                (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
                (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, // TIFF header, IFD0 at offset 8
                0x00, 0x01,                                              // one entry
                0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01,          // tag 0x0112 Orientation, SHORT, count 1
                0x00, (byte)orientation, 0x00, 0x00,                     // value
                0x00, 0x00, 0x00, 0x00                                   // no next IFD
            };
            s.Write(seg, 0, seg.Length);
        }
    }
}
