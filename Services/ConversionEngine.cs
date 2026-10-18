using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
                        await HandleConversionAsync(job, settings);
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
                    job.ConvertedSizeBytes = new FileInfo(job.TargetPath).Length;
                    job.IsSuccess = true;
                }
            }

            return job;
        }

        private async Task HandleConversionAsync(ConversionJob job, AppSettings settings)
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

            // FFmpeg conversions (Images, Audio, Video)
            if (IsFFmpegAvailable)
            {
                string args = BuildFFmpegArgs(job.SourcePath, targetPath, ext, settings);
                await RunProcessAsync(_ffmpegPath!, args);
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
                using var stream = File.OpenRead(job.SourcePath);
                byte[] hash = await SHA256.Create().ComputeHashAsync(stream);
                string hex = Convert.ToHexString(hash).ToLowerInvariant();
                RunOnSta(() => Clipboard.SetText(hex));
                job.TargetPath = job.SourcePath;
                job.IsSuccess = true;
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

            // FFmpeg Tools
            string targetPath = GetTargetFilePath(job.SourcePath, sourceExt, settings, $"_{job.Option.Id}");
            job.TargetPath = targetPath;

            if (!IsFFmpegAvailable)
            {
                throw new InvalidOperationException("FFmpeg is required for advanced file tools.");
            }

            string args = job.Option.Id switch
            {
                "tool_resize_50" => $"-y -i \"{job.SourcePath}\" -vf \"scale=iw*0.5:ih*0.5\" \"{targetPath}\"",
                "tool_grayscale" => $"-y -i \"{job.SourcePath}\" -vf \"format=gray\" \"{targetPath}\"",
                "tool_strip_meta" => $"-y -i \"{job.SourcePath}\" -map_metadata -1 -c copy \"{targetPath}\"",

                "tool_mute" => $"-y -i \"{job.SourcePath}\" -c:v copy -an \"{targetPath}\"",
                "tool_720p" => $"-y -i \"{job.SourcePath}\" -vf \"scale=-2:720\" -c:v libx264 -crf 23 -c:a copy \"{targetPath}\"",
                "tool_trim_10s" => $"-y -i \"{job.SourcePath}\" -t 10 -c copy \"{targetPath}\"",

                "tool_normalize" => $"-y -i \"{job.SourcePath}\" -af \"loudnorm\" \"{targetPath}\"",
                "tool_mono" => $"-y -i \"{job.SourcePath}\" -ac 1 \"{targetPath}\"",

                _ => $"-y -i \"{job.SourcePath}\" \"{targetPath}\""
            };

            await RunProcessAsync(_ffmpegPath!, args);
        }

        private async Task HandlePdfToolAsync(ConversionJob job, AppSettings settings)
        {
            string src = job.SourcePath;

            switch (job.Option.Id)
            {
                case "tool_pdf_cover":
                {
                    string target = GetTargetFilePath(src, "png", settings, "_cover");
                    string tempDir = Path.Combine(Path.GetTempPath(), "og-converter-pdf-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        var pages = await PdfRenderService.RenderPagesToPngAsync(src, (p, _) => Path.Combine(tempDir, $"page_{p:D3}.png"), maxPages: 1);
                        File.Copy(pages[0], target, true);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
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
                    using (var stream = File.OpenRead(job.SourcePath))
                    {
                        byte[] hash = await SHA256.Create().ComputeHashAsync(stream);
                        string hex = Convert.ToHexString(hash).ToLowerInvariant();
                        RunOnSta(() => Clipboard.SetText(hex));
                    }
                    job.IsSuccess = true;
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
            try
            {
                Process.Start(new ProcessStartInfo("wt.exe")
                {
                    ArgumentList = { "-d", dir },
                    UseShellExecute = true,
                    WorkingDirectory = dir
                });
                return;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Windows Terminal not installed
            }

            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoExit", "-NoLogo" },
                UseShellExecute = true,
                WorkingDirectory = dir
            });
        }

        /// <summary>
        /// Renders a PDF to images. Single-page PDFs produce "name.ext"; multi-page PDFs produce
        /// "name_page_001.ext", "name_page_002.ext", ... Returns the first output path.
        /// </summary>
        private async Task<string> RenderPdfToImagesAsync(string pdfPath, string targetExt, AppSettings settings)
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
                    string? suffix = pngPages.Count > 1 ? $"_page_{i + 1:D3}" : null;
                    string target = GetTargetFilePath(pdfPath, targetExt, settings, suffix);

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
                        await RunProcessAsync(_ffmpegPath!, BuildFFmpegArgs(pngPages[i], target, targetExt, settings));
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

        private static string BuildFFmpegArgs(string input, string output, string targetExt, AppSettings settings)
        {
            // Video to GIF high quality palette
            if (targetExt == "gif")
            {
                return $"-y -i \"{input}\" -vf \"fps=15,scale=480:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" \"{output}\"";
            }

            // Video to Audio extract (MP3)
            if (targetExt == "mp3")
            {
                return $"-y -i \"{input}\" -vn -c:a libmp3lame -b:a 192k \"{output}\"";
            }

            if (targetExt == "wav")
            {
                return $"-y -i \"{input}\" -vn -c:a pcm_s16le \"{output}\"";
            }

            if (targetExt == "aac")
            {
                return $"-y -i \"{input}\" -vn -c:a aac -b:a 192k \"{output}\"";
            }

            if (targetExt == "wma")
            {
                return $"-y -i \"{input}\" -vn -c:a wmav2 -b:a 192k \"{output}\"";
            }

            // Image conversions (WebP, AVIF, PNG, JPG)
            if (targetExt == "webp")
            {
                return $"-y -i \"{input}\" -quality {settings.ImageQuality} \"{output}\"";
            }

            if (targetExt == "avif")
            {
                return $"-y -i \"{input}\" -c:v libsvtav1 -crf 30 \"{output}\"";
            }

            if (targetExt == "mp4")
            {
                return $"-y -i \"{input}\" -vcodec libx264 -crf {settings.VideoCrf} -preset fast -pix_fmt yuv420p -c:a aac -b:a 192k \"{output}\"";
            }

            if (targetExt == "avi")
            {
                return $"-y -i \"{input}\" -c:v mpeg4 -q:v 4 -c:a mp3 \"{output}\"";
            }

            if (targetExt == "wmv")
            {
                return $"-y -i \"{input}\" -c:v wmv2 -b:v 2M -c:a wmav2 -b:a 192k \"{output}\"";
            }

            // General FFmpeg command
            return $"-y -i \"{input}\" \"{output}\"";
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

        private static async Task RunProcessAsync(string exePath, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "-nostdin " + args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
                RedirectStandardInput = false
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Conversion process failed (exit code {process.ExitCode}): {stderr}");
            }
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
