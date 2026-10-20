using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OgConverter.Services
{
    /// <summary>
    /// Renders PDF pages to images with the system's PDF engine (<see cref="Platform.Pdf"/>; on Windows, Windows.Data.Pdf).
    /// FFmpeg has no PDF decoder, so this is the only offline path for PDF → image.
    /// </summary>
    public static class PdfRenderService
    {
        private const double RenderScale = 2.0; // 2x of 96 DPI ≈ 192 DPI

        /// <summary>
        /// Renders every page to PNG. Returns the written PNG paths in page order.
        /// </summary>
        /// <param name="pathForPage">Maps a 1-based page number and total page count to an output path.</param>
        public static Task<List<string>> RenderPagesToPngAsync(string pdfPath, Func<int, int, string> pathForPage, int maxPages = int.MaxValue)
        {
            var renderer = Platform.Pdf ?? throw new InvalidOperationException("This system has no PDF renderer to turn pages into pictures.");
            return renderer.RenderPagesToPngAsync(pdfPath, pathForPage, maxPages, RenderScale);
        }
    }
}
