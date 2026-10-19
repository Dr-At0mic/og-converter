using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using BitmapAlphaMode = Windows.Graphics.Imaging.BitmapAlphaMode;
using BitmapPixelFormat = Windows.Graphics.Imaging.BitmapPixelFormat;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace OgConverter.Services
{
    /// <summary>
    /// Reads the text in pictures and scanned pages with the OCR engine built into Windows 10/11 (Windows.Media.Ocr).
    /// Offline; it reads the languages installed in Windows (Settings › Time &amp; language › Language).
    /// </summary>
    public static class OcrService
    {
        public const string NoLanguageHint = "Windows has no text-recognition language installed. Add one in Settings › Time & language › Language & region.";

        private static readonly Lazy<OcrEngine?> Engine = new(() =>
        {
            try
            {
                return OcrEngine.TryCreateFromUserProfileLanguages()
                       ?? (OcrEngine.AvailableRecognizerLanguages.FirstOrDefault() is Language first ? OcrEngine.TryCreateFromLanguage(first) : null);
            }
            catch
            {
                return null;
            }
        });

        public static bool IsAvailable => Engine.Value != null;

        /// <summary>Text in the picture at <paramref name="imagePath"/>, line by line, with a blank line between paragraphs.</summary>
        public static async Task<string> RecognizeFileAsync(string imagePath)
        {
            // Through WIC so phone photos are read the right way up (EXIF rotation applied) and huge scans fit the engine
            var image = ImageCodec.TryLoad(imagePath, applyOrientation: true, maxDimension: (int)OcrEngine.MaxImageDimension)
                        ?? throw new InvalidOperationException("Windows can't open this picture to read its text.");
            return await RecognizeAsync(image.Bitmap);
        }

        public static async Task<string> RecognizeAsync(BitmapSource bitmap)
        {
            var engine = Engine.Value ?? throw new InvalidOperationException(NoLanguageHint);

            // WPF bitmap → PNG in memory → WinRT SoftwareBitmap
            byte[] png;
            using (var ms = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(ms);
                png = ms.ToArray();
            }

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var result = await engine.RecognizeAsync(software);
            return Layout(result.Lines);
        }

        /// <summary>Lines in reading order; a gap taller than a line starts a new paragraph.</summary>
        private static string Layout(IReadOnlyList<OcrLine> lines)
        {
            var sb = new StringBuilder();
            double previousBottom = double.NaN;
            foreach (var line in lines)
            {
                if (line.Words.Count == 0) continue;
                double top = line.Words.Min(w => w.BoundingRect.Top);
                double bottom = line.Words.Max(w => w.BoundingRect.Bottom);
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
