using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using OgConverter.Services;
using OgConverter.Themes;

namespace OgConverter.Views
{
    /// <summary>
    /// Base for the tool dialogs: a borderless, always-on-top card with a drag-to-move header, like the Windows dialogs.
    /// <see cref="ShowForResultAsync"/> works with or without an owner (the wheel can run while the dashboard is hidden).
    /// </summary>
    public class DialogWindow : Window
    {
        protected override Type StyleKeyOverride => typeof(Window);

        public DialogWindow()
        {
            WindowDecorations = WindowDecorations.None;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            CanResize = false;
            Topmost = true;
            ShowInTaskbar = true;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = AppIcon.CreateWindowIcon(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { }

            Opened += (_, _) => Activate();
        }

        /// <summary>Shows the dialog and waits for it to close. True when the user confirmed.</summary>
        public async Task<bool> ShowForResultAsync(Window? owner)
        {
            if (owner != null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                return await ShowDialog<bool?>(owner) == true;
            }

            var closed = new TaskCompletionSource<bool>();
            Closed += (_, _) => closed.TrySetResult(_confirmed);
            Show();
            return await closed.Task;
        }

        private bool _confirmed;

        /// <summary>Closes with the user's answer.</summary>
        protected void Finish(bool confirmed)
        {
            _confirmed = confirmed;
            Close(confirmed);
        }

        protected void OnCancelClick(object? sender, RoutedEventArgs e) => Finish(false);

        /// <summary>The header moves the window (there's no title bar).</summary>
        protected void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!e.Handled && e.Key == Key.Escape)
            {
                Finish(false);
                e.Handled = true;
            }
        }

        /// <summary>A theme brush by key (the palette follows the wheel template).</summary>
        protected static IBrush Brush(string key) => AppThemeService.Brush(key);

        /// <summary>Wait cursor while a quick check runs on the UI thread.</summary>
        protected void SetBusy(bool busy) => Cursor = busy ? new Cursor(StandardCursorType.Wait) : null;

        /// <summary>The status line under a form: check, warning or info glyph and text in the matching color.</summary>
        protected static void SetStatusLine(TextBlock icon, TextBlock text, string message, bool? ok)
        {
            var brush = Brush(ok switch { true => "SuccessBrush", false => "DangerBrush", _ => "TextSecondaryBrush" });
            text.Text = message;
            text.Foreground = brush;
            icon.Text = Icons.Icons.Glyph(ok switch { true => "", false => "", _ => "" });
            icon.Foreground = brush;
        }
    }
}
