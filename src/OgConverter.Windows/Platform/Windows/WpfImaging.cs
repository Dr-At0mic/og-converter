using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Platform.Windows
{
    /// <summary>Conversions between the portable picture types and WPF's, where the UI meets the services.</summary>
    public static class WpfImaging
    {
        /// <summary>A frozen 96-DPI bitmap of the pixels, so any thread (the UI) can draw it.</summary>
        public static BitmapSource ToBitmapSource(this BgraImage image)
        {
            var copy = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Stride);
            copy.Freeze();
            return copy;
        }

        public static PixelRect ToPixelRect(this Int32Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

        /// <summary>The WPF bitmap behind a picture from <see cref="WpfImageBackend"/>.</summary>
        public static BitmapSource ToBitmapSource(this RasterImage image) => ((WpfRasterImage)image).Source;
    }
}
