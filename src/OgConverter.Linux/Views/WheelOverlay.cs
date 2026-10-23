using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Wheel;

namespace OgConverter.Views
{
    /// <summary>
    /// The wheel's own window: transparent, undecorated, always on top, centered on the cursor. The Linux port of the
    /// Windows RadialWheelOverlay.
    /// </summary>
    /// <remarks>
    /// Two ways in. <b>Drag</b> (the GNOME extension saw a key tapped during a drag): the dragged file is unknown until the
    /// drag reaches this window, so the window is a drop target; drag positions hover the options and the drop picks one.
    /// <b>Click</b> (<c>--wheel &lt;file&gt;</c>, the dashboard): the file is known; the pointer hovers, a click picks, and Esc
    /// or clicking elsewhere closes it.
    /// </remarks>
    public sealed class WheelOverlay : Window
    {
        /// <summary>After the button goes up, how long a drop may still arrive (X11 drag-and-drop runs behind the extension's call).</summary>
        private static readonly TimeSpan DropGrace = TimeSpan.FromMilliseconds(450);

        /// <summary>A slice was picked: the files (the shown one first, then the rest of a multi-file drag) and the option.</summary>
        public event Action<IReadOnlyList<string>, WheelItem>? FileActionRequested;

        public RadialWheelCanvas WheelCanvas { get; }

        private readonly Panel _root;
        private readonly Border _hintBar;
        private readonly LayoutTransformControl _hintHost;
        private readonly Border _convertBadge, _toolsBadge, _actionsBadge, _cancelBadge;
        private readonly Border _convertKeyCap, _toolsKeyCap, _actionsKeyCap, _cancelKeyCap;
        private readonly TextBlock _convertKeyText, _toolsKeyText, _actionsKeyText, _cancelKeyText;
        private readonly TextBlock _convertLabel, _toolsLabel, _actionsLabel, _cancelLabel;
        private readonly Border _separator;

        private string? _currentFilePath;
        /// <summary>Everything in the drag, when it held more than the shown file.</summary>
        private List<string> _draggedFiles = new();
        private WheelMode _currentMode = WheelMode.Convert;
        private string _convertKey = "Shift";
        private double _scale = 1.0;
        private bool _showHints = true;
        private bool _dragHasFile;
        private bool _clickMode;
        private bool _open;
        private int _showToken;
        private DispatcherTimer? _releaseTimer;

        // Template chrome for the hint bar
        private IBrush _keyCapBrush = Brushes.Transparent, _keyCapEdgeBrush = Brushes.Gray, _keyTextBrush = Brushes.Gray, _hintLabelBrush = Brushes.White;

        public WheelOverlay()
        {
            // A title nothing else has, so the GNOME extension can find and move this window
            Title = $"OG Converter Wheel {Environment.ProcessId}";
            WindowDecorations = WindowDecorations.None;
            CanResize = false;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.Manual;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Focusable = true;

            WheelCanvas = new RadialWheelCanvas
            {
                Width = RadialWheelCanvas.BaseSize,
                Height = RadialWheelCanvas.BaseSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0)
            };

            (_convertBadge, _convertKeyCap, _convertKeyText, _convertLabel) = Segment("Shift", "Convert");
            (_toolsBadge, _toolsKeyCap, _toolsKeyText, _toolsLabel) = Segment("Ctrl", "Tools");
            (_actionsBadge, _actionsKeyCap, _actionsKeyText, _actionsLabel) = Segment("Alt", "Actions");
            (_cancelBadge, _cancelKeyCap, _cancelKeyText, _cancelLabel) = Segment("Esc", "Cancel");
            _cancelBadge.Opacity = 0.75;
            _separator = new Border { Width = 1, Height = 13, Margin = new Thickness(4, 0) };

            var hints = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            hints.Children.Add(_convertBadge);
            hints.Children.Add(_toolsBadge);
            hints.Children.Add(_actionsBadge);
            hints.Children.Add(_separator);
            hints.Children.Add(_cancelBadge);

            _hintBar = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(3),
                BoxShadow = BoxShadows.Parse("0 3 16 0 #59000000"),
                Child = hints
            };

            _hintHost = new LayoutTransformControl { HorizontalAlignment = HorizontalAlignment.Center, Child = _hintBar };

            var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(WheelCanvas);
            stack.Children.Add(_hintHost);
            _root = new Panel { Background = Brushes.Transparent, Children = { stack } };
            Content = _root;

            DragDrop.SetAllowDrop(this, true);
            DragDrop.SetAllowDrop(_root, true);
            AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            AddHandler(DragDrop.DropEvent, OnDrop);
            Deactivated += OnDeactivated;

            TemplateManager.Instance.CurrentChanged += ApplyTemplateChrome;
            Closed += (_, _) => TemplateManager.Instance.CurrentChanged -= ApplyTemplateChrome;
            ApplyTemplateChrome();
        }

        private (Border Segment, Border KeyCap, TextBlock KeyText, TextBlock Label) Segment(string key, string label)
        {
            var keyText = new TextBlock { Text = key, FontSize = 9.5, FontWeight = FontWeight.SemiBold, FontFamily = new FontFamily(WheelPainter.UiFont) };
            var keyCap = new Border
            {
                BorderThickness = new Thickness(1, 1, 1, 1.5),
                CornerRadius = new CornerRadius(3.5),
                Padding = new Thickness(4, 0, 4, 1),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = keyText
            };
            var text = new TextBlock { Text = label, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily(WheelPainter.UiFont) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(keyCap);
            row.Children.Add(text);
            var segment = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 3, 7, 3), Margin = new Thickness(1, 0), Child = row };
            return (segment, keyCap, keyText, text);
        }

        public string? CurrentFilePath => _currentFilePath;
        public WheelMode CurrentMode => _currentMode;

        /// <summary>The wheel is showing (or about to, while it's being placed).</summary>
        public bool IsOpen => _open;

        /// <summary>Opened for a drag (closes on the button going up) rather than for a click.</summary>
        public bool IsDragMode => _open && !_clickMode;

        /// <summary>Wheel size (0.4–1.4) and whether the shortcut hint bar is shown under it.</summary>
        public void ApplyScale(double scale, bool showHints)
        {
            _scale = Math.Clamp(scale, RadialWheelCanvas.MinScale, RadialWheelCanvas.MaxScale);
            _showHints = showHints;
            WheelCanvas.WheelScale = _scale;
            double canvasDim = RadialWheelCanvas.BaseSize * _scale;
            WheelCanvas.Width = canvasDim;
            WheelCanvas.Height = canvasDim;

            // The hint bar shrinks with the wheel, but not so far that the key names become unreadable.
            // Small wheels get just the key caps (the active one in its wheel's color), so the bar stays narrower than the wheel.
            double hintScale = Math.Clamp(0.55 + 0.45 * _scale, 0.8, 1.0);
            _hintHost.LayoutTransform = new ScaleTransform(hintScale, hintScale);
            bool compactHints = _scale < 0.7;
            foreach (var label in new[] { _convertLabel, _toolsLabel, _actionsLabel, _cancelLabel }) label.IsVisible = !compactHints;
            foreach (var cap in new[] { _convertKeyCap, _toolsKeyCap, _actionsKeyCap, _cancelKeyCap }) cap.Margin = new Thickness(0, 0, compactHints ? 0 : 5, 0);
            _hintHost.IsVisible = showHints;
            // The canvas leaves room for the wheel's shadow; tuck the bar into it
            _hintHost.Margin = new Thickness(0, -22 * _scale, 0, 0);
            UpdateWindowSize();
        }

        public void ApplyScale(double scale) => ApplyScale(scale, _showHints);

        private Size _hintSize;

        private void UpdateWindowSize()
        {
            double canvasDim = RadialWheelCanvas.BaseSize * _scale;
            double width = canvasDim + 16;
            double height = canvasDim + 8;
            if (_showHints)
            {
                // Labels may have just been shown or hidden: measure afresh, not from the cached layout
                foreach (var v in _hintHost.GetVisualDescendants()) if (v is Layoutable l) l.InvalidateMeasure();
                _hintHost.InvalidateMeasure();
                _hintHost.Measure(Size.Infinity);
                // DesiredSize includes the (negative) tuck margin; the bar itself is taller than that
                var m = _hintHost.Margin;
                _hintSize = new Size(_hintHost.DesiredSize.Width - m.Left - m.Right, _hintHost.DesiredSize.Height - m.Top - m.Bottom);
                width = Math.Max(width, _hintSize.Width + 24);
                // Tucked into the wheel's shadow, with room below for the bar's own shadow
                height += _hintSize.Height - 22 * _scale + 14;
            }
            Width = Math.Ceiling(width);
            Height = Math.Ceiling(height);
        }

        /// <summary>Where the wheel's center is, relative to the window's top-left, in window units (= stage units).</summary>
        private Point WheelCenterOffset => new(Width / 2.0, 4 + RadialWheelCanvas.BaseSize * _scale / 2.0);

        public void UpdateKeyLabels(string convertKey, string toolsKey, string actionsKey)
        {
            _convertKey = convertKey;
            _convertKeyText.Text = FormatKeyText(convertKey);
            _toolsKeyText.Text = FormatKeyText(toolsKey);
            _actionsKeyText.Text = FormatKeyText(actionsKey);
            UpdateWindowSize();
        }

        /// <summary>Hold-to-use mode cancels by letting go of the key, so the hint says so.</summary>
        public void SetHoldToUse(bool holdToUse)
        {
            _cancelKeyText.Text = holdToUse ? "Release" : "Esc";
            _cancelLabel.Text = holdToUse ? "key to cancel" : "Cancel";
            UpdateWindowSize();
        }

        /// <summary>Key names as Linux keyboards label them ("Win" is the Super key).</summary>
        public static string FormatKeyText(string key)
        {
            return key.Trim().ToLowerInvariant() switch
            {
                "shift" => "Shift",
                "control" or "ctrl" => "Ctrl",
                "alt" => "Alt",
                "win" or "windows" or "super" => "Super",
                "space" => "Space",
                _ => key
            };
        }

        /// <summary>Colors the hint bar to match the template's wheel.</summary>
        private void ApplyTemplateChrome()
        {
            var p = WheelPalette.For(TemplateManager.Instance.Current);
            _hintBar.Background = WheelPainter.Solid(ColorUtil.WithAlpha(p.Ring, Math.Max(p.Ring.A, (byte)0xF0)));
            _hintBar.BorderBrush = WheelPainter.Solid(p.Edge);
            _separator.Background = WheelPainter.Solid(p.Edge);
            _keyCapBrush = WheelPainter.Solid(ColorUtil.Mix(p.Hub, p.IsLight ? Colors.White : Colors.Black, 0.1));
            _keyCapEdgeBrush = WheelPainter.Solid(ColorUtil.Lerp(p.Edge, p.TextFaint, 0.35));
            _keyTextBrush = WheelPainter.Solid(p.TextMuted);
            _hintLabelBrush = WheelPainter.Solid(p.Text);
            foreach (var cap in new[] { _convertKeyCap, _toolsKeyCap, _actionsKeyCap, _cancelKeyCap }) cap.Background = _keyCapBrush;
            foreach (var label in new[] { _convertLabel, _toolsLabel, _actionsLabel }) label.Foreground = _hintLabelBrush;
            _cancelLabel.Foreground = _keyTextBrush;
            _cancelKeyCap.BorderBrush = _keyCapEdgeBrush;
            _cancelKeyText.Foreground = _keyTextBrush;
            UpdateBadges();
        }

        // ------------------------------------------------------------------ opening and closing

        /// <summary>
        /// Opens the wheel centered on <paramref name="stagePoint"/> (GNOME Shell's logical coordinates, as the extension
        /// reports them), or in the middle of the screen when null. <paramref name="dragMode"/>: opened during a drag, so
        /// the file arrives by drag-and-drop; otherwise <paramref name="initialFile"/> is shown and a click picks.
        /// </summary>
        public void ShowAt(Point? stagePoint, WheelMode mode, string? initialFile, bool dragMode)
        {
            if (_open)
            {
                if (_currentMode != mode) SwitchMode(mode);
                return;
            }

            // Opening on a different wheel than last time is not a "switch": only the open sound should play
            _currentMode = mode;
            WheelCanvas.PlaySounds = false;
            WheelCanvas.CurrentMode = mode;
            WheelCanvas.HoveredIndex = -1;
            WheelCanvas.PlaySounds = true;
            UpdateBadges();

            _clickMode = !dragMode;
            _dragHasFile = false;
            _releaseTimer?.Stop();
            if (!string.IsNullOrEmpty(initialFile)) SetCurrentFile(initialFile);
            else ShowNoFile();

            _open = true;
            int token = ++_showToken;
            ShowActivated = _clickMode;

            // Hidden until it's in place: the window manager may first map it somewhere else
            _root.Opacity = 0;
            Point center = stagePoint ?? ScreenCenter();
            Point topLeft = ClampToScreen(new Point(center.X - WheelCenterOffset.X, center.Y - WheelCenterOffset.Y));
            Position = ToPixels(topLeft);
            ReassertWindowState();
            Show();
            if (_clickMode) Activate();
            _ = PlaceAndRevealAsync(topLeft, token);
        }

        /// <summary>
        /// The window manager drops a window's "always on top" and "not in the taskbar" states when it's hidden, and Avalonia
        /// doesn't send them again on the next show. Without them, GNOME's focus-stealing prevention stacks the wheel under
        /// the window being dragged from and posts "“OG Converter Wheel” is ready" instead of showing it.
        /// </summary>
        private void ReassertWindowState()
        {
            Topmost = false;
            Topmost = true;
            ShowInTaskbar = true;
            ShowInTaskbar = false;
        }

        /// <summary>
        /// Asks the GNOME extension to move the window to exactly <paramref name="topLeft"/> (stage coordinates), which also
        /// fixes placement on scaled displays and when the window manager ignored the requested position, then fades in.
        /// </summary>
        private async Task PlaceAndRevealAsync(Point topLeft, int token)
        {
            bool placed = false;
            if (DBusService.Primary is { } bus)
            {
                // The window manager needs a moment to manage the newly mapped window before the extension can find it
                for (int attempt = 0; attempt < 6 && !placed; attempt++)
                {
                    if (token != _showToken || !_open) return;
                    placed = await bus.MoveWindowByTitleAsync(Title ?? string.Empty, (int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y));
                    if (!placed) await Task.Delay(25);
                }
            }
            if (token != _showToken || !_open) return;

            _root.Opacity = 1;
            WheelCanvas.PlayIntro();
            SoundService.Instance.Play(UiSound.Open);

            // The drag only tells this window about the file once the pointer moves over it, and it opened under a still
            // pointer: without a nudge the wheel stays empty until the user moves the mouse
            if (placed && !_clickMode && !_dragHasFile && DBusService.Primary is { } shell) await shell.NudgePointerAsync();
        }

        /// <summary>X11 pixels for a stage point. XWayland uses the stage's units unless it scales natively, where Avalonia's scaling matches.</summary>
        private PixelPoint ToPixels(Point stage)
        {
            double scaling = Screens.ScreenFromPoint(new PixelPoint((int)stage.X, (int)stage.Y))?.Scaling ?? Screens.Primary?.Scaling ?? 1.0;
            return new PixelPoint((int)Math.Round(stage.X * scaling), (int)Math.Round(stage.Y * scaling));
        }

        private Point ScreenCenter()
        {
            var screen = Screens.Primary;
            if (screen == null) return new Point(800, 500);
            var area = screen.WorkingArea;
            return new Point((area.X + area.Width / 2.0) / screen.Scaling, (area.Y + area.Height / 2.0) / screen.Scaling);
        }

        private Point ClampToScreen(Point topLeft)
        {
            var screen = Screens.ScreenFromPoint(new PixelPoint((int)topLeft.X, (int)topLeft.Y)) ?? Screens.Primary;
            if (screen == null) return topLeft;
            double s = screen.Scaling;
            var a = screen.WorkingArea;
            double left = a.X / s, top = a.Y / s, right = (a.X + a.Width) / s, bottom = (a.Y + a.Height) / s;
            return new Point(Math.Clamp(topLeft.X, left, Math.Max(left, right - Width)), Math.Clamp(topLeft.Y, top, Math.Max(top, bottom - Height)));
        }

        public void Dismiss()
        {
            _releaseTimer?.Stop();
            if (!_open) return;
            _open = false;
            _showToken++;
            _currentFilePath = null;
            _draggedFiles = new();
            _dragHasFile = false;
            WheelCanvas.HoveredIndex = -1;
            Hide();
        }

        /// <summary>Picks option <paramref name="index"/> for the current file, closes, and reports it.</summary>
        private void Choose(int index)
        {
            string? file = _currentFilePath;
            var files = _draggedFiles.Count > 1 && _draggedFiles[0] == file ? _draggedFiles : new List<string> { file ?? "" };
            var items = WheelCanvas.Items;
            Dismiss();

            if (!string.IsNullOrEmpty(file) && index >= 0 && index < items.Count)
            {
                SoundService.Instance.Play(UiSound.Select);
                FileActionRequested?.Invoke(files, items[index]);
            }
        }

        /// <summary>
        /// The mouse button went up (from the extension). A drop on an option normally arrives as well, possibly a little
        /// later; if none does, the option last hovered by the drag still counts, else the wheel closes.
        /// </summary>
        public void HandleButtonReleased()
        {
            if (!_open || _clickMode) return;
            _releaseTimer?.Stop();
            _releaseTimer = new DispatcherTimer { Interval = DropGrace };
            int token = _showToken;
            _releaseTimer.Tick += (_, _) =>
            {
                _releaseTimer?.Stop();
                if (token != _showToken || !_open) return;
                if (_dragHasFile && WheelCanvas.HoveredIndex >= 0) Choose(WheelCanvas.HoveredIndex);
                else Dismiss();
            };
            _releaseTimer.Start();
        }

        public void SwitchMode(WheelMode newMode)
        {
            _currentMode = newMode;
            WheelCanvas.CurrentMode = newMode;
            UpdateBadges();

            if (!string.IsNullOrEmpty(_currentFilePath)) SetCurrentFile(_currentFilePath);
            else ShowNoFile();
        }

        public void SetCurrentFile(string filePath)
        {
            _currentFilePath = filePath;
            var items = FormatRegistry.GetWheelItems(filePath, _currentMode);
            WheelCanvas.Items = items;
            WheelCanvas.HoveredIndex = -1;

            string hint = items.Count == 0 ? "No options for this file" : _currentMode switch
            {
                WheelMode.Convert => "Choose a format",
                WheelMode.Tools => "Choose a tool",
                WheelMode.Actions => "Choose an action",
                _ => "Choose an option"
            };
            WheelCanvas.SetFile(filePath, hint);
        }

        private void ShowNoFile()
        {
            _currentFilePath = null;
            WheelCanvas.Items = FormatRegistry.GetWheelItems(null, _currentMode);
            WheelCanvas.SetMessage("", "Drop a file here", $"Or drag one from Files and tap {FormatKeyText(_convertKey)}");
        }

        private void UpdateBadges()
        {
            SetBadge(_convertBadge, _convertKeyCap, _convertKeyText, WheelMode.Convert);
            SetBadge(_toolsBadge, _toolsKeyCap, _toolsKeyText, WheelMode.Tools);
            SetBadge(_actionsBadge, _actionsKeyCap, _actionsKeyText, WheelMode.Actions);
        }

        private void SetBadge(Border segment, Border keyCap, TextBlock keyText, WheelMode mode)
        {
            bool active = _currentMode == mode;
            Color accent = RadialWheelCanvas.AccentFor(mode);
            segment.Background = active ? WheelPainter.Solid(ColorUtil.WithAlpha(accent, (byte)0x2E)) : Brushes.Transparent;
            keyCap.BorderBrush = active ? WheelPainter.Solid(accent) : _keyCapEdgeBrush;
            keyText.Foreground = active ? WheelPainter.Solid(accent) : _keyTextBrush;
            segment.Opacity = active ? 1.0 : 0.75;
        }

        // ------------------------------------------------------------------ drag and drop

        /// <summary>File paths in drag data: a file list, else text/uri-list or path text (editors' file trees).</summary>
        public static List<string> GetPaths(IDataTransfer? data)
        {
            var paths = new List<string>();
            if (data == null) return paths;
            try
            {
                if (data.TryGetFiles() is { } files)
                {
                    foreach (var f in files) DroppedPaths.Add(f.TryGetLocalPath(), paths);
                    if (paths.Count > 0) return paths;
                }
                if (data.TryGetValue(DataFormat.CreateStringPlatformFormat("text/uri-list")) is { Length: > 0 } uris)
                {
                    DroppedPaths.AddLines(uris, paths);
                    if (paths.Count > 0) return paths;
                }
                if (data.TryGetText() is { Length: > 0 } text) DroppedPaths.AddLines(text, paths);
            }
            catch (Exception ex)
            {
                // Some drag sources fail for formats they advertise
                Console.Error.WriteLine($"[OG Converter] Reading drag data failed: {ex.Message}");
            }
            return paths;
        }

        private void OnDragEnter(object? sender, DragEventArgs e)
        {
            if (!_open) return;
            var files = GetPaths(e.DataTransfer);
            _dragHasFile = files.Count > 0;
            if (_dragHasFile)
            {
                _draggedFiles = files;
                if (files[0] != _currentFilePath) SetCurrentFile(files[0]);
                UpdateHover(e);
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            if (_dragHasFile)
            {
                UpdateHover(e);
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void UpdateHover(DragEventArgs e)
        {
            int idx = WheelCanvas.HitTestSlice(e.GetPosition(WheelCanvas));
            if (idx != WheelCanvas.HoveredIndex) WheelCanvas.HoveredIndex = idx;
        }

        private void OnDragLeave(object? sender, DragEventArgs e)
        {
            WheelCanvas.HoveredIndex = -1;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            var files = GetPaths(e.DataTransfer);
            if (files.Count > 0 && files[0] != _currentFilePath) SetCurrentFile(files[0]);
            if (files.Count > 0) _draggedFiles = files;
            int idx = files.Count > 0 ? WheelCanvas.HitTestSlice(e.GetPosition(WheelCanvas)) : -1;
            _dragHasFile = false;
            e.DragEffects = idx >= 0 ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;

            if (idx >= 0) Choose(idx);
            else Dismiss();
        }

        // ------------------------------------------------------------------ click mode

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_open || !_clickMode) return;
            int idx = WheelCanvas.HitTestSlice(e.GetPosition(WheelCanvas));
            if (idx != WheelCanvas.HoveredIndex) WheelCanvas.HoveredIndex = idx;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (_open && _clickMode) WheelCanvas.HoveredIndex = -1;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!_open || !_clickMode) return;
            e.Handled = true;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Dismiss();
                return;
            }
            int idx = WheelCanvas.HitTestSlice(e.GetPosition(WheelCanvas));
            if (idx >= 0) Choose(idx);
            else if (!IsOverHub(e.GetPosition(WheelCanvas))) Dismiss();
        }

        private bool IsOverHub(Point p)
        {
            double dx = p.X - WheelCanvas.Bounds.Width / 2.0, dy = p.Y - WheelCanvas.Bounds.Height / 2.0;
            return Math.Sqrt(dx * dx + dy * dy) < WheelCanvas.InnerRadius;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape && _open)
            {
                Dismiss();
                e.Handled = true;
            }
        }

        /// <summary>Clicking another window closes a click-mode wheel.</summary>
        private void OnDeactivated(object? sender, EventArgs e)
        {
            if (_open && _clickMode) Dismiss();
        }
    }
}
