using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OgConverter.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace OgConverter.Services
{
    /// <summary>
    /// PDF compression in the spirit of Ghostscript's PDFSETTINGS presets, done in-process:
    /// lossy mode downsamples images to a target resolution and re-encodes them as JPEG (optionally grayscale);
    /// both modes re-deflate streams at maximum, drop page thumbnails and unreferenced objects, and can remove metadata.
    /// Images that can't be safely re-encoded (masks, CMYK, indexed, unusual filters) are left exactly as they are.
    /// </summary>
    public static class PdfCompressor
    {
        /// <summary>Returns how many images were re-encoded.</summary>
        public static int Compress(string src, string dst, CompressionOptions o)
        {
            using var doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);
            int recompressed = 0;

            // Page thumbnails are embedded preview images that readers regenerate on their own
            foreach (PdfPage page in doc.Pages)
            {
                page.Elements.Remove("/Thumb");
                page.Elements.Remove("/PieceInfo"); // private application data (e.g. Illustrator's editing copy)
            }

            var objects = doc.Internals.GetAllObjects();

            if (o.Mode == CompressionMode.Lossy && doc.PageCount > 0)
            {
                // An image never needs more pixels than the page at the target resolution
                double maxPagePoints = doc.Pages.Cast<PdfPage>().Max(p => Math.Max(p.Width.Point, p.Height.Point));
                int maxPixels = Math.Max(16, (int)Math.Ceiling(maxPagePoints / 72.0 * o.PdfDpi));

                // Soft masks are alpha channels: JPEG artifacts there show up as halos, so leave them lossless
                var masks = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
                foreach (var obj in objects)
                {
                    if (obj is PdfDictionary d && IsImage(d))
                    {
                        if (Resolve(d.Elements["/SMask"]) is PdfDictionary sm) masks.Add(sm);
                        if (Resolve(d.Elements["/Mask"]) is PdfDictionary m) masks.Add(m);
                    }
                }

                foreach (var obj in objects)
                {
                    if (obj is PdfDictionary d && IsImage(d) && !masks.Contains(d))
                    {
                        try
                        {
                            if (TryRecompressImage(d, maxPixels, o.Quality, o.Grayscale)) recompressed++;
                        }
                        catch
                        {
                            // Undecodable image: keep the original bytes
                        }
                    }
                }
            }

            // Uncompressed streams (fonts, old content) → Flate. Zip() is a no-op for already-filtered streams.
            foreach (var obj in objects)
            {
                if (obj is PdfDictionary d && d.Stream != null && d.Stream.Length > 256 && d.Elements["/Filter"] == null)
                {
                    string type = d.Elements.GetName("/Type");
                    if (type is "/XRef" or "/ObjStm" or "/Metadata") continue;
                    d.Stream.Zip();
                }
            }

            if (o.StripMetadata)
            {
                doc.Info.Elements.Clear();
                doc.Internals.Catalog.Elements.Remove("/Metadata");
            }

            doc.Options.NoCompression = false;
            doc.Options.CompressContentStreams = true;
            doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            doc.Options.EnableCcittCompressionForBilevelImages = true;
            doc.Save(dst);
            return recompressed;
        }

        private static bool IsImage(PdfDictionary d) => d.Stream != null && d.Elements.GetName("/Subtype") == "/Image";

        private static PdfItem? Resolve(PdfItem? item) => item is PdfReference r ? r.Value : item;

        private static bool TryRecompressImage(PdfDictionary d, int maxPixels, int quality, bool grayscale)
        {
            // Only plain 8-bit RGB / gray images with a single Flate or DCT filter (or none)
            string? filter = Resolve(d.Elements["/Filter"]) switch
            {
                null => "",
                PdfName n => n.Value,
                PdfArray { Elements.Count: 1 } a when Resolve(a.Elements[0]) is PdfName n1 => n1.Value,
                _ => null
            };
            if (filter is not ("" or "/FlateDecode" or "/DCTDecode")) return false;
            if (d.Elements.GetBoolean("/ImageMask")) return false;
            if (d.Elements.ContainsKey("/Decode")) return false;
            if (Resolve(d.Elements["/Mask"]) is PdfArray) return false;   // color-key mask depends on exact sample values
            if (d.Elements.GetInteger("/BitsPerComponent") != 8) return false;

            int w = d.Elements.GetInteger("/Width");
            int h = d.Elements.GetInteger("/Height");
            if (w < 32 || h < 32) return false;

            int components = ColorComponents(Resolve(d.Elements["/ColorSpace"]));
            if (components is not (1 or 3)) return false;

            byte[] encoded = d.Stream.Value;
            if (encoded.Length < 8 * 1024) return false;

            RasterImage bmp;
            if (filter == "/DCTDecode")
            {
                var frame = Platform.Images.Decode(encoded);
                if (frame.Format == RasterFormat.Cmyk32) return false;
                if (frame.Width != w || frame.Height != h) return false;
                bmp = Platform.Images.ConvertFormat(frame, components == 1 ? RasterFormat.Gray8 : RasterFormat.Bgr24);
            }
            else
            {
                byte[] raw = filter == "" ? encoded : d.Stream.UnfilteredValue;
                if (raw == null || raw.Length != w * h * components) return false; // predictors or padding we don't model
                if (components == 3)
                {
                    // PDF samples are RGB; the image backend wants BGR
                    var bgr = new byte[raw.Length];
                    for (int i = 0; i < raw.Length; i += 3)
                    {
                        bgr[i] = raw[i + 2];
                        bgr[i + 1] = raw[i + 1];
                        bgr[i + 2] = raw[i];
                    }
                    bmp = Platform.Images.FromPixels(w, h, 72, RasterFormat.Bgr24, bgr, w * 3);
                }
                else
                {
                    bmp = Platform.Images.FromPixels(w, h, 72, RasterFormat.Gray8, raw, w);
                }
            }

            bool resized = false;
            if (Math.Max(w, h) > maxPixels)
            {
                double s = (double)maxPixels / Math.Max(w, h);
                int nw = Math.Max(1, (int)Math.Round(w * s));
                int nh = Math.Max(1, (int)Math.Round(h * s));
                bmp = Platform.Images.Resize(bmp, nw, nh);
                resized = true;
            }

            bool toGray = grayscale && components == 3;
            if (toGray) bmp = Platform.Images.ConvertFormat(bmp, RasterFormat.Gray8);

            byte[] jpeg;
            using (var outMs = new MemoryStream())
            {
                Platform.Images.Encode(bmp, outMs, ImageEncoding.Jpeg(quality));
                jpeg = outMs.ToArray();
            }

            // Re-encoding a JPEG without resizing only pays off if it saves a real amount; otherwise keep the original
            double threshold = resized || toGray || filter != "/DCTDecode" ? 1.0 : 0.85;
            if (jpeg.Length >= encoded.Length * threshold) return false;

            d.Stream.Value = jpeg;
            d.Elements.SetName("/Filter", "/DCTDecode");
            d.Elements.Remove("/DecodeParms");
            d.Elements.SetInteger("/Width", bmp.Width);
            d.Elements.SetInteger("/Height", bmp.Height);
            d.Elements.SetInteger("/BitsPerComponent", 8);
            d.Elements.SetInteger("/Length", jpeg.Length);
            if (toGray) d.Elements.SetName("/ColorSpace", "/DeviceGray");
            return true;
        }

        /// <summary>1 or 3 for gray/RGB-like color spaces (incl. ICC profiles); 0 for anything we don't re-encode.</summary>
        private static int ColorComponents(PdfItem? cs)
        {
            switch (cs)
            {
                case PdfName n:
                    return n.Value switch { "/DeviceRGB" or "/CalRGB" => 3, "/DeviceGray" or "/CalGray" => 1, _ => 0 };
                case PdfArray a when a.Elements.Count >= 2 && Resolve(a.Elements[0]) is PdfName kind:
                    if (kind.Value == "/ICCBased" && Resolve(a.Elements[1]) is PdfDictionary icc)
                    {
                        int n = icc.Elements.GetInteger("/N");
                        return n is 1 or 3 ? n : 0;
                    }
                    if (kind.Value is "/CalRGB") return 3;
                    if (kind.Value is "/CalGray") return 1;
                    return 0;
                default:
                    return 0;
            }
        }
    }
}
