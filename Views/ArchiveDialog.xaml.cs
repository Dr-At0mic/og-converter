using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// The Archive action: pick a format (ZIP, 7z, RAR, tar.*, single-file codecs, cpio, ISO, ar), tune its compression,
    /// optionally re-encode pictures (losslessly or with Guetzli), and add a password under Advanced.
    /// </summary>
    public partial class ArchiveDialog : Window
    {
        private const int MinPasswordLength = 4;

        /// <summary>Quick picks shown as chips; every format is in the list below them.</summary>
        private static readonly string[] Popular = { "zip", "7z", "tar.gz", "tar.zst", "tar.xz", "rar" };

        private readonly string _path;
        private readonly bool _isFolder;
        private readonly bool _hasPictures;
        private readonly List<ArchiveFormat> _formats;
        private ArchiveOptions _o;
        private bool _building;

        public ArchiveOptions? Result { get; private set; }

        public ArchiveDialog(string path, ArchiveOptions? initial)
        {
            InitializeComponent();
            _path = path.TrimEnd('\\', '/');
            _isFolder = Directory.Exists(_path);
            _formats = ArchiveWriter.FormatsFor(_path).ToList();
            _hasPictures = _isFolder || Path.GetExtension(_path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";
            _o = Normalize(initial?.Clone() ?? new ArchiveOptions());

            FileInfoText.Text = Path.GetFileName(_path) + (_isFolder ? "  •  folder" : "");
            FileInfoText.ToolTip = _path;

            foreach (var f in _formats)
            {
                FormatCombo.Items.Add(new ComboBoxItem
                {
                    Tag = f.Key,
                    Content = new TextBlock
                    {
                        Inlines =
                        {
                            new System.Windows.Documents.Run("." + f.Key) { FontWeight = FontWeights.SemiBold },
                            new System.Windows.Documents.Run("   " + f.Description) { Foreground = (Brush)FindResource("TextSecondaryBrush") }
                        }
                    }
                });
            }
            foreach (string key in Popular.Where(k => _formats.Any(f => f.Key == k)))
            {
                var chip = new RadioButton { Content = "." + key, Tag = key, Style = (Style)FindResource("PresetChip"), GroupName = "Popular" };
                chip.Click += (_, _) => SelectFormat(key);
                PresetPanel.Children.Add(chip);
            }

            Loaded += async (_, _) =>
            {
                BodyScroller.MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 220);
                Activate();
                SelectFormat(_o.Format);
                if (_hasPictures)
                {
                    // Picture sizes, for Guetzli's time estimate and size limit (headers only, so it's quick)
                    _jpegMegapixels = await System.Threading.Tasks.Task.Run(() =>
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
            foreach (ComboBoxItem item in FormatCombo.Items)
            {
                if ((string)item.Tag == key) FormatCombo.SelectedItem = item;
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

        private void OnFormatChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_building || FormatCombo.SelectedItem is not ComboBoxItem item) return;
            SetFormat((string)item.Tag);
        }

        // ---------- Settings for the chosen format ----------

        private void Rebuild()
        {
            _building = true;
            try
            {
                SettingsPanel.Children.Clear();
                var f = Format;
                foreach (RadioButton chip in PresetPanel.Children) chip.IsChecked = (string)chip.Tag == f.Key;

                if (f.HasLevels) LevelSlider(f);

                if (f.Methods.Length > 0)
                {
                    Combo("Method", f.Methods.Select(m => ((object)m, MethodLabel(f.Key, m))).ToList(), ArchiveWriter.MethodFor(f, _o), v => _o.Method = (string)v);
                }

                switch (f.Key)
                {
                    case "7z":
                        Check("Solid: compress all files together (smaller archive)", _o.Solid, v => _o.Solid = v);
                        Check("Use all CPU cores", _o.MultiThreaded, v => _o.MultiThreaded = v);
                        break;
                    case "rar":
                        Check("Solid: compress all files together (smaller archive)", _o.Solid, v => _o.Solid = v);
                        Combo("Recovery record", new (object, string)[] { (0, "None"), (1, "1% (repairs small damage)"), (3, "3%"), (5, "5%"), (10, "10% (survives bad sectors and broken downloads)") },
                            _o.RecoveryPercent, v => _o.RecoveryPercent = (int)v);
                        Check("Use all CPU cores", _o.MultiThreaded, v => _o.MultiThreaded = v);
                        break;
                    case "zst" or "tar.zst":
                        Check("Long-range matching: smaller for big, repetitive data (backups)", _o.LongRange, v => _o.LongRange = v);
                        Check("Use all CPU cores", _o.MultiThreaded, v => _o.MultiThreaded = v);
                        break;
                    case "xz" or "tar.xz":
                        Check("Use all CPU cores", _o.MultiThreaded, v => _o.MultiThreaded = v);
                        break;
                    case "br" or "tar.br":
                        Combo("Window size", new (object, string)[] { (16, "64 KB (low memory)"), (20, "1 MB"), (22, "4 MB (default)"), (24, "16 MB (best for big files)") },
                            _o.BrotliWindow, v => _o.BrotliWindow = (int)v);
                        break;
                }

                if (_hasPictures)
                {
                    Combo("Pictures inside", new (object, string)[]
                    {
                        (ImageRecompression.None, "Keep as they are"),
                        (ImageRecompression.Lossless, "Optimize losslessly (JPEG metadata removed, PNGs re-encoded)"),
                        (ImageRecompression.Guetzli, "Re-encode JPEGs with Guetzli: 20–30% smaller, look the same (very slow)")
                    }, _o.Images, v => _o.Images = (ImageRecompression)v, rebuild: true);

                    if (_o.Images == ImageRecompression.Guetzli)
                    {
                        Slider("Guetzli quality", GuetzliService.MinQuality, 100, _o.GuetzliQuality, v => _o.GuetzliQuality = v,
                               q => q >= 95 ? $"{q}  (visually lossless)" : q >= 90 ? $"{q}  (high)" : $"{q}  (good)", "Smaller file", "Better quality");
                        GuetzliResources();
                    }
                }

                Note(FormatNote(f));
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

        private string FormatNote(ArchiveFormat f) => f.Key switch
        {
            "zip" => "Opens with a double-click in Windows, macOS and Linux.",
            "7z" => "Opens in 7-Zip, WinRAR, PeaZip, macOS archive apps and Windows 11 File Explorer.",
            "rar" => "Opens in WinRAR, 7-Zip and Windows 11 File Explorer. Creating RAR needs WinRAR (shareware).",
            "tar" => "No compression: files are only bundled, keeping folders and dates. Fastest to make.",
            "iso" => "Folder contents go to the disc's root. Windows mounts it as a drive with a double-click.",
            "cpio" => "For Linux tooling (initramfs, RPM). Not compressed.",
            "ar" => "For Unix tooling. Most people want ZIP instead.",
            _ when f.SingleFile => $"Keeps the file's name and adds .{f.Key}. To pack several files or a folder, choose tar.{f.Key}.",
            _ => "Opens in 7-Zip, WinRAR, Windows 11 File Explorer and on any Linux or Mac."
        };

        // ---------- Advanced: password ----------

        private bool Showing => ShowPasswordCheck.IsChecked == true;
        private string Password => Showing ? PasswordVisible.Text : PasswordInput.Password;
        private string Confirmation => Showing ? ConfirmVisible.Text : ConfirmInput.Password;

        private void OnAdvancedToggled(object sender, RoutedEventArgs e)
        {
            bool open = AdvancedPanel.Visibility != Visibility.Visible;
            AdvancedPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            AdvancedChevron.Text = open ? "" : "";
            if (open && PasswordFields.IsEnabled) Dispatcher.BeginInvoke(() => (Showing ? (Control)PasswordVisible : PasswordInput).Focus());
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_building) Refresh();
        }

        private void OnShowPasswordChanged(object sender, RoutedEventArgs e)
        {
            bool show = Showing;
            _building = true;
            if (show)
            {
                PasswordVisible.Text = PasswordInput.Password;
                ConfirmVisible.Text = ConfirmInput.Password;
            }
            else
            {
                PasswordInput.Password = PasswordVisible.Text;
                ConfirmInput.Password = ConfirmVisible.Text;
            }
            _building = false;
            PasswordInput.Visibility = ConfirmInput.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            PasswordVisible.Visibility = ConfirmVisible.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        }

        private void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            string generated = ProtectionService.GeneratePassword();
            ShowPasswordCheck.IsChecked = true;
            _building = true;
            PasswordVisible.Text = ConfirmVisible.Text = generated;
            PasswordInput.Password = ConfirmInput.Password = generated;
            _building = false;
            try { Clipboard.SetText(generated); } catch { }
            Refresh();
        }

        // ---------- Summary, validation ----------

        private void Refresh()
        {
            if (!IsLoaded) return;
            var f = Format;

            // Password fields only for formats that can hold one
            PasswordFields.IsEnabled = f.CanEncrypt;
            PasswordFields.Opacity = f.CanEncrypt ? 1 : 0.45;
            ZipCipherPanel.Visibility = f.Key == "zip" ? Visibility.Visible : Visibility.Collapsed;
            EncryptNamesCheck.Visibility = f.Key is "7z" or "rar" ? Visibility.Visible : Visibility.Collapsed;
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
            ToolMissingPanel.Visibility = missing != null ? Visibility.Visible : Visibility.Collapsed;
            if (missing is { } tool)
            {
                string name = DependencyService.DisplayName(tool);
                ToolMissingText.Text = tool switch
                {
                    Dependency.WinRar => "Creating RAR archives needs WinRAR (shareware, free to try), which isn't installed.",
                    Dependency.Guetzli => "Guetzli isn't downloaded yet: one small program (0.6 MB) from Google's official GitHub release.",
                    _ when f.Key == "7z" && _o.Password.Length > 0 => "Password-protected 7z needs 7-Zip (free), which isn't installed.",
                    _ => $"This Windows version's tar can't write .{f.Key}. 7-Zip (free) can."
                };
                InstallToolButton.Content = !DependencyService.CanInstallDependency(tool) ? "Download page" : tool == Dependency.Guetzli ? "Download" : $"Install {name}";
                InstallToolButton.Tag = tool;
            }

            var warnings = new List<string>();
            string? error = Validate(f, missing, warnings);
            SummaryText.Text = error ?? Describe(f);
            SummaryText.Foreground = (Brush)FindResource(error != null ? "DangerBrush" : "TextPrimaryBrush");
            SummaryIcon.Text = error != null ? "" : "";
            SummaryIcon.Foreground = (Brush)FindResource(error != null ? "DangerBrush" : "TextSecondaryBrush");
            WarningText.Text = string.Join("\n", warnings);
            WarningText.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ConfirmButton.IsEnabled = error == null;
        }

        private string? Validate(ArchiveFormat f, Dependency? missing, List<string> warnings)
        {
            if (missing is { } tool) return $"Install {DependencyService.DisplayName(tool)} to continue, or choose another option.";

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
                    warnings.Add($"This needs up to {SystemSpecs.Gb(plan.PeakBytes)} but {SystemSpecs.Gb(plan.Specs.FreeRamBytes)} is free: Windows will use the page file, which is much slower and can make the PC sluggish. Fewer parallel jobs need less.");
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

        private async void OnInstallToolClick(object sender, RoutedEventArgs e)
        {
            if (InstallToolButton.Tag is not Dependency tool) return;
            if (!DependencyService.CanInstallDependency(tool))
            {
                try { DependencyService.OpenDownloadPage(tool); } catch (System.ComponentModel.Win32Exception) { }
                return;
            }
            if (tool == Dependency.WinRar)
            {
                var answer = MessageBox.Show(this,
                    "Install WinRAR?\n\nWinRAR is shareware: free to try for 40 days, then it asks you to buy a license (it keeps working). " +
                    "Windows will ask for admin permission once.\n\nOnly creating RAR needs it; 7z is free and usually just as small.",
                    "Install WinRAR", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK) return;
            }

            object content = InstallToolButton.Content;
            InstallToolButton.IsEnabled = false;
            InstallToolButton.Content = tool == Dependency.Guetzli ? "Downloading…" : "Installing…";
            string? error = await DependencyService.InstallToolAsync(tool);
            InstallToolButton.Content = content;
            InstallToolButton.IsEnabled = true;
            if (error != null) MessageBox.Show(this, error, $"Install {DependencyService.DisplayName(tool)}", MessageBoxButton.OK, MessageBoxImage.Warning);
            Rebuild();
        }

        /// <summary>Parallel jobs, "do it anyway" and the memory / time panel, from this PC's specs and the pictures' sizes.</summary>
        private void GuetzliResources()
        {
            if (_jpegMegapixels == null)
            {
                Note("Measuring the pictures…");
                return;
            }
            var specs = GuetzliService.Specs();
            double safe = GuetzliService.SafeMegapixels(specs);

            if (_jpegMegapixels.Count > 1)
            {
                // Like LM Studio's offload options: each choice shows what it costs on this PC
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
                Combo("Parallel jobs (one CPU core each)", choices, _o.GuetzliJobs, v => _o.GuetzliJobs = (int)v, rebuild: true);
            }
            else
            {
                Note("Guetzli encodes one picture on one CPU core; parallel jobs speed up folders with many photos.");
            }

            if (_jpegMegapixels.Any(mp => mp > safe) || _o.GuetzliAllowLarge)
            {
                double biggest = _jpegMegapixels.Max();
                Check("Do it anyway, even if it needs more than the free memory",
                      _o.GuetzliAllowLarge, v => { _o.GuetzliAllowLarge = v; Dispatcher.BeginInvoke(Rebuild); });
                Note($"With the memory free now, pictures up to {safe:0} MP fit. The largest here is {biggest:0.#} MP and needs about {SystemSpecs.Gb(GuetzliService.BytesFor(biggest))}. No admin rights are needed: Windows lends apps all the memory it has, then the page file.");
            }

            // When nothing fits, show what "do it anyway" would need (in red) rather than an empty bar
            var shown = GuetzliService.Plan(_jpegMegapixels, _o.GuetzliJobs, _o.GuetzliAllowLarge, specs);
            if (shown.Included.Count == 0) shown = GuetzliService.Plan(_jpegMegapixels, _o.GuetzliJobs, true, specs);
            SettingsPanel.Children.Add(GuetzliResourcePanel.Build(this, shown));
        }

        // ---------- Control builders ----------

        private void Label(string text) =>
            SettingsPanel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });

        private void Note(string text) =>
            SettingsPanel.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Caption"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

        private void Combo(string label, IList<(object Value, string Text)> items, object current, Action<object> set, bool rebuild = false)
        {
            Label(label);
            var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 14) };
            int selected = 0;
            for (int i = 0; i < items.Count; i++)
            {
                combo.Items.Add(new ComboBoxItem { Content = items[i].Text, Tag = items[i].Value });
                if (Equals(items[i].Value, current)) selected = i;
            }
            combo.SelectedIndex = selected;
            set(items[selected].Value);
            combo.SelectionChanged += (_, _) =>
            {
                if (_building || combo.SelectedItem is not ComboBoxItem item) return;
                set(item.Tag);
                if (rebuild) Rebuild(); else Refresh();
            };
            SettingsPanel.Children.Add(combo);
        }

        private void LevelSlider(ArchiveFormat f)
        {
            int level = ArchiveWriter.LevelFor(f, _o);
            _o.Level = level;
            Slider("Compression level", f.MinLevel, f.MaxLevel, level, v => _o.Level = v,
                   v => v == f.DefaultLevel ? $"{v}  (default)" : v == f.MaxLevel ? $"{v}  (maximum)" : v == 0 && f.Key is "zip" or "7z" or "rar" ? $"{v}  (store only)" : $"{v}",
                   "Faster, bigger", "Smallest, slower");
        }

        private void Slider(string label, int min, int max, int value, Action<int> set, Func<int, string> describe, string left, string right)
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            header.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
            var text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)FindResource("TextSecondaryBrush"), FontSize = 12, Text = describe(value) };
            header.Children.Add(text);
            SettingsPanel.Children.Add(header);

            var slider = new Slider { Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 1, IsSnapToTickEnabled = true, TickFrequency = 1, Value = value };
            slider.ValueChanged += (_, e) =>
            {
                int v = (int)e.NewValue;
                set(v);
                text.Text = describe(v);
                if (!_building) Refresh();
            };
            SettingsPanel.Children.Add(slider);

            var scale = new Grid { Margin = new Thickness(0, 2, 0, 14) };
            scale.Children.Add(new TextBlock { Text = left, FontSize = 11, Foreground = (Brush)FindResource("TextTertiaryBrush") });
            scale.Children.Add(new TextBlock { Text = right, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)FindResource("TextTertiaryBrush") });
            SettingsPanel.Children.Add(scale);
        }

        private void Check(string text, bool value, Action<bool> set)
        {
            var check = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 10) };
            check.Checked += (_, _) => { set(true); if (!_building) Refresh(); };
            check.Unchecked += (_, _) => { set(false); if (!_building) Refresh(); };
            SettingsPanel.Children.Add(check);
        }

        // ---------- Buttons ----------

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            _o = Normalize(new ArchiveOptions());
            SelectFormat(_o.Format);
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            Refresh();
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
