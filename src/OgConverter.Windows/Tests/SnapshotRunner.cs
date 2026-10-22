using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OgConverter.Models;
using OgConverter.Platform.Windows;
using OgConverter.Services;
using OgConverter.Views;

namespace OgConverter.Tests
{
    /// <summary>
    /// Dev aid: `og-converter.exe --snapshot &lt;dir&gt;` renders every screen to PNG for visual review.
    /// </summary>
    public static class SnapshotRunner
    {
        public static void Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            // Snapshots capture a single frame: no open/hover animations in flight
            Controls.RadialWheelCanvas.ReduceMotion = true;
            string pdf = Path.Combine(outDir, "Quarterly Report.pdf");
            VerificationRunner.CreateDummyPdf(pdf, 12);

            // Main window, every tab, at default and large sizes
            var main = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false, OfferExtras = false };
            main.Show();
            Controls.RadialWheelCanvas.ReduceMotion = true;
            foreach (var (w, h) in new[] { (1020, 720), (1440, 900) })
            {
                main.Width = w;
                main.Height = h;
                foreach (var (tab, name) in new[] { (main.TabOverview, "overview"), (main.TabSettings, "settings"), (main.TabAbout, "about") })
                {
                    tab.IsChecked = true;
                    Save((FrameworkElement)main.Content, Path.Combine(outDir, $"main_{name}_{w}.png"), null);
                }
            }
            main.TabOverview.IsChecked = true;

            // Wheel overlay in each mode with a hovered slice, over light and dark backdrops
            var overlay = new RadialWheelOverlay { Left = -20000, Top = -20000, ShowActivated = false };
            overlay.UpdateKeyLabels("Shift", "Control", "Alt");
            overlay.Show();
            foreach (var mode in new[] { WheelMode.Convert, WheelMode.Tools, WheelMode.Actions })
            {
                overlay.SwitchMode(mode);
                overlay.SetCurrentFile(pdf);
                overlay.WheelCanvas.HoveredIndex = 1;
                Save((FrameworkElement)overlay.Content, Path.Combine(outDir, $"wheel_{mode.ToString().ToLowerInvariant()}_dark.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
                Save((FrameworkElement)overlay.Content, Path.Combine(outDir, $"wheel_{mode.ToString().ToLowerInvariant()}_light.png"), Color.FromRgb(0xF3, 0xF4, 0xF6));
            }
            overlay.WheelCanvas.HoveredIndex = -1;
            overlay.SwitchMode(WheelMode.Convert);
            overlay.SetCurrentFile(pdf);
            Save((FrameworkElement)overlay.Content, Path.Combine(outDir, "wheel_idle_light.png"), Color.FromRgb(0xF3, 0xF4, 0xF6));
            overlay.SetCurrentFiles(new[] { pdf, pdf, pdf, pdf, pdf, pdf, pdf, pdf, pdf, pdf, pdf, pdf });
            Save((FrameworkElement)overlay.Content, Path.Combine(outDir, "wheel_batch_light.png"), Color.FromRgb(0xF3, 0xF4, 0xF6));
            overlay.SetCurrentFile(pdf);

            // Office document wheels
            foreach (string doc in new[] { "Proposal.docx", "Budget 2026.xlsx", "Pitch.pptx" })
            {
                string docPath = Path.Combine(outDir, doc);
                File.WriteAllText(docPath, "placeholder");
                overlay.SetCurrentFile(docPath);
                overlay.WheelCanvas.HoveredIndex = 0;
                Save((FrameworkElement)overlay.Content, Path.Combine(outDir, $"wheel_doc_{Path.GetExtension(doc).TrimStart('.')}.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            }

            // Smallest wheel size, idle and hovered, with the 8-option Actions wheel
            overlay.ApplyScale(0.6);
            overlay.SwitchMode(WheelMode.Actions);
            overlay.SetCurrentFile(pdf);
            Save((FrameworkElement)overlay.Content, Path.Combine(outDir, "wheel_60_idle.png"), Color.FromRgb(0xF3, 0xF4, 0xF6));
            overlay.SetCurrentFiles(new[] { pdf, pdf, pdf, pdf, pdf, pdf, pdf, pdf, pdf, pdf, pdf, pdf });
            Save((FrameworkElement)overlay.Content, Path.Combine(outDir, "wheel_60_batch.png"), Color.FromRgb(0xF3, 0xF4, 0xF6));
            overlay.SetCurrentFile(pdf);
            overlay.WheelCanvas.HoveredIndex = 6;
            Save((FrameworkElement)overlay.Content, Path.Combine(outDir, "wheel_60_hover.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            overlay.SwitchMode(WheelMode.Tools);
            overlay.SetCurrentFile(pdf);
            overlay.WheelCanvas.HoveredIndex = 1;
            Save((FrameworkElement)overlay.Content, Path.Combine(outDir, "wheel_60_tools.png"), Color.FromRgb(0xF3, 0xF4, 0xF6));
            overlay.ApplyScale(1.0);
            overlay.Close();

            // Dialogs
            var split = new PdfSplitDialog(pdf) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            split.Show();
            split.RangesBox.Text = "1-3, 7, 10-";
            Save((FrameworkElement)split.Content, Path.Combine(outDir, "dialog_split.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            split.Close();

            // Protect: PDF (with restrictions), Office, and a generic file
            var protect = new ProtectDialog(pdf) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            protect.Show();
            protect.PasswordInput.Password = "correct horse battery";
            protect.ConfirmInput.Password = "correct horse battery";
            protect.AllowCopyCheck.IsChecked = false;
            Save((FrameworkElement)protect.Content, Path.Combine(outDir, "dialog_protect_pdf.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            protect.Close();

            string docx = Path.Combine(outDir, "Proposal.docx");
            if (!File.Exists(docx)) File.WriteAllText(docx, "placeholder");
            var protectDoc = new ProtectDialog(docx) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            protectDoc.Show();
            Save((FrameworkElement)protectDoc.Content, Path.Combine(outDir, "dialog_protect_office.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            protectDoc.Close();

            string other = Path.Combine(outDir, "secrets.csv.bak");
            File.WriteAllText(other, "x");
            var protectOther = new ProtectDialog(other) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            protectOther.Show();
            protectOther.PasswordInput.Password = "abc";
            Save((FrameworkElement)protectOther.Content, Path.Combine(outDir, "dialog_protect_file.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            protectOther.Close();

            string locked = Path.Combine(outDir, "Quarterly Report_locked.pdf");
            PdfToolsService.Protect(pdf, locked, "secret");
            var unlockDlg = new UnlockDialog(locked) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            unlockDlg.Show();
            Save((FrameworkElement)unlockDlg.Content, Path.Combine(outDir, "dialog_unlock.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            unlockDlg.Close();

            var extras = new ExtrasDialog(true, true, (_, _) => Task.FromResult<string?>(null)) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            extras.Show();
            Save((FrameworkElement)extras.Content, Path.Combine(outDir, "dialog_extras.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            extras.Close();

            File.Copy(pdf, pdf.Replace("Quarterly Report", "Appendix A"), true);
            var merge = new PdfMergeDialog(new[] { pdf, pdf.Replace("Quarterly Report", "Appendix A") }) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            merge.Show();
            Save((FrameworkElement)merge.Content, Path.Combine(outDir, "dialog_merge.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            merge.Close();

            // Archive: default ZIP, 7z with a password (Advanced open), tar.zst with Guetzli pictures; Protect as a 7z
            string album = Path.Combine(outDir, "Holiday Album");
            Directory.CreateDirectory(album);
            File.WriteAllText(Path.Combine(album, "notes.txt"), "notes");
            void SnapArchive(string name, Action<ArchiveDialog>? setup = null)
            {
                var dlg = new ArchiveDialog(album, null) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                dlg.Show();
                Pump();
                setup?.Invoke(dlg);
                Pump();
                Save((FrameworkElement)dlg.Content, Path.Combine(outDir, name), Color.FromRgb(0x1E, 0x23, 0x2E));
                dlg.Close();
            }
            static void PickFormat(ArchiveDialog d, string key) =>
                d.FormatCombo.SelectedItem = d.FormatCombo.Items.Cast<System.Windows.Controls.ComboBoxItem>().First(i => (string)i.Tag == key);
            SnapArchive("dialog_archive_zip.png");
            SnapArchive("dialog_archive_7z_password.png", d =>
            {
                PickFormat(d, "7z");
                d.AdvancedPanel.Visibility = Visibility.Visible;
                d.PasswordInput.Password = d.ConfirmInput.Password = "correct horse battery";
            });
            SnapArchive("dialog_archive_zst.png", d => PickFormat(d, "tar.zst"));

            // Guetzli: a 36.6 MP photo, a folder of 12 MP photos (parallel jobs), and an 81 MP photo (over the free memory)
            void SnapGuetzli(string source, string name)
            {
                var dlg = new ArchiveDialog(source, new ArchiveOptions { Images = ImageRecompression.Guetzli }) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                dlg.Show();
                PumpFor(TimeSpan.FromSeconds(1.5)); // picture sizes are read in the background
                Save((FrameworkElement)dlg.Content, Path.Combine(outDir, name), Color.FromRgb(0x1E, 0x23, 0x2E));
                dlg.Close();
            }
            string GrayJpeg(string path, int w, int h)
            {
                if (!File.Exists(path)) ImageCodec.SaveJpeg(new WpfRasterImage(BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, new byte[w * h], w)), path, 90, null);
                return path;
            }
            SnapGuetzli(GrayJpeg(Path.Combine(outDir, "Big photo.jpg"), 8064, 4536), "dialog_archive_guetzli_photo.png");
            string shoot = Path.Combine(outDir, "Photo shoot");
            Directory.CreateDirectory(shoot);
            for (int n = 1; n <= 8; n++) GrayJpeg(Path.Combine(shoot, $"IMG_{n:0000}.jpg"), 4240, 2832);
            SnapGuetzli(shoot, "dialog_archive_guetzli_folder.png");
            SnapGuetzli(GrayJpeg(Path.Combine(outDir, "Panorama.jpg"), 9000, 9000), "dialog_archive_guetzli_too_big.png");

            var protectArchive = new ProtectDialog(album) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            protectArchive.Show();
            protectArchive.ArchiveFormatCombo.SelectedIndex = 1;
            protectArchive.PasswordInput.Password = protectArchive.ConfirmInput.Password = "correct horse battery";
            Pump();
            Save((FrameworkElement)protectArchive.Content, Path.Combine(outDir, "dialog_protect_archive_7z.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            protectArchive.Close();

            // Compress dialog: every file kind, both modes, with real media so file details and GPU detection show
            string? ffmpeg = ConversionEngine.FindFFmpeg();
            string png = Path.Combine(outDir, "Holiday photo.png");
            VerificationRunner.CreateDummyImage(png);
            string mp4 = Path.Combine(outDir, "Screen recording.mp4");
            string wav = Path.Combine(outDir, "Podcast episode.wav");
            if (ffmpeg != null)
            {
                Task.Run(() => MediaToolkit.RunAsync(ffmpeg, new[] { "-f", "lavfi", "-i", "testsrc2=s=1920x1080:r=60:d=3", "-f", "lavfi", "-i", "sine=d=3",
                                                                    "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", mp4 })).GetAwaiter().GetResult();
                Task.Run(() => MediaToolkit.RunAsync(ffmpeg, new[] { "-f", "lavfi", "-i", "sine=d=3", wav })).GetAwaiter().GetResult();
            }
            string pptx = Path.Combine(outDir, "Pitch.pptx");
            File.WriteAllText(pptx, "placeholder");
            string log = Path.Combine(outDir, "server.log");
            File.WriteAllText(log, "log");

            void SnapCompress(string file, string name, Action<CompressDialog>? setup = null)
            {
                if (!File.Exists(file)) return;
                var dlg = new CompressDialog(file, null, ffmpeg) { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                dlg.Show();
                PumpFor(TimeSpan.FromSeconds(4)); // background inspection (probe, GPU detection)
                setup?.Invoke(dlg);
                Save((FrameworkElement)dlg.Content, Path.Combine(outDir, $"dialog_compress_{name}.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
                dlg.Close();
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
            void SnapWindow(Window dlg, string name, Action? setup = null)
            {
                dlg.Left = -20000;
                dlg.Top = -20000;
                dlg.ShowActivated = false;
                dlg.WindowStartupLocation = WindowStartupLocation.Manual;
                dlg.Show();
                PumpFor(TimeSpan.FromSeconds(3)); // preview frames load in the background
                setup?.Invoke();
                PumpFor(TimeSpan.FromSeconds(1));
                Save((FrameworkElement)dlg.Content, Path.Combine(outDir, $"dialog_{name}.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
                dlg.Close();
            }

            if (ffmpeg != null)
            {
                string photo = Path.Combine(outDir, "Landscape.png");
                Task.Run(() => MediaToolkit.RunAsync(ffmpeg, new[] { "-f", "lavfi", "-i", "testsrc2=s=1600x1000", "-frames:v", "1", photo })).GetAwaiter().GetResult();
                var crop = new CropDialog(photo, isVideo: false, ffmpeg);
                SnapWindow(crop, "crop_image", () => crop.RatioPanel.Children.OfType<RadioButton>().First(r => (string)r.Content == "16:9").IsChecked = true);

                // A long page (a README saved as one picture), zoomed to its width
                string page = Path.Combine(outDir, "Long page.png");
                Task.Run(() => MediaToolkit.RunAsync(ffmpeg, new[] { "-f", "lavfi", "-i", "testsrc2=s=980x6000", "-frames:v", "1", page })).GetAwaiter().GetResult();
                var cropLong = new CropDialog(page, isVideo: false, ffmpeg);
                SnapWindow(cropLong, "crop_long", () => cropLong.Cropper.ZoomToWidth());

                var cropVideo = new CropDialog(mp4, isVideo: true, ffmpeg);
                SnapWindow(cropVideo, "crop_video", () => cropVideo.Cropper.SetSelection(new Rect(400, 150, 900, 700)));

                var trim = new TrimDialog(mp4, ffmpeg);
                SnapWindow(trim, "trim", () => trim.Timeline.SetRange(0.8, 2.2));
            }

            static void ClickChip(CompressDialog d, string label)
            {
                foreach (var chip in d.PresetPanel.Children.OfType<RadioButton>().ToList())
                {
                    if ((string)chip.Content == label) chip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }
            }

            // Notifications
            var okJob = new ConversionJob { SourcePath = pdf, TargetPath = pdf, Option = new WheelItem { Title = "PNG", Mode = WheelMode.Convert }, IsSuccess = true, OriginalSizeBytes = 1_200_000, ConvertedSizeBytes = 340_000, EndTime = DateTime.Now.AddSeconds(0.4) };
            okJob.StartTime = okJob.EndTime.Value.AddSeconds(-0.4);
            var toast = new ToastNotificationWindow();
            toast.ShowJob(okJob);
            Save((FrameworkElement)toast.Content, Path.Combine(outDir, "toast_success.png"), Color.FromRgb(0xF3, 0xF4, 0xF6));
            toast.Close();

            var failJob = new ConversionJob { SourcePath = pdf, Option = new WheelItem { Title = "Compress", Mode = WheelMode.Tools }, ErrorMessage = "This PDF is already compact; compression couldn't make it smaller." };
            toast = new ToastNotificationWindow();
            toast.ShowJob(failJob);
            Save((FrameworkElement)toast.Content, Path.Combine(outDir, "toast_failure.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            toast.Close();

            // Batch: one toast for every file, with Cancel while it runs
            toast = new ToastNotificationWindow();
            toast.ShowProgress("Converting 10 files to MP4…", pdf, () => { });
            toast.ReportBatch("Converting 10 files to MP4…", "4 of 10 done  ·  2 running  ·  1 failed", 0.47);
            Save((FrameworkElement)toast.Content, Path.Combine(outDir, "toast_batch_progress.png"), Color.FromRgb(0xF3, 0xF4, 0xF6));
            toast.ShowBatchResult(false, "8 of 10 files converted to MP4", "1 failed  ·  1 cancelled", "video_03.mov: FFmpeg failed (exit code 1): Invalid data found when processing input", outDir);
            Save((FrameworkElement)toast.Content, Path.Combine(outDir, "toast_batch_partial.png"), Color.FromRgb(0x1E, 0x23, 0x2E));
            toast.Close();

            // Home with history and a loaded playground
            main.Show();
            main.Width = 1080; main.Height = 720;
            var history = (System.Collections.ObjectModel.ObservableCollection<ConversionJob>)main.HistoryList.ItemsSource;
            history.Insert(0, failJob);
            history.Insert(0, okJob);
            history.Insert(0, new ConversionJob { SourcePath = Path.Combine(outDir, "holiday-photo.heic.jpg"), TargetPath = pdf, Option = new WheelItem { Title = "WEBP" }, TargetFormat = "WEBP", IsSuccess = true, OriginalSizeBytes = 4_800_000, ConvertedSizeBytes = 610_000 });
            okJob.TargetFormat = "PNG"; failJob.TargetFormat = "Compress";
            main.HistoryList.Items.Refresh();
            main.EmptyHistoryNotice.Visibility = Visibility.Collapsed;
            main.TabOverview.IsChecked = true;
            Save((FrameworkElement)main.Content, Path.Combine(outDir, "main_overview_history.png"), null);

            // A batch in Recent activity: running, waiting for a slot, and being cancelled
            var mp4Item = new WheelItem { Title = "MP4", Mode = WheelMode.Convert };
            var cancelling = new ConversionJob { SourcePath = Path.Combine(outDir, "video_04.mov"), Option = mp4Item, TargetFormat = "MP4", IsRunning = true, Cancellation = new System.Threading.CancellationTokenSource() };
            var queued = new ConversionJob { SourcePath = Path.Combine(outDir, "video_03.mov"), Option = mp4Item, TargetFormat = "MP4", IsRunning = true, IsQueued = true, Cancellation = new System.Threading.CancellationTokenSource() };
            var running2 = new ConversionJob { SourcePath = Path.Combine(outDir, "video_02.mov"), Option = mp4Item, TargetFormat = "MP4", IsRunning = true, Cancellation = new System.Threading.CancellationTokenSource() };
            var running1 = new ConversionJob { SourcePath = Path.Combine(outDir, "video_01.mov"), Option = mp4Item, TargetFormat = "MP4", IsRunning = true, Cancellation = new System.Threading.CancellationTokenSource() };
            running1.Progress = 0.62;
            running2.Progress = 0.18;
            cancelling.Progress = 0.3;
            cancelling.Cancel();
            foreach (var j in new[] { cancelling, queued, running2, running1 }) history.Insert(0, j);
            main.CancelAllButton.Visibility = Visibility.Visible;
            Save((FrameworkElement)main.Content, Path.Combine(outDir, "main_overview_batch.png"), null);
            foreach (var j in new[] { cancelling, queued, running2, running1 }) history.Remove(j);
            main.CancelAllButton.Visibility = Visibility.Collapsed;

            main.LoadFileIntoPlayground(pdf);
            main.PlaygroundWheel.HoveredIndex = 2;
            Save((FrameworkElement)main.Content, Path.Combine(outDir, "main_overview_playground.png"), null);

            // Every wheel template: the app in its colors, each wheel, and the smallest size
            Controls.RadialWheelCanvas.ReduceMotion = true; // MainWindow applied the user's setting
            string originalTemplate = TemplateManager.Instance.Current.Id;
            var wheel = new RadialWheelOverlay { Left = -20000, Top = -20000, ShowActivated = false };
            wheel.UpdateKeyLabels("Shift", "Control", "Alt");
            wheel.Show();
            foreach (var template in TemplateManager.Instance.Templates.ToList())
            {
                TemplateManager.Instance.Apply(template.Id);
                string id = template.Id;
                main.TabOverview.IsChecked = true;
                Save((FrameworkElement)main.Content, Path.Combine(outDir, $"tpl_{id}_home.png"), null);
                main.TabSettings.IsChecked = true;
                Save((FrameworkElement)main.Content, Path.Combine(outDir, $"tpl_{id}_settings.png"), null);

                wheel.ApplyScale(1.0, true);
                foreach (var mode in new[] { WheelMode.Convert, WheelMode.Tools, WheelMode.Actions })
                {
                    wheel.SwitchMode(mode);
                    wheel.SetCurrentFile(pdf);
                    wheel.WheelCanvas.HoveredIndex = 1;
                    Save((FrameworkElement)wheel.Content, Path.Combine(outDir, $"tpl_{id}_wheel_{mode.ToString().ToLowerInvariant()}.png"), Color.FromRgb(0x5A, 0x6B, 0x86));
                }
                wheel.WheelCanvas.HoveredIndex = -1;
                Save((FrameworkElement)wheel.Content, Path.Combine(outDir, $"tpl_{id}_wheel_idle_light.png"), Color.FromRgb(0xF3, 0xF4, 0xF6));

                wheel.ApplyScale(0.4, true);
                wheel.SwitchMode(WheelMode.Convert);
                wheel.SetCurrentFile(pdf);
                wheel.WheelCanvas.HoveredIndex = 2;
                Save((FrameworkElement)wheel.Content, Path.Combine(outDir, $"tpl_{id}_wheel_40.png"), Color.FromRgb(0x5A, 0x6B, 0x86));
                wheel.ApplyScale(0.4, false);
                Save((FrameworkElement)wheel.Content, Path.Combine(outDir, $"tpl_{id}_wheel_40_nohints.png"), Color.FromRgb(0x5A, 0x6B, 0x86));
            }
            wheel.Close();
            TemplateManager.Instance.Apply(originalTemplate);
            main.TabOverview.IsChecked = true;

            main.Close();
        }

        private static void Save(FrameworkElement element, string path, Color? backdrop)
        {
            Pump();
            element.UpdateLayout();
            Pump();

            int w = (int)Math.Ceiling(element.ActualWidth);
            int h = (int)Math.Ceiling(element.ActualHeight);
            if (w <= 0 || h <= 0) return;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (backdrop.HasValue) dc.DrawRectangle(new SolidColorBrush(backdrop.Value), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, w, h));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        private static void PumpFor(TimeSpan duration)
        {
            var until = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < until)
            {
                Pump();
                System.Threading.Thread.Sleep(30);
            }
        }

        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
