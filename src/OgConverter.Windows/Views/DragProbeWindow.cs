using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// A small, practically invisible drop target put under the cursor when a wheel key is tapped while the left button
    /// is held. Holding the button down also happens in games, text selections and window moves, so the wheel only opens
    /// once Windows offers this window a drag that carries files or folders.
    /// </summary>
    public sealed class DragProbeWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        // SHQueryUserNotificationState: a full-screen app (borderless or exclusive Direct3D) is in front
        private const int QUNS_BUSY = 2;
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

        /// <summary>
        /// How long to wait for the drag to reach the probe. The drag loop checks the window under the cursor on every
        /// mouse move and key change and on a timer, so a real drag arrives well within this.
        /// </summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(600);

        private readonly DispatcherTimer _timer;
        private Point _cursor;

        /// <summary>A file drag reached the probe: open <c>mode</c>'s wheel at the cursor for these paths.</summary>
        public event Action<WheelMode, Point, IReadOnlyList<string>>? FileDragConfirmed;

        /// <summary>The wheel to open; follows the last wheel key tapped while waiting.</summary>
        public WheelMode Mode { get; set; }

        public DragProbeWindow()
        {
            Title = "OG Converter drag check";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            // Not fully transparent: a layered window's alpha-0 pixels are skipped by the drag loop's hit test
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = Height = 48;
            AllowDrop = true;

            SourceInitialized += (_, _) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            };

            DragEnter += OnDrag;
            DragOver += OnDrag;
            Drop += (_, e) =>
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                Cancel();
            };

            _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = Timeout };
            _timer.Tick += (_, _) => Cancel();
        }

        /// <summary>
        /// True while a full-screen app such as a game is in front. The wheel isn't offered there: a game's fire button
        /// isn't a file drag, and a window popping up over an exclusive full-screen game can knock it out of full screen.
        /// </summary>
        public static bool FullScreenAppInFront =>
            SHQueryUserNotificationState(out int state) == 0 && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN;

        /// <summary>Puts the probe under <paramref name="screenPoint"/> (physical pixels) and waits for a file drag.</summary>
        public void Begin(WheelMode mode, Point screenPoint)
        {
            Mode = mode;
            _cursor = screenPoint;
            Show();
            MoveTo(screenPoint);
            _timer.Stop();
            _timer.Start();
        }

        /// <summary>Keeps the probe under the cursor while it waits.</summary>
        public void MoveTo(Point screenPoint)
        {
            if (!IsVisible) return;
            _cursor = screenPoint;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT r)) return;
            int x = (int)screenPoint.X - (r.Right - r.Left) / 2;
            int y = (int)screenPoint.Y - (r.Bottom - r.Top) / 2;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        public void Cancel()
        {
            _timer.Stop();
            if (IsVisible) Hide();
        }

        private void OnDrag(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (!IsVisible) return;

            // Text, links and pictures from a browser are drags too, but not files: keep waiting until the timeout
            var paths = DroppedFiles.GetPaths(e.Data);
            if (paths.Count == 0) return;

            Cancel();
            WheelMode mode = Mode;
            Point at = _cursor;
            // Opened after this callback returns, so the drag loop moves on to the wheel window
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => FileDragConfirmed?.Invoke(mode, at, paths));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);
    }
}
