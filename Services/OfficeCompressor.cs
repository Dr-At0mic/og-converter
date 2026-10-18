using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using OgConverter.Models;
using CompressionMode = OgConverter.Models.CompressionMode;

namespace OgConverter.Services
{
    /// <summary>
    /// Compresses Office Open XML (docx/xlsx/pptx) and OpenDocument (odt/ods/odp) files, which are ZIP packages.
    /// Lossy works like Office's own "Compress Pictures": embedded photos are downsized to a pixel budget and
    /// re-encoded. Both modes re-zip the package at maximum compression and optimize PNGs losslessly.
    /// File names and formats inside the package never change, so every reference stays valid.
    /// </summary>
    public static class OfficeCompressor
    {
        public static async Task<string> CompressAsync(string src, string dst, CompressionOptions o, string? ffmpeg)
        {
            string ext = Path.GetExtension(src).TrimStart('.').ToLowerInvariant();
            bool odf = ext is "odt" or "ods" or "odp";

            try
            {
                using var input = ZipFile.OpenRead(src);
                await using var outFs = File.Create(dst);
                using var output = new ZipArchive(outFs, ZipArchiveMode.Create);

                // OpenDocument requires an uncompressed "mimetype" as the very first entry
                var ordered = odf
                    ? input.Entries.OrderBy(e => e.FullName == "mimetype" ? 0 : 1).ToList()
                    : input.Entries.ToList();

                foreach (var entry in ordered)
                {
                    if (entry.FullName.EndsWith('/'))
                    {
                        output.CreateEntry(entry.FullName);
                        continue;
                    }

                    byte[] data;
                    using (var s = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        await s.CopyToAsync(ms);
                        data = ms.ToArray();
                    }

                    string name = entry.FullName;
                    string entryExt = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
                    if (entryExt is "jpg" or "jpeg" or "png" or "bmp" or "tif" or "tiff")
                    {
                        data = await ShrinkImageAsync(data, entryExt, o, ffmpeg);
                    }
                    else if (o.StripMetadata)
                    {
                        data = StripPersonalInfo(name, data);
                    }

                    bool store = odf && name == "mimetype";
                    var e = output.CreateEntry(name, store ? CompressionLevel.NoCompression : CompressionLevel.SmallestSize);
                    e.LastWriteTime = entry.LastWriteTime;
                    await using var to = e.Open();
                    await to.WriteAsync(data);
                }
            }
            catch (InvalidDataException ex)
            {
                try { File.Delete(dst); } catch { }
                throw new InvalidOperationException("This document is password-protected or isn't a valid Office file. Unlock it first, then compress.", ex);
            }
            return dst;
        }

        /// <summary>Returns smaller bytes for the picture, or the original bytes if nothing helps.</summary>
        private static async Task<byte[]> ShrinkImageAsync(byte[] data, string ext, CompressionOptions o, string? ffmpeg)
        {
            try
            {
                byte[] best = data;
                bool lossy = o.Mode == CompressionMode.Lossy;

                if (ext is "jpg" or "jpeg")
                {
                    if (!lossy)
                    {
                        if (!o.StripMetadata) return data;
                        best = Candidate(best, StripJpeg(data));
                        return best;
                    }

                    var frame = Decode(data);
                    if (frame == null || frame.Format == PixelFormats.Cmyk32) return data;
                    // Office may honor EXIF rotation; re-encoding would drop it, so leave rotated photos alone
                    if (ImageCodec.ReadOrientation(frame.Metadata as BitmapMetadata) != 1) return data;

                    BitmapSource bmp = new FormatConvertedBitmap(frame, frame.Format == PixelFormats.Gray8 ? PixelFormats.Gray8 : PixelFormats.Bgr24, null, 0);
                    bool resized = Downscale(ref bmp, o.MaxDimension);
                    var enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(o.Quality, 1, 100) };
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    byte[] jpeg = Encode(enc);
                    // Without a resize, re-encoding must save a real amount to be worth the generation loss
                    return jpeg.Length < data.Length * (resized ? 1.0 : 0.85) ? jpeg : data;
                }

                if (ext == "png")
                {
                    var frame = Decode(data);
                    if (frame == null) return data;
                    BitmapSource bmp = frame;
                    if (lossy)
                    {
                        bool alpha = frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Pbgra32 ||
                                     frame.Format == PixelFormats.Rgba64 || frame.Format == PixelFormats.Prgba64 || frame.Palette != null;
                        BitmapSource converted = new FormatConvertedBitmap(frame, alpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24, null, 0);
                        if (Downscale(ref converted, o.MaxDimension)) bmp = converted;
                    }
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(bmp));
                    best = Candidate(best, Encode(png));
                    if (!string.IsNullOrEmpty(ffmpeg)) best = Candidate(best, await OptimizePngAsync(ffmpeg, best));
                    return best;
                }

                if (ext is "tif" or "tiff" or "bmp")
                {
                    var frame = Decode(data);
                    if (frame == null) return data;
                    BitmapSource bmp = frame;
                    if (lossy)
                    {
                        BitmapSource converted = new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
                        if (Downscale(ref converted, o.MaxDimension)) bmp = converted;
                    }
                    BitmapEncoder enc = ext == "bmp" ? new BmpBitmapEncoder() : new TiffBitmapEncoder { Compression = TiffCompressOption.Zip };
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    return Candidate(best, Encode(enc));
                }
            }
            catch
            {
                // Unusual picture: keep it untouched
            }
            return data;
        }

        private static byte[] Candidate(byte[] current, byte[]? candidate) =>
            candidate != null && candidate.Length > 0 && candidate.Length < current.Length ? candidate : current;

        private static BitmapFrame? Decode(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data, false);
                return BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            }
            catch
            {
                return null;
            }
        }

        private static bool Downscale(ref BitmapSource bmp, int maxDimension)
        {
            int longest = Math.Max(bmp.PixelWidth, bmp.PixelHeight);
            if (maxDimension <= 0 || longest <= maxDimension) return false;
            double s = (double)maxDimension / longest;
            int w = Math.Max(1, (int)Math.Round(bmp.PixelWidth * s));
            int h = Math.Max(1, (int)Math.Round(bmp.PixelHeight * s));
            bmp = new TransformedBitmap(bmp, new ScaleTransform((double)w / bmp.PixelWidth, (double)h / bmp.PixelHeight));
            return true;
        }

        private static byte[] Encode(BitmapEncoder enc)
        {
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        private static byte[]? StripJpeg(byte[] data)
        {
            string a = Path.Combine(Path.GetTempPath(), $"og-converter-{Guid.NewGuid():N}.jpg");
            string b = a + ".out.jpg";
            try
            {
                File.WriteAllBytes(a, data);
                ImageCodec.StripJpegMetadata(a, b);
                return File.ReadAllBytes(b);
            }
            catch
            {
                return null;
            }
            finally
            {
                try { File.Delete(a); } catch { }
                try { File.Delete(b); } catch { }
            }
        }

        private static async Task<byte[]?> OptimizePngAsync(string ffmpeg, byte[] data)
        {
            string a = Path.Combine(Path.GetTempPath(), $"og-converter-{Guid.NewGuid():N}.png");
            string b = a + ".out.png";
            try
            {
                await File.WriteAllBytesAsync(a, data);
                await MediaToolkit.RunAsync(ffmpeg, new[] { "-i", a, "-frames:v", "1", "-c:v", "png", "-compression_level", "9", "-pred", "mixed", b });
                return await File.ReadAllBytesAsync(b);
            }
            catch
            {
                return null;
            }
            finally
            {
                try { File.Delete(a); } catch { }
                try { File.Delete(b); } catch { }
            }
        }

        /// <summary>Clears author/editor/company fields from the package's property parts.</summary>
        private static byte[] StripPersonalInfo(string name, byte[] data)
        {
            string[] fields = name switch
            {
                "docProps/core.xml" => new[] { "creator", "lastModifiedBy", "lastPrinted" },
                "docProps/app.xml" => new[] { "Company", "Manager" },
                "meta.xml" => new[] { "initial-creator", "creator", "printed-by" },
                _ => Array.Empty<string>()
            };
            if (fields.Length == 0) return data;

            try
            {
                using var ms = new MemoryStream(data, false);
                var doc = XDocument.Load(ms, LoadOptions.PreserveWhitespace);
                var remove = doc.Descendants().Where(e => fields.Contains(e.Name.LocalName)).ToList();
                if (remove.Count == 0) return data;
                remove.ForEach(e => e.Remove());
                using var outMs = new MemoryStream();
                doc.Save(outMs, SaveOptions.DisableFormatting);
                return outMs.ToArray();
            }
            catch
            {
                return data;
            }
        }
    }
}
