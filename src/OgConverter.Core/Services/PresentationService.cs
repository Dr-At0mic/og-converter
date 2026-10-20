using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace OgConverter.Services
{
    /// <summary>
    /// Presentation extras on top of <see cref="DocumentEngine"/>: anything that needs the rendered slides
    /// (images, text, handouts, slideshow video) goes through a temporary PDF so it works with PowerPoint or LibreOffice;
    /// notes and media are read straight from the PPTX package.
    /// </summary>
    public static class PresentationService
    {
        public const int SecondsPerSlide = 5;

        /// <summary>Exports the deck to a temporary PDF, runs <paramref name="work"/> on it, then deletes it.</summary>
        public static async Task<T> WithPdfAsync<T>(string src, Func<string, Task<T>> work)
        {
            string pdf = Path.Combine(Path.GetTempPath(), $"og-converter-slides-{Guid.NewGuid():N}.pdf");
            try
            {
                await DocumentEngine.ConvertAsync(src, pdf, "pdf");
                return await work(pdf);
            }
            finally
            {
                try { File.Delete(pdf); } catch { }
            }
        }

        /// <summary>Same as <see cref="WithPdfAsync"/> but with a PPTX: .ppt and .odp are converted first, .pptx is used as is.</summary>
        private static async Task<T> WithPptxAsync<T>(string src, Func<string, T> work)
        {
            if (Path.GetExtension(src).Equals(".pptx", StringComparison.OrdinalIgnoreCase)) return work(src);

            string pptx = Path.Combine(Path.GetTempPath(), $"og-converter-slides-{Guid.NewGuid():N}.pptx");
            try
            {
                await DocumentEngine.ConvertAsync(src, pptx, "pptx");
                return work(pptx);
            }
            finally
            {
                try { File.Delete(pptx); } catch { }
            }
        }

        // ------------------------------------------------------------------ Video

        /// <summary>
        /// MP4 of the deck. PowerPoint records it with transitions, animations and timings; without PowerPoint the
        /// rendered slides become a still-image slideshow (<see cref="SecondsPerSlide"/> s each) via FFmpeg.
        /// </summary>
        public static async Task ToVideoAsync(string src, string dst, string? ffmpeg, IProgress<double>? progress)
        {
            if (DocumentEngine.HasPowerPoint)
            {
                await DocumentEngine.ExportPowerPointVideoAsync(src, dst, SecondsPerSlide);
                return;
            }
            if (ffmpeg == null) throw new InvalidOperationException("Turning slides into a video needs PowerPoint or FFmpeg.");
            await WithPdfAsync(src, async pdf => { await SlideshowFromPdfAsync(pdf, dst, ffmpeg, progress); return true; });
        }

        /// <summary>Still-image slideshow: each PDF page held for <see cref="SecondsPerSlide"/> seconds, 1080p H.264.</summary>
        public static async Task SlideshowFromPdfAsync(string pdf, string dst, string ffmpeg, IProgress<double>? progress)
        {
            string dir = Path.Combine(Path.GetTempPath(), "og-converter-slides-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var pages = await PdfRenderService.RenderPagesToPngAsync(pdf, (p, _) => Path.Combine(dir, $"slide_{p:D3}.png"));
                if (pages.Count == 0) throw new InvalidOperationException("The presentation has no slides.");

                // Concat demuxer: the last file is listed twice or its duration is ignored
                var list = new StringBuilder();
                foreach (string page in pages)
                {
                    list.Append("file '").Append(Path.GetFileName(page)).Append("'\n");
                    list.Append("duration ").Append(SecondsPerSlide.ToString(CultureInfo.InvariantCulture)).Append('\n');
                }
                list.Append("file '").Append(Path.GetFileName(pages[^1])).Append("'\n");
                string listPath = Path.Combine(dir, "slides.txt");
                await File.WriteAllTextAsync(listPath, list.ToString());

                await MediaToolkit.RunAsync(ffmpeg, new[]
                {
                    "-f", "concat", "-safe", "0", "-i", listPath,
                    "-vf", "scale='min(1920,iw)':-2:flags=lanczos,format=yuv420p", "-r", "30",
                    "-c:v", "libx264", "-tune", "stillimage", "-crf", "20", "-preset", "fast", "-movflags", "+faststart",
                    dst
                }, pages.Count * SecondsPerSlide, progress, dir);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ------------------------------------------------------------------ Handouts

        /// <summary>Prints <paramref name="perPage"/> slides per A4 portrait page (2 columns), each framed, in order.</summary>
        public static void HandoutFromPdf(string pdf, string dst, int perPage = 6)
        {
            const double margin = 36, gap = 18;   // points
            int columns = 2, rows = (perPage + 1) / 2;

            using var form = XPdfForm.FromFile(pdf);
            using var doc = new PdfDocument();
            var pen = new XPen(XColors.LightGray, 0.75);

            PdfPage? page = null;
            XGraphics? gfx = null;
            try
            {
                for (int i = 0; i < form.PageCount; i++)
                {
                    int slot = i % perPage;
                    if (slot == 0)
                    {
                        gfx?.Dispose();
                        page = doc.AddPage();
                        page.Size = PdfSharp.PageSize.A4;
                        gfx = XGraphics.FromPdfPage(page);
                    }

                    double cellW = (page!.Width.Point - 2 * margin - (columns - 1) * gap) / columns;
                    double cellH = (page.Height.Point - 2 * margin - (rows - 1) * gap) / rows;
                    double cellX = margin + (slot % columns) * (cellW + gap);
                    double cellY = margin + (slot / columns) * (cellH + gap);

                    form.PageNumber = i + 1;
                    double scale = Math.Min(cellW / form.PointWidth, cellH / form.PointHeight);
                    double w = form.PointWidth * scale, h = form.PointHeight * scale;
                    var rect = new XRect(cellX + (cellW - w) / 2, cellY + (cellH - h) / 2, w, h);
                    gfx!.DrawImage(form, rect);
                    gfx.DrawRectangle(pen, rect);
                }
            }
            finally
            {
                gfx?.Dispose();
            }

            if (doc.PageCount == 0) throw new InvalidOperationException("The presentation has no slides.");
            doc.Save(dst);
        }

        // ------------------------------------------------------------------ Speaker notes

        private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>Writes each slide's title and speaker notes, in slide order, as UTF-8 text. Returns how many slides had notes.</summary>
        public static Task<int> ExtractNotesAsync(string src, string dst) => WithPptxAsync(src, pptx =>
        {
            using var zip = ZipFile.OpenRead(pptx);
            var sb = new StringBuilder();
            int number = 0, withNotes = 0;

            foreach (string slidePath in SlidePartsInOrder(zip))
            {
                number++;
                var slide = LoadXml(zip, slidePath);
                string title = slide == null ? "" : string.Join(" ", ShapesWithPlaceholder(slide, "title", "ctrTitle").Select(ShapeText)).Trim();

                string notes = "";
                string? notesPath = RelatedPart(zip, slidePath, "/notesSlide");
                if (notesPath != null && LoadXml(zip, notesPath) is { } notesXml)
                {
                    notes = string.Join(Environment.NewLine, ShapesWithPlaceholder(notesXml, "body").Select(ShapeText)).Trim();
                }
                if (notes.Length > 0) withNotes++;

                sb.AppendLine(title.Length > 0 ? $"----- Slide {number}: {title} -----" : $"----- Slide {number} -----");
                sb.AppendLine(notes.Length > 0 ? notes : "(no notes)");
                sb.AppendLine();
            }

            if (number == 0) throw new InvalidOperationException("The presentation has no slides.");
            if (withNotes == 0) throw new InvalidOperationException("None of the slides have speaker notes.");
            File.WriteAllText(dst, sb.ToString().TrimEnd() + Environment.NewLine, new UTF8Encoding(false));
            return withNotes;
        });

        /// <summary>Slide part names ("ppt/slides/slide3.xml") in presentation order (not file-name order).</summary>
        private static List<string> SlidePartsInOrder(ZipArchive zip)
        {
            var presentation = LoadXml(zip, "ppt/presentation.xml") ?? throw new InvalidOperationException("This isn't a valid PowerPoint file.");
            var rels = RelationshipTargets(zip, "ppt/presentation.xml");
            return presentation.Descendants(P + "sldId")
                .Select(id => (string?)id.Attribute(R + "id"))
                .Where(id => id != null && rels.ContainsKey(id))
                .Select(id => rels[id!])
                .ToList();
        }

        private static string? RelatedPart(ZipArchive zip, string part, string typeSuffix)
        {
            var relsXml = LoadXml(zip, RelsPathFor(part));
            var target = relsXml?.Root?.Elements(Rel + "Relationship")
                .FirstOrDefault(r => ((string?)r.Attribute("Type") ?? "").EndsWith(typeSuffix, StringComparison.Ordinal));
            return target == null ? null : ResolvePart(part, (string)target.Attribute("Target")!);
        }

        private static Dictionary<string, string> RelationshipTargets(ZipArchive zip, string part)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var relsXml = LoadXml(zip, RelsPathFor(part));
            if (relsXml?.Root == null) return map;
            foreach (var r in relsXml.Root.Elements(Rel + "Relationship"))
            {
                if ((string?)r.Attribute("Id") is { } id && (string?)r.Attribute("Target") is { } target)
                {
                    map[id] = ResolvePart(part, target);
                }
            }
            return map;
        }

        private static string RelsPathFor(string part)
        {
            int slash = part.LastIndexOf('/');
            return $"{part[..(slash + 1)]}_rels/{part[(slash + 1)..]}.rels";
        }

        /// <summary>"ppt/slides/slide1.xml" + "../notesSlides/notesSlide1.xml" → "ppt/notesSlides/notesSlide1.xml".</summary>
        private static string ResolvePart(string fromPart, string target)
        {
            if (target.StartsWith('/')) return target.TrimStart('/');
            var segments = fromPart.Split('/').SkipLast(1).ToList();
            foreach (string seg in target.Split('/'))
            {
                if (seg == "..") { if (segments.Count > 0) segments.RemoveAt(segments.Count - 1); }
                else if (seg != ".") segments.Add(seg);
            }
            return string.Join('/', segments);
        }

        private static XDocument? LoadXml(ZipArchive zip, string part)
        {
            var entry = zip.GetEntry(part);
            if (entry == null) return null;
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }

        private static IEnumerable<XElement> ShapesWithPlaceholder(XDocument xml, params string[] types) =>
            xml.Descendants(P + "sp").Where(sp =>
                sp.Element(P + "nvSpPr")?.Element(P + "nvPr")?.Element(P + "ph") is { } ph &&
                types.Contains((string?)ph.Attribute("type") ?? "obj"));

        /// <summary>Paragraphs of a shape's text body, one per line.</summary>
        private static string ShapeText(XElement shape) =>
            string.Join(Environment.NewLine, shape.Descendants(A + "p")
                .Select(p => string.Concat(p.Descendants().Where(e => e.Name == A + "t" || e.Name == A + "br")
                                            .Select(e => e.Name == A + "br" ? "\n" : e.Value)))
                .Where(line => line.Trim().Length > 0));

        // ------------------------------------------------------------------ Media

        /// <summary>
        /// Copies the pictures, audio and video embedded in the deck into <paramref name="outDir"/>.
        /// Returns how many files were written.
        /// </summary>
        public static async Task<int> ExtractMediaAsync(string src, string outDir)
        {
            // ODP keeps media in Pictures/ and Media/; read it directly instead of round-tripping through PPTX
            bool odp = Path.GetExtension(src).Equals(".odp", StringComparison.OrdinalIgnoreCase);
            int count = odp
                ? CopyEntries(src, outDir, e => e.StartsWith("Pictures/", StringComparison.Ordinal) || e.StartsWith("Media/", StringComparison.Ordinal))
                : await WithPptxAsync(src, pptx => CopyEntries(pptx, outDir, e => e.StartsWith("ppt/media/", StringComparison.Ordinal)));

            if (count == 0)
            {
                try { Directory.Delete(outDir); } catch { }
                throw new InvalidOperationException("The presentation has no embedded pictures, audio or video.");
            }
            return count;
        }

        private static int CopyEntries(string zipPath, string outDir, Func<string, bool> include)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            int count = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.Length == 0 || !include(entry.FullName)) continue;
                Directory.CreateDirectory(outDir);
                // Flat folder, names taken from the package (no paths from inside the archive)
                entry.ExtractToFile(Path.Combine(outDir, Path.GetFileName(entry.FullName)), overwrite: true);
                count++;
            }
            return count;
        }
    }
}
