using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Tests
{
    /// <summary>A README with relative screenshots converts to every Markdown output, pictures included.</summary>
    public static class MarkdownVerification
    {
        public static async Task RunAsync(ConversionEngine engine, AppSettings settings, string tempDir, string testPng)
        {
            Console.WriteLine("[22] Markdown / README");

            string repo = Path.Combine(tempDir, "my repo");
            Directory.CreateDirectory(Path.Combine(repo, "docs", "img"));
            File.Copy(testPng, Path.Combine(repo, "docs", "img", "screen shot.png"));
            string readme = Path.Combine(repo, "README.md");
            File.WriteAllText(readme, string.Join("\n",
                "# My Project", "", "Intro with **bold**, `code` and a [link](https://example.com).", "",
                "![Screenshot](docs/img/screen%20shot.png)", "",
                "<p align=\"center\"><img src=\"docs/img/screen shot.png\" width=\"60\"></p>", "",
                "> [!NOTE]", "> Heads up.", "",
                "- [x] done", "- [ ] todo", "",
                "| Col | Val |", "|-----|-----|", "| a | 1 |", "",
                "```csharp", "Console.WriteLine(\"hi\");", "```"));

            var items = FormatRegistry.GetWheelItems(readme, WheelMode.Convert);
            Console.WriteLine($"     Wheel: {string.Join(", ", items.Select(i => i.Title))}");
            foreach (string ext in new[] { "pdf", "txt", "rtf" })
            {
                if (!items.Any(i => i.Extension == ext)) throw new Exception($"README wheel is missing {ext}");
            }

            foreach (string ext in new[] { "pdf", "html", "txt", "docx", "rtf" })
            {
                if (ext is "docx" or "rtf" && !DocumentEngine.HasWord && DocumentEngine.LibreOfficePath == null)
                {
                    Console.WriteLine($"     (skip {ext}: no Word or LibreOffice)");
                    continue;
                }

                var item = items.FirstOrDefault(i => i.Extension == ext) ?? throw new Exception($"No {ext} option");
                var started = DateTime.Now;
                var job = await engine.ExecuteAsync(readme, item, settings);
                string detail = "";
                if (job.IsSuccess)
                {
                    switch (ext)
                    {
                        case "pdf":
                        {
                            using var pdf = UglyToad.PdfPig.PdfDocument.Open(job.TargetPath);
                            int images = pdf.GetPages().Sum(p => p.GetImages().Count());
                            string text = string.Join(" ", pdf.GetPages().Select(p => p.Text));
                            detail = $"{pdf.NumberOfPages} page(s), {images} picture(s), via {(BrowserRenderService.IsAvailable ? "browser" : "Office")}";
                            if (images < 2 || !text.Contains("My Project")) throw new Exception($"README PDF is missing pictures or text ({detail})");
                            break;
                        }
                        case "txt":
                        {
                            string text = File.ReadAllText(job.TargetPath);
                            detail = $"{text.Length} chars";
                            if (text.Contains("**") || text.Contains("# My") || !text.Contains("My Project")) throw new Exception("Plain text still has Markdown symbols");
                            break;
                        }
                        case "html":
                        {
                            // Saved beside the README: links stay relative so the folder can be moved or published
                            string html = File.ReadAllText(job.TargetPath);
                            detail = "relative pictures kept";
                            if (html.Contains("file:///") || !html.Contains("docs/img/screen%20shot.png")) throw new Exception("HTML beside the README should keep relative paths");
                            break;
                        }
                        case "docx":
                        {
                            // Pictures must travel inside the document, not as links to this PC's files
                            using var zip = System.IO.Compression.ZipFile.OpenRead(job.TargetPath);
                            int media = zip.Entries.Count(e => e.FullName.StartsWith("word/media/"));
                            string rels = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open()).ReadToEnd();
                            bool linked = rels.Contains("relationships/image\" Target=\"file:", StringComparison.OrdinalIgnoreCase) ||
                                          System.Text.RegularExpressions.Regex.IsMatch(rels, "relationships/image\"[^>]*TargetMode=\"External\"");
                            detail = $"{new FileInfo(job.TargetPath).Length / 1024} KB, {media} embedded picture(s)";
                            if (media < 1 || linked) throw new Exception($"README DOCX pictures aren't embedded ({detail}, linked={linked})");
                            break;
                        }
                        default:
                            detail = $"{new FileInfo(job.TargetPath).Length / 1024} KB";
                            break;
                    }
                }
                Console.WriteLine($"     README → {ext,-4} Success={job.IsSuccess} {(DateTime.Now - started).TotalSeconds,4:0.0}s {Path.GetFileName(job.TargetPath)} {detail} {job.ErrorMessage}");
                if (!job.IsSuccess) throw new Exception($"README → {ext} failed: {job.ErrorMessage}");
            }

            // HTML into a different output folder: pictures must still load, so they become absolute
            string outDir = Path.Combine(tempDir, "exports");
            Directory.CreateDirectory(outDir);
            var elsewhere = new AppSettings { SaveInSameDirectory = false, CustomOutputDirectory = outDir };
            var htmlJob = await engine.ExecuteAsync(readme, items.First(i => i.Extension == "html"), elsewhere);
            string moved = htmlJob.IsSuccess ? File.ReadAllText(htmlJob.TargetPath) : "";
            bool absolute = moved.Contains(new Uri(Path.Combine(repo, "docs", "img", "screen shot.png")).AbsoluteUri) && moved.Contains("<base href=\"file:///");
            Console.WriteLine($"     README → html in another folder: Success={htmlJob.IsSuccess}, absolute pictures={absolute} {htmlJob.ErrorMessage}");
            if (!absolute) throw new Exception("HTML saved elsewhere lost its pictures");
        }
    }
}
