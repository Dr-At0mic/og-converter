using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Tests
{
    /// <summary>Self-test for universal actions, PDF passwords and Office document conversion.</summary>
    internal static class DocumentVerification
    {
        public static async Task RunAsync(ConversionEngine engine, AppSettings settings, string tempDir, string testPdf)
        {
            // Universal actions exist for any extension
            string odd = Path.Combine(tempDir, "data.xyz");
            await File.WriteAllTextAsync(odd, "hello universal actions");
            var actions = FormatRegistry.GetWheelItems(odd, WheelMode.Actions);
            Console.WriteLine($"[17] Actions for .xyz: {string.Join(", ", actions.ConvertAll(a => a.Title))}");
            foreach (string id in new[] { "act_zip", "act_base64" })
            {
                var item = actions.Find(a => a.Id == id) ?? throw new Exception($"{id} missing from the Actions wheel");
                var job = await engine.ExecuteAsync(odd, item, settings);
                Console.WriteLine($"     {item.Title}: Success={job.IsSuccess}, Target={Path.GetFileName(job.TargetPath)} {job.ErrorMessage}");
                if (!job.IsSuccess || job.TargetPath == odd) throw new Exception($"{item.Title} action failed");
            }
            string b64 = await File.ReadAllTextAsync(Path.Combine(tempDir, "data_base64.txt"));
            if (System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64)) != "hello universal actions") throw new Exception("Base64 round-trip failed");

            var folderZip = FormatRegistry.GetWheelItems(tempDir, WheelMode.Actions).Find(a => a.Id == "act_zip")!;
            string sub = Path.Combine(tempDir, "my.project");
            Directory.CreateDirectory(sub);
            await File.WriteAllTextAsync(Path.Combine(sub, "a.txt"), "a");
            var folderJob = await engine.ExecuteAsync(sub, folderZip, settings);
            Console.WriteLine($"     Zip folder: Success={folderJob.IsSuccess}, Target={Path.GetFileName(folderJob.TargetPath)}");
            if (!folderJob.IsSuccess || Path.GetFileName(folderJob.TargetPath) != "my.project.zip") throw new Exception("Folder zip failed");

            // PDF passwords
            var protect = FormatRegistry.GetWheelItems(testPdf, WheelMode.Tools).Find(t => t.Id == "tool_pdf_protect") ?? throw new Exception("Protect missing for unencrypted PDF");
            var protectJob = await engine.ExecuteAsync(testPdf, protect, settings, new ToolParameters { Protection = new ProtectionOptions { Method = ProtectionMethod.PdfPassword, Password = "s3cret!" } });
            Console.WriteLine($"[18] Protect: Success={protectJob.IsSuccess}, Target={Path.GetFileName(protectJob.TargetPath)} {protectJob.ErrorMessage}");
            string locked = protectJob.TargetPath;
            if (!protectJob.IsSuccess || !PdfToolsService.IsEncrypted(locked)) throw new Exception("Protect did not encrypt the PDF");
            if (PdfToolsService.CheckPassword(locked, "wrong")) throw new Exception("Wrong password was accepted");
            if (!PdfToolsService.CheckPassword(locked, "s3cret!")) throw new Exception("Correct password was rejected");

            var unlock = FormatRegistry.GetWheelItems(locked, WheelMode.Tools).Find(t => t.Id == "tool_pdf_unlock") ?? throw new Exception("Unlock missing for encrypted PDF");
            var badUnlock = await engine.ExecuteAsync(locked, unlock, settings, new ToolParameters { Password = "nope" });
            Console.WriteLine($"     Unlock with wrong password rejected: {!badUnlock.IsSuccess} ({badUnlock.ErrorMessage})");
            if (badUnlock.IsSuccess) throw new Exception("Unlock accepted a wrong password");
            var unlockJob = await engine.ExecuteAsync(locked, unlock, settings, new ToolParameters { Password = "s3cret!" });
            Console.WriteLine($"     Unlock: Success={unlockJob.IsSuccess}, Target={Path.GetFileName(unlockJob.TargetPath)} {unlockJob.ErrorMessage}");
            if (!unlockJob.IsSuccess || PdfToolsService.IsEncrypted(unlockJob.TargetPath) || PdfToolsService.GetPageCount(unlockJob.TargetPath) != 2)
                throw new Exception("Unlock did not produce an open PDF");

            // Markdown → HTML is built in (no Office needed)
            string md = Path.Combine(tempDir, "notes.md");
            await File.WriteAllTextAsync(md, "# Release notes\n\nSome **bold** text.\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");
            await ConvertTo(engine, settings, md, "html", mustContain: "<strong>bold</strong>");

            Console.WriteLine($"[19] Document engine: {DocumentEngine.DescribeBackends()}");
            if (!DocumentEngine.HasAnyBackend)
            {
                Console.WriteLine("     (no Office or LibreOffice: skipping Office conversions)");
                return;
            }

            // Word: RTF → DOCX → ODT → PDF, DOCX → TXT, MD → DOCX, PDF → DOCX
            string rtf = Path.Combine(tempDir, "letter.rtf");
            await File.WriteAllTextAsync(rtf, @"{\rtf1\ansi{\fonttbl\f0\fswiss Helvetica;}\f0\pard Quarterly {\b report} body text.\par}");
            string docx = await ConvertTo(engine, settings, rtf, "docx");
            string odt = await ConvertTo(engine, settings, docx, "odt");
            await ConvertTo(engine, settings, odt, "pdf");
            await ConvertTo(engine, settings, docx, "txt", mustContain: "Quarterly report body text");
            await ConvertTo(engine, settings, docx, "doc");
            await ConvertTo(engine, settings, md, "docx");
            if (DocumentEngine.HasWord) await ConvertTo(engine, settings, testPdf, "docx");

            // Regression: background picture in the header + white text must survive Word → HTML, as one file
            if (DocumentEngine.HasWord)
            {
                string bgImage = Path.Combine(tempDir, "page-bg.png");
                VerificationRunner.CreateDummyImage(bgImage);
                string watermarked = Path.Combine(tempDir, "neurons.docx");
                CreateHeaderBackgroundDocx(watermarked, bgImage);
                string html = await ConvertTo(engine, settings, watermarked, "html", mustContain: "background-image: url(\"data:image/");
                string htmlText = await File.ReadAllTextAsync(html);
                bool selfContained = !Directory.Exists(Path.Combine(tempDir, "neurons_files")) && !htmlText.Contains("_files/");
                Console.WriteLine($"     DOCX → HTML keeps header background and is self-contained: {selfContained}");
                if (!selfContained) throw new Exception("HTML output still depends on a side folder");
            }

            // Excel: CSV → XLSX → ODS → CSV, XLSX → PDF
            string csv = Path.Combine(tempDir, "sales.csv");
            await File.WriteAllTextAsync(csv, "Region,Revenue\nNorth,1200\nSouth,980\n");
            string xlsx = await ConvertTo(engine, settings, csv, "xlsx");
            string ods = await ConvertTo(engine, settings, xlsx, "ods");
            string csvBack = Path.Combine(tempDir, "sales_from_ods");
            Directory.CreateDirectory(csvBack);
            var csvSettings = new AppSettings { SaveInSameDirectory = false, CustomOutputDirectory = csvBack };
            await ConvertTo(engine, csvSettings, ods, "csv", mustContain: "South");
            await ConvertTo(engine, settings, xlsx, "pdf");

            // PowerPoint: build a PPTX, then PPTX → ODP → PDF
            if (DocumentEngine.HasPowerPoint)
            {
                string pptx = Path.Combine(tempDir, "deck.pptx");
                CreatePresentation(pptx);
                string odp = await ConvertTo(engine, settings, pptx, "odp");
                await ConvertTo(engine, settings, odp, "pdf");
                await ConvertTo(engine, settings, pptx, "ppt");
            }

            // Wheels show document formats
            Console.WriteLine($"     DOCX wheel: {string.Join(", ", FormatRegistry.GetWheelItems(docx, WheelMode.Convert).ConvertAll(x => x.Title))}");
            Console.WriteLine($"     XLSX wheel: {string.Join(", ", FormatRegistry.GetWheelItems(xlsx, WheelMode.Convert).ConvertAll(x => x.Title))}");
            Console.WriteLine($"     PPTX wheel: {string.Join(", ", FormatRegistry.GetWheelItems("deck.pptx", WheelMode.Convert).ConvertAll(x => x.Title))}");
            Console.WriteLine($"     PDF wheel:  {string.Join(", ", FormatRegistry.GetWheelItems(testPdf, WheelMode.Convert).ConvertAll(x => x.Title))}");
        }

        private static async Task<string> ConvertTo(ConversionEngine engine, AppSettings settings, string src, string ext, string? mustContain = null)
        {
            var item = new WheelItem { Id = ext, Title = ext.ToUpperInvariant(), Extension = ext, Mode = WheelMode.Convert };
            var started = DateTime.Now;
            var job = await engine.ExecuteAsync(src, item, settings);
            Console.WriteLine($"     {Path.GetExtension(src).TrimStart('.').ToUpperInvariant(),-5} → {ext.ToUpperInvariant(),-5} Success={job.IsSuccess} {(DateTime.Now - started).TotalSeconds,5:0.0}s  {Path.GetFileName(job.TargetPath)} {job.ErrorMessage}");
            if (!job.IsSuccess || new FileInfo(job.TargetPath).Length == 0) throw new Exception($"{src} → {ext} failed: {job.ErrorMessage}");
            if (mustContain != null)
            {
                string text = await File.ReadAllTextAsync(job.TargetPath);
                if (!text.Contains(mustContain)) throw new Exception($"{ext} output is missing \"{mustContain}\"");
            }
            return job.TargetPath;
        }

        /// <summary>White text over a full-page picture placed behind the text in the header (like the user's document).</summary>
        private static void CreateHeaderBackgroundDocx(string path, string imagePath)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                dynamic? word = null;
                try
                {
                    word = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
                    word.Visible = false;
                    word.DisplayAlerts = 0;
                    dynamic doc = word.Documents.Add();
                    doc.Content.Text = "Neurons\rIf you are reading this article, neurons in your brain are working.";
                    doc.Content.Font.Color = 16777215; // white
                    dynamic shape = doc.Sections(1).Headers(1).Shapes.AddPicture(imagePath, false, true, 0, 0, 595, 842);
                    shape.WrapFormat.Type = 5;         // wdWrapBehind
                    shape.RelativeHorizontalPosition = 1;
                    shape.RelativeVerticalPosition = 1;
                    doc.SaveAs2(path, 16);
                    doc.Close(0);
                }
                catch (Exception ex) { error = ex; }
                finally
                {
                    try { word?.Quit(0); } catch { }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) throw new Exception("Couldn't create the watermark test document: " + error.Message, error);
        }

        /// <summary>Creates a two-slide deck with PowerPoint so the presentation path can be tested.</summary>
        private static void CreatePresentation(string path)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                bool wasRunning = System.Diagnostics.Process.GetProcessesByName("POWERPNT").Length > 0;
                dynamic? app = null;
                try
                {
                    app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
                    dynamic pres = app.Presentations.Add(0); // msoFalse: no window
                    dynamic slide = pres.Slides.Add(1, 1);   // ppLayoutTitle
                    slide.Shapes[1].TextFrame.TextRange.Text = "OG Converter test deck";
                    pres.Slides.Add(2, 2);                   // ppLayoutText
                    pres.SaveAs(path, 24);
                    pres.Close();
                }
                catch (Exception ex) { error = ex; }
                finally
                {
                    try { if (app is not null && !wasRunning) { dynamic a = app; if ((int)a.Presentations.Count == 0) a.Quit(); } } catch { }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) throw new Exception("Couldn't create a test presentation: " + error.Message, error);
        }
    }
}
