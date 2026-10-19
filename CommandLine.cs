using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter
{
    /// <summary>
    /// <c>og-converter.exe --convert &lt;file&gt; &lt;format or option&gt; [--out &lt;folder&gt;]</c>: runs one Convert or Tools
    /// wheel option without the UI, for scripts and for reproducing problems. Without a format it lists the choices.
    /// </summary>
    public static class CommandLine
    {
        public static async Task<int> ConvertAsync(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: og-converter --convert <file> [format or tool id] [--out <folder>]");
                return 2;
            }

            string file = Path.GetFullPath(args[1]);
            string? choice = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : null;
            int outIndex = Array.IndexOf(args, "--out");
            string? outDir = outIndex > 0 && outIndex + 1 < args.Length ? Path.GetFullPath(args[outIndex + 1]) : null;

            var items = FormatRegistry.GetWheelItems(file, WheelMode.Convert)
                .Concat(FormatRegistry.GetWheelItems(file, WheelMode.Tools))
                .ToList();
            if (choice == null)
            {
                foreach (var i in items) Console.WriteLine($"  {(i.Mode == WheelMode.Convert ? i.Extension : i.Id),-22} {i.Title}: {i.Description}");
                return 0;
            }

            var item = items.FirstOrDefault(i => i.Mode == WheelMode.Convert && string.Equals(i.Extension, choice, StringComparison.OrdinalIgnoreCase))
                       ?? items.FirstOrDefault(i => string.Equals(i.Id, choice, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                Console.Error.WriteLine($"'{choice}' isn't offered for {Path.GetFileName(file)}. Run without a format to see the choices.");
                return 2;
            }

            var settings = new SettingsService().Current;
            if (outDir != null)
            {
                settings.SaveInSameDirectory = false;
                settings.CustomOutputDirectory = outDir;
            }

            var started = DateTime.Now;
            var job = await new ConversionEngine().ExecuteAsync(file, item, settings);
            if (!job.IsSuccess)
            {
                Console.Error.WriteLine($"Failed after {(DateTime.Now - started).TotalSeconds:0.0}s: {job.ErrorMessage}");
                return 1;
            }
            Console.WriteLine($"{job.TargetPath} ({(DateTime.Now - started).TotalSeconds:0.0}s){(job.ResultText != null ? "  " + job.ResultText : "")}");
            return 0;
        }
    }
}
