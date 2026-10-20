using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// The Archive action: pick a format (ZIP, 7z, RAR, tar.*, single-file codecs, cpio, ISO, ar), tune its compression,
    /// optionally re-encode pictures (losslessly or with Guetzli), and add a password under Advanced.
    /// </summary>
    public partial class ArchiveDialog : DialogWindow
    {
        private const int MinPasswordLength = 4;

        /// <summary>Quick picks shown as chips; every format is in the list below them.</summary>
        private static readonly string[] Popular = { "zip", "7z", "tar.gz", "tar.zst", "tar.xz", "rar" };

        private readonly string _path = string.Empty;
        private readonly bool _isFolder;
        private readonly bool _hasPictures;
        private readonly List<ArchiveFormat> _formats = new();
        private readonly SettingsBuilder _ui;
        private ArchiveOptions _o = new();
        private bool _building;

        public ArchiveOptions? Result { get; private set; }

        /// <summary>For the XAML previewer.</summary>
        public ArchiveDialog()
        {
            InitializeComponent();
            _ui = new SettingsBuilder(SettingsPanel, () => _building, Refresh, Rebuild);
        }

        public ArchiveDialog(string path, ArchiveOptions? initial)
        {
            InitializeComponent();
            _ui = new SettingsBuilder(SettingsPanel, () => _building, Refresh, Rebuild);
            _path = path.TrimEnd('\\', '/');
            _isFolder = Directory.Exists(_path);
            _formats = ArchiveWriter.FormatsFor(_path).ToList();
            _hasPictures = _isFolder || Path.GetExtension(_path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";
            _o = Normalize(initial?.Clone() ?? new ArchiveOptions());

            FileInfoText.Text = Path.GetFileName(_path) + (_isFolder ? "  •  folder" : "");
            ToolTip.SetTip(FileInfoText, _path);

            foreach (var f in _formats)
            {
                var description = new Run("   " + f.Description);
                description.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("TextSecondaryBrush"));
                FormatCombo.Items.Add(new ComboBoxItem
                {
                    Tag = f.Key,
                    Content = new TextBlock { Inlines = new InlineCollection { new Run("." + f.Key) { FontWeight = FontWeight.SemiBold }, description } }
                });
            }
            var chipTheme = this.TryFindResource("PresetChip", out object? theme) ? theme as ControlTheme : null;
            foreach (string key in Popular.Where(k => _formats.Any(f => f.Key == k)))
            {
                var chip = new RadioButton { Content = "." + key, Tag = key, Theme = chipTheme, GroupName = "Popular" };
                chip.Click += (_, _) => SelectFormat(key);
                PresetPanel.Children.Add(chip);
            }

            Opened += async (_, _) =>
            {
                if (Screens.ScreenFromWindow(this) is { } screen) BodyScroller.MaxHeight = Math.Max(360, screen.WorkingArea.Height / screen.Scaling - 220);
                SelectFormat(_o.Format);
                if (_hasPictures)
                {
                    // Picture sizes, for Guetzli's time estimate and size limit (headers only, so it's quick)
                    _jpegMegapixels = await Task.Run(() =>
                        (_isFolder ? Directory.EnumerateFiles(_path, "*", SearchOption.AllDirectories) : new[] { _path })
                            .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".jpg" or ".jpeg")
                            .Select(GuetzliService.Megapixels)
                            .Where(mp => mp > 0)
                            .ToList());
                    Rebuild();
                }
            };
        }

        /// <summary>Megapixels of each JPEG in the source; null until measured.</summary>
        private List<double>? _jpegMegapixels;

        private ArchiveOptions Normalize(ArchiveOptions o)
        {
            // A single-file format saved from a file doesn't fit a folder
            if (!_formats.Any(f => f.Key == o.Format)) o.Format = "zip";
            if (!_hasPictures) o.Images = ImageRecompression.None;
            o.GuetzliQuality = Math.Clamp(o.GuetzliQuality, GuetzliService.MinQuality, 100);
            return o;
        }

        private ArchiveFormat Format => ArchiveWriter.Find(_o.Format);

        private void SelectFormat(string key)
        {
            _building = true;
            foreach (var item in FormatCombo.Items.OfType<ComboBoxItem>())
            {
                if ((string?)item.Tag == key) FormatCombo.SelectedItem = item;
            }
            _building = false;
            SetFormat(key);
        }

        /// <summary>Levels and methods mean different things per format: a new format starts from its own defaults.</summary>
        private void SetFormat(string key)
        {
            if (key != _o.Format)
            {
                _o.Level = -1;
                _o.Method = "";
            }
            _o.Format = key;
            Rebuild();
        }

        private void OnFormatChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_building || FormatCombo.SelectedItem is not ComboBoxItem { Tag: string key }) return;
            SetFormat(key);
        }

        // ---------- Settings for the chosen format ----------

        private void Rebuild()
        {
            if (_formats.Count == 0) return;
            _building = true;
            try
            {
                SettingsPanel.Children.Clear();
                var f = Format;
                foreach (var chip in PresetPanel.Children.OfType<RadioButton>()) chip.IsChecked = (string?)chip.Tag == f.Key;

                if (f.HasLevels) LevelSlider(f);

                if (f.Methods.Length > 0)
                {
                    _ui.Combo("Method", f.Methods.Select(m => ((object)m, MethodLabel(f.Key, m))).ToList(), ArchiveWriter.MethodFor(f, _o), v => _o.Method = (string)v, keepCustom: false);
                }

                switch (f.Key)
                {
                    case "7z":
                        _ui.Check("Solid: compress all files together (smaller archive)", _o.Solid, v => _o.Solid = v);
                        _ui.Check("Use all CPU cores", _o.MultiThreaded, v => _o.MultiThreaded = v);
                        break;
                    case "rar":
                        _ui.Check("Solid: compress all files together (smaller archive)", _o.Solid, v => _o.Solid = v);
                        _ui.Combo("Recovery record", new (object, string)[] { (0, "None"), (1, "1% (repairs small damage)"), (3, "3%"), (5, "5%"), (10, "10% (survives bad sectors and broken downloads)") },
                            _o.RecoveryPercent, v => _o.RecoveryPercent = (int)v, keepCustom: false);
                        _ui.Check("Use all CPU cores", _o.MultiThreaded, v => _o.MultiThreaded = v);
                        break;
                    case "zst" or "tar.zst":
                        _ui.Check("Long-range matching: smaller for big, repetitive data (backups)", _o.LongRange, v => _o.LongRange = v);
                        _ui.Check("Use all CPU cores", _o.MultiThreaded, v => _o.MultiThreaded = v);
                        break;
                    case "xz" or "tar.xz":
                        _ui.Check("Use all CPU cores", _o.MultiThreaded, v => _o.MultiThreaded = v);
                        break;
                    case "br" or "tar.br":
                        _ui.Combo("Window size", new (object, string)[] { (16, "64 KB (low memory)"), (20, "1 MB"), (22, "4 MB (default)"), (24, "16 MB (best for big files)") },
                            _o.BrotliWindow, v => _o.BrotliWindow = (int)v, keepCustom: false);
                        break;
                }

                if (_hasPictures)
                {
                    _ui.Combo("Pictures inside", new (object, string)[]
                    {
                        (ImageRecompression.None, "Keep as they are"),
                        (ImageRecompression.Lossless, "Optimize losslessly (JPEG metadata removed, PNGs re-encoded)"),
                        (ImageRecompression.Guetzli, "Re-encode JPEGs with Guetzli: 20–30% smaller, look the same (very slow)")
                    }, _o.Images, v => _o.Images = (ImageRecompression)v, rebuild: true, keepCustom: false);

                    if (_o.Images == ImageRecompression.Guetzli)
                    {
                        _ui.Slider("Guetzli quality", GuetzliService.MinQuality, 100, _o.GuetzliQuality, v => _o.GuetzliQuality = v,
                                   q => q >= 95 ? $"{q}  (visually lossless)" : q >= 90 ? $"{q}  (high)" : $"{q}  (good)", "Smaller file", "Better quality", largeChange: 1);
                        GuetzliResources();
                    }
                }

                _ui.Note(FormatNote(f));
            }
            finally
            {
                _building = false;
            }
            Refresh();
        }

        private static string MethodLabel(string format, string method) => (format, method) switch
        {
            ("zip", "deflate") => "Deflate (every unzip tool)",
            ("zip", "store") => "Store (no compression, instant)",
            (_, "lzma2") => "LZMA2 (best, multi-core)",
            (_, "lzma") => "LZMA (older 7-Zip versions)",
            (_, "ppmd") => "PPMd (best for plain text)",
            (_, "bzip2") => "BZip2",
            (_, "deflate") => "Deflate (fastest)",
            (_, "store") => "Store (no compression)",
            _ => method
        };

        private static string FormatNote(ArchiveFormat f) => f.Key switch
        {
            "zip" => "Opens with a double-click on Linux, Windows and macOS.",
            "7z" => "Opens in 7-Zip, File Roller, Ark, PeaZip, WinRAR and macOS archive apps.",
            "rar" => "Opens in 7-Zip, WinRAR and most archive apps. Creating RAR needs the rar program (shareware).",
            "tar" => "No compression: files are only bundled, keeping folders, permissions and dates. Fastest to make.",
            "iso" => "Folder contents go to the disc's root. File managers mount it with a double-click.",
            "cpio" => "For Linux tooling (initramfs, RPM). Not compressed.",
            "ar" => "For Unix tooling. Most people want ZIP instead.",
            _ when f.SingleFile => $"Keeps the file's name and adds .{f.Key}. To pack several files or a folder, choose tar.{f.Key}.",
            _ => "Opens in File Roller, Ark, 7-Zip and on any Linux or Mac, and in Windows 11 File Explorer."
        };

        // ---------- Advanced: password ----------

        private string Password => PasswordInput.Text ?? string.Empty;
        private string Confirmation => ConfirmInput.Text ?? string.Empty;

        private void OnAdvancedToggled(object? sender, RoutedEventArgs e)
        {
            bool open = !AdvancedPanel.IsVisible;
            AdvancedPanel.IsVisible = open;
            AdvancedChevron.Text = IconExtension.Glyph(open ? "E70D" : "E76C");
            if (open && PasswordFields.IsEnabled) Dispatcher.UIThread.Post(() => PasswordInput.Focus());
        }

        private void OnPasswordChanged(object? sender, RoutedEventArgs e)
        {
            if (!_building) Refresh();
        }

        private void OnPasswordTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (!_building) Refresh();
        }

        private void OnPasswordSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_building) Refresh();
        }

        private void OnShowPasswordChanged(object? sender, RoutedEventArgs e)
        {
            PasswordInput.RevealPassword = ConfirmInput.RevealPassword = ShowPasswordCheck.IsChecked == true;
            Refresh();
        }

        private async void OnGenerateClick(object? sender, RoutedEventArgs e)
        {
            string generated = ProtectionService.GeneratePassword();
            ShowPasswordCheck.IsChecked = true;
            _building = true;
            PasswordInput.Text = ConfirmInput.Text = generated;
            _building = false;
            await DesktopActions.CopyTextAsync(this, generated);
            Refresh();
        }

        // ---------- Summary, validation ----------

        private void Refresh()
        {
            if (!IsLoaded || _formats.Count == 0) return;
            var f = Format;

            // Password fields only for formats that can hold one
            PasswordFields.IsEnabled = f.CanEncrypt;
            PasswordFields.Opacity = f.CanEncrypt ? 1 : 0.45;
            ZipCipherPanel.IsVisible = f.Key == "zip";
            EncryptNamesCheck.IsVisible = f.Key is "7z" or "rar";
            PasswordNote.Text = f.Key switch
            {
                "zip" => "ZIP keeps file names readable; only the contents are encrypted. Choose 7z to hide the names too.",
                "7z" or "rar" => "AES-256. The password can't be recovered if it's forgotten.",
                _ => $".{f.Key} can't hold a password. Choose ZIP, 7z or RAR, or protect the finished archive with Protect › Encrypted file."
            };

            string pw = f.CanEncrypt ? Password : "";
            _o.Password = pw;
            _o.ZipCipher = ZipCipherCombo.SelectedIndex == 1 ? ZipCipher.ZipCrypto : ZipCipher.Aes256;
            _o.EncryptNames = EncryptNamesCheck.IsChecked == true;
            AdvancedBadge.Text = pw.Length > 0 ? "· on" : "";

            var missing = ArchiveWriter.MissingTool(f, _o);
            ToolMissing.Show(missing, missing switch
            {
                Dependency.WinRar => "Creating RAR archives needs the rar program (shareware, free to try), which isn't installed.",
                Dependency.Guetzli => "Guetzli isn't installed yet. It's a small program from your distribution's packages.",
                not null when f.Key == "7z" && _o.Password.Length > 0 => "Password-protected 7z needs 7-Zip (free), which isn't installed.",
                not null => $"The system's tar can't write .{f.Key}. 7-Zip (free) can.",
                _ => null
            });

            var warnings = new List<string>();
            string? error = Validate(f, missing, warnings);
            SummaryText.Text = error ?? Describe(f);
            SummaryText.Foreground = Brush(error != null ? "DangerBrush" : "TextPrimaryBrush");
            SummaryIcon.Text = IconExtension.Glyph(error != null ? "E783" : "E946");
            SummaryIcon.Foreground = Brush(error != null ? "DangerBrush" : "TextSecondaryBrush");
            WarningText.Text = string.Join("\n", warnings);
            WarningText.IsVisible = warnings.Count > 0;
            ConfirmButton.IsEnabled = error == null;
        }

        private string? Validate(ArchiveFormat f, Dependency? missing, List<string> warnings)
        {
            if (missing is { } tool) return $"Install {PackageHints.DisplayName(tool)} to continue, or choose another option.";

            string pw = _o.Password;
            if (pw.Length > 0)
            {
                if (pw.Length < MinPasswordLength) return $"Use a password of at least {MinPasswordLength} characters, or leave it blank.";
                if (Confirmation != pw) return Confirmation.Length == 0 ? "Type the password again under Advanced to confirm it." : "The passwords don't match.";
                if (f.Key == "zip" && _o.ZipCipher == ZipCipher.ZipCrypto) warnings.Add("ZipCrypto can be cracked quickly. Prefer AES-256 for anything sensitive.");
                else if (ProtectionService.EstimateStrength(pw) == 0) warnings.Add("This password is weak. Longer is stronger: try a passphrase of several words.");
            }

            int level = ArchiveWriter.LevelFor(f, _o);
            if (f.Codec == "zst" && level >= 20) warnings.Add("zstd levels 20–22 need a lot of memory to pack and are slow; 19 is nearly as small.");
            if (f.Codec == "br" && level >= 10 && _isFolder) warnings.Add("Brotli 10–11 is slow on large folders (about 1 MB/s). Level 9 is several times faster.");
            if (_o.Images == ImageRecompression.Guetzli && _jpegMegapixels != null)
            {
                if (_jpegMegapixels.Count == 0) return "There are no JPEGs here for Guetzli to re-encode. Choose \"Keep as they are\".";
                var plan = GuetzliService.Plan(_jpegMegapixels, _o.GuetzliJobs, _o.GuetzliAllowLarge);
                if (plan.Included.Count == 0)
                {
                    return _jpegMegapixels.Count == 1
                        ? $"This photo is {_jpegMegapixels[0]:0.#} megapixels and needs about {SystemSpecs.Gb(GuetzliService.BytesFor(_jpegMegapixels[0]))} of memory; {SystemSpecs.Gb(plan.Specs.FreeRamBytes)} is free right now. Close some apps, or tick \"Do it anyway\"."
                        : "Every JPEG here needs more memory than is free right now. Close some apps, or tick \"Do it anyway\".";
                }
                if (plan.MemoryLoad > 1)
                    warnings.Add($"This needs up to {SystemSpecs.Gb(plan.PeakBytes)} but {SystemSpecs.Gb(plan.Specs.FreeRamBytes)} is free: the system will use swap, which is much slower and can make the computer sluggish. Fewer parallel jobs need less.");
                warnings.Add($"Guetzli: {plan.Included.Count} JPEG{(plan.Included.Count == 1 ? "" : "s")}, {GuetzliService.DescribeTime(plan.Seconds)}" +
                             (plan.Skipped > 0 ? $"; {plan.Skipped} too big for the free memory will be packed unchanged." : ". JPEGs it can't improve are kept as they are."));
            }
            if (f.Key == "iso" && !_isFolder) warnings.Add("The disc image will contain just this one file.");
            return null;
        }

        private string Describe(ArchiveFormat f)
        {
            var parts = new List<string> { f.Title };
            if (f.HasLevels)
            {
                int level = ArchiveWriter.LevelFor(f, _o);
                parts.Add(level == f.MaxLevel ? $"level {level} (maximum)" : level == f.DefaultLevel ? $"level {level} (default)" : $"level {level}");
            }
            if (f.Methods.Length > 0) parts.Add(MethodLabel(f.Key, ArchiveWriter.MethodFor(f, _o)).Split(" (")[0]);
            if (f.Key is "7z" or "rar" && _o.Solid) parts.Add("solid");
            if (f.Key == "rar" && _o.RecoveryPercent > 0) parts.Add($"{_o.RecoveryPercent}% recovery");
            if (f.Codec == "zst" && _o.LongRange) parts.Add("long range");
            if (_o.Password.Length > 0)
            {
                parts.Add(f.Key == "zip"
                    ? $"password ({(_o.ZipCipher == ZipCipher.ZipCrypto ? "ZipCrypto" : "AES-256")})"
                    : $"password (AES-256{(_o.EncryptNames ? ", names hidden" : "")})");
            }
            if (_o.Images == ImageRecompression.Lossless) parts.Add("pictures optimized");
            if (_o.Images == ImageRecompression.Guetzli) parts.Add($"JPEGs via Guetzli q{_o.GuetzliQuality}");
            parts.Add("by " + ArchiveWriter.EngineName(f, _o));

            string name = Path.GetFileName(_path);
            string output = _isFolder || !f.KeepsExtension ? (_isFolder ? name : Path.GetFileNameWithoutExtension(name)) + "." + f.Key : name + "." + f.Key;
            return string.Join(" · ", parts) + $"  →  {output}";
        }

        private void OnToolInstalled(object? sender, EventArgs e) => Rebuild();

        /// <summary>Parallel jobs, "do it anyway" and the memory / time panel, from this computer's specs and the pictures' sizes.</summary>
        private void GuetzliResources()
        {
            if (_jpegMegapixels == null)
            {
                _ui.Note("Measuring the pictures…");
                return;
            }
            var specs = GuetzliService.Specs();
            double safe = GuetzliService.SafeMegapixels(specs);

            if (_jpegMegapixels.Count > 1)
            {
                // Like LM Studio's offload options: each choice shows what it costs on this computer
                var choices = new List<(object, string)>();
                var auto = GuetzliService.Plan(_jpegMegapixels, 0, _o.GuetzliAllowLarge, specs);
                choices.Add((0, $"Auto: {auto.Jobs} at once · {GuetzliService.DescribeTime(auto.Seconds)} · up to {SystemSpecs.Gb(auto.PeakBytes)}"));
                int max = Math.Min(GuetzliService.MaxJobs(specs), _jpegMegapixels.Count);
                foreach (int jobs in new[] { 1, 2, 3, 4, 6, 8, 12, 16 }.Where(j => j <= max))
                {
                    var plan = GuetzliService.Plan(_jpegMegapixels, jobs, _o.GuetzliAllowLarge, specs);
                    string fit = plan.MemoryLoad > 1 ? "  (more than free memory)" : "";
                    choices.Add((jobs, $"{jobs} at once · {GuetzliService.DescribeTime(plan.Seconds)} · up to {SystemSpecs.Gb(plan.PeakBytes)}{fit}"));
                }
                _ui.Combo("Parallel jobs (one CPU core each)", choices, _o.GuetzliJobs, v => _o.GuetzliJobs = (int)v, rebuild: true, keepCustom: false);
            }
            else
            {
                _ui.Note("Guetzli encodes one picture on one CPU core; parallel jobs speed up folders with many photos.");
            }

            if (_jpegMegapixels.Any(mp => mp > safe) || _o.GuetzliAllowLarge)
            {
                double biggest = _jpegMegapixels.Max();
                _ui.Check("Do it anyway, even if it needs more than the free memory",
                          _o.GuetzliAllowLarge, v => { _o.GuetzliAllowLarge = v; Dispatcher.UIThread.Post(Rebuild); });
                _ui.Note($"With the memory free now, pictures up to {safe:0} MP fit. The largest here is {biggest:0.#} MP and needs about {SystemSpecs.Gb(GuetzliService.BytesFor(biggest))}. No admin rights are needed: the system lends apps all the memory it has, then swap.");
            }

            // When nothing fits, show what "do it anyway" would need (in red) rather than an empty bar
            var shown = GuetzliService.Plan(_jpegMegapixels, _o.GuetzliJobs, _o.GuetzliAllowLarge, specs);
            if (shown.Included.Count == 0) shown = GuetzliService.Plan(_jpegMegapixels, _o.GuetzliJobs, true, specs);
            _ui.Add(GuetzliResourcePanel.Build(shown));
        }

        private void LevelSlider(ArchiveFormat f)
        {
            int level = ArchiveWriter.LevelFor(f, _o);
            _o.Level = level;
            _ui.Slider("Compression level", f.MinLevel, f.MaxLevel, level, v => _o.Level = v,
                       v => v == f.DefaultLevel ? $"{v}  (default)" : v == f.MaxLevel ? $"{v}  (maximum)" : v == 0 && f.Key is "zip" or "7z" or "rar" ? $"{v}  (store only)" : $"{v}",
                       "Faster, bigger", "Smallest, slower", largeChange: 1);
        }

        // ---------- Buttons ----------

        private void OnResetClick(object? sender, RoutedEventArgs e)
        {
            _o = Normalize(new ArchiveOptions());
            SelectFormat(_o.Format);
        }

        private void OnConfirmClick(object? sender, RoutedEventArgs e)
        {
            Refresh();
            if (!ConfirmButton.IsEnabled) return;
            Result = _o.Clone();
            Finish(true);
        }
    }
}
