using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OgConverter.Services
{
    /// <summary>
    /// Turns Word's "filtered HTML" output into a single self-contained page:
    /// <list type="bullet">
    /// <item>Images from the side "*_files" folder are embedded as data URIs, so the page works when moved or shared.</item>
    /// <item>Background pictures placed in the header (Word's picture watermark, full-page designs) are restored.
    /// Word's HTML export drops headers, which otherwise leaves light text on a blank white page.</item>
    /// </list>
    /// </summary>
    internal static class WordHtmlPackager
    {
        private static readonly Regex AssetRef = new(@"(?<attr>\b(?:src|background))\s*=\s*""(?<path>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <param name="wordHtmlPath">HTML written by Word (its images sit in a "&lt;name&gt;_files" folder next to it).</param>
        /// <param name="docxPath">The same document as .docx, used to recover header backgrounds. Optional.</param>
        /// <param name="targetPath">Where to write the finished single-file HTML.</param>
        public static void Package(string wordHtmlPath, string? docxPath, string targetPath)
        {
            string html = ReadWordHtml(wordHtmlPath);
            string baseDir = Path.GetDirectoryName(wordHtmlPath)!;

            // 1. Embed every local image/background reference
            html = AssetRef.Replace(html, m =>
            {
                string relative = System.Net.WebUtility.HtmlDecode(m.Groups["path"].Value);
                if (relative.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || relative.Contains("://")) return m.Value;

                string file = Path.GetFullPath(Path.Combine(baseDir, Uri.UnescapeDataString(relative).Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(file)) return m.Value;
                return $"{m.Groups["attr"].Value}=\"{ToDataUri(File.ReadAllBytes(file), Path.GetExtension(file))}\"";
            });

            // 2. Restore a header background if Word's export lost it
            bool hasPageBackground = Regex.IsMatch(html, @"<body[^>]*\bbackground\s*=", RegexOptions.IgnoreCase);
            if (!hasPageBackground && docxPath != null && FindHeaderBackground(docxPath) is { } bg)
            {
                string css = "<style>\n" +
                             "  body { background-image: url(\"" + ToDataUri(bg.Bytes, bg.Extension) + "\"); " +
                             "background-size: cover; background-position: center top; background-attachment: fixed; }\n" +
                             "</style>\n";
                int head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                html = head >= 0 ? html.Insert(head, css) : css + html;
            }

            // Readable on phones too
            if (!html.Contains("name=viewport", StringComparison.OrdinalIgnoreCase) && !html.Contains("name=\"viewport\"", StringComparison.OrdinalIgnoreCase))
            {
                int head = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
                if (head >= 0) html = html.Insert(head + 6, "\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            }

            // Saved as UTF-8; make the declared charset agree with that
            html = Regex.Replace(html, @"charset\s*=\s*[""']?[\w-]+", "charset=utf-8", RegexOptions.IgnoreCase);
            File.WriteAllText(targetPath, html, new UTF8Encoding(false));
        }

        /// <summary>Word writes filtered HTML in the system code page (windows-1252 etc.); decode with the charset it declares.</summary>
        private static string ReadWordHtml(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string probe = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            var match = Regex.Match(probe, @"charset\s*=\s*[""']?(?<cs>[\w-]+)", RegexOptions.IgnoreCase);

            Encoding encoding = Encoding.UTF8;
            if (match.Success)
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    encoding = Encoding.GetEncoding(match.Groups["cs"].Value);
                }
                catch { }
            }
            return encoding.GetString(bytes);
        }

        private static string ToDataUri(byte[] bytes, string extension)
        {
            string mime = extension.ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".svg" => "image/svg+xml",
                ".webp" => "image/webp",
                ".tif" or ".tiff" => "image/tiff",
                _ => "image/jpeg"
            };
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }

        /// <summary>
        /// Finds the largest picture that sits behind the text in any header: DrawingML anchors with behindDoc="1",
        /// or VML shapes with a negative z-index (Word's picture watermark).
        /// </summary>
        internal static (byte[] Bytes, string Extension)? FindHeaderBackground(string docxPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(docxPath);
                var documentRels = LoadRelationships(zip, "word/_rels/document.xml.rels");
                var candidates = new List<ZipArchiveEntry>();

                foreach (var header in documentRels.Values.Where(r => r.Type.EndsWith("/header", StringComparison.Ordinal)))
                {
                    string headerPath = "word/" + header.Target.TrimStart('/');
                    var headerEntry = zip.GetEntry(headerPath);
                    if (headerEntry == null) continue;

                    XDocument xml;
                    using (var s = headerEntry.Open()) xml = XDocument.Load(s);
                    var headerRels = LoadRelationships(zip, $"word/_rels/{Path.GetFileName(headerPath)}.rels");

                    foreach (string id in BehindTextImageIds(xml))
                    {
                        if (headerRels.TryGetValue(id, out var rel) &&
                            zip.GetEntry("word/" + rel.Target.Replace("../", "").TrimStart('/')) is { } media)
                        {
                            candidates.Add(media);
                        }
                    }
                }

                var best = candidates.OrderByDescending(e => e.Length).FirstOrDefault();
                if (best == null) return null;

                using var ms = new MemoryStream();
                using (var s = best.Open()) s.CopyTo(ms);
                return (ms.ToArray(), Path.GetExtension(best.Name));
            }
            catch
            {
                return null; // Not a readable OOXML package; nothing to recover
            }
        }

        private static IEnumerable<string> BehindTextImageIds(XDocument xml)
        {
            foreach (var anchor in xml.Descendants().Where(e => e.Name.LocalName == "anchor" && (string?)e.Attribute("behindDoc") == "1"))
            {
                foreach (var blip in anchor.Descendants().Where(e => e.Name.LocalName == "blip"))
                {
                    if (blip.Attributes().FirstOrDefault(a => a.Name.LocalName == "embed")?.Value is { } id) yield return id;
                }
            }

            foreach (var shape in xml.Descendants().Where(e => e.Name.LocalName == "shape"))
            {
                string style = (string?)shape.Attribute("style") ?? "";
                string shapeId = (string?)shape.Attribute("id") ?? "";
                if (!style.Contains("z-index:-", StringComparison.Ordinal) && !shapeId.Contains("Watermark", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var image in shape.Descendants().Where(e => e.Name.LocalName == "imagedata"))
                {
                    if (image.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value is { } id) yield return id;
                }
            }
        }

        private static Dictionary<string, (string Type, string Target)> LoadRelationships(ZipArchive zip, string path)
        {
            var map = new Dictionary<string, (string, string)>();
            var entry = zip.GetEntry(path);
            if (entry == null) return map;

            using var s = entry.Open();
            foreach (var rel in XDocument.Load(s).Descendants().Where(e => e.Name.LocalName == "Relationship"))
            {
                string? id = (string?)rel.Attribute("Id");
                if (id != null) map[id] = ((string?)rel.Attribute("Type") ?? "", (string?)rel.Attribute("Target") ?? "");
            }
            return map;
        }
    }
}
