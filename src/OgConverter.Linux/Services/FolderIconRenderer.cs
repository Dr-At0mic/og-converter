using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>Draws a <see cref="FolderIconLibrary"/> design, or a picture, as the PNGs <see cref="FolderIconService"/> saves.</summary>
    public static class FolderIconRenderer
    {
        private const double Grid = 256;

        /// <summary>A symbol on its own, filling a <paramref name="size"/> box at (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public static Geometry Symbol(FolderIconSymbol symbol, double x = 0, double y = 0, double size = 24)
        {
            // "F1": non-zero filling, as in the SVGs the paths come from
            var geometry = Geometry.Parse("F1 " + symbol.PathData);
            double k = size / 24;
            geometry.Transform = new MatrixTransform(new Matrix(k, 0, 0, k, x, y));
            return geometry;
        }

        /// <summary>
        /// The folder in <paramref name="color"/> with <paramref name="symbol"/> on its front (none for a plain folder), or
        /// <paramref name="picture"/> fitted into the square instead when given.
        /// </summary>
        public static RenderTargetBitmap Render(FolderIconColor color, FolderIconSymbol? symbol, Bitmap? picture, int size)
        {
            var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            using (var ctx = bitmap.CreateDrawingContext())
            using (ctx.PushTransform(Matrix.CreateScale(size / Grid, size / Grid)))
            {
                if (picture != null)
                {
                    double scale = Grid / Math.Max(picture.PixelSize.Width, picture.PixelSize.Height);
                    double w = picture.PixelSize.Width * scale, h = picture.PixelSize.Height * scale;
                    using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                    {
                        ctx.DrawImage(picture, new Rect(0, 0, picture.PixelSize.Width, picture.PixelSize.Height), new Rect((Grid - w) / 2, (Grid - h) / 2, w, h));
                    }
                }
                else
                {
                    ctx.DrawGeometry(Brush(color.Back), null, Geometry.Parse(FolderIconLibrary.BackPath));
                    ctx.DrawGeometry(Brush(color.Front), null, Geometry.Parse(FolderIconLibrary.FrontPath));
                    if (symbol != null)
                    {
                        ctx.DrawGeometry(Brush(color.Symbol), null, Symbol(symbol, FolderIconLibrary.SymbolX, FolderIconLibrary.SymbolY, FolderIconLibrary.SymbolSize));
                    }
                }
            }
            return bitmap;
        }

        /// <summary>Every size in <see cref="FolderIconService.Sizes"/>, as PNG.</summary>
        public static List<FolderIconImage> RenderAll(FolderIconColor color, FolderIconSymbol? symbol, Bitmap? picture) =>
            FolderIconService.Sizes.Select(size =>
            {
                using var bitmap = Render(color, symbol, picture, size);
                using var ms = new MemoryStream();
                bitmap.Save(ms, PngBitmapEncoderOptions.Default);
                return new FolderIconImage(size, ms.ToArray());
            }).ToList();

        public static IBrush Brush(uint argb) =>
            new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
    }
}
