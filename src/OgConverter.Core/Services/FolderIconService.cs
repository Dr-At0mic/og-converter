using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>
    /// Gives a folder its own icon in the file manager. The icon is saved inside the folder as a hidden file, so it moves,
    /// copies and syncs with it: <c>.folder-icon.ico</c> on Windows (every size Explorer asks for), <c>.folder-icon.png</c>
    /// elsewhere. <see cref="IDesktopServices.SetFolderIcon"/> then tells the file manager to use it.
    /// </summary>
    public static class FolderIconService
    {
        public const string IconFileBase = ".folder-icon";

        /// <summary>The sizes to draw, largest first: Explorer picks from an .ico's sizes, GNOME Files scales one PNG.</summary>
        public static IReadOnlyList<int> Sizes { get; } = OperatingSystem.IsWindows() ? new[] { 256, 48, 32, 24, 16 } : new[] { 256 };

        /// <summary>The icon file this app keeps in <paramref name="folder"/>.</summary>
        public static string IconFile(string folder) => Path.Combine(folder, IconFileBase + (OperatingSystem.IsWindows() ? ".ico" : ".png"));

        /// <summary>Saves the drawn icon into the folder and makes it the folder's icon.</summary>
        public static void Apply(string folder, IReadOnlyList<FolderIconImage> images)
        {
            if (!Directory.Exists(folder)) throw new InvalidOperationException("Folder icons can only be set on folders.");
            if (images.Count == 0) throw new ArgumentException("No icon to set.", nameof(images));

            string file = IconFile(folder);
            byte[] data = OperatingSystem.IsWindows() ? BuildIco(images) : images.OrderByDescending(i => i.Size).First().Png;
            try
            {
                // A hidden or system file can't be overwritten until those attributes are cleared
                if (File.Exists(file)) File.SetAttributes(file, FileAttributes.Normal);
                File.WriteAllBytes(file, data);
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Can't write into {Path.GetFileName(folder)}: you don't have permission to change this folder.");
            }
            Platform.Desktop.SetFolderIcon(folder, file);
        }

        /// <summary>Back to the default folder icon; removes the icon file this app saved there.</summary>
        public static void Reset(string folder)
        {
            if (!Directory.Exists(folder)) throw new InvalidOperationException("Folder icons can only be set on folders.");
            Platform.Desktop.SetFolderIcon(folder, null);

            string file = IconFile(folder);
            if (!File.Exists(file)) return;
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// An .ico holding each PNG as is (Windows Vista and later read PNG entries). The directory lists the sizes from
        /// largest to smallest; a 256 px entry is written as size 0, as the format requires.
        /// </summary>
        public static byte[] BuildIco(IReadOnlyList<FolderIconImage> images)
        {
            var entries = images.OrderByDescending(i => i.Size).ToList();
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((ushort)0);             // reserved
            w.Write((ushort)1);             // type: icon
            w.Write((ushort)entries.Count);

            int offset = 6 + 16 * entries.Count;
            foreach (var e in entries)
            {
                if (e.Size < 1 || e.Size > 256) throw new ArgumentException($"Icons are 1 to 256 pixels, not {e.Size}.", nameof(images));
                w.Write((byte)(e.Size == 256 ? 0 : e.Size));
                w.Write((byte)(e.Size == 256 ? 0 : e.Size));
                w.Write((byte)0);           // no palette
                w.Write((byte)0);           // reserved
                w.Write((ushort)1);         // colour planes
                w.Write((ushort)32);        // bits per pixel
                w.Write(e.Png.Length);
                w.Write(offset);
                offset += e.Png.Length;
            }
            foreach (var e in entries) w.Write(e.Png);
            w.Flush();
            return ms.ToArray();
        }
    }
}
