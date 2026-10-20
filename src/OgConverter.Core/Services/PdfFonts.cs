using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PdfSharp.Fonts;

namespace OgConverter.Services
{
    /// <summary>
    /// Fonts for text that PDFsharp draws itself. On Windows PDFsharp reads the Windows fonts folder; elsewhere it has no
    /// fonts of its own, so <see cref="SystemFontResolver"/> finds them with fontconfig.
    /// </summary>
    public static class PdfFonts
    {
        private static readonly object Lock = new();

        /// <summary>Call before drawing text with PDFsharp (new XFont). Safe to call any number of times.</summary>
        public static void EnsureConfigured()
        {
            if (OperatingSystem.IsWindows())
            {
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;
                return;
            }
            lock (Lock)
            {
                GlobalFontSettings.FontResolver ??= new SystemFontResolver();
            }
        }
    }

    /// <summary>
    /// Resolves font families to TrueType files with fontconfig (<c>fc-match</c>), so "Arial" becomes Liberation Sans on a
    /// typical Linux desktop. PDFsharp embeds TrueType outlines only, so CFF-based OpenType fonts are skipped; when nothing
    /// matches, the first sans-serif TrueType font fontconfig lists is used. Bold and italic are simulated when the family
    /// has no such face.
    /// </summary>
    public sealed class SystemFontResolver : IFontResolver
    {
        /// <summary>Face name (the font file path) → font bytes.</summary>
        private readonly ConcurrentDictionary<string, byte[]> _files = new();
        private readonly ConcurrentDictionary<string, string?> _matches = new();

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            string style = (isBold ? ":weight=bold" : "") + (isItalic ? ":slant=italic" : "");
            string? file = Match(familyName + style) ?? Match(familyName) ?? Match("sans-serif" + style) ?? Match("sans-serif") ?? AnyTrueType();
            if (file == null) return null;

            // The file's own style decides what still needs simulating
            string? plainFile = Match(familyName) ?? Match("sans-serif");
            bool exactStyle = file != plainFile || !isBold && !isItalic;
            return exactStyle ? new FontResolverInfo(file) : new FontResolverInfo(file, isBold, isItalic);
        }

        public byte[]? GetFont(string faceName) =>
            _files.GetOrAdd(faceName, f => File.Exists(f) ? File.ReadAllBytes(f) : Array.Empty<byte>()) is { Length: > 0 } data ? data : null;

        /// <summary>The TrueType file fontconfig picks for a pattern ("Arial:weight=bold"), or null.</summary>
        private string? Match(string pattern) => _matches.GetOrAdd(pattern, p =>
        {
            string? file = Run("fc-match", "--format=%{file}", p + ":fontformat=TrueType")?.Trim();
            return IsTrueType(file) ? file : null;
        });

        private string? AnyTrueType() => _matches.GetOrAdd("\0any", _ =>
            (Run("fc-list", ":fontformat=TrueType", "file") ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim().TrimEnd(':'))
                .Where(IsTrueType)
                .OrderBy(f => f.Contains("Sans", StringComparison.Ordinal) ? 0 : 1)
                .FirstOrDefault());

        /// <summary>A plain .ttf file (collections and CFF outlines can't be embedded by PDFsharp).</summary>
        private static bool IsTrueType(string? file) =>
            !string.IsNullOrEmpty(file) && file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) && File.Exists(file);

        private static string? Run(string tool, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (string a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p == null) return null;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10_000);
                return p.ExitCode == 0 ? output : null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return null;
            }
        }
    }
}
