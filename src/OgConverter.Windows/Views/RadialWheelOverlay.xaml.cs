using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Wheel;

namespace OgConverter.Views
{
    public partial class RadialWheelOverlay : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        /// <summary>An option was picked for the dragged files (the wheel shows the first file's options).</summary>
        public event Action<IReadOnlyList<string>, WheelItem>? FileActionRequested;

        private string? _currentFilePath;
        private IReadOnlyList<string> _currentFiles = Array.Empty<string>();
        private WheelMode _currentMode = WheelMode.Convert;
        private string _convertKey = "Shift";
        private double _scale = 1.0;
        private bool _showHints = true;

        public RadialWheelOverlay()
        {
            InitializeComponent();
            SourceInitialized += (s, e) => MakeNonActivating();
            TemplateManager.Instance.CurrentChanged += ApplyTemplateChrome;
            Closed += (s, e) => TemplateManager.Instance.CurrentChanged -= ApplyTemplateChrome;
            ApplyTemplateChrome();
        }

        private void MakeNonActivating()
        {
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        public string? CurrentFilePath => _currentFilePath;
        public WheelMode CurrentMode => _currentMode;

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
            HintScale.ScaleX = hintScale;
            HintScale.ScaleY = hintScale;
            bool compactHints = _scale < 0.7;
            var labelVisibility = compactHints ? Visibility.Collapsed : Visibility.Visible;
            foreach (var label in new[] { ConvertLabel, ToolsLabel, ActionsLabel, CancelLabel }) label.Visibility = labelVisibility;
            foreach (var cap in new[] { ConvertKeyCap, ToolsKeyCap, ActionsKeyCap, CancelKeyCap }) cap.Margin = new Thickness(0, 0, compactHints ? 0 : 5, 0);
            HintBar.Visibility = showHints ? Visibility.Visible : Visibility.Collapsed;
            // The canvas leaves room for the wheel's shadow; tuck the bar into it
            double tuck = 22 * _scale;
            HintBar.Margin = new Thickness(0, -tuck, 0, 0);
            UpdateWindowSize();
        }

        public void ApplyScale(double scale) => ApplyScale(scale, _showHints);

        private void UpdateWindowSize()
        {
            double canvasDim = RadialWheelCanvas.BaseSize * _scale;
            double width = canvasDim + 16;
            double height = canvasDim + 8;
            if (_showHints)
            {
                HintBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var hint = HintBar.DesiredSize;
                width = Math.Max(width, hint.Width + 24);
                height += hint.Height - 22 * _scale + 14;
            }
            Width = Math.Ceiling(width);
            Height = Math.Ceiling(height);
        }

        public void UpdateKeyLabels(string convertKey, string toolsKey, string actionsKey)
        {
            _convertKey = convertKey;
            ConvertKeyText.Text = FormatKeyText(convertKey);
            ToolsKeyText.Text = FormatKeyText(toolsKey);
            ActionsKeyText.Text = FormatKeyText(actionsKey);
            UpdateWindowSize();
        }

        /// <summary>Hold-to-use mode cancels by letting go of the key, so the hint says so.</summary>
        public void SetHoldToUse(bool holdToUse)
        {
            CancelKeyText.Text = holdToUse ? "Release" : "Esc";
            CancelLabel.Text = holdToUse ? "key to cancel" : "Cancel";
            UpdateWindowSize();
        }

        public static string FormatKeyText(string key)
        {
            return key.Trim().ToLowerInvariant() switch
            {
                "shift" => "Shift",
                "control" or "ctrl" => "Ctrl",
                "alt" => "Alt",
                "win" or "windows" => "Win",
                "space" => "Space",
                _ => key
            };
        }

        /// <summary>Colors the hint bar to match the template's wheel.</summary>
        private void ApplyTemplateChrome()
        {
            var p = WheelPalette.For(TemplateManager.Instance.Current);
            Resources["HintBarBrush"] = WheelPainter.Solid(ColorUtil.WithAlpha(p.Ring, Math.Max(p.Ring.A, (byte)0xF0)));
            Resources["HintEdgeBrush"] = WheelPainter.Solid(p.Edge);
            Resources["KeyCapBrush"] = WheelPainter.Solid(ColorUtil.Mix(p.Hub, p.IsLight ? Colors.White : Colors.Black, 0.1));
            Resources["KeyCapEdgeBrush"] = WheelPainter.Solid(ColorUtil.Lerp(p.Edge, p.TextFaint, 0.35));
            Resources["KeyTextBrush"] = WheelPainter.Solid(p.TextMuted);
            Resources["HintLabelBrush"] = WheelPainter.Solid(p.Text);
            UpdateBadges();
        }

        /// <summary>
        /// Opens the wheel centered on <paramref name="screenPoint"/>, in physical screen pixels (what the
        /// low-level hooks report).
        /// </summary>
        public void ShowAt(Point screenPoint, WheelMode mode, IReadOnlyList<string>? initialFiles = null)
        {
            if (IsVisible)
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

            if (initialFiles is { Count: > 0 }) SetCurrentFiles(initialFiles);
            else if (ShellSelectionService.GetActiveFile() is { Length: > 0 } activeFile) SetCurrentFile(activeFile);
            else ShowNoFile();

            // Hidden until it's in place: Show() first appears wherever the window was last time.
            // Clear the last open animation first, or its held end value would override this.
            RootGrid.BeginAnimation(OpacityProperty, null);
            RootGrid.Opacity = 0;
            Show();
            PositionAt(new POINT { X = (int)screenPoint.X, Y = (int)screenPoint.Y });

            ((Storyboard)Resources["OpenAnimation"]).Begin();
            WheelCanvas.PlayIntro();
            SoundService.Instance.Play(UiSound.Open);
        }

        /// <summary>Opens the wheel in the middle of the monitor showing <paramref name="owner"/> (the Preview button).</summary>
        public void ShowCentered(Window owner, WheelMode mode)
        {
            IntPtr monitor = MonitorFromWindow(new WindowInteropHelper(owner).Handle, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref info);
            var center = new Point((info.rcWork.Left + info.rcWork.Right) / 2.0, (info.rcWork.Top + info.rcWork.Bottom) / 2.0);
            ShowAt(center, mode);
        }

        private void PositionAt(POINT cursor)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            IntPtr monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            bool haveMonitor = GetMonitorInfo(monitor, ref info);

            // Two passes: moving onto a monitor with a different DPI resizes the window, which shifts the center
            for (int pass = 0; pass < 2; pass++)
            {
                UpdateLayout();
                if (!GetWindowRect(hwnd, out RECT wr) || ActualWidth <= 0) return;
                int wPx = wr.Right - wr.Left, hPx = wr.Bottom - wr.Top;
                double toPx = wPx / ActualWidth;

                Point c = WheelCanvas.TranslatePoint(new Point(WheelCanvas.ActualWidth / 2.0, WheelCanvas.ActualHeight / 2.0), this);
                int x = (int)Math.Round(cursor.X - c.X * toPx);
                int y = (int)Math.Round(cursor.Y - c.Y * toPx);

                if (haveMonitor)
                {
                    var work = info.rcWork;
                    x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - wPx));
                    y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - hPx));
                }

                if (x == wr.Left && y == wr.Top) return;
                SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }

        public void UpdateCursorPosition(Point screenPoint)
        {
            if (!IsVisible) return;

            Point clientPoint;
            try
            {
                clientPoint = WheelCanvas.PointFromScreen(screenPoint);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            // The canvas renders the hovered option's title and description in the hub itself
            int idx = WheelCanvas.HitTestSlice(clientPoint);
            if (idx != WheelCanvas.HoveredIndex)
            {
                WheelCanvas.HoveredIndex = idx;
            }
        }

        public void HandleMouseDrop()
        {
            if (!IsVisible) return;

            int selectedIdx = WheelCanvas.HoveredIndex;
            IReadOnlyList<string> files = _currentFiles;

            if (files.Count == 0 && ShellSelectionService.GetActiveFile() is { Length: > 0 } activeFile)
            {
                files = new[] { activeFile };
            }

            Dismiss();

            if (files.Count > 0 && selectedIdx >= 0 && selectedIdx < WheelCanvas.Items.Count)
            {
                SoundService.Instance.Play(UiSound.Select);
                var selectedOption = WheelCanvas.Items[selectedIdx];
                FileActionRequested?.Invoke(files, selectedOption);
            }
        }

        public void Dismiss()
        {
            if (IsVisible)
            {
                Hide();
                _currentFilePath = null;
                _currentFiles = Array.Empty<string>();
                WheelCanvas.HoveredIndex = -1;
            }
        }

        public void SwitchMode(WheelMode newMode)
        {
            _currentMode = newMode;
            WheelCanvas.CurrentMode = newMode;
            UpdateBadges();

            if (_currentFiles.Count > 0) SetCurrentFiles(_currentFiles);
            else ShowNoFile();
        }

        public void SetCurrentFile(string filePath) => SetCurrentFiles(new[] { filePath });

        /// <summary>Shows the first file's options; the pick is applied to every file it suits.</summary>
        public void SetCurrentFiles(IReadOnlyList<string> files)
        {
            string filePath = files[0];
            _currentFiles = files;
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
            // The hub has room for a short line only: the file count replaces the prompt
            if (files.Count > 1 && items.Count > 0) hint = files.Count == 2 ? "+1 more file" : $"+{files.Count - 1} more files";
            WheelCanvas.SetFile(filePath, hint);
        }

        private void ShowNoFile()
        {
            _currentFilePath = null;
            _currentFiles = Array.Empty<string>();
            WheelCanvas.Items = FormatRegistry.GetWheelItems(null, _currentMode);
            WheelCanvas.SetMessage("", "Drop a file here", $"Or drag one from Explorer or your editor and tap {FormatKeyText(_convertKey)}");
        }

        private void UpdateBadges()
        {
            SetBadge(ConvertBadge, ConvertKeyCap, ConvertKeyText, WheelMode.Convert);
            SetBadge(ToolsBadge, ToolsKeyCap, ToolsKeyText, WheelMode.Tools);
            SetBadge(ActionsBadge, ActionsKeyCap, ActionsKeyText, WheelMode.Actions);
        }

        private void SetBadge(Border segment, Border keyCap, TextBlock keyText, WheelMode mode)
        {
            bool active = _currentMode == mode;
            Color accent = RadialWheelCanvas.AccentFor(mode);
            segment.Background = active ? WheelPainter.Solid(ColorUtil.WithAlpha(accent, (byte)0x2E)) : Brushes.Transparent;
            if (active)
            {
                keyCap.BorderBrush = WheelPainter.Solid(accent);
                keyText.Foreground = WheelPainter.Solid(accent);
            }
            else
            {
                keyCap.ClearValue(Border.BorderBrushProperty);
                keyText.ClearValue(TextBlock.ForegroundProperty);
            }
            segment.Opacity = active ? 1.0 : 0.75;
        }

        private bool _dragHasFile;

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            // Explorer drags carry a file list; IDE file trees (VS Code, JetBrains…) carry file:// URLs or path text
            var files = DroppedFiles.GetPaths(e.Data);
            _dragHasFile = files.Count > 0;
            if (_dragHasFile)
            {
                SetCurrentFiles(files);
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (_dragHasFile)
            {
                int idx = WheelCanvas.HitTestSlice(e.GetPosition(WheelCanvas));
                if (idx != WheelCanvas.HoveredIndex)
                {
                    WheelCanvas.HoveredIndex = idx;
                }

                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            WheelCanvas.HoveredIndex = -1;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            var files = DroppedFiles.GetPaths(e.Data);
            if (files.Count > 0)
            {
                _currentFiles = files;
                _currentFilePath = files[0];
            }
            _dragHasFile = false;

            HandleMouseDrop();
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }
}
