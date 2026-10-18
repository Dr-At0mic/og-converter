using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>Collects page ranges and split mode for the PDF SPLIT / EXTRACT tool.</summary>
    public partial class PdfSplitDialog : Window
    {
        private readonly int _pageCount;

        public ToolParameters? Result { get; private set; }

        public PdfSplitDialog(string pdfPath)
        {
            InitializeComponent();

            _pageCount = PdfToolsService.GetPageCount(pdfPath);
            FileInfoText.Text = $"{Path.GetFileName(pdfPath)}  •  {_pageCount} page{(_pageCount == 1 ? "" : "s")}";
            FileInfoText.ToolTip = pdfPath;
            RangesBox.Text = _pageCount > 1 ? $"1-{_pageCount}" : "1";

            Loaded += (_, _) =>
            {
                Activate();
                RangesBox.Focus();
                RangesBox.SelectAll();
                UpdatePreview();
            };
        }

        private void OnInputChanged(object sender, RoutedEventArgs e) => UpdatePreview();

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
                var ranges = PdfToolsService.ParsePageRanges(RangesBox.Text, _pageCount);
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
            var brush = (Brush)FindResource(ok ? "SuccessBrush" : "DangerBrush");
            PreviewText.Text = text;
            PreviewText.Foreground = brush;
            PreviewIcon.Text = ok ? "\uE73E" : "\uE783";
            PreviewIcon.Foreground = brush;
            ConfirmButton.IsEnabled = ok;
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            if (!ConfirmButton.IsEnabled) return;

            Result = new ToolParameters
            {
                PageRanges = RangesBox.Text,
                SplitMode = ModeEveryPageRadio.IsChecked == true ? PdfSplitMode.EveryPage
                          : ModeEachRangeRadio.IsChecked == true ? PdfSplitMode.EachRange
                          : PdfSplitMode.ExtractToOne
            };
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
