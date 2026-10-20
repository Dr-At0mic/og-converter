using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Wheel;

namespace OgConverter.Tests
{
    /// <summary>
    /// Self-test for wheel templates: built-ins render in every mode and size, community templates import from
    /// JSON and .zip, plugin DLLs load (when OGCONVERTER_PLUGIN_SAMPLE points at the sample plugin), and bad input
    /// is rejected. Runs against a temporary templates folder unless OGCONVERTER_TEMPLATES_DIR is already set.
    /// </summary>
    internal static class TemplateVerification
    {
        /// <summary>Call before anything touches TemplateManager, so it never sees the real %AppData% templates.</summary>
        public static void IsolateTemplatesFolder(string tempDir)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OGCONVERTER_TEMPLATES_DIR")))
                Environment.SetEnvironmentVariable("OGCONVERTER_TEMPLATES_DIR", Path.Combine(tempDir, "templates"));
        }

        public static void Run(string tempDir)
        {
            Console.WriteLine("[30] Wheel templates");
            var manager = TemplateManager.Instance;
            int builtIns = manager.Templates.Count(t => t.Source == TemplateSource.BuiltIn);
            if (builtIns < 5) throw new Exception($"Expected 5 built-in templates, found {builtIns}");

            foreach (var t in manager.Templates.ToList()) RenderAll(t);
            Console.WriteLine($"     {builtIns} built-ins render in all modes at 40% and 100%");

            // JSON template with a custom sound that must be copied along, and one that tries to escape its folder
            string src = Path.Combine(tempDir, "tpl-src");
            Directory.CreateDirectory(src);
            File.WriteAllBytes(Path.Combine(src, "tick.wav"), MinimalWav());
            File.WriteAllText(Path.Combine(src, "template.json"), """
                // comments and trailing commas are allowed
                {
                  "name": "Test Dusk",
                  "renderer": "glass",
                  "wheel": { "convert": "#FF7A59", "ring": "not-a-color", },
                  "app": { "base": "light" },
                  "shape": { "innerRatio": 5 },
                  "sounds": { "pack": "bubble", "tick": "tick.wav", "open": "..\\..\\secret.wav" },
                }
                """);
            var added = manager.Import(Path.Combine(src, "template.json"));
            var dusk = added.Single();
            if (dusk.Id != "test-dusk") throw new Exception($"Id not derived from the name: {dusk.Id}");
            if (dusk.Shape.InnerRatio > 0.6) throw new Exception("innerRatio was not clamped");
            if (TemplateManager.ResolveAsset(dusk, dusk.Sounds.Tick) == null) throw new Exception("Referenced sound was not copied");
            if (TemplateManager.ResolveAsset(dusk, dusk.Sounds.Open) != null) throw new Exception("A sound path escaped the template folder");
            manager.Apply(dusk.Id);
            AppThemeService.Apply(manager.Current);
            RenderAll(dusk);
            SoundService.Instance.Play(UiSound.Tick, dusk, force: true);
            Console.WriteLine("     JSON import, id/limits normalization, sound sandboxing: OK");

            // Adding the same template again another way (its folder instead of its template.json) replaces it
            manager.Import(src);
            int copies = manager.Templates.Count(t => t.Id == "test-dusk");
            if (copies != 1 || manager.LoadErrors.Any(e => e.Contains("already loaded"))) throw new Exception($"Re-import left {copies} copies of the template");
            dusk = manager.Find("test-dusk")!;
            Console.WriteLine("     Re-import by id replaces the installed copy: OK");

            // Zip package wrapping everything in one folder
            string zip = Path.Combine(tempDir, "neon-pack.ogtemplate");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("neon-pack/template.json");
                using var w = new StreamWriter(entry.Open());
                w.Write("""[{ "id": "pack-a", "name": "Pack A", "renderer": "neon" }, { "id": "pack-b", "name": "Pack B", "renderer": "orbit" }]""");
            }
            var pack = manager.Import(zip);
            if (pack.Count != 2) throw new Exception($"Expected 2 templates from the package, got {pack.Count}");
            foreach (var t in pack) RenderAll(t);
            Console.WriteLine("     .ogtemplate package with two templates: OK");

            // Rejections
            File.WriteAllText(Path.Combine(src, "broken.json"), "{ \"name\": ");
            ExpectFailure(() => manager.Import(Path.Combine(src, "broken.json")), "invalid JSON");
            ExpectFailure(() => manager.Import(Path.Combine(src, "tick.wav")), "unsupported file");

            // Optional plugin
            string? plugin = Environment.GetEnvironmentVariable("OGCONVERTER_PLUGIN_SAMPLE");
            if (!string.IsNullOrEmpty(plugin) && File.Exists(plugin))
            {
                if (!TemplateManager.ContainsCode(plugin)) throw new Exception("Plugin not flagged as code");
                var fromPlugin = manager.Import(plugin);
                var petals = fromPlugin.Single();
                if (!manager.HasRenderer(petals.Renderer)) throw new Exception($"Plugin renderer {petals.Renderer} not registered");
                if (manager.RendererFor(petals).GetType().Assembly == typeof(IWheelRenderer).Assembly) throw new Exception("Plugin template fell back to a built-in renderer");
                RenderAll(petals);
                manager.Remove(petals);
                if (manager.HasRenderer("petals")) throw new Exception("Plugin renderer still registered after removal");
                Console.WriteLine("     Plugin .dll import, render and removal: OK");
            }
            else
            {
                Console.WriteLine("     Plugin check skipped (set OGCONVERTER_PLUGIN_SAMPLE to the sample plugin .dll)");
            }

            manager.Remove(dusk);
            foreach (var t in pack) manager.Remove(manager.Find(t.Id) ?? t);
            if (manager.Current.Source != TemplateSource.BuiltIn) throw new Exception("Removing the active template didn't fall back to a built-in");
            if (manager.Templates.Count != builtIns) throw new Exception("Community templates left behind after removal");
            Console.WriteLine("     Removal and fallback to the default template: OK");
        }

        private static void RenderAll(WheelTemplate t)
        {
            var items = FormatRegistry.GetWheelItems("photo.png", WheelMode.Convert);
            foreach (double scale in new[] { 0.4, 1.0 })
            {
                foreach (var mode in new[] { WheelMode.Convert, WheelMode.Tools, WheelMode.Actions })
                {
                    var canvas = new RadialWheelCanvas { TemplateOverride = t, PlaySounds = false, Items = items, CurrentMode = mode, WheelScale = scale, HoveredIndex = 1 };
                    double size = RadialWheelCanvas.BaseSize * scale;
                    canvas.Measure(new Size(size, size));
                    canvas.Arrange(new Rect(0, 0, size, size));
                    var bmp = new RenderTargetBitmap((int)size, (int)size, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(canvas);
                    if (canvas.HitTestSlice(new Point(size / 2, size / 2 - canvas.OuterRadius * 0.7)) != 0)
                        throw new Exception($"{t.Name}: hit test at the top slice failed ({mode}, {scale:P0})");
                }
            }
        }

        private static void ExpectFailure(Action action, string what)
        {
            try
            {
                action();
            }
            catch (TemplateException)
            {
                return;
            }
            throw new Exception($"Importing {what} should have failed");
        }

        private static byte[] MinimalWav()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            short[] samples = new short[441];
            bw.Write("RIFF"u8); bw.Write(36 + samples.Length * 2); bw.Write("WAVE"u8);
            bw.Write("fmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(44100); bw.Write(88200); bw.Write((short)2); bw.Write((short)16);
            bw.Write("data"u8); bw.Write(samples.Length * 2);
            foreach (var s in samples) bw.Write(s);
            return ms.ToArray();
        }
    }
}
