using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform;

namespace OgConverter.Services
{
    /// <summary>
    /// The app icon. One .ico (shared with Windows) ships as an Avalonia resource; its frames are PNGs, which the
    /// window, the tray, notifications and the installed hicolor icons use.
    /// </summary>
    public static class AppIcon
    {
        private static readonly Uri IcoUri = new("avares://og-converter/Assets/og-converter.ico");

        /// <summary>The window / tray icon.</summary>
        public static WindowIcon CreateWindowIcon()
        {
            using var stream = AssetLoader.Open(IcoUri);
            return new WindowIcon(stream);
        }

        /// <summary>Frame sizes in the .ico (16 … 256).</summary>
        public static IReadOnlyList<int> Sizes => ReadFrames().ConvertAll(f => f.Size);

        /// <summary>The PNG frame of the given size (the nearest larger one when that size isn't there).</summary>
        public static byte[] Png(int size)
        {
            var frames = ReadFrames();
            frames.Sort((a, b) => a.Size.CompareTo(b.Size));
            var pick = frames.Find(f => f.Size >= size) ?? frames[^1];
            return pick.Png;
        }

        /// <summary>Writes the PNG frame of <paramref name="size"/> to <paramref name="path"/> (once) and returns the path.</summary>
        public static string ExtractPng(int size, string path)
        {
            byte[] png = Png(size);
            if (!File.Exists(path) || new FileInfo(path).Length != png.Length)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, png);
            }
            return path;
        }

        private sealed record Frame(int Size, byte[] Png);

        /// <summary>ICONDIR + ICONDIRENTRY records; every frame of this icon is PNG-compressed.</summary>
        private static List<Frame> ReadFrames()
        {
            using var stream = AssetLoader.Open(IcoUri);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] d = ms.ToArray();
            if (d.Length < 6 || BitConverter.ToUInt16(d, 2) != 1) throw new InvalidDataException("Not an .ico file.");

            int count = BitConverter.ToUInt16(d, 4);
            var frames = new List<Frame>();
            for (int i = 0; i < count; i++)
            {
                int e = 6 + 16 * i;
                int size = d[e] == 0 ? 256 : d[e];
                int length = BitConverter.ToInt32(d, e + 8), offset = BitConverter.ToInt32(d, e + 12);
                if (offset + length > d.Length || length < 8) continue;
                // PNG signature: 89 50 4E 47
                if (d[offset] != 0x89 || d[offset + 1] != 0x50) continue;
                frames.Add(new Frame(size, d.AsSpan(offset, length).ToArray()));
            }
            if (frames.Count == 0) throw new InvalidDataException("The icon has no PNG frames.");
            return frames;
        }
    }
}
