using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using OgConverter.Models;

namespace OgConverter.Services
{
    public class GlobalHookService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_LBUTTONUP = 0x0202;

        private const int VK_SHIFT = 0x10;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;

        private const int VK_CONTROL = 0x11;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;

        private const int VK_MENU = 0x12; // Alt
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;

        private const int VK_ESCAPE = 0x1B;
        private const int VK_LBUTTON = 0x01;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private LowLevelKeyboardProc? _keyboardProc;
        private LowLevelMouseProc? _mouseProc;
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;

        public event Action<WheelMode, Point, string?>? TriggerWheelRequested;
        public event Action? DismissWheelRequested;
        public event Action? MouseReleased;

        /// <summary>A wheel shortcut key was released (used by the "close on key release" setting).</summary>
        public event Action<WheelMode>? ShortcutReleased;
        public event Action<Point>? MouseMoved;

        private const int WM_MOUSEMOVE = 0x0200;

        public bool IsActive => _keyboardHookId != IntPtr.Zero;

        /// <summary>
        /// Forward mouse moves and releases only while the wheel is open. Otherwise every mouse move on the desktop
        /// would wake the UI thread.
        /// </summary>
        public bool TrackMouse
        {
            get => Volatile.Read(ref _trackMouse);
            set => Volatile.Write(ref _trackMouse, value);
        }
        private bool _trackMouse;

        private Thread? _hookThread;
        private uint _hookThreadId;
        private Dispatcher? _ui;
        private long _pendingMove;      // latest cursor position, packed X:Y
        private int _moveQueued;        // 1 while a MouseMoved dispatch is waiting to run

        /// <summary>
        /// Installs the hooks on a dedicated thread with its own message loop. Low-level hooks are called on the
        /// installing thread, so on the UI thread any busy moment (a render, a dialog opening) would stall keyboard
        /// and mouse input for the whole desktop, and Windows silently removes hooks that are too slow.
        /// </summary>
        public void Start()
        {
            if (_hookThread != null) return;

            _ui = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;

            using var ready = new ManualResetEventSlim();
            _hookThread = new Thread(() =>
            {
                _hookThreadId = GetCurrentThreadId();
                IntPtr module = GetModuleHandle(null);
                _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
                _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
                ready.Set();

                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                if (_keyboardHookId != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHookId);
                if (_mouseHookId != IntPtr.Zero) UnhookWindowsHookEx(_mouseHookId);
                _keyboardHookId = IntPtr.Zero;
                _mouseHookId = IntPtr.Zero;
            })
            {
                IsBackground = true,
                Name = "OG Converter input hooks",
                Priority = ThreadPriority.AboveNormal
            };
            _hookThread.Start();
            ready.Wait(TimeSpan.FromSeconds(2));
        }

        public void Stop()
        {
            if (_hookThread == null) return;
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookThread.Join(TimeSpan.FromSeconds(1));
            _hookThread = null;
            _keyboardProc = null;
            _mouseProc = null;
        }

        private void Post(Action action) => _ui?.BeginInvoke(DispatcherPriority.Input, action);

        public string ConvertKeyName { get; set; } = "Shift";
        public string ToolsKeyName { get; set; } = "Control";
        public string ActionsKeyName { get; set; } = "Alt";

        public void UpdateKeyBindings(string convertKey, string toolsKey, string actionsKey)
        {
            ConvertKeyName = string.IsNullOrWhiteSpace(convertKey) ? "Shift" : convertKey;
            ToolsKeyName = string.IsNullOrWhiteSpace(toolsKey) ? "Control" : toolsKey;
            ActionsKeyName = string.IsNullOrWhiteSpace(actionsKey) ? "Alt" : actionsKey;
        }

        private int _lastModifierVk = 0;

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                {
                    if (vkCode == VK_ESCAPE)
                    {
                        _lastModifierVk = 0;
                        Post(() => DismissWheelRequested?.Invoke());
                    }
                    else if (IsConfiguredModifierKey(vkCode))
                    {
                        // Prevent auto-repeat from spamming reposition while key is held down
                        if (vkCode == _lastModifierVk)
                        {
                            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
                        }
                        _lastModifierVk = vkCode;

                        // Check if mouse left button is down (indicates an active drag)
                        bool isDragging = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                        if (isDragging)
                        {
                            WheelMode mode = GetModeForVk(vkCode) ?? WheelMode.Convert;

                            GetCursorPos(out POINT cursorPt);
                            var screenPoint = new Point(cursorPt.X, cursorPt.Y);

                            // The dragged file is looked up on the UI thread: that's a COM call into Explorer,
                            // far too slow for a hook callback
                            Post(() => TriggerWheelRequested?.Invoke(mode, screenPoint, null));
                        }
                    }
                }
                else if (wParam == (IntPtr)0x0101 || wParam == (IntPtr)0x0105) // WM_KEYUP or WM_SYSKEYUP
                {
                    if (IsConfiguredModifierKey(vkCode))
                    {
                        if (vkCode == _lastModifierVk)
                        {
                            _lastModifierVk = 0;
                        }

                        if (TrackMouse && GetModeForVk(vkCode) is WheelMode released)
                        {
                            Post(() => ShortcutReleased?.Invoke(released));
                        }
                    }
                }
            }

            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                if (wParam == (IntPtr)WM_MOUSEMOVE)
                {
                    if (TrackMouse)
                    {
                        var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                        Interlocked.Exchange(ref _pendingMove, ((long)hookStruct.pt.X << 32) | (uint)hookStruct.pt.Y);
                        // Coalesce: one queued dispatch carries the latest position, however many moves arrive meanwhile
                        if (Interlocked.Exchange(ref _moveQueued, 1) == 0) Post(FlushMouseMove);
                    }
                }
                else if (wParam == (IntPtr)WM_LBUTTONUP)
                {
                    _lastModifierVk = 0;
                    if (TrackMouse) Post(() => MouseReleased?.Invoke());
                }
            }

            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private void FlushMouseMove()
        {
            Interlocked.Exchange(ref _moveQueued, 0);
            long packed = Interlocked.Read(ref _pendingMove);
            MouseMoved?.Invoke(new Point((int)(packed >> 32), (int)(packed & 0xFFFFFFFF)));
        }

        private WheelMode? GetModeForVk(int vk)
        {
            if (MatchesKey(vk, ConvertKeyName)) return WheelMode.Convert;
            if (MatchesKey(vk, ToolsKeyName)) return WheelMode.Tools;
            if (MatchesKey(vk, ActionsKeyName)) return WheelMode.Actions;
            return null;
        }

        private bool IsConfiguredModifierKey(int vk)
        {
            return MatchesKey(vk, ConvertKeyName) ||
                   MatchesKey(vk, ToolsKeyName) ||
                   MatchesKey(vk, ActionsKeyName);
        }

        private static bool MatchesKey(int vk, string keyName)
        {
            return keyName.Trim().ToLowerInvariant() switch
            {
                "shift" => vk is VK_SHIFT or VK_LSHIFT or VK_RSHIFT,
                "control" or "ctrl" => vk is VK_CONTROL or VK_LCONTROL or VK_RCONTROL,
                "alt" or "menu" => vk is VK_MENU or VK_LMENU or VK_RMENU,
                "win" or "windows" => vk is 0x5B or 0x5C,
                "space" => vk == 0x20,
                _ => false
            };
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_QUIT = 0x0012;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);
    }
}
