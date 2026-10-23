using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Tests
{
    /// <summary>Folder Icon: the wheel offers it for folders only, the icon is saved in the folder and shown, and Reset undoes it.</summary>
    public static class FolderIconVerification
    {
        public static async Task RunAsync(ConversionEngine engine, AppSettings settings, string tempDir, string testPng)
        {
            Console.WriteLine("[25] Folder icons");

            string folder = Path.Combine(tempDir, "Icon Folder");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "notes");

            var item = FormatRegistry.GetWheelItems(folder, WheelMode.Actions).FirstOrDefault(i => i.Id == ConversionEngine.FolderIconActionId)
                ?? throw new Exception("Folder Icon isn't on the folder's Actions wheel");
            if (FormatRegistry.GetWheelItems(testPng, WheelMode.Actions).Any(i => i.Id == ConversionEngine.FolderIconActionId))
                throw new Exception("Folder Icon is offered for a file");

            // The library: every symbol is usable path data on the 24 x 24 grid
            Console.WriteLine($"     Library: {FolderIconLibrary.Colors.Count} colours, {FolderIconLibrary.Symbols.Count} symbols");
            if (FolderIconLibrary.Symbols.Any(s => s.PathData.Length < 10 || !s.PathData.StartsWith('M')))
                throw new Exception("A folder symbol has no path data");

            byte[] png = File.ReadAllBytes(testPng);
            var images = FolderIconService.Sizes.Select(s => new FolderIconImage(s, png)).ToList();
            string iconFile = FolderIconService.IconFile(folder);

            // Twice: the second time overwrites the hidden icon file
            for (int pass = 1; pass <= 2; pass++)
            {
                var job = await engine.ExecuteAsync(folder, item, settings, new ToolParameters { FolderIcon = images });
                string? shown = Platform.Desktop.GetFolderIcon(folder);
                Console.WriteLine($"     Set (pass {pass}): Success={job.IsSuccess} file={File.Exists(iconFile)} shown={(shown == null ? "none" : Path.GetFileName(shown))} {job.ErrorMessage}");
                if (!job.IsSuccess || !File.Exists(iconFile)) throw new Exception($"Setting the folder icon failed: {job.ErrorMessage}");
                if (!string.Equals(shown, iconFile, Platform.PathComparison)) throw new Exception($"The file manager shows '{shown}', not the new icon");
            }

            if (OperatingSystem.IsWindows())
            {
                byte[] ico = File.ReadAllBytes(iconFile);
                int count = ico[4] | ico[5] << 8;
                var attributes = File.GetAttributes(iconFile);
                string ini = File.ReadAllText(Path.Combine(folder, "desktop.ini"));
                Console.WriteLine($"     .ico: {count} sizes, {ico.Length:N0} bytes, {attributes}; desktop.ini names '{ini.Split('\n').FirstOrDefault(l => l.StartsWith("IconResource", StringComparison.OrdinalIgnoreCase))?.Trim()}'");
                if (ico[0] != 0 || ico[1] != 0 || ico[2] != 1 || count != FolderIconService.Sizes.Count) throw new Exception("The .ico header is wrong");
                if (!attributes.HasFlag(FileAttributes.Hidden)) throw new Exception("The icon file isn't hidden");
                if (ini.Contains(folder, StringComparison.OrdinalIgnoreCase)) throw new Exception("desktop.ini names the icon by full path, so it breaks when the folder moves");
            }

            // Reset: the default icon again, and our file is gone
            var reset = await engine.ExecuteAsync(folder, item, settings, new ToolParameters { FolderIcon = new() });
            string? after = Platform.Desktop.GetFolderIcon(folder);
            Console.WriteLine($"     Reset: Success={reset.IsSuccess} file={File.Exists(iconFile)} shown={(after == null ? "none" : Path.GetFileName(after))} {reset.ErrorMessage}");
            if (!reset.IsSuccess || File.Exists(iconFile) || after != null) throw new Exception("Resetting the folder icon failed");
            if (!File.Exists(Path.Combine(folder, "notes.txt"))) throw new Exception("Reset touched the folder's own files");

            var noChoice = await engine.ExecuteAsync(folder, item, settings);
            if (noChoice.IsSuccess) throw new Exception("Folder Icon without a chosen icon should fail");
        }
    }
}
