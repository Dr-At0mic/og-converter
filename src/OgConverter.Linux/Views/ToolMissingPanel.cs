using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// "This needs a helper that isn't installed" strip for the Archive, Protect and Compress dialogs. Installs it when
    /// the system has a package manager OG Converter may run; otherwise copies the distribution's install command (it
    /// needs root) and offers Check again; otherwise opens the download page. <see cref="Installed"/> asks the dialog to
    /// look again.
    /// </summary>
    public sealed class ToolMissingPanel : Border
    {
        private readonly TextBlock _text;
        private readonly Button _action;
        private readonly Button _recheck;
        private Dependency _tool;

        /// <summary>The helper may be there now: re-validate.</summary>
        public event EventHandler? Installed;

        protected override Type StyleKeyOverride => typeof(Border);

        public ToolMissingPanel()
        {
            IsVisible = false;
            CornerRadius = new CornerRadius(6);
            Padding = new Thickness(12, 10);
            this.Bind(BackgroundProperty, this.GetResourceObservable("WarningSubtleBrush"));

            _text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            _action = new Button { Padding = new Thickness(12, 4), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            _action.Click += OnActionClick;
            _recheck = new Button { Content = "Check again", Padding = new Thickness(12, 4), FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
            _recheck.Click += (_, _) => Installed?.Invoke(this, EventArgs.Empty);
            AttachedToVisualTree += (_, _) =>
            {
                if (this.TryFindResource("GhostButton", out object? theme) && theme is ControlTheme ghost) _recheck.Theme = ghost;
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _action, _recheck } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(_text);
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);
            Child = grid;
        }

        /// <summary>Shows the strip for <paramref name="tool"/> with <paramref name="message"/>, or hides it when null.</summary>
        public void Show(Dependency? tool, string? message = null)
        {
            IsVisible = tool != null;
            if (tool is not { } t) return;
            if (t != _tool) _recheck.IsVisible = false;
            _tool = t;
            _text.Text = message ?? $"This needs {PackageHints.DisplayName(t)}, which isn't installed.";
            _action.IsEnabled = true;
            _action.Content = DependencyService.CanInstallDependency(t) ? $"Install {PackageHints.DisplayName(t)}"
                            : PackageHints.InstallCommand(PackageHints.ToolFor(t)) != null ? "Copy install command"
                            : "Download page";
        }

        private async void OnActionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var tool = _tool;
            var owner = TopLevel.GetTopLevel(this) as Window;

            if (DependencyService.CanInstallDependency(tool))
            {
                object? content = _action.Content;
                _action.IsEnabled = false;
                _action.Content = tool == Dependency.Guetzli ? "Downloading…" : "Installing…";
                string? error = await DependencyService.InstallToolAsync(tool);
                _action.Content = content;
                _action.IsEnabled = true;
                if (error != null) await MessageDialog.ShowAsync(owner, $"Install {PackageHints.DisplayName(tool)}", error, MessageKind.Warning);
                Installed?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (PackageHints.InstallCommand(PackageHints.ToolFor(tool)) is { } command)
            {
                await DesktopActions.CopyTextAsync(owner, command);
                _action.Content = "Copied";
                _text.Text = $"Run this in a terminal, then click Check again:\n{command}";
                _recheck.IsVisible = true;
                return;
            }

            try { DependencyService.OpenDownloadPage(tool); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { DesktopActions.OpenUrl(DependencyService.DownloadPage(tool)); }
        }
    }
}
