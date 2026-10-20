using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace OgConverter.Services
{
    /// <summary>
    /// Reads files from the file tree of VS Code-based editors (VS Code, Cursor, Windsurf, Antigravity…) through
    /// UI Automation. Each tree row exposes its full path as the accessible name of its label
    /// ("E:\repo\README.md • Modified") and is marked "selected" in its class list.
    /// </summary>
    public static class IdeSelectionService
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private static readonly Condition TreeItems = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem);

        /// <summary>Electron/Chromium top-level window (where VS Code-based editors live).</summary>
        public static bool IsChromiumWindow(IntPtr hwnd)
        {
            var sb = new StringBuilder(64);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString().StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal);
        }

        /// <summary>
        /// The tree row under the cursor (what's being pressed or dragged), else the selected rows.
        /// Empty if the window has no such file tree.
        /// </summary>
        public static List<string> GetFiles(IntPtr topLevel, int cursorX, int cursorY)
        {
            var results = new List<string>();
            try
            {
                var underCursor = AutomationElement.FromPoint(new System.Windows.Point(cursorX, cursorY));
                if (FindRow(underCursor) is { } row && PathOf(row) is { } path)
                {
                    results.Add(path);
                    return results;
                }

                var root = AutomationElement.FromHandle(topLevel);
                // The first query switches Chromium's accessibility tree on; it's built a moment later
                AutomationElementCollection rows = root.FindAll(TreeScope.Descendants, TreeItems);
                for (int attempt = 0; rows.Count == 0 && attempt < 3; attempt++)
                {
                    Thread.Sleep(150);
                    rows = root.FindAll(TreeScope.Descendants, TreeItems);
                }

                foreach (AutomationElement r in rows)
                {
                    if (!HasClass(r, "selected")) continue;
                    if (PathOf(r) is { } p && !results.Contains(p)) results.Add(p);
                }

                // Nothing was under the cursor at first because the tree didn't exist yet: try that again
                if (results.Count == 0 && FindRow(AutomationElement.FromPoint(new System.Windows.Point(cursorX, cursorY))) is { } late && PathOf(late) is { } lp)
                {
                    results.Add(lp);
                }
            }
            catch
            {
                // Window closed, accessibility unavailable, element gone: no IDE selection
            }
            return results;
        }

        /// <summary>The list row containing <paramref name="element"/> (or itself).</summary>
        private static AutomationElement? FindRow(AutomationElement? element)
        {
            var walker = TreeWalker.RawViewWalker;
            for (int depth = 0; element != null && depth < 8; depth++)
            {
                if (element.Current.ControlType == ControlType.TreeItem && HasClass(element, "monaco-list-row")) return element;
                element = walker.GetParent(element);
            }
            return null;
        }

        /// <summary>The file or folder a file-tree row stands for, read from its label; null for other trees.</summary>
        private static string? PathOf(AutomationElement row)
        {
            foreach (AutomationElement e in row.FindAll(TreeScope.Descendants, Condition.TrueCondition))
            {
                if (!HasClass(e, "explorer-item")) continue;

                // "E:\repo\README.md • Modified": the decoration after the bullet is git/problem status
                string name = e.Current.Name ?? "";
                int bullet = name.IndexOf(" \u2022 ", StringComparison.Ordinal);
                string path = (bullet >= 0 ? name[..bullet] : name).Trim();
                return Path.IsPathFullyQualified(path) && (File.Exists(path) || Directory.Exists(path)) ? path : null;
            }
            return null;
        }

        private static bool HasClass(AutomationElement e, string cls)
        {
            string classes = e.Current.ClassName ?? "";
            foreach (string c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (c == cls) return true;
            }
            return false;
        }
    }
}
