using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>Draws a <see cref="FolderIconLibrary"/> design, or a picture, as the PNGs <see cref="FolderIconService"/> saves.</summary>
    public static class FolderIconRenderer
    {
        private const double Grid = 256;

        /// <summary>The folder in <paramref name="color"/>, with <paramref name="symbol"/> on its front (none for a plain folder).</summary>
        public static Drawing Design(FolderIconColor color, FolderIconSymbol? symbol)
        {
            var group = new DrawingGroup();
            Frame(group);
            group.Children.Add(new GeometryDrawing(Brush(color.Back), null, Geometry.Parse(FolderIconLibrary.BackPath)));
            group.Children.Add(new GeometryDrawing(Brush(color.Front), null, Geometry.Parse(FolderIconLibrary.FrontPath)));
            if (symbol != null) group.Children.Add(new GeometryDrawing(Brush(color.Symbol), null, Symbol(symbol, FolderIconLibrary.SymbolX, FolderIconLibrary.SymbolY, FolderIconLibrary.SymbolSize)));
            group.Freeze();
            return group;
        }

        /// <summary>A symbol on its own, filling a 24 × 24 box (for the picker's tiles).</summary>
        public static Geometry Symbol(FolderIconSymbol symbol, double x = 0, double y = 0, double size = 24)
        {
            // "F1": non-zero filling, as in the SVGs the paths come from
            var geometry = Geometry.Parse("F1 " + symbol.PathData).Clone();
            double k = size / 24;
            geometry.Transform = new MatrixTransform(k, 0, 0, k, x, y);
            geometry.Freeze();
            return geometry;
        }

        /// <summary>A picture fitted into the icon square, centred, aspect ratio kept.</summary>
        public static Drawing Picture(BitmapSource picture)
        {
            double scale = Grid / System.Math.Max(picture.PixelWidth, picture.PixelHeight);
            double w = picture.PixelWidth * scale, h = picture.PixelHeight * scale;
            var image = new ImageDrawing(picture, new Rect((Grid - w) / 2, (Grid - h) / 2, w, h));
            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
            Frame(group);
            group.Children.Add(image);
            group.Freeze();
            return group;
        }

        /// <summary>Every size in <see cref="FolderIconService.Sizes"/>.</summary>
        public static List<FolderIconImage> Render(Drawing drawing) =>
            FolderIconService.Sizes.Select(size => new FolderIconImage(size, RenderPng(drawing, size))).ToList();

        public static byte[] RenderPng(Drawing drawing, int size)
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / Grid, size / Grid));
                dc.DrawDrawing(drawing);
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>An invisible square so the drawing keeps the full grid's bounds.</summary>
        private static void Frame(DrawingGroup group) =>
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Grid, Grid))));

        private static SolidColorBrush Brush(uint argb)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            brush.Freeze();
            return brush;
        }
    }
}
