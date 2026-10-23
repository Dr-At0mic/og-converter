using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>A colour swatch in the Folder Icon dialog.</summary>
    public sealed record ColorChoice(FolderIconColor Color, IBrush Fill)
    {
        public string Name => Color.Name;
    }

    /// <summary>A symbol tile in the Folder Icon dialog; <see cref="Symbol"/> null for a plain folder.</summary>
    public sealed record SymbolChoice(FolderIconSymbol? Symbol, Geometry? Geometry)
    {
        public string Name => Symbol?.Name ?? "No symbol";
        public bool IsNone => Symbol == null;
    }

    /// <summary>
    /// Folder Icon: a colour and a symbol from <see cref="FolderIconLibrary"/>, or a picture of the user's own, with a
    /// live preview. <see cref="Result"/> holds the drawn sizes; an empty list means Reset to default.
    /// </summary>
    public partial class FolderIconDialog : DialogWindow
    {
        private Bitmap? _picture;
        private string? _pictureName;

        public List<FolderIconImage>? Result { get; private set; }

        /// <summary>The colour and symbol of the design applied ("" symbol: none), to reopen with next time.</summary>
        public string ChosenColor => (ColorList.SelectedItem as ColorChoice)?.Name ?? "";
        public string ChosenSymbol => (SymbolList.SelectedItem as SymbolChoice)?.Symbol?.Name ?? "";

        /// <summary>For the XAML previewer.</summary>
        public FolderIconDialog() : this(new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }, "Blue", "Star") { }

        public FolderIconDialog(IReadOnlyList<string> folders, string lastColor, string lastSymbol)
        {
            InitializeComponent();

            SubtitleText.Text = folders.Count == 1 ? Path.GetFileName(folders[0]) : $"{folders.Count} folders: {string.Join(", ", folders.Take(3).Select(Path.GetFileName))}{(folders.Count > 3 ? "…" : "")}";
            ToolTip.SetTip(SubtitleText, string.Join("\n", folders));
            ResetButton.IsEnabled = folders.Any(f => Services.Platform.Desktop.GetFolderIcon(f) != null);

            var colors = FolderIconLibrary.Colors.Select(c => new ColorChoice(c, FolderIconRenderer.Brush(c.Front))).ToList();
            var symbols = new List<SymbolChoice> { new(null, null) };
            symbols.AddRange(FolderIconLibrary.Symbols.Select(s => new SymbolChoice(s, FolderIconRenderer.Symbol(s))));
            ColorList.ItemsSource = colors;
            SymbolList.ItemsSource = symbols;
            ColorList.SelectedItem = colors.FirstOrDefault(c => c.Name == lastColor) ?? colors[0];
            SymbolList.SelectedItem = symbols.FirstOrDefault(s => (s.Symbol?.Name ?? "") == lastSymbol) ?? symbols[0];
            UpdatePreview();
        }

        private FolderIconColor CurrentColor => ((ColorChoice)ColorList.SelectedItem!).Color;
        private FolderIconSymbol? CurrentSymbol => ((SymbolChoice)SymbolList.SelectedItem!).Symbol;

        private void UpdatePreview()
        {
            if (ColorList.SelectedItem == null || SymbolList.SelectedItem == null) return;
            var old = PreviewImage.Source as IDisposable;
            PreviewImage.Source = FolderIconRenderer.Render(CurrentColor, CurrentSymbol, _picture, 256);
            old?.Dispose();
            PreviewCaption.Text = _picture != null
                ? $"{_pictureName}. Pick a colour or symbol to go back to the library."
                : $"{ChosenColor} folder{(CurrentSymbol is { } s ? $", {s.Name.ToLowerInvariant()}" : "")}";
        }

        /// <summary>Picking from the library leaves picture mode.</summary>
        private void OnChoiceChanged(object? sender, SelectionChangedEventArgs e)
        {
            _picture = null;
            UpdatePreview();
        }

        private async void OnPictureClick(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a picture for the folder",
                FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;

            var image = ImageCodec.TryLoad(path, applyOrientation: true, maxDimension: 1024);
            if (image == null)
            {
                PreviewCaption.Text = $"{Path.GetFileName(path)} can't be opened.";
                return;
            }
            using var png = new MemoryStream();
            Services.Platform.Images.Encode(image.Bitmap, png, ImageEncoding.Png());
            png.Position = 0;
            _picture = new Bitmap(png);
            _pictureName = Path.GetFileName(path);
            UpdatePreview();
        }

        private void OnResetClick(object? sender, RoutedEventArgs e)
        {
            Result = new List<FolderIconImage>();
            Finish(true);
        }

        private void OnApplyClick(object? sender, RoutedEventArgs e)
        {
            Result = FolderIconRenderer.RenderAll(CurrentColor, CurrentSymbol, _picture);
            Finish(true);
        }
    }
}
