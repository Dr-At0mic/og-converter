using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using OgConverter.Controls;
using OgConverter.Services;
using OgConverter.Views;

namespace OgConverter
{
    /// <summary>
    /// Settings › Engine (one row per helper program, with the command that installs a missing one) and the "Recommended
    /// extras" prompt (first run and right after Install). OG Converter never runs the package manager: that needs root, so
    /// the user copies the command into a terminal.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Set by App when this copy was just started by Install, so the prompt shows even if it was seen before.</summary>
        public bool JustInstalled { get; set; }

        /// <summary>Off for snapshots, which show the window off-screen and can't answer a dialog.</summary>
        public bool OfferExtras { get; set; } = true;

        private sealed record EngineRow(EngineTool Tool, string Glyph, string Title, Func<string?> Path, string Purpose);

        private static readonly EngineRow[] EngineRows =
        {
            new(EngineTool.FFmpeg, "E714", "FFmpeg", () => null, "Video, audio, GIF, WebP, AVIF and JPEG XL conversion and compression, plus video crop and trim"),
            new(EngineTool.LibreOffice, "E8A5", "Documents (LibreOffice)", () => DocumentEngine.LibreOfficePath, "Word, Excel and PowerPoint formats, plus OpenDocument"),
            new(EngineTool.Browser, "E774", "Browser (Chromium)", () => BrowserRenderService.BrowserPath, "Markdown, web pages and SVG to PDF and images"),
            new(EngineTool.SevenZip, "E7B8", "7-Zip", () => DependencyService.SevenZipPath, "7z archives, and opening RAR and other archive formats"),
            new(EngineTool.Tesseract, "E8D2", "Text recognition (Tesseract)", () => Services.Platform.FindOnPath("tesseract"), "Extract text from pictures and scanned PDFs")
        };

        private void SetupExtrasPrompt()
        {
            Opened += async (_, _) =>
            {
                await Task.Delay(400); // let the window settle before a dialog appears over it
                await ShowExtrasPromptIfNeededAsync();
            };
        }

        private async Task ShowExtrasPromptIfNeededAsync()
        {
            if (!OfferExtras || (!JustInstalled && _settingsService.Current.ExtrasPromptShown)) return;

            bool ffmpegMissing = !_engine.IsFFmpegAvailable;
            bool officeMissing = !DocumentEngine.HasAnyBackend;
            if (!ffmpegMissing && !officeMissing) return;

            _settingsService.Current.ExtrasPromptShown = true;
            _settingsService.SaveSettings(_settingsService.Current);

            await new ExtrasDialog(ffmpegMissing, officeMissing).ShowForResultAsync(this);
            RecheckEngines();
        }

        /// <summary>Looks for the helpers again (after the user installed one) and rebuilds Settings › Engine.</summary>
        private void RecheckEngines()
        {
            _engine.RefreshFFmpeg();
            DocumentEngine.RefreshLibreOffice();
            CheckFFmpegStatus();
        }

        /// <summary>Builds Settings › Engine: status badge, path or purpose, and the install command for a missing helper.</summary>
        private void CheckFFmpegStatus()
        {
            if (EnginePanel == null) return;
            EnginePanel.Children.Clear();

            var missing = new List<string>();
            for (int i = 0; i < EngineRows.Length; i++)
            {
                var row = EngineRows[i];
                string? path = row.Tool == EngineTool.FFmpeg ? (_engine.IsFFmpegAvailable ? _engine.FFmpegPath : null) : row.Path();
                bool ok = path != null;
                string? command = ok ? null : PackageHints.InstallCommand(row.Tool);
                if (!ok) missing.Add(row.Title);

                if (i > 0) EnginePanel.Children.Add(new Border { Classes = { "RowDivider" } });
                EnginePanel.Children.Add(BuildEngineRow(row, path, command));
            }

            EngineHintText.IsVisible = missing.Count > 0;
            EngineHintText.Text = missing.Count > 0
                ? "Missing helpers are optional. Copy the command, run it in a terminal, then click Check again."
                : string.Empty;
        }

        private Control BuildEngineRow(EngineRow row, string? path, string? command)
        {
            bool ok = path != null;
            var grid = new Grid { Classes = { "SettingRow" }, ColumnDefinitions = new ColumnDefinitions("32,*,Auto") };

            grid.Children.Add(new TextBlock
            {
                Text = IconExtension.Glyph(row.Glyph), Classes = { "Icon" },
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0)
            });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = row.Title, FontWeight = FontWeight.SemiBold });
            var detail = new TextBlock
            {
                Text = ok ? path : $"Optional. {row.Purpose}.", Classes = { "Caption" }, Margin = new Thickness(0, 2, 24, 0),
                TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = ok ? TextWrapping.NoWrap : TextWrapping.Wrap
            };
            if (ok) ToolTip.SetTip(detail, path);
            text.Children.Add(detail);

            if (command != null)
            {
                var commandRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                var commandBox = new Border
                {
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4),
                    Child = new SelectableTextBlock { Text = command, Classes = { "Mono" }, FontSize = 12 }
                };
                commandBox.Bind(Border.BackgroundProperty, this.GetResourceObservable("InputBrush"));
                commandRow.Children.Add(commandBox);
                var copy = new Button { Content = "Copy", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4), FontSize = 12, Theme = ControlThemeOf("GhostButton") };
                copy.Click += async (_, _) =>
                {
                    await DesktopActions.CopyTextAsync(this, command);
                    copy.Content = "Copied";
                };
                commandRow.Children.Add(copy);
                text.Children.Add(commandRow);
            }
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!ok)
            {
                var recheck = new Button { Content = "Check again", Margin = new Thickness(0, 0, 10, 0), Theme = ControlThemeOf("GhostButton") };
                recheck.Click += (_, _) => RecheckEngines();
                right.Children.Add(recheck);
            }
            right.Children.Add(new Border
            {
                Classes = { "Badge", ok ? "ok" : "warn" },
                Child = new TextBlock { Text = ok ? "Ready" : "Not found" }
            });
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            return grid;
        }

        /// <summary>A button theme from Themes/Theme.axaml, for controls built in code.</summary>
        private ControlTheme? ControlThemeOf(string key) => this.TryFindResource(key, out object? theme) ? theme as ControlTheme : null;
    }
}
