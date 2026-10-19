using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OgConverter.Models;

namespace OgConverter.Services
{
    public class ConversionEngine
    {
        private string? _ffmpegPath;

        public ConversionEngine()
        {
            _ffmpegPath = FindFFmpeg();
        }

        /// <summary>Wheel id of the Compress tool, for every file kind.</summary>
        public const string CompressToolId = "tool_compress";

        public bool IsFFmpegAvailable => !string.IsNullOrEmpty(_ffmpegPath) && File.Exists(_ffmpegPath);
        public string? FFmpegPath => _ffmpegPath;

        public static string? FindFFmpeg()
        {
            // 1. Check local app directory
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string localExe = Path.Combine(appDir, "ffmpeg.exe");
            if (File.Exists(localExe)) return localExe;

            // 2. Check PATH environment variable
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string dir in pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { }
                }
            }

            // 3. Check common Windows winget / scoop / chocolatey / local appdata locations
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] commonPaths =
            {
                Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe"),
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe"
            };

            foreach (var p in commonPaths)
            {
                if (File.Exists(p)) return p;
            }

            return null;
        }

        public async Task<ConversionJob> ExecuteAsync(string sourcePath, WheelItem item, AppSettings settings, ToolParameters? parameters = null,
            IProgress<double>? progress = null)
        {
            var job = new ConversionJob
            {
                SourcePath = sourcePath,
                Option = item,
                Parameters = parameters,
                StartTime = DateTime.Now
            };

            bool isDirectory = Directory.Exists(sourcePath);

            // Folders are only valid for system actions (terminal, reveal, copy path)
            if (!File.Exists(sourcePath) && !(isDirectory && item.Mode == WheelMode.Actions))
            {
                job.IsSuccess = false;
                job.ErrorMessage = isDirectory
                    ? "Folders are only supported by the Actions wheel."
                    : "Source file does not exist.";
                job.EndTime = DateTime.Now;
                return job;
            }

            job.OriginalSizeBytes = isDirectory ? 0 : new FileInfo(sourcePath).Length;

            try
            {
                switch (item.Mode)
                {
                    case WheelMode.Convert:
                        await HandleConversionAsync(job, settings, progress);
                        break;
                    case WheelMode.Tools:
                        await HandleToolAsync(job, settings, progress);
                        break;
                    case WheelMode.Actions:
                        await HandleActionAsync(job, settings);
                        break;
                }
            }
            catch (Exception ex)
            {
                job.IsSuccess = false;
                job.ErrorMessage = ex.Message;
            }
            finally
            {
                job.EndTime = DateTime.Now;
                // An existing target only means success if nothing failed (actions start with target = source)
                if (job.ErrorMessage == null && File.Exists(job.TargetPath))
                {
                    // Results written in place (hash, copy path) have no output file to compare sizes with
                    if (!string.Equals(job.TargetPath, job.SourcePath, StringComparison.OrdinalIgnoreCase))
                    {
                        job.ConvertedSizeBytes = new FileInfo(job.TargetPath).Length;
                    }
                    job.IsSuccess = true;
                }
            }

            return job;
        }

        private async Task HandleConversionAsync(ConversionJob job, AppSettings settings, IProgress<double>? progress)
        {
            // Google Drive shortcut: the content is online, so open it there
            if (job.Option.Id == "gdoc_open")
            {
                OpenGoogleShortcut(job.SourcePath);
                job.TargetPath = job.SourcePath;
                job.TargetFormat = "Google";
                job.IsSuccess = true;
                return;
            }

            string ext = job.Option.Extension.ToLowerInvariant();
            string sourceExt = Path.GetExtension(job.SourcePath).ToLowerInvariant().TrimStart('.');
            string targetPath = GetTargetFilePath(job.SourcePath, ext, settings);
            job.TargetPath = targetPath;
            job.TargetFormat = ext.ToUpperInvariant();

            // Archive Extraction
            if (job.Option.Id == "extract")
            {
                string extractDir = Path.Combine(Path.GetDirectoryName(job.SourcePath)!, Path.GetFileNameWithoutExtension(job.SourcePath) + "_extracted");
                Directory.CreateDirectory(extractDir);
                await Task.Run(() => ZipFile.ExtractToDirectory(job.SourcePath, extractDir, true));
                job.TargetPath = extractDir;
                job.IsSuccess = true;
                return;
            }

            // Repack into ZIP
            if (ext == "zip" && sourceExt != "zip")
            {
                await Task.Run(() =>
                {
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    using var archive = ZipFile.Open(targetPath, ZipArchiveMode.Create);
                    archive.CreateEntryFromFile(job.SourcePath, Path.GetFileName(job.SourcePath), CompressionLevel.Optimal);
                });
                job.IsSuccess = true;
                return;
            }

            // Base64 encoding
            if (job.Option.Id == "b64")
            {
                byte[] bytes = await File.ReadAllBytesAsync(job.SourcePath);
                string b64 = Convert.ToBase64String(bytes);
                await File.WriteAllTextAsync(targetPath, b64);
                job.IsSuccess = true;
                return;
            }

            // Hex Dump
            if (job.Option.Id == "hex")
            {
                byte[] bytes = await File.ReadAllBytesAsync(job.SourcePath);
                var sb = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    sb.Append($"{bytes[i]:X2} ");
                    if ((i + 1) % 16 == 0) sb.AppendLine();
                }
                await File.WriteAllTextAsync(targetPath, sb.ToString());
                job.IsSuccess = true;
                return;
            }

            // Presentations → slide images, slide text, video (see PresentationService)
            if (FormatRegistry.DetectCategory(job.SourcePath) == FileCategory.Presentation && ext is "png" or "jpg" or "txt" or "mp4")
            {
                switch (ext)
                {
                    case "mp4":
                        await PresentationService.ToVideoAsync(job.SourcePath, targetPath, IsFFmpegAvailable ? _ffmpegPath : null, progress);
                        break;
                    case "txt":
                        await PresentationService.WithPdfAsync(job.SourcePath, async pdf =>
                        {
                            await File.WriteAllTextAsync(targetPath, PdfToolsService.ExtractText(pdf, "Slide"));
                            return true;
                        });
                        break;
                    default:
                        job.TargetPath = await PresentationService.WithPdfAsync(job.SourcePath, pdf => RenderPdfToImagesAsync(pdf, ext, settings, job.SourcePath, "slide"));
                        break;
                }
                job.IsSuccess = true;
                return;
            }

            // Office documents, spreadsheets, presentations, Markdown, PDF → Word
            if (DocumentEngine.Handles(sourceExt, ext))
            {
                await DocumentEngine.ConvertAsync(job.SourcePath, targetPath, ext);
                job.IsSuccess = true;
                return;
            }

            // Text Extraction (from PDF or text docs)
            if (ext == "txt")
            {
                await Task.Run(() =>
                {
                    string text = sourceExt == "pdf"
                        ? PdfToolsService.ExtractText(job.SourcePath)
                        : File.ReadAllText(job.SourcePath);
                    File.WriteAllText(targetPath, text);
                });
                job.IsSuccess = true;
                return;
            }

            // PDF to images (FFmpeg cannot decode PDF; use the native Windows PDF renderer)
            if (sourceExt == "pdf" && ext is "png" or "jpg" or "jpeg" or "bmp" or "tiff" or "tif" or "webp")
            {
                job.TargetPath = await RenderPdfToImagesAsync(job.SourcePath, ext, settings);
                job.IsSuccess = true;
                return;
            }

            // Image to PDF (FFmpeg has no PDF muxer; embed the image with PDFsharp)
            if (ext == "pdf" && FormatRegistry.DetectCategory(job.SourcePath) == FileCategory.Image)
            {
                await ImageToPdfAsync(job.SourcePath, targetPath);
                job.IsSuccess = true;
                return;
            }

            // FFmpeg conversions (Images, Audio, Video)
            if (IsFFmpegAvailable)
            {
                await RunFFmpegAsync(job.SourcePath, BuildFFmpegArgs(job.SourcePath, targetPath, ext, settings), progress);
                return;
            }

            // Built-in .NET WPF Bitmap fallback for basic images if FFmpeg is not in PATH
            if (TryWpfImageConvert(job.SourcePath, targetPath, ext))
            {
                job.IsSuccess = true;
                return;
            }

            throw new InvalidOperationException($"FFmpeg is required to convert from .{sourceExt} to .{ext}. Please install FFmpeg or place ffmpeg.exe in PATH.");
        }

        private async Task HandleToolAsync(ConversionJob job, AppSettings settings, IProgress<double>? progress)
        {
            string sourceExt = Path.GetExtension(job.SourcePath).ToLowerInvariant().TrimStart('.');
            job.TargetFormat = job.Option.Title;

            // Compress: lossless or lossy, any file kind (choices come from the Compress dialog)
            if (job.Option.Id == CompressToolId)
            {
                job.TargetFormat = "Compressed";
                job.TargetPath = await CompressionService.CompressAsync(job.SourcePath, job.Parameters?.Compression, _ffmpegPath,
                    (ext, suffix) => GetTargetFilePath(job.SourcePath, ext, settings, suffix), progress);
                return;
            }

            // Crop: images and videos (the area comes from the Crop dialog)
            if (job.Option.Id == "tool_crop")
            {
                if (job.Parameters?.Crop is not { } rect) throw new InvalidOperationException("No crop area was chosen.");
                Func<string, string, string> targetFor = (ext, suffix) => GetTargetFilePath(job.SourcePath, ext, settings, suffix);
                string? ffmpeg = IsFFmpegAvailable ? _ffmpegPath : null;

                if (FormatRegistry.DetectCategory(job.SourcePath) == FileCategory.Video)
                {
                    if (ffmpeg == null) throw new InvalidOperationException("FFmpeg is required to crop videos.");
                    job.TargetPath = await MediaEditService.CropVideoAsync(ffmpeg, job.SourcePath, rect, settings.VideoCrf, targetFor, progress);
                }
                else
                {
                    job.TargetPath = await MediaEditService.CropImageAsync(job.SourcePath, rect, ffmpeg, targetFor);
                }
                return;
            }

            // Trim: keep start–end of a video (times come from the Trim dialog)
            if (job.Option.Id == "tool_trim")
            {
                var p = job.Parameters ?? throw new InvalidOperationException("No start and end were chosen.");
                if (!IsFFmpegAvailable) throw new InvalidOperationException("FFmpeg is required to trim videos.");
                job.TargetPath = await MediaEditService.TrimVideoAsync(_ffmpegPath!, job.SourcePath, p.TrimStart, p.TrimEnd, p.TrimFast, settings.VideoCrf,
                    (ext, suffix) => GetTargetFilePath(job.SourcePath, ext, settings, suffix), progress);
                return;
            }

            // Extract Archive
            if (job.Option.Id == "tool_extract")
            {
                string extractDir = Path.Combine(Path.GetDirectoryName(job.SourcePath)!, Path.GetFileNameWithoutExtension(job.SourcePath) + "_extracted");
                Directory.CreateDirectory(extractDir);
                await Task.Run(() => ZipFile.ExtractToDirectory(job.SourcePath, extractDir, true));
                job.TargetPath = extractDir;
                job.IsSuccess = true;
                return;
            }

            // SHA-256
            if (job.Option.Id == "tool_sha256")
            {
                await HashToClipboardAsync(job, progress);
                return;
            }

            // Base64
            if (job.Option.Id == "tool_b64")
            {
                string b64Target = GetTargetFilePath(job.SourcePath, "txt", settings, "_b64");
                job.TargetPath = b64Target;
                byte[] bytes = await File.ReadAllBytesAsync(job.SourcePath);
                await File.WriteAllTextAsync(b64Target, Convert.ToBase64String(bytes));
                job.IsSuccess = true;
                return;
            }

            // Duplicate
            if (job.Option.Id == "tool_duplicate")
            {
                string dupTarget = GetTargetFilePath(job.SourcePath, sourceExt, settings, "_copy");
                File.Copy(job.SourcePath, dupTarget, true);
                job.TargetPath = dupTarget;
                job.IsSuccess = true;
                return;
            }

            // Render PDF pages to images
            if (job.Option.Id.StartsWith("tool_pdf_", StringComparison.Ordinal))
            {
                if (sourceExt != "pdf")
                {
                    throw new InvalidOperationException($"{job.Option.Title} only works on PDF files (got .{sourceExt}).");
                }
                await HandlePdfToolAsync(job, settings);
                job.IsSuccess = true;
                return;
            }

            // Presentation tools
            if (job.Option.Id.StartsWith("tool_slides_", StringComparison.Ordinal))
            {
                job.TargetPath = await HandleSlidesToolAsync(job, settings);
                job.IsSuccess = true;
                return;
            }

            // FFmpeg Tools
            string targetPath = GetTargetFilePath(job.SourcePath, sourceExt, settings, $"_{job.Option.Id}");
            job.TargetPath = targetPath;

            if (!IsFFmpegAvailable)
            {
                throw new InvalidOperationException("FFmpeg is required for advanced file tools.");
            }

            string[] options = job.Option.Id switch
            {
                "tool_resize_50" => new[] { "-vf", "scale=iw*0.5:ih*0.5" },
                "tool_grayscale" => new[] { "-vf", "format=gray" },
                "tool_strip_meta" => new[] { "-map_metadata", "-1", "-c", "copy" },

                "tool_mute" => new[] { "-c:v", "copy", "-an" },
                "tool_720p" => new[] { "-vf", "scale=-2:720", "-c:v", "libx264", "-crf", "23", "-c:a", "copy" },
                "tool_trim_10s" => new[] { "-t", "10", "-c", "copy" },

                "tool_normalize" => new[] { "-af", "loudnorm" },
                "tool_mono" => new[] { "-ac", "1" },

                _ => Array.Empty<string>()
            };

            // Trimming stops at 10 s, so the full duration would leave the bar short of 100%; it's a quick stream copy anyway
            await RunFFmpegAsync(job.SourcePath, new[] { "-i", job.SourcePath }.Concat(options).Append(targetPath),
                job.Option.Id == "tool_trim_10s" ? null : progress);
        }

        private static async Task<string> HandleSlidesToolAsync(ConversionJob job, AppSettings settings)
        {
            string src = job.SourcePath;
            switch (job.Option.Id)
            {
                case "tool_slides_cover":
                {
                    string target = GetTargetFilePath(src, "png", settings, "_cover");
                    await PresentationService.WithPdfAsync(src, async pdf => { await SaveFirstPageAsync(pdf, target); return true; });
                    return target;
                }
                case "tool_slides_handout":
                {
                    string target = GetTargetFilePath(src, "pdf", settings, "_handout");
                    await PresentationService.WithPdfAsync(src, pdf => Task.Run(() => { PresentationService.HandoutFromPdf(pdf, target); return true; }));
                    return target;
                }
                case "tool_slides_notes":
                {
                    string target = GetTargetFilePath(src, "txt", settings, "_notes");
                    await PresentationService.ExtractNotesAsync(src, target);
                    return target;
                }
                case "tool_slides_media":
                {
                    string dir = GetUniquePath(Path.Combine(GetOutputDirectory(src, settings), Path.GetFileNameWithoutExtension(src) + "_media"), settings);
                    await PresentationService.ExtractMediaAsync(src, dir);
                    return dir;
                }
                default:
                    throw new InvalidOperationException($"Unknown presentation tool: {job.Option.Id}");
            }
        }

        private async Task HandlePdfToolAsync(ConversionJob job, AppSettings settings)
        {
            string src = job.SourcePath;

            switch (job.Option.Id)
            {
                case "tool_pdf_cover":
                {
                    string target = GetTargetFilePath(src, "png", settings, "_cover");
                    await SaveFirstPageAsync(src, target);
                    job.TargetPath = target;
                    break;
                }

                case "tool_pdf_split":
                    job.TargetPath = await Task.Run(() => SplitPdf(src, job.Parameters ?? new ToolParameters { SplitMode = PdfSplitMode.EveryPage }, settings));
                    break;

                case "tool_pdf_merge":
                {
                    var inputs = job.Parameters?.MergeInputs ?? new List<string>();
                    if (inputs.Count < 2) throw new InvalidOperationException("Choose at least two PDFs to merge.");
                    var missing = inputs.Find(p => !File.Exists(p));
                    if (missing != null) throw new InvalidOperationException($"File not found: {Path.GetFileName(missing)}");

                    job.TargetPath = GetTargetFilePath(inputs[0], "pdf", settings, "_merged");
                    job.OriginalSizeBytes = inputs.Sum(p => new FileInfo(p).Length);
                    await Task.Run(() => PdfToolsService.Merge(inputs, job.TargetPath));
                    break;
                }

                case "tool_pdf_rotate":
                    job.TargetPath = GetTargetFilePath(src, "pdf", settings, "_rotated");
                    await Task.Run(() => PdfToolsService.RotatePages(src, job.TargetPath, 90));
                    break;

                case "tool_pdf_strip_meta":
                    job.TargetPath = GetTargetFilePath(src, "pdf", settings, "_clean");
                    await Task.Run(() => PdfToolsService.StripMetadata(src, job.TargetPath));
                    break;

                case "tool_pdf_protect":
                    job.TargetPath = await ProtectAsync(job, settings);
                    break;

                case "tool_pdf_unlock":
                    job.TargetPath = await UnlockAsync(job, settings);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown PDF tool: {job.Option.Id}");
            }
        }

        /// <summary>Runs a split/extract according to the dialog choices. Returns the first written file.</summary>
        private static string SplitPdf(string src, ToolParameters p, AppSettings settings)
        {
            int pageCount = PdfToolsService.GetPageCount(src);
            if (pageCount == 0) throw new InvalidOperationException("The PDF has no pages.");

            var ranges = p.SplitMode == PdfSplitMode.EveryPage
                ? Enumerable.Range(1, pageCount).Select(n => (Start: n, End: n)).ToList()
                : PdfToolsService.ParsePageRanges(p.PageRanges, pageCount);

            if (p.SplitMode == PdfSplitMode.ExtractToOne)
            {
                string target = GetTargetFilePath(src, "pdf", settings, "_pages_" + DescribeRanges(ranges));
                PdfToolsService.ExtractPages(src, ranges.SelectMany(r => Enumerable.Range(r.Start, r.End - r.Start + 1)), target);
                return target;
            }

            var parts = PdfToolsService.SplitRanges(src, ranges, r => GetTargetFilePath(src, "pdf", settings,
                r.Start == r.End ? $"_page_{r.Start:D3}" : $"_pages_{r.Start}-{r.End}"));
            return parts[0];
        }

        /// <summary>"1-3_5_8-12" for use in file names; long selections are shortened.</summary>
        private static string DescribeRanges(List<(int Start, int End)> ranges)
        {
            string text = string.Join("_", ranges.Select(r => r.Start == r.End ? $"{r.Start}" : $"{r.Start}-{r.End}"));
            return text.Length <= 40 ? text : $"{ranges[0].Start}-{ranges[^1].End}_selection";
        }

        private async Task HandleActionAsync(ConversionJob job, AppSettings settings)
        {
            job.TargetPath = job.SourcePath;
            job.TargetFormat = job.Option.Title;

            switch (job.Option.Id)
            {
                case "act_copy_path":
                    RunOnSta(() => Clipboard.SetText(job.SourcePath));
                    job.IsSuccess = true;
                    break;

                case "act_reveal":
                    Process.Start("explorer.exe", $"/select,\"{job.SourcePath}\"");
                    job.IsSuccess = true;
                    break;

                case "act_terminal":
                    string? dir = Directory.Exists(job.SourcePath) ? job.SourcePath : Path.GetDirectoryName(job.SourcePath);
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    OpenTerminalAt(dir);
                    job.TargetPath = dir;
                    job.IsSuccess = true;
                    break;

                case "act_sha256":
                    await HashToClipboardAsync(job, null);
                    break;

                case "act_copy_file":
                    string copyPath = GetTargetFilePath(job.SourcePath, Path.GetExtension(job.SourcePath).TrimStart('.'), settings, "_copy");
                    File.Copy(job.SourcePath, copyPath, true);
                    job.TargetPath = copyPath;
                    job.IsSuccess = true;
                    break;

                case "act_zip":
                {
                    bool isFolder = Directory.Exists(job.SourcePath);
                    string source = job.SourcePath.TrimEnd('\\', '/');
                    // Dummy extension so a folder named "my.project" becomes "my.project.zip", not "my.zip"
                    string zipPath = GetTargetFilePath(isFolder ? source + ".folder" : source, "zip", settings);
                    await Task.Run(() =>
                    {
                        if (File.Exists(zipPath)) File.Delete(zipPath);
                        if (isFolder)
                        {
                            ZipFile.CreateFromDirectory(source, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
                        }
                        else
                        {
                            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                            archive.CreateEntryFromFile(source, Path.GetFileName(source), CompressionLevel.Optimal);
                        }
                    });
                    job.TargetPath = zipPath;
                    job.IsSuccess = true;
                    break;
                }

                case "act_protect":
                    job.TargetPath = await ProtectAsync(job, settings);
                    job.IsSuccess = true;
                    break;

                case "act_unlock":
                    job.TargetPath = await UnlockAsync(job, settings);
                    job.IsSuccess = true;
                    break;

                case "act_base64":
                {
                    string b64Path = GetTargetFilePath(job.SourcePath, "txt", settings, "_base64");
                    string b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(job.SourcePath));
                    await File.WriteAllTextAsync(b64Path, b64);
                    // Copy too, unless it's too large to paste anywhere sensibly
                    if (b64.Length <= 5 * 1024 * 1024) RunOnSta(() => Clipboard.SetText(b64));
                    job.TargetPath = b64Path;
                    job.IsSuccess = true;
                    break;
                }
            }
        }

        /// <summary>Reads the doc id from a Google Drive shortcut (.gdoc/.gsheet/.gslides JSON) and opens it in the browser.</summary>
        private static void OpenGoogleShortcut(string path)
        {
            string? url = null;
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                var root = json.RootElement;
                if (root.TryGetProperty("url", out var u)) url = u.GetString();
                if (string.IsNullOrEmpty(url) && root.TryGetProperty("doc_id", out var id))
                {
                    string kind = Path.GetExtension(path).ToLowerInvariant() switch
                    {
                        ".gsheet" => "spreadsheets",
                        ".gslides" => "presentation",
                        _ => "document"
                    };
                    url = $"https://docs.google.com/{kind}/d/{id.GetString()}/edit";
                }
            }
            catch
            {
                // Newer Drive versions keep shortcuts as placeholders that can't be read directly
            }

            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This Google file can't be read offline. Open it from Google Drive, then use File → Download.");
            }
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        /// <summary>
        /// Opens a new terminal window in the given directory. Prefers Windows Terminal, falls back to PowerShell.
        /// UseShellExecute is required: the app attaches to its parent console on startup, so a plain
        /// CreateProcess would make PowerShell share that console instead of opening its own window.
        /// </summary>
        private static void OpenTerminalAt(string dir)
        {
            // Launch directly, not via the shell: ShellExecute on wt.exe (a Store app alias) "succeeds" without opening a window
            try
            {
                Process.Start(new ProcessStartInfo("wt.exe")
                {
                    ArgumentList = { "-d", dir },
                    UseShellExecute = false,
                    WorkingDirectory = dir
                });
                return;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Windows Terminal not installed
            }

            // A console app started from a GUI app gets its own new console window
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoExit", "-NoLogo" },
                UseShellExecute = false,
                WorkingDirectory = dir
            });
        }

        /// <summary>
        /// Renders a PDF to images. Single-page PDFs produce "name.ext"; multi-page PDFs produce
        /// "name_page_001.ext", "name_page_002.ext", ... Returns the first output path.
        /// </summary>
        /// <summary>Renders page 1 of the PDF to a PNG at <paramref name="target"/>.</summary>
        private static async Task SaveFirstPageAsync(string pdfPath, string target)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "og-converter-pdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var pages = await PdfRenderService.RenderPagesToPngAsync(pdfPath, (p, _) => Path.Combine(tempDir, $"page_{p:D3}.png"), maxPages: 1);
                if (pages.Count == 0) throw new InvalidOperationException("There is no page to render.");
                File.Copy(pages[0], target, true);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        /// <param name="namingPath">File the outputs are named after (the original deck when <paramref name="pdfPath"/> is a temporary export).</param>
        /// <param name="unit">Word used in the per-page suffix: "name_page_001.png", "name_slide_001.png".</param>
        private async Task<string> RenderPdfToImagesAsync(string pdfPath, string targetExt, AppSettings settings, string? namingPath = null, string unit = "page")
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "og-converter-pdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var pngPages = await PdfRenderService.RenderPagesToPngAsync(pdfPath, (page, _) => Path.Combine(tempDir, $"page_{page:D3}.png"));
                if (pngPages.Count == 0)
                {
                    throw new InvalidOperationException("The PDF has no pages to render.");
                }

                string? first = null;
                for (int i = 0; i < pngPages.Count; i++)
                {
                    string? suffix = pngPages.Count > 1 ? $"_{unit}_{i + 1:D3}" : null;
                    string target = GetTargetFilePath(namingPath ?? pdfPath, targetExt, settings, suffix);

                    if (targetExt == "png")
                    {
                        File.Copy(pngPages[i], target, true);
                    }
                    else if (!TryWpfImageConvert(pngPages[i], target, targetExt))
                    {
                        if (!IsFFmpegAvailable)
                        {
                            throw new InvalidOperationException($"FFmpeg is required to save PDF pages as .{targetExt}.");
                        }
                        await RunFFmpegAsync(pngPages[i], BuildFFmpegArgs(pngPages[i], target, targetExt, settings), null);
                    }

                    first ??= target;
                }

                return first!;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static string[] BuildFFmpegArgs(string input, string output, string targetExt, AppSettings settings)
        {
            string[] codec = targetExt switch
            {
                // Video to GIF high quality palette
                "gif" => new[] { "-vf", "fps=15,scale=480:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse" },

                "webp" => new[] { "-quality", settings.ImageQuality.ToString() },
                "avif" => new[] { "-c:v", "libsvtav1", "-crf", "30" },

                // Audio (also extracts the soundtrack of a video)
                "mp3" => new[] { "-vn", "-c:a", "libmp3lame", "-b:a", "192k" },
                "wav" => new[] { "-vn", "-c:a", "pcm_s16le" },
                "aac" => new[] { "-vn", "-c:a", "aac", "-b:a", "192k" },
                "wma" => new[] { "-vn", "-c:a", "wmav2", "-b:a", "192k" },

                // Video
                "mp4" => new[] { "-vcodec", "libx264", "-crf", settings.VideoCrf.ToString(), "-preset", "fast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k" },
                // FFmpeg's VP9 defaults are single-threaded at the slowest speed (about 3x slower than real time for 1080p).
                // Row multithreading + tiles + cpu-used 4 is ~6x faster at about the same size and quality.
                "webm" => new[] { "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-deadline", "good", "-cpu-used", "4",
                                  "-row-mt", "1", "-tile-columns", "2", "-threads", "0", "-c:a", "libopus", "-b:a", "128k" },
                "avi" => new[] { "-c:v", "mpeg4", "-q:v", "4", "-c:a", "mp3" },
                "wmv" => new[] { "-c:v", "wmv2", "-b:v", "2M", "-c:a", "wmav2", "-b:a", "192k" },

                _ => Array.Empty<string>()
            };

            return new[] { "-i", input }.Concat(codec).Append(output).ToArray();
        }

        /// <summary>
        /// Runs FFmpeg (overwrite is implied). With <paramref name="progress"/>, the input's duration is probed so
        /// FFmpeg's position can be reported as 0–1; inputs without a duration (still images) report nothing.
        /// </summary>
        private async Task RunFFmpegAsync(string input, IEnumerable<string> args, IProgress<double>? progress)
        {
            double duration = 0;
            if (progress != null)
            {
                try { duration = (await MediaToolkit.ProbeAsync(_ffmpegPath!, input)).DurationSeconds; } catch { }
            }
            await MediaToolkit.RunAsync(_ffmpegPath!, args, duration, progress);
        }

        /// <summary>Formats Windows can't decode (e.g. AVIF without the codec extension) go through a temporary PNG made by FFmpeg.</summary>
        private async Task ImageToPdfAsync(string input, string output)
        {
            if (ImageCodec.TryLoad(input, applyOrientation: false, maxDimension: 0) != null || !IsFFmpegAvailable)
            {
                await Task.Run(() => PdfToolsService.ImageToPdf(input, output));
                return;
            }

            string tempPng = Path.Combine(Path.GetTempPath(), $"ogc_{Guid.NewGuid():N}.png");
            try
            {
                await RunFFmpegAsync(input, new[] { "-i", input, "-frames:v", "1", tempPng }, null);
                await Task.Run(() => PdfToolsService.ImageToPdf(tempPng, output));
            }
            finally
            {
                try { File.Delete(tempPng); } catch { }
            }
        }

        private static bool TryWpfImageConvert(string input, string output, string targetExt)
        {
            try
            {
                using var stream = File.OpenRead(input);
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

                if (decoder.Frames.Count == 0) return false;
                var frame = decoder.Frames[0];

                System.Windows.Media.Imaging.BitmapEncoder? encoder = targetExt switch
                {
                    "png" => new System.Windows.Media.Imaging.PngBitmapEncoder(),
                    "jpg" or "jpeg" => new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 90 },
                    "bmp" => new System.Windows.Media.Imaging.BmpBitmapEncoder(),
                    "tiff" or "tif" => new System.Windows.Media.Imaging.TiffBitmapEncoder(),
                    "gif" => new System.Windows.Media.Imaging.GifBitmapEncoder(),
                    _ => null
                };

                if (encoder == null) return false;

                encoder.Frames.Add(frame);
                using var outStream = File.Create(output);
                encoder.Save(outStream);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<string> ProtectAsync(ConversionJob job, AppSettings settings)
        {
            var options = job.Parameters?.Protection ?? throw new InvalidOperationException("Choose a password to protect the file.");
            job.TargetFormat = "Protected";
            return await ProtectionService.ProtectAsync(job.SourcePath, options, (p, ext, suffix) => GetTargetFilePath(p, ext, settings, suffix));
        }

        private static async Task<string> UnlockAsync(ConversionJob job, AppSettings settings)
        {
            job.TargetFormat = "Unlocked";
            return await ProtectionService.UnlockAsync(job.SourcePath, job.Parameters?.Password ?? "",
                (p, ext, suffix) => GetTargetFilePath(p, ext, settings, suffix),
                path => GetUniquePath(path, settings),
                GetOutputDirectory(job.SourcePath, settings));
        }

        private static string GetOutputDirectory(string sourcePath, AppSettings settings)
        {
            string dir = settings.SaveInSameDirectory
                ? (Path.GetDirectoryName(sourcePath.TrimEnd('\\', '/')) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop))
                : (string.IsNullOrWhiteSpace(settings.CustomOutputDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : settings.CustomOutputDirectory);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>"name.ext" → "name_1.ext" (or a folder "name" → "name_1") until nothing exists at that path.</summary>
        private static string GetUniquePath(string path, AppSettings settings)
        {
            if (settings.OverwriteExisting && !Directory.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path)!;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            string candidate = path;
            for (int i = 1; File.Exists(candidate) || Directory.Exists(candidate); i++)
            {
                candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            }
            return candidate;
        }

        private static string GetTargetFilePath(string sourcePath, string targetExt, AppSettings settings, string? suffix = null)
        {
            string dir = GetOutputDirectory(sourcePath, settings);

            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            if (!string.IsNullOrEmpty(suffix))
            {
                baseName += suffix;
            }

            string cleanExt = targetExt.TrimStart('.');
            string candidate = Path.Combine(dir, $"{baseName}.{cleanExt}");

            if (settings.OverwriteExisting) return candidate;

            int counter = 1;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(dir, $"{baseName}_{counter}.{cleanExt}");
                counter++;
            }

            return candidate;
        }


        /// <summary>Computes the SHA-256 of the source, copies it, and records it as the job's visible result.</summary>
        private static async Task HashToClipboardAsync(ConversionJob job, IProgress<double>? progress)
        {
            string hex = await Task.Run(async () =>
            {
                // Shared read so files open in another app (an image viewer, an editor) can still be hashed
                await using var stream = new FileStream(job.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, useAsync: true);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[1 << 20];
                long total = stream.Length, done = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
                return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            });

            RunOnSta(() => Clipboard.SetText(hex));
            job.TargetPath = job.SourcePath;
            job.ResultText = hex;
            job.IsSuccess = true;
        }

        /// <summary>
        /// Runs clipboard work on an STA thread. Uses a short-lived thread rather than the UI dispatcher,
        /// which would deadlock if the UI thread is itself waiting on this job.
        /// </summary>
        private static void RunOnSta(Action action)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                action();
                return;
            }

            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) throw new InvalidOperationException("Couldn't copy to the clipboard: " + error.Message, error);
        }
    }
}
