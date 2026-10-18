using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// Protect any file or folder: pick a method (only the ones this file supports are offered), set a password,
    /// and tune the encryption for that method.
    /// </summary>
    public partial class ProtectDialog : Window
    {
        private const int MinLength = 4;

        private readonly string _path;
        private readonly string _ext;
        private readonly Dictionary<ProtectionMethod, RadioButton> _methodCards = new();
        private bool _syncing;

        public ToolParameters? Result { get; private set; }

        public ProtectDialog(string path, ProtectionMethod? preferred = null)
        {
            InitializeComponent();
            _path = path;
            _ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

            bool folder = Directory.Exists(path);
            FileInfoText.Text = Path.GetFileName(path.TrimEnd('\\', '/'));
            FileInfoText.ToolTip = path;

            var methods = ProtectionService.AvailableMethods(path);
            for (int i = 0; i < methods.Count; i++)
            {
                var card = CreateMethodCard(methods[i], recommended: i == 0, folder);
                _methodCards[methods[i]] = card;
                MethodList.Children.Add(card);
            }
            (preferred is { } p && _methodCards.TryGetValue(p, out var chosen) ? chosen : _methodCards[methods[0]]).IsChecked = true;

            ChaChaItem.IsEnabled = FileVault.IsChaChaSupported;
            if (!FileVault.IsChaChaSupported) ChaChaItem.Content = "ChaCha20-Poly1305 (not available on this Windows version)";

            string name = Path.GetFileName(path.TrimEnd('\\', '/'));
            VaultNote.Text = $"Creates {name}.ogx. Only OG Converter can open it: drag it in and choose Unlock.";
            OfficeCipherText.Text = _ext is "doc" or "xls" or "ppt"
                ? "Uses Office's legacy RC4 encryption (the only kind this old format supports). Convert to the modern format first for AES-256."
                : "Uses Office's own encryption: AES-256 with SHA-512 (Agile Encryption). Opens in Microsoft Office and LibreOffice with the password (Google Docs can't open password-protected files).";

            Loaded += (_, _) =>
            {
                // Use the available screen height, minus room for the header and footer
                BodyScroller.MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 220);
                Activate();
                PasswordInput.Focus();
                Validate();
            };
        }

        private RadioButton CreateMethodCard(ProtectionMethod method, bool recommended, bool folder)
        {
            (string title, string description) = method switch
            {
                ProtectionMethod.PdfPassword => ("PDF password", "Standard PDF encryption. Opens in any PDF reader once the password is entered."),
                ProtectionMethod.OfficePassword => ("Office password", "Office's built-in \"Encrypt with password\". Opens in Word, Excel or PowerPoint."),
                ProtectionMethod.EncryptedFile => ("Encrypted file (.ogx)", "Strongest option for any file: modern authenticated encryption. Opens with OG Converter."),
                _ => ("Password-protected ZIP", folder
                    ? "Packs the folder into a .zip that asks for the password when extracted."
                    : "A .zip that asks for the password when extracted. Easy to share.")
            };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
            if (recommended)
            {
                titleRow.Children.Add(new Border
                {
                    Background = (Brush)FindResource("SuccessSubtleBrush"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 0, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "Recommended", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("SuccessBrush") }
                });
            }

            var content = new StackPanel();
            content.Children.Add(titleRow);
            content.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("Caption"), FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });

            var card = new RadioButton { Style = (Style)FindResource("MethodCard"), GroupName = "Method", Content = content, Tag = method };
            card.Checked += OnInputChanged;
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

        private bool Showing => ShowPasswordCheck.IsChecked == true;
        private string Password => Showing ? PasswordVisible.Text : PasswordInput.Password;
        private string Confirmation => Showing ? ConfirmVisible.Text : ConfirmInput.Password;

        private void OnInputChanged(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            Validate();
        }

        private void OnShowPasswordChanged(object sender, RoutedEventArgs e)
        {
            bool show = Showing;
            _syncing = true;
            if (show)
            {
                PasswordVisible.Text = PasswordInput.Password;
                ConfirmVisible.Text = ConfirmInput.Password;
            }
            else
            {
                PasswordInput.Password = PasswordVisible.Text;
                ConfirmInput.Password = ConfirmVisible.Text;
            }
            _syncing = false;

            PasswordInput.Visibility = ConfirmInput.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            PasswordVisible.Visibility = ConfirmVisible.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            Validate();
        }

        private void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            string generated = ProtectionService.GeneratePassword();
            ShowPasswordCheck.IsChecked = true;
            _syncing = true;
            PasswordVisible.Text = ConfirmVisible.Text = generated;
            PasswordInput.Password = ConfirmInput.Password = generated;
            _syncing = false;
            try { Clipboard.SetText(generated); } catch { }
            Validate();
            SetStatus("Strong password generated and copied to the clipboard. Save it in your password manager: it can't be recovered.", ok: true);
        }

        private void Validate()
        {
            if (!IsLoaded) return;

            var method = SelectedMethod;
            PdfOptions.Visibility = method == ProtectionMethod.PdfPassword ? Visibility.Visible : Visibility.Collapsed;
            OfficeOptions.Visibility = method == ProtectionMethod.OfficePassword ? Visibility.Visible : Visibility.Collapsed;
            VaultOptions.Visibility = method == ProtectionMethod.EncryptedFile ? Visibility.Visible : Visibility.Collapsed;
            ZipOptions.Visibility = method == ProtectionMethod.PasswordZip ? Visibility.Visible : Visibility.Collapsed;

            bool restricted = AllowPrintCheck.IsChecked != true || AllowCopyCheck.IsChecked != true || AllowEditCheck.IsChecked != true || AllowCommentCheck.IsChecked != true;
            PermissionsPasswordPanel.Visibility = restricted ? Visibility.Visible : Visibility.Collapsed;

            ZipNote.Text = ZipCipherCombo.SelectedIndex == 1
                ? "ZipCrypto opens in Windows Explorer, but it is easy to crack. Use it only for convenience, not secrets."
                : "AES-256 zips open in 7-Zip, WinRAR and most archive tools. Older Windows Explorer versions can't open them.";

            UpdateStrength(Password);

            string pw = Password;
            string? problem = null;
            string? warning = null;

            if (pw.Length == 0) problem = "";
            else if (pw.Length < MinLength) problem = $"Use at least {MinLength} characters.";
            else if (Confirmation != pw) problem = Confirmation.Length == 0 ? "" : "The passwords don't match.";
            else if (method == ProtectionMethod.PdfPassword && restricted && PermissionsPasswordInput.Password == pw)
                problem = "The permissions password must differ from the open password, or anyone could lift the restrictions.";
            else if (method == ProtectionMethod.PdfPassword && PdfCipherCombo.SelectedIndex == 2)
                warning = "RC4 is broken and only for very old readers. Prefer AES-256.";
            else if (method == ProtectionMethod.PasswordZip && ZipCipherCombo.SelectedIndex == 1)
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
            ProtectionMethod.PdfPassword => $"Saves {Path.GetFileNameWithoutExtension(_path)}_protected.pdf with {((ComboBoxItem)PdfCipherCombo.SelectedItem).Content.ToString()!.Split(" ·")[0]} encryption.",
            ProtectionMethod.OfficePassword => $"Saves {Path.GetFileNameWithoutExtension(_path)}_protected.{_ext} with an open password{(ModifyPasswordInput.Password.Length > 0 ? " and an edit password" : "")}.",
            ProtectionMethod.EncryptedFile => $"Encrypts with {(VaultCipherCombo.SelectedIndex == 1 ? "ChaCha20-Poly1305" : "AES-256-GCM")}, key derived with {(KdfCombo.SelectedIndex == 1 ? "PBKDF2-SHA512" : "Argon2id")}.",
            _ => $"Creates a password-protected .zip using {(ZipCipherCombo.SelectedIndex == 1 ? "ZipCrypto" : "AES-256")}."
        };

        private void UpdateStrength(string password)
        {
            int level = ProtectionService.EstimateStrength(password);
            string[] labels = { "Weak", "Fair", "Strong", "Very strong" };
            string[] colors = { "DangerBrush", "WarningBrush", "SuccessBrush", "SuccessBrush" };
            var bars = new[] { Strength0, Strength1, Strength2, Strength3 };
            for (int i = 0; i < bars.Length; i++)
            {
                bars[i].Background = (Brush)FindResource(level >= 0 && i <= level ? colors[level] : "BorderStrongBrush");
            }
            StrengthText.Text = level >= 0 ? labels[level] : "";
            StrengthText.Foreground = (Brush)FindResource(level >= 0 ? colors[level] : "TextTertiaryBrush");
        }

        private void SetStatus(string text, bool? ok, bool isWarning = false)
        {
            string key = isWarning ? "WarningBrush" : ok switch { true => "SuccessBrush", false => "DangerBrush", _ => "TextSecondaryBrush" };
            var brush = (Brush)FindResource(key);
            StatusText.Text = text;
            StatusText.Foreground = brush;
            StatusIcon.Text = isWarning ? "\uE7BA" : ok switch { true => "\uE73E", false => "\uE783", _ => "\uE946" };
            StatusIcon.Foreground = brush;
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
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
                    PermissionsPassword = PermissionsPasswordInput.Password,
                    ModifyPassword = ModifyPasswordInput.Password,
                    FileCipher = VaultCipherCombo.SelectedIndex == 1 ? FileCipher.ChaCha20Poly1305 : FileCipher.Aes256Gcm,
                    KeyDerivation = KdfCombo.SelectedIndex == 1 ? KeyDerivation.Pbkdf2Sha512 : KeyDerivation.Argon2id,
                    ZipCipher = ZipCipherCombo.SelectedIndex == 1 ? ZipCipher.ZipCrypto : ZipCipher.Aes256
                }
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
