using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Views;

namespace OgConverter.Tests
{
    /// <summary>
    /// Dev aid: <c>og-converter --snapshot &lt;dir&gt;</c> renders the dashboard and every dialog to PNG without a display
    /// (Avalonia headless with Skia), with the same file names as the Windows app's <c>--snapshot</c> so the two can be
    /// compared side by side. The wheel has its own <c>--snapshot-wheel</c>.
    /// </summary>
    public static class SnapshotRunner
    {
        private static readonly Color Dark = Color.FromRgb(0x1E, 0x23, 0x2E);

        public static int Run(string outDir)
        {
            try
            {
                AppBuilder.Configure<App>()
                    .UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

                outDir = Path.GetFullPath(outDir);
                Directory.CreateDirectory(outDir);
                Render(outDir);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"SNAPSHOT FAILED: {ex}");
                return 1;
            }
        }

        private static void Render(string outDir)
        {
            _outDir = outDir;
            string pdf = Path.Combine(outDir, "Quarterly Report.pdf");
            VerificationRunner.CreateDummyPdf(pdf, 12);

            // Main window, every tab, at default and large sizes
            var main = new MainWindow { OfferExtras = false };
            main.Show();
            foreach (var (w, h) in new[] { (1020, 720), (1440, 900) })
            {
                main.Width = w;
                main.Height = h;
                foreach (var (tab, name) in new[] { (main.TabOverview, "overview"), (main.TabSettings, "settings"), (main.TabAbout, "about") })
                {
                    tab.IsChecked = true;
                    Save(main, Path.Combine(outDir, $"main_{name}_{w}.png"), null);
                }
            }
            main.TabOverview.IsChecked = true;

            // Dialogs
            Snap(() => new PdfSplitDialog(pdf), "dialog_split.png", d => d.RangesBox.Text = "1-3, 7, 10-");

            // Protect: PDF (with restrictions), Office, and a generic file
            Snap(() => new ProtectDialog(pdf), "dialog_protect_pdf.png", d =>
            {
                d.PasswordInput.Text = d.ConfirmInput.Text = "correct horse battery";
                d.AllowCopyCheck.IsChecked = false;
            });
            string docx = Path.Combine(outDir, "Proposal.docx");
            if (!File.Exists(docx)) File.WriteAllText(docx, "placeholder");
            Snap(() => new ProtectDialog(docx), "dialog_protect_office.png");
            string other = Path.Combine(outDir, "secrets.csv.bak");
            File.WriteAllText(other, "x");
            Snap(() => new ProtectDialog(other), "dialog_protect_file.png", d => d.PasswordInput.Text = "abc");

            string locked = Path.Combine(outDir, "Quarterly Report_locked.pdf");
            PdfToolsService.Protect(pdf, locked, "secret");
            Snap(() => new UnlockDialog(locked), "dialog_unlock.png");

            Snap(() => new ExtrasDialog(true, true), "dialog_extras.png");

            string appendix = pdf.Replace("Quarterly Report", "Appendix A");
            File.Copy(pdf, appendix, true);
            Snap(() => new PdfMergeDialog(new[] { pdf, appendix }), "dialog_merge.png");

            // Archive: default ZIP, 7z with a password (Advanced open), tar.zst; Protect as a 7z
            string album = Path.Combine(outDir, "Holiday Album");
            Directory.CreateDirectory(album);
            File.WriteAllText(Path.Combine(album, "notes.txt"), "notes");
            static void PickFormat(ArchiveDialog d, string key) =>
                d.FormatCombo.SelectedItem = d.FormatCombo.Items.OfType<ComboBoxItem>().First(i => (string?)i.Tag == key);
            Snap(() => new ArchiveDialog(album, null), "dialog_archive_zip.png");
            Snap(() => new ArchiveDialog(album, null), "dialog_archive_7z_password.png", d =>
            {
                PickFormat(d, "7z");
                d.AdvancedPanel.IsVisible = true;
                d.PasswordInput.Text = d.ConfirmInput.Text = "correct horse battery";
            });
            Snap(() => new ArchiveDialog(album, null), "dialog_archive_zst.png", d => PickFormat(d, "tar.zst"));
            Snap(() => new ProtectDialog(album), "dialog_protect_archive_7z.png", d =>
            {
                d.ArchiveFormatCombo.SelectedIndex = 1;
                d.PasswordInput.Text = d.ConfirmInput.Text = "correct horse battery";
            });

            // Compress dialog: every file kind, both modes, with real media so file details and GPU detection show
            var engine = new ConversionEngine();
            string? ffmpeg = engine.IsFFmpegAvailable ? engine.FFmpegPath : null;
            string png = Path.Combine(outDir, "Holiday photo.png");
            Try(() => VerificationRunner.CreateDummyImage(png), "the sample picture (no image backend)");
            string mp4 = Path.Combine(outDir, "Screen recording.mp4");
            string wav = Path.Combine(outDir, "Podcast episode.wav");
            if (ffmpeg != null)
            {
                Task.Run(() => MediaToolkit.RunAsync(ffmpeg, new[] { "-y", "-f", "lavfi", "-i", "testsrc2=s=1920x1080:r=60:d=3", "-f", "lavfi", "-i", "sine=d=3",
                                                                    "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", mp4 })).GetAwaiter().GetResult();
                Task.Run(() => MediaToolkit.RunAsync(ffmpeg, new[] { "-y", "-f", "lavfi", "-i", "sine=d=3", wav })).GetAwaiter().GetResult();
            }
            string pptx = Path.Combine(outDir, "Pitch.pptx");
            File.WriteAllText(pptx, "placeholder");
            string log = Path.Combine(outDir, "server.log");
            File.WriteAllText(log, "log");

            void SnapCompress(string file, string name, Action<CompressDialog>? setup = null)
            {
                if (!File.Exists(file)) return;
                Snap(() => new CompressDialog(file, null, ffmpeg), $"dialog_compress_{name}.png", setup, wait: TimeSpan.FromSeconds(4));
            }
            SnapCompress(png, "image_lossy");
            SnapCompress(png, "image_lossless", d => d.LosslessCard.IsChecked = true);
            SnapCompress(mp4, "video_lossy");
            SnapCompress(mp4, "video_target", d => ClickChip(d, "Discord · 10 MB"));
            SnapCompress(mp4, "video_lossless", d => d.LosslessCard.IsChecked = true);
            SnapCompress(wav, "audio_lossy", d => d.LossyCard.IsChecked = true);
            SnapCompress(wav, "audio_lossless");
            SnapCompress(pdf, "pdf_lossy");
            SnapCompress(pptx, "office_lossy");
            SnapCompress(log, "general");

            // Crop (image with a 16:9 selection, video) and Trim
            if (ffmpeg != null)
            {
                string photo = Path.Combine(outDir, "Landscape.png");
                Task.Run(() => MediaToolkit.RunAsync(ffmpeg, new[] { "-y", "-f", "lavfi", "-i", "testsrc2=s=1600x1000", "-frames:v", "1", photo })).GetAwaiter().GetResult();
                Snap(() => new CropDialog(photo, isVideo: false, ffmpeg), "dialog_crop_image.png",
                     d => d.RatioPanel.Children.OfType<RadioButton>().First(r => (string?)r.Content == "16:9").IsChecked = true, wait: TimeSpan.FromSeconds(3));

                // A long page (a README saved as one picture), zoomed to its width
                string page = Path.Combine(outDir, "Long page.png");
                Task.Run(() => MediaToolkit.RunAsync(ffmpeg, new[] { "-y", "-f", "lavfi", "-i", "testsrc2=s=980x6000", "-frames:v", "1", page })).GetAwaiter().GetResult();
                Snap(() => new CropDialog(page, isVideo: false, ffmpeg), "dialog_crop_long.png", d => d.Cropper.ZoomToWidth(), wait: TimeSpan.FromSeconds(3));
                Snap(() => new CropDialog(mp4, isVideo: true, ffmpeg), "dialog_crop_video.png",
                     d => d.Cropper.SetSelection(new Rect(400, 150, 900, 700)), wait: TimeSpan.FromSeconds(3));
                Snap(() => new TrimDialog(mp4, ffmpeg), "dialog_trim.png", d => d.Timeline.SetRange(0.8, 2.2), wait: TimeSpan.FromSeconds(3));
            }

            static void ClickChip(CompressDialog d, string label)
            {
                foreach (var chip in d.PresetPanel.Children.OfType<RadioButton>().ToList())
                {
                    if ((string?)chip.Content == label) chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }

            // Home with history and a loaded playground
            var okJob = new ConversionJob { SourcePath = pdf, TargetPath = pdf, Option = new WheelItem { Title = "PNG", Mode = WheelMode.Convert }, IsSuccess = true, OriginalSizeBytes = 1_200_000, ConvertedSizeBytes = 340_000, EndTime = DateTime.Now.AddSeconds(0.4), TargetFormat = "PNG" };
            okJob.StartTime = okJob.EndTime.Value.AddSeconds(-0.4);
            var failJob = new ConversionJob { SourcePath = pdf, Option = new WheelItem { Title = "Compress", Mode = WheelMode.Tools }, ErrorMessage = "This PDF is already compact; compression couldn't make it smaller.", TargetFormat = "Compress" };
            main.Width = 1080;
            main.Height = 720;
            var history = (ObservableCollection<ConversionJob>)main.HistoryList.ItemsSource!;
            history.Insert(0, failJob);
            history.Insert(0, okJob);
            history.Insert(0, new ConversionJob { SourcePath = Path.Combine(outDir, "holiday-photo.heic.jpg"), TargetPath = pdf, Option = new WheelItem { Title = "WEBP" }, TargetFormat = "WEBP", IsSuccess = true, OriginalSizeBytes = 4_800_000, ConvertedSizeBytes = 610_000 });
            main.EmptyHistoryNotice.IsVisible = false;
            main.TabOverview.IsChecked = true;
            Save(main, Path.Combine(outDir, "main_overview_history.png"), null);

            main.LoadFileIntoPlayground(pdf);
            Save(main, Path.Combine(outDir, "main_overview_playground.png"), null);

            // Every wheel template: the app in its colors
            string originalTemplate = TemplateManager.Instance.Current.Id;
            foreach (var template in TemplateManager.Instance.Templates.ToList())
            {
                TemplateManager.Instance.Apply(template.Id);
                Themes.AppThemeService.Apply(TemplateManager.Instance.Current);
                main.TabOverview.IsChecked = true;
                Save(main, Path.Combine(outDir, $"tpl_{template.Id}_home.png"), null);
                main.TabSettings.IsChecked = true;
                Save(main, Path.Combine(outDir, $"tpl_{template.Id}_settings.png"), null);
            }
            TemplateManager.Instance.Apply(originalTemplate);
            Themes.AppThemeService.Apply(TemplateManager.Instance.Current);
            main.Close();
        }

        /// <summary>Opens a dialog, lets it load, applies <paramref name="setup"/>, saves it over a dark backdrop and closes it.</summary>
        private static void Snap<T>(Func<T> create, string name, Action<T>? setup = null, TimeSpan? wait = null) where T : Window
        {
            T dialog;
            try { dialog = create(); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  skipped {name}: {ex.Message}");
                return;
            }
            dialog.Show();
            PumpFor(wait ?? TimeSpan.FromMilliseconds(300));
            setup?.Invoke(dialog);
            PumpFor(TimeSpan.FromMilliseconds(200));
            Save(dialog, Path.Combine(_outDir, name), Dark);
            dialog.Close();
        }

        /// <summary>Where <see cref="Snap{T}"/> saves.</summary>
        private static string _outDir = string.Empty;

        private static void Try(Action action, string what)
        {
            try { action(); }
            catch (Exception ex) { Console.Error.WriteLine($"  skipped {what}: {ex.Message}"); }
        }

        private static void Save(Window window, string path, Color? backdrop)
        {
            var background = window.Background;
            if (backdrop is { } color) window.Background = new SolidColorBrush(color);
            Pump();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.Background = background;
            Console.WriteLine($"  {Path.GetFileName(path)}");
        }

        private static void PumpFor(TimeSpan duration)
        {
            var until = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < until)
            {
                Pump();
                Thread.Sleep(30);
            }
        }

        private static void Pump() => Dispatcher.UIThread.RunJobs();
    }
}
