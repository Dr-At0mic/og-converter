using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using OgConverter.Services;

namespace OgConverter.Platform.Windows
{
    /// <summary>Renders PDF pages to images using the built-in Windows PDF engine (Windows.Data.Pdf).</summary>
    public sealed class WindowsPdfRasterizer : IPdfRasterizer
    {
        public async Task<List<string>> RenderPagesToPngAsync(string pdfPath, Func<int, int, string> pathForPage, int maxPages, double scale)
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
                    DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
                    DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)),
                    BackgroundColor = global::Windows.UI.Color.FromArgb(255, 255, 255, 255)
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
