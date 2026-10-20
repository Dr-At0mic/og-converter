using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Themes;
using OgConverter.Views;

namespace OgConverter
{
    /// <summary>Settings › Wheel templates and the Home quick switcher.</summary>
    public partial class MainWindow
    {
        private static readonly List<WheelItem> PreviewItems = new()
        {
            new WheelItem { Title = "PNG", IconGlyph = "" },
            new WheelItem { Title = "JPG", IconGlyph = "" },
            new WheelItem { Title = "WebP", IconGlyph = "" },
            new WheelItem { Title = "AVIF", IconGlyph = "" },
            new WheelItem { Title = "PDF", IconGlyph = "" },
            new WheelItem { Title = "ICO", IconGlyph = "" },
        };

        private readonly Dictionary<string, Border> _templateCards = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Border> _templateChecks = new(StringComparer.OrdinalIgnoreCase);
        private bool _syncingTemplateUi;

        private void SetupTemplates()
        {
            TemplateManager.Instance.TemplatesChanged += RebuildTemplateUi;
            TemplateManager.Instance.CurrentChanged += OnCurrentTemplateChanged;
            RebuildTemplateUi();

            TemplateGalleryCard.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            {
                var files = DroppedPaths(e);
                e.DragEffects = files.Count > 0 && TemplateManager.IsImportable(files[0]) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            });
            TemplateGalleryCard.AddHandler(DragDrop.DropEvent, async (_, e) =>
            {
                var files = DroppedPaths(e);
                e.Handled = true;
                if (files.Count > 0) await ImportTemplateAsync(files[0]);
            });
        }

        private void RebuildTemplateUi()
        {
            var manager = TemplateManager.Instance;

            TemplateGallery.Children.Clear();
            _templateCards.Clear();
            _templateChecks.Clear();
            foreach (var t in manager.Templates)
            {
                var card = BuildTemplateCard(t);
                _templateCards[t.Id] = card;
                TemplateGallery.Children.Add(card);
            }

            _syncingTemplateUi = true;
            TemplateQuickCombo.ItemsSource = manager.Templates.ToList();
            TemplateQuickCombo.SelectedItem = manager.Current;
            _syncingTemplateUi = false;

            TemplateErrorsText.Text = string.Join("\n", manager.LoadErrors.Select(e => "• " + e));
            TemplateErrorsText.IsVisible = manager.LoadErrors.Count > 0;
            UpdateTemplateSelection();
        }

        /// <summary>A gallery card: the template's own wheel on a little "desktop" tinted like the app would be.</summary>
        private Border BuildTemplateCard(WheelTemplate t)
        {
            var (convert, _, _) = AppThemeService.ModeColorsOf(t);
            Color window = AppThemeService.WindowColorOf(t);
            bool lightBase = string.Equals(t.App?.Base, "light", StringComparison.OrdinalIgnoreCase);

            var preview = new RadialWheelCanvas
            {
                Width = RadialWheelCanvas.BaseSize,
                Height = RadialWheelCanvas.BaseSize,
                PlaySounds = false,
                TemplateOverride = t,
                Items = PreviewItems,
                CurrentMode = WheelMode.Convert
            };
            preview.SetFile("Holiday.png", "Choose a format");

            // A little "desktop" behind each preview, tinted with the template's own colors
            Color backdropTop = PaletteMath.Mix(window, convert, lightBase ? 0.10 : 0.16);
            var backdrop = new Border
            {
                Height = 128,
                CornerRadius = new CornerRadius(7),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(backdropTop, 0), new GradientStop(window, 1) }
                },
                Child = new Viewbox { Child = preview, Margin = new Thickness(6), Stretch = Stretch.Uniform }
            };

            var check = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                IsVisible = false,
                Child = new TextBlock
                {
                    Text = IconExtension.Glyph("E73E"),
                    FontFamily = Icons.Icons.Font,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            check.Bind(Border.BackgroundProperty, this.GetResourceObservable("AccentBrush"));
            ((TextBlock)check.Child).Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("OnAccentBrush"));
            _templateChecks[t.Id] = check;

            var top = new Panel();
            top.Children.Add(backdrop);
            top.Children.Add(check);

            Button? remove = null;
            if (t.Source != TemplateSource.BuiltIn)
            {
                remove = new Button
                {
                    Theme = (Avalonia.Styling.ControlTheme)this.FindResource("IconButton")!,
                    Content = IconExtension.Glyph("E74D"),
                    Width = 26,
                    Height = 26,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(4),
                    Opacity = 0,
                    Tag = t
                };
                ToolTip.SetTip(remove, "Remove this template");
                remove.Click += OnRemoveTemplateClick;
                top.Children.Add(remove);
            }

            var name = new TextBlock { Text = t.Name, FontWeight = FontWeight.SemiBold, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 9, 2, 0) };
            string kind = t.Source switch { TemplateSource.BuiltIn => "Built-in", TemplateSource.Plugin => "Plugin", _ => "Community" };
            var meta = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(t.Author) || t.Source == TemplateSource.BuiltIn ? kind : $"{kind} · {t.Author}",
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(2, 1, 2, 0)
            };
            meta.Classes.Add("Caption");

            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(name);
            stack.Children.Add(meta);

            var card = new Border
            {
                Width = 154,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(6, 6, 6, 8),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = stack,
                Tag = t
            };
            ToolTip.SetTip(card, string.IsNullOrWhiteSpace(t.Description) ? t.Name : t.Description);
            card.Bind(Border.BackgroundProperty, this.GetResourceObservable("SurfaceRaisedBrush"));
            card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BorderBrush"));

            // Hovering a card replays its open animation, so you can see how it moves before picking it
            card.PointerEntered += (_, _) =>
            {
                preview.HoveredIndex = 2;
                preview.PlayIntro();
                if (remove != null) remove.Opacity = 1;
            };
            card.PointerExited += (_, _) =>
            {
                preview.HoveredIndex = -1;
                if (remove != null) remove.Opacity = 0;
            };
            card.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null) return;
                SelectTemplate(t);
            };
            return card;
        }

        private void UpdateTemplateSelection()
        {
            string current = TemplateManager.Instance.Current.Id;
            foreach (var (id, card) in _templateCards)
            {
                bool selected = string.Equals(id, current, StringComparison.OrdinalIgnoreCase);
                card.Bind(Border.BorderBrushProperty, this.GetResourceObservable(selected ? "AccentBrush" : "BorderBrush"));
                if (_templateChecks.TryGetValue(id, out var check)) check.IsVisible = selected;
            }
        }

        /// <summary>Switches template: crossfades the window from the old look to the new one.</summary>
        private void SelectTemplate(WheelTemplate template)
        {
            if (ReferenceEquals(template, TemplateManager.Instance.Current)) return;
            BeginThemeCrossfade();
            TemplateManager.Instance.Apply(template.Id);
            SoundService.Instance.Play(UiSound.Open, template);
        }

        private void OnCurrentTemplateChanged()
        {
            var current = TemplateManager.Instance.Current;
            AppThemeService.Apply(current);

            _settingsService.Current.WheelTemplateId = current.Id;
            _settingsService.SaveSettings(_settingsService.Current);

            _syncingTemplateUi = true;
            TemplateQuickCombo.SelectedItem = current;
            _syncingTemplateUi = false;
            UpdateTemplateSelection();
        }

        private void OnQuickTemplateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_syncingTemplateUi || TemplateQuickCombo.SelectedItem is not WheelTemplate t) return;
            SelectTemplate(t);
        }

        /// <summary>Freezes the current look into an image on top of the window, then fades it out once the new palette is in.</summary>
        private void BeginThemeCrossfade()
        {
            if (!IsVisible || _settingsService.Current.ReduceMotion || Content is not Control root || root.Bounds.Width < 1) return;

            double scaling = RenderScaling;
            var size = new PixelSize((int)Math.Ceiling(root.Bounds.Width * scaling), (int)Math.Ceiling(root.Bounds.Height * scaling));
            var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
            ThemeFadeImage.IsVisible = false;
            bitmap.Render(root);

            ThemeFadeImage.Transitions = null;
            ThemeFadeImage.Source = bitmap;
            ThemeFadeImage.Opacity = 1;
            ThemeFadeImage.IsVisible = true;
            ThemeFadeImage.Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(320), Delay = TimeSpan.FromMilliseconds(30), Easing = new CubicEaseInOut() }
            };
            Dispatcher.UIThread.Post(() => ThemeFadeImage.Opacity = 0, DispatcherPriority.Background);
            DispatcherTimer.RunOnce(() =>
            {
                ThemeFadeImage.IsVisible = false;
                ThemeFadeImage.Source = null;
                bitmap.Dispose();
            }, TimeSpan.FromMilliseconds(420));
        }

        // ------------------------------------------------------------------ add / remove / export

        private async void OnAddTemplateClick(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Locate a wheel template",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Wheel templates (template.json, *.ogtemplate, *.zip, *.dll)") { Patterns = new[] { "*.json", "*.ogtemplate", "*.zip", "*.dll" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await ImportTemplateAsync(path);
        }

        private async Task ImportTemplateAsync(string path)
        {
            if (TemplateManager.ContainsCode(path))
            {
                bool ok = await MessageDialog.ShowAsync(this, "Add plugin template",
                    "This template includes a plugin (.dll). Plugins are programs: they run with the same access to your files as OG Converter.\n\nOnly add plugins from people you trust. Add it?",
                    MessageKind.Warning, "Add", "Cancel");
                if (!ok) return;
            }

            try
            {
                var added = TemplateManager.Instance.Import(path);
                SelectTemplate(added[0]);
            }
            catch (TemplateException ex)
            {
                await MessageDialog.ShowAsync(this, "Couldn't add the template", ex.Message, MessageKind.Warning);
            }
        }

        private async void OnRemoveTemplateClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WheelTemplate t }) return;
            bool ok = await MessageDialog.ShowAsync(this, "Remove template", $"Remove the \"{t.Name}\" template?", MessageKind.Question, "Remove", "Cancel");
            if (!ok) return;

            if (ReferenceEquals(t, TemplateManager.Instance.Current)) BeginThemeCrossfade();
            try
            {
                TemplateManager.Instance.Remove(t);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await MessageDialog.ShowAsync(this, "Couldn't remove the template", ex.Message, MessageKind.Warning);
            }
        }

        private async void OnExportTemplateClick(object? sender, RoutedEventArgs e)
        {
            var t = TemplateManager.Instance.Current;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"Export \"{t.Name}\" as a template",
                SuggestedFileName = "template.json",
                DefaultExtension = "json",
                FileTypeChoices = new[] { new FilePickerFileType("Template") { Patterns = new[] { "*.json" } } }
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            try
            {
                TemplateManager.Export(t, path);
                DesktopActions.RevealInFileManager(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await MessageDialog.ShowAsync(this, "Couldn't export the template", ex.Message, MessageKind.Warning);
            }
        }

        private void OnOpenTemplatesFolderClick(object? sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(TemplateManager.TemplatesDirectory);
            DesktopActions.OpenFile(TemplateManager.TemplatesDirectory);
        }

        private void OnReloadTemplatesClick(object? sender, RoutedEventArgs e)
        {
            TemplateManager.Instance.Reload();
        }
    }
}
