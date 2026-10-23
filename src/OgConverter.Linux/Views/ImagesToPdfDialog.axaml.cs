using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>One picture in the page list.</summary>
    public sealed record PageEntry(string Path, int Index)
    {
        public string FileName => System.IO.Path.GetFileName(Path);
    }

    /// <summary>
    /// Several pictures dropped on Convert › PDF: one merged PDF (in an order the user can change) or a PDF per picture.
    /// </summary>
    public partial class ImagesToPdfDialog : DialogWindow
    {
        private readonly ObservableCollection<PageEntry> _entries = new();

        /// <summary>True for one merged PDF, false for a PDF per picture.</summary>
        public bool Merge { get; private set; }

        /// <summary>The pictures in the chosen page order (all of them when not merging).</summary>
        public IReadOnlyList<string> Pictures => _entries.Select(e => e.Path).ToList();

        /// <summary>For the XAML previewer.</summary>
        public ImagesToPdfDialog() : this(Array.Empty<string>(), true) { }

        public ImagesToPdfDialog(IEnumerable<string> pictures, bool merge)
        {
            InitializeComponent();
            foreach (string path in DroppedPaths.SortByName(pictures)) _entries.Add(new PageEntry(path, _entries.Count + 1));
            FileList.ItemsSource = _entries;
            TitleText.Text = $"{_entries.Count} pictures to PDF";
            (merge ? MergeRadio : EachRadio).IsChecked = true;

            Opened += (_, _) => UpdateState();
        }

        /// <summary>Numbers follow the order after a move or a removal.</summary>
        private void Renumber()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Index != i + 1) _entries[i] = _entries[i] with { Index = i + 1 };
            }
        }

        private void UpdateState()
        {
            if (!IsLoaded) return;

            bool merge = MergeRadio.IsChecked == true;
            int idx = FileList.SelectedIndex;
            FileList.IsEnabled = merge;
            RemoveButton.IsEnabled = merge && idx >= 0;
            UpButton.IsEnabled = merge && idx > 0;
            DownButton.IsEnabled = merge && idx >= 0 && idx < _entries.Count - 1;

            string? problem = merge && _entries.Count < 2 ? "Keep at least two pictures to merge." : null;
            bool ok = problem == null;
            SetStatusLine(SummaryIcon, SummaryText, problem ?? (merge
                ? $"1 PDF with {_entries.Count} pages, saved as {System.IO.Path.GetFileNameWithoutExtension(_entries[0].Path)}_merged.pdf"
                : $"{_entries.Count} PDFs, one per picture"), ok);
            CreateButton.IsEnabled = ok;
        }

        private void Move(int delta)
        {
            int idx = FileList.SelectedIndex;
            int target = idx + delta;
            if (idx < 0 || target < 0 || target >= _entries.Count) return;

            _entries.Move(idx, target);
            Renumber();
            FileList.SelectedIndex = target;
            UpdateState();
        }

        private void OnChoiceChanged(object? sender, RoutedEventArgs e) => UpdateState();
        private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateState();
        private void OnMoveUpClick(object? sender, RoutedEventArgs e) => Move(-1);
        private void OnMoveDownClick(object? sender, RoutedEventArgs e) => Move(+1);

        private void OnRemoveClick(object? sender, RoutedEventArgs e)
        {
            int idx = FileList.SelectedIndex;
            if (idx < 0) return;
            _entries.RemoveAt(idx);
            Renumber();
            if (_entries.Count > 0) FileList.SelectedIndex = Math.Min(idx, _entries.Count - 1);
            UpdateState();
        }

        private void OnCreateClick(object? sender, RoutedEventArgs e)
        {
            if (!CreateButton.IsEnabled) return;
            Merge = MergeRadio.IsChecked == true;
            Finish(true);
        }
    }
}
