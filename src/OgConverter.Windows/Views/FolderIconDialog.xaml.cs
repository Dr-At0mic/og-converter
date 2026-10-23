using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OgConverter.Models;
using OgConverter.Platform.Windows;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// Folder Icon: a colour and a symbol from <see cref="FolderIconLibrary"/>, or a picture of the user's own, with a
    /// live preview. <see cref="Result"/> holds the drawn sizes; an empty list means Reset to default.
    /// </summary>
    public partial class FolderIconDialog : Window
    {
        public sealed class ColorChoice
        {
            public FolderIconColor Color { get; init; } = null!;
            public string Name => Color.Name;
            public Brush Fill { get; init; } = Brushes.Transparent;
        }

        public sealed class SymbolChoice
        {
            /// <summary>Null for a plain folder.</summary>
            public FolderIconSymbol? Symbol { get; init; }
            public string Name => Symbol?.Name ?? "No symbol";
            public Geometry? Geometry { get; init; }
            public Visibility NoneVisibility => Symbol == null ? Visibility.Visible : Visibility.Collapsed;
        }

        private BitmapSource? _picture;
        private string? _pictureName;

        public List<FolderIconImage>? Result { get; private set; }

        /// <summary>The colour and symbol of the design applied (null symbol: none), to reopen with next time.</summary>
        public string ChosenColor => (ColorList.SelectedItem as ColorChoice)?.Name ?? "";
        public string ChosenSymbol => (SymbolList.SelectedItem as SymbolChoice)?.Symbol?.Name ?? "";

        public FolderIconDialog(IReadOnlyList<string> folders, string lastColor, string lastSymbol)
        {
            InitializeComponent();

            SubtitleText.Text = folders.Count == 1 ? Path.GetFileName(folders[0]) : $"{folders.Count} folders: {string.Join(", ", folders.Take(3).Select(Path.GetFileName))}{(folders.Count > 3 ? "…" : "")}";
            SubtitleText.ToolTip = string.Join("\n", folders);
            bool anyCustom = folders.Any(f => Services.Platform.Desktop.GetFolderIcon(f) != null);
            ResetButton.IsEnabled = anyCustom;

            ColorList.ItemsSource = FolderIconLibrary.Colors.Select(c => new ColorChoice
            {
                Color = c,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(c.Front >> 24), (byte)(c.Front >> 16), (byte)(c.Front >> 8), (byte)c.Front))
            }).ToList();
            var symbols = new List<SymbolChoice> { new() };
            symbols.AddRange(FolderIconLibrary.Symbols.Select(s => new SymbolChoice { Symbol = s, Geometry = FolderIconRenderer.Symbol(s) }));
            SymbolList.ItemsSource = symbols;

            var colors = (List<ColorChoice>)ColorList.ItemsSource;
            ColorList.SelectedItem = colors.FirstOrDefault(c => c.Name == lastColor) ?? colors[0];
            SymbolList.SelectedItem = symbols.FirstOrDefault(s => (s.Symbol?.Name ?? "") == lastSymbol) ?? symbols[0];
            UpdatePreview();

            Loaded += (_, _) => Activate();
        }

        /// <summary>What Apply would set: the picture when one is chosen, else the folder design.</summary>
        private Drawing CurrentDrawing() =>
            _picture != null
                ? FolderIconRenderer.Picture(_picture)
                : FolderIconRenderer.Design(((ColorChoice)ColorList.SelectedItem).Color, ((SymbolChoice)SymbolList.SelectedItem).Symbol);

        private void UpdatePreview()
        {
            if (ColorList.SelectedItem == null || SymbolList.SelectedItem == null) return;
            PreviewImage.Source = new DrawingImage(CurrentDrawing());
            PreviewCaption.Text = _picture != null
                ? $"{_pictureName}. Pick a colour or symbol to go back to the library."
                : $"{ChosenColor} folder{(SymbolList.SelectedItem is SymbolChoice { Symbol: { } s } ? $", {s.Name.ToLowerInvariant()}" : "")}";
        }

        /// <summary>Picking from the library leaves picture mode.</summary>
        private void OnColorChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _picture = null;
            UpdatePreview();
        }

        private void OnSymbolChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _picture = null;
            UpdatePreview();
        }

        private void OnPictureClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose a picture for the folder",
                Filter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.heic;*.heif;*.avif|All files|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;

            var image = ImageCodec.TryLoad(dlg.FileName, applyOrientation: true, maxDimension: 1024);
            if (image == null)
            {
                PreviewCaption.Text = $"Windows can't open {Path.GetFileName(dlg.FileName)}.";
                return;
            }
            var bitmap = image.Bitmap.ToBitmapSource();
            bitmap.Freeze();
            _picture = bitmap;
            _pictureName = Path.GetFileName(dlg.FileName);
            UpdatePreview();
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            Result = new List<FolderIconImage>();
            DialogResult = true;
        }

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            Result = FolderIconRenderer.Render(CurrentDrawing());
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
