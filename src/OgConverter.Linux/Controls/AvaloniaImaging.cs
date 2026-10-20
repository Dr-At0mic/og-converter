using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OgConverter.Services;

namespace OgConverter.Controls
{
    /// <summary>Core's toolkit-neutral pictures as Avalonia bitmaps (the Windows app's WpfImaging).</summary>
    public static class AvaloniaImaging
    {
        /// <summary>A copy of the BGRA pixels as a bitmap the UI can draw.</summary>
        public static Bitmap ToBitmap(this BgraImage image)
        {
            var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using var buffer = bitmap.Lock();
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(image.Pixels, y * image.Stride, buffer.Address + y * buffer.RowBytes, image.Stride);
            }
            return bitmap;
        }
    }
}
