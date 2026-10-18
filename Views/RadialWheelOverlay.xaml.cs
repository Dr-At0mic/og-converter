using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    public partial class RadialWheelOverlay : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public event Action<string, WheelItem>? FileActionRequested;

        private string? _currentFilePath;
        private WheelMode _currentMode = WheelMode.Convert;
        private string _convertKey = "Shift";

        public RadialWheelOverlay()
        {
            InitializeComponent();
            Loaded += (s, e) => MakeNonActivating();
        }

        private void MakeNonActivating()
        {
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        public string? CurrentFilePath => _currentFilePath;
        public WheelMode CurrentMode => _currentMode;

        public void ApplyScale(double scale)
        {
            scale = Math.Clamp(scale, 0.6, 1.4);
            WheelCanvas.WheelScale = scale;
            double canvasDim = 460.0 * scale;
            WheelCanvas.Width = canvasDim;
            WheelCanvas.Height = canvasDim;
            // Never narrower than the key-hint bar below the wheel
            Width = Math.Max(canvasDim + 100.0, 540.0);
            Height = canvasDim + 160.0;
        }

        public void UpdateKeyLabels(string convertKey, string toolsKey, string actionsKey)
        {
            _convertKey = convertKey;
            if (ConvertKeyText != null) ConvertKeyText.Text = FormatKeyText(convertKey);
            if (ToolsKeyText != null) ToolsKeyText.Text = FormatKeyText(toolsKey);
            if (ActionsKeyText != null) ActionsKeyText.Text = FormatKeyText(actionsKey);
        }

        /// <summary>Hold-to-use mode cancels by letting go of the key, so the hint says so.</summary>
        public void SetHoldToUse(bool holdToUse)
        {
            CancelKeyText.Text = holdToUse ? "Release" : "Esc";
            CancelLabel.Text = holdToUse ? "key to cancel" : "Cancel";
        }

        private static string FormatKeyText(string key)
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

        public void ShowAt(Point screenPoint, WheelMode mode, string? initialFile = null)
        {
            if (IsVisible)
            {
                if (_currentMode != mode)
                {
                    SwitchMode(mode);
                }
                return;
            }

            _currentMode = mode;
            WheelCanvas.CurrentMode = mode;
            WheelCanvas.HoveredIndex = -1;
            UpdateBadges();

            string? activeFile = initialFile;
            if (string.IsNullOrEmpty(activeFile))
            {
                activeFile = ShellSelectionService.GetActiveFile();
            }

            if (!string.IsNullOrEmpty(activeFile))
            {
                SetCurrentFile(activeFile);
            }
            else
            {
                ShowNoFile();
            }

            double w = Width;
            double h = Height;
            // Center the wheel exactly under the cursor
            double targetX = screenPoint.X - (w / 2.0);
            double targetY = screenPoint.Y - (WheelCanvas.Height / 2.0);

            // Clamp to screen bounds
            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;
            Left = Math.Clamp(targetX, 10, screenW - w - 10);
            Top = Math.Clamp(targetY, 10, screenH - h - 10);

            Show();
            var sb = (Storyboard)Resources["OpenAnimation"];
            sb?.Begin();
        }

        public void UpdateCursorPosition(Point screenPoint)
        {
            if (!IsVisible) return;

            Point clientPoint;
            try
            {
                clientPoint = WheelCanvas.PointFromScreen(screenPoint);
            }
            catch
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
            string? file = _currentFilePath;

            if (string.IsNullOrEmpty(file))
            {
                file = ShellSelectionService.GetActiveFile();
            }

            Dismiss();

            if (!string.IsNullOrEmpty(file) && selectedIdx >= 0 && selectedIdx < WheelCanvas.Items.Count)
            {
                var selectedOption = WheelCanvas.Items[selectedIdx];
                FileActionRequested?.Invoke(file, selectedOption);
            }
        }

        public void Dismiss()
        {
            if (IsVisible)
            {
                Hide();
                _currentFilePath = null;
                WheelCanvas.HoveredIndex = -1;
            }
        }

        public void SwitchMode(WheelMode newMode)
        {
            _currentMode = newMode;
            WheelCanvas.CurrentMode = newMode;
            UpdateBadges();

            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                SetCurrentFile(_currentFilePath);
            }
            else
            {
                ShowNoFile();
            }
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
            WheelCanvas.SetMessage("\uE898", "Drop a file here", $"Or drag one from Explorer and tap {FormatKeyText(_convertKey)}");
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
            segment.Background = active ? new SolidColorBrush(Color.FromArgb(0x2E, accent.R, accent.G, accent.B)) : Brushes.Transparent;
            keyCap.BorderBrush = new SolidColorBrush(active ? accent : Color.FromRgb(0x3A, 0x3D, 0x46));
            keyText.Foreground = new SolidColorBrush(active ? accent : Color.FromRgb(0xA1, 0xA6, 0xB0));
            segment.Opacity = active ? 1.0 : 0.75;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    SetCurrentFile(files[0]);
                }

                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
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
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    _currentFilePath = files[0];
                }
            }

            HandleMouseDrop();
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }
}
