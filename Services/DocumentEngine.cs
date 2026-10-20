using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
    /// Uses Microsoft Office via COM automation when installed (highest fidelity for Microsoft formats),
    /// otherwise LibreOffice in headless mode. Markdown → HTML is built in (Markdig).
    /// Everything runs locally; nothing is uploaded.
    /// </summary>
    public static class DocumentEngine
    {
        public const string LibreOfficeInstallHint = "Install Microsoft Office, or LibreOffice for free (winget install TheDocumentFoundation.LibreOffice).";

        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

        // Office automation is not safe to run concurrently; one conversion at a time
        private static readonly SemaphoreSlim Gate = new(1, 1);

        private static readonly Lazy<bool> WordInstalled = new(() => ProgIdExists("Word.Application"));
        private static readonly Lazy<bool> ExcelInstalled = new(() => ProgIdExists("Excel.Application"));
        private static readonly Lazy<bool> PowerPointInstalled = new(() => ProgIdExists("PowerPoint.Application"));
        private static Lazy<string?> LibreOffice = new(FindLibreOffice);

        public static bool HasWord => WordInstalled.Value;
        public static bool HasExcel => ExcelInstalled.Value;
        public static bool HasPowerPoint => PowerPointInstalled.Value;
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
                bool besideSource = string.Equals(Path.GetDirectoryName(Path.GetFullPath(dst)), srcDir, StringComparison.OrdinalIgnoreCase);
                string html = MarkdownToHtml(markdown, Path.GetFileNameWithoutExtension(src), dstExt == "html" && besideSource ? null : srcDir);
                if (dstExt == "html")
                {
                    await File.WriteAllTextAsync(dst, html, new UTF8Encoding(false));
                    return;
                }

                string tempHtml = Path.Combine(Path.GetTempPath(), $"og-converter-md-{Guid.NewGuid():N}.html");
                await File.WriteAllTextAsync(tempHtml, html, new UTF8Encoding(false));
                try
                {
                    // A browser lays out code, tables and images like GitHub does; Word's HTML import is rougher
                    if (dstExt == "pdf" && BrowserRenderService.IsAvailable) await BrowserRenderService.PrintAsync(tempHtml, dst);
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
                    await RunStaAsync(() => ConvertWithWord(src, dst, dstExt));
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
                    Action convert = kind switch
                    {
                        DocumentKind.Text => () => ConvertWithWord(src, dst, dstExt),
                        DocumentKind.Spreadsheet => () => ConvertWithExcel(src, dst, dstExt),
                        _ => () => ConvertWithPowerPoint(src, dst, dstExt)
                    };
                    await RunStaAsync(convert);
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

            string html = MarkdownToHtml(await File.ReadAllTextAsync(src), Path.GetFileNameWithoutExtension(src), Path.GetDirectoryName(Path.GetFullPath(src)));
            string tempHtml = Path.Combine(Path.GetTempPath(), $"og-converter-md-{Guid.NewGuid():N}.html");
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
            if (!CanProtectOffice(ext)) throw new InvalidOperationException($"Password-protecting .{ext} files needs Microsoft Office.");

            // Wrong-password attempts must not prompt; "\u0001" can never match a real password
            string open = openPassword ?? "\u0001";

            await Gate.WaitAsync();
            try
            {
                await RunStaAsync(() =>
                {
                    switch (ext)
                    {
                        case "docx" or "doc":
                        {
                            dynamic? word = null, doc = null;
                            try
                            {
                                word = CreateOfficeInstance("Word.Application", "WINWORD");
                                word.Visible = false;
                                word.DisplayAlerts = 0;
                                word.AutomationSecurity = 3;
                                // Read-only: never asks for a "password to edit"; SaveAs still writes the new copy
                                doc = word.Documents.Open(FileName: src, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false,
                                                          PasswordDocument: open, Visible: false, NoEncodingDialog: true);
                                doc.SaveAs2(FileName: dst, FileFormat: ext == "doc" ? 0 : 16, AddToRecentFiles: false,
                                            Password: newPassword, WritePassword: modifyPassword);
                            }
                            catch (COMException ex) { throw new InvalidOperationException(PasswordError("Word", ex, openPassword != null), ex); }
                            finally
                            {
                                try { doc?.Close(SaveChanges: 0); } catch { }
                                try { word?.Quit(SaveChanges: 0); } catch { }
                                Release(doc); Release(word);
                            }
                            break;
                        }

                        case "xlsx" or "xls":
                        {
                            dynamic? excel = null, book = null;
                            try
                            {
                                excel = CreateOfficeInstance("Excel.Application", "EXCEL");
                                excel.Visible = false;
                                excel.DisplayAlerts = false;
                                excel.AutomationSecurity = 3;
                                book = excel.Workbooks.Open(Filename: src, UpdateLinks: 0, ReadOnly: true, Password: open, AddToMru: false);
                                book.SaveAs(Filename: dst, FileFormat: ext == "xls" ? 56 : 51, Password: newPassword, WriteResPassword: modifyPassword);
                            }
                            catch (COMException ex) { throw new InvalidOperationException(PasswordError("Excel", ex, openPassword != null), ex); }
                            finally
                            {
                                try { book?.Close(SaveChanges: false); } catch { }
                                try { excel?.Quit(); } catch { }
                                Release(book); Release(excel);
                            }
                            break;
                        }

                        default: // pptx / ppt
                        {
                            bool wasRunning = Process.GetProcessesByName("POWERPNT").Length > 0;
                            dynamic? app = null, pres = null;
                            try
                            {
                                app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
                                app.DisplayAlerts = 1;
                                app.AutomationSecurity = 3;
                                // PowerPoint takes the password appended to the file name: "file::password"
                                pres = app.Presentations.Open(FileName: src + "::" + open, ReadOnly: 0, Untitled: 0, WithWindow: 0);
                                pres.Password = newPassword;
                                pres.WritePassword = modifyPassword;
                                pres.SaveAs(FileName: dst, FileFormat: ext == "ppt" ? 1 : 24);
                            }
                            catch (COMException ex) { throw new InvalidOperationException(PasswordError("PowerPoint", ex, openPassword != null), ex); }
                            finally
                            {
                                try { pres?.Close(); } catch { }
                                try
                                {
                                    if (app is not null && !wasRunning)
                                    {
                                        dynamic started = app;
                                        if ((int)started.Presentations.Count == 0) started.Quit();
                                    }
                                }
                                catch { }
                                Release(pres); Release(app);
                            }
                            break;
                        }
                    }
                });
            }
            finally
            {
                Gate.Release();
            }

            if (!File.Exists(dst)) throw new InvalidOperationException("Office finished but did not save the file.");
        }

        private static string PasswordError(string app, COMException ex, bool unlocking)
        {
            string msg = ex.Message ?? "";
            if (msg.Contains("password", StringComparison.OrdinalIgnoreCase))
                return unlocking ? "That password is incorrect." : "This file already has a password. Unlock it first to set a new one.";
            return FriendlyOfficeError(app, ex);
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

            string baseTag = resourceDir == null ? "" : $"<base href=\"{new Uri(resourceDir.TrimEnd('\\') + "\\").AbsoluteUri}\">";
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

        // ------------------------------------------------------------------ Microsoft Office (COM)

        private static readonly Lazy<string?> PdfPrinter = new(FindPdfPrinter);

        /// <summary>"Microsoft Print to PDF on Ne01:" (Office's printer naming), or null if that printer isn't installed.</summary>
        private static string? PdfPrinterName => PdfPrinter.Value;

        private static string? FindPdfPrinter()
        {
            try
            {
                using var devices = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Devices");
                // Value format: "winspool,Ne01:"
                if (devices?.GetValue("Microsoft Print to PDF") is string value && value.Split(',') is { Length: >= 2 } parts)
                {
                    return $"Microsoft Print to PDF on {parts[1].Trim()}";
                }
            }
            catch { }
            return null;
        }

        private static bool ProgIdExists(string progId)
        {
            try { return Type.GetTypeFromProgID(progId) != null; }
            catch { return false; }
        }

        private static void ConvertWithWord(string src, string dst, string dstExt)
        {
            // WdSaveFormat values
            int format = dstExt switch
            {
                "docx" => 16,  // wdFormatDocumentDefault
                "doc" => 0,    // wdFormatDocument (97-2003)
                "odt" => 23,   // wdFormatOpenDocumentText
                "rtf" => 6,    // wdFormatRTF
                "pdf" => 17,   // wdFormatPDF
                "txt" => 2,    // wdFormatText (saved as UTF-8 below)
                "html" => 10,  // wdFormatFilteredHTML (clean markup)
                _ => throw new InvalidOperationException($"Word can't save .{dstExt} files.")
            };

            dynamic? word = null;
            dynamic? doc = null;
            Action? restorePdfNotice = null;
            string? htmlWorkDir = null;
            (string Html, string Docx)? packageHtml = null;
            try
            {
                word = CreateOfficeInstance("Word.Application", "WINWORD");
                word.Visible = false;
                word.DisplayAlerts = 0;          // wdAlertsNone
                word.AutomationSecurity = 3;     // msoAutomationSecurityForceDisable: never run macros from converted files
                word.Options.ConfirmConversions = false;

                // Word shows a modal "will now convert your PDF" notice that DisplayAlerts doesn't cover
                if (src.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    restorePdfNotice = SuppressWordPdfNotice((string)word.Version);
                }

                // Linked pictures can only be embedded in a document that has a window (Word itself stays hidden)
                bool webPage = Path.GetExtension(src).ToLowerInvariant() is ".html" or ".htm" or ".mht" or ".mhtml";
                doc = word.Documents.Open(
                    FileName: src,
                    ConfirmConversions: false,
                    ReadOnly: true,
                    AddToRecentFiles: false,
                    PasswordDocument: "\u0001",  // fail fast on password-protected files instead of prompting
                    Visible: webPage,
                    NoEncodingDialog: true);

                // Pictures in web pages and Markdown arrive as links to the files on disk: embed them, or the output
                // shows empty frames on any other PC (and Word stalls saving such a document as RTF)
                if (webPage)
                {
                    EmbedLinkedPictures(doc);
                }

                // Layout uses the printer; an offline default printer makes Word stall for minutes.
                // Switch this hidden instance to the virtual PDF printer without touching the system default.
                if (PdfPrinterName is { } pdfPrinter)
                {
                    try { word.WordBasic.FilePrintSetup(Printer: pdfPrinter, DoNotSetAsSysDefault: 1); } catch { }
                }

                if (dstExt == "txt")
                {
                    doc.SaveAs2(FileName: dst, FileFormat: format, AddToRecentFiles: false, Encoding: 65001);
                }
                else if (dstExt == "html")
                {
                    // Word writes images to a side folder and drops header backgrounds; build in a scratch
                    // folder, then package into one self-contained page
                    htmlWorkDir = Path.Combine(Path.GetTempPath(), "og-converter-html-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(htmlWorkDir);
                    string wordHtml = Path.Combine(htmlWorkDir, "page.html");
                    doc.SaveAs2(FileName: wordHtml, FileFormat: format, AddToRecentFiles: false);

                    string? docx = src.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? src : null;
                    if (docx == null)
                    {
                        docx = Path.Combine(htmlWorkDir, "source.docx");
                        doc.SaveAs2(FileName: docx, FileFormat: 16, AddToRecentFiles: false);
                    }
                    // Word keeps the saved files open until the document closes; package afterwards
                    packageHtml = (wordHtml, docx);
                }
                else
                {
                    doc.SaveAs2(FileName: dst, FileFormat: format, AddToRecentFiles: false);
                }
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException(FriendlyOfficeError("Word", ex), ex);
            }
            finally
            {
                try { doc?.Close(SaveChanges: 0); } catch { }
                try { word?.Quit(SaveChanges: 0); } catch { }
                Release(doc);
                Release(word);
                restorePdfNotice?.Invoke();

                // Failed before anything was saved: nothing to package, so clean up here
                if (packageHtml == null && htmlWorkDir != null)
                {
                    try { Directory.Delete(htmlWorkDir, true); } catch { }
                }
            }

            // Word has closed the document now, so its HTML and images can be read
            try
            {
                if (packageHtml is { } p) WordHtmlPackager.Package(p.Html, p.Docx, dst);
            }
            finally
            {
                if (htmlWorkDir != null)
                {
                    try { Directory.Delete(htmlWorkDir, true); } catch { }
                }
            }
        }

        private static void EmbedLinkedPictures(dynamic doc)
        {
            foreach (dynamic collection in new[] { doc.InlineShapes, doc.Shapes })
            {
                int count;
                try { count = (int)collection.Count; } catch { continue; }
                // Breaking a link can replace the shape, so walk from the end
                for (int i = count; i >= 1; i--)
                {
                    dynamic? shape = null;
                    try
                    {
                        shape = collection.Item(i);
                        dynamic? link = shape.LinkFormat;
                        if (link == null) continue;
                        link.SavePictureWithDocument = true;
                        link.BreakLink();
                    }
                    catch
                    {
                        // Not a linked picture (or one Word can't read): leave it as it is
                    }
                    finally
                    {
                        Release(shape);
                    }
                }
                Release(collection);
            }
        }

        /// <summary>
        /// Temporarily sets Word's DisableConvertPdfWarning option so opening a PDF doesn't block on a dialog.
        /// Returns an action that restores the user's original setting.
        /// </summary>
        private static Action SuppressWordPdfNotice(string officeVersion)
        {
            string keyPath = $@"Software\Microsoft\Office\{officeVersion}\Word\Options";
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath);
                object? original = key.GetValue("DisableConvertPdfWarning");
                key.SetValue("DisableConvertPdfWarning", 1, Microsoft.Win32.RegistryValueKind.DWord);

                return () =>
                {
                    try
                    {
                        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath);
                        if (original == null) k.DeleteValue("DisableConvertPdfWarning", throwOnMissingValue: false);
                        else k.SetValue("DisableConvertPdfWarning", original, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    catch { }
                };
            }
            catch
            {
                return () => { };
            }
        }

        // Process started by the current automation call, so a timeout can end it. Conversions are serialized by Gate.
        private static int _automationPid;

        /// <summary>Starts a new Office instance and remembers its process id (Word and Excel always start a fresh process).</summary>
        private static dynamic CreateOfficeInstance(string progId, string processName)
        {
            var before = new HashSet<int>(Array.ConvertAll(Process.GetProcessesByName(processName), p => p.Id));
            dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID(progId)!)!;
            foreach (var p in Process.GetProcessesByName(processName))
            {
                if (!before.Contains(p.Id)) _automationPid = p.Id;
            }
            return app;
        }

        private static void KillAutomationProcess()
        {
            int pid = Interlocked.Exchange(ref _automationPid, 0);
            if (pid == 0) return;
            try { Process.GetProcessById(pid).Kill(); } catch { }
        }

        private static void ConvertWithExcel(string src, string dst, string dstExt)
        {
            dynamic? excel = null;
            dynamic? book = null;
            try
            {
                excel = CreateOfficeInstance("Excel.Application", "EXCEL");
                excel.Visible = false;
                excel.DisplayAlerts = false;
                excel.AutomationSecurity = 3;
                excel.ScreenUpdating = false;

                book = excel.Workbooks.Open(
                    Filename: src,
                    UpdateLinks: 0,
                    ReadOnly: true,
                    Password: "\u0001",
                    AddToMru: false,
                    Local: true);

                if (dstExt == "pdf")
                {
                    // Same offline-printer stall as Word; Excel's ActivePrinter only affects this instance
                    if (PdfPrinterName is { } pdfPrinter)
                    {
                        try { excel.ActivePrinter = pdfPrinter; } catch { }
                    }
                    book.ExportAsFixedFormat(Type: 0, Filename: dst); // xlTypePDF, all sheets
                }
                else
                {
                    // XlFileFormat values. CSV writes the active (first) sheet only.
                    int format = dstExt switch
                    {
                        "xlsx" => 51,
                        "xls" => 56,
                        "ods" => 60,
                        "csv" => 62, // xlCSVUTF8
                        _ => throw new InvalidOperationException($"Excel can't save .{dstExt} files.")
                    };
                    book.SaveAs(Filename: dst, FileFormat: format, Local: true);
                }
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException(FriendlyOfficeError("Excel", ex), ex);
            }
            finally
            {
                try { book?.Close(SaveChanges: false); } catch { }
                try { excel?.Quit(); } catch { }
                Release(book);
                Release(excel);
            }
        }

        private static void ConvertWithPowerPoint(string src, string dst, string dstExt)
        {
            // PpSaveAsFileType values
            int format = dstExt switch
            {
                "pptx" => 24,
                "ppt" => 1,
                "odp" => 35,
                "ppsx" => 28,  // ppSaveAsOpenXMLShow
                "pdf" => 32,
                _ => throw new InvalidOperationException($"PowerPoint can't save .{dstExt} files.")
            };

            // PowerPoint is single-instance: automation attaches to a copy the user already has open.
            // Only quit it afterwards if we were the ones who started it.
            bool wasRunning = Process.GetProcessesByName("POWERPNT").Length > 0;

            dynamic? app = null;
            dynamic? pres = null;
            try
            {
                app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
                app.DisplayAlerts = 1;           // ppAlertsNone
                app.AutomationSecurity = 3;

                pres = app.Presentations.Open(FileName: src + "::\u0001", ReadOnly: -1, Untitled: 0, WithWindow: 0);
                pres.SaveAs(FileName: dst, FileFormat: format);
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException(FriendlyOfficeError("PowerPoint", ex), ex);
            }
            finally
            {
                try { pres?.Close(); } catch { }
                try
                {
                    if (app is not null && !wasRunning)
                    {
                        dynamic started = app;
                        if ((int)started.Presentations.Count == 0) started.Quit();
                    }
                }
                catch { }
                Release(pres);
                Release(app);
            }
        }

        private static readonly TimeSpan VideoTimeout = TimeSpan.FromMinutes(30);

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
                await RunStaAsync(() => CreateVideoWithPowerPoint(src, dst, secondsPerSlide), VideoTimeout);
            }
            finally
            {
                Gate.Release();
            }
            if (!File.Exists(dst)) throw new InvalidOperationException("PowerPoint finished but did not produce a video.");
        }

        private static void CreateVideoWithPowerPoint(string src, string dst, int secondsPerSlide)
        {
            bool wasRunning = Process.GetProcessesByName("POWERPNT").Length > 0;
            dynamic? app = null;
            dynamic? pres = null;
            try
            {
                app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
                app.DisplayAlerts = 1;
                app.AutomationSecurity = 3;
                pres = app.Presentations.Open(FileName: src + "::\u0001", ReadOnly: -1, Untitled: 0, WithWindow: 0);

                // Encoding runs in the background inside PowerPoint; poll PpMediaTaskStatus until it settles
                pres.CreateVideo(FileName: dst, UseTimingsAndNarrations: true, DefaultSlideDuration: secondsPerSlide,
                                 VertResolution: 1080, FramesPerSecond: 30, Quality: 85);
                while (true)
                {
                    int status = (int)pres.CreateVideoStatus;
                    if (status == 3) break;                                                         // done
                    if (status == 4) throw new InvalidOperationException("PowerPoint couldn't record the video.");  // failed
                    Thread.Sleep(500);
                }
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException(FriendlyOfficeError("PowerPoint", ex), ex);
            }
            finally
            {
                try { pres?.Close(); } catch { }
                try
                {
                    if (app is not null && !wasRunning)
                    {
                        dynamic started = app;
                        if ((int)started.Presentations.Count == 0) started.Quit();
                    }
                }
                catch { }
                Release(pres);
                Release(app);
            }
        }

        private static string FriendlyOfficeError(string app, COMException ex)
        {
            string msg = ex.Message ?? "";
            if (msg.Contains("password", StringComparison.OrdinalIgnoreCase))
                return "This file is password-protected. Open it, remove the password, then try again.";
            if (msg.Contains("corrupt", StringComparison.OrdinalIgnoreCase) || msg.Contains("damaged", StringComparison.OrdinalIgnoreCase))
                return $"{app} reports the file is damaged and can't be opened.";
            return $"{app} couldn't convert this file: {msg.Trim()}";
        }

        private static void Release(object? com)
        {
            if (com != null && Marshal.IsComObject(com))
            {
                try { Marshal.FinalReleaseComObject(com); } catch { }
            }
        }

        /// <summary>Office COM objects must live on an STA thread. Runs the work on a fresh one with a timeout.</summary>
        private static async Task RunStaAsync(Action work, TimeSpan? timeout = null)
        {
            var limit = timeout ?? Timeout;
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _automationPid = 0;
            var thread = new Thread(() =>
            {
                try
                {
                    work();
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    // Make sure the RCWs are released so the Office process can exit
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            })
            {
                IsBackground = true,
                Name = "OG Converter Office automation"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            var finished = await Task.WhenAny(tcs.Task, Task.Delay(limit));
            if (finished != tcs.Task)
            {
                // Don't leave a hidden, stuck Office process behind (only ever the one we started)
                KillAutomationProcess();
                throw new TimeoutException($"The document took longer than {limit.TotalMinutes:0} minutes to convert. Office may be waiting on a dialog (e.g. activation or a repair prompt).");
            }
            _automationPid = 0;
            await tcs.Task;
        }

        // ------------------------------------------------------------------ LibreOffice (headless)

        private static string? FindLibreOffice()
        {
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
                                               "--convert-to", filter, "--outdir", outDir, src })
                {
                    psi.ArgumentList.Add(arg);
                }

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

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
