using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using OgConverter.Models;
using OgConverter.Services;
using SkiaSharp;

namespace OgConverter.Platform.Linux
{
    /// <summary>What the Skia backend learned from a file's headers: EXIF orientation, and a JPEG's EXIF and color profile.</summary>
    public sealed class SkiaImageMetadata
    {
        public int Orientation { get; init; } = 1;
        /// <summary>The EXIF block ("Exif\0\0" + TIFF data) of a JPEG; null for other formats.</summary>
        public byte[]? JpegExif { get; init; }
        /// <summary>A JPEG's ICC profile segments, kept so re-encoded colors don't change.</summary>
        public IReadOnlyList<byte[]> JpegIcc { get; init; } = Array.Empty<byte[]>();
    }

    /// <summary>
    /// A Skia bitmap as the portable code sees it. 24- and 32-bit color are both stored as BGRA (opaque for
    /// <see cref="RasterFormat.Bgr24"/>, unpremultiplied otherwise); <see cref="Format"/> keeps the logical layout.
    /// </summary>
    public sealed class SkiaRasterImage : RasterImage
    {
        public SkiaRasterImage(SKBitmap bitmap, RasterFormat format, double dpiX = 96, double dpiY = 96, bool palette = false, SkiaImageMetadata? metadata = null)
        {
            Bitmap = bitmap;
            Format = format;
            DpiX = dpiX;
            DpiY = dpiY;
            HasPalette = palette;
            Metadata = metadata;
        }

        public SKBitmap Bitmap { get; }
        public override int Width => Bitmap.Width;
        public override int Height => Bitmap.Height;
        public override double DpiX { get; }
        public override double DpiY { get; }
        public override RasterFormat Format { get; }
        public override bool HasPalette { get; }
        public override object? Metadata { get; }
    }

    /// <summary>
    /// Decoding, pixel work and encoding with SkiaSharp (the library Avalonia draws with). Skia reads JPEG, PNG, GIF, BMP,
    /// WebP and ICO; TIFF goes through <see cref="TiffCodec"/>. Skia writes PNG and JPEG; BMP, TIFF and GIF have small
    /// encoders of their own here. Formats nobody here reads (HEIC, AVIF) return null so the caller can use FFmpeg.
    /// Colors are passed through as stored (no color management), like the Windows backend.
    /// </summary>
    public sealed class SkiaImageBackend : IImageBackend
    {
        private static SkiaRasterImage Skia(RasterImage image) => (SkiaRasterImage)image;

        // ------------------------------------------------------------------ Decoding

        public LoadedImage? TryLoad(string path, bool applyOrientation, int maxDimension)
        {
            SkiaRasterImage? decoded;
            try
            {
                decoded = DecodeFirstFrame(File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                return null;
            }
            if (decoded == null) return null;

            var metadata = decoded.Metadata as SkiaImageMetadata;
            bool hasAlpha = decoded.Format is RasterFormat.Bgra32 or RasterFormat.Rgba64;
            int bits = decoded.Format == RasterFormat.Rgba64 ? 16 : 8;
            int orientation = applyOrientation ? metadata?.Orientation ?? 1 : 1;
            bool resize = maxDimension > 0 && Math.Max(decoded.Width, decoded.Height) > maxDimension;

            SkiaRasterImage image = decoded;
            if (orientation != 1 || resize)
            {
                // Transforms work on plain 8-bit pixels
                image = ToEightBit(image, hasAlpha);
                bits = 8;
                if (orientation != 1) image = Orient(image, orientation);
                if (resize)
                {
                    double s = (double)maxDimension / Math.Max(image.Width, image.Height);
                    image = (SkiaRasterImage)Resize(image, Math.Max(1, (int)Math.Round(image.Width * s)), Math.Max(1, (int)Math.Round(image.Height * s)));
                }
            }

            bool any = false, partial = false;
            if (hasAlpha) (any, partial) = ScanAlpha(image);

            return new LoadedImage
            {
                Bitmap = image,
                Metadata = metadata,
                HasAlpha = any,
                HasPartialAlpha = partial,
                BitsPerChannel = bits
            };
        }

        public RasterImage Decode(byte[] data) =>
            DecodeFirstFrame(data) ?? throw new InvalidDataException("This picture's format can't be read.");

        /// <summary>The first frame, pixel layout kept (gray stays gray, 16-bit stays 16-bit); null for unknown formats.</summary>
        private static SkiaRasterImage? DecodeFirstFrame(byte[] data)
        {
            if (TiffCodec.IsTiff(data)) return TiffCodec.Decode(data);

            using var skData = SKData.CreateCopy(data);
            using var codec = SKCodec.Create(skData);
            if (codec == null) return null;

            var info = codec.Info;
            RasterFormat format;
            SKImageInfo target;
            if (info.ColorType == SKColorType.Gray8)
            {
                format = RasterFormat.Gray8;
                target = new SKImageInfo(info.Width, info.Height, SKColorType.Gray8, SKAlphaType.Opaque);
            }
            else if (info.ColorType is SKColorType.Rgba16161616 or SKColorType.RgbaF16 && IsSixteenBitPng(data))
            {
                format = RasterFormat.Rgba64;
                target = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba16161616, info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Unpremul);
            }
            else
            {
                bool opaque = info.AlphaType == SKAlphaType.Opaque;
                format = opaque ? RasterFormat.Bgr24 : RasterFormat.Bgra32;
                target = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, opaque ? SKAlphaType.Opaque : SKAlphaType.Unpremul);
            }

            var bitmap = new SKBitmap(target);
            var result = codec.GetPixels(target, bitmap.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                bitmap.Dispose();
                throw new InvalidDataException($"This picture can't be decoded ({result}).");
            }

            bool jpeg = codec.EncodedFormat == SKEncodedImageFormat.Jpeg;
            var metadata = new SkiaImageMetadata
            {
                Orientation = (int)codec.EncodedOrigin is int o and >= 1 and <= 8 ? o : 1,
                JpegExif = jpeg ? Services.JpegExif.Read(data) : null,
                JpegIcc = jpeg ? Services.JpegExif.ReadIccSegments(data) : Array.Empty<byte[]>()
            };
            var (dpiX, dpiY) = ImageHeaders.ReadDpi(data);
            return new SkiaRasterImage(bitmap, format, dpiX, dpiY, ImageHeaders.HasPalette(data), metadata);
        }

        /// <summary>Skia reports 16-bit PNGs as 16-bit; anything else it widens on its own is decoded at 8 bits.</summary>
        private static bool IsSixteenBitPng(byte[] data) => data.Length > 24 && data[0] == 0x89 && data[1] == (byte)'P' && data[24] == 16;

        public int ReadOrientation(object? metadata) => metadata is SkiaImageMetadata m ? m.Orientation : 1;

        public int ReadOrientation(byte[] encoded)
        {
            if (TiffCodec.IsTiff(encoded)) return TiffCodec.ReadOrientation(encoded);
            using var skData = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(skData);
            return codec != null && (int)codec.EncodedOrigin is int o and >= 1 and <= 8 ? o : 1;
        }

        public (int Width, int Height) ReadPixelSize(string path)
        {
            byte[] head = ReadHead(path, 64 * 1024);
            if (TiffCodec.IsTiff(head)) return TiffCodec.ReadSize(File.ReadAllBytes(path));
            using var stream = new SKFileStream(path);
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("This picture's format can't be read.");
            return (codec.Info.Width, codec.Info.Height);
        }

        public int CountGifFrames(string path)
        {
            using var stream = new SKFileStream(path);
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("This GIF can't be read.");
            return Math.Max(1, codec.FrameCount);
        }

        private static byte[] ReadHead(string path, int max)
        {
            using var fs = File.OpenRead(path);
            var buffer = new byte[(int)Math.Min(max, fs.Length)];
            fs.ReadExactly(buffer);
            return buffer;
        }

        // ------------------------------------------------------------------ Pixels

        public RasterImage FromPixels(int width, int height, double dpi, RasterFormat format, byte[] pixels, int stride)
        {
            switch (format)
            {
                case RasterFormat.Gray8:
                {
                    var tight = new byte[width * height];
                    for (int y = 0; y < height; y++) Buffer.BlockCopy(pixels, y * stride, tight, y * width, width);
                    return new SkiaRasterImage(Create(width, height, SKColorType.Gray8, SKAlphaType.Opaque, tight), format, dpi, dpi);
                }
                case RasterFormat.Bgr24:
                case RasterFormat.Bgr32:
                {
                    int bpp = format == RasterFormat.Bgr24 ? 3 : 4;
                    var bgra = new byte[width * height * 4];
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0, s = y * stride, d = y * width * 4; x < width; x++, s += bpp, d += 4)
                        {
                            bgra[d] = pixels[s];
                            bgra[d + 1] = pixels[s + 1];
                            bgra[d + 2] = pixels[s + 2];
                            bgra[d + 3] = 255;
                        }
                    }
                    return new SkiaRasterImage(Create(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque, bgra), format, dpi, dpi);
                }
                case RasterFormat.Bgra32:
                {
                    var tight = new byte[width * height * 4];
                    for (int y = 0; y < height; y++) Buffer.BlockCopy(pixels, y * stride, tight, y * width * 4, width * 4);
                    return new SkiaRasterImage(Create(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul, tight), format, dpi, dpi);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "Only Gray8, Bgr24, Bgr32 and Bgra32 pixels are accepted.");
            }
        }

        public RasterImage ConvertFormat(RasterImage image, RasterFormat format)
        {
            var src = Skia(image);
            switch (format)
            {
                case RasterFormat.Gray8:
                {
                    if (src.Format == RasterFormat.Gray8) return src;
                    var bgra = Bytes(ToBgraBitmap(src));
                    var gray = new byte[src.Width * src.Height];
                    for (int i = 0, j = 0; j < gray.Length; i += 4, j++)
                    {
                        // ITU-R BT.601 luma, alpha ignored (like converting a picture to gray on Windows)
                        gray[j] = (byte)((bgra[i + 2] * 299 + bgra[i + 1] * 587 + bgra[i] * 114 + 500) / 1000);
                    }
                    return new SkiaRasterImage(Create(src.Width, src.Height, SKColorType.Gray8, SKAlphaType.Opaque, gray), format, src.DpiX, src.DpiY);
                }
                case RasterFormat.Bgr24:
                case RasterFormat.Bgr32:
                {
                    var bgra = Bytes(ToBgraBitmap(src));
                    for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255; // alpha dropped, colors kept
                    return new SkiaRasterImage(Create(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Opaque, bgra), format, src.DpiX, src.DpiY);
                }
                case RasterFormat.Bgra32:
                    return new SkiaRasterImage(ToBgraBitmap(src), format, src.DpiX, src.DpiY);
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "Only Gray8, Bgr24 and Bgra32 are supported.");
            }
        }

        /// <summary>
        /// Scales with good quality: halves with a 2×2 box average while more than twice too big (like an area-averaging
        /// downscale), then a Mitchell cubic for the last step.
        /// </summary>
        public RasterImage Resize(RasterImage image, int width, int height)
        {
            var src = Skia(image);
            if (src.Width == width && src.Height == height) return src;

            bool gray = src.Format == RasterFormat.Gray8;
            bool opaque = src.Bitmap.AlphaType == SKAlphaType.Opaque || gray;
            // Skia draws into premultiplied 32-bit pixels; soft edges scale correctly that way
            var current = Convert(src.Bitmap, SKColorType.Bgra8888, opaque ? SKAlphaType.Opaque : SKAlphaType.Premul);
            while (current.Width >= width * 2 && current.Height >= height * 2)
            {
                var half = new SKBitmap(new SKImageInfo(current.Width / 2, current.Height / 2, SKColorType.Bgra8888, current.AlphaType));
                current.ScalePixels(half, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                current.Dispose();
                current = half;
            }
            var scaled = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, current.AlphaType));
            current.ScalePixels(scaled, new SKSamplingOptions(SKCubicResampler.Mitchell));
            current.Dispose();

            if (gray) return ConvertFormat(new SkiaRasterImage(scaled, RasterFormat.Bgr24, src.DpiX, src.DpiY), RasterFormat.Gray8);
            var result = opaque ? scaled : Convert(scaled, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            return new SkiaRasterImage(result, opaque ? RasterFormat.Bgr24 : RasterFormat.Bgra32, src.DpiX, src.DpiY);
        }

        public RasterImage Crop(RasterImage image, PixelRect area)
        {
            var src = Skia(image);
            if (area.X < 0 || area.Y < 0 || area.Width <= 0 || area.Height <= 0 || area.X + area.Width > src.Width || area.Y + area.Height > src.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(area), "The crop area must lie within the picture.");
            }
            int bpp = src.Bitmap.BytesPerPixel;
            var pixels = Bytes(src.Bitmap);
            var cropped = new byte[area.Width * area.Height * bpp];
            for (int y = 0; y < area.Height; y++)
            {
                Buffer.BlockCopy(pixels, ((area.Y + y) * src.Width + area.X) * bpp, cropped, y * area.Width * bpp, area.Width * bpp);
            }
            return Derived(src, Create(area.Width, area.Height, src.Bitmap.ColorType, src.Bitmap.AlphaType, cropped));
        }

        public RasterImage Rotate90(RasterImage image) => Orient(Skia(image), 6);

        public BgraImage ToBgra(RasterImage image)
        {
            var src = Skia(image);
            return new BgraImage(src.Width, src.Height, Bytes(ToBgraBitmap(src)));
        }

        // ------------------------------------------------------------------ Encoding

        public void Encode(RasterImage image, Stream output, ImageEncoding encoding)
        {
            var src = Skia(image);
            switch (encoding.Format)
            {
                case ImageFileFormat.Png:
                    // Skia never interlaces; zlib at its maximum
                    WriteSkia(src.Bitmap, output, p => p.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 9)));
                    break;
                case ImageFileFormat.Jpeg:
                    WriteSkia(JpegPixels(src), output, p => p.Encode(new SKJpegEncoderOptions(Math.Clamp(encoding.Quality, 1, 100), SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore)));
                    break;
                case ImageFileFormat.Bmp:
                    LegacyEncoders.WriteBmp(output, Channels(src, out var bmp), src.Width, src.Height, bmp, src.DpiX);
                    break;
                case ImageFileFormat.Tiff:
                    TiffCodec.Encode(output, Channels(src, out var tiff), src.Width, src.Height, tiff, encoding.Deflate, src.DpiX, src.DpiY);
                    break;
                case ImageFileFormat.Gif:
                    LegacyEncoders.WriteGif(output, Bytes(ToBgraBitmap(src)), src.Width, src.Height);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(encoding));
            }
        }

        public void SaveJpeg(RasterImage image, string path, int quality, object? keepMetadata)
        {
            var src = Skia(image);
            byte[] jpeg;
            using (var ms = new MemoryStream())
            {
                Encode(src.Format == RasterFormat.Gray8 ? src : ConvertFormat(src, RasterFormat.Bgr24), ms, ImageEncoding.Jpeg(quality));
                jpeg = ms.ToArray();
            }

            // Camera details kept; the pixels are already upright
            if (keepMetadata is SkiaImageMetadata { JpegExif: { } exif } meta)
            {
                jpeg = Services.JpegExif.Insert(jpeg, Services.JpegExif.WithOrientation(exif, 1), meta.JpegIcc.Count > 0 ? meta.JpegIcc : null);
            }
            File.WriteAllBytes(path, jpeg);
        }

        public bool TranscodeFirstFrame(string input, string output, ImageEncoding encoding)
        {
            var image = DecodeFirstFrame(File.ReadAllBytes(input));
            if (image == null) return false;
            var meta = image.Metadata as SkiaImageMetadata;

            byte[] encoded;
            using (var ms = new MemoryStream())
            {
                if (encoding.Format == ImageFileFormat.Jpeg && meta?.JpegExif != null)
                {
                    // JPEG → JPEG keeps the EXIF block (orientation included) and the color profile
                    Encode(image, ms, encoding);
                    encoded = Services.JpegExif.Insert(ms.ToArray(), meta.JpegExif, meta.JpegIcc.Count > 0 ? meta.JpegIcc : null);
                }
                else
                {
                    // The other encoders here write no metadata, so the rotation goes into the pixels
                    if (meta?.Orientation is int o and not 1) image = Orient(ToEightBit(image, image.Format is RasterFormat.Bgra32 or RasterFormat.Rgba64), o);
                    Encode(image, ms, encoding);
                    encoded = ms.ToArray();
                }
            }
            File.WriteAllBytes(output, encoded);
            return true;
        }

        private static void WriteSkia(SKBitmap bitmap, Stream output, Func<SKPixmap, SKData?> encode)
        {
            using var pixmap = bitmap.PeekPixels();
            using var data = encode(pixmap);
            if (data != null)
            {
                data.SaveTo(output);
                return;
            }
            // 16-bit and other layouts the encoder refuses: plain 8-bit BGRA
            using var bgra = Convert(bitmap, SKColorType.Bgra8888, bitmap.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Unpremul);
            using var fallbackPixmap = bgra.PeekPixels();
            using var fallback = encode(fallbackPixmap) ?? throw new InvalidOperationException("The picture couldn't be encoded.");
            fallback.SaveTo(output);
        }

        /// <summary>JPEG has no transparency: 8-bit gray or opaque color.</summary>
        private static SKBitmap JpegPixels(SkiaRasterImage src) =>
            src.Format == RasterFormat.Gray8 || src.Bitmap.AlphaType == SKAlphaType.Opaque && src.Bitmap.ColorType == SKColorType.Bgra8888
                ? src.Bitmap
                : Convert(src.Bitmap, SKColorType.Bgra8888, SKAlphaType.Opaque);

        /// <summary>Tightly packed samples for the BMP and TIFF writers: gray, BGR or BGRA.</summary>
        private static byte[] Channels(SkiaRasterImage src, out int channels)
        {
            if (src.Format == RasterFormat.Gray8)
            {
                channels = 1;
                return Bytes(src.Bitmap);
            }
            var bgra = Bytes(ToBgraBitmap(src));
            if (src.Bitmap.AlphaType != SKAlphaType.Opaque)
            {
                channels = 4;
                return bgra;
            }
            channels = 3;
            var bgr = new byte[src.Width * src.Height * 3];
            for (int i = 0, j = 0; j < bgr.Length; i += 4, j += 3)
            {
                bgr[j] = bgra[i];
                bgr[j + 1] = bgra[i + 1];
                bgr[j + 2] = bgra[i + 2];
            }
            return bgr;
        }

        // ------------------------------------------------------------------ Helpers

        /// <summary>An 8-bit copy for transforms: unpremultiplied BGRA, or opaque BGRA / gray when there's no alpha.</summary>
        private static SkiaRasterImage ToEightBit(SkiaRasterImage src, bool alpha)
        {
            if (src.Format == RasterFormat.Gray8 || src.Bitmap.ColorType == SKColorType.Bgra8888) return src;
            var bitmap = Convert(src.Bitmap, SKColorType.Bgra8888, alpha ? SKAlphaType.Unpremul : SKAlphaType.Opaque);
            return new SkiaRasterImage(bitmap, alpha ? RasterFormat.Bgra32 : RasterFormat.Bgr24, src.DpiX, src.DpiY, src.HasPalette, src.Metadata as SkiaImageMetadata);
        }

        private static SKBitmap ToBgraBitmap(SkiaRasterImage src) =>
            src.Bitmap.ColorType == SKColorType.Bgra8888 && src.Bitmap.AlphaType != SKAlphaType.Premul
                ? src.Bitmap
                : Convert(src.Bitmap, SKColorType.Bgra8888, src.Bitmap.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Unpremul);

        /// <summary>A copy in another pixel layout (Skia converts color type and alpha type).</summary>
        private static SKBitmap Convert(SKBitmap src, SKColorType colorType, SKAlphaType alphaType)
        {
            var dst = new SKBitmap(new SKImageInfo(src.Width, src.Height, colorType, alphaType));
            using var pixmap = src.PeekPixels();
            if (!pixmap.ReadPixels(dst.Info, dst.GetPixels(), dst.RowBytes))
            {
                dst.Dispose();
                throw new InvalidOperationException($"Couldn't convert the picture from {src.ColorType} to {colorType}.");
            }
            return dst;
        }

        /// <summary>The pixels without row padding.</summary>
        private static byte[] Bytes(SKBitmap bitmap)
        {
            int row = bitmap.Width * bitmap.BytesPerPixel;
            var data = new byte[row * bitmap.Height];
            IntPtr pixels = bitmap.GetPixels();
            for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(pixels + y * bitmap.RowBytes, data, y * row, row);
            return data;
        }

        internal static SKBitmap Create(int width, int height, SKColorType colorType, SKAlphaType alphaType, byte[] tight)
        {
            var bitmap = new SKBitmap(new SKImageInfo(width, height, colorType, alphaType));
            int row = width * bitmap.BytesPerPixel;
            IntPtr pixels = bitmap.GetPixels();
            for (int y = 0; y < height; y++) Marshal.Copy(tight, y * row, pixels + y * bitmap.RowBytes, row);
            return bitmap;
        }

        private static SkiaRasterImage Derived(SkiaRasterImage src, SKBitmap bitmap) => new(bitmap, src.Format, src.DpiX, src.DpiY);

        /// <summary>Applies an EXIF orientation (1–8) so the pixels display upright without the tag.</summary>
        private static SkiaRasterImage Orient(SkiaRasterImage src, int orientation)
        {
            int w = src.Width, h = src.Height, bpp = src.Bitmap.BytesPerPixel;
            bool swap = orientation >= 5;
            int dw = swap ? h : w, dh = swap ? w : h;
            var from = Bytes(src.Bitmap);
            var to = new byte[from.Length];
            for (int y = 0; y < dh; y++)
            {
                for (int x = 0; x < dw; x++)
                {
                    var (sx, sy) = orientation switch
                    {
                        2 => (w - 1 - x, y),
                        3 => (w - 1 - x, h - 1 - y),
                        4 => (x, h - 1 - y),
                        5 => (y, x),
                        6 => (y, h - 1 - x),
                        7 => (w - 1 - y, h - 1 - x),
                        8 => (w - 1 - y, x),
                        _ => (x, y)
                    };
                    Buffer.BlockCopy(from, (sy * w + sx) * bpp, to, (y * dw + x) * bpp, bpp);
                }
            }
            var bitmap = Create(dw, dh, src.Bitmap.ColorType, src.Bitmap.AlphaType, to);
            return new SkiaRasterImage(bitmap, src.Format, swap ? src.DpiY : src.DpiX, swap ? src.DpiX : src.DpiY);
        }

        /// <summary>(any pixel not opaque, any pixel semi-transparent).</summary>
        private static (bool Any, bool Partial) ScanAlpha(SkiaRasterImage image)
        {
            if (image.Bitmap.AlphaType == SKAlphaType.Opaque || image.Format == RasterFormat.Gray8) return (false, false);
            var bgra = Bytes(ToBgraBitmap(image));
            bool any = false;
            for (int i = 3; i < bgra.Length; i += 4)
            {
                byte a = bgra[i];
                if (a == 255) continue;
                any = true;
                if (a != 0) return (true, true);
            }
            return (any, false);
        }
    }
}
