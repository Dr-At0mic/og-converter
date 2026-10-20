using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OgConverter.Services;

namespace OgConverter.Platform.Linux
{
    /// <summary>
    /// Text recognition with the Tesseract command-line tool (offline). Reads the language matching the user's locale
    /// when its data is installed, plus English.
    /// </summary>
    public sealed class TesseractOcrEngine : IOcrEngine
    {
        private static readonly Lazy<string?> Tool = new(() => Services.Platform.FindOnPath("tesseract"));
        private static readonly Lazy<IReadOnlyList<string>> Languages = new(ListLanguages);

        public bool IsAvailable => Tool.Value != null && Languages.Value.Count > 0;

        public string UnavailableHint => Tool.Value == null
            ? "Install Tesseract: " + InstallHints.Command("tesseract")
            : "Tesseract has no recognition language installed. Install one: " + InstallHints.Command("tesseract-eng");

        /// <summary>Tesseract takes large pictures; this only bounds memory and time on huge scans.</summary>
        public int MaxImageDimension => 10000;

        public async Task<IReadOnlyList<OcrTextLine>> RecognizeAsync(RasterImage image)
        {
            string tool = Tool.Value ?? throw new InvalidOperationException(UnavailableHint);
            if (Languages.Value.Count == 0) throw new InvalidOperationException(UnavailableHint);

            string png = Path.Combine(Path.GetTempPath(), $"ogc-ocr-{Guid.NewGuid():N}.png");
            try
            {
                using (var fs = File.Create(png)) Services.Platform.Images.Encode(image, fs, ImageEncoding.Png());

                var psi = new ProcessStartInfo(tool)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                foreach (string a in new[] { png, "stdout", "-l", LanguageArgument(), "tsv" }) psi.ArgumentList.Add(a);

                using var p = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start Tesseract.");
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode != 0) throw new InvalidOperationException("Tesseract couldn't read the picture: " + (await stderr).Trim());
                return ParseTsv(await stdout);
            }
            finally
            {
                try { File.Delete(png); } catch { }
            }
        }

        /// <summary>
        /// TSV rows: level page block paragraph line word left top width height confidence text. Words (level 5) are
        /// grouped into their lines, in Tesseract's reading order.
        /// </summary>
        internal static List<OcrTextLine> ParseTsv(string tsv)
        {
            var lines = new List<OcrTextLine>();
            string? key = null;
            var words = new List<string>();
            double top = double.MaxValue, bottom = 0;

            void Flush()
            {
                if (words.Count > 0) lines.Add(new OcrTextLine(string.Join(" ", words), top, bottom));
                words.Clear();
                top = double.MaxValue;
                bottom = 0;
            }

            foreach (string row in tsv.Split('\n'))
            {
                string[] f = row.TrimEnd('\r').Split('\t');
                if (f.Length < 12 || f[0] != "5") continue;
                string text = f[11].Trim();
                if (text.Length == 0) continue;
                string lineKey = string.Join(",", f[1], f[2], f[3], f[4]);
                if (lineKey != key)
                {
                    Flush();
                    key = lineKey;
                }
                words.Add(text);
                if (double.TryParse(f[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out double t) &&
                    double.TryParse(f[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out double h))
                {
                    top = Math.Min(top, t);
                    bottom = Math.Max(bottom, t + h);
                }
            }
            Flush();
            return lines;
        }

        /// <summary>"deu+eng": the locale's language when installed, then English; else the first installed language.</summary>
        private static string LanguageArgument()
        {
            var installed = Languages.Value;
            var picked = new List<string>();
            string mine = CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName;
            if (mine != "eng" && installed.Contains(mine)) picked.Add(mine);
            if (installed.Contains("eng")) picked.Add("eng");
            if (picked.Count == 0) picked.Add(installed[0]);
            return string.Join("+", picked);
        }

        /// <summary>Installed recognition languages ("osd" is page-layout data, not a language).</summary>
        private static IReadOnlyList<string> ListLanguages()
        {
            if (Tool.Value == null) return Array.Empty<string>();
            try
            {
                var psi = new ProcessStartInfo(Tool.Value) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add("--list-langs");
                using var p = Process.Start(psi);
                if (p == null) return Array.Empty<string>();
                string output = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return output.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("List of", StringComparison.Ordinal) && l != "osd" && !l.Contains(' '))
                    .ToList();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return Array.Empty<string>();
            }
        }
    }
}
