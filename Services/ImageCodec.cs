using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OgConverter.Services
{
    /// <summary>A decoded image ready for encoding: oriented, optionally resized, with what we learned about it.</summary>
    public class LoadedImage
    {
        public BitmapSource Bitmap { get; init; } = null!;
        public BitmapMetadata? Metadata { get; init; }
        public bool HasAlpha { get; init; }
        /// <summary>Some pixels are neither fully opaque nor fully transparent (soft edges, shadows).</summary>
        public bool HasPartialAlpha { get; init; }
        public int BitsPerChannel { get; init; } = 8;
    }

    /// <summary>Windows Imaging Component (WIC) helpers for the image compression pipeline.</summary>
    public static class ImageCodec
    {
        private const string JpegOrientationQuery = "/app1/ifd/{ushort=274}";
        private const string TiffOrientationQuery = "/ifd/{ushort=274}";

        /// <summary>
        /// Decodes the first frame. Returns null if Windows has no decoder for the format.
        /// With <paramref name="applyOrientation"/> the EXIF rotation is baked into the pixels;
        /// <paramref name="maxDimension"/> &gt; 0 shrinks the longest edge to at most that many pixels.
        /// Without either, pixels and pixel format are untouched (safe for lossless re-encoding).
        /// </summary>
        public static LoadedImage? TryLoad(string path, bool applyOrientation, int maxDimension)
        {
            BitmapFrame frame;
            try
            {
                using var fs = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) return null;
                frame = decoder.Frames[0];
            }
            catch
            {
                return null;
            }

            BitmapMetadata? metadata = null;
            try { metadata = (frame.Metadata as BitmapMetadata)?.Clone(); } catch { }

            BitmapSource bmp = frame;
            var format = bmp.Format;
            bool hasAlpha = format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32 || format == PixelFormats.Rgba64 ||
                            format == PixelFormats.Prgba64 || format == PixelFormats.Rgba128Float || format == PixelFormats.Prgba128Float ||
                            (bmp.Palette?.Colors is { } colors && HasTransparentEntry(colors));
            int bits = format.BitsPerPixel / Math.Max(1, ChannelCount(format));

            int orientation = applyOrientation ? ReadOrientation(metadata) : 1;
            bool resize = maxDimension > 0 && Math.Max(bmp.PixelWidth, bmp.PixelHeight) > maxDimension;

            if (orientation != 1 || resize)
            {
                // Transforms need a plain 8-bit format (indexed and 16-bit formats don't survive them everywhere)
                bmp = new FormatConvertedBitmap(bmp, hasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24, null, 0);
                bits = 8;
                if (orientation != 1) bmp = Orient(bmp, orientation);
                if (resize)
                {
                    double s = (double)maxDimension / Math.Max(bmp.PixelWidth, bmp.PixelHeight);
                    bmp = Resize(bmp, s);
                }
            }

            bool partial = false;
            bool anyAlpha = false;
            if (hasAlpha) (anyAlpha, partial) = ScanAlpha(bmp);

            bmp.Freeze();
            return new LoadedImage
            {
                Bitmap = bmp,
                Metadata = metadata,
                HasAlpha = anyAlpha,
                HasPartialAlpha = partial,
                BitsPerChannel = bits
            };
        }

        /// <summary>Downscale with WIC's scaler (thread-safe, no visual tree needed).</summary>
        private static BitmapSource Resize(BitmapSource src, double scale)
        {
            int w = Math.Max(1, (int)Math.Round(src.PixelWidth * scale));
            int h = Math.Max(1, (int)Math.Round(src.PixelHeight * scale));
            return new TransformedBitmap(src, new ScaleTransform((double)w / src.PixelWidth, (double)h / src.PixelHeight));
        }

        public static int ReadOrientation(BitmapMetadata? metadata)
        {
            if (metadata == null) return 1;
            foreach (string q in new[] { JpegOrientationQuery, TiffOrientationQuery })
            {
                try
                {
                    if (metadata.ContainsQuery(q) && metadata.GetQuery(q) is ushort v && v is >= 1 and <= 8) return v;
                }
                catch
                {
                    // Query not valid for this container
                }
            }
            return 1;
        }

        /// <summary>Applies an EXIF orientation (1–8) so the pixels display upright without the tag.</summary>
        private static BitmapSource Orient(BitmapSource src, int orientation)
        {
            Transform t = orientation switch
            {
                2 => new ScaleTransform(-1, 1),
                3 => new RotateTransform(180),
                4 => new ScaleTransform(1, -1),
                5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
                6 => new RotateTransform(90),
                7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
                8 => new RotateTransform(270),
                _ => Transform.Identity
            };
            return new TransformedBitmap(src, t);
        }

        private static bool HasTransparentEntry(System.Collections.Generic.IList<Color> colors)
        {
            foreach (var c in colors) if (c.A < 255) return true;
            return false;
        }

        private static int ChannelCount(PixelFormat f)
        {
            if (f == PixelFormats.Gray16 || f == PixelFormats.Gray8 || f == PixelFormats.Gray32Float) return 1;
            if (f == PixelFormats.Rgb48 || f == PixelFormats.Bgr24 || f == PixelFormats.Rgb24) return 3;
            if (f == PixelFormats.Rgba64 || f == PixelFormats.Prgba64 || f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32) return 4;
            if (f == PixelFormats.Bgr32) return 4;
            return Math.Max(1, f.BitsPerPixel / 8);
        }

        /// <summary>(any pixel not opaque, any pixel semi-transparent).</summary>
        private static (bool Any, bool Partial) ScanAlpha(BitmapSource bmp)
        {
            var bgra = bmp.Format == PixelFormats.Bgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int stride = bgra.PixelWidth * 4;
            var row = new byte[stride];
            bool any = false;
            for (int y = 0; y < bgra.PixelHeight; y++)
            {
                bgra.CopyPixels(new Int32Rect(0, y, bgra.PixelWidth, 1), row, stride, 0);
                for (int i = 3; i < stride; i += 4)
                {
                    byte a = row[i];
                    if (a == 255) continue;
                    any = true;
                    if (a != 0) return (true, true);
                }
            }
            return (any, false);
        }

        /// <summary>For formats without transparency: composite onto white instead of letting transparent areas turn black.</summary>
        public static BitmapSource FlattenIfTransparent(LoadedImage image)
        {
            if (!image.HasAlpha) return image.Bitmap;
            var src = image.Bitmap.Format == PixelFormats.Bgra32 ? image.Bitmap : new FormatConvertedBitmap(image.Bitmap, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            var bgra = new byte[w * h * 4];
            src.CopyPixels(bgra, w * 4, 0);
            var bgr = new byte[w * h * 3];
            for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
            {
                int a = bgra[i + 3];
                bgr[j] = (byte)((bgra[i] * a + 255 * (255 - a)) / 255);
                bgr[j + 1] = (byte)((bgra[i + 1] * a + 255 * (255 - a)) / 255);
                bgr[j + 2] = (byte)((bgra[i + 2] * a + 255 * (255 - a)) / 255);
            }
            var flat = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, bgr, w * 3);
            flat.Freeze();
            return flat;
        }

        public static void SaveJpeg(BitmapSource bmp, string path, int quality, BitmapMetadata? keepMetadata)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
            BitmapSource pixels = bmp.Format == PixelFormats.Bgr24 || bmp.Format == PixelFormats.Gray8 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0);

            BitmapMetadata? meta = null;
            if (keepMetadata != null && keepMetadata.Format == "jpg")
            {
                try
                {
                    meta = keepMetadata.Clone();
                    // Pixels are already upright
                    if (meta.ContainsQuery(JpegOrientationQuery)) meta.SetQuery(JpegOrientationQuery, (ushort)1);
                }
                catch
                {
                    meta = null;
                }
            }

            encoder.Frames.Add(BitmapFrame.Create(pixels, null, meta, null));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }

        public static void SavePng(BitmapSource bmp, string path)
        {
            var encoder = new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }

        public static void SaveTiff(BitmapSource bmp, string path)
        {
            var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Zip };
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            encoder.Save(fs);
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
                using var ms = new MemoryStream(b, false);
                var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                orientation = ReadOrientation(frame.Metadata as BitmapMetadata);
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
