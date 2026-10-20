using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OgConverter.Controls;
using OgConverter.Services;
using OgConverter.Views;

namespace OgConverter
{
    /// <summary>
    /// Settings › Desktop integration (the GNOME Shell drag-and-tap helper and the file-manager right-click menus), the Home
    /// helper banner, and the "Listening for shortcuts" pill. Linux only: Windows gets global hooks and the Explorer menu
    /// for free.
    /// </summary>
    public partial class MainWindow
    {
        private readonly Dictionary<FileManagerKind, CheckBox> _fileManagerToggles = new();
        private bool _syncingIntegration;
        private HelperState _helperState = HelperState.NotGnome;
        private DateTime _lastIntegrationCheck;

        private void SetupIntegration()
        {
            foreach (FileManagerKind kind in Enum.GetValues<FileManagerKind>())
            {
                // Only the file managers this computer has, but always the one already enabled (so it can be turned off)
                if (!FileManagerIntegration.IsFileManagerPresent(kind) && !FileManagerIntegration.IsEnabled(kind)) continue;
                IntegrationPanel.Children.Add(new Border { Classes = { "RowDivider" } });
                IntegrationPanel.Children.Add(BuildFileManagerRow(kind));
            }

            // The helper can be enabled from GNOME's Extensions app or a re-login while the window is hidden
            Activated += (_, _) =>
            {
                if (DateTime.UtcNow - _lastIntegrationCheck > TimeSpan.FromSeconds(5)) RefreshIntegration();
            };
            RefreshIntegration();
        }

        private Control BuildFileManagerRow(FileManagerKind kind)
        {
            var grid = new Grid { Classes = { "SettingRow" }, ColumnDefinitions = new ColumnDefinitions("32,*,Auto") };
            grid.Children.Add(new TextBlock
            {
                Text = IconExtension.Glyph("E8B7"), Classes = { "Icon" },
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0)
            });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = $"{FileManagerIntegration.DisplayName(kind)} menu", FontWeight = FontWeight.SemiBold });
            text.Children.Add(new TextBlock { Text = FileManagerIntegration.Description(kind), Classes = { "Caption" }, Margin = new Thickness(0, 2, 24, 0) });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var toggle = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Theme = ControlThemeOf("ToggleSwitch") };
            toggle.IsCheckedChanged += (_, _) => OnFileManagerToggled(kind, toggle);
            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);
            _fileManagerToggles[kind] = toggle;
            return grid;
        }

        private async void OnFileManagerToggled(FileManagerKind kind, CheckBox toggle)
        {
            if (_syncingIntegration) return;
            try
            {
                if (toggle.IsChecked == true) FileManagerIntegration.Enable(kind);
                else FileManagerIntegration.Disable(kind);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                await MessageDialog.ShowAsync(this, $"{FileManagerIntegration.DisplayName(kind)} menu", $"Couldn't change the menu:\n{ex.Message}", MessageKind.Warning);
            }
            RefreshIntegration();
        }

        /// <summary>Re-reads the helper state and the menu files, and updates Settings, the Home banner and the pill.</summary>
        private async void RefreshIntegration()
        {
            _lastIntegrationCheck = DateTime.UtcNow;

            _syncingIntegration = true;
            foreach (var (kind, toggle) in _fileManagerToggles) toggle.IsChecked = FileManagerIntegration.IsEnabled(kind);
            _syncingIntegration = false;

            HelperState state;
            try { state = await GnomeExtensionService.GetStateAsync(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { state = HelperState.Error; }
            _helperState = state;
            ShowHelperState(state);
        }

        private void ShowHelperState(HelperState state)
        {
            (string text, string badge, string badgeClass, string? button) = state switch
            {
                HelperState.Active => ("Drag a file in Files and tap a key: the extension tells OG Converter which key and where the pointer is.",
                    "Active", "ok", null),
                HelperState.NeedsLogin => ("Installed. GNOME loads new extensions when you log in, so log out and back in once to start using drag-and-tap.",
                    "Log out to finish", "warn", null),
                HelperState.Disabled => ("Installed but turned off. Turn it on here or in the Extensions app.",
                    "Off", "warn", "Turn on"),
                HelperState.Outdated => ("A newer version ships with this copy of OG Converter. Update it, then log out and back in.",
                    "Update available", "warn", "Update"),
                HelperState.Error => ("GNOME Shell reported a problem loading the extension. Reinstall it, then log out and back in.",
                    "Error", "warn", "Reinstall"),
                HelperState.NotInstalled => ("GNOME doesn't let apps see keys during a drag. This small extension does it for OG Converter, and only while OG Converter is running.",
                    "Not installed", "warn", "Install"),
                _ => ($"Drag-and-tap needs GNOME Shell; this is {GnomeExtensionService.DesktopName}. Use the right-click menus below instead.",
                    "Not available", "neutral", null)
            };

            GnomeHelperText.Text = text;
            GnomeHelperStatusText.Text = badge;
            GnomeHelperBadge.Classes.Set("ok", badgeClass == "ok");
            GnomeHelperBadge.Classes.Set("warn", badgeClass == "warn");
            GnomeHelperBadge.Classes.Set("neutral", badgeClass == "neutral");
            GnomeHelperButton.IsVisible = button != null;
            GnomeHelperButton.Content = button ?? "Install";

            // Home: a banner when drag-and-tap could work but doesn't yet
            bool needsAction = state is HelperState.NotInstalled or HelperState.Disabled or HelperState.Outdated or HelperState.Error or HelperState.NeedsLogin;
            HelperBanner.IsVisible = needsAction;
            HelperBannerButton.IsVisible = button != null;
            HelperBannerButton.Content = button == "Install" ? "Install helper" : button ?? string.Empty;
            HelperBannerTitle.Text = state == HelperState.NeedsLogin ? "Log out and back in to finish setting up drag-and-tap" : "Drag-and-tap needs the GNOME helper";
            HelperBannerText.Text = text;

            HomeSubtitle.Text = state == HelperState.NotGnome
                ? "Right-click any file in your file manager and choose OG Converter to open the wheel."
                : "Start dragging any file in Files, then tap a shortcut key to open the wheel.";

            UpdateListeningPill();
        }

        /// <summary>Green when key taps reach the app; amber when only the menus and the test area work.</summary>
        private void UpdateListeningPill()
        {
            bool listening = WheelController.ListeningForTriggers && _helperState == HelperState.Active;
            ListeningText.Text = listening ? "Listening for shortcuts"
                               : _helperState == HelperState.NotGnome ? "Right-click menus"
                               : "Shortcuts not set up";
            string color = listening ? "SuccessBrush" : "WarningBrush";
            ListeningText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(color));
            ListeningDot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, this.GetResourceObservable(color));
            ListeningPill.Bind(Border.BackgroundProperty, this.GetResourceObservable(listening ? "SuccessSubtleBrush" : "WarningSubtleBrush"));
            ToolTip.SetTip(ListeningPill, listening
                ? "The GNOME helper sends key taps during a drag to OG Converter"
                : "See Settings › Desktop integration");
        }

        private async void OnInstallGnomeHelperClick(object? sender, RoutedEventArgs e)
        {
            if (_helperState == HelperState.Disabled)
            {
                await Task.Run(() => LinuxInstallService.RunQuietly("gnome-extensions", "enable", GnomeExtensionService.Uuid));
                RefreshIntegration();
                return;
            }

            GnomeHelperButton.IsEnabled = HelperBannerButton.IsEnabled = false;
            bool needsLogin;
            try
            {
                needsLogin = await GnomeExtensionService.InstallAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GnomeHelperButton.IsEnabled = HelperBannerButton.IsEnabled = true;
                await MessageDialog.ShowAsync(this, "Drag-and-tap helper", $"Couldn't install the GNOME Shell extension:\n{ex.Message}", MessageKind.Warning);
                return;
            }
            GnomeHelperButton.IsEnabled = HelperBannerButton.IsEnabled = true;
            RefreshIntegration();

            if (needsLogin)
            {
                await MessageDialog.ShowAsync(this, "Drag-and-tap helper",
                    "The extension is installed. GNOME on Wayland only loads extensions when you log in, so log out and back in once.\n\n" +
                    "After that, drag any file in Files and tap Shift, Ctrl or Alt to open the wheel. Until then, use the right-click menus or the test area.");
            }
        }
    }
}
