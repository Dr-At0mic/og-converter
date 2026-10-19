using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OgConverter.Services
{
#nullable disable
    public static class ShellSelectionService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        // Used to find which Explorer tab a shell window object belongs to (Windows 11 tabs share one top-level HWND)
        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [PreserveSig]
            int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
        }

        // Only IOleWindow::GetWindow (first IShellBrowser method) is needed
        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            [PreserveSig]
            int GetWindow(out IntPtr phwnd);
        }

        private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");

        public static string GetActiveFile()
        {
            var files = GetSelectedOrFocusedFiles();
            return files.Count > 0 ? files[0] : null;
        }

        /// <summary>
        /// Returns the selection of the Explorer window the user is interacting with: the foreground window first
        /// (the drag source is activated when the drag starts), then the window under the cursor. Only if neither
        /// is an Explorer window does it fall back to any window with a selection.
        /// </summary>
        public static List<string> GetSelectedOrFocusedFiles()
        {
            var results = new List<string>();

            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return results;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic windows = shell.Windows();

                var targets = new List<IntPtr>();
                IntPtr foreground = GetForegroundWindow();
                if (foreground != IntPtr.Zero) targets.Add(GetAncestor(foreground, GA_ROOT));
                if (GetCursorPos(out POINT pt))
                {
                    IntPtr underCursor = GetAncestor(WindowFromPoint(pt), GA_ROOT);
                    if (underCursor != IntPtr.Zero && !targets.Contains(underCursor)) targets.Add(underCursor);
                }

                foreach (IntPtr target in targets)
                {
                    if (IsDesktopWindow(target))
                    {
                        ReadDesktopSelection(windows, results);
                    }
                    else if (IdeSelectionService.IsChromiumWindow(target))
                    {
                        // VS Code-based editors: their file tree, through UI Automation
                        results.AddRange(IdeSelectionService.GetFiles(target, pt.X, pt.Y));
                    }
                    else
                    {
                        ReadExplorerWindowSelection(windows, target, results);
                    }

                    if (results.Count > 0) return results;
                }

                // The user is in another app (an IDE, a browser…): a selection left in some background Explorer
                // window has nothing to do with what they're dragging, so don't guess
                if (targets.Count > 0 && IsOtherApp(targets[0])) return results;

                // Fallback: first Explorer window with a selection, then the desktop
                foreach (dynamic w in windows)
                {
                    try
                    {
                        ReadSelection(w.Document, results);
                        if (results.Count > 0) return results;
                    }
                    catch { }
                }

                ReadDesktopSelection(windows, results);
            }
            catch { }

            return results;
        }

        private static void ReadExplorerWindowSelection(dynamic windows, IntPtr topLevel, List<string> results)
        {
            // Windows 11 Explorer tabs: every tab is a separate shell window sharing the same top-level HWND.
            // The first ShellTabWindowClass child is the visible tab.
            IntPtr activeTab = FindWindowEx(topLevel, IntPtr.Zero, "ShellTabWindowClass", null);
            dynamic fallbackDoc = null;

            foreach (dynamic w in windows)
            {
                try
                {
                    if (new IntPtr((long)w.HWND) != topLevel) continue;

                    IntPtr tab = GetTabWindow(w);
                    if (activeTab == IntPtr.Zero || tab == IntPtr.Zero || tab == activeTab)
                    {
                        ReadSelection(w.Document, results);
                        if (results.Count > 0) return;
                    }
                    fallbackDoc ??= w.Document;
                }
                catch { }
            }

            // Couldn't match the tab precisely; use the first tab of this window
            if (results.Count == 0 && fallbackDoc != null)
            {
                try { ReadSelection(fallbackDoc, results); } catch { }
            }
        }

        private static IntPtr GetTabWindow(object shellWindow)
        {
            IntPtr browserPtr = IntPtr.Zero;
            try
            {
                if (shellWindow is not IServiceProvider sp) return IntPtr.Zero;
                Guid sid = SID_STopLevelBrowser, iid = IID_IShellBrowser;
                if (sp.QueryService(ref sid, ref iid, out browserPtr) != 0 || browserPtr == IntPtr.Zero) return IntPtr.Zero;

                var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(browserPtr);
                return browser.GetWindow(out IntPtr hwnd) == 0 ? hwnd : IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
            finally
            {
                if (browserPtr != IntPtr.Zero) Marshal.Release(browserPtr);
            }
        }

        private static void ReadDesktopSelection(dynamic windows, List<string> results)
        {
            try
            {
                object loc = 0; // CSIDL_DESKTOP
                object locRoot = 0;
                int hwnd = 0;
                // SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1
                dynamic deskObj = windows.FindWindowSW(ref loc, ref locRoot, 8, out hwnd, 1);
                if (deskObj != null) ReadSelection(deskObj.Document, results);
            }
            catch { }
        }

        private static void ReadSelection(dynamic doc, List<string> results)
        {
            if (doc == null) return;

            dynamic sel = doc.SelectedItems();
            if (sel != null && sel.Count > 0)
            {
                foreach (dynamic item in sel)
                {
                    AddIfExists((string)item.Path, results);
                }
            }

            // FocusedItem is vital during an active drag when the selection count can be 0
            if (results.Count == 0)
            {
                dynamic focused = doc.FocusedItem;
                if (focused != null) AddIfExists((string)focused.Path, results);
            }
        }

        private static void AddIfExists(string path, List<string> results)
        {
            if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)) && !results.Contains(path))
            {
                results.Add(path);
            }
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>A window of some other application: not Explorer, the desktop, the taskbar, or OG Converter itself.</summary>
        private static bool IsOtherApp(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;

            var sb = new StringBuilder(64);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString() is not ("CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
        }

        private static bool IsDesktopWindow(IntPtr hwnd)
        {
            var sb = new StringBuilder(64);
            GetClassName(hwnd, sb, sb.Capacity);
            string cls = sb.ToString();
            return cls is "Progman" or "WorkerW";
        }
    }
}
