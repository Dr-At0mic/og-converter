using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using OgConverter.Services;

namespace OgConverter.Tests
{
    /// <summary>
    /// What the portable self-test needs from Linux: the clipboard (wl-paste or xclip, in a desktop session), HEIC test
    /// files (libheif's heif-enc) and a test deck (LibreOffice). Linux doesn't mark downloaded files, and the Word-only
    /// and UI checks are left to the app.
    /// </summary>
    public static class LinuxSelfTests
    {
        private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

        public static PlatformSelfTests Create()
        {
            // Copy and paste need a desktop session and its clipboard tool; over SSH or in CI there's neither. xclip reads
            // the clipboard GNOME and KDE share between X11 and Wayland, and unlike wl-paste it works while the screen is
            // locked (wl-paste then waits forever)
            string? paste = Env("DISPLAY") != null ? Services.Platform.FindOnPath("xclip") : null;
            paste ??= Env("WAYLAND_DISPLAY") != null ? Services.Platform.FindOnPath("wl-paste") : null;
            return new PlatformSelfTests
            {
                CreatePresentation = DocumentEngine.LibreOfficePath != null ? CreatePresentation : null,
                WriteHeicAsync = TryWriteHeicAsync,
                ReadClipboardText = paste == null ? null : () => ReadClipboardText(paste),
                ReadClipboardImageAndFile = paste == null ? null : () =>
                {
                    // wl-copy and xclip offer one format per copy, so Copy Image puts only the picture there
                    bool image = ClipboardTypes(paste).Contains("image/png");
                    return (image, image);
                }
            };
        }

        private static bool IsWlPaste(string tool) => Path.GetFileName(tool) == "wl-paste";

        /// <summary>The clipboard as text.</summary>
        private static string ReadClipboardText(string tool) => Encoding.UTF8.GetString(Run(tool, IsWlPaste(tool)
            ? new[] { "--no-newline", "--type", "text/plain" }
            : new[] { "-selection", "clipboard", "-o" }));

        /// <summary>The formats the clipboard offers ("image/png", "text/plain", …).</summary>
        private static string[] ClipboardTypes(string tool) => Encoding.UTF8.GetString(Run(tool, IsWlPaste(tool)
                ? new[] { "--list-types" }
                : new[] { "-selection", "clipboard", "-o", "-t", "TARGETS" }))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>
        /// The two-slide test deck: written as a flat OpenDocument presentation (one XML file), then saved as PPTX by
        /// LibreOffice, the way PowerPoint makes it on Windows. Title and notes need a presentation style to come out as
        /// PowerPoint's title and notes placeholders rather than plain text boxes.
        /// </summary>
        private static void CreatePresentation(string path, string picture)
        {
            string work = Path.Combine(Path.GetTempPath(), "ogc-deck-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string fodp = Path.Combine(work, Path.GetFileNameWithoutExtension(path) + ".fodp");
                File.WriteAllText(fodp, DeckXml(Convert.ToBase64String(File.ReadAllBytes(picture))), new UTF8Encoding(false));
                string profile = new Uri(Path.Combine(work, "profile")).AbsoluteUri;
                Run(DocumentEngine.LibreOfficePath!, new[] { $"-env:UserInstallation={profile}", "--headless", "--norestore", "--nolockcheck",
                                                             "--convert-to", "pptx", "--outdir", work, fodp }, timeoutMs: 180_000);
                string produced = Path.ChangeExtension(fodp, ".pptx");
                if (!File.Exists(produced)) throw new Exception("LibreOffice didn't make the test deck");
                File.Move(produced, path, overwrite: true);
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        private static string DeckXml(string pngBase64) => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"
                xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
                xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
                xmlns:presentation="urn:oasis:names:tc:opendocument:xmlns:presentation:1.0"
                xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"
                xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"
                office:version="1.3" office:mimetype="application/vnd.oasis.opendocument.presentation">
              <office:automatic-styles>
                <style:page-layout style:name="PM1">
                  <style:page-layout-properties fo:page-width="25.4cm" fo:page-height="14.288cm" style:print-orientation="landscape"/>
                </style:page-layout>
                <style:style style:name="pr1" style:family="presentation"/>
              </office:automatic-styles>
              <office:master-styles>
                <style:master-page style:name="Default" style:page-layout-name="PM1"/>
              </office:master-styles>
              <office:body>
                <office:presentation>
                  <draw:page draw:name="Slide 1" draw:master-page-name="Default">
                    <draw:frame draw:layer="layout" presentation:style-name="pr1" presentation:class="title" presentation:placeholder="false" presentation:user-transformed="true" svg:x="2cm" svg:y="5cm" svg:width="21.4cm" svg:height="3cm">
                      <draw:text-box><text:p>{SecurityElement.Escape("OG Converter test deck")}</text:p></draw:text-box>
                    </draw:frame>
                    <presentation:notes>
                      <draw:page-thumbnail draw:layer="layout" presentation:class="page" svg:x="3.5cm" svg:y="2cm" svg:width="14cm" svg:height="7.9cm"/>
                      <draw:frame draw:layer="layout" presentation:style-name="pr1" presentation:class="notes" presentation:placeholder="false" presentation:user-transformed="true" svg:x="2cm" svg:y="14cm" svg:width="17cm" svg:height="10cm">
                        <draw:text-box><text:p>Remember the demo</text:p></draw:text-box>
                      </draw:frame>
                    </presentation:notes>
                  </draw:page>
                  <draw:page draw:name="Slide 2" draw:master-page-name="Default">
                    <draw:frame svg:x="5cm" svg:y="2cm" svg:width="15.4cm" svg:height="10cm">
                      <draw:image><office:binary-data>{pngBase64}</office:binary-data></draw:image>
                    </draw:frame>
                  </draw:page>
                </office:presentation>
              </office:body>
            </office:document>
            """;

        /// <summary>A 64×48 HEIC photo through libheif's heif-enc; false when it isn't installed.</summary>
        private static Task<bool> TryWriteHeicAsync(string path)
        {
            string? heifEnc = Services.Platform.FindOnPath("heif-enc");
            if (heifEnc == null)
            {
                Console.WriteLine($"     (no HEIC encoder: {InstallHints.Command("libheif-examples")})");
                return Task.FromResult(false);
            }
            string png = Path.ChangeExtension(path, ".source.png");
            try
            {
                byte[] px = new byte[64 * 48 * 4];
                for (int i = 0; i < px.Length; i += 4) { px[i] = 220; px[i + 1] = 120; px[i + 2] = 40; px[i + 3] = 255; } // BGRA
                using (var file = File.Create(png))
                {
                    Services.Platform.Images.Encode(Services.Platform.Images.FromPixels(64, 48, 96, RasterFormat.Bgra32, px, 64 * 4), file, ImageEncoding.Png());
                }
                Run(heifEnc, new[] { "-o", path, png });
                return Task.FromResult(File.Exists(path) && new FileInfo(path).Length > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"     (HEIC encoder: {ex.Message.Trim()})");
                return Task.FromResult(false);
            }
            finally
            {
                try { File.Delete(png); } catch { }
            }
        }

        /// <summary>Runs a tool to completion and returns what it printed; throws when it fails.</summary>
        private static byte[] Run(string tool, string[] args, int timeoutMs = 15_000)
        {
            var psi = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new Exception($"Couldn't start {Path.GetFileName(tool)}");
            using var output = new MemoryStream();
            var copy = p.StandardOutput.BaseStream.CopyToAsync(output);
            var errors = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                throw new Exception($"{Path.GetFileName(tool)} didn't finish");
            }
            copy.Wait(2000);
            if (p.ExitCode != 0) throw new Exception($"{Path.GetFileName(tool)} failed: {(errors.Wait(500) ? errors.Result.Trim() : "")}");
            return output.ToArray();
        }
    }
}
