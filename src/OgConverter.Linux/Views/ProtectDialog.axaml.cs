using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// Protect any file or folder: pick a method (only the ones this file supports are offered), set a password,
    /// and tune the encryption for that method.
    /// </summary>
    public partial class ProtectDialog : DialogWindow
    {
        private const int MinLength = 4;

        private readonly string _path = string.Empty;
        private readonly string _ext = string.Empty;
        private readonly Dictionary<ProtectionMethod, RadioButton> _methodCards = new();

        public ToolParameters? Result { get; private set; }

        /// <summary>For the XAML previewer.</summary>
        public ProtectDialog() => InitializeComponent();

        public ProtectDialog(string path, ProtectionMethod? preferred = null)
        {
            InitializeComponent();
            _path = path;
            _ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

            bool folder = Directory.Exists(path);
            string name = Path.GetFileName(path.TrimEnd('\\', '/'));
            FileInfoText.Text = name;
            ToolTip.SetTip(FileInfoText, path);

            var methods = ProtectionService.AvailableMethods(path);
            for (int i = 0; i < methods.Count; i++)
            {
                var card = CreateMethodCard(methods[i], recommended: i == 0, folder);
                _methodCards[methods[i]] = card;
                MethodList.Children.Add(card);
            }
            (preferred is { } p && _methodCards.TryGetValue(p, out var chosen) ? chosen : _methodCards[methods[0]]).IsChecked = true;

            ChaChaItem.IsEnabled = FileVault.IsChaChaSupported;
            if (!FileVault.IsChaChaSupported) ChaChaItem.Content = "ChaCha20-Poly1305 (not available on this system)";

            VaultNote.Text = $"Creates {name}.ogx. Only OG Converter can open it: drag it in and choose Unlock.";
            OfficeCipherText.Text = _ext is "doc" or "xls" or "ppt"
                ? "Uses Office's legacy RC4 encryption (the only kind this old format supports). Convert to the modern format first for AES-256."
                : "Uses Office's own encryption: AES-256 with SHA-512 (Agile Encryption). Opens in Microsoft Office and LibreOffice with the password (Google Docs can't open password-protected files).";

            Opened += (_, _) =>
            {
                // Use the available screen height, minus room for the header and footer
                if (Screens.ScreenFromWindow(this) is { } screen) BodyScroller.MaxHeight = Math.Max(360, screen.WorkingArea.Height / screen.Scaling - 220);
                PasswordInput.Focus();
                Validate();
            };
        }

        private RadioButton CreateMethodCard(ProtectionMethod method, bool recommended, bool folder)
        {
            (string title, string description) = method switch
            {
                ProtectionMethod.PdfPassword => ("PDF password", "Standard PDF encryption. Opens in any PDF reader once the password is entered."),
                ProtectionMethod.OfficePassword => ("Office password", "Office's built-in \"Encrypt with password\". Opens in Word, Excel, PowerPoint or LibreOffice."),
                ProtectionMethod.EncryptedFile => ("Encrypted file (.ogx)", "Strongest option for any file: modern authenticated encryption. Opens with OG Converter."),
                _ => ("Password-protected archive", folder
                    ? "Packs the folder into a .zip, .7z or .rar that asks for the password when extracted."
                    : "A .zip, .7z or .rar that asks for the password when extracted. Easy to share.")
            };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
            if (recommended)
            {
                var tag = new TextBlock { Text = "Recommended", FontSize = 10.5, FontWeight = FontWeight.SemiBold };
                tag.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("SuccessBrush"));
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 0, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = tag
                };
                badge.Bind(Border.BackgroundProperty, this.GetResourceObservable("SuccessSubtleBrush"));
                titleRow.Children.Add(badge);
            }

            var content = new StackPanel();
            content.Children.Add(titleRow);
            content.Children.Add(new TextBlock { Text = description, Classes = { "Caption" }, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });

            var card = new RadioButton { GroupName = "Method", Content = content, Tag = method, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
            if (this.TryFindResource("ModeCard", out object? theme) && theme is ControlTheme modeCard) card.Theme = modeCard;
            card.IsCheckedChanged += OnInputChanged;
            return card;
        }

        private ProtectionMethod SelectedMethod
        {
            get
            {
                foreach (var (method, card) in _methodCards)
                {
                    if (card.IsChecked == true) return method;
                }
                return ProtectionMethod.EncryptedFile;
            }
        }

        private string Password => PasswordInput.Text ?? string.Empty;
        private string Confirmation => ConfirmInput.Text ?? string.Empty;

        private void OnInputChanged(object? sender, RoutedEventArgs e) => Validate();

        private void OnTextChanged(object? sender, TextChangedEventArgs e) => Validate();

        private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => Validate();

        private void OnShowPasswordChanged(object? sender, RoutedEventArgs e)
        {
            bool show = ShowPasswordCheck.IsChecked == true;
            PasswordInput.RevealPassword = ConfirmInput.RevealPassword = show;
            Validate();
        }

        private async void OnGenerateClick(object? sender, RoutedEventArgs e)
        {
            string generated = ProtectionService.GeneratePassword();
            ShowPasswordCheck.IsChecked = true;
            PasswordInput.Text = ConfirmInput.Text = generated;
            await DesktopActions.CopyTextAsync(this, generated);
            Validate();
            SetStatus("Strong password generated and copied to the clipboard. Save it in your password manager: it can't be recovered.", ok: true);
        }

        private void OnToolInstalled(object? sender, EventArgs e) => Validate();

        private void Validate()
        {
            if (!IsLoaded) return;

            var method = SelectedMethod;
            PdfOptions.IsVisible = method == ProtectionMethod.PdfPassword;
            OfficeOptions.IsVisible = method == ProtectionMethod.OfficePassword;
            VaultOptions.IsVisible = method == ProtectionMethod.EncryptedFile;
            ZipOptions.IsVisible = method == ProtectionMethod.PasswordArchive;

            bool restricted = AllowPrintCheck.IsChecked != true || AllowCopyCheck.IsChecked != true || AllowEditCheck.IsChecked != true || AllowCommentCheck.IsChecked != true;
            PermissionsPasswordPanel.IsVisible = restricted;

            string format = ArchiveFormat;
            ZipCipherPanel.IsVisible = format == "zip";
            EncryptNamesCheck.IsVisible = format != "zip";
            ZipNote.Text = format switch
            {
                "7z" => "Opens in 7-Zip, PeaZip, File Roller, Ark, WinRAR and macOS archive apps. With file names encrypted, even the list of contents needs the password.",
                "rar" => "Opens in 7-Zip, WinRAR and most archive apps. Creating RAR needs the rar program (shareware); opening it doesn't.",
                _ when ZipCipherCombo.SelectedIndex == 1 => "ZipCrypto opens in every archive app, but it is easy to crack. Use it only for convenience, not secrets.",
                _ => "AES-256 zips open in 7-Zip, File Roller, Ark and most archive tools. File names stay readable (ZIP can't hide them); use 7z to hide them too."
            };

            // 7z with a password needs 7-Zip; RAR needs rar
            var missing = method == ProtectionMethod.PasswordArchive
                ? ArchiveWriter.MissingTool(ArchiveWriter.Find(format), new ArchiveOptions { Format = format, Password = "x" })
                : null;
            ToolMissing.Show(missing, missing is { } tool
                ? tool == Dependency.WinRar
                    ? "Creating RAR archives needs the rar program (shareware, free to try), which isn't installed."
                    : $".{format} passwords need {PackageHints.DisplayName(tool)} (free), which isn't installed."
                : null);

            UpdateStrength(Password);

            string pw = Password;
            string? problem = null;
            string? warning = null;

            if (pw.Length == 0) problem = "";
            else if (pw.Length < MinLength) problem = $"Use at least {MinLength} characters.";
            else if (Confirmation != pw) problem = Confirmation.Length == 0 ? "" : "The passwords don't match.";
            else if (method == ProtectionMethod.PdfPassword && restricted && PermissionsPasswordInput.Text == pw)
                problem = "The permissions password must differ from the open password, or anyone could lift the restrictions.";
            else if (method == ProtectionMethod.PdfPassword && PdfCipherCombo.SelectedIndex == 2)
                warning = "RC4 is broken and only for very old readers. Prefer AES-256.";
            else if (missing != null)
                problem = $"Install {PackageHints.DisplayName(missing.Value)} to create .{format} archives, or choose another format.";
            else if (method == ProtectionMethod.PasswordArchive && format == "zip" && ZipCipherCombo.SelectedIndex == 1)
                warning = "ZipCrypto can be cracked quickly. Prefer AES-256 for anything sensitive.";
            else if (ProtectionService.EstimateStrength(pw) == 0)
                warning = "This password is weak. Longer is stronger: try a passphrase of several words.";

            ConfirmButton.IsEnabled = problem == null;
            if (problem == "")
                SetStatus(pw.Length == 0 ? "A new protected copy is created; the original file is not changed. The password can't be recovered if forgotten."
                                         : "Type the password again to confirm it.", ok: null);
            else if (problem != null)
                SetStatus(problem, ok: false);
            else if (warning != null)
                SetStatus(warning, ok: false, isWarning: true);
            else
                SetStatus(SummaryFor(method), ok: true);
        }

        private string SummaryFor(ProtectionMethod method) => method switch
        {
            ProtectionMethod.PdfPassword => $"Saves {Path.GetFileNameWithoutExtension(_path)}_protected.pdf with {((PdfCipherCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "AES-256").Split(" ·")[0]} encryption.",
            ProtectionMethod.OfficePassword => $"Saves {Path.GetFileNameWithoutExtension(_path)}_protected.{_ext} with an open password{((ModifyPasswordInput.Text ?? "").Length > 0 ? " and an edit password" : "")}.",
            ProtectionMethod.EncryptedFile => $"Encrypts with {(VaultCipherCombo.SelectedIndex == 1 ? "ChaCha20-Poly1305" : "AES-256-GCM")}, key derived with {(KdfCombo.SelectedIndex == 1 ? "PBKDF2-SHA512" : "Argon2id")}.",
            _ => ArchiveFormat == "zip"
                ? $"Creates a password-protected .zip using {(ZipCipherCombo.SelectedIndex == 1 ? "ZipCrypto" : "AES-256")}."
                : $"Creates a password-protected .{ArchiveFormat} using AES-256{(EncryptNamesCheck.IsChecked == true ? ", file names hidden" : "")}."
        };

        private string ArchiveFormat => (ArchiveFormatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "zip";

        /// <summary>Store, the format's default, or its maximum level.</summary>
        private int ArchiveLevel => ((ArchiveLevelCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "store" => 0,
            "max" => ArchiveWriter.Find(ArchiveFormat).MaxLevel,
            _ => -1
        };

        private void UpdateStrength(string password)
        {
            int level = ProtectionService.EstimateStrength(password);
            string[] labels = { "Weak", "Fair", "Strong", "Very strong" };
            string[] colors = { "DangerBrush", "WarningBrush", "SuccessBrush", "SuccessBrush" };
            var bars = new[] { Strength0, Strength1, Strength2, Strength3 };
            for (int i = 0; i < bars.Length; i++)
            {
                bars[i].Background = Brush(level >= 0 && i <= level ? colors[level] : "BorderStrongBrush");
            }
            StrengthText.Text = level >= 0 ? labels[level] : "";
            StrengthText.Foreground = Brush(level >= 0 ? colors[level] : "TextTertiaryBrush");
        }

        private void SetStatus(string text, bool? ok, bool isWarning = false)
        {
            SetStatusLine(StatusIcon, StatusText, text, ok);
            if (!isWarning) return;
            var brush = Brush("WarningBrush");
            StatusText.Foreground = StatusIcon.Foreground = brush;
            StatusIcon.Text = Icons.Icons.Glyph("");
        }

        private void OnConfirmClick(object? sender, RoutedEventArgs e)
        {
            if (!ConfirmButton.IsEnabled) return;

            Result = new ToolParameters
            {
                Protection = new ProtectionOptions
                {
                    Method = SelectedMethod,
                    Password = Password,
                    PdfCipher = PdfCipherCombo.SelectedIndex switch { 1 => PdfCipher.Aes128, 2 => PdfCipher.Rc4_128, _ => PdfCipher.Aes256 },
                    AllowPrinting = AllowPrintCheck.IsChecked == true,
                    AllowCopying = AllowCopyCheck.IsChecked == true,
                    AllowEditing = AllowEditCheck.IsChecked == true,
                    AllowComments = AllowCommentCheck.IsChecked == true,
                    PermissionsPassword = PermissionsPasswordInput.Text ?? string.Empty,
                    ModifyPassword = ModifyPasswordInput.Text ?? string.Empty,
                    FileCipher = VaultCipherCombo.SelectedIndex == 1 ? FileCipher.ChaCha20Poly1305 : FileCipher.Aes256Gcm,
                    KeyDerivation = KdfCombo.SelectedIndex == 1 ? KeyDerivation.Pbkdf2Sha512 : KeyDerivation.Argon2id,
                    ZipCipher = ZipCipherCombo.SelectedIndex == 1 ? ZipCipher.ZipCrypto : ZipCipher.Aes256,
                    ArchiveFormat = ArchiveFormat,
                    EncryptNames = EncryptNamesCheck.IsChecked == true,
                    ArchiveLevel = ArchiveLevel
                }
            };
            Finish(true);
        }
    }
}
