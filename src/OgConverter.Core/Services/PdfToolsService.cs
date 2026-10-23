using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OgConverter.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace OgConverter.Services
{
    /// <summary>
    /// Offline PDF manipulation: PDFsharp for structural edits (split, rotate, metadata; compression is in <see cref="PdfCompressor"/>),
    /// PdfPig for text extraction. Rendering to images lives in <see cref="PdfRenderService"/>.
    /// </summary>
    public static class PdfToolsService
    {
        /// <summary>
        /// Cheap check used to decide between Protect and Unlock in the wheel: an encrypted PDF names an
        /// /Encrypt dictionary in its trailer (end of file, or start of file when linearized).
        /// </summary>
        public static bool IsEncrypted(string pdfPath)
        {
            try
            {
                using var fs = File.OpenRead(pdfPath);
                const int window = 64 * 1024;
                var buffer = new byte[window];

                int read = fs.Read(buffer, 0, (int)Math.Min(window, fs.Length));
                if (Encoding.ASCII.GetString(buffer, 0, read).Contains("/Encrypt", StringComparison.Ordinal)) return true;

                if (fs.Length > window)
                {
                    fs.Seek(-window, SeekOrigin.End);
                    read = fs.Read(buffer, 0, window);
                    return Encoding.ASCII.GetString(buffer, 0, read).Contains("/Encrypt", StringComparison.Ordinal);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Encrypts the PDF with AES-256 and no restrictions. The password is required to open it.</summary>
        public static void Protect(string pdfPath, string targetPath, string password) =>
            Protect(pdfPath, targetPath, new ProtectionOptions { Method = ProtectionMethod.PdfPassword, Password = password });

        /// <summary>
        /// Encrypts the PDF with the chosen cipher and permissions. The open password unlocks reading;
        /// restrictions are tied to a separate permissions password (random if not given, so they can't be lifted).
        /// </summary>
        public static void Protect(string pdfPath, string targetPath, ProtectionOptions o)
        {
            string password = o.Password;
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("A password is required.", nameof(o));

            PdfDocument doc;
            try
            {
                doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
            }
            catch (Exception ex) when (IsPasswordError(ex))
            {
                throw new InvalidOperationException("This PDF already has a password. Unlock it first to set a new one.", ex);
            }

            using (doc)
            {
                bool restricted = !(o.AllowPrinting && o.AllowCopying && o.AllowEditing && o.AllowComments);
                var s = doc.SecuritySettings;
                s.UserPassword = password;
                // Opening with the owner password grants every permission, so restrictions need a different one
                s.OwnerPassword = !string.IsNullOrEmpty(o.PermissionsPassword) ? o.PermissionsPassword
                                : restricted ? ProtectionService.GeneratePassword(32)
                                : password;

                s.PermitPrint = o.AllowPrinting;
                s.PermitFullQualityPrint = o.AllowPrinting;
                s.PermitExtractContent = o.AllowCopying;
                s.PermitModifyDocument = o.AllowEditing;
                s.PermitAssembleDocument = o.AllowEditing;
                s.PermitAnnotations = o.AllowComments;
                s.PermitFormsFill = o.AllowComments;

                switch (o.PdfCipher)
                {
                    case PdfCipher.Aes128: doc.SecurityHandler.SetEncryptionToV4UsingAES(true); break;  // PDF 1.6, Acrobat 7+
                    case PdfCipher.Rc4_128: doc.SecurityHandler.SetEncryptionToV2With128Bits(); break;  // legacy, PDF 1.4
                    default: doc.SecurityHandler.SetEncryptionToV5(true); break;                         // AES-256, PDF 2.0
                }
                doc.Save(targetPath);
            }
        }

        /// <summary>Opens the PDF with its password and saves an unencrypted copy.</summary>
        public static void Unlock(string pdfPath, string targetPath, string password)
        {
            using var doc = OpenWithPassword(pdfPath, password, PdfDocumentOpenMode.Modify);
            doc.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
            doc.Save(targetPath);
        }

        /// <summary>True if the password opens the PDF. Used by the Unlock dialog to validate before running.</summary>
        public static bool CheckPassword(string pdfPath, string password)
        {
            try
            {
                using var doc = OpenWithPassword(pdfPath, password, PdfDocumentOpenMode.Import);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static PdfDocument OpenWithPassword(string pdfPath, string password, PdfDocumentOpenMode mode)
        {
            try
            {
                return PdfReader.Open(pdfPath, password, mode);
            }
            catch (Exception ex) when (IsPasswordError(ex))
            {
                throw new InvalidOperationException("That password is incorrect.", ex);
            }
        }

        private static bool IsPasswordError(Exception ex) =>
            ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.GetType().Name.Contains("Password");

        public static int GetPageCount(string pdfPath)
        {
            using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }

        /// <summary>
        /// Parses a page range expression such as "1-3, 5, 8-" (8 to the end) or "-4" (1 to 4).
        /// Pages are 1-based. Throws <see cref="FormatException"/> with a user-facing message on invalid input.
        /// </summary>
        public static List<(int Start, int End)> ParsePageRanges(string text, int pageCount)
        {
            var ranges = new List<(int, int)>();
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Enter at least one page or range.");

            foreach (string rawPart in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = rawPart.Trim();
                if (part.Length == 0) continue;

                int start, end;
                int dash = part.IndexOf('-');
                if (dash < 0)
                {
                    start = end = ParsePage(part);
                }
                else
                {
                    string left = part[..dash].Trim();
                    string right = part[(dash + 1)..].Trim();
                    start = left.Length == 0 ? 1 : ParsePage(left);
                    end = right.Length == 0 ? pageCount : ParsePage(right);
                }

                if (start < 1 || end < 1) throw new FormatException($"\"{part}\": pages start at 1.");
                if (start > pageCount || end > pageCount) throw new FormatException($"\"{part}\": this PDF only has {pageCount} page{(pageCount == 1 ? "" : "s")}.");
                if (start > end) throw new FormatException($"\"{part}\": start page is after end page.");

                ranges.Add((start, end));
            }

            if (ranges.Count == 0) throw new FormatException("Enter at least one page or range.");
            return ranges;

            static int ParsePage(string s) =>
                int.TryParse(s, out int n) ? n : throw new FormatException($"\"{s}\" is not a page number.");
        }

        /// <summary>Copies the given 1-based pages, in the given order, into a single new PDF.</summary>
        public static void ExtractPages(string pdfPath, IEnumerable<int> pageNumbers, string targetPath)
        {
            using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            using var output = new PdfDocument();
            output.Version = input.Version;

            foreach (int page in pageNumbers)
            {
                output.AddPage(input.Pages[page - 1]);
            }
            output.Save(targetPath);
        }

        /// <summary>Writes each range to its own PDF. Returns the written paths in range order.</summary>
        public static List<string> SplitRanges(string pdfPath, IReadOnlyList<(int Start, int End)> ranges, Func<(int Start, int End), string> pathForRange)
        {
            var written = new List<string>();
            using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);

            foreach (var range in ranges)
            {
                using var output = new PdfDocument();
                output.Version = input.Version;
                for (int p = range.Start; p <= range.End; p++)
                {
                    output.AddPage(input.Pages[p - 1]);
                }

                string target = pathForRange(range);
                output.Save(target);
                written.Add(target);
            }

            return written;
        }

        /// <summary>Appends all pages of every input PDF, in list order, into one PDF.</summary>
        public static void Merge(IReadOnlyList<string> inputPaths, string targetPath)
        {
            using var output = new PdfDocument();
            foreach (string path in inputPaths)
            {
                using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                if (input.Version > output.Version) output.Version = input.Version;
                for (int i = 0; i < input.PageCount; i++)
                {
                    output.AddPage(input.Pages[i]);
                }
            }
            output.Save(targetPath);
        }

        /// <summary>
        /// Wraps an image in a single-page PDF sized to the image's physical size (96 dpi when the file has none).
        /// Decoding goes through the image backend (WIC on Windows) so anything the system can open works and EXIF rotation is applied.
        /// Upright JPEGs are embedded byte-for-byte; everything else is embedded as lossless PNG (alpha kept).
        /// </summary>
        public static void ImageToPdf(string imagePath, string targetPath)
        {
            var image = ImageCodec.TryLoad(imagePath, applyOrientation: true, maxDimension: 0)
                ?? throw new InvalidOperationException(OperatingSystem.IsWindows() ? $"Windows can't decode {Path.GetFileName(imagePath)}." : $"{Path.GetFileName(imagePath)} can't be decoded.");

            using var data = new MemoryStream();
            string ext = Path.GetExtension(imagePath).ToLowerInvariant();
            XImage? xImage = null;
            if (ext is ".jpg" or ".jpeg" && ImageCodec.ReadOrientation(image.Metadata) == 1)
            {
                using var fs = File.OpenRead(imagePath);
                fs.CopyTo(data);
                data.Position = 0;
                // PDFsharp only reads JPEGs with a JFIF or Exif header (Skia and some web tools write neither): those go in as PNG
                try { xImage = XImage.FromStream(data); }
                catch (InvalidOperationException) { data.SetLength(0); }
            }
            if (xImage == null)
            {
                Platform.Images.Encode(image.Bitmap, data, ImageEncoding.Png());
                data.Position = 0;
                xImage = XImage.FromStream(data);
            }

            var bmp = image.Bitmap;
            double dpiX = bmp.DpiX > 1 ? bmp.DpiX : 96, dpiY = bmp.DpiY > 1 ? bmp.DpiY : 96;
            double width = bmp.Width * 72.0 / dpiX, height = bmp.Height * 72.0 / dpiY;

            using var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(height);
            using (xImage)
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawImage(xImage, 0, 0, width, height);
            }
            doc.Save(targetPath);
        }

        /// <summary>Rotates every page clockwise by the given multiple of 90 degrees.</summary>
        public static void RotatePages(string pdfPath, string targetPath, int degrees)
        {
            using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
            foreach (PdfPage page in doc.Pages)
            {
                page.Rotate = ((page.Rotate + degrees) % 360 + 360) % 360;
            }
            doc.Save(targetPath);
        }

        /// <summary>Removes the document info dictionary (author, title, dates, tool names) and XMP metadata.</summary>
        public static void StripMetadata(string pdfPath, string targetPath)
        {
            using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
            doc.Info.Elements.Clear();
            doc.Internals.Catalog.Elements.Remove("/Metadata");
            doc.Save(targetPath);
        }

        /// <summary>Extracts text in reading order, one block per page (headed "Page n", or <paramref name="unit"/> n).</summary>
        public static string ExtractText(string pdfPath, string unit = "Page")
        {
            var sb = new StringBuilder();
            using var doc = PigDocument.Open(pdfPath);

            foreach (var page in doc.GetPages())
            {
                if (doc.NumberOfPages > 1)
                {
                    sb.AppendLine($"----- {unit} {page.Number} -----");
                }
                sb.AppendLine(ContentOrderTextExtractor.GetText(page).Trim());
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd() + Environment.NewLine;
        }
    }
}
