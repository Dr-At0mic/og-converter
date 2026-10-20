using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// First-run / post-install prompt for the free helpers that aren't on this computer: FFmpeg, and LibreOffice when it's
    /// missing. Installing needs root, so it shows the package-manager command for what the user ticks and checks again
    /// once they've run it.
    /// </summary>
    public partial class ExtrasDialog : DialogWindow
    {
        private readonly ConversionEngine? _engine;

        /// <summary>For the XAML previewer.</summary>
        public ExtrasDialog() => InitializeComponent();

        public ExtrasDialog(bool offerFFmpeg, bool offerLibreOffice)
        {
            InitializeComponent();
            _engine = new ConversionEngine();

            FFmpegRow.IsVisible = offerFFmpeg;
            LibreOfficeRow.IsVisible = offerLibreOffice;
            UpdateCommand();
        }

        private IEnumerable<EngineTool> Picked
        {
            get
            {
                if (FFmpegRow.IsVisible && FFmpegCheck.IsChecked == true) yield return EngineTool.FFmpeg;
                if (LibreOfficeRow.IsVisible && LibreOfficeCheck.IsChecked == true) yield return EngineTool.LibreOffice;
            }
        }

        private void OnPickChanged(object? sender, RoutedEventArgs e) => UpdateCommand();

        private void UpdateCommand()
        {
            string? command = PackageHints.InstallCommand(Picked);
            bool any = command != null;
            CommandPanel.IsVisible = CommandLabel.IsVisible = any;
            CommandText.Text = command ?? string.Empty;
            CopyButton.Content = "Copy";
            if (!any)
            {
                FooterNote.Text = FFmpegCheck.IsChecked == true || LibreOfficeCheck.IsChecked == true
                    ? "Install them with your distribution's software center or package manager, then click Check again."
                    : "Nothing selected.";
            }
        }

        private async void OnCopyClick(object? sender, RoutedEventArgs e)
        {
            await DesktopActions.CopyTextAsync(this, CommandText.Text ?? string.Empty);
            CopyButton.Content = "Copied";
        }

        /// <summary>Looks for the helpers again; closes when everything ticked is there.</summary>
        private void OnConfirmClick(object? sender, RoutedEventArgs e)
        {
            if (_engine == null) return;
            _engine.RefreshFFmpeg();
            DocumentEngine.RefreshLibreOffice();

            bool allOk = true;
            if (FFmpegRow.IsVisible) allOk &= Report(FFmpegStatus, FFmpegCheck, _engine.IsFFmpegAvailable);
            if (LibreOfficeRow.IsVisible) allOk &= Report(LibreOfficeStatus, LibreOfficeCheck, DocumentEngine.LibreOfficePath != null);

            if (allOk)
            {
                Finish(true);
                return;
            }
            FooterNote.Text = "Not found yet. Run the command in a terminal, then check again. You can also do this later from Settings › Engine.";
        }

        /// <summary>Shows found / not found under a ticked helper. True when it's there or wasn't asked for.</summary>
        private static bool Report(TextBlock status, CheckBox check, bool found)
        {
            if (check.IsChecked != true && !found) return true;
            status.IsVisible = true;
            status.Text = found ? "✓ Installed and ready" : "Not found yet";
            status.Foreground = Brush(found ? "SuccessBrush" : "WarningBrush");
            if (found) check.IsEnabled = false;
            return found;
        }
    }
}
