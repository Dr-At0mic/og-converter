using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OgConverter.Controls;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Wheel;

namespace OgConverter
{
    /// <summary>Settings › Wheel templates and the Home quick switcher.</summary>
    public partial class MainWindow
    {
        private readonly Dictionary<string, Border> _templateCards = new(StringComparer.OrdinalIgnoreCase);
        private bool _syncingTemplateUi;

        private static readonly List<WheelItem> PreviewItems = new()
        {
            new WheelItem { Title = "PNG", IconGlyph = "" },
            new WheelItem { Title = "JPG", IconGlyph = "" },
            new WheelItem { Title = "WebP", IconGlyph = "" },
            new WheelItem { Title = "AVIF", IconGlyph = "" },
            new WheelItem { Title = "PDF", IconGlyph = "" },
            new WheelItem { Title = "ICO", IconGlyph = "" },
        };

        private void SetupTemplates()
        {
            TemplateManager.Instance.TemplatesChanged += RebuildTemplateUi;
            TemplateManager.Instance.CurrentChanged += OnCurrentTemplateChanged;
            RebuildTemplateUi();
        }

        private void RebuildTemplateUi()
        {
            var manager = TemplateManager.Instance;

            TemplateGallery.Children.Clear();
            _templateCards.Clear();
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
            TemplateErrorsText.Visibility = manager.LoadErrors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateTemplateSelection();
        }

        private Border BuildTemplateCard(WheelTemplate t)
        {
            var palette = WheelPalette.For(t);

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

            // A little "desktop" behind each wheel, tinted with the template's own colors
            Color backdropTop = ColorUtil.Mix(AppThemeService.WindowColorOf(t), palette.Convert, palette.IsLight ? 0.10 : 0.16);
            Color backdropBottom = AppThemeService.WindowColorOf(t);
            var backdrop = new Border
            {
                Height = 128,
                CornerRadius = new CornerRadius(7),
                Background = WheelPainter.Frozen(new LinearGradientBrush(backdropTop, backdropBottom, 90)),
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
                Visibility = Visibility.Collapsed,
                Child = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            check.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            ((TextBlock)check.Child).SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");

            var top = new Grid();
            top.Children.Add(backdrop);
            top.Children.Add(check);

            Button? remove = null;
            if (t.Source != TemplateSource.BuiltIn)
            {
                remove = new Button
                {
                    Style = (Style)FindResource("IconButton"),
                    Content = "",
                    Width = 26,
                    Height = 26,
                    FontSize = 11,
                    ToolTip = "Remove this template",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(4),
                    Opacity = 0,
                    Tag = t
                };
                remove.Click += OnRemoveTemplateClick;
                top.Children.Add(remove);
            }

            var name = new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 9, 2, 0) };
            string kind = t.Source switch { TemplateSource.BuiltIn => "Built-in", TemplateSource.Plugin => "Plugin", _ => "Community" };
            var meta = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(t.Author) || t.Source == TemplateSource.BuiltIn ? kind : $"{kind} · {t.Author}",
                Style = (Style)FindResource("Caption"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(2, 1, 2, 0)
            };

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
                Cursor = Cursors.Hand,
                Child = stack,
                Tag = t,
                ToolTip = string.IsNullOrWhiteSpace(t.Description) ? t.Name : t.Description
            };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            // Hovering a card replays its open animation, so you can see how it moves before picking it
            card.MouseEnter += (_, _) =>
            {
                preview.HoveredIndex = 2;
                preview.PlayIntro();
                if (remove != null) remove.Opacity = 1;
            };
            card.MouseLeave += (_, _) =>
            {
                preview.HoveredIndex = -1;
                if (remove != null) remove.Opacity = 0;
            };
            card.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && FindAncestor<Button>(d) != null) return;
                SelectTemplate(t);
            };
            return card;
        }

        private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            for (var cur = d; cur != null; cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur))
            {
                if (cur is T match) return match;
            }
            return null;
        }

        private void UpdateTemplateSelection()
        {
            string current = TemplateManager.Instance.Current.Id;
            foreach (var (id, card) in _templateCards)
            {
                bool selected = string.Equals(id, current, StringComparison.OrdinalIgnoreCase);
                card.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentBrush" : "BorderBrush");
                var check = ((Grid)((StackPanel)card.Child).Children[0]).Children[1];
                check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>Switches template: crossfades the window from the old look to the new one and plays the new sound set.</summary>
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

        private void OnQuickTemplateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingTemplateUi || TemplateQuickCombo.SelectedItem is not WheelTemplate t) return;
            SelectTemplate(t);
        }

        /// <summary>Freezes the current look into an image on top of the window, then fades it out once the new palette is in.</summary>
        private void BeginThemeCrossfade()
        {
            if (!IsVisible || RadialWheelCanvas.ReduceMotion || Content is not FrameworkElement root || root.ActualWidth < 1) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX);
            int h = (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                                 null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            }
            var bitmap = new RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();

            ThemeFadeImage.Source = bitmap;
            ThemeFadeImage.Visibility = Visibility.Visible;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                BeginTime = TimeSpan.FromMilliseconds(30)
            };
            fade.Completed += (_, _) =>
            {
                ThemeFadeImage.Visibility = Visibility.Collapsed;
                ThemeFadeImage.Source = null;
                ThemeFadeImage.BeginAnimation(OpacityProperty, null);
            };
            ThemeFadeImage.BeginAnimation(OpacityProperty, fade);
        }

        // ------------------------------------------------------------------ add / remove / export

        private void OnAddTemplateClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Locate a wheel template",
                Filter = "Wheel templates (template.json, *.ogtemplate, *.zip, *.dll)|*.json;*.ogtemplate;*.zip;*.dll|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true) ImportTemplate(dlg.FileName);
        }

        private void OnTemplateGalleryDragOver(object sender, DragEventArgs e)
        {
            bool ok = e.Data.GetDataPresent(DataFormats.FileDrop)
                      && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && TemplateManager.IsImportable(files[0]);
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnTemplateGalleryDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) ImportTemplate(files[0]);
            e.Handled = true;
        }

        private void ImportTemplate(string path)
        {
            if (TemplateManager.ContainsCode(path))
            {
                var answer = MessageBox.Show(this,
                    "This template includes a plugin (.dll). Plugins are programs: they run with the same access to your files as OG Converter.\n\nOnly add plugins from people you trust. Add it?",
                    "Add plugin template", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
            }

            try
            {
                var added = TemplateManager.Instance.Import(path);
                SelectTemplate(added[0]);
            }
            catch (TemplateException ex)
            {
                MessageBox.Show(this, ex.Message, "Couldn't add the template", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnRemoveTemplateClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WheelTemplate t }) return;
            var answer = MessageBox.Show(this, $"Remove the \"{t.Name}\" template?", "Remove template", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;

            if (ReferenceEquals(t, TemplateManager.Instance.Current)) BeginThemeCrossfade();
            try
            {
                TemplateManager.Instance.Remove(t);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "Couldn't remove the template", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnExportTemplateClick(object sender, RoutedEventArgs e)
        {
            var t = TemplateManager.Instance.Current;
            var dlg = new SaveFileDialog
            {
                Title = $"Export \"{t.Name}\" as a template",
                FileName = "template.json",
                Filter = "Template (*.json)|*.json"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                TemplateManager.Export(t, dlg.FileName);
                Process.Start("explorer.exe", $"/select,\"{dlg.FileName}\"");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "Couldn't export the template", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnOpenTemplatesFolderClick(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(TemplateManager.TemplatesDirectory);
            Process.Start(new ProcessStartInfo(TemplateManager.TemplatesDirectory) { UseShellExecute = true });
        }

        private void OnReloadTemplatesClick(object sender, RoutedEventArgs e)
        {
            TemplateManager.Instance.Reload();
        }
    }
}
