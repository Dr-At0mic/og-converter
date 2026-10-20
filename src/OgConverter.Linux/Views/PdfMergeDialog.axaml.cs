using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>One PDF in the merge list.</summary>
    public sealed record MergeEntry(string Path, int PageCount, string? Error, int Index)
    {
        public string FileName => System.IO.Path.GetFileName(Path);
        public string Details => Error ?? $"{PageCount} page{(PageCount == 1 ? "" : "s")}";
    }

    /// <summary>Lets the user build and order the list of PDFs for the MERGE PDFs tool.</summary>
    public partial class PdfMergeDialog : DialogWindow
    {
        private readonly ObservableCollection<MergeEntry> _entries = new();

        public ToolParameters? Result { get; private set; }

        /// <summary>For the XAML previewer.</summary>
        public PdfMergeDialog() : this(Array.Empty<string>()) { }

        public PdfMergeDialog(IEnumerable<string> initialFiles)
        {
            InitializeComponent();
            FileList.ItemsSource = _entries;
            FileList.AddHandler(DragDrop.DragOverEvent, OnFileListDragOver);
            FileList.AddHandler(DragDrop.DropEvent, OnFileListDrop);
            AddFiles(initialFiles);

            Opened += (_, _) => UpdateState();
        }

        private void AddFiles(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (!string.Equals(System.IO.Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                if (_entries.Any(e => e.Path == path)) continue;

                int pages = 0;
                string? error = null;
                try { pages = PdfToolsService.GetPageCount(path); }
                catch (Exception ex) { error = ex.Message.Length > 40 ? "can't be read" : ex.Message; }

                _entries.Add(new MergeEntry(path, pages, error, _entries.Count + 1));
            }
            UpdateState();
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

            int idx = FileList.SelectedIndex;
            RemoveButton.IsEnabled = idx >= 0;
            UpButton.IsEnabled = idx > 0;
            DownButton.IsEnabled = idx >= 0 && idx < _entries.Count - 1;

            var broken = _entries.FirstOrDefault(e => e.Error != null);
            string? problem = broken != null ? $"{broken.FileName} can't be read (encrypted or damaged). Remove it to continue."
                : _entries.Count < 2 ? "Add at least two PDFs to merge."
                : null;

            bool ok = problem == null;
            SetStatusLine(SummaryIcon, SummaryText, problem
                ?? $"1 PDF with {_entries.Sum(e => e.PageCount)} pages from {_entries.Count} files, saved as {System.IO.Path.GetFileNameWithoutExtension(_entries[0].Path)}_merged.pdf", ok);
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
            UpdateState();
        }

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

        private async void OnAddClick(object? sender, RoutedEventArgs e)
        {
            var options = new FilePickerOpenOptions
            {
                Title = "Add PDFs to merge",
                AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType("PDF documents") { Patterns = new[] { "*.pdf", "*.PDF" }, MimeTypes = new[] { "application/pdf" } } }
            };
            if (_entries.Count > 0 && System.IO.Path.GetDirectoryName(_entries[^1].Path) is { } folder)
            {
                options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(folder);
            }

            var files = await StorageProvider.OpenFilePickerAsync(options);
            AddFiles(files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!));
        }

        private void OnFileListDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = MainWindow.DroppedPaths(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnFileListDrop(object? sender, DragEventArgs e)
        {
            var files = MainWindow.DroppedPaths(e);
            if (files.Count > 0) AddFiles(files);
            e.Handled = true;
        }

        private void OnMergeClick(object? sender, RoutedEventArgs e)
        {
            if (!MergeButton.IsEnabled) return;
            Result = new ToolParameters { MergeInputs = _entries.Select(x => x.Path).ToList() };
            Finish(true);
        }
    }
}
