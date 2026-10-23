using System;
using System.Collections.Generic;
using System.IO;
using OgConverter.Models;

namespace OgConverter.Services
{
    public static class FormatRegistry
    {
        private const string WordGlyph = "\uE8A5";   // Document
        private const string SheetGlyph = "\uE80A";  // Grid
        private const string SlideGlyph = "\uE786";  // Slideshow

        public static FileCategory DetectCategory(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return FileCategory.Other;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            return ext switch
            {
                ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif" or ".tiff" or ".tif" or ".ico" or ".avif" or ".svg" or ".heic" or ".heif" => FileCategory.Image,
                ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" or ".wmv" or ".flv" or ".m4v" or ".ts" => FileCategory.Video,
                ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" or ".opus" or ".wma" or ".aiff" => FileCategory.Audio,
                ".pdf" or ".docx" or ".doc" or ".odt" or ".rtf" or ".txt" or ".md" or ".markdown" or ".html" or ".htm" or ".mht" or ".mhtml" or ".json" => FileCategory.Document,
                ".xlsx" or ".xls" or ".ods" or ".csv" => FileCategory.Spreadsheet,
                ".pptx" or ".ppt" or ".odp" or ".ppsx" => FileCategory.Presentation,
                ".zip" or ".tar" or ".gz" or ".tgz" or ".rar" or ".7z" or ".bz2" or ".xz" or ".cab" or ".zst" or ".tzst" or ".lz4" or ".lz"
                    or ".br" or ".txz" or ".tbz2" or ".cpio" or ".iso" or ".ar" => FileCategory.Archive,
                _ => FileCategory.Other
            };
        }

        /// <summary>Fluent icon for the kind of file (or folder) at the path.</summary>
        public static string GetCategoryGlyph(string? path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) return "\uE8B7";
            return DetectCategory(path) switch
            {
                FileCategory.Image => "\uE91B",
                FileCategory.Video => "\uE714",
                FileCategory.Audio => "\uE8D6",
                FileCategory.Document => "\uE8A5",
                FileCategory.Spreadsheet => "\uE80A",
                FileCategory.Presentation => "\uE786",
                FileCategory.Archive => "\uE7B8",
                _ => "\uE7C3"
            };
        }

        public static List<WheelItem> GetWheelItems(string? filePath, WheelMode mode)
        {
            // Never guess: with no detected file there is nothing meaningful to offer
            if (string.IsNullOrWhiteSpace(filePath)) return new List<WheelItem>();

            // Folders only support system actions that don't read file contents
            if (Directory.Exists(filePath))
            {
                if (mode != WheelMode.Actions) return new List<WheelItem>();
                var folderItems = GetActionItems(filePath).FindAll(x => x.Id is "act_copy_path" or "act_reveal" or "act_terminal" or "act_zip" or "act_protect");
                folderItems.Add(new WheelItem { Id = ConversionEngine.FolderIconActionId, Title = "Folder Icon", Description = "Give the folder a coloured icon from the library, or your own picture", IconGlyph = "", Mode = WheelMode.Actions });
                return folderItems;
            }

            FileCategory category = DetectCategory(filePath);
            string currentExt = string.IsNullOrWhiteSpace(filePath) ? "" : Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();

            // Google Drive shortcuts (.gdoc/.gsheet/.gslides) hold only a link; the document lives online
            if (currentExt is "GDOC" or "GSHEET" or "GSLIDES")
            {
                return mode switch
                {
                    WheelMode.Convert => new List<WheelItem>
                    {
                        new() { Id = "gdoc_open", Title = "Open in Google", Description = "Google files live online. Open it, use File → Download, then convert the download here.", IconGlyph = "\uE774", Mode = WheelMode.Convert }
                    },
                    WheelMode.Actions => GetActionItems(filePath),
                    _ => new List<WheelItem>()
                };
            }

            return mode switch
            {
                WheelMode.Convert => GetConversionItems(category, currentExt),
                WheelMode.Tools => GetToolItems(category, currentExt, filePath),
                WheelMode.Actions => GetActionItems(filePath),
                _ => new List<WheelItem>()
            };
        }

        private static List<WheelItem> GetConversionItems(FileCategory category, string currentExt)
        {
            var list = new List<WheelItem>();

            switch (category)
            {
                case FileCategory.Image:
                    AddIfDifferent(list, "PNG", "Portable Network Graphics (Lossless)", "png", "\uE91B");
                    AddIfDifferent(list, "WEBP", "Modern Web Format (High Compression)", "webp", "\uE774");
                    AddIfDifferent(list, "JPG", "Standard JPEG Image", "jpg", "\uE91B");
                    AddIfDifferent(list, "AVIF", "Next-Gen AV1 Image Format", "avif", "\uE91B");
                    AddIfDifferent(list, "BMP", "Uncompressed Bitmap", "bmp", "\uE91B");
                    AddIfDifferent(list, "PDF", "Convert image to single-page PDF", "pdf", "\uE8A5");
                    AddIfDifferent(list, "ICO", "Windows Icon format", "ico", "\uE734");
                    AddIfDifferent(list, "TIFF", "High Quality Raster TIFF", "tiff", "\uE91B");
                    break;

                case FileCategory.Video:
                    AddIfDifferent(list, "MP4", "Universal H.264 / AAC Video", "mp4", "\uE714");
                    AddIfDifferent(list, "GIF", "Animated GIF Clip", "gif", "\uE786");
                    AddIfDifferent(list, "WEBM", "VP9 Web-Optimized Video", "webm", "\uE714");
                    AddIfDifferent(list, "AVI", "Audio Video Interleave", "avi", "\uE714");
                    AddIfDifferent(list, "MKV", "Matroska Multimedia Container", "mkv", "\uE714");
                    AddIfDifferent(list, "WMV", "Windows Media Video", "wmv", "\uE714");
                    AddIfDifferent(list, "MP3", "Extract Audio Track", "mp3", "\uE8D6");
                    AddIfDifferent(list, "WAV", "Extract Uncompressed Audio", "wav", "\uE8D6");
                    break;

                case FileCategory.Audio:
                    AddIfDifferent(list, "MP3", "Standard MP3 (Universal)", "mp3", "\uE8D6");
                    AddIfDifferent(list, "WAV", "Lossless WAV Audio", "wav", "\uE8D6");
                    AddIfDifferent(list, "FLAC", "Lossless Free Audio Codec", "flac", "\uE8D6");
                    AddIfDifferent(list, "AAC", "Advanced Audio Coding", "aac", "\uE8D6");
                    AddIfDifferent(list, "OGG", "Ogg Vorbis Audio", "ogg", "\uE8D6");
                    AddIfDifferent(list, "OPUS", "Low-Latency High Efficiency", "opus", "\uE8D6");
                    AddIfDifferent(list, "WMA", "Windows Media Audio", "wma", "\uE8D6");
                    break;

                case FileCategory.Document when currentExt == "PDF":
                    if (DocumentEngine.HasWord)
                    {
                        AddIfDifferent(list, "Word", "Editable Word document (DOCX) via Word's PDF reflow", "docx", WordGlyph);
                    }
                    AddIfDifferent(list, "PNG", "Render every page to PNG", "png", "\uE91B");
                    AddIfDifferent(list, "JPG", "Render every page to JPG", "jpg", "\uE91B");
                    AddIfDifferent(list, "WEBP", "Render every page to WebP", "webp", "\uE774");
                    AddIfDifferent(list, "TIFF", "Render every page to TIFF", "tiff", "\uE91B");
                    AddIfDifferent(list, "TXT", "Extract the text (scanned pages are read with OCR)", "txt", "\uE8D2");
                    break;

                case FileCategory.Document when currentExt is "DOCX" or "DOC" or "ODT" or "RTF":
                    AddIfDifferent(list, "PDF", "Print-ready PDF", "pdf", "\uE8A5");
                    AddIfDifferent(list, "DOCX", "Word document", "docx", WordGlyph);
                    AddIfDifferent(list, "ODT", "OpenDocument Text (LibreOffice, Google Docs)", "odt", WordGlyph);
                    AddIfDifferent(list, "DOC", "Word 97\u20132003 document", "doc", WordGlyph);
                    AddIfDifferent(list, "RTF", "Rich Text Format, opens anywhere", "rtf", WordGlyph);
                    AddIfDifferent(list, "TXT", "Plain text (UTF-8)", "txt", "\uE8D2");
                    AddIfDifferent(list, "HTML", "Web page", "html", "\uE774");
                    break;

                case FileCategory.Document when currentExt is "MD" or "MARKDOWN":
                    AddIfDifferent(list, "HTML", "Rendered web page", "html", "\uE774");
                    AddIfDifferent(list, "PDF", "Rendered, print-ready PDF", "pdf", "\uE8A5");
                    if (BrowserRenderService.IsAvailable) AddIfDifferent(list, "PNG", "The whole rendered page as one picture", "png", "\uE91B");
                    AddIfDifferent(list, "DOCX", "Formatted Word document", "docx", WordGlyph);
                    AddIfDifferent(list, "ODT", "Formatted OpenDocument Text", "odt", WordGlyph);
                    AddIfDifferent(list, "RTF", "Formatted Rich Text, opens anywhere", "rtf", WordGlyph);
                    AddIfDifferent(list, "TXT", "Plain text without Markdown symbols", "txt", "");
                    break;

                case FileCategory.Document when currentExt is "HTML" or "HTM" or "MHT" or "MHTML":
                    if (BrowserRenderService.IsAvailable)
                    {
                        AddIfDifferent(list, "PDF", "Print the page to PDF, the way your browser shows it", "pdf", "\uE8A5");
                        AddIfDifferent(list, "PNG", "The whole page, top to bottom, as one picture", "png", "\uE91B");
                        AddIfDifferent(list, "JPG", "The whole page as one smaller picture", "jpg", "\uE91B");
                    }
                    else if (currentExt is "HTML" or "HTM")
                    {
                        AddIfDifferent(list, "PDF", "Print-ready PDF", "pdf", "\uE8A5");
                    }
                    if (currentExt is "HTML" or "HTM")
                    {
                        AddIfDifferent(list, "DOCX", "Word document", "docx", WordGlyph);
                        AddIfDifferent(list, "ODT", "OpenDocument Text", "odt", WordGlyph);
                        AddIfDifferent(list, "RTF", "Rich Text Format", "rtf", WordGlyph);
                        AddIfDifferent(list, "TXT", "Plain text (UTF-8)", "txt", "\uE8D2");
                    }
                    break;

                case FileCategory.Document when currentExt is "TXT":
                    AddIfDifferent(list, "PDF", "Print-ready PDF", "pdf", "\uE8A5");
                    AddIfDifferent(list, "DOCX", "Word document", "docx", WordGlyph);
                    AddIfDifferent(list, "ODT", "OpenDocument Text", "odt", WordGlyph);
                    AddIfDifferent(list, "RTF", "Rich Text Format", "rtf", WordGlyph);
                    AddIfDifferent(list, "TXT", "Plain text (UTF-8)", "txt", "\uE8D2");
                    break;

                case FileCategory.Document:
                    // JSON and other text data: nothing format-specific; universal actions cover the rest
                    AddIfDifferent(list, "TXT", "Save as plain text", "txt", "\uE8D2");
                    break;

                case FileCategory.Spreadsheet:
                    AddIfDifferent(list, "XLSX", "Excel workbook", "xlsx", SheetGlyph);
                    AddIfDifferent(list, "ODS", "OpenDocument Spreadsheet (LibreOffice, Google Sheets)", "ods", SheetGlyph);
                    AddIfDifferent(list, "CSV", "Comma-separated values, first sheet (UTF-8)", "csv", SheetGlyph);
                    AddIfDifferent(list, "XLS", "Excel 97\u20132003 workbook", "xls", SheetGlyph);
                    AddIfDifferent(list, "PDF", "Every sheet as a print-ready PDF", "pdf", "\uE8A5");
                    break;

                case FileCategory.Presentation:
                    AddIfDifferent(list, "PDF", "Every slide as a print-ready PDF", "pdf", "\uE8A5");
                    AddIfDifferent(list, "PPTX", "PowerPoint presentation", "pptx", SlideGlyph);
                    AddIfDifferent(list, "ODP", "OpenDocument Presentation (LibreOffice, Google Slides)", "odp", SlideGlyph);
                    AddIfDifferent(list, "PPT", "PowerPoint 97\u20132003 presentation", "ppt", SlideGlyph);
                    AddIfDifferent(list, "PPSX", "Slideshow that opens straight into presenting", "ppsx", SlideGlyph);
                    AddIfDifferent(list, "PNG", "Every slide as a PNG image", "png", "\ue91b");
                    AddIfDifferent(list, "JPG", "Every slide as a JPG image", "jpg", "\ue91b");
                    AddIfDifferent(list, "MP4", DocumentEngine.HasPowerPoint
                        ? "Video with transitions and animations (PowerPoint)"
                        : $"Slideshow video, {PresentationService.SecondsPerSlide} s per slide", "mp4", "\ue714");
                    AddIfDifferent(list, "TXT", "All slide text, slide by slide", "txt", "\ue8d2");
                    break;

                case FileCategory.Archive:
                    list.Add(new WheelItem { Id = "extract", Title = "Extract", Description = "Unpack archive contents here", Extension = "dir", IconGlyph = "\uE838", Mode = WheelMode.Convert });
                    AddIfDifferent(list, "ZIP", "Repack into ZIP Archive", "zip", "\uE7B8");
                    AddIfDifferent(list, "TAR", "Repack into TAR Archive", "tar", "\uE7B8");
                    AddIfDifferent(list, "GZ", "Gzip Compression", "gz", "\uE7B8");
                    break;

                default:
                    // Any generic file
                    list.Add(new WheelItem { Id = "zip", Title = "Zip", Description = "Compress to ZIP Archive", Extension = "zip", IconGlyph = "\uE7B8", Mode = WheelMode.Convert });
                    list.Add(new WheelItem { Id = "b64", Title = "Base64", Description = "Encode file to Base64 text", Extension = "txt", IconGlyph = "\uE943", Mode = WheelMode.Convert });
                    list.Add(new WheelItem { Id = "hex", Title = "Hex Dump", Description = "Export binary hex dump", Extension = "hex", IconGlyph = "\uE943", Mode = WheelMode.Convert });
                    break;
            }

            return list;

            void AddIfDifferent(List<WheelItem> items, string title, string desc, string ext, string glyph)
            {
                if (string.Equals(ext, currentExt, StringComparison.OrdinalIgnoreCase) ||
                    (ext == "jpg" && currentExt == "jpeg"))
                {
                    // Can still offer compress or re-encode, or skip
                    return;
                }

                items.Add(new WheelItem
                {
                    Id = ext,
                    Title = title,
                    Description = desc,
                    Extension = ext,
                    IconGlyph = glyph,
                    Mode = WheelMode.Convert
                });
            }
        }

        private static List<WheelItem> GetToolItems(FileCategory category, string currentExt, string? filePath = null)
        {
            var list = new List<WheelItem>();

            switch (category)
            {
                case FileCategory.Image when currentExt is "HEIC" or "HEIF":
                    // Windows can read HEIC photos but not write them: edits go through the Convert wheel (JPG, PNG…)
                    AddOcr(list);
                    break;

                case FileCategory.Image when currentExt == "SVG":
                    // Vector: no pixel grid to cut or resample, and FFmpeg can't read SVG. Convert to PNG for pixel edits.
                    AddCompress(list, filePath);
                    break;

                case FileCategory.Image:
                    AddCompress(list, filePath);
                    list.Add(new WheelItem { Id = "tool_crop", Title = "Crop", Description = "Pick the area to keep on a preview", IconGlyph = "", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_resize_50", Title = "Half Size", Description = "Halve image dimensions", IconGlyph = "\uE740", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_grayscale", Title = "Grayscale", Description = "Remove color saturation", IconGlyph = "\uE790", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_strip_meta", Title = "Remove EXIF", Description = "Remove GPS and camera metadata for privacy", IconGlyph = "\uEA18", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_rotate", Title = "Rotate 90°", Description = "Turn the picture clockwise", IconGlyph = "\uE7AD", Mode = WheelMode.Tools });
                    AddOcr(list);
                    break;

                case FileCategory.Video:
                    AddCompress(list, filePath);
                    list.Add(new WheelItem { Id = "tool_trim", Title = "Trim", Description = "Keep only the part between a start and end time", IconGlyph = "", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_crop", Title = "Crop", Description = "Pick the area of the picture to keep", IconGlyph = "", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_mute", Title = "Mute", Description = "Strip audio track from video", IconGlyph = "\uE74F", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_720p", Title = "720p", Description = "Scale down video to 720p HD", IconGlyph = "\uE7F4", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_trim_10s", Title = "First 10s", Description = "Trim first 10 seconds sample", IconGlyph = "\uE8C6", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_snapshot", Title = "Snapshot", Description = "Save the frame from the middle of the video as a PNG", IconGlyph = "\uE722", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_rotate", Title = "Rotate 90°", Description = "Turn the video clockwise (sound kept)", IconGlyph = "\uE7AD", Mode = WheelMode.Tools });
                    break;

                case FileCategory.Audio:
                    AddCompress(list, filePath);
                    list.Add(new WheelItem { Id = "tool_normalize", Title = "Normalize", Description = "Balance peak audio volume", IconGlyph = "\uE767", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_mono", Title = "Mono", Description = "Mix down stereo channels to single mono", IconGlyph = "\uE7F6", Mode = WheelMode.Tools });
                    break;

                case FileCategory.Document when currentExt == "PDF":
                    list.Add(new WheelItem { Id = "tool_pdf_cover", Title = "Cover Image", Description = "Save the first page as a PNG thumbnail", IconGlyph = "\uE91B", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_pdf_split", Title = "Split / Extract", Description = "Pick page ranges to extract or split", IconGlyph = "\uE8C6", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_pdf_merge", Title = "Merge PDFs", Description = "Combine several PDFs into one", IconGlyph = "\uE710", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_pdf_rotate", Title = "Rotate 90°", Description = "Rotate all pages clockwise", IconGlyph = "\uE7AD", Mode = WheelMode.Tools });
                    AddCompress(list, filePath);
                    list.Add(new WheelItem { Id = "tool_pdf_strip_meta", Title = "Remove Metadata", Description = "Remove author, title and tool metadata", IconGlyph = "\uEA18", Mode = WheelMode.Tools });
                    // Offer whichever password action applies to this file
                    list.Add(filePath != null && PdfToolsService.IsEncrypted(filePath)
                        ? new WheelItem { Id = "tool_pdf_unlock", Title = "Unlock", Description = "Remove the password (you need to know it)", IconGlyph = "\uE785", Mode = WheelMode.Tools }
                        : new WheelItem { Id = "tool_pdf_protect", Title = "Protect", Description = "Add a password (AES-256 encryption)", IconGlyph = "\uE72E", Mode = WheelMode.Tools });
                    break;

                case FileCategory.Presentation:
                    AddCompress(list, filePath);
                    list.Add(new WheelItem { Id = "tool_slides_cover", Title = "Cover Image", Description = "Save the first slide as a PNG thumbnail", IconGlyph = "", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_slides_handout", Title = "Handouts", Description = "PDF with 6 slides per A4 page, for printing", IconGlyph = "", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_slides_notes", Title = "Speaker Notes", Description = "Export every slide's notes to a text file", IconGlyph = "", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_slides_media", Title = "Extract Media", Description = "Save embedded pictures, audio and video to a folder", IconGlyph = "", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_sha256", Title = "SHA-256", Description = "Compute cryptographic checksum", IconGlyph = "", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_duplicate", Title = "Duplicate", Description = "Create instant duplicate copy", IconGlyph = "", Mode = WheelMode.Tools });
                    break;

                case FileCategory.Document:
                case FileCategory.Spreadsheet:
                    AddCompress(list, filePath);
                    list.Add(new WheelItem { Id = "tool_b64", Title = "Base64", Description = "Encode file to Base64 string", IconGlyph = "\uE943", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_sha256", Title = "SHA-256", Description = "Compute cryptographic checksum", IconGlyph = "\uE8D7", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_duplicate", Title = "Duplicate", Description = "Create instant duplicate copy", IconGlyph = "\uE8C8", Mode = WheelMode.Tools });
                    break;

                case FileCategory.Archive:
                    list.Add(new WheelItem { Id = "tool_extract", Title = "Extract", Description = "Extract all archive contents", IconGlyph = "\uE838", Mode = WheelMode.Tools });
                    AddCompress(list, filePath);
                    list.Add(new WheelItem { Id = "tool_sha256", Title = "SHA-256", Description = "Compute archive hash checksum", IconGlyph = "\uE8D7", Mode = WheelMode.Tools });
                    break;

                default:
                    AddCompress(list, filePath);
                    list.Add(new WheelItem { Id = "tool_b64", Title = "Base64", Description = "Encode file to Base64 string", IconGlyph = "\uE943", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_sha256", Title = "SHA-256", Description = "Compute cryptographic checksum", IconGlyph = "\uE8D7", Mode = WheelMode.Tools });
                    list.Add(new WheelItem { Id = "tool_duplicate", Title = "Duplicate", Description = "Create instant duplicate copy", IconGlyph = "\uE8C8", Mode = WheelMode.Tools });
                    break;
            }

            return list;
        }

        /// <summary>Extract Text: read the words in a picture (screenshots, photos of documents) with Windows OCR.</summary>
        private static void AddOcr(List<WheelItem> list)
        {
            list.Add(new WheelItem { Id = "tool_ocr", Title = "Extract Text", Description = "Read the text in the picture (OCR) and copy it", IconGlyph = "\uE8D2", Mode = WheelMode.Tools });
        }

        /// <summary>The Compress tool, described for this file's kind (nothing for already-compressed files).</summary>
        private static void AddCompress(List<WheelItem> list, string? filePath)
        {
            var kind = filePath != null ? CompressionService.KindOf(filePath) : null;
            if (kind == null) return;
            string description = kind switch
            {
                CompressionKind.Image => "Lossless or lossy: optimized PNG/JPEG, WebP, AVIF, JPEG XL",
                CompressionKind.AnimatedGif => "Fewer colors, lower frame rate, or animated WebP",
                CompressionKind.Svg => "Minify, or gzip to .svgz",
                CompressionKind.Video => "H.264, H.265, AV1 or VP9 with target size and GPU encoding",
                CompressionKind.Audio => "Opus, AAC, MP3, or lossless FLAC / ALAC",
                CompressionKind.Pdf => "Downsample images (screen, ebook, print) or optimize losslessly",
                CompressionKind.Office => "Shrink pictures and re-pack the document",
                CompressionKind.Archive => "Re-pack at maximum compression",
                _ => "ZIP, GZip or Brotli at maximum compression"
            };
            list.Add(new WheelItem { Id = ConversionEngine.CompressToolId, Title = "Compress", Description = description, IconGlyph = "", Mode = WheelMode.Tools });
        }

        private static List<WheelItem> GetActionItems(string? path = null)
        {
            var list = new List<WheelItem>
            {
                new WheelItem { Id = "act_copy_path", Title = "Copy Path", Description = "Copy full file path to clipboard", IconGlyph = "\uE71B", Mode = WheelMode.Actions },
                new WheelItem { Id = "act_reveal", Title = OperatingSystem.IsWindows() ? "Show in Explorer" : "Show in Folder", Description = OperatingSystem.IsWindows() ? "Select the file in File Explorer" : "Select the file in the file manager", IconGlyph = "\uE838", Mode = WheelMode.Actions },
                new WheelItem { Id = "act_terminal", Title = "Terminal", Description = "Open a terminal in this folder", IconGlyph = "\uE756", Mode = WheelMode.Actions },
                new WheelItem { Id = "act_sha256", Title = "SHA-256", Description = "Compute the SHA-256 checksum and copy it", IconGlyph = "\uE8D7", Mode = WheelMode.Actions },
                new WheelItem { Id = "act_copy_file", Title = "Duplicate", Description = "Create an instant copy next to the original", IconGlyph = "\uE8C8", Mode = WheelMode.Actions },
                new WheelItem { Id = "act_zip", Title = "Archive", Description = "Pack into ZIP, 7z, RAR, tar.gz, tar.zst, ISO… with your choice of compression and an optional password", IconGlyph = "\uE7B8", Mode = WheelMode.Actions },
                new WheelItem { Id = "act_base64", Title = "Base64", Description = "Encode as Base64 text (saved and copied)", IconGlyph = "\uE943", Mode = WheelMode.Actions },
                ProtectionItem(path),
            };

            if (path == null || !File.Exists(path)) return list;

            // Extras that only make sense for some files
            if (DetectCategory(path) == FileCategory.Image && !path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new WheelItem { Id = "act_copy_image", Title = "Copy Image", Description = "Copy the picture itself, ready to paste into chats, mails and documents", IconGlyph = "\uE8C8", Mode = WheelMode.Actions });
                list.Add(new WheelItem { Id = "act_wallpaper", Title = "Wallpaper", Description = "Set as the desktop background", IconGlyph = "\uE7F4", Mode = WheelMode.Actions });
            }
            else if (IsTextFile(path))
            {
                list.Add(new WheelItem { Id = "act_copy_text", Title = "Copy Text", Description = "Copy the file's contents to the clipboard", IconGlyph = "\uE8C8", Mode = WheelMode.Actions });
            }
            if (Platform.Desktop.IsBlocked(path))
            {
                list.Add(new WheelItem { Id = "act_unblock", Title = "Unblock", Description = "Downloaded from the internet: remove the Windows block so it opens without warnings", IconGlyph = "\uE785", Mode = WheelMode.Actions });
            }
            return list;
        }

        private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".markdown", ".json", ".csv", ".tsv", ".log", ".xml", ".yml", ".yaml", ".toml", ".ini", ".cfg", ".conf", ".env",
            ".html", ".htm", ".css", ".scss", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".cs", ".py", ".java", ".kt", ".go", ".rs", ".c", ".h",
            ".cpp", ".hpp", ".php", ".rb", ".swift", ".dart", ".sql", ".sh", ".ps1", ".bat", ".cmd", ".gitignore", ".editorconfig", ".srt", ".vtt"
        };

        /// <summary>Plain text small enough to paste: a known text extension, or an unknown one whose start has no binary bytes.</summary>
        public static bool IsTextFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 4 * 1024 * 1024) return false;
                if (TextExtensions.Contains(info.Extension)) return true;
                if (DetectCategory(path) != FileCategory.Other) return false;

                using var fs = info.OpenRead();
                byte[] head = new byte[Math.Min(4096, (int)info.Length)];
                int read = fs.Read(head, 0, head.Length);
                return read > 0 && Array.IndexOf(head, (byte)0, 0, read) < 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>"Unlock" for files that are already password-protected, otherwise "Protect".</summary>
        private static WheelItem ProtectionItem(string? path)
        {
            var kind = path != null ? ProtectionService.DetectLock(path) : ProtectionService.LockKind.None;
            if (kind != ProtectionService.LockKind.None)
            {
                string what = kind switch
                {
                    ProtectionService.LockKind.Vault => "Decrypt and restore the original file",
                    ProtectionService.LockKind.Zip or ProtectionService.LockKind.SevenZipOrRar => "Extract the password-protected archive",
                    _ => "Remove the password (you need to know it)"
                };
                return new WheelItem { Id = "act_unlock", Title = "Unlock", Description = what, IconGlyph = "\uE785", Mode = WheelMode.Actions };
            }
            return new WheelItem { Id = "act_protect", Title = "Protect", Description = "Add a password with strong encryption", IconGlyph = "\uE72E", Mode = WheelMode.Actions };
        }
    }
}
