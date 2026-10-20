using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using OgConverter.Services;
using BitmapAlphaMode = Windows.Graphics.Imaging.BitmapAlphaMode;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using BitmapPixelFormat = Windows.Graphics.Imaging.BitmapPixelFormat;

namespace OgConverter.Platform.Windows
{
    /// <summary>
    /// The OCR engine built into Windows 10/11 (Windows.Media.Ocr). Offline; it reads the languages installed in Windows
    /// (Settings › Time &amp; language › Language).
    /// </summary>
    public sealed class WindowsOcrEngine : IOcrEngine
    {
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

        public bool IsAvailable => Engine.Value != null;

        public string UnavailableHint => OcrService.NoLanguageHint;

        public int MaxImageDimension => (int)OcrEngine.MaxImageDimension;

        public async Task<IReadOnlyList<OcrTextLine>> RecognizeAsync(RasterImage image)
        {
            var engine = Engine.Value ?? throw new InvalidOperationException(OcrService.NoLanguageHint);

            // WPF bitmap → PNG in memory → WinRT SoftwareBitmap
            byte[] png;
            using (var ms = new MemoryStream())
            {
                OgConverter.Services.Platform.Images.Encode(image, ms, ImageEncoding.Png());
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
            var lines = new List<OcrTextLine>();
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0) continue;
                lines.Add(new OcrTextLine(line.Text, line.Words.Min(w => w.BoundingRect.Top), line.Words.Max(w => w.BoundingRect.Bottom)));
            }
            return lines;
        }
    }
}
