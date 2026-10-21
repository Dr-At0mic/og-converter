using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// Compress any file: pick Lossless or Lossy, start from a preset, then fine-tune format, quality, size and
    /// the options that matter for that kind of file. The summary line always says exactly what will happen.
    /// </summary>
    public partial class CompressDialog : Window
    {
        private readonly string _path;
        private readonly string _ext;
        private readonly CompressionKind _kind;
        private readonly string? _ffmpeg;
        private CompressionOptions _o;

        // Learned in the background after opening
        private MediaInfo? _media;
        private LoadedImage? _image;
        private HashSet<string> _workingHardware = new();
        private HashSet<string> _encoders = new();

        private readonly List<(RadioButton Chip, Func<CompressionOptions, bool> IsActive)> _presetChips = new();
        private bool _building;

        public CompressionOptions? Result { get; private set; }

        public CompressDialog(string path, CompressionOptions? initial, string? ffmpeg)
        {
            InitializeComponent();
            _path = path;
            _ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            _kind = CompressionService.KindOf(path) ?? CompressionKind.General;
            _ffmpeg = ffmpeg;
            _o = CompressionService.Normalize(_kind, path, initial ?? CompressionService.Defaults(_kind, path));

            HeaderText.Text = "Compress " + _kind switch
            {
                CompressionKind.Image => "image",
                CompressionKind.AnimatedGif => "GIF",
                CompressionKind.Svg => "SVG",
                CompressionKind.Video => "video",
                CompressionKind.Audio => "audio",
                CompressionKind.Pdf => "PDF",
                CompressionKind.Office => "document",
                CompressionKind.Archive => "archive",
                _ => "file"
            };
            SetFileInfo(null);
            FileInfoText.ToolTip = path;

            (LosslessHint.Text, LossyHint.Text) = _kind switch
            {
                CompressionKind.Image => ("Pixel-identical. Optimized encoding, metadata removed.", "Much smaller. WebP, AVIF, JPEG XL or fewer colors."),
                CompressionKind.AnimatedGif => ("Every frame identical, as animated WebP.", "Fewer colors and frames, smaller size."),
                CompressionKind.Video => ("No quality change. Remux, or archival codecs.", "Re-encode with a modern codec. Huge savings."),
                CompressionKind.Audio => ("Bit-perfect audio (FLAC / ALAC, or tags removed).", "Opus, AAC or MP3 at the bitrate you choose."),
                CompressionKind.Pdf => ("Text and images untouched. Structure optimized.", "Images downsampled and re-encoded (like Acrobat)."),
                CompressionKind.Office => ("Package re-zipped, PNGs optimized losslessly.", "Pictures resized like Office's Compress Pictures."),
                _ => ("Exact copy back when decompressed.", "")
            };
            if (!CompressionService.IsLossyAvailable(_kind))
            {
                LossyCard.IsEnabled = false;
                LossyHint.Text = "Not possible for this file type: every byte matters.";
            }

            Loaded += async (_, _) =>
            {
                BodyScroller.MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 220);
                Activate();
                _building = true;
                (_o.Mode == CompressionMode.Lossless ? LosslessCard : LossyCard).IsChecked = true;
                _building = false;
                Rebuild();
                await InspectAsync();
            };
        }

        /// <summary>Learns about the file (dimensions, duration, transparency, working GPU encoders) without blocking the dialog.</summary>
        private async Task InspectAsync()
        {
            try
            {
                switch (_kind)
                {
                    case CompressionKind.Image:
                        _image = await Task.Run(() => ImageCodec.TryLoad(_path, applyOrientation: true, maxDimension: 0));
                        break;
                    case CompressionKind.Video or CompressionKind.Audio or CompressionKind.AnimatedGif when _ffmpeg != null:
                        _media = await MediaToolkit.ProbeAsync(_ffmpeg, _path);
                        break;
                }
                if (_ffmpeg != null && _kind is CompressionKind.Video or CompressionKind.Image or CompressionKind.AnimatedGif or CompressionKind.Audio)
                {
                    _encoders = await MediaToolkit.GetEncodersAsync(_ffmpeg);
                }
                SetFileInfo(null);
                Rebuild();

                if (_kind == CompressionKind.Video && _ffmpeg != null)
                {
                    _workingHardware = await MediaToolkit.GetWorkingHardwareEncodersAsync(_ffmpeg);
                    Rebuild();
                }
            }
            catch
            {
                // Inspection is a nicety; the dialog works without it
            }
        }

        private void SetFileInfo(string? extra)
        {
            var parts = new List<string> { Path.GetFileName(_path) };
            try { parts.Add(FormatSize(new FileInfo(_path).Length)); } catch { }
            if (_image != null) parts.Add($"{_image.Bitmap.Width} × {_image.Bitmap.Height}");
            if (_media != null)
            {
                if (_media.HasVideo && _media.Width > 0) parts.Add($"{_media.Width} × {_media.Height}");
                if (_media.DurationSeconds > 0) parts.Add(FormatDuration(_media.DurationSeconds));
                if (_media.HasVideo && _media.Fps > 0) parts.Add($"{_media.Fps:0.##} fps");
                if (_kind == CompressionKind.Audio && _media.AudioKbps > 0) parts.Add($"{_media.AudioKbps} kbps");
            }
            if (extra != null) parts.Add(extra);
            FileInfoText.Text = string.Join("  •  ", parts);
        }

        // ---------- Building the settings UI ----------

        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            if (_building || !IsLoaded) return;
            var mode = LosslessCard.IsChecked == true ? CompressionMode.Lossless : CompressionMode.Lossy;
            if (mode == _o.Mode) return;
            // Each mode has its own sensible format; keep the rest of the choices
            _o.Mode = mode;
            var choices = Choices();
            if (!choices.Any(c => c.Key == _o.Format))
            {
                var def = CompressionService.Defaults(_kind, _path);
                _o.Format = def.Mode == mode && choices.Any(c => c.Key == def.Format) ? def.Format : choices.FirstOrDefault().Key ?? "keep";
            }
            Rebuild();
        }

        private List<(string Key, string Label)> Choices() =>
            CompressionService.FormatChoices(_kind, _path, _o.Mode, _image?.HasAlpha == true);

        private void Rebuild()
        {
            _building = true;
            try
            {
                SettingsPanel.Children.Clear();
                PresetPanel.Children.Clear();
                _presetChips.Clear();

                bool lossy = _o.Mode == CompressionMode.Lossy;
                switch (_kind)
                {
                    case CompressionKind.Image: BuildImage(lossy); break;
                    case CompressionKind.AnimatedGif: BuildGif(lossy); break;
                    case CompressionKind.Video: BuildVideo(lossy); break;
                    case CompressionKind.Audio: BuildAudio(lossy); break;
                    case CompressionKind.Pdf: BuildPdf(lossy); break;
                    case CompressionKind.Office: BuildOffice(lossy); break;
                    default: BuildGeneral(); break;
                }
                PresetSection.Visibility = _presetChips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                _building = false;
            }
            Refresh();
        }

        private void BuildImage(bool lossy)
        {
            if (lossy)
            {
                Preset("Max quality", o => { o.Quality = 90; o.TargetSizeMb = 0; }, o => o.Quality == 90 && o.TargetSizeMb == 0);
                Preset("High", o => { o.Quality = 80; o.TargetSizeMb = 0; }, o => o.Quality == 80 && o.TargetSizeMb == 0);
                Preset("Balanced", o => { o.Quality = 70; o.TargetSizeMb = 0; }, o => o.Quality == 70 && o.TargetSizeMb == 0);
                Preset("Small", o => { o.Quality = 55; o.TargetSizeMb = 0; }, o => o.Quality == 55 && o.TargetSizeMb == 0);
                Preset("Tiny", o => { o.Quality = 40; o.TargetSizeMb = 0; }, o => o.Quality == 40 && o.TargetSizeMb == 0);
                if (_o.Format != "guetzli") SizePresets();
            }

            FormatCombo("Format");
            if (_o.Format == "guetzli" && !GuetzliService.IsAvailable) DownloadGuetzliButton();
            if (lossy)
            {
                bool palette = _o.Format == "keep" && _ext == "png";
                bool bySize = _o.Format != "guetzli" && SizeControl(palette ? "Colors" : "Quality");
                if (!bySize) QualitySlider(palette ? "Colors" : "Quality", q => palette ? $"{CompressionService.PaletteColors(q)} colors" : FormatImageQuality(q));
                ResizeCombo(_image?.Bitmap is { } b ? Math.Max(b.Width, b.Height) : 0);
                if (bySize) Note("Tries qualities from the best down and keeps the best one that fits. The picture is made smaller only when even a low quality doesn't fit.");
            }
            if (_o.Format == "guetzli" && GuetzliService.IsAvailable && lossy && GuetzliMegapixels() is { } megapixels)
            {
                var specs = GuetzliService.Specs();
                if (megapixels > GuetzliService.SafeMegapixels(specs) || _o.GuetzliAllowLarge)
                {
                    Check("Do it anyway, even if it needs more than the free memory", _o.GuetzliAllowLarge, v => { _o.GuetzliAllowLarge = v; Dispatcher.BeginInvoke(Rebuild); });
                    Note($"No admin rights are needed: Windows lends apps all the memory it has, then the page file. Resizing (Longest edge) makes it smaller and much faster.");
                }
                // The bar shows what it needs even when that's more than is free
                SettingsPanel.Children.Add(GuetzliResourcePanel.Build(this, GuetzliService.Plan(new[] { megapixels }, 1, true, specs)));
            }
            Check("Remove metadata (GPS location, camera details, editing history)", _o.StripMetadata, v => _o.StripMetadata = v);
        }

        private void BuildGif(bool lossy)
        {
            if (lossy)
            {
                Preset("High", o => { o.Quality = 85; o.MaxFps = 0; }, o => o.Quality == 85 && o.MaxFps == 0);
                Preset("Balanced", o => { o.Quality = 65; o.MaxFps = 0; }, o => o.Quality == 65 && o.MaxFps == 0);
                Preset("Small", o => { o.Quality = 45; o.MaxFps = 15; }, o => o.Quality == 45 && o.MaxFps == 15);
                Preset("Chat sticker", o => { o.Quality = 40; o.MaxFps = 15; o.MaxDimension = 480; }, o => o.Quality == 40 && o.MaxFps == 15 && o.MaxDimension == 480);
            }
            FormatCombo("Output");
            if (lossy)
            {
                if (!SizeControl(_o.Format == "gif" ? "Colors" : "Quality"))
                    QualitySlider(_o.Format == "gif" ? "Colors" : "Quality", q => _o.Format == "gif" ? $"{CompressionService.PaletteColors(q)} colors" : FormatImageQuality(q));
                ResizeCombo(0);
                Combo("Frame rate", new (object, string)[] { (0, "Original"), (30, "30 fps"), (24, "24 fps"), (20, "20 fps"), (15, "15 fps"), (10, "10 fps") },
                    _o.MaxFps, v => _o.MaxFps = (int)v);
            }
            else
            {
                Note("Animated WebP keeps every frame and color exactly, usually at a fraction of the GIF's size. It plays in browsers, chat apps and Windows Photos.");
            }
        }

        private void BuildVideo(bool lossy)
        {
            if (!lossy)
            {
                FormatCombo("Method");
                if (_o.Format is "x264" or "x265") SpeedCombo();
                Check("Remove metadata (location, device, titles)", _o.StripMetadata, v => _o.StripMetadata = v);
                Note(_o.Format == "remux"
                    ? "Copies the video and audio exactly as they are into a fresh container, dropping junk data and moving the index to the front for faster streaming. Instant, but savings are small."
                    : "True lossless re-encoding only shrinks near-uncompressed sources (screen recordings, ProRes, DNxHD, raw AVI). A normal MP4 will usually get bigger. Use Lossy at high quality for a visually identical, much smaller file.");
                return;
            }

            Preset("High quality", o => { o.TargetSizeMb = 0; o.Quality = 75; o.MaxHeight = 0; o.Speed = 2; }, o => o.TargetSizeMb == 0 && o.Quality == 75 && o.MaxHeight == 0 && o.Speed == 2);
            Preset("Balanced", o => { o.TargetSizeMb = 0; o.Quality = 60; o.MaxHeight = 0; o.Speed = 1; }, o => o.TargetSizeMb == 0 && o.Quality == 60 && o.MaxHeight == 0 && o.Speed == 1);
            Preset("Small", o => { o.TargetSizeMb = 0; o.Quality = 45; o.MaxHeight = 1080; o.Speed = 2; }, o => o.TargetSizeMb == 0 && o.Quality == 45 && o.MaxHeight == 1080 && o.Speed == 2);
            Preset("Discord · 10 MB", o => { o.Format = "h264"; o.TargetSizeMb = 10; o.MaxHeight = 720; o.MaxFps = 30; o.VideoAudioKbps = 96; },
                o => o.TargetSizeMb == 10 && o.Format == "h264" && o.MaxHeight == 720);
            Preset("WhatsApp · 16 MB", o => { o.Format = "h264"; o.TargetSizeMb = 15; o.MaxHeight = 720; o.MaxFps = 30; o.VideoAudioKbps = 96; },
                o => o.TargetSizeMb == 15 && o.Format == "h264" && o.MaxHeight == 720);
            Preset("Email · 25 MB", o => { o.Format = "h264"; o.TargetSizeMb = 24; o.MaxHeight = 1080; o.VideoAudioKbps = 128; },
                o => o.TargetSizeMb == 24 && o.Format == "h264" && o.MaxHeight == 1080);

            FormatCombo("Codec");

            // Rate control: quality (constant quality) or a target file size (two-pass where the encoder supports it)
            if (!SizeControl("Constant quality"))
            {
                QualitySlider("Quality", q => _o.Format switch
                {
                    "h265" => $"{q}  (CRF {CompressionService.X265Crf(q)})",
                    "av1" => $"{q}  (CRF {CompressionService.Av1Crf(q)})",
                    "vp9" => $"{q}  (CRF {CompressionService.Vp9Crf(q)})",
                    _ => $"{q}  (CRF {CompressionService.X264Crf(q)})"
                });
            }

            var resolutions = new List<(object, string)> { (0, "Original") };
            foreach (var (h, name) in new[] { (2160, "4K (2160p)"), (1440, "1440p"), (1080, "1080p"), (720, "720p"), (480, "480p"), (360, "360p") })
            {
                int shortEdge = _media is { Width: > 0 } m ? Math.Min(m.Width, m.Height) : int.MaxValue;
                if (h < shortEdge || (int)_o.MaxHeight == h) resolutions.Add((h, name));
            }
            Combo("Resolution", resolutions, _o.MaxHeight, v => _o.MaxHeight = (int)v);

            var rates = new List<(object, string)> { (0, "Original") };
            foreach (int f in new[] { 60, 30, 24 })
            {
                if (_media == null || _media.Fps <= 0 || _media.Fps > f + 0.5 || _o.MaxFps == f) rates.Add((f, $"{f} fps"));
            }
            Combo("Frame rate", rates, _o.MaxFps, v => _o.MaxFps = (int)v);

            SpeedCombo();

            string audioCodec = _o.Format == "vp9" ? "Opus" : "AAC";
            Combo("Audio", new (object, string)[]
            {
                (0, "Keep original"), (192, $"{audioCodec} 192 kbps"), (160, $"{audioCodec} 160 kbps"), (128, $"{audioCodec} 128 kbps"),
                (96, $"{audioCodec} 96 kbps"), (64, $"{audioCodec} 64 kbps"), (-1, "Remove audio")
            }, _o.VideoAudioKbps, v => _o.VideoAudioKbps = (int)v);

            string? gpu = HardwareEncoderFor(_o.Format);
            var gpuCheck = Check(gpu != null
                    ? $"Use the GPU encoder ({DescribeHardware(gpu)}): many times faster, slightly larger files"
                    : _o.Format == "vp9" ? "GPU encoding isn't available for VP9" : "GPU encoding: no compatible graphics encoder found for this codec",
                _o.UseHardwareEncoder && gpu != null, v => _o.UseHardwareEncoder = v);
            gpuCheck.IsEnabled = gpu != null;

            Check("Remove metadata (location, device, titles)", _o.StripMetadata, v => _o.StripMetadata = v);
        }

        private void BuildAudio(bool lossy)
        {
            if (!lossy)
            {
                FormatCombo("Format");
                if (_o.Format != "keep") Check("Remove tags and cover art", _o.StripMetadata, v => _o.StripMetadata = v);
                Note(_o.Format == "keep"
                    ? "This file is already in a lossy format, so the audio is copied untouched and only tags and embedded artwork are removed. For a real size reduction, use Lossy."
                    : "Decodes to exactly the same samples as the original. FLAC is the standard for lossless music; ALAC plays natively in Apple Music and on iPhone.");
                return;
            }

            // Presets adapt to the chosen codec: each codec needs a different bitrate for the same quality
            (int Opus, int Aac, int Mp3)[] levels = { (160, 256, 245), (128, 192, 190), (96, 128, 130), (64, 64, 65), (32, 48, 65) };
            string[] names = { "Transparent", "High", "Standard", "Voice / podcast", "Smallest" };
            for (int i = 0; i < levels.Length; i++)
            {
                var l = levels[i];
                bool mono = i >= 3;
                Preset(names[i], o => { o.AudioKbps = Pick(o.Format, l); o.Mono = mono; o.TargetSizeMb = 0; }, o => o.AudioKbps == Pick(o.Format, l) && o.Mono == mono && o.TargetSizeMb == 0);
            }

            FormatCombo("Codec");
            if (!SizeControl("Bitrate"))
            {
                var bitrates = (_o.Format == "opus" ? new[] { 256, 192, 160, 128, 112, 96, 80, 64, 48, 32, 24 }
                              : _o.Format == "mp3" ? new[] { 245, 225, 190, 175, 165, 130, 115, 100, 85, 65 }
                              : new[] { 320, 256, 192, 160, 128, 112, 96, 80, 64, 48 })
                    .Select(k => ((object)k, _o.Format == "mp3" ? $"≈{k} kbps (V{CompressionService.LameVbrLevel(k)})" : $"{k} kbps")).ToList();
                if (!bitrates.Any(b => (int)b.Item1 == _o.AudioKbps))
                {
                    int nearest = bitrates.Select(b => (int)b.Item1).OrderBy(k => Math.Abs(k - _o.AudioKbps)).First();
                    _o.AudioKbps = nearest;
                }
                Combo("Bitrate", bitrates, _o.AudioKbps, v => _o.AudioKbps = (int)v);
            }
            Combo("Channels", new (object, string)[] { (false, "Keep original"), (true, "Mono (half the data, fine for speech)") }, _o.Mono, v => _o.Mono = (bool)v);
            if (_o.Format != "opus")
            {
                Combo("Sample rate", new (object, string)[] { (0, "Keep original"), (48000, "48 kHz"), (44100, "44.1 kHz"), (32000, "32 kHz"), (22050, "22.05 kHz (speech)") },
                    _o.SampleRate, v => _o.SampleRate = (int)v);
            }
            Check("Remove tags and cover art", _o.StripMetadata, v => _o.StripMetadata = v);

            static int Pick(string format, (int Opus, int Aac, int Mp3) l) => format switch { "opus" => l.Opus, "mp3" => l.Mp3, _ => l.Aac };
        }

        private void BuildPdf(bool lossy)
        {
            if (!lossy)
            {
                Check("Remove metadata (author, title, creator app)", _o.StripMetadata, v => _o.StripMetadata = v);
                Note("Re-compresses every stream at maximum, compresses uncompressed fonts and content, and drops page thumbnails and unused objects. Text, vectors and images stay exactly the same.");
                return;
            }

            Preset("Screen · 72 dpi", o => { o.PdfDpi = 72; o.Quality = 60; o.TargetSizeMb = 0; }, o => o.PdfDpi == 72 && o.Quality == 60 && o.TargetSizeMb == 0);
            Preset("eBook · 150 dpi", o => { o.PdfDpi = 150; o.Quality = 75; o.TargetSizeMb = 0; }, o => o.PdfDpi == 150 && o.Quality == 75 && o.TargetSizeMb == 0);
            Preset("Print · 300 dpi", o => { o.PdfDpi = 300; o.Quality = 85; o.TargetSizeMb = 0; }, o => o.PdfDpi == 300 && o.Quality == 85 && o.TargetSizeMb == 0);
            Preset("Archive · 300 dpi", o => { o.PdfDpi = 300; o.Quality = 92; o.TargetSizeMb = 0; }, o => o.PdfDpi == 300 && o.Quality == 92 && o.TargetSizeMb == 0);
            SizePresets();

            if (!SizeControl("Resolution and quality"))
            {
                Combo("Image resolution", new (object, string)[]
                {
                    (72, "72 dpi (screen, smallest)"), (96, "96 dpi"), (120, "120 dpi"), (150, "150 dpi (ebook, readable when zoomed)"),
                    (200, "200 dpi"), (300, "300 dpi (print)")
                }, _o.PdfDpi, v => _o.PdfDpi = (int)v);
                QualitySlider("JPEG quality", FormatImageQuality);
            }
            Check("Convert images to grayscale", _o.Grayscale, v => _o.Grayscale = v);
            Check("Remove metadata (author, title, creator app)", _o.StripMetadata, v => _o.StripMetadata = v);
            Note("Text and vector graphics stay sharp; only photos and scans are resampled. Masks and special color spaces are left untouched.");
        }

        private void BuildOffice(bool lossy)
        {
            if (lossy)
            {
                Preset("Email · 96 ppi", o => { o.MaxDimension = 1280; o.Quality = 70; o.TargetSizeMb = 0; }, o => o.MaxDimension == 1280 && o.Quality == 70 && o.TargetSizeMb == 0);
                Preset("Web · 150 ppi", o => { o.MaxDimension = 1920; o.Quality = 80; o.TargetSizeMb = 0; }, o => o.MaxDimension == 1920 && o.Quality == 80 && o.TargetSizeMb == 0);
                Preset("Print · 220 ppi", o => { o.MaxDimension = 2800; o.Quality = 85; o.TargetSizeMb = 0; }, o => o.MaxDimension == 2800 && o.Quality == 85 && o.TargetSizeMb == 0);
                Preset("HD · 330 ppi", o => { o.MaxDimension = 4000; o.Quality = 90; o.TargetSizeMb = 0; }, o => o.MaxDimension == 4000 && o.Quality == 90 && o.TargetSizeMb == 0);
                SizePresets();

                if (!SizeControl("Picture size and quality"))
                {
                    Combo("Largest picture size", new (object, string)[]
                    {
                        (1024, "1024 px"), (1280, "1280 px (email)"), (1600, "1600 px"), (1920, "1920 px (web, screens)"),
                        (2560, "2560 px"), (2800, "2800 px (print)"), (4000, "4000 px (HD print)")
                    }, _o.MaxDimension, v => _o.MaxDimension = (int)v);
                    QualitySlider("JPEG quality", FormatImageQuality);
                }
            }
            Check("Remove personal info (author, last editor, company)", _o.StripMetadata, v => _o.StripMetadata = v);
            Note(lossy
                ? "Photos are resized and re-encoded in place, keeping their file names and formats, so the layout doesn't change. Text, tables and charts are untouched."
                : "Re-zips the document at maximum compression and losslessly optimizes PNG pictures. Opens exactly the same.");
        }

        private void BuildGeneral()
        {
            FormatCombo(_kind == CompressionKind.Svg ? "Output" : "Format");
            Note(_kind switch
            {
                CompressionKind.Svg => "Removes comments, editor data (Inkscape, Illustrator, Sketch), metadata and formatting whitespace. The drawing is unchanged. .svgz is understood by browsers and vector editors.",
                CompressionKind.Archive => "Repacks every entry at the highest compression level. Contents are unchanged.",
                _ when _ext == "json" => "Minified JSON drops indentation and line breaks; the data is identical. The archive formats keep the file exactly as-is.",
                _ => "ZIP opens on every computer. GZip and Brotli compress a single file further; Brotli is the best for text, code and data files."
            });
        }

        // ---------- Control builders ----------

        private void Preset(string label, Action<CompressionOptions> apply, Func<CompressionOptions, bool> isActive)
        {
            var chip = new RadioButton { Content = label, Style = (Style)FindResource("PresetChip"), GroupName = "Preset" };
            chip.Click += (_, _) =>
            {
                apply(_o);
                Rebuild();
            };
            _presetChips.Add((chip, isActive));
            PresetPanel.Children.Add(chip);
        }

        private void Label(string text) =>
            SettingsPanel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });

        private void Note(string text) =>
            SettingsPanel.Children.Add(new TextBlock
            {
                Text = text,
                Style = (Style)FindResource("Caption"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 4)
            });

        private ComboBox Combo(string label, IList<(object Value, string Text)> items, object current, Action<object> set, bool rebuild = false)
        {
            Label(label);
            var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 14) };
            int selected = -1;
            for (int i = 0; i < items.Count; i++)
            {
                combo.Items.Add(new ComboBoxItem { Content = items[i].Text, Tag = items[i].Value });
                if (Equals(items[i].Value, current)) selected = i;
            }
            if (selected < 0)
            {
                // A saved value that isn't one of the standard steps: show it rather than silently changing it
                combo.Items.Add(new ComboBoxItem { Content = $"Custom ({current})", Tag = current });
                selected = combo.Items.Count - 1;
            }
            combo.SelectedIndex = selected;
            combo.SelectionChanged += (_, _) =>
            {
                if (_building || combo.SelectedItem is not ComboBoxItem item) return;
                set(item.Tag);
                if (rebuild) Rebuild(); else Refresh();
            };
            SettingsPanel.Children.Add(combo);
            return combo;
        }

        private void FormatCombo(string label)
        {
            var choices = Choices();
            Combo(label, choices.Select(c => ((object)c.Key, c.Label)).ToList(), _o.Format, v => _o.Format = (string)v, rebuild: true);
        }

        /// <summary>
        /// "Size control": the quality settings (<paramref name="qualityLabel"/>), or a target file size. Returns true when
        /// a target size is chosen, so the caller leaves out the controls it replaces.
        /// </summary>
        private bool SizeControl(string qualityLabel)
        {
            bool bySize = _o.TargetSizeMb != 0;
            Label("Size control");
            var rateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var byQuality = new RadioButton { Content = qualityLabel, GroupName = "Rate", IsChecked = !bySize, Margin = new Thickness(0, 0, 18, 0) };
            var bySizeRadio = new RadioButton { Content = "Target file size", GroupName = "Rate", IsChecked = bySize };
            byQuality.Checked += (_, _) => { if (_building) return; _o.TargetSizeMb = 0; Dispatcher.BeginInvoke(Rebuild); };
            bySizeRadio.Checked += (_, _) =>
            {
                if (_building) return;
                _o.TargetSizeMb = DefaultTargetMb();
                Dispatcher.BeginInvoke(Rebuild);
            };
            rateRow.Children.Add(byQuality);
            rateRow.Children.Add(bySizeRadio);
            SettingsPanel.Children.Add(rateRow);

            if (bySize)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
                var box = new TextBox { Width = 90, Text = _o.TargetSizeMb > 0 ? _o.TargetSizeMb.ToString("0.####", CultureInfo.CurrentCulture) : "" };
                box.TextChanged += (_, _) =>
                {
                    if (_building) return;
                    _o.TargetSizeMb = double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double mb) && mb > 0 ? mb : -1;
                    Refresh();
                };
                row.Children.Add(box);
                row.Children.Add(new TextBlock { Text = "MB  (0.5 = 500 KB)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = (Brush)FindResource("TextSecondaryBrush") });
                SettingsPanel.Children.Add(row);
            }
            return bySize;
        }

        /// <summary>About half the file, as a round number: where the target box starts.</summary>
        private double DefaultTargetMb()
        {
            double half = new FileInfo(_path).Length / 2.0 / 1_000_000;
            return half >= 2 ? Math.Round(half) : Math.Max(0.1, Math.Round(half, 1));
        }

        /// <summary>Common upload limits, for files bigger than them.</summary>
        private void SizePresets()
        {
            long size = new FileInfo(_path).Length;
            foreach (var (mb, label) in new[] { (2.0, "Under 2 MB"), (1.0, "Under 1 MB"), (0.5, "Under 500 KB") })
            {
                if (CompressionService.TargetBytes(mb) < size) Preset(label, o => o.TargetSizeMb = mb, o => o.TargetSizeMb == mb);
            }
        }

        private void SpeedCombo() =>
            Combo("Encoding speed", new (object, string)[]
            {
                (0, "Fast"), (1, "Balanced"), (2, "Slower, smaller file"), (3, "Slowest, smallest file")
            }, _o.Speed, v => _o.Speed = (int)v);

        private void ResizeCombo(int longestEdge)
        {
            var sizes = new List<(object, string)> { (0, "Original size") };
            foreach (var (px, name) in new[] { (3840, "3840 px (4K)"), (2560, "2560 px"), (1920, "1920 px (Full HD)"), (1600, "1600 px"),
                                               (1280, "1280 px"), (1024, "1024 px"), (800, "800 px"), (640, "640 px"), (480, "480 px") })
            {
                if (longestEdge == 0 || px < longestEdge || _o.MaxDimension == px) sizes.Add((px, name));
            }
            Combo("Longest edge", sizes, _o.MaxDimension, v => _o.MaxDimension = (int)v, rebuild: _o.Format == "guetzli");
        }

        private void QualitySlider(string label, Func<int, string> describe)
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            header.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
            var value = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)FindResource("TextSecondaryBrush"), FontSize = 12 };
            header.Children.Add(value);
            SettingsPanel.Children.Add(header);

            var slider = new Slider { Minimum = 1, Maximum = 100, SmallChange = 1, LargeChange = 5, IsSnapToTickEnabled = true, TickFrequency = 1, Value = _o.Quality };
            value.Text = describe(_o.Quality);
            slider.ValueChanged += (_, e) =>
            {
                _o.Quality = (int)e.NewValue;
                value.Text = describe(_o.Quality);
                if (!_building) Refresh();
            };
            SettingsPanel.Children.Add(slider);

            var scale = new Grid { Margin = new Thickness(0, 2, 0, 14) };
            scale.Children.Add(new TextBlock { Text = "Smaller file", FontSize = 11, Foreground = (Brush)FindResource("TextTertiaryBrush") });
            scale.Children.Add(new TextBlock { Text = "Better quality", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)FindResource("TextTertiaryBrush") });
            SettingsPanel.Children.Add(scale);
        }

        /// <summary>The picture's size after the optional resize (Longest edge), or null before it's been inspected.</summary>
        private double? GuetzliMegapixels()
        {
            if (_image == null) return null;
            double w = _image.Bitmap.Width, h = _image.Bitmap.Height;
            double scale = _o.MaxDimension > 0 && Math.Max(w, h) > _o.MaxDimension ? _o.MaxDimension / Math.Max(w, h) : 1;
            return w * h * scale * scale / 1e6;
        }

        private void DownloadGuetzliButton()
        {
            var button = new Button { Content = "Download Guetzli", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14), Padding = new Thickness(14, 5, 14, 5) };
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                button.Content = "Downloading…";
                string? error = await DependencyService.InstallToolAsync(Dependency.Guetzli);
                if (error != null) MessageBox.Show(this, error, "Download Guetzli", MessageBoxButton.OK, MessageBoxImage.Warning);
                Rebuild();
            };
            SettingsPanel.Children.Add(button);
        }

        private CheckBox Check(string text, bool value, Action<bool> set)
        {
            var check = new CheckBox
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                IsChecked = value,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 10)
            };
            check.Checked += (_, _) => { set(true); if (!_building) Refresh(); };
            check.Unchecked += (_, _) => { set(false); if (!_building) Refresh(); };
            SettingsPanel.Children.Add(check);
            return check;
        }

        // ---------- Summary, validation ----------

        /// <summary>Re-evaluates preset highlights, the summary and warnings after any change.</summary>
        private void Refresh()
        {
            foreach (var (chip, isActive) in _presetChips) chip.IsChecked = isActive(_o);

            var warnings = new List<string>();
            string? error = Validate(warnings);

            SummaryText.Text = error ?? CompressionService.Describe(_kind, _path, _o, _media, HardwareEncoderFor(_o.Format) != null);
            SummaryText.Foreground = (Brush)FindResource(error != null ? "DangerBrush" : "TextPrimaryBrush");
            SummaryIcon.Text = error != null ? "" : "";
            SummaryIcon.Foreground = (Brush)FindResource(error != null ? "DangerBrush" : "TextSecondaryBrush");
            WarningText.Text = string.Join("\n", warnings);
            WarningText.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ConfirmButton.IsEnabled = error == null;
        }

        /// <summary>Returns a blocking problem, and adds non-blocking advice to <paramref name="warnings"/>.</summary>
        private string? Validate(List<string> warnings)
        {
            bool lossy = _o.Mode == CompressionMode.Lossy;

            bool needsFFmpeg = _kind switch
            {
                CompressionKind.Video or CompressionKind.Audio or CompressionKind.AnimatedGif => true,
                CompressionKind.Image => _o.Format is "webp" or "avif" or "jxl" || (lossy && _o.Format == "keep" && _ext is "png" or "webp" or "avif"),
                _ => false
            };
            if (needsFFmpeg && _ffmpeg == null) return "This needs FFmpeg, which wasn't found. Install it with: winget install Gyan.FFmpeg";

            string? missing = _o.Format switch
            {
                "webp" when _kind == CompressionKind.Image => "libwebp",
                "webp" when _kind == CompressionKind.AnimatedGif => "libwebp_anim",
                "jxl" => "libjxl",
                "avif" => _encoders.Contains("libaom-av1") ? null : "libsvtav1",
                "h265" when !_o.UseHardwareEncoder => "libx265",
                "av1" when !_o.UseHardwareEncoder => _encoders.Contains("libsvtav1") ? null : "libaom-av1",
                "vp9" => "libvpx-vp9",
                "opus" => "libopus",
                "mp3" when _kind == CompressionKind.Audio => "libmp3lame",
                _ => null
            };
            if (missing != null && _encoders.Count > 0 && !_encoders.Contains(missing))
            {
                return $"Your FFmpeg build has no {missing} encoder. Install the full build (winget install Gyan.FFmpeg) or choose another format.";
            }

            // Target size for everything but video (which checks its own below)
            if (_kind != CompressionKind.Video && _o.TargetSizeMb != 0 && CompressionService.SupportsTargetSize(_kind, _o))
            {
                if (_o.TargetSizeMb < 0) return "Enter a target size in MB, for example 1 or 0.5.";
                long size = new FileInfo(_path).Length;
                if (CompressionService.TargetBytes(_o.TargetSizeMb) >= size)
                    return $"The target ({_o.TargetSizeMb:0.###} MB) isn't smaller than the file ({FormatSize(size)}).";
                if (_kind != CompressionKind.Audio) warnings.Add("Finding the best quality that fits takes several tries, so this is slower than a fixed quality.");
                else if (_media?.DurationSeconds > 0 && CompressionService.TargetBytes(_o.TargetSizeMb) * 8 / _media.DurationSeconds / 1000 is var kbps && kbps < 48)
                    warnings.Add($"That leaves about {kbps:0} kbps for {FormatDuration(_media.DurationSeconds)} of audio; expect muffled sound. Mono helps for speech.");
            }

            switch (_kind)
            {
                case CompressionKind.Image:
                    if (_o.Format == "guetzli")
                    {
                        if (!GuetzliService.IsAvailable) return "Guetzli isn't installed yet. Use the button above to download it (about 0.6 MB, from Google's GitHub release).";
                        if (_o.Quality < GuetzliService.MinQuality) warnings.Add($"Guetzli works from quality {GuetzliService.MinQuality} up, so {GuetzliService.MinQuality} is used.");
                        if (GuetzliMegapixels() is { } megapixels)
                        {
                            var plan = GuetzliService.Plan(new[] { megapixels }, 1, _o.GuetzliAllowLarge);
                            if (plan.Included.Count == 0)
                                return $"At {megapixels:0.#} megapixels this needs about {SystemSpecs.Gb(GuetzliService.BytesFor(megapixels))} of memory; {SystemSpecs.Gb(plan.Specs.FreeRamBytes)} is free. Choose a smaller Longest edge, close some apps, or tick \"Do it anyway\".";
                            if (plan.MemoryLoad > 1)
                                warnings.Add("This needs more than the free memory: Windows will use the page file, which is much slower and can make the PC sluggish.");
                            warnings.Add($"Guetzli is slow: {GuetzliService.DescribeTime(plan.Seconds)} for this picture.");
                        }
                        break;
                    }
                    if (_o.Format == "jxl") warnings.Add("JPEG XL opens in Photoshop, GIMP, Affinity, Safari and Windows with the JPEG XL extension; most other browsers can't show it yet.");
                    if (_o.Format == "avif") warnings.Add("AVIF opens in modern browsers, phones and Windows 11 Photos; older software may not.");
                    if (lossy && _o.Format == "keep" && _ext == "png" && _image?.HasPartialAlpha == true)
                        warnings.Add("This PNG has soft transparency, which a reduced palette would ruin; it will be optimized (and resized if chosen) instead. WebP keeps soft edges and is much smaller.");
                    if (!lossy && _o.Format == "keep" && _ext is "jpg" or "jpeg" && !_o.StripMetadata)
                        return "Lossless JPEG optimization works by removing metadata. Turn on \"Remove metadata\", or use Lossy.";
                    if (!lossy && _o.Format == "webp" && _image?.BitsPerChannel > 8)
                        return "This is a 16-bit image; WebP stores 8 bits per channel, so it wouldn't be lossless. Choose PNG or JPEG XL.";
                    break;

                case CompressionKind.Video when lossy:
                    if (_o.TargetSizeMb < 0) return "Enter a target size in MB, for example 10 or 24.5.";
                    if (_o.TargetSizeMb > 0 && _media?.DurationSeconds > 0)
                    {
                        int audio = !_media.HasAudio || _o.VideoAudioKbps < 0 ? 0 : _o.VideoAudioKbps > 0 ? _o.VideoAudioKbps : Math.Max(_media.AudioKbps, 96);
                        int kbps = CompressionService.TargetVideoKbps(_o.TargetSizeMb, _media.DurationSeconds, audio);
                        int pixels = PixelsAfterScaling();
                        double perPixel = pixels > 0 ? kbps * 1000.0 / pixels / Math.Max(1, EffectiveFps()) : 1;
                        if (kbps <= 40 || perPixel < 0.02)
                            warnings.Add($"{_o.TargetSizeMb:0.#} MB is very tight for {FormatDuration(_media.DurationSeconds)} at this resolution; expect blocky video. Lower the resolution or frame rate, or allow a bigger file.");
                        double sourceMb = new FileInfo(_path).Length / 1024.0 / 1024.0;
                        if (_o.TargetSizeMb >= sourceMb) return $"The target ({_o.TargetSizeMb:0.#} MB) isn't smaller than the file ({sourceMb:0.#} MB).";
                    }
                    if (_o.Format == "h265") warnings.Add("Windows needs the free HEVC Video Extension to play H.265; phones, Macs and smart TVs play it natively.");
                    if (_o.Format == "av1") warnings.Add("AV1 plays in modern browsers, VLC and recent phones/PCs; older devices may not decode it. Encoding is slower.");
                    if (_o.UseHardwareEncoder && _o.TargetSizeMb > 0) warnings.Add("GPU encoders use single-pass bitrate control, so the final size may land a little off target.");
                    break;

                case CompressionKind.Audio when lossy:
                    if (_media?.AudioKbps > 0 && _o.AudioKbps >= _media.AudioKbps && !CompressionService.IsLosslessAudioSource(_path))
                        warnings.Add($"The source is already {_media.AudioKbps} kbps; re-encoding at {_o.AudioKbps} kbps won't make it much smaller and loses a little quality.");
                    break;
            }
            return null;
        }

        private int PixelsAfterScaling()
        {
            if (_media == null || _media.Width <= 0) return 0;
            int w = _media.Width, h = _media.Height;
            int shortEdge = Math.Min(w, h);
            if (_o.MaxHeight > 0 && shortEdge > _o.MaxHeight)
            {
                double s = (double)_o.MaxHeight / shortEdge;
                w = (int)(w * s);
                h = (int)(h * s);
            }
            return w * h;
        }

        private double EffectiveFps() =>
            _o.MaxFps > 0 && (_media?.Fps ?? 0) > _o.MaxFps ? _o.MaxFps : _media?.Fps > 0 ? _media.Fps : 30;

        private string? HardwareEncoderFor(string codec)
        {
            if (codec == "vp9") return null;
            string prefix = codec switch { "h265" => "hevc", "av1" => "av1", _ => "h264" };
            return new[] { "_nvenc", "_qsv", "_amf" }.Select(s => prefix + s).FirstOrDefault(_workingHardware.Contains);
        }

        private static string DescribeHardware(string encoder) =>
            encoder.EndsWith("_nvenc", StringComparison.Ordinal) ? "NVIDIA NVENC"
            : encoder.EndsWith("_qsv", StringComparison.Ordinal) ? "Intel Quick Sync"
            : "AMD AMF";

        private static string FormatImageQuality(int q) => q switch
        {
            >= 90 => $"{q}  (near-identical)",
            >= 75 => $"{q}  (high)",
            >= 55 => $"{q}  (good)",
            >= 35 => $"{q}  (small)",
            _ => $"{q}  (tiny, visible artifacts)"
        };

        private static string FormatSize(long bytes) =>
            bytes < 1024 ? $"{bytes} B"
            : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB"
            : bytes < 1024L * 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.#} MB"
            : $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB";

        private static string FormatDuration(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }

        // ---------- Buttons ----------

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            _o = CompressionService.Normalize(_kind, _path, CompressionService.Defaults(_kind, _path));
            _building = true;
            (_o.Mode == CompressionMode.Lossless ? LosslessCard : LossyCard).IsChecked = true;
            _building = false;
            Rebuild();
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            if (!ConfirmButton.IsEnabled) return;
            Result = _o.Clone();
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
