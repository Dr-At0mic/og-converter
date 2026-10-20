using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace OgConverter.Views
{
    public enum MessageKind { Info, Question, Warning }

    /// <summary>The app's MessageBox (Avalonia has none): a <see cref="DialogWindow"/> card with a message and one or two buttons.</summary>
    public sealed class MessageDialog : DialogWindow
    {
        private MessageDialog(string title, string message, MessageKind kind, string okText, string? cancelText)
        {
            Title = title;
            Width = 460;

            var (glyph, color, tint) = kind switch
            {
                MessageKind.Warning => ("", "WarningBrush", "WarningSubtleBrush"),
                MessageKind.Question => ("", "AccentBrush", "AccentSubtleBrush"),
                _ => ("", "AccentBrush", "AccentSubtleBrush")
            };

            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(24, 22, 24, 14), Background = Brushes.Transparent };
            header.PointerPressed += OnHeaderPointerPressed;
            var badge = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Top, Background = Brush(tint),
                Child = new TextBlock { Text = Icons.Icons.Glyph(glyph), Classes = { "Icon" }, Foreground = Brush(color) }
            };
            header.Children.Add(badge);
            var heading = new TextBlock { Text = title, Classes = { "H2" }, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(heading, 1);
            header.Children.Add(heading);

            var body = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 0, 24, 0), LineHeight = 19 };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (cancelText != null)
            {
                var cancel = new Button { Content = cancelText, IsCancel = true, MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
                cancel.Click += OnCancelClick;
                buttons.Children.Add(cancel);
            }
            var ok = new Button { Content = okText, IsDefault = true, MinWidth = 88 };
            if (this.TryFindResource("AccentButton", out object? theme) && theme is ControlTheme accent) ok.Theme = accent;
            ok.Click += (_, _) => Finish(true);
            buttons.Children.Add(ok);

            var footer = new Border
            {
                Background = Brush("WindowBrush"), BorderBrush = Brush("DividerBrush"), BorderThickness = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(0, 0, 12, 12), Padding = new Thickness(24, 14), Margin = new Thickness(0, 22, 0, 0),
                Child = buttons
            };

            Content = new Border
            {
                Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderStrongBrush"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Margin = new Thickness(16),
                BoxShadow = BoxShadows.Parse("0 8 28 0 #73000000"),
                Child = new StackPanel { Children = { header, body, footer } }
            };

            Opened += (_, _) => ok.Focus();
        }

        /// <summary>Shows a message with OK (and Cancel when <paramref name="cancelText"/> is set). True for OK.</summary>
        public static Task<bool> ShowAsync(Window? owner, string title, string message, MessageKind kind = MessageKind.Info,
            string okText = "OK", string? cancelText = null) =>
            new MessageDialog(title, message, kind, okText, cancelText).ShowForResultAsync(owner);

        /// <summary>OK / Cancel question.</summary>
        public static Task<bool> ConfirmAsync(Window? owner, string title, string message, string okText = "OK") =>
            ShowAsync(owner, title, message, MessageKind.Question, okText, "Cancel");
    }
}
