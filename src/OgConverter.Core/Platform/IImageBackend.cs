using System;
using System.IO;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>Pixel layouts the portable code asks for or checks. Anything else a decoder produces is <see cref="Other"/>.</summary>
    public enum RasterFormat
    {
        Other,
        Gray8,
        Bgr24,
        Bgr32,
        Bgra32,
        /// <summary>Premultiplied BGRA.</summary>
        Pbgra32,
        Rgba64,
        /// <summary>Premultiplied 16-bit RGBA.</summary>
        Prgba64,
        Cmyk32
    }

    /// <summary>
    /// A picture owned by the image backend (on Windows a WPF BitmapSource). Opaque to the portable code: pass it back to
    /// <see cref="IImageBackend"/> to transform or encode it. Derived images may be lazy, so keep the work on one thread.
    /// </summary>
    public abstract class RasterImage
    {
        public abstract int Width { get; }
        public abstract int Height { get; }
        /// <summary>Resolution stored in the file (96 when it has none).</summary>
        public abstract double DpiX { get; }
        public abstract double DpiY { get; }
        public abstract RasterFormat Format { get; }
        /// <summary>Indexed colors (a palette), e.g. most GIFs and palette PNGs.</summary>
        public abstract bool HasPalette { get; }
        /// <summary>The decoder's metadata for a decoded frame (for <see cref="IImageBackend.ReadOrientation(object?)"/>); null for derived images.</summary>
        public abstract object? Metadata { get; }
    }

    /// <summary>Plain 32-bit BGRA pixels (stride = Width × 4), not tied to any thread or toolkit; what the UI shows in previews.</summary>
    public sealed class BgraImage
    {
        public BgraImage(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }
        public int Stride => Width * 4;
    }

    public enum ImageFileFormat
    {
        Png,
        Jpeg,
        Bmp,
        Tiff,
        Gif
    }

    /// <summary>Which encoder to use and how. Options left unset keep the encoder's own defaults.</summary>
    public sealed record ImageEncoding(ImageFileFormat Format)
    {
        /// <summary>JPEG quality, 1–100 (clamped).</summary>
        public int Quality { get; init; } = 90;
        /// <summary>PNG: never interlace. Otherwise the encoder decides.</summary>
        public bool NoInterlace { get; init; }
        /// <summary>TIFF: Deflate (ZIP) compression. Otherwise the encoder's default.</summary>
        public bool Deflate { get; init; }

        public static ImageEncoding Png(bool noInterlace = false) => new(ImageFileFormat.Png) { NoInterlace = noInterlace };
        public static ImageEncoding Jpeg(int quality) => new(ImageFileFormat.Jpeg) { Quality = quality };
        public static ImageEncoding Tiff(bool deflate = false) => new(ImageFileFormat.Tiff) { Deflate = deflate };
        public static ImageEncoding Bmp() => new(ImageFileFormat.Bmp);
        public static ImageEncoding Gif() => new(ImageFileFormat.Gif);
    }

    /// <summary>
    /// Decoding, pixel work and encoding. The compression, crop/rotate and PDF logic stays portable and only calls these
    /// primitives. Methods throw when a picture can't be read unless they say otherwise.
    /// </summary>
    public interface IImageBackend
    {
        /// <summary>
        /// Decodes the first frame of a file. Null if there's no decoder for the format. With <paramref name="applyOrientation"/>
        /// the EXIF rotation is baked into the pixels; <paramref name="maxDimension"/> &gt; 0 shrinks the longest edge to at
        /// most that many pixels. Without either, pixels and pixel format are untouched (safe for lossless re-encoding).
        /// Transparency is scanned for <see cref="LoadedImage.HasAlpha"/> / <see cref="LoadedImage.HasPartialAlpha"/>.
        /// </summary>
        LoadedImage? TryLoad(string path, bool applyOrientation, int maxDimension);

        /// <summary>First frame of an encoded picture in memory, pixel format preserved, fully decoded (no ties to <paramref name="data"/>).</summary>
        RasterImage Decode(byte[] data);

        /// <summary>EXIF orientation 1–8 from decoder metadata (<see cref="LoadedImage.Metadata"/>, <see cref="RasterImage.Metadata"/>); 1 when absent.</summary>
        int ReadOrientation(object? metadata);

        /// <summary>EXIF orientation 1–8 of an encoded picture (headers only); 1 when absent.</summary>
        int ReadOrientation(byte[] encoded);

        /// <summary>Width and height from the file's header, without decoding the pixels.</summary>
        (int Width, int Height) ReadPixelSize(string path);

        /// <summary>Frames in a GIF file (more than one: animated).</summary>
        int CountGifFrames(string path);

        /// <summary>A picture from raw pixels (<see cref="RasterFormat.Gray8"/>, <see cref="RasterFormat.Bgr24"/>, <see cref="RasterFormat.Bgr32"/> or <see cref="RasterFormat.Bgra32"/>).</summary>
        RasterImage FromPixels(int width, int height, double dpi, RasterFormat format, byte[] pixels, int stride);

        /// <summary>Converts to <see cref="RasterFormat.Gray8"/>, <see cref="RasterFormat.Bgr24"/> or <see cref="RasterFormat.Bgra32"/>.</summary>
        RasterImage ConvertFormat(RasterImage image, RasterFormat format);

        /// <summary>Scales to exactly <paramref name="width"/> × <paramref name="height"/> with the system's scaler.</summary>
        RasterImage Resize(RasterImage image, int width, int height);

        /// <summary>The part inside <paramref name="area"/>, which must lie within the image. Ready to use on any thread.</summary>
        RasterImage Crop(RasterImage image, PixelRect area);

        /// <summary>A quarter turn clockwise. Ready to use on any thread.</summary>
        RasterImage Rotate90(RasterImage image);

        /// <summary>A copy of the pixels as 32-bit BGRA.</summary>
        BgraImage ToBgra(RasterImage image);

        /// <summary>Encodes as-is (no conversion beyond what the encoder does itself), without metadata.</summary>
        void Encode(RasterImage image, Stream output, ImageEncoding encoding);

        /// <summary>
        /// Writes a JPEG of a photo: 8-bit color or gray (other formats are converted to 24-bit color first). With
        /// <paramref name="keepMetadata"/> from a JPEG, its camera details are kept and the orientation tag is reset to 1
        /// since the pixels are already upright.
        /// </summary>
        void SaveJpeg(RasterImage image, string path, int quality, object? keepMetadata);

        /// <summary>
        /// Re-encodes the first frame of <paramref name="input"/> into <paramref name="output"/>, keeping what the encoder can
        /// carry over from the frame (metadata, color profile). False when the file has no frames.
        /// </summary>
        bool TranscodeFirstFrame(string input, string output, ImageEncoding encoding);
    }
}
