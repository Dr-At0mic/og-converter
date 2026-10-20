using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OgConverter.Services;

namespace OgConverter.Platform.Linux
{
    /// <summary>
    /// Renders PDF pages with PDFium (Chrome's PDF engine, from the bblanchon.PDFium.Linux package). Falls back to
    /// poppler's <c>pdftoppm</c> when the library can't be loaded.
    /// </summary>
    public sealed class PdfiumRasterizer : IPdfRasterizer
    {
        // PDFium is not thread-safe: one call at a time
        private static readonly object Gate = new();
        private static bool _initialized;
        private static bool _unavailable;

        public Task<List<string>> RenderPagesToPngAsync(string pdfPath, Func<int, int, string> pathForPage, int maxPages, double scale) =>
            Task.Run(async () =>
            {
                if (!_unavailable)
                {
                    try
                    {
                        return RenderWithPdfium(Path.GetFullPath(pdfPath), pathForPage, maxPages, scale);
                    }
                    catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                    {
                        _unavailable = true;
                    }
                }
                return await RenderWithPopplerAsync(pdfPath, pathForPage, maxPages, scale);
            });

        private static List<string> RenderWithPdfium(string pdfPath, Func<int, int, string> pathForPage, int maxPages, double scale)
        {
            var output = new List<string>();
            lock (Gate)
            {
                if (!_initialized)
                {
                    Native.FPDF_InitLibrary();
                    _initialized = true;
                }

                IntPtr doc = Native.FPDF_LoadDocument(pdfPath, null);
                if (doc == IntPtr.Zero)
                {
                    throw new InvalidOperationException(Native.FPDF_GetLastError() switch
                    {
                        4 => "This PDF is password-protected. Unlock it first.",
                        2 => "The PDF file can't be opened.",
                        _ => "This PDF is damaged or not a PDF."
                    });
                }

                try
                {
                    int count = Math.Min(Native.FPDF_GetPageCount(doc), Math.Max(1, maxPages));
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr page = Native.FPDF_LoadPage(doc, i);
                        if (page == IntPtr.Zero) throw new InvalidOperationException($"Page {i + 1} of the PDF can't be read.");
                        try
                        {
                            // PDF points are 1/72 inch; the target is scale × 96 DPI, like the Windows renderer
                            int w = Math.Max(1, (int)Math.Round(Native.FPDF_GetPageWidthF(page) * 96 / 72 * scale));
                            int h = Math.Max(1, (int)Math.Round(Native.FPDF_GetPageHeightF(page) * 96 / 72 * scale));

                            IntPtr bitmap = Native.FPDFBitmap_Create(w, h, 0);
                            if (bitmap == IntPtr.Zero) throw new OutOfMemoryException($"Page {i + 1} is too large to render.");
                            try
                            {
                                Native.FPDFBitmap_FillRect(bitmap, 0, 0, w, h, 0xFFFFFFFF);
                                Native.FPDF_RenderPageBitmap(bitmap, page, 0, 0, w, h, 0, Native.FPDF_ANNOT);

                                int stride = Native.FPDFBitmap_GetStride(bitmap);
                                var pixels = new byte[stride * h];
                                Marshal.Copy(Native.FPDFBitmap_GetBuffer(bitmap), pixels, 0, pixels.Length);

                                string target = pathForPage(i + 1, count);
                                var image = Services.Platform.Images.FromPixels(w, h, 96 * scale, RasterFormat.Bgr32, pixels, stride);
                                using (var fs = File.Create(target)) Services.Platform.Images.Encode(image, fs, ImageEncoding.Png());
                                output.Add(target);
                            }
                            finally
                            {
                                Native.FPDFBitmap_Destroy(bitmap);
                            }
                        }
                        finally
                        {
                            Native.FPDF_ClosePage(page);
                        }
                    }
                }
                finally
                {
                    Native.FPDF_CloseDocument(doc);
                }
            }
            return output;
        }

        /// <summary>One pdftoppm run per page, so each page lands at the path the caller chose.</summary>
        private static async Task<List<string>> RenderWithPopplerAsync(string pdfPath, Func<int, int, string> pathForPage, int maxPages, double scale)
        {
            string tool = Services.Platform.FindOnPath("pdftoppm")
                          ?? throw new InvalidOperationException($"Rendering PDF pages needs PDFium or poppler ({InstallHints.Command("poppler")}).");
            int count = Math.Min(PdfToolsService.GetPageCount(pdfPath), Math.Max(1, maxPages));
            string dpi = (96 * scale).ToString("0.###", CultureInfo.InvariantCulture);
            var output = new List<string>();
            for (int page = 1; page <= count; page++)
            {
                string target = pathForPage(page, count);
                string prefix = target.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? target[..^4] : target;
                var psi = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (string a in new[] { "-png", "-r", dpi, "-f", page.ToString(CultureInfo.InvariantCulture), "-l", page.ToString(CultureInfo.InvariantCulture), "-singlefile", pdfPath, prefix })
                {
                    psi.ArgumentList.Add(a);
                }
                using var p = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start pdftoppm.");
                string errors = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode != 0 || !File.Exists(prefix + ".png")) throw new InvalidOperationException($"pdftoppm couldn't render page {page}: {errors.Trim()}");
                if (prefix + ".png" != target) File.Move(prefix + ".png", target, true);
                output.Add(target);
            }
            return output;
        }

        private static class Native
        {
            private const string Lib = "pdfium";
            public const int FPDF_ANNOT = 0x01;

            [DllImport(Lib)] public static extern void FPDF_InitLibrary();
            [DllImport(Lib)] public static extern IntPtr FPDF_LoadDocument([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);
            [DllImport(Lib)] public static extern uint FPDF_GetLastError();
            [DllImport(Lib)] public static extern int FPDF_GetPageCount(IntPtr document);
            [DllImport(Lib)] public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
            [DllImport(Lib)] public static extern float FPDF_GetPageWidthF(IntPtr page);
            [DllImport(Lib)] public static extern float FPDF_GetPageHeightF(IntPtr page);
            [DllImport(Lib)] public static extern void FPDF_ClosePage(IntPtr page);
            [DllImport(Lib)] public static extern void FPDF_CloseDocument(IntPtr document);
            [DllImport(Lib)] public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
            [DllImport(Lib)] public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
            [DllImport(Lib)] public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
            [DllImport(Lib)] public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
            [DllImport(Lib)] public static extern int FPDFBitmap_GetStride(IntPtr bitmap);
            [DllImport(Lib)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        }
    }
}
