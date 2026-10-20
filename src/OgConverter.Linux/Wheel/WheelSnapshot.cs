using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Views;

namespace OgConverter.Wheel
{
    /// <summary>
    /// Dev aid: <c>og-converter --snapshot-wheel &lt;dir&gt;</c> renders every built-in template's wheel to PNG without a
    /// display (Avalonia headless with Skia), named like the Windows app's <c>--snapshot</c> so the two can be compared.
    /// </summary>
    public static class WheelSnapshot
    {
        private static readonly Color Slate = Color.FromRgb(0x5A, 0x6B, 0x86);
        private static readonly Color Light = Color.FromRgb(0xF3, 0xF4, 0xF6);

        private sealed class SnapshotApp : Application
        {
            public override void Initialize() => Styles.Add(new FluentTheme());
        }

        public static int Run(string outDir)
        {
            try
            {
                LinuxWheel.Register();
                EnsureDesktop();
                AppBuilder.Configure<SnapshotApp>()
                    .UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

                Directory.CreateDirectory(outDir);
                Render(Path.GetFullPath(outDir));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"WHEEL SNAPSHOT FAILED: {ex}");
                return 1;
            }
        }

        private static void Render(string outDir)
        {
            // Snapshots capture a single frame: no open/hover animations in flight, no sounds
            RadialWheelCanvas.ReduceMotion = true;
            SoundService.Instance.IsEnabled = false;

            string pdf = Path.Combine(outDir, "Quarterly Report.pdf");
            Tests.VerificationRunner.CreateDummyPdf(pdf, 12);
            string png = Path.Combine(outDir, "Holiday photo.png");
            File.WriteAllBytes(png, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
            string mp4 = Path.Combine(outDir, "Screen recording.mp4");
            File.WriteAllBytes(mp4, new byte[1024]);

            var overlay = new WheelOverlay();
            overlay.UpdateKeyLabels("Shift", "Control", "Alt");
            string originalTemplate = TemplateManager.Instance.Current.Id;
            int count = 0;

            foreach (var template in TemplateManager.Instance.Templates.Where(t => t.Source == TemplateSource.BuiltIn).ToList())
            {
                TemplateManager.Instance.Apply(template.Id);
                string id = template.Id;

                overlay.ApplyScale(1.0, true);
                foreach (var mode in new[] { WheelMode.Convert, WheelMode.Tools, WheelMode.Actions })
                {
                    string m = mode.ToString().ToLowerInvariant();
                    Show(overlay, mode, pdf, 1);
                    Save(overlay, Path.Combine(outDir, $"tpl_{id}_wheel_{m}.png"), Slate, ref count);
                    Show(overlay, mode, png, 1);
                    Save(overlay, Path.Combine(outDir, $"tpl_{id}_png_{m}.png"), Slate, ref count);
                    Show(overlay, mode, mp4, 1);
                    Save(overlay, Path.Combine(outDir, $"tpl_{id}_mp4_{m}.png"), Slate, ref count);
                }
                Show(overlay, WheelMode.Convert, pdf, -1);
                Save(overlay, Path.Combine(outDir, $"tpl_{id}_wheel_idle_light.png"), Light, ref count);

                overlay.ApplyScale(0.4, true);
                Show(overlay, WheelMode.Convert, pdf, 2);
                Save(overlay, Path.Combine(outDir, $"tpl_{id}_wheel_40.png"), Slate, ref count);
                overlay.ApplyScale(0.4, false);
                Show(overlay, WheelMode.Convert, pdf, 2);
                Save(overlay, Path.Combine(outDir, $"tpl_{id}_wheel_40_nohints.png"), Slate, ref count);

                // The drag-mode hub before a file arrives
                overlay.ApplyScale(1.0, true);
                overlay.Dismiss();
                overlay.ShowAt(null, WheelMode.Convert, null, dragMode: true);
                Save(overlay, Path.Combine(outDir, $"tpl_{id}_wheel_nofile.png"), Slate, ref count);
            }

            overlay.Dismiss();
            overlay.Close();
            TemplateManager.Instance.Apply(originalTemplate);
            Console.WriteLine($"Wrote {count} wheel snapshots to {outDir}");
        }

        private static void Show(WheelOverlay overlay, WheelMode mode, string file, int hovered)
        {
            overlay.Dismiss();
            overlay.ShowAt(null, mode, file, dragMode: false);
            overlay.WheelCanvas.HoveredIndex = hovered;
        }

        private static void Save(Window window, string path, Color backdrop, ref int count)
        {
            var previous = window.Background;
            window.Background = new SolidColorBrush(backdrop);
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            window.Background = previous;
            if (frame == null) return;
            frame.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            count++;
        }

        /// <summary>The Actions wheel asks the desktop whether a file is blocked; give it an answer when no backend is registered.</summary>
        private static void EnsureDesktop()
        {
            try
            {
                _ = Services.Platform.Desktop;
            }
            catch (InvalidOperationException)
            {
                Services.Platform.Desktop = new NoDesktop();
            }
        }

        private sealed class NoDesktop : IDesktopServices
        {
            public void SetClipboardText(string text) { }
            public void SetClipboardImageAndFile(string picturePath, string filePath) { }
            public void RevealInFileManager(string path) { }
            public void OpenTerminal(string directory) { }
            public void OpenUrl(string url) { }
            public bool IsBlocked(string path) => false;
            public void Unblock(string path) { }
            public void SetWallpaper(string imagePath) { }
        }
    }
}
