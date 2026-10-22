using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Views;

namespace OgConverter
{
    public partial class MainWindow : Window
    {
        private readonly ConversionEngine _engine;
        private readonly SettingsService _settingsService;
        private readonly NotificationService _notifyService;
        private readonly GlobalHookService _hookService;
        private readonly TrayIconService _trayService;
        private readonly RadialWheelOverlay _overlay;
        private readonly DragProbeWindow _dragProbe;

        private readonly ObservableCollection<ConversionJob> _history = new();
        private string? _playgroundLoadedFile;

        public MainWindow()
        {
            // Theme first, so the window never shows a frame in the default colors
            _settingsService = new SettingsService();
            TemplateManager.Instance.Apply(_settingsService.Current.WheelTemplateId);
            AppThemeService.Apply(TemplateManager.Instance.Current);

            InitializeComponent();

            _engine = new ConversionEngine();
            _notifyService = new NotificationService();
            _hookService = new GlobalHookService();
            _trayService = new TrayIconService();
            _overlay = new RadialWheelOverlay();
            _dragProbe = new DragProbeWindow();

            HistoryList.ItemsSource = _history;

            LoadSettingsIntoUi();
            CheckFFmpegStatus();
            SetupGlobalHooks();
            SetupOverlay();
            SetupTray();
            SetupPlaygroundWheelInteraction();
            SetupTemplates();
            RefreshInstallState();
            SetupUpdates();
            SetupExtrasPrompt();

            StateChanged += (_, _) => UpdateMaximizeState();

            Closing += MainWindow_Closing;
        }

        private bool _loadingSettings;

        private void LoadSettingsIntoUi()
        {
            // Setting each control fires OnSettingChanged, which would save the not-yet-loaded controls' defaults
            _loadingSettings = true;
            var s = _settingsService.Current;
            SaveSameDirRadio.IsChecked = s.SaveInSameDirectory;
            SaveCustomDirRadio.IsChecked = !s.SaveInSameDirectory;
            CustomDirPanel.Visibility = s.SaveInSameDirectory ? Visibility.Collapsed : Visibility.Visible;
            CustomDirBox.Text = s.CustomOutputDirectory;
            ShowToastCheck.IsChecked = s.ShowToastOnComplete;
            OverwriteCheck.IsChecked = s.OverwriteExisting;

            WheelScaleSlider.Value = s.WheelScale;
            UpdateWheelScaleLabel(s.WheelScale);
            SoundFeedbackCheck.IsChecked = s.SoundFeedbackEnabled;
            CloseOnReleaseCheck.IsChecked = s.CloseWheelOnKeyRelease;
            _overlay.SetHoldToUse(s.CloseWheelOnKeyRelease);
            SoundService.Instance.IsEnabled = s.SoundFeedbackEnabled;
            ShowKeyHintsCheck.IsChecked = s.ShowKeyHints;
            ReduceMotionCheck.IsChecked = s.ReduceMotion;
            RadialWheelCanvas.ReduceMotion = s.ReduceMotion;

            // Selecting items fires OnKeyMappingChanged; suppress it so half-loaded combos aren't treated as conflicts
            _suppressKeyMappingEvents = true;
            SelectComboItem(ConvertKeyCombo, s.ConvertKey);
            SelectComboItem(ToolsKeyCombo, s.ToolsKey);
            SelectComboItem(ActionsKeyCombo, s.ActionsKey);
            _suppressKeyMappingEvents = false;

            _hookService.UpdateKeyBindings(s.ConvertKey, s.ToolsKey, s.ActionsKey);
            _overlay.UpdateKeyLabels(s.ConvertKey, s.ToolsKey, s.ActionsKey);
            EmptyHistoryKeyRun.Text = s.ConvertKey;
            _overlay.ApplyScale(s.WheelScale, s.ShowKeyHints);
            _loadingSettings = false;
        }

        private static void SelectComboItem(ComboBox combo, string text)
        {
            if (combo == null || string.IsNullOrEmpty(text)) return;
            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }
        }

        private void CheckFFmpegStatus()
        {
            if (FFmpegStatusText == null || FFmpegBadge == null) return;

            bool ok = _engine.IsFFmpegAvailable;
            FFmpegStatusText.Text = ok ? "Ready" : "Not found";
            FFmpegStatusText.SetResourceReference(TextBlock.ForegroundProperty, ok ? "SuccessBrush" : "WarningBrush");
            FFmpegBadge.SetResourceReference(Border.BackgroundProperty, ok ? "SuccessSubtleBrush" : "WarningSubtleBrush");
            FFmpegPathText.Text = ok
                ? _engine.FFmpegPath
                : "Optional. Install it (winget install Gyan.FFmpeg), then restart OG Converter, for video, audio, GIF, WebP, AVIF and JPEG XL";
            FFmpegPathText.ToolTip = ok ? _engine.FFmpegPath : null;

            bool docs = DocumentEngine.HasAnyBackend;
            DocEngineStatusText.Text = docs ? "Ready" : "Not found";
            DocEngineStatusText.SetResourceReference(TextBlock.ForegroundProperty, docs ? "SuccessBrush" : "WarningBrush");
            DocEngineBadge.SetResourceReference(Border.BackgroundProperty, docs ? "SuccessSubtleBrush" : "WarningSubtleBrush");
            DocEngineText.Text = docs ? "Using " + DocumentEngine.DescribeBackends() : DocumentEngine.LibreOfficeInstallHint;

            InstallFFmpegButton.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            InstallLibreOfficeButton.Visibility = docs ? Visibility.Collapsed : Visibility.Visible;

            // Status only: Edge ships with Windows
            bool browser = BrowserRenderService.IsAvailable;
            BrowserStatusText.Text = browser ? "Ready" : "Not found";
            BrowserStatusText.SetResourceReference(TextBlock.ForegroundProperty, browser ? "SuccessBrush" : "WarningBrush");
            BrowserBadge.SetResourceReference(Border.BackgroundProperty, browser ? "SuccessSubtleBrush" : "WarningSubtleBrush");
            BrowserEngineText.Text = browser ? BrowserRenderService.BrowserPath : BrowserRenderService.MissingBrowserHint;
            BrowserEngineText.ToolTip = browser ? BrowserRenderService.BrowserPath : null;
        }

        private void SetupGlobalHooks()
        {
            // Mouse traffic is only forwarded from the hook thread while the wheel or the drag check is open
            _overlay.IsVisibleChanged += (_, _) => _hookService.TrackMouse = _overlay.IsVisible || _dragProbe.IsVisible;
            _dragProbe.IsVisibleChanged += (_, _) => _hookService.TrackMouse = _overlay.IsVisible || _dragProbe.IsVisible;

            _hookService.TriggerWheelRequested += (mode, screenPt, filePath) =>
            {
                // Already open: the key switches wheels
                if (_overlay.IsVisible)
                {
                    _overlay.ShowAt(screenPt, mode, filePath != null ? new[] { filePath } : null);
                    return;
                }

                // The button is held, but that's also a game's fire button or a text selection: the wheel waits until
                // the drag check sees files or folders
                if (_dragProbe.IsVisible) _dragProbe.Mode = mode;
                else if (!DragProbeWindow.FullScreenAppInFront) _dragProbe.Begin(mode, screenPt);
            };

            _dragProbe.FileDragConfirmed += (mode, screenPt, files) =>
            {
                _overlay.ShowAt(screenPt, mode, files);
            };

            _hookService.DismissWheelRequested += () =>
            {
                _dragProbe.Cancel();
                _overlay.Dismiss();
            };

            _hookService.MouseMoved += (screenPt) =>
            {
                _dragProbe.MoveTo(screenPt);
                _overlay.UpdateCursorPosition(screenPt);
            };

            _hookService.MouseReleased += () =>
            {
                _dragProbe.Cancel();
                _overlay.HandleMouseDrop();
            };

            // Hold-to-use mode: letting go of the active wheel's key cancels it
            _hookService.ShortcutReleased += (mode) =>
            {
                if (!_settingsService.Current.CloseWheelOnKeyRelease) return;
                if (_dragProbe.IsVisible && _dragProbe.Mode == mode) _dragProbe.Cancel();
                if (_overlay.IsVisible && _overlay.CurrentMode == mode)
                {
                    _overlay.Dismiss();
                }
            };

            try
            {
                _hookService.Start();
            }
            catch { }
        }

        /// <summary>Opens links (About tab) in the default browser.</summary>
        private void OnLinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void SetupOverlay()
        {
            _overlay.FileActionRequested += async (files, option) =>
            {
                // Merge takes all the dragged PDFs as one job; instant actions (terminal, reveal, copy path) stay on the
                // first file rather than opening a window per file
                if (files.Count == 1 || option.Id == "tool_pdf_merge" || !ShowsProgress(option)) await ProcessJobAsync(files[0], option, files);
                else await ProcessBatchAsync(files, option);
            };
        }

        private void SetupTray()
        {
            _trayService.Initialize(this);
            _trayService.TrayClicked += () =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };
        }

        /// <summary>One file (<paramref name="dragged"/>: everything that was dropped, for Merge).</summary>
        private async Task ProcessJobAsync(string filePath, WheelItem option, IReadOnlyList<string>? dragged = null)
        {
            ToolParameters? parameters;
            try
            {
                if (!TryCollectToolParameters(filePath, option, dragged, out parameters)) return; // user cancelled
            }
            catch (Exception ex)
            {
                // e.g. encrypted/damaged PDF can't be opened to count pages
                var failed = new ConversionJob { SourcePath = filePath, Option = option, TargetFormat = option.Title, ErrorMessage = ex.Message, EndTime = DateTime.Now };
                _history.Insert(0, failed);
                EmptyHistoryNotice.Visibility = Visibility.Collapsed;
                if (_settingsService.Current.ShowToastOnComplete) _notifyService.ShowCompleted(failed);
                return;
            }

            // Conversions and tools can take minutes (video): show live progress in Recent activity and a toast
            // until the result replaces them. Actions are instant and skip this, except packing an archive or a
            // password archive, which can take as long as a conversion.
            ToastNotificationWindow? progressToast = null;
            ConversionJob? running = null;
            IProgress<double>? progress = null;
            if (ShowsProgress(option))
            {
                running = NewPlaceholder(filePath, option);
                _history.Insert(0, running);
                EmptyHistoryNotice.Visibility = Visibility.Collapsed;
                UpdateCancelAllButton();

                if (_settingsService.Current.ShowToastOnComplete)
                {
                    progressToast = _notifyService.ShowProgress(ProgressTitle(option, 1), filePath, running.Cancel);
                }

                // Progress<T> captures this (UI) thread, so both updates land on the dispatcher
                progress = new Progress<double>(f =>
                {
                    running.Progress = f;
                    progressToast?.ReportProgress(f);
                });
            }

            var job = running != null
                ? await RunQueuedAsync(running, parameters, progress)
                : await _engine.ExecuteAsync(filePath, option, _settingsService.Current, parameters, progress);

            Dispatcher.Invoke(() =>
            {
                ReplacePlaceholder(running, job);

                if (_settingsService.Current.ShowToastOnComplete)
                {
                    _notifyService.ShowCompleted(job, progressToast);
                }
            });
        }

        /// <summary>
        /// Several dropped files, one option: asks for any tool input up front, then queues one job per file
        /// (<see cref="JobQueue"/> decides how many run at once) under a single progress toast.
        /// </summary>
        private async Task ProcessBatchAsync(IReadOnlyList<string> files, WheelItem option)
        {
            var settings = _settingsService.Current;
            var failed = new List<ConversionJob>();
            var work = new List<(string File, ToolParameters? Parameters)>();

            // Compress (per kind of file) and Zip ask once for the batch; the other dialogs depend on the file itself
            var shared = new Dictionary<string, ToolParameters?>();
            foreach (string file in files)
            {
                // The wheel showed the first file's options; skip files the pick doesn't apply to
                if (!FormatRegistry.GetWheelItems(file, option.Mode).Any(i => i.Id == option.Id))
                {
                    failed.Add(new ConversionJob { SourcePath = file, Option = option, TargetFormat = option.Title, EndTime = DateTime.Now,
                                                   ErrorMessage = $"{option.Title} isn't available for this kind of file." });
                    continue;
                }

                string? shareKey = option.Id == ConversionEngine.CompressToolId ? "compress:" + (CompressionService.KindOf(file) ?? CompressionKind.General)
                                 : option.Id == "act_zip" ? "zip" : null;
                ToolParameters? parameters;
                if (shareKey != null && shared.TryGetValue(shareKey, out var first))
                {
                    // Jobs adjust their options while running, so each gets its own copy
                    parameters = first == null ? null : new ToolParameters { Compression = first.Compression?.Clone(), Archive = first.Archive?.Clone() };
                }
                else
                {
                    try
                    {
                        // Cancelling a dialog cancels the batch; nothing has started yet
                        if (!TryCollectToolParameters(file, option, null, out parameters)) return;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new ConversionJob { SourcePath = file, Option = option, TargetFormat = option.Title, ErrorMessage = ex.Message, EndTime = DateTime.Now });
                        continue;
                    }
                    if (shareKey != null) shared[shareKey] = parameters;
                }
                work.Add((file, parameters));
            }

            // Newest first, like single jobs: insert in reverse so the batch reads top to bottom in drop order
            foreach (var f in Enumerable.Reverse(failed)) _history.Insert(0, f);
            var placeholders = work.Select(w => NewPlaceholder(w.File, option)).ToList();
            for (int i = placeholders.Count - 1; i >= 0; i--) _history.Insert(0, placeholders[i]);
            if (_history.Count > 0) EmptyHistoryNotice.Visibility = Visibility.Collapsed;
            UpdateCancelAllButton();

            int total = files.Count;
            var results = new List<ConversionJob>(failed);
            var finished = new HashSet<ConversionJob>();
            var started = DateTime.Now;

            ToastNotificationWindow? toast = null;
            if (settings.ShowToastOnComplete && work.Count > 0)
            {
                toast = _notifyService.ShowProgress(ProgressTitle(option, total), files[0], () => placeholders.ForEach(p => p.Cancel()));
            }

            // Overall progress: finished and skipped files count whole, running ones by their own progress
            void ReportBatch()
            {
                if (toast == null) return;
                var active = placeholders.Where(p => !p.IsQueued && !finished.Contains(p)).ToList();
                double fraction = (results.Count + active.Sum(p => p.HasProgress ? p.Progress : 0)) / total;
                int failures = results.Count(r => !r.IsSuccess);
                string subtitle = $"{results.Count} of {total} done" + (active.Count > 0 ? $"  ·  {active.Count} running" : "")
                                + (failures > 0 ? $"  ·  {failures} failed" : "");
                toast.ReportBatch(ProgressTitle(option, total), subtitle, Math.Clamp(fraction, 0, 1));
            }

            var tasks = work.Select(async (w, i) =>
            {
                var running = placeholders[i];
                var progress = new Progress<double>(f =>
                {
                    running.Progress = f;
                    ReportBatch();
                });
                var job = await RunQueuedAsync(running, w.Parameters, progress, onStarted: ReportBatch);

                // Back on the UI thread (awaited from it)
                finished.Add(running);
                results.Add(job);
                ReplacePlaceholder(running, job);
                ReportBatch();
            }).ToList();
            ReportBatch();
            await Task.WhenAll(tasks);

            if (!settings.ShowToastOnComplete) return;
            ShowBatchSummary(option, total, results, DateTime.Now - started, toast);
        }

        /// <summary>The batch toast's final state: counts, the first failure's reason, and the shared output folder.</summary>
        private void ShowBatchSummary(WheelItem option, int total, List<ConversionJob> results, TimeSpan elapsed, ToastNotificationWindow? toast)
        {
            int ok = results.Count(r => r.IsSuccess);
            int cancelled = results.Count(r => r.ErrorMessage == ConversionEngine.CancelledMessage);
            int failures = results.Count - ok - cancelled;

            string verb = option.Mode == WheelMode.Convert ? $"converted to {option.Title}" : "done";
            string title = ok == total ? $"{total} files {verb}" : $"{ok} of {total} files {verb}";
            var parts = new List<string>();
            if (failures > 0) parts.Add($"{failures} failed");
            if (cancelled > 0) parts.Add($"{cancelled} cancelled");
            var firstError = results.FirstOrDefault(r => !r.IsSuccess && r.ErrorMessage != ConversionEngine.CancelledMessage);
            string subtitle = parts.Count == 0
                ? string.Join(", ", results.Where(r => r.IsSuccess).Select(r => r.TargetFileName).Take(3)) + (ok > 3 ? $" +{ok - 3} more" : "")
                : string.Join("  ·  ", parts);
            // The first failure's reason (all of them are in Recent activity), else the time taken
            string metrics = firstError != null ? $"{firstError.SourceFileName}: {firstError.ErrorMessage}" : $"{elapsed.TotalSeconds:0.0}s";

            var folders = results.Where(r => r.IsSuccess && File.Exists(r.TargetPath))
                                 .Select(r => Path.GetDirectoryName(r.TargetPath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string? folder = folders.Count == 1 ? folders[0] : null;

            var summaryToast = toast is { IsClosed: false } ? toast : new ToastNotificationWindow();
            summaryToast.ShowBatchResult(ok == total, title, subtitle, metrics, folder);
        }

        /// <summary>Conversions and tools (and packing or protecting an archive) get a progress row; other actions are instant.</summary>
        private static bool ShowsProgress(WheelItem option) => option.Mode != WheelMode.Actions || option.Id is "act_zip" or "act_protect";

        private static string ProgressTitle(WheelItem option, int count)
        {
            string files = count > 1 ? $" {count} files" : "";
            return option.Id == ConversionEngine.CompressToolId ? $"Compressing{files}…"
                 : option.Id == "act_zip" ? (count > 1 ? $"Creating {count} archives…" : "Creating archive…")
                 : option.Id == "act_protect" ? $"Protecting{files}…"
                 : option.Mode == WheelMode.Convert ? $"Converting{files} to {option.Title}…"
                 : count > 1 ? $"{option.Title}: {count} files…" : $"{option.Title}…";
        }

        private static ConversionJob NewPlaceholder(string filePath, WheelItem option) => new()
        {
            SourcePath = filePath, Option = option, TargetFormat = option.Title, IsRunning = true, IsQueued = true,
            Cancellation = new System.Threading.CancellationTokenSource()
        };

        /// <summary>Waits for a queue slot, then runs the job; a cancelled wait comes back as a cancelled job.</summary>
        private async Task<ConversionJob> RunQueuedAsync(ConversionJob running, ToolParameters? parameters, IProgress<double>? progress, Action? onStarted = null)
        {
            var token = running.Cancellation!.Token;
            try
            {
                using var slot = await JobQueue.EnterAsync(running.SourcePath, token);
                running.IsQueued = false;
                onStarted?.Invoke();
                return await _engine.ExecuteAsync(running.SourcePath, running.Option, _settingsService.Current, parameters, progress, token);
            }
            catch (OperationCanceledException)
            {
                return new ConversionJob { SourcePath = running.SourcePath, Option = running.Option, TargetFormat = running.TargetFormat,
                                           ErrorMessage = ConversionEngine.CancelledMessage, EndTime = DateTime.Now };
            }
        }

        /// <summary>Puts the finished job where its running placeholder was (or on top).</summary>
        private void ReplacePlaceholder(ConversionJob? running, ConversionJob job)
        {
            int index = running != null ? _history.IndexOf(running) : -1;
            if (index >= 0) _history[index] = job;
            else _history.Insert(0, job);
            EmptyHistoryNotice.Visibility = Visibility.Collapsed;
            UpdateCancelAllButton();
        }

        private void UpdateCancelAllButton()
        {
            int running = _history.Count(j => j.IsRunning && j.Cancellation != null);
            CancelAllButton.Visibility = running > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnCancelHistoryJobClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ConversionJob job }) job.Cancel();
        }

        private void OnCancelAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var job in _history.Where(j => j.IsRunning).ToList()) job.Cancel();
        }

        /// <summary>
        /// Shows the input dialog for tools that need more than one click. Returns false if the user cancelled.
        /// <paramref name="dragged"/> is everything that was dropped (Merge starts with those PDFs).
        /// </summary>
        private bool TryCollectToolParameters(string filePath, WheelItem option, IReadOnlyList<string>? dragged, out ToolParameters? parameters)
        {
            parameters = null;

            switch (option.Id)
            {
                case ConversionEngine.CompressToolId:
                {
                    // Reopen with the choices last used for this kind of file
                    var settings = _settingsService.Current;
                    string key = (CompressionService.KindOf(filePath) ?? CompressionKind.General).ToString();
                    settings.LastCompression.TryGetValue(key, out var last);

                    var dlg = new CompressDialog(filePath, last, _engine.FFmpegPath);
                    if (dlg.ShowDialog() != true || dlg.Result == null) return false;
                    settings.LastCompression[key] = dlg.Result;
                    _settingsService.SaveSettings(settings);
                    parameters = new ToolParameters { Compression = dlg.Result };
                    return true;
                }

                case "act_zip":
                {
                    var settings = _settingsService.Current;
                    var dlg = new ArchiveDialog(filePath, settings.LastArchive);
                    if (dlg.ShowDialog() != true || dlg.Result == null) return false;
                    // Remember the choices, never the password
                    var remembered = dlg.Result.Clone();
                    remembered.Password = string.Empty;
                    settings.LastArchive = remembered;
                    _settingsService.SaveSettings(settings);
                    parameters = new ToolParameters { Archive = dlg.Result };
                    return true;
                }

                case "tool_crop":
                {
                    var dlg = new CropDialog(filePath, FormatRegistry.DetectCategory(filePath) == FileCategory.Video, _engine.FFmpegPath);
                    if (dlg.ShowDialog() != true) return false;
                    parameters = dlg.Result;
                    return true;
                }

                case "tool_trim":
                {
                    var dlg = new TrimDialog(filePath, _engine.FFmpegPath);
                    if (dlg.ShowDialog() != true) return false;
                    parameters = dlg.Result;
                    return true;
                }

                case "tool_pdf_split":
                {
                    var dlg = new PdfSplitDialog(filePath);
                    if (dlg.ShowDialog() != true) return false;
                    parameters = dlg.Result;
                    return true;
                }

                case "tool_pdf_merge":
                {
                    // Pre-fill with the other dragged PDFs, or else those selected in Explorer, keeping the dragged file first
                    var files = new List<string> { filePath };
                    var selection = dragged is { Count: > 1 } ? dragged.ToList() : ShellSelectionService.GetSelectedOrFocusedFiles();
                    if (selection.Exists(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        files.AddRange(selection.Where(p => !string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase)));
                    }

                    var dlg = new PdfMergeDialog(files);
                    if (dlg.ShowDialog() != true) return false;
                    parameters = dlg.Result;
                    return true;
                }

                case "tool_pdf_protect":
                case "act_protect":
                {
                    var dlg = new ProtectDialog(filePath, option.Id == "tool_pdf_protect" ? ProtectionMethod.PdfPassword : null);
                    if (dlg.ShowDialog() != true) return false;
                    parameters = dlg.Result;
                    return true;
                }

                case "tool_pdf_unlock":
                case "act_unlock":
                {
                    var dlg = new UnlockDialog(filePath);
                    if (dlg.ShowDialog() != true) return false;
                    parameters = dlg.Result;
                    return true;
                }

                default:
                    return true;
            }
        }

        private void SetupPlaygroundWheelInteraction()
        {
            PlaygroundWheel.MouseMove += (s, e) =>
            {
                Point pos = e.GetPosition(PlaygroundWheel);
                int idx = PlaygroundWheel.HitTestSlice(pos);
                if (idx != PlaygroundWheel.HoveredIndex)
                {
                    PlaygroundWheel.HoveredIndex = idx;
                }
            };

            PlaygroundWheel.MouseLeave += (s, e) =>
            {
                PlaygroundWheel.HoveredIndex = -1;
            };

            PlaygroundWheel.MouseLeftButtonUp += async (s, e) =>
            {
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

        #region Playground Drag and Drop Events

        private void OnPlaygroundDragEnter(object sender, DragEventArgs e)
        {
            if (DroppedFiles.HasPaths(e.Data))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void OnPlaygroundDragOver(object sender, DragEventArgs e) => OnPlaygroundDragEnter(sender, e);

        private void OnPlaygroundDragLeave(object sender, DragEventArgs e)
        {
        }

        private void OnPlaygroundDrop(object sender, DragEventArgs e)
        {
            var files = DroppedFiles.GetPaths(e.Data);
            if (files.Count > 0)
            {
                LoadFileIntoPlayground(files[0]);
                e.Handled = true;
            }
        }

        private void OnChooseFileClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose any file to test OG Converter",
                Filter = "All Files (*.*)|*.*|Images (*.jpg;*.png;*.webp)|*.jpg;*.png;*.webp|Videos (*.mp4;*.mov)|*.mp4;*.mov|Audio (*.mp3;*.wav)|*.mp3;*.wav"
            };

            if (dlg.ShowDialog() == true)
            {
                LoadFileIntoPlayground(dlg.FileName);
            }
        }

        internal void LoadFileIntoPlayground(string filePath)
        {
            _playgroundLoadedFile = filePath;
            PlaygroundPrompt.Visibility = Visibility.Collapsed;
            PlaygroundWheelContainer.Visibility = Visibility.Visible;

            var items = FormatRegistry.GetWheelItems(filePath, WheelMode.Convert);
            PlaygroundWheel.Items = items;
            PlaygroundWheel.CurrentMode = WheelMode.Convert;
            PlaygroundWheel.HoveredIndex = -1;

            PlaygroundWheel.SetFile(filePath, "Click an option");

            PlaygroundInstructions.Text = $"{Path.GetFileName(filePath)}  ·  {items.Count} options. Click one to convert.";
        }

        private void OnCancelPlaygroundWheel(object sender, RoutedEventArgs e)
        {
            _playgroundLoadedFile = null;
            PlaygroundWheelContainer.Visibility = Visibility.Collapsed;
            PlaygroundPrompt.Visibility = Visibility.Visible;
        }

        #endregion

        #region Actions and Settings

        private void OnTestWheelClick(object sender, RoutedEventArgs e)
        {
            _overlay.ShowCentered(this, WheelMode.Convert);
        }

        /// <summary>Started with Windows: create the window (hooks and tray need its handle) without showing it.</summary>
        public void StartHidden()
        {
            new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        }

        private void OnMinimizeToTrayClick(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Clean up hooks and tray
            _settingsService.Flush();
            _hookService.Dispose();
            _trayService.Dispose();
            _dragProbe.Close();
            _overlay.Close();
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (_overlay == null || _loadingSettings) return; // still inside InitializeComponent

            var s = _settingsService.Current;
            s.SaveInSameDirectory = SaveSameDirRadio.IsChecked == true;
            s.ShowToastOnComplete = ShowToastCheck.IsChecked == true;
            s.OverwriteExisting = OverwriteCheck.IsChecked == true;
            s.SoundFeedbackEnabled = SoundFeedbackCheck.IsChecked == true;
            s.CloseWheelOnKeyRelease = CloseOnReleaseCheck.IsChecked == true;
            _overlay.SetHoldToUse(s.CloseWheelOnKeyRelease);
            s.ReduceMotion = ReduceMotionCheck.IsChecked == true;
            RadialWheelCanvas.ReduceMotion = s.ReduceMotion;
            if (s.ShowKeyHints != (ShowKeyHintsCheck.IsChecked == true))
            {
                s.ShowKeyHints = ShowKeyHintsCheck.IsChecked == true;
                _overlay.ApplyScale(s.WheelScale, s.ShowKeyHints);
            }

            SoundService.Instance.IsEnabled = s.SoundFeedbackEnabled;

            if (CustomDirPanel != null)
            {
                CustomDirPanel.Visibility = s.SaveInSameDirectory ? Visibility.Collapsed : Visibility.Visible;
            }

            _settingsService.SaveSettings(s);
        }

        private void OnWheelScaleSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_overlay == null || WheelScaleText == null) return;

            double val = Math.Round(e.NewValue, 2);
            UpdateWheelScaleLabel(val);

            _settingsService.Current.WheelScale = val;
            _settingsService.SaveSettings(_settingsService.Current);

            _overlay.ApplyScale(val, _settingsService.Current.ShowKeyHints);
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

        private void OnBrowseOutputDirClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select Custom Output Directory"
            };

            if (dlg.ShowDialog() == true)
            {
                CustomDirBox.Text = dlg.FolderName;
                _settingsService.Current.CustomOutputDirectory = dlg.FolderName;
                _settingsService.SaveSettings(_settingsService.Current);
            }
        }

        private void OnClearHistoryClick(object sender, RoutedEventArgs e)
        {
            // Jobs still running stay; their result replaces them when done
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (!_history[i].IsRunning) _history.RemoveAt(i);
            }
            EmptyHistoryNotice.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnOpenHistoryFileClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }

        private void OnOpenHistoryFolderClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                if (File.Exists(path))
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                else if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
        }

        private void OnMinimizeWindowClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximizeWindowClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void UpdateMaximizeState()
        {
            bool max = WindowState == WindowState.Maximized;
            MaximizeBtn.Content = max ? "\uE923" : "\uE922";
            MaximizeBtn.ToolTip = max ? "Restore" : "Maximize";
            // A maximized WindowChrome window overhangs the screen by the resize border; pad it back in
            ((FrameworkElement)Content).Margin = max ? new Thickness(7) : new Thickness(0);
        }

        private void OnCloseWindowClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnTabNavChanged(object sender, RoutedEventArgs e)
        {
            if (ViewOverview == null || ViewSettings == null || ViewAbout == null) return;

            if (TabOverview?.IsChecked == true)
            {
                ViewOverview.Visibility = Visibility.Visible;
                ViewSettings.Visibility = Visibility.Collapsed;
                ViewAbout.Visibility = Visibility.Collapsed;
            }
            else if (TabSettings?.IsChecked == true)
            {
                ViewOverview.Visibility = Visibility.Collapsed;
                ViewSettings.Visibility = Visibility.Visible;
                ViewAbout.Visibility = Visibility.Collapsed;
            }
            else if (TabAbout?.IsChecked == true)
            {
                ViewOverview.Visibility = Visibility.Collapsed;
                ViewSettings.Visibility = Visibility.Collapsed;
                ViewAbout.Visibility = Visibility.Visible;
            }
        }

        private bool _suppressKeyMappingEvents;
        private System.Windows.Threading.DispatcherTimer? _keyMappingNoticeTimer;

        private void OnKeyMappingChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressKeyMappingEvents) return;
            if (_hookService == null || ConvertKeyCombo == null || ToolsKeyCombo == null || ActionsKeyCombo == null) return;

            // Resolve conflicts by swapping: the wheel that already owned the new key takes over the old key
            if (sender is ComboBox changed && e.AddedItems.Count > 0 && e.RemovedItems.Count > 0)
            {
                string? newKey = (e.AddedItems[0] as ComboBoxItem)?.Content?.ToString();
                string? oldKey = (e.RemovedItems[0] as ComboBoxItem)?.Content?.ToString();

                foreach (var (other, wheelName) in new[] { (ConvertKeyCombo, "Conversion"), (ToolsKeyCombo, "File Tools"), (ActionsKeyCombo, "File Actions") })
                {
                    if (other == changed || newKey == null || oldKey == null) continue;
                    if (string.Equals(GetComboKey(other), newKey, StringComparison.OrdinalIgnoreCase))
                    {
                        _suppressKeyMappingEvents = true;
                        SelectComboItem(other, oldKey);
                        _suppressKeyMappingEvents = false;
                        ShowKeyMappingNotice($"{newKey} was already used by the {wheelName} wheel, so it now uses {oldKey}");
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
                (convertKey, toolsKey, actionsKey) = (s.ConvertKey, s.ToolsKey, s.ActionsKey);
            }

            _settingsService.SaveSettings(s);

            _hookService.UpdateKeyBindings(convertKey, toolsKey, actionsKey);
            _overlay.UpdateKeyLabels(convertKey, toolsKey, actionsKey);
            EmptyHistoryKeyRun.Text = convertKey;
        }

        private static string? GetComboKey(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Content?.ToString();

        private void ShowKeyMappingNotice(string message)
        {
            KeyMappingNoticeText.Text = message;
            KeyMappingNotice.Visibility = Visibility.Visible;

            _keyMappingNoticeTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _keyMappingNoticeTimer.Stop();
            _keyMappingNoticeTimer.Tick -= OnKeyMappingNoticeTimeout;
            _keyMappingNoticeTimer.Tick += OnKeyMappingNoticeTimeout;
            _keyMappingNoticeTimer.Start();
        }

        private void OnKeyMappingNoticeTimeout(object? sender, EventArgs e)
        {
            _keyMappingNoticeTimer?.Stop();
            KeyMappingNotice.Visibility = Visibility.Collapsed;
        }

        private void OnTestSoundClick(object sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayTick();
        }

        #endregion
    }
}