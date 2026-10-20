using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>Collects page ranges and split mode for the PDF SPLIT / EXTRACT tool.</summary>
    public partial class PdfSplitDialog : DialogWindow
    {
        private readonly int _pageCount;

        public ToolParameters? Result { get; private set; }

        /// <summary>For the XAML previewer.</summary>
        public PdfSplitDialog() => InitializeComponent();

        public PdfSplitDialog(string pdfPath)
        {
            InitializeComponent();

            _pageCount = PdfToolsService.GetPageCount(pdfPath);
            FileInfoText.Text = $"{Path.GetFileName(pdfPath)}  •  {_pageCount} page{(_pageCount == 1 ? "" : "s")}";
            ToolTip.SetTip(FileInfoText, pdfPath);
            RangesBox.Text = _pageCount > 1 ? $"1-{_pageCount}" : "1";

            Opened += (_, _) =>
            {
                RangesBox.Focus();
                RangesBox.SelectAll();
                UpdatePreview();
            };
        }

        private void OnInputChanged(object? sender, RoutedEventArgs e) => UpdatePreview();

        private void OnRangesChanged(object? sender, TextChangedEventArgs e) => UpdatePreview();

        private void UpdatePreview()
        {
            if (!IsLoaded) return;

            bool everyPage = ModeEveryPageRadio.IsChecked == true;
            RangesBox.IsEnabled = !everyPage;
            ConfirmButton.Content = ModeExtractRadio.IsChecked == true ? "Extract" : "Split";

            if (everyPage)
            {
                ShowPreview($"{_pageCount} PDF file{(_pageCount == 1 ? "" : "s")}, one per page", ok: true);
                return;
            }

            try
            {
                var ranges = PdfToolsService.ParsePageRanges(RangesBox.Text ?? string.Empty, _pageCount);
                int pages = ranges.Sum(r => r.End - r.Start + 1);

                ShowPreview(ModeExtractRadio.IsChecked == true
                    ? $"1 PDF with {pages} page{(pages == 1 ? "" : "s")}"
                    : $"{ranges.Count} PDF file{(ranges.Count == 1 ? "" : "s")} ({string.Join(", ", ranges.Select(r => r.Start == r.End ? $"p{r.Start}" : $"p{r.Start}-{r.End}"))})",
                    ok: true);
            }
            catch (FormatException ex)
            {
                ShowPreview(ex.Message, ok: false);
            }
        }

        private void ShowPreview(string text, bool ok)
        {
            SetStatusLine(PreviewIcon, PreviewText, text, ok);
            ConfirmButton.IsEnabled = ok;
        }

        private void OnConfirmClick(object? sender, RoutedEventArgs e)
        {
            if (!ConfirmButton.IsEnabled) return;

            Result = new ToolParameters
            {
                PageRanges = RangesBox.Text ?? string.Empty,
                SplitMode = ModeEveryPageRadio.IsChecked == true ? PdfSplitMode.EveryPage
                          : ModeEachRangeRadio.IsChecked == true ? PdfSplitMode.EachRange
                          : PdfSplitMode.ExtractToOne
            };
            Finish(true);
        }
    }
}
