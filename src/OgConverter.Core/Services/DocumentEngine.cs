using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Markdig;
using Markdig.Syntax;

namespace OgConverter.Services
{
    public enum DocumentKind
    {
        Text,
        Spreadsheet,
        Presentation
    }

    /// <summary>
    /// Office document conversion (Word/Excel/PowerPoint formats and their OpenDocument equivalents).
    /// Uses Microsoft Office through <see cref="Platform.Office"/> when installed (highest fidelity for Microsoft formats),
    /// otherwise LibreOffice in headless mode. Markdown → HTML is built in (Markdig).
    /// Everything runs locally; nothing is uploaded.
    /// </summary>
    public static class DocumentEngine
    {
        public static string LibreOfficeInstallHint => OperatingSystem.IsWindows()
            ? "Install Microsoft Office, or LibreOffice for free (winget install TheDocumentFoundation.LibreOffice)."
            : $"Install LibreOffice for free ({InstallHints.Command("libreoffice")}).";

        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

        // Office automation is not safe to run concurrently; one conversion at a time
        private static readonly SemaphoreSlim Gate = new(1, 1);

        private static Lazy<string?> LibreOffice = new(FindLibreOffice);

        public static bool HasWord => Platform.Office?.HasWord ?? false;
        public static bool HasExcel => Platform.Office?.HasExcel ?? false;
        public static bool HasPowerPoint => Platform.Office?.HasPowerPoint ?? false;
        public static string? LibreOfficePath => LibreOffice.Value;
        /// <summary>Looks for LibreOffice again, e.g. after installing it from Settings.</summary>
        public static void RefreshLibreOffice() => LibreOffice = new(FindLibreOffice);

        public static bool HasAnyBackend => HasWord || HasExcel || HasPowerPoint || LibreOfficePath != null;

        private static readonly HashSet<string> TextOutputs = new() { "docx", "doc", "odt", "rtf", "pdf", "txt", "html" };
        private static readonly HashSet<string> SheetOutputs = new() { "xlsx", "xls", "ods", "csv", "pdf" };
        private static readonly HashSet<string> SlideOutputs = new() { "pptx", "ppt", "odp", "ppsx", "pdf" };

        public static DocumentKind? KindOf(string ext) => ext.ToLowerInvariant() switch
        {
            "docx" or "doc" or "odt" or "rtf" or "txt" or "html" or "htm" or "md" or "markdown" => DocumentKind.Text,
            "xlsx" or "xls" or "ods" or "csv" => DocumentKind.Spreadsheet,
            "pptx" or "ppt" or "odp" or "ppsx" => DocumentKind.Presentation,
            _ => null
        };

        /// <summary>True if this engine is responsible for converting <paramref name="srcExt"/> to <paramref name="dstExt"/>.</summary>
        public static bool Handles(string srcExt, string dstExt)
        {
            srcExt = srcExt.ToLowerInvariant();
            dstExt = dstExt.ToLowerInvariant();
            if (srcExt == dstExt) return false;

            if (IsMarkdown(srcExt)) return dstExt is "html" or "docx" or "odt" or "pdf" or "rtf" or "txt";

            // PDF → editable document needs Word's PDF reflow; LibreOffice opens PDFs as drawings
            if (srcExt == "pdf") return dstExt is "docx" or "odt" or "rtf" or "doc";

            return KindOf(srcExt) switch
            {
                DocumentKind.Text => TextOutputs.Contains(dstExt) && !(srcExt == "txt" && dstExt == "txt"),
                DocumentKind.Spreadsheet => SheetOutputs.Contains(dstExt),
                DocumentKind.Presentation => SlideOutputs.Contains(dstExt),
                _ => false
            };
        }

        /// <summary>Human-readable summary of the available backends, for Settings.</summary>
        public static string DescribeBackends()
        {
            var office = new List<string>();
            if (HasWord) office.Add("Word");
            if (HasExcel) office.Add("Excel");
            if (HasPowerPoint) office.Add("PowerPoint");

            var parts = new List<string>();
            if (office.Count > 0) parts.Add("Microsoft " + string.Join(", ", office));
            if (LibreOfficePath != null) parts.Add("LibreOffice");
            return parts.Count > 0 ? string.Join(" + ", parts) : "No document engine found. " + LibreOfficeInstallHint;
        }

        public static async Task ConvertAsync(string src, string dst, string dstExt)
        {
            string srcExt = Path.GetExtension(src).TrimStart('.').ToLowerInvariant();
            dstExt = dstExt.ToLowerInvariant();

            // Markdown: render to HTML first, then treat it as an HTML document
            if (IsMarkdown(srcExt))
            {
                string markdown = await File.ReadAllTextAsync(src);
                if (dstExt == "txt")
                {
                    await File.WriteAllTextAsync(dst, Markdown.ToPlainText(markdown, MarkdownPipeline), new UTF8Encoding(false));
                    return;
                }

                // Relative pictures (README screenshots) only resolve from the README's own folder; anywhere else
                // (the temp folder, another output folder) they need absolute addresses
                string srcDir = Path.GetDirectoryName(Path.GetFullPath(src))!;
                bool besideSource = string.Equals(Path.GetDirectoryName(Path.GetFullPath(dst)), srcDir, Platform.PathComparison);
                bool viaBrowser = dstExt == "pdf" && BrowserRenderService.IsAvailable;
                using var resources = viaBrowser ? BrowserRenderService.MakeReadable(srcDir) : null;
                string html = MarkdownToHtml(markdown, Path.GetFileNameWithoutExtension(src), dstExt == "html" && besideSource ? null : resources?.Path ?? srcDir);
                if (dstExt == "html")
                {
                    await File.WriteAllTextAsync(dst, html, new UTF8Encoding(false));
                    return;
                }
                // LibreOffice links the pictures of an imported web page; inside the page they travel with the document
                if (!HasWord && dstExt is "docx" or "doc" or "odt" or "rtf") html = EmbedLocalPictures(html, srcDir);

                string tempHtml = Path.Combine(BrowserRenderService.WorkFolder, $"og-converter-md-{Guid.NewGuid():N}.html");
                await File.WriteAllTextAsync(tempHtml, html, new UTF8Encoding(false));
                try
                {
                    // A browser lays out code, tables and images like GitHub does; Word's HTML import is rougher
                    if (viaBrowser) await BrowserRenderService.PrintAsync(tempHtml, dst);
                    else await ConvertAsync(tempHtml, dst, dstExt);
                }
                finally
                {
                    TryDelete(tempHtml);
                }
                return;
            }

            // Web pages print best in a browser: their own styles, backgrounds and fonts, no Word import quirks
            if (srcExt is "html" or "htm" or "mht" or "mhtml" && dstExt == "pdf" && BrowserRenderService.IsAvailable)
            {
                await BrowserRenderService.PrintAsync(src, dst);
                return;
            }

            DocumentKind kind = srcExt == "pdf" ? DocumentKind.Text : KindOf(srcExt) ?? throw new InvalidOperationException($".{srcExt} is not a supported document format.");

            await Gate.WaitAsync();
            try
            {
                if (srcExt == "pdf")
                {
                    if (!HasWord) throw new InvalidOperationException("Turning a PDF into an editable document needs Microsoft Word.");
                    await Platform.Office!.ConvertAsync(DocumentKind.Text, src, dst, dstExt);
                    return;
                }

                bool officeAvailable = kind switch
                {
                    DocumentKind.Text => HasWord,
                    DocumentKind.Spreadsheet => HasExcel,
                    _ => HasPowerPoint
                };

                if (officeAvailable)
                {
                    await Platform.Office!.ConvertAsync(kind, src, dst, dstExt);
                }
                else if (LibreOfficePath != null)
                {
                    await ConvertWithLibreOfficeAsync(src, dst, dstExt);
                }
                else
                {
                    throw new InvalidOperationException($"Converting .{srcExt} files needs a document engine. {LibreOfficeInstallHint}");
                }
            }
            finally
            {
                Gate.Release();
            }

            if (!File.Exists(dst))
            {
                throw new InvalidOperationException($"The document engine finished but did not produce a .{dstExt} file.");
            }
        }

        /// <summary>
        /// Markdown or a web page → one tall picture of the whole rendered page (PNG). Markdown is laid out like
        /// GitHub's README view, at its reading width.
        /// </summary>
        public static async Task RenderToPngAsync(string src, string pngPath)
        {
            if (!BrowserRenderService.IsAvailable) throw new InvalidOperationException(BrowserRenderService.MissingBrowserHint);
            string srcExt = Path.GetExtension(src).TrimStart('.').ToLowerInvariant();
            if (!IsMarkdown(srcExt))
            {
                await BrowserRenderService.ScreenshotAsync(src, pngPath);
                return;
            }

            using var resources = BrowserRenderService.MakeReadable(Path.GetDirectoryName(Path.GetFullPath(src))!);
            string html = MarkdownToHtml(await File.ReadAllTextAsync(src), Path.GetFileNameWithoutExtension(src), resources.Path);
            string tempHtml = Path.Combine(BrowserRenderService.WorkFolder, $"og-converter-md-{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(tempHtml, html, new UTF8Encoding(false));
            try
            {
                await BrowserRenderService.ScreenshotAsync(tempHtml, pngPath, width: 980);
            }
            finally
            {
                TryDelete(tempHtml);
            }
        }

        // ------------------------------------------------------------------ Office passwords

        public static bool IsOfficeFile(string ext) => ext.ToLowerInvariant().TrimStart('.') is "docx" or "doc" or "xlsx" or "xls" or "pptx" or "ppt";

        public static bool CanProtectOffice(string ext) => ext.ToLowerInvariant().TrimStart('.') switch
        {
            "docx" or "doc" => HasWord,
            "xlsx" or "xls" => HasExcel,
            "pptx" or "ppt" => HasPowerPoint,
            _ => false
        };

        /// <summary>
        /// An encrypted .docx/.xlsx/.pptx is stored as an OLE compound file (D0 CF 11 E0) instead of a ZIP package.
        /// </summary>
        public static bool IsEncryptedOfficeFile(string path)
        {
            if (Path.GetExtension(path).ToLowerInvariant() is not (".docx" or ".xlsx" or ".pptx")) return false;
            try
            {
                using var fs = File.OpenRead(path);
                Span<byte> head = stackalloc byte[4];
                return fs.Read(head) == 4 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Saves a copy with "Encrypt with password". Modern formats use Office's Agile Encryption (AES-256, SHA-512);
        /// legacy .doc/.xls/.ppt can only use the older RC4 CryptoAPI scheme.
        /// </summary>
        public static Task ProtectOfficeAsync(string src, string dst, string password, string modifyPassword) =>
            RunOfficePasswordJobAsync(src, dst, openPassword: null, newPassword: password, modifyPassword: modifyPassword);

        /// <summary>Opens with the password and saves an unencrypted copy.</summary>
        public static Task UnlockOfficeAsync(string src, string dst, string password) =>
            RunOfficePasswordJobAsync(src, dst, openPassword: password, newPassword: "", modifyPassword: "");

        private static async Task RunOfficePasswordJobAsync(string src, string dst, string? openPassword, string newPassword, string modifyPassword)
        {
            string ext = Path.GetExtension(src).TrimStart('.').ToLowerInvariant();
            if (!CanProtectOffice(ext)) throw new InvalidOperationException(OperatingSystem.IsWindows() ? $"Password-protecting .{ext} files needs Microsoft Office." : $"Password-protecting .{ext} files isn't available on this system.");

            await Gate.WaitAsync();
            try
            {
                await Platform.Office!.ChangePasswordAsync(src, dst, openPassword, newPassword, modifyPassword);
            }
            finally
            {
                Gate.Release();
            }

            if (!File.Exists(dst)) throw new InvalidOperationException("Office finished but did not save the file.");
        }

        // ------------------------------------------------------------------ Markdown

        private static bool IsMarkdown(string ext) => ext is "md" or "markdown";

        private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        /// <summary>
        /// A standalone page styled like GitHub's README view. With <paramref name="resourceDir"/>, relative image
        /// paths are made absolute (Markdown images directly, raw &lt;img&gt; tags through &lt;base&gt;).
        /// </summary>
        private static string MarkdownToHtml(string markdown, string title, string? resourceDir = null)
        {
            var document = Markdown.Parse(markdown, MarkdownPipeline);
            if (resourceDir != null)
            {
                foreach (var link in document.Descendants<Markdig.Syntax.Inlines.LinkInline>())
                {
                    if (link.IsImage && link.Url is { Length: > 0 } url && !Uri.TryCreate(url, UriKind.Absolute, out _) && !url.StartsWith('#'))
                    {
                        try { link.Url = new Uri(Path.Combine(resourceDir, Uri.UnescapeDataString(url.Split('?', '#')[0]))).AbsoluteUri; }
                        catch { }
                    }
                }
            }

            using var writer = new StringWriter();
            var renderer = new Markdig.Renderers.HtmlRenderer(writer);
            MarkdownPipeline.Setup(renderer);
            renderer.Render(document);
            string body = writer.ToString();

            string baseTag = resourceDir == null ? "" : $"<base href=\"{new Uri(resourceDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).AbsoluteUri}\">";
            return $$"""
                <!DOCTYPE html>
                <html>
                <head>
                <meta charset="utf-8">
                {{baseTag}}
                <title>{{System.Net.WebUtility.HtmlEncode(title)}}</title>
                <style>
                  @page { margin: 16mm 14mm; }
                  body { font-family: "Segoe UI", -apple-system, Calibri, Arial, sans-serif, "Segoe UI Emoji"; font-size: 11pt; line-height: 1.55; max-width: 48em; margin: 2em auto; padding: 0 1em; color: #1f2328; }
                  h1, h2, h3, h4 { line-height: 1.25; margin: 1.4em 0 0.6em; }
                  h1, h2 { padding-bottom: 0.3em; border-bottom: 1px solid #d1d9e0; }
                  a { color: #0969da; text-decoration: none; }
                  img { max-width: 100%; }
                  code, pre { font-family: "Cascadia Mono", Consolas, monospace; font-size: 9.5pt; }
                  code { background: #eff1f3; padding: 0.15em 0.35em; border-radius: 4px; }
                  pre { background: #f6f8fa; padding: 0.9em 1em; border-radius: 6px; overflow-x: auto; white-space: pre-wrap; word-wrap: break-word; }
                  pre code { background: none; padding: 0; }
                  table { border-collapse: collapse; margin: 1em 0; }
                  th, td { border: 1px solid #d1d9e0; padding: 6px 12px; }
                  th { background: #f6f8fa; }
                  blockquote { border-left: 4px solid #d1d9e0; margin: 1em 0; padding: 0 1em; color: #59636e; }
                  hr { border: 0; border-top: 1px solid #d1d9e0; margin: 1.5em 0; }
                  .task-list-item { list-style: none; }
                  .task-list-item input { margin: 0 0.4em 0 -1.3em; }
                  .markdown-alert { border-left: 4px solid #0969da; padding: 0.4em 1em; margin: 1em 0; color: inherit; }
                  .markdown-alert-title { font-weight: 600; margin: 0 0 0.3em; }
                  .markdown-alert-tip { border-color: #1a7f37; }
                  .markdown-alert-important { border-color: #8250df; }
                  .markdown-alert-warning { border-color: #9a6700; }
                  .markdown-alert-caution { border-color: #d1242f; }
                  pre, table, img, blockquote { break-inside: avoid; }
                  h1, h2, h3, h4 { break-after: avoid; }
                </style>
                </head>
                <body>
                {{body}}
                </body>
                </html>
                """;
        }

        // ------------------------------------------------------------------ Microsoft Office (Platform.Office)

        /// <summary>
        /// Records the presentation as an MP4 with PowerPoint itself, so transitions, animations, rehearsed timings,
        /// narration and embedded media are kept. Slides without timings show for <paramref name="secondsPerSlide"/>.
        /// </summary>
        public static async Task ExportPowerPointVideoAsync(string src, string dst, int secondsPerSlide = 5)
        {
            if (!HasPowerPoint) throw new InvalidOperationException("Recording a video with transitions needs Microsoft PowerPoint.");
            await Gate.WaitAsync();
            try
            {
                await Platform.Office!.RecordVideoAsync(src, dst, secondsPerSlide);
            }
            finally
            {
                Gate.Release();
            }
            if (!File.Exists(dst)) throw new InvalidOperationException("PowerPoint finished but did not produce a video.");
        }

        // ------------------------------------------------------------------ LibreOffice (headless)

        private static string? FindLibreOffice()
        {
            // Outside Windows LibreOffice comes from the package manager
            if (!OperatingSystem.IsWindows())
            {
                return Platform.FindOnPath("soffice", "libreoffice")
                       ?? new[] { "/usr/lib/libreoffice/program/soffice", "/opt/libreoffice/program/soffice" }.FirstOrDefault(File.Exists);
            }

            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe"),
            };

            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string dir in pathEnv.Split(Path.PathSeparator))
                {
                    try { candidates.Add(Path.Combine(dir.Trim(), "soffice.exe")); } catch { }
                }
            }

            return candidates.Find(File.Exists);
        }

        private static async Task ConvertWithLibreOfficeAsync(string src, string dst, string dstExt)
        {
            string filter = dstExt switch
            {
                "txt" => "txt:Text (encoded):UTF8",
                "csv" => "csv:Text - txt - csv (StarCalc):44,34,76,1",
                "html" => "html",
                "ppsx" => "ppsx:Impress MS PowerPoint 2007 XML AutoPlay",
                _ => dstExt
            };

            string outDir = Path.Combine(Path.GetTempPath(), "og-converter-lo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outDir);

            // A private profile lets conversions run even while the user has LibreOffice open
            string profile = new Uri(Path.Combine(Path.GetTempPath(), "og-converter-lo-profile")).AbsoluteUri;

            try
            {
                var psi = new ProcessStartInfo(LibreOfficePath!)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                foreach (string arg in new[] { $"-env:UserInstallation={profile}", "--headless", "--norestore", "--nolockcheck",
                                               "--convert-to", filter, "--outdir", outDir })
                {
                    psi.ArgumentList.Add(arg);
                }
                // A web page opens in Writer/Web, which can only save HTML and text: open it in Writer itself instead
                if (Path.GetExtension(src).ToLowerInvariant() is ".html" or ".htm" && dstExt is "docx" or "doc" or "odt" or "rtf" or "pdf")
                {
                    psi.ArgumentList.Add("--infilter=HTML (StarWriter)");
                }
                psi.ArgumentList.Add(src);

                using var process = Process.Start(psi)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                using var cts = new CancellationTokenSource(Timeout);
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { }
                    throw new TimeoutException("LibreOffice took longer than 3 minutes to convert the document.");
                }

                string produced = Path.Combine(outDir, Path.GetFileNameWithoutExtension(src) + "." + dstExt);
                if (!File.Exists(produced))
                {
                    throw new InvalidOperationException($"LibreOffice couldn't convert this file. {(await errors).Trim()} {(await output).Trim()}".Trim());
                }
                File.Move(produced, dst, overwrite: true);
            }
            finally
            {
                try { Directory.Delete(outDir, true); } catch { }
            }
        }

        private static readonly System.Text.RegularExpressions.Regex ImgSrc =
            new(@"(<img\b[^>]*?\bsrc\s*=\s*"")(?<src>[^""]+)("")", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Pictures on this PC (relative to <paramref name="baseDir"/>, or file: addresses) as data: addresses.</summary>
        private static string EmbedLocalPictures(string html, string baseDir) => ImgSrc.Replace(html, m =>
        {
            string src = m.Groups["src"].Value;
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return m.Value;
            try
            {
                string path = Uri.TryCreate(src, UriKind.Absolute, out var uri)
                    ? uri.IsFile ? uri.LocalPath : ""
                    : Path.GetFullPath(Path.Combine(baseDir, Uri.UnescapeDataString(src.Split('?', '#')[0])));
                if (path.Length == 0 || !File.Exists(path) || new FileInfo(path).Length > 20 * 1024 * 1024) return m.Value;
                string mime = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".bmp" => "image/bmp",
                    ".webp" => "image/webp",
                    ".svg" => "image/svg+xml",
                    _ => ""
                };
                if (mime.Length == 0) return m.Value;
                return m.Groups[1].Value + $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}" + m.Groups[3].Value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return m.Value;
            }
        });

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
