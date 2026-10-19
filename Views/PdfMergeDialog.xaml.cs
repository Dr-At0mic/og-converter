using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>Lets the user build and order the list of PDFs for the MERGE PDFs tool.</summary>
    public partial class PdfMergeDialog : Window
    {
        public class MergeEntry
        {
            public string Path { get; init; } = string.Empty;
            public int PageCount { get; init; }
            public string? Error { get; init; }
            public int Index { get; set; }
            public string FileName => System.IO.Path.GetFileName(Path);
            public string Details => Error ?? $"{PageCount} page{(PageCount == 1 ? "" : "s")}";
        }

        private readonly ObservableCollection<MergeEntry> _entries = new();

        public ToolParameters? Result { get; private set; }

        public PdfMergeDialog(IEnumerable<string> initialFiles)
        {
            InitializeComponent();
            FileList.ItemsSource = _entries;
            AddFiles(initialFiles);

            Loaded += (_, _) =>
            {
                Activate();
                UpdateState();
            };
        }

        private void AddFiles(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (!string.Equals(System.IO.Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                if (_entries.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))) continue;

                int pages = 0;
                string? error = null;
                try { pages = PdfToolsService.GetPageCount(path); }
                catch (Exception ex) { error = ex.Message.Length > 40 ? "can't be read" : ex.Message; }

                _entries.Add(new MergeEntry { Path = path, PageCount = pages, Error = error });
            }
            Renumber();
        }

        private void Renumber()
        {
            for (int i = 0; i < _entries.Count; i++) _entries[i].Index = i + 1;
            FileList.Items.Refresh();
            UpdateState();
        }

        private void UpdateState()
        {
            if (!IsLoaded) return;

            int idx = FileList.SelectedIndex;
            RemoveButton.IsEnabled = idx >= 0;
            UpButton.IsEnabled = idx > 0;
            DownButton.IsEnabled = idx >= 0 && idx < _entries.Count - 1;

            var broken = _entries.FirstOrDefault(e => e.Error != null);
            string? problem = broken != null ? $"{broken.FileName} can't be read (encrypted or damaged). Remove it to continue."
                : _entries.Count < 2 ? "Add at least two PDFs to merge."
                : null;

            bool ok = problem == null;
            var brush = (Brush)FindResource(ok ? "SuccessBrush" : "DangerBrush");
            SummaryText.Text = problem
                ?? $"1 PDF with {_entries.Sum(e => e.PageCount)} pages from {_entries.Count} files, saved as {System.IO.Path.GetFileNameWithoutExtension(_entries[0].Path)}_merged.pdf";
            SummaryText.Foreground = brush;
            SummaryIcon.Text = ok ? "\uE73E" : "\uE783";
            SummaryIcon.Foreground = brush;
            MergeButton.IsEnabled = ok;
        }

        private void Move(int delta)
        {
            int idx = FileList.SelectedIndex;
            int target = idx + delta;
            if (idx < 0 || target < 0 || target >= _entries.Count) return;

            _entries.Move(idx, target);
            Renumber();
            FileList.SelectedIndex = target;
        }

        private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateState();
        private void OnMoveUpClick(object sender, RoutedEventArgs e) => Move(-1);
        private void OnMoveDownClick(object sender, RoutedEventArgs e) => Move(+1);

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            int idx = FileList.SelectedIndex;
            if (idx < 0) return;
            _entries.RemoveAt(idx);
            Renumber();
            if (_entries.Count > 0) FileList.SelectedIndex = Math.Min(idx, _entries.Count - 1);
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Add PDFs to merge",
                Filter = "PDF documents (*.pdf)|*.pdf",
                Multiselect = true
            };
            if (_entries.Count > 0) dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_entries[^1].Path);

            if (dlg.ShowDialog(this) == true) AddFiles(dlg.FileNames);
        }

        private void OnFileListDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DroppedFiles.HasPaths(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnFileListDrop(object sender, DragEventArgs e)
        {
            var files = DroppedFiles.GetPaths(e.Data);
            if (files.Count > 0) AddFiles(files);
            e.Handled = true;
        }

        private void OnMergeClick(object sender, RoutedEventArgs e)
        {
            if (!MergeButton.IsEnabled) return;
            Result = new ToolParameters { MergeInputs = _entries.Select(x => x.Path).ToList() };
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
