using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OgConverter.Services;

namespace OgConverter.Platform.Windows
{
    /// <summary>
    /// Microsoft Office via COM automation: Word, Excel and PowerPoint conversions, Office passwords and PowerPoint videos.
    /// Every job runs on its own STA thread with a time limit; <see cref="DocumentEngine"/> runs one job at a time.
    /// </summary>
    public sealed class WindowsOfficeAutomation : IOfficeAutomation
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

        private static readonly Lazy<bool> WordInstalled = new(() => ProgIdExists("Word.Application"));
        private static readonly Lazy<bool> ExcelInstalled = new(() => ProgIdExists("Excel.Application"));
        private static readonly Lazy<bool> PowerPointInstalled = new(() => ProgIdExists("PowerPoint.Application"));

        public bool HasWord => WordInstalled.Value;
        public bool HasExcel => ExcelInstalled.Value;
        public bool HasPowerPoint => PowerPointInstalled.Value;

        public Task ConvertAsync(DocumentKind kind, string src, string dst, string dstExt)
        {
            Action convert = kind switch
            {
                DocumentKind.Text => () => ConvertWithWord(src, dst, dstExt),
                DocumentKind.Spreadsheet => () => ConvertWithExcel(src, dst, dstExt),
                _ => () => ConvertWithPowerPoint(src, dst, dstExt)
            };
            return RunStaAsync(convert);
        }

        public Task RecordVideoAsync(string src, string dst, int secondsPerSlide) =>
            RunStaAsync(() => CreateVideoWithPowerPoint(src, dst, secondsPerSlide), VideoTimeout);

        public Task ChangePasswordAsync(string src, string dst, string? openPassword, string newPassword, string modifyPassword)
        {
            string ext = Path.GetExtension(src).TrimStart('.').ToLowerInvariant();

            // Wrong-password attempts must not prompt; "\u0001" can never match a real password
            string open = openPassword ?? "\u0001";

            return RunStaAsync(() =>
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

        private static string PasswordError(string app, COMException ex, bool unlocking)
        {
            string msg = ex.Message ?? "";
            if (msg.Contains("password", StringComparison.OrdinalIgnoreCase))
                return unlocking ? "That password is incorrect." : "This file already has a password. Unlock it first to set a new one.";
            return FriendlyOfficeError(app, ex);
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
    }
}
