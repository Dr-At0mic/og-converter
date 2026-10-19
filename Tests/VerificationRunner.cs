using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Tests
{
    public static class VerificationRunner
    {
        public static async Task RunSelfTestAsync()
        {
            Console.WriteLine("=== OG CONVERTER ENGINE VERIFICATION TEST ===");

            string tempDir = Path.Combine(Path.GetTempPath(), "og-converter-test-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            try
            {
                // 1. Create a dummy PNG file
                string testPng = Path.Combine(tempDir, "test_input.png");
                CreateDummyImage(testPng);
                Console.WriteLine($"[1] Created sample image: {testPng} ({new FileInfo(testPng).Length} bytes)");

                // 2. Test FormatRegistry detection
                var cat = FormatRegistry.DetectCategory(testPng);
                Console.WriteLine($"[2] FormatRegistry category detected: {cat}");

                var convertItems = FormatRegistry.GetWheelItems(testPng, WheelMode.Convert);
                Console.WriteLine($"[3] Available conversion formats: {convertItems.Count} (e.g. {convertItems[0].Title}, {convertItems[1].Title})");

                // 3. Test Conversion to WEBP
                var engine = new ConversionEngine();
                Console.WriteLine($"[4] FFmpeg available: {engine.IsFFmpegAvailable} ({engine.FFmpegPath})");

                var webpItem = convertItems.Find(x => x.Extension == "webp") ?? convertItems[0];
                var settings = new AppSettings { SaveInSameDirectory = true };

                var job = await engine.ExecuteAsync(testPng, webpItem, settings);
                Console.WriteLine($"[5] Conversion to {job.TargetFormat}: Success={job.IsSuccess}, Target={job.TargetPath}, Converted Size={job.ConvertedSizeBytes} bytes");

                if (!job.IsSuccess || !File.Exists(job.TargetPath))
                {
                    throw new Exception($"Conversion test failed: {job.ErrorMessage}");
                }

                // 4. Test Conversion to JPG
                var jpgItem = convertItems.Find(x => x.Extension == "jpg");
                if (jpgItem != null)
                {
                    var jobJpg = await engine.ExecuteAsync(testPng, jpgItem, settings);
                    Console.WriteLine($"[6] Conversion to JPG: Success={jobJpg.IsSuccess}, Target={jobJpg.TargetPath}, Converted Size={jobJpg.ConvertedSizeBytes} bytes");
                }

                // 4b. Image to PDF (native, not FFmpeg)
                var imagePdfItem = convertItems.Find(x => x.Extension == "pdf");
                if (imagePdfItem != null)
                {
                    var jobPdf = await engine.ExecuteAsync(testPng, imagePdfItem, settings);
                    Console.WriteLine($"[6b] Conversion to PDF: Success={jobPdf.IsSuccess}, Target={jobPdf.TargetPath}, Converted Size={jobPdf.ConvertedSizeBytes} bytes");
                    if (!jobPdf.IsSuccess || PdfToolsService.GetPageCount(jobPdf.TargetPath) != 1)
                    {
                        throw new Exception($"Image to PDF test failed: {jobPdf.ErrorMessage}");
                    }
                }

                // 4c. Video to WEBM: tuned VP9 speed, with progress
                if (engine.FFmpegPath != null)
                {
                    string clip = Path.Combine(tempDir, "progress_clip.mp4");
                    await MediaToolkit.RunAsync(engine.FFmpegPath, new[] { "-f", "lavfi", "-i", "testsrc2=size=1920x1080:rate=30", "-t", "4", "-c:v", "libx264", "-preset", "ultrafast", clip });
                    var webmItem = FormatRegistry.GetWheelItems(clip, WheelMode.Convert).Find(x => x.Extension == "webm");
                    if (webmItem != null)
                    {
                        double last = 0;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var jobWebm = await engine.ExecuteAsync(clip, webmItem, settings, null, new DocumentVerification.SyncProgress(f => last = f));
                        Console.WriteLine($"[6c] Video to WEBM: Success={jobWebm.IsSuccess}, {sw.Elapsed.TotalSeconds:0.0}s, {jobWebm.ConvertedSizeBytes / 1024} KB, progress reached {last:P0} {jobWebm.ErrorMessage}");
                        if (!jobWebm.IsSuccess || last < 0.9) throw new Exception($"Video to WEBM failed or reported no progress: {jobWebm.ErrorMessage}");
                    }
                }

                // 5. Test Tools (Compress)
                var toolItems = FormatRegistry.GetWheelItems(testPng, WheelMode.Tools);
                if (toolItems.Count > 0)
                {
                    var jobTool = await engine.ExecuteAsync(testPng, toolItems[0], settings);
                    Console.WriteLine($"[7] Tool '{toolItems[0].Title}': Success={jobTool.IsSuccess}, Target={jobTool.TargetPath}");
                }

                // 6. Test Video/Audio Format Registry
                var videoFormats = FormatRegistry.GetWheelItems("video.mp4", WheelMode.Convert);
                Console.WriteLine($"[8] Video Formats available: {videoFormats.Count} ({string.Join(", ", videoFormats.ConvertAll(x => x.Title))})");

                var audioFormats = FormatRegistry.GetWheelItems("audio.wav", WheelMode.Convert);
                Console.WriteLine($"[9] Audio Formats available: {audioFormats.Count} ({string.Join(", ", audioFormats.ConvertAll(x => x.Title))})");

                var actionItems = FormatRegistry.GetWheelItems(testPng, WheelMode.Actions);
                Console.WriteLine($"[10] File Actions available: {actionItems.Count} ({string.Join(", ", actionItems.ConvertAll(x => x.Title))})");

                // 7. Test ZIP packing
                var zipItem = new WheelItem { Id = "zip", Title = "ZIP", Extension = "zip", Mode = WheelMode.Convert };
                var jobZip = await engine.ExecuteAsync(testPng, zipItem, settings);
                Console.WriteLine($"[11] Archive to ZIP: Success={jobZip.IsSuccess}, Target={jobZip.TargetPath}, Converted Size={jobZip.ConvertedSizeBytes} bytes");

                // 8. Test PDF -> images (native Windows PDF renderer)
                string testPdf = Path.Combine(tempDir, "test_doc.pdf");
                CreateDummyPdf(testPdf, pageCount: 2);
                var pdfItems = FormatRegistry.GetWheelItems(testPdf, WheelMode.Convert);
                foreach (string ext in new[] { "png", "jpg" })
                {
                    var pdfItem = pdfItems.Find(x => x.Extension == ext) ?? throw new Exception($"PDF wheel is missing {ext}");
                    var jobPdf = await engine.ExecuteAsync(testPdf, pdfItem, settings);
                    Console.WriteLine($"[12] PDF to {ext.ToUpperInvariant()}: Success={jobPdf.IsSuccess}, Target={jobPdf.TargetPath}, Size={jobPdf.ConvertedSizeBytes} bytes");
                    if (!jobPdf.IsSuccess || !File.Exists(jobPdf.TargetPath) || !File.Exists(jobPdf.TargetPath.Replace("_page_001", "_page_002")))
                    {
                        throw new Exception($"PDF to {ext} failed: {jobPdf.ErrorMessage}");
                    }
                }

                Console.WriteLine($"[13] PDF convert wheel: {string.Join(", ", pdfItems.ConvertAll(x => x.Title))}");
                var pdfTools = FormatRegistry.GetWheelItems(testPdf, WheelMode.Tools);
                Console.WriteLine($"     PDF tools wheel: {string.Join(", ", pdfTools.ConvertAll(x => x.Title))}");
                foreach (var pdfTool in pdfTools.FindAll(t => t.Id is not ("tool_pdf_split" or "tool_pdf_merge" or "tool_pdf_protect" or "tool_pdf_unlock")))
                {
                    var jobPdfTool = await engine.ExecuteAsync(testPdf, pdfTool, settings);
                    Console.WriteLine($"     PDF tool '{pdfTool.Title}': Success={jobPdfTool.IsSuccess}, Target={Path.GetFileName(jobPdfTool.TargetPath)}, Size={jobPdfTool.ConvertedSizeBytes} bytes {jobPdfTool.ErrorMessage}");
                    // The tiny test PDF can't be shrunk; COMPRESS must refuse rather than leave a larger copy
                    bool expectedRefusal = pdfTool.Id == ConversionEngine.CompressToolId && !jobPdfTool.IsSuccess && !File.Exists(Path.Combine(tempDir, "test_doc_compressed.pdf"));
                    if (!jobPdfTool.IsSuccess && !expectedRefusal) throw new Exception($"PDF tool {pdfTool.Title} failed: {jobPdfTool.ErrorMessage}");
                }

                string extracted = PdfToolsService.ExtractText(testPdf);
                if (!extracted.Contains("Hello page 1") || !extracted.Contains("Hello page 2"))
                {
                    throw new Exception($"PDF text extraction returned unexpected text: {extracted}");
                }

                string rotatedPdf = Path.Combine(tempDir, "rotated_check.pdf");
                PdfToolsService.RotatePages(testPdf, rotatedPdf, 90);
                using (var rotated = PdfSharp.Pdf.IO.PdfReader.Open(rotatedPdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                {
                    if (rotated.Pages[0].Rotate != 90) throw new Exception("PDF rotation was not applied");
                }

                // Page range parsing
                var parsed = PdfToolsService.ParsePageRanges("1-3, 5, 8-", 10);
                if (parsed.Count != 3 || parsed[2] != (8, 10)) throw new Exception("Range parsing failed for '1-3, 5, 8-'");
                if (PdfToolsService.ParsePageRanges("-4", 10)[0] != (1, 4)) throw new Exception("Range parsing failed for '-4'");
                foreach (string bad in new[] { "0", "11", "5-3", "abc", "" })
                {
                    try { PdfToolsService.ParsePageRanges(bad, 10); throw new Exception($"Range '{bad}' should be rejected"); }
                    catch (FormatException fe) { Console.WriteLine($"     Range '{bad}' rejected: {fe.Message}"); }
                }

                // Split modes
                var splitItem = pdfTools.Find(t => t.Id == "tool_pdf_split")!;
                var extractJob = await engine.ExecuteAsync(testPdf, splitItem, settings, new ToolParameters { PageRanges = "2, 1", SplitMode = PdfSplitMode.ExtractToOne });
                Console.WriteLine($"[15] Extract '2, 1' to one PDF: Success={extractJob.IsSuccess}, Target={Path.GetFileName(extractJob.TargetPath)} {extractJob.ErrorMessage}");
                if (!extractJob.IsSuccess || PdfToolsService.GetPageCount(extractJob.TargetPath) != 2) throw new Exception("Extract to one PDF failed");
                if (!PdfToolsService.ExtractText(extractJob.TargetPath).TrimStart().Contains("Hello page 2")) throw new Exception("Extracted pages lost their order");

                var eachRangeJob = await engine.ExecuteAsync(testPdf, splitItem, settings, new ToolParameters { PageRanges = "1, 2", SplitMode = PdfSplitMode.EachRange });
                Console.WriteLine($"     Split each range '1, 2': Success={eachRangeJob.IsSuccess}, First={Path.GetFileName(eachRangeJob.TargetPath)} {eachRangeJob.ErrorMessage}");
                if (!eachRangeJob.IsSuccess || PdfToolsService.GetPageCount(eachRangeJob.TargetPath) != 1) throw new Exception("Split each range failed");

                var everyPageJob = await engine.ExecuteAsync(testPdf, splitItem, settings, new ToolParameters { SplitMode = PdfSplitMode.EveryPage });
                Console.WriteLine($"     Split every page: Success={everyPageJob.IsSuccess}, First={Path.GetFileName(everyPageJob.TargetPath)} {everyPageJob.ErrorMessage}");
                if (!everyPageJob.IsSuccess) throw new Exception("Split every page failed");

                var badRangeJob = await engine.ExecuteAsync(testPdf, splitItem, settings, new ToolParameters { PageRanges = "1-9", SplitMode = PdfSplitMode.ExtractToOne });
                Console.WriteLine($"     Out-of-range '1-9' rejected: {!badRangeJob.IsSuccess} ({badRangeJob.ErrorMessage})");
                if (badRangeJob.IsSuccess) throw new Exception("Out-of-range split should fail");

                // Merge
                string secondPdf = Path.Combine(tempDir, "second.pdf");
                CreateDummyPdf(secondPdf, pageCount: 3);
                var mergeItem = pdfTools.Find(t => t.Id == "tool_pdf_merge")!;
                var mergeJob = await engine.ExecuteAsync(testPdf, mergeItem, settings, new ToolParameters { MergeInputs = { testPdf, secondPdf } });
                Console.WriteLine($"[16] Merge 2 + 3 pages: Success={mergeJob.IsSuccess}, Target={Path.GetFileName(mergeJob.TargetPath)} {mergeJob.ErrorMessage}");
                if (!mergeJob.IsSuccess || PdfToolsService.GetPageCount(mergeJob.TargetPath) != 5) throw new Exception("Merge failed");

                var mergeOneJob = await engine.ExecuteAsync(testPdf, mergeItem, settings, new ToolParameters { MergeInputs = { testPdf } });
                if (mergeOneJob.IsSuccess) throw new Exception("Merging a single PDF should be rejected");

                var otherDocItems = FormatRegistry.GetWheelItems("notes.docx", WheelMode.Convert);
                Console.WriteLine($"     DOCX convert wheel (no PDF options): {string.Join(", ", otherDocItems.ConvertAll(x => x.Title))}");
                if (otherDocItems.Exists(x => x.Extension is "png" or "jpg")) throw new Exception("Non-PDF documents must not offer page rendering");

                // 9. Folders are accepted by the Actions wheel but rejected by conversions
                var dirJob = await engine.ExecuteAsync(tempDir, convertItems[0], settings);
                Console.WriteLine($"[14] Folder via Convert wheel rejected: {!dirJob.IsSuccess} ({dirJob.ErrorMessage})");
                if (dirJob.IsSuccess) throw new Exception("Folder conversion should be rejected");

                await DocumentVerification.RunAsync(engine, settings, tempDir, testPdf);
                await ProtectionVerification.RunAsync(engine, settings, tempDir, testPdf);
                await CompressionVerification.RunAsync(engine, settings, tempDir);
                await EditVerification.RunAsync(engine, settings, tempDir, testPng);
                DragDropVerification.Run(tempDir);
                await MarkdownVerification.RunAsync(engine, settings, tempDir, testPng);

                Console.WriteLine("=== ALL VERIFICATION TESTS PASSED SUCCESSFULLY ===");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        internal static void CreateDummyPdf(string filePath, int pageCount)
        {
            // Minimal valid PDF: catalog, page tree, N pages each drawing a filled rectangle
            var objects = new System.Collections.Generic.List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "" // page tree, filled below
            };
            var kids = new System.Text.StringBuilder();
            for (int i = 0; i < pageCount; i++)
            {
                int pageObj = objects.Count + 1;
                int contentObj = pageObj + 1;
                kids.Append($"{pageObj} 0 R ");
                string stream = $"0.2 0.4 {0.5 + i * 0.3:0.0} rg 50 50 200 250 re f BT /F1 18 Tf 0 0 0 rg 50 340 Td (Hello page {i + 1}) Tj ET";
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Contents {contentObj} 0 R " +
                            "/Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>");
                objects.Add($"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream");
            }
            objects[1] = $"<< /Type /Pages /Kids [{kids.ToString().Trim()}] /Count {pageCount} >>";

            var sb = new System.Text.StringBuilder("%PDF-1.4\n");
            var offsets = new System.Collections.Generic.List<int>();
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(sb.Length);
                sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }
            int xref = sb.Length;
            sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (int off in offsets) sb.Append($"{off:D10} 00000 n \n");
            sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            File.WriteAllText(filePath, sb.ToString(), System.Text.Encoding.ASCII);
        }

        internal static void CreateDummyImage(string filePath)
        {
            int width = 120;
            int height = 120;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            // Fill with the brand orange
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * stride) + (x * 4);
                    pixels[idx + 0] = 53;  // B
                    pixels[idx + 1] = 110; // G
                    pixels[idx + 2] = 255; // R
                    pixels[idx + 3] = 255; // A
                }
            }

            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            using var fileStream = new FileStream(filePath, FileMode.Create);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(fileStream);
        }
    }
}
