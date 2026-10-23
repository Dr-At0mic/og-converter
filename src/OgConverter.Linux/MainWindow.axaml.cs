using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Themes;
using OgConverter.Views;

namespace OgConverter
{
    /// <summary>
    /// The dashboard: Home (wheel keys, try-it area, Recent activity), Settings and About. Port of the Windows MainWindow;
    /// the job pipeline (<see cref="ProcessJobAsync"/>) runs everything the wheel asks for.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ConversionEngine _engine;
        private readonly SettingsService _settingsService;
        private readonly NotificationService _notifyService;
        private readonly WheelController _wheel;

        private readonly ObservableCollection<ConversionJob> _history = new();
        private string? _playgroundLoadedFile;

        public MainWindow()
        {
            // Theme first, so the window never shows a frame in the default colors
            _settingsService = new SettingsService();
            TemplateManager.Instance.Apply(_settingsService.Current.WheelTemplateId);
            AppThemeService.Apply(TemplateManager.Instance.Current);

            InitializeComponent();
            try { Icon = AppIcon.CreateWindowIcon(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { }

            _engine = new ConversionEngine();
            _notifyService = new NotificationService();
            _wheel = new WheelController(_settingsService);

            HistoryList.ItemsSource = _history;

            LoadSettingsIntoUi();
            CheckFFmpegStatus();
            SetupWheel();
            SetupTitleBar();
            SetupPlayground();
            SetupTemplates();
            RefreshInstallState();
            SetupUpdates();
            SetupExtrasPrompt();
            SetupIntegration();

            PropertyChanged += (_, e) =>
            {
                if (e.Property == WindowStateProperty) UpdateMaximizeState();
            };
        }

        /// <summary>Runs jobs for the wheel and the file-manager menus.</summary>
        public NotificationService Notifications => _notifyService;

        /// <summary>The wheel, for the App (second launches with <c>--wheel</c>).</summary>
        public WheelController Wheel => _wheel;

        private bool _loadingSettings;

        private void LoadSettingsIntoUi()
        {
            // Setting each control fires OnSettingChanged, which would save the not-yet-loaded controls' defaults
            _loadingSettings = true;
            var s = _settingsService.Current;
            SaveSameDirRadio.IsChecked = s.SaveInSameDirectory;
            SaveCustomDirRadio.IsChecked = !s.SaveInSameDirectory;
            CustomDirPanel.IsVisible = !s.SaveInSameDirectory;
            CustomDirBox.Text = s.CustomOutputDirectory;
            ShowToastCheck.IsChecked = s.ShowToastOnComplete;
            OverwriteCheck.IsChecked = s.OverwriteExisting;

            WheelScaleSlider.Value = s.WheelScale;
            UpdateWheelScaleLabel(s.WheelScale);
            SoundFeedbackCheck.IsChecked = s.SoundFeedbackEnabled;
            CloseOnReleaseCheck.IsChecked = s.CloseWheelOnKeyRelease;
            ShowKeyHintsCheck.IsChecked = s.ShowKeyHints;
            ReduceMotionCheck.IsChecked = s.ReduceMotion;
            StartHiddenCheck.IsChecked = LinuxPreferences.Current.StartHidden;
            // The overlay re-reads scale, hints, keys and hold-to-use each time it opens; the previews here need these now
            SoundService.Instance.IsEnabled = s.SoundFeedbackEnabled;
            RadialWheelCanvas.ReduceMotion = s.ReduceMotion;

            // Selecting items fires OnKeyMappingChanged; suppress it so half-loaded combos aren't treated as conflicts
            _suppressKeyMappingEvents = true;
            SelectComboItem(ConvertKeyCombo, s.ConvertKey);
            SelectComboItem(ToolsKeyCombo, s.ToolsKey);
            SelectComboItem(ActionsKeyCombo, s.ActionsKey);
            _suppressKeyMappingEvents = false;

            EmptyHistoryKeyRun.Text = KeyDisplayName(s.ConvertKey);
            _loadingSettings = false;
        }

        /// <summary>The settings keep "Win" (shared with Windows); Linux calls that key Super.</summary>
        internal static string KeyDisplayName(string key) => string.Equals(key, "Win", StringComparison.OrdinalIgnoreCase) ? "Super" : key;

        private static void SelectComboItem(ComboBox combo, string key)
        {
            if (combo == null || string.IsNullOrEmpty(key)) return;
            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem cbi && string.Equals(cbi.Tag as string, key, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }
        }

        private void SetupWheel()
        {
            _wheel.FileActionRequested += OnWheelActionAsync;
        }

        /// <summary>
        /// A slice picked on the wheel. Several pictures to PDF ask whether to make one PDF or one each, and Folder Icon
        /// gives every dragged folder the icon picked once; anything else runs on the file the wheel showed (batches of
        /// other options aren't on Linux yet).
        /// </summary>
        private async Task OnWheelActionAsync(IReadOnlyList<string> files, WheelItem option)
        {
            if (option.Id == ConversionEngine.FolderIconActionId)
            {
                var folders = files.Where(Directory.Exists).ToList();
                if (folders.Count == 0) return;
                var (ok, chosen) = await AskFolderIconAsync(folders);
                if (!ok) return;
                foreach (string folder in folders) await ProcessJobAsync(folder, option, chosen);
                return;
            }

            if (files.Count > 1 && ConversionEngine.IsPicturesToPdf(files, option))
            {
                var settings = _settingsService.Current;
                var dlg = new ImagesToPdfDialog(files, settings.LastImagesToPdfMerge);
                if (!await dlg.ShowForResultAsync(DialogOwner)) return;
                settings.LastImagesToPdfMerge = dlg.Merge;
                _settingsService.SaveSettings(settings);

                var pictures = dlg.Pictures;
                if (dlg.Merge)
                {
                    await ProcessJobAsync(pictures[0], option, new ToolParameters { MergeInputs = pictures.ToList() });
                }
                else
                {
                    foreach (string picture in pictures) await ProcessJobAsync(picture, option);
                }
                return;
            }
            await ProcessJobAsync(files[0], option);
        }

        /// <summary>Starts listening for wheel triggers. Called by the App once the window exists (shown or not).</summary>
        public void StartServices()
        {
            try
            {
                _wheel.Start();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[OG Converter] The wheel couldn't start listening: {ex.Message}");
            }
        }

        /// <summary>Writes settings and stops the wheel. Called by the App on exit.</summary>
        public void Shutdown()
        {
            _settingsService.Flush();
            _wheel.Dispose();
        }

        // ------------------------------------------------------------------ title bar

        private void SetupTitleBar()
        {
            // The app draws its own caption buttons only where the window really extends into the title bar; elsewhere
            // the window manager's title bar has them already
            Opened += (_, _) => CaptionButtons.IsVisible = IsExtendedIntoWindowDecorations;

            // The title bar is drawn by the app (like on Windows): drag it to move, double-click to maximize
            TitleBar.PointerPressed += (_, e) =>
            {
                if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null) return;
                if (e.Source is Visual r && r.FindAncestorOfType<RadioButton>(includeSelf: true) != null) return;
                var point = e.GetCurrentPoint(this);
                if (!point.Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2)
                {
                    OnMaximizeWindowClick(this, new RoutedEventArgs());
                    return;
                }
                BeginMoveDrag(e);
            };
        }

        private void OnMinimizeWindowClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaximizeWindowClick(object? sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void UpdateMaximizeState()
        {
            bool max = WindowState == WindowState.Maximized;
            MaximizeBtn.Content = IconExtension.Glyph(max ? "E923" : "E922");
            ToolTip.SetTip(MaximizeBtn, max ? "Restore" : "Maximize");
        }

        /// <summary>Close keeps OG Converter running in the tray (the App decides; without a tray it quits).</summary>
        private void OnCloseWindowClick(object? sender, RoutedEventArgs e) => Close();

        private void OnMinimizeToTrayClick(object? sender, RoutedEventArgs e) => Hide();

        /// <summary>Without a tray host, hiding would leave no way back: the App hides the button.</summary>
        public void SetTrayAvailable(bool available) => TrayButton.IsVisible = available;

        private void OnTabNavChanged(object? sender, RoutedEventArgs e)
        {
            if (ViewOverview == null || ViewSettings == null || ViewAbout == null) return;
            ViewOverview.IsVisible = TabOverview.IsChecked == true;
            ViewSettings.IsVisible = TabSettings.IsChecked == true;
            ViewAbout.IsVisible = TabAbout.IsChecked == true;
        }

        private void OnLinkClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string url }) DesktopActions.OpenUrl(url);
        }

        // ------------------------------------------------------------------ job pipeline

        /// <summary>
        /// Collects any input the tool needs, runs the job with live progress, and records the result.
        /// <paramref name="collected"/> is input already asked for, so no dialog opens.
        /// </summary>
        public async Task ProcessJobAsync(string filePath, WheelItem option, ToolParameters? collected = null)
        {
            ToolParameters? parameters = collected;
            try
            {
                if (collected == null)
                {
                    var (ok, asked) = await CollectToolParametersAsync(filePath, option);
                    if (!ok) return; // user cancelled
                    parameters = asked;
                }
            }
            catch (Exception ex)
            {
                // e.g. encrypted/damaged PDF can't be opened to count pages
                var failed = new ConversionJob { SourcePath = filePath, Option = option, TargetFormat = option.Title, ErrorMessage = ex.Message, EndTime = DateTime.Now };
                _history.Insert(0, failed);
                EmptyHistoryNotice.IsVisible = false;
                if (_settingsService.Current.ShowToastOnComplete) _notifyService.ShowCompleted(failed);
                return;
            }

            // Conversions and tools can take minutes (video): show live progress in Recent activity and a notification
            // until the result replaces them. Actions are instant and skip this, except packing an archive or a
            // password archive, which can take as long as a conversion.
            ProgressNotification? progressNote = null;
            ConversionJob? running = null;
            IProgress<double>? progress = null;
            if (option.Mode != WheelMode.Actions || option.Id is "act_zip" or "act_protect")
            {
                running = new ConversionJob { SourcePath = filePath, Option = option, TargetFormat = option.Title, IsRunning = true };
                _history.Insert(0, running);
                EmptyHistoryNotice.IsVisible = false;

                if (_settingsService.Current.ShowToastOnComplete)
                {
                    string title = option.Id == ConversionEngine.CompressToolId ? "Compressing…"
                                 : option.Id == "act_zip" ? "Creating archive…"
                                 : option.Id == "act_protect" ? "Protecting…"
                                 : option.Mode == WheelMode.Convert ? $"Converting to {option.Title}…"
                                 : $"{option.Title}…";
                    progressNote = _notifyService.ShowProgress(title, filePath);
                }

                // Progress<T> captures this (UI) thread, so both updates land on the dispatcher
                progress = new Progress<double>(f =>
                {
                    running.Progress = f;
                    progressNote?.Report(f);
                });
            }

            var job = await _engine.ExecuteAsync(filePath, option, _settingsService.Current, parameters, progress);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                int index = running != null ? _history.IndexOf(running) : -1;
                if (index >= 0) _history[index] = job;
                else _history.Insert(0, job);
                EmptyHistoryNotice.IsVisible = false;

                if (_settingsService.Current.ShowToastOnComplete)
                {
                    _notifyService.ShowCompleted(job, progressNote);
                }
            });
        }

        /// <summary>The Folder Icon dialog for <paramref name="folders"/>; remembers the colour and symbol picked.</summary>
        private async Task<(bool Ok, ToolParameters? Parameters)> AskFolderIconAsync(IReadOnlyList<string> folders)
        {
            var settings = _settingsService.Current;
            var dlg = new FolderIconDialog(folders, settings.LastFolderIconColor, settings.LastFolderIconSymbol);
            if (!await dlg.ShowForResultAsync(DialogOwner) || dlg.Result == null) return (false, null);
            if (dlg.Result.Count > 0 && dlg.ChosenColor.Length > 0)
            {
                settings.LastFolderIconColor = dlg.ChosenColor;
                settings.LastFolderIconSymbol = dlg.ChosenSymbol;
                _settingsService.SaveSettings(settings);
            }
            return (true, new ToolParameters { FolderIcon = dlg.Result });
        }

        /// <summary>
        /// Shows the input dialog for tools that need more than one click. Ok is false if the user cancelled.
        /// </summary>
        private async Task<(bool Ok, ToolParameters? Parameters)> CollectToolParametersAsync(string filePath, WheelItem option)
        {
            switch (option.Id)
            {
                case ConversionEngine.CompressToolId:
                {
                    // Reopen with the choices last used for this kind of file
                    var settings = _settingsService.Current;
                    string key = (CompressionService.KindOf(filePath) ?? CompressionKind.General).ToString();
                    settings.LastCompression.TryGetValue(key, out var last);

                    var dlg = new CompressDialog(filePath, last, _engine.FFmpegPath);
                    if (!await dlg.ShowForResultAsync(DialogOwner) || dlg.Result == null) return (false, null);
                    settings.LastCompression[key] = dlg.Result;
                    _settingsService.SaveSettings(settings);
                    return (true, new ToolParameters { Compression = dlg.Result });
                }

                case "act_zip":
                {
                    var settings = _settingsService.Current;
                    var dlg = new ArchiveDialog(filePath, settings.LastArchive);
                    if (!await dlg.ShowForResultAsync(DialogOwner) || dlg.Result == null) return (false, null);
                    // Remember the choices, never the password
                    var remembered = dlg.Result.Clone();
                    remembered.Password = string.Empty;
                    settings.LastArchive = remembered;
                    _settingsService.SaveSettings(settings);
                    return (true, new ToolParameters { Archive = dlg.Result });
                }

                case "tool_crop":
                {
                    var dlg = new CropDialog(filePath, FormatRegistry.DetectCategory(filePath) == FileCategory.Video, _engine.FFmpegPath);
                    return await dlg.ShowForResultAsync(DialogOwner) ? (true, dlg.Result) : (false, null);
                }

                case "tool_trim":
                {
                    var dlg = new TrimDialog(filePath, _engine.FFmpegPath);
                    return await dlg.ShowForResultAsync(DialogOwner) ? (true, dlg.Result) : (false, null);
                }

                case "tool_pdf_split":
                {
                    var dlg = new PdfSplitDialog(filePath);
                    return await dlg.ShowForResultAsync(DialogOwner) ? (true, dlg.Result) : (false, null);
                }

                case "tool_pdf_merge":
                {
                    // Windows pre-fills the other PDFs selected in Explorer. Linux has no way to ask the file manager
                    // for its selection, so the list starts with the dragged file and the dialog adds the rest.
                    var dlg = new PdfMergeDialog(new List<string> { filePath });
                    return await dlg.ShowForResultAsync(DialogOwner) ? (true, dlg.Result) : (false, null);
                }

                case ConversionEngine.FolderIconActionId:
                    return await AskFolderIconAsync(new[] { filePath });

                case "tool_pdf_protect":
                case "act_protect":
                {
                    var dlg = new ProtectDialog(filePath, option.Id == "tool_pdf_protect" ? ProtectionMethod.PdfPassword : null);
                    return await dlg.ShowForResultAsync(DialogOwner) ? (true, dlg.Result) : (false, null);
                }

                case "tool_pdf_unlock":
                case "act_unlock":
                {
                    var dlg = new UnlockDialog(filePath);
                    return await dlg.ShowForResultAsync(DialogOwner) ? (true, dlg.Result) : (false, null);
                }

                default:
                    return (true, null);
            }
        }

        /// <summary>Dialogs sit on the dashboard when it's open; from the tray they open on their own.</summary>
        private Window? DialogOwner => IsVisible && WindowState != WindowState.Minimized ? this : null;

        // ------------------------------------------------------------------ try it here

        private void SetupPlayground()
        {
            PlaygroundCard.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            {
                e.DragEffects = DroppedPaths(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            });
            PlaygroundCard.AddHandler(DragDrop.DropEvent, (_, e) =>
            {
                var files = DroppedPaths(e);
                if (files.Count > 0)
                {
                    LoadFileIntoPlayground(files[0]);
                    e.Handled = true;
                }
            });

            PlaygroundWheel.PointerMoved += (_, e) =>
            {
                int idx = PlaygroundWheel.HitTestSlice(e.GetPosition(PlaygroundWheel));
                if (idx != PlaygroundWheel.HoveredIndex) PlaygroundWheel.HoveredIndex = idx;
            };
            PlaygroundWheel.PointerExited += (_, _) => PlaygroundWheel.HoveredIndex = -1;
            PlaygroundWheel.PointerReleased += async (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                int idx = PlaygroundWheel.HoveredIndex;
                if (idx >= 0 && idx < PlaygroundWheel.Items.Count && !string.IsNullOrEmpty(_playgroundLoadedFile))
                {
                    var opt = PlaygroundWheel.Items[idx];
                    string file = _playgroundLoadedFile;
                    OnCancelPlaygroundWheel(this, new RoutedEventArgs());
                    await ProcessJobAsync(file, opt);
                }
            };
        }

        /// <summary>Local paths of files or folders in a drop.</summary>
        internal static List<string> DroppedPaths(DragEventArgs e) =>
            (e.DataTransfer.TryGetFiles() ?? Array.Empty<IStorageItem>())
                .Select(item => item.TryGetLocalPath())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList();

        private async void OnChooseFileClick(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose any file to test OG Converter",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
                    new FilePickerFileType("Images") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp" } },
                    new FilePickerFileType("Videos") { Patterns = new[] { "*.mp4", "*.mov" } },
                    new FilePickerFileType("Audio") { Patterns = new[] { "*.mp3", "*.wav" } }
                }
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) LoadFileIntoPlayground(path);
        }

        internal void LoadFileIntoPlayground(string filePath)
        {
            _playgroundLoadedFile = filePath;
            PlaygroundPrompt.IsVisible = false;
            PlaygroundWheelContainer.IsVisible = true;

            var items = FormatRegistry.GetWheelItems(filePath, WheelMode.Convert);

            PlaygroundWheel.Items = items;
            PlaygroundWheel.CurrentMode = WheelMode.Convert;
            PlaygroundWheel.HoveredIndex = -1;

            PlaygroundWheel.SetFile(filePath, "Click an option");

            PlaygroundInstructions.Text = $"{Path.GetFileName(filePath)}  ·  {items.Count} options. Click one to convert.";
        }

        private void OnShowPlaygroundWheelClick(object? sender, RoutedEventArgs e)
        {
            if (_playgroundLoadedFile != null) _wheel.ShowFor(_playgroundLoadedFile, WheelMode.Convert);
        }

        private void OnCancelPlaygroundWheel(object? sender, RoutedEventArgs e)
        {
            _playgroundLoadedFile = null;
            PlaygroundWheelContainer.IsVisible = false;
            PlaygroundPrompt.IsVisible = true;
        }

        /// <summary>Title bar "Preview the wheel": the wheel for the try-it file, or a sample picture.</summary>
        private void OnTestWheelClick(object? sender, RoutedEventArgs e) => PreviewWheel();

        /// <summary>The wheel for the try-it file, or a sample picture (title bar, tray menu).</summary>
        public void PreviewWheel() =>
            _wheel.ShowFor(_playgroundLoadedFile ?? Path.Combine(Path.GetTempPath(), "Holiday.png"), WheelMode.Convert);

        // ------------------------------------------------------------------ settings

        private void OnSettingChanged(object? sender, RoutedEventArgs e)
        {
            if (_loadingSettings || _settingsService == null || CustomDirPanel == null) return; // still inside InitializeComponent

            var s = _settingsService.Current;
            s.SaveInSameDirectory = SaveSameDirRadio.IsChecked == true;
            s.ShowToastOnComplete = ShowToastCheck.IsChecked == true;
            s.OverwriteExisting = OverwriteCheck.IsChecked == true;
            s.SoundFeedbackEnabled = SoundFeedbackCheck.IsChecked == true;
            s.CloseWheelOnKeyRelease = CloseOnReleaseCheck.IsChecked == true;
            s.ReduceMotion = ReduceMotionCheck.IsChecked == true;
            s.ShowKeyHints = ShowKeyHintsCheck.IsChecked == true;
            SoundService.Instance.IsEnabled = s.SoundFeedbackEnabled;
            RadialWheelCanvas.ReduceMotion = s.ReduceMotion;

            CustomDirPanel.IsVisible = !s.SaveInSameDirectory;
            _settingsService.SaveSettings(s);
        }

        private void OnStartHiddenChanged(object? sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            LinuxPreferences.Current.StartHidden = StartHiddenCheck.IsChecked == true;
            LinuxPreferences.Current.Save();
        }

        private void OnWheelScaleSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loadingSettings || _settingsService == null || WheelScaleText == null) return;

            double val = Math.Round(e.NewValue, 2);
            UpdateWheelScaleLabel(val);

            _settingsService.Current.WheelScale = val;
            _settingsService.SaveSettings(_settingsService.Current);
            // The WheelController applies the new size the next time the wheel opens
        }

        private void UpdateWheelScaleLabel(double val)
        {
            if (WheelScaleText == null) return;
            string label = val switch
            {
                <= 0.47 => "Tiny",
                <= 0.57 => "Mini",
                <= 0.7 => "Small",
                <= 0.95 => "Compact",
                <= 1.05 => "Default",
                <= 1.15 => "Medium",
                <= 1.25 => "Large",
                _ => "Extra large"
            };
            WheelScaleText.Text = $"{label}  ·  {(int)Math.Round(val * 100)}%";
        }

        private async void OnBrowseOutputDirClick(object? sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Custom Output Directory",
                AllowMultiple = false
            });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder) return;

            CustomDirBox.Text = folder;
            _settingsService.Current.CustomOutputDirectory = folder;
            _settingsService.SaveSettings(_settingsService.Current);
        }

        private void OnTestSoundClick(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayTick();
        }

        // ------------------------------------------------------------------ recent activity

        private void OnClearHistoryClick(object? sender, RoutedEventArgs e)
        {
            // Jobs still running stay; their result replaces them when done
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (!_history[i].IsRunning) _history.RemoveAt(i);
            }
            EmptyHistoryNotice.IsVisible = _history.Count == 0;
        }

        private void OnOpenHistoryFileClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string path }) DesktopActions.OpenFile(path);
        }

        private void OnOpenHistoryFolderClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string path }) return;
            if (File.Exists(path)) DesktopActions.RevealInFileManager(path);
            else if (Directory.Exists(path)) DesktopActions.OpenFile(path);
        }

        // ------------------------------------------------------------------ wheel keys

        private bool _suppressKeyMappingEvents;
        private DispatcherTimer? _keyMappingNoticeTimer;

        private void OnKeyMappingChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressKeyMappingEvents || _settingsService == null) return;
            if (ConvertKeyCombo == null || ToolsKeyCombo == null || ActionsKeyCombo == null) return;

            // Resolve conflicts by swapping: the wheel that already owned the new key takes over the old key
            if (sender is ComboBox changed && e.AddedItems.Count > 0 && e.RemovedItems.Count > 0)
            {
                string? newKey = (e.AddedItems[0] as ComboBoxItem)?.Tag as string;
                string? oldKey = (e.RemovedItems[0] as ComboBoxItem)?.Tag as string;

                foreach (var (other, wheelName) in new[] { (ConvertKeyCombo, "Conversion"), (ToolsKeyCombo, "File Tools"), (ActionsKeyCombo, "File Actions") })
                {
                    if (other == changed || newKey == null || oldKey == null) continue;
                    if (string.Equals(GetComboKey(other), newKey, StringComparison.OrdinalIgnoreCase))
                    {
                        _suppressKeyMappingEvents = true;
                        SelectComboItem(other, oldKey);
                        _suppressKeyMappingEvents = false;
                        ShowKeyMappingNotice($"{KeyDisplayName(newKey)} was already used by the {wheelName} wheel, so it now uses {KeyDisplayName(oldKey)}");
                    }
                }
            }

            string convertKey = GetComboKey(ConvertKeyCombo) ?? "Shift";
            string toolsKey = GetComboKey(ToolsKeyCombo) ?? "Control";
            string actionsKey = GetComboKey(ActionsKeyCombo) ?? "Alt";

            var s = _settingsService.Current;
            s.ConvertKey = convertKey;
            s.ToolsKey = toolsKey;
            s.ActionsKey = actionsKey;

            // Safety net: never persist duplicate bindings
            if (s.EnsureUniqueKeyBindings())
            {
                _suppressKeyMappingEvents = true;
                SelectComboItem(ConvertKeyCombo, s.ConvertKey);
                SelectComboItem(ToolsKeyCombo, s.ToolsKey);
                SelectComboItem(ActionsKeyCombo, s.ActionsKey);
                _suppressKeyMappingEvents = false;
                convertKey = s.ConvertKey;
            }

            _settingsService.SaveSettings(s);
            // The WheelController reads the keys (and the overlay's key labels) from the settings on each trigger
            EmptyHistoryKeyRun.Text = KeyDisplayName(convertKey);
        }

        private static string? GetComboKey(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

        private void ShowKeyMappingNotice(string message)
        {
            KeyMappingNoticeText.Text = message;
            KeyMappingNotice.IsVisible = true;

            _keyMappingNoticeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _keyMappingNoticeTimer.Stop();
            _keyMappingNoticeTimer.Tick -= OnKeyMappingNoticeTimeout;
            _keyMappingNoticeTimer.Tick += OnKeyMappingNoticeTimeout;
            _keyMappingNoticeTimer.Start();
        }

        private void OnKeyMappingNoticeTimeout(object? sender, EventArgs e)
        {
            _keyMappingNoticeTimer?.Stop();
            KeyMappingNotice.IsVisible = false;
        }
    }
}
