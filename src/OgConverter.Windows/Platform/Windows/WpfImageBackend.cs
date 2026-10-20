using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Platform.Windows
{
    /// <summary>A WPF bitmap as the portable code sees it.</summary>
    public sealed class WpfRasterImage : RasterImage
    {
        public WpfRasterImage(BitmapSource source) => Source = source;

        public BitmapSource Source { get; }

        public override int Width => Source.PixelWidth;
        public override int Height => Source.PixelHeight;
        public override double DpiX => Source.DpiX;
        public override double DpiY => Source.DpiY;
        public override bool HasPalette => Source.Palette != null;
        public override object? Metadata => (Source as BitmapFrame)?.Metadata;

        public override RasterFormat Format
        {
            get
            {
                var f = Source.Format;
                if (f == PixelFormats.Gray8) return RasterFormat.Gray8;
                if (f == PixelFormats.Bgr24) return RasterFormat.Bgr24;
                if (f == PixelFormats.Bgr32) return RasterFormat.Bgr32;
                if (f == PixelFormats.Bgra32) return RasterFormat.Bgra32;
                if (f == PixelFormats.Pbgra32) return RasterFormat.Pbgra32;
                if (f == PixelFormats.Rgba64) return RasterFormat.Rgba64;
                if (f == PixelFormats.Prgba64) return RasterFormat.Prgba64;
                if (f == PixelFormats.Cmyk32) return RasterFormat.Cmyk32;
                return RasterFormat.Other;
            }
        }
    }

    /// <summary>
    /// Windows Imaging Component (WIC) through WPF. Bitmaps from a decoder (and lazy ones derived from them) belong to the
    /// thread that made them, so callers keep decode, edit and encode on one thread; frozen results can go anywhere.
    /// </summary>
    public sealed class WpfImageBackend : IImageBackend
    {
        private const string JpegOrientationQuery = "/app1/ifd/{ushort=274}";
        private const string TiffOrientationQuery = "/ifd/{ushort=274}";

        private static BitmapSource Wpf(RasterImage image) => ((WpfRasterImage)image).Source;
        private static RasterImage Wrap(BitmapSource source) => new WpfRasterImage(source);

        public LoadedImage? TryLoad(string path, bool applyOrientation, int maxDimension)
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
                Bitmap = Wrap(bmp),
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

        public int ReadOrientation(object? metadata) => ReadOrientation(metadata as BitmapMetadata);

        private static int ReadOrientation(BitmapMetadata? metadata)
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

        public int ReadOrientation(byte[] encoded)
        {
            using var ms = new MemoryStream(encoded, false);
            var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return ReadOrientation(frame.Metadata as BitmapMetadata);
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

        public RasterImage Decode(byte[] data)
        {
            using var ms = new MemoryStream(data, false);
            return Wrap(BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0]);
        }

        public (int Width, int Height) ReadPixelSize(string path)
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            return (frame.PixelWidth, frame.PixelHeight);
        }

        public int CountGifFrames(string path)
        {
            using var fs = File.OpenRead(path);
            return new GifBitmapDecoder(fs, BitmapCreateOptions.None, BitmapCacheOption.None).Frames.Count;
        }

        public RasterImage FromPixels(int width, int height, double dpi, RasterFormat format, byte[] pixels, int stride)
        {
            var bmp = BitmapSource.Create(width, height, dpi, dpi, ToWpf(format), null, pixels, stride);
            bmp.Freeze();
            return Wrap(bmp);
        }

        public RasterImage ConvertFormat(RasterImage image, RasterFormat format) =>
            Wrap(new FormatConvertedBitmap(Wpf(image), ToWpf(format), null, 0));

        public RasterImage Resize(RasterImage image, int width, int height)
        {
            var src = Wpf(image);
            return Wrap(new TransformedBitmap(src, new ScaleTransform((double)width / src.PixelWidth, (double)height / src.PixelHeight)));
        }

        public RasterImage Crop(RasterImage image, PixelRect area)
        {
            var cropped = new CroppedBitmap(Wpf(image), new Int32Rect(area.X, area.Y, area.Width, area.Height));
            cropped.Freeze();
            return Wrap(cropped);
        }

        public RasterImage Rotate90(RasterImage image)
        {
            var turned = new TransformedBitmap(Wpf(image), new RotateTransform(90));
            turned.Freeze();
            return Wrap(turned);
        }

        public BgraImage ToBgra(RasterImage image)
        {
            var src = Wpf(image);
            var bgra = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int stride = bgra.PixelWidth * 4;
            var pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);
            return new BgraImage(bgra.PixelWidth, bgra.PixelHeight, pixels);
        }

        public void Encode(RasterImage image, Stream output, ImageEncoding encoding)
        {
            var encoder = CreateEncoder(encoding);
            encoder.Frames.Add(BitmapFrame.Create(Wpf(image)));
            encoder.Save(output);
        }

        public void SaveJpeg(RasterImage image, string path, int quality, object? keepMetadata)
        {
            var bmp = Wpf(image);
            var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
            BitmapSource pixels = bmp.Format == PixelFormats.Bgr24 || bmp.Format == PixelFormats.Gray8 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0);

            BitmapMetadata? meta = null;
            if (keepMetadata is BitmapMetadata source && source.Format == "jpg")
            {
                try
                {
                    meta = source.Clone();
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

        public bool TranscodeFirstFrame(string input, string output, ImageEncoding encoding)
        {
            using var stream = File.OpenRead(input);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return false;
            var frame = decoder.Frames[0];

            var encoder = CreateEncoder(encoding);
            encoder.Frames.Add(frame);
            using var outStream = File.Create(output);
            encoder.Save(outStream);
            return true;
        }

        private static BitmapEncoder CreateEncoder(ImageEncoding encoding) => encoding.Format switch
        {
            ImageFileFormat.Png => encoding.NoInterlace ? new PngBitmapEncoder { Interlace = PngInterlaceOption.Off } : new PngBitmapEncoder(),
            ImageFileFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(encoding.Quality, 1, 100) },
            ImageFileFormat.Bmp => new BmpBitmapEncoder(),
            ImageFileFormat.Tiff => encoding.Deflate ? new TiffBitmapEncoder { Compression = TiffCompressOption.Zip }
                : encoding.Uncompressed ? new TiffBitmapEncoder { Compression = TiffCompressOption.None }
                : new TiffBitmapEncoder(),
            ImageFileFormat.Gif => new GifBitmapEncoder(),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };

        private static PixelFormat ToWpf(RasterFormat format) => format switch
        {
            RasterFormat.Gray8 => PixelFormats.Gray8,
            RasterFormat.Bgr24 => PixelFormats.Bgr24,
            RasterFormat.Bgr32 => PixelFormats.Bgr32,
            RasterFormat.Bgra32 => PixelFormats.Bgra32,
            RasterFormat.Pbgra32 => PixelFormats.Pbgra32,
            RasterFormat.Rgba64 => PixelFormats.Rgba64,
            RasterFormat.Prgba64 => PixelFormats.Prgba64,
            RasterFormat.Cmyk32 => PixelFormats.Cmyk32,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }
}
