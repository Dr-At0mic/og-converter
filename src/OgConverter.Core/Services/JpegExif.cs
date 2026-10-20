using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OgConverter.Services
{
    /// <summary>
    /// EXIF in JPEG files at byte level: find the APP1 "Exif" block and the ICC profile, read and change tags in its first
    /// directory (IFD0: orientation, camera make, software), build a new block and put it into a JPEG. For image backends
    /// whose encoders don't carry metadata, and for tests.
    /// </summary>
    public static class JpegExif
    {
        public const int OrientationTag = 0x0112;
        public const int MakeTag = 0x010F;
        public const int SoftwareTag = 0x0131;
        public const int ArtistTag = 0x013B;

        private static readonly byte[] ExifHeader = { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 };

        /// <summary>The headers before the image data, as (marker, offset of the marker, segment length incl. marker).</summary>
        private static IEnumerable<(byte Marker, int Offset, int Length)> Segments(byte[] jpeg)
        {
            if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) yield break;
            int pos = 2;
            while (pos + 4 <= jpeg.Length && jpeg[pos] == 0xFF)
            {
                byte marker = jpeg[pos + 1];
                if (marker == 0xFF) { pos++; continue; }
                if (marker == 0xDA || marker == 0xD9) yield break; // image data starts
                int length = 2 + ((jpeg[pos + 2] << 8) | jpeg[pos + 3]);
                if (pos + length > jpeg.Length) yield break;
                yield return (marker, pos, length);
                pos += length;
            }
        }

        private static bool IsExif(byte[] jpeg, int offset, int length) =>
            length >= 4 + ExifHeader.Length && jpeg.AsSpan(offset + 4, ExifHeader.Length).SequenceEqual(ExifHeader);

        /// <summary>The EXIF block's content ("Exif\0\0" + TIFF data), or null when the JPEG has none.</summary>
        public static byte[]? Read(byte[] jpeg)
        {
            foreach (var (marker, offset, length) in Segments(jpeg))
            {
                if (marker == 0xE1 && IsExif(jpeg, offset, length)) return jpeg.AsSpan(offset + 4, length - 4).ToArray();
            }
            return null;
        }

        /// <summary>The ICC color profile blocks (APP2 "ICC_PROFILE"), whole segments, in file order.</summary>
        public static List<byte[]> ReadIccSegments(byte[] jpeg)
        {
            var list = new List<byte[]>();
            foreach (var (marker, offset, length) in Segments(jpeg))
            {
                if (marker == 0xE2 && length > 16 && Encoding.ASCII.GetString(jpeg, offset + 4, 12) == "ICC_PROFILE\0")
                {
                    list.Add(jpeg.AsSpan(offset, length).ToArray());
                }
            }
            return list;
        }

        /// <summary>Orientation 1–8 from an EXIF block; 1 when it has none.</summary>
        public static int ReadOrientation(byte[]? exif) =>
            exif != null && FindEntry(exif, OrientationTag) is { } e && ReadShort(exif, e.ValueOffset, e.BigEndian) is int v and >= 1 and <= 8 ? v : 1;

        /// <summary>A text tag (make, software, artist) from an EXIF block, or null.</summary>
        public static string? ReadText(byte[]? exif, int tag)
        {
            if (exif == null || FindEntry(exif, tag) is not { } e || e.Type != 2) return null;
            int start = e.Count <= 4 ? e.ValueOffset : 6 + ReadInt(exif, e.ValueOffset, e.BigEndian);
            if (start < 0 || start + e.Count > exif.Length) return null;
            return Encoding.ASCII.GetString(exif, start, e.Count).TrimEnd('\0');
        }

        /// <summary>A copy of an EXIF block with its orientation tag set to <paramref name="orientation"/> (unchanged if it has none).</summary>
        public static byte[] WithOrientation(byte[] exif, int orientation)
        {
            var copy = (byte[])exif.Clone();
            if (FindEntry(copy, OrientationTag) is { Type: 3 } e)
            {
                if (e.BigEndian) { copy[e.ValueOffset] = 0; copy[e.ValueOffset + 1] = (byte)orientation; }
                else { copy[e.ValueOffset] = (byte)orientation; copy[e.ValueOffset + 1] = 0; }
            }
            return copy;
        }

        /// <summary>A new EXIF block (big-endian) with an orientation and text tags in IFD0.</summary>
        public static byte[] Build(int orientation, params (int Tag, string Value)[] texts)
        {
            var entries = new List<(int Tag, int Type, int Count, byte[] Data)> { (OrientationTag, 3, 1, new byte[] { 0, (byte)orientation }) };
            foreach (var (tag, value) in texts) entries.Add((tag, 2, value.Length + 1, Encoding.ASCII.GetBytes(value + "\0")));
            entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));

            using var ms = new MemoryStream();
            ms.Write(ExifHeader);
            var tiff = new MemoryStream();
            void U16(int v) { tiff.WriteByte((byte)(v >> 8)); tiff.WriteByte((byte)v); }
            void U32(int v) { U16(v >> 16); U16(v & 0xFFFF); }

            tiff.Write(new byte[] { (byte)'M', (byte)'M', 0, 42 });
            U32(8);
            U16(entries.Count);
            int dataOffset = 8 + 2 + entries.Count * 12 + 4;
            var extra = new MemoryStream();
            foreach (var (tag, type, count, data) in entries)
            {
                U16(tag);
                U16(type);
                U32(count);
                if (data.Length <= 4)
                {
                    tiff.Write(data);
                    for (int i = data.Length; i < 4; i++) tiff.WriteByte(0);
                }
                else
                {
                    U32(dataOffset + (int)extra.Length);
                    extra.Write(data);
                    if (extra.Length % 2 == 1) extra.WriteByte(0);
                }
            }
            U32(0); // no next IFD
            extra.WriteTo(tiff);
            tiff.WriteTo(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// The JPEG with <paramref name="exif"/> as its EXIF block (replacing any existing one), placed after the JFIF
        /// header. <paramref name="iccSegments"/>, when given, replace the color profile blocks.
        /// </summary>
        public static byte[] Insert(byte[] jpeg, byte[] exif, IReadOnlyList<byte[]>? iccSegments = null)
        {
            if (exif.Length + 2 > 0xFFFF) throw new ArgumentException("The EXIF block is too large for a JPEG segment.", nameof(exif));
            using var output = new MemoryStream(jpeg.Length + exif.Length + 64);
            output.Write(jpeg, 0, 2);
            int pos = 2;
            bool written = false;

            void WriteBlocks()
            {
                output.WriteByte(0xFF);
                output.WriteByte(0xE1);
                output.WriteByte((byte)((exif.Length + 2) >> 8));
                output.WriteByte((byte)(exif.Length + 2));
                output.Write(exif);
                if (iccSegments != null) foreach (var icc in iccSegments) output.Write(icc);
                written = true;
            }

            foreach (var (marker, offset, length) in Segments(jpeg))
            {
                if (!written && marker != 0xE0) WriteBlocks();
                bool drop = marker == 0xE1 && IsExif(jpeg, offset, length) ||
                            iccSegments != null && marker == 0xE2 && length > 16 && Encoding.ASCII.GetString(jpeg, offset + 4, 12) == "ICC_PROFILE\0";
                if (!drop) output.Write(jpeg, offset, length);
                pos = offset + length;
            }
            if (!written) WriteBlocks();
            output.Write(jpeg, pos, jpeg.Length - pos);
            return output.ToArray();
        }

        private readonly record struct Entry(int Type, int Count, int ValueOffset, bool BigEndian);

        /// <summary>A tag in IFD0 of an EXIF block; ValueOffset points at the 4-byte value field.</summary>
        private static Entry? FindEntry(byte[] exif, int tag)
        {
            const int tiffStart = 6;
            if (exif.Length < tiffStart + 8) return null;
            bool big = exif[tiffStart] == (byte)'M';
            int ifd = tiffStart + ReadInt(exif, tiffStart + 4, big);
            if (ifd < tiffStart || ifd + 2 > exif.Length) return null;
            int count = ReadShort(exif, ifd, big);
            for (int i = 0; i < count; i++)
            {
                int at = ifd + 2 + i * 12;
                if (at + 12 > exif.Length) return null;
                if (ReadShort(exif, at, big) == tag) return new Entry(ReadShort(exif, at + 2, big), ReadInt(exif, at + 4, big), at + 8, big);
            }
            return null;
        }

        private static int ReadShort(byte[] b, int at, bool big) => big ? (b[at] << 8) | b[at + 1] : b[at] | (b[at + 1] << 8);

        private static int ReadInt(byte[] b, int at, bool big) =>
            big ? (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3] : b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24);
    }
}
