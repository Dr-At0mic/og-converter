using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OgConverter.Services
{
    /// <summary>
    /// Reads the text in pictures and scanned pages with the system's OCR engine (<see cref="Platform.Ocr"/>; on Windows
    /// the one built into Windows 10/11, which reads the languages installed in Windows). Offline.
    /// </summary>
    public static class OcrService
    {
        public const string NoLanguageHint = "Windows has no text-recognition language installed. Add one in Settings › Time & language › Language & region.";

        public static bool IsAvailable => Platform.Ocr?.IsAvailable == true;

        /// <summary>Text in the picture at <paramref name="imagePath"/>, line by line, with a blank line between paragraphs.</summary>
        public static async Task<string> RecognizeFileAsync(string imagePath)
        {
            var engine = Platform.Ocr ?? throw new InvalidOperationException(NotAvailable);
            // Decoded upright (EXIF rotation applied) so phone photos are read the right way up, and huge scans fit the engine
            var image = ImageCodec.TryLoad(imagePath, applyOrientation: true, maxDimension: engine.MaxImageDimension)
                        ?? throw new InvalidOperationException("Windows can't open this picture to read its text.");
            return await RecognizeAsync(image.Bitmap);
        }

        public static async Task<string> RecognizeAsync(RasterImage bitmap)
        {
            var engine = Platform.Ocr is { IsAvailable: true } ocr ? ocr : throw new InvalidOperationException(NotAvailable);
            return Layout(await engine.RecognizeAsync(bitmap));
        }

        private static string NotAvailable => Platform.Ocr?.UnavailableHint ?? "Text recognition isn't available on this system.";

        /// <summary>Lines in reading order; a gap taller than a line starts a new paragraph.</summary>
        private static string Layout(IReadOnlyList<OcrTextLine> lines)
        {
            var sb = new StringBuilder();
            double previousBottom = double.NaN;
            foreach (var line in lines)
            {
                double top = line.Top;
                double bottom = line.Bottom;
                if (!double.IsNaN(previousBottom) && top - previousBottom > (bottom - top) * 0.9) sb.AppendLine();
                sb.AppendLine(line.Text);
                previousBottom = bottom;
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Reads every page of a (scanned) PDF.</summary>
        public static async Task<string> RecognizePdfAsync(string pdfPath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "og-converter-ocr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var pages = await PdfRenderService.RenderPagesToPngAsync(pdfPath, (p, _) => Path.Combine(tempDir, $"page_{p:D3}.png"));
                var sb = new StringBuilder();
                for (int i = 0; i < pages.Count; i++)
                {
                    if (pages.Count > 1) sb.AppendLine($"--- Page {i + 1} ---");
                    sb.AppendLine(await RecognizeFileAsync(pages[i]));
                    sb.AppendLine();
                }
                return sb.ToString().TrimEnd();
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
