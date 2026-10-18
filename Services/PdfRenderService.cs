using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace OgConverter.Services
{
    /// <summary>
    /// Renders PDF pages to images using the built-in Windows PDF engine (Windows.Data.Pdf).
    /// FFmpeg has no PDF decoder, so this is the only offline path for PDF → image.
    /// </summary>
    public static class PdfRenderService
    {
        private const double RenderScale = 2.0; // 2x of 96 DPI ≈ 192 DPI

        /// <summary>
        /// Renders every page to PNG. Returns the written PNG paths in page order.
        /// </summary>
        /// <param name="pathForPage">Maps a 1-based page number and total page count to an output path.</param>
        public static async Task<List<string>> RenderPagesToPngAsync(string pdfPath, Func<int, int, string> pathForPage, int maxPages = int.MaxValue)
        {
            var output = new List<string>();
            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
            PdfDocument doc = await PdfDocument.LoadFromFileAsync(file);

            int count = (int)Math.Min(doc.PageCount, (uint)Math.Max(1, maxPages));
            for (uint i = 0; i < count; i++)
            {
                using PdfPage page = doc.GetPage(i);
                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * RenderScale)),
                    DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * RenderScale)),
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
                };

                using var ras = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(ras, options); // PNG by default

                string target = pathForPage((int)i + 1, count);
                using (var src = ras.AsStreamForRead())
                using (var dst = File.Create(target))
                {
                    src.Position = 0;
                    await src.CopyToAsync(dst);
                }
                output.Add(target);
            }

            return output;
        }
    }
}
