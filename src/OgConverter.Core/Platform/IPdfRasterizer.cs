using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OgConverter.Services
{
    /// <summary>Draws PDF pages as pictures. FFmpeg has no PDF decoder, so this is the only path for PDF → image.</summary>
    public interface IPdfRasterizer
    {
        /// <summary>
        /// Renders up to <paramref name="maxPages"/> pages, in order, as PNG on a white background at <paramref name="scale"/>
        /// × 96 DPI. Returns the written paths.
        /// </summary>
        /// <param name="pathForPage">Maps a 1-based page number and the number of pages rendered to an output path.</param>
        Task<List<string>> RenderPagesToPngAsync(string pdfPath, Func<int, int, string> pathForPage, int maxPages, double scale);
    }
}
