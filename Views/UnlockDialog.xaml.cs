using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>Asks for the current password of a protected PDF, Office file, .ogx or zip, and checks it before closing.</summary>
    public partial class UnlockDialog : Window
    {
        private readonly string _path;
        private readonly ProtectionService.LockKind _kind;
        private bool _syncing;

        public ToolParameters? Result { get; private set; }

        public UnlockDialog(string path)
        {
            InitializeComponent();
            _path = path;
            _kind = ProtectionService.DetectLock(path);

            FileInfoText.Text = Path.GetFileName(path);
            FileInfoText.ToolTip = path;
            (HeaderTitle.Text, ConfirmButton.Content) = _kind switch
            {
                ProtectionService.LockKind.Vault => ("Decrypt file", "Decrypt"),
                ProtectionService.LockKind.Zip => ("Extract protected archive", "Extract"),
                _ => ("Remove the password", "Unlock")
            };

            Loaded += (_, _) =>
            {
                Activate();
                PasswordInput.Focus();
                Validate();
            };
        }

        private bool Showing => ShowPasswordCheck.IsChecked == true;
        private string Password => Showing ? PasswordVisible.Text : PasswordInput.Password;

        private void OnInputChanged(object sender, RoutedEventArgs e)
        {
            if (!_syncing) Validate();
        }

        private void OnShowPasswordChanged(object sender, RoutedEventArgs e)
        {
            _syncing = true;
            if (Showing) PasswordVisible.Text = PasswordInput.Password;
            else PasswordInput.Password = PasswordVisible.Text;
            _syncing = false;

            PasswordInput.Visibility = Showing ? Visibility.Collapsed : Visibility.Visible;
            PasswordVisible.Visibility = Showing ? Visibility.Visible : Visibility.Collapsed;
            (Showing ? (UIElement)PasswordVisible : PasswordInput).Focus();
        }

        private void Validate()
        {
            if (!IsLoaded) return;
            ConfirmButton.IsEnabled = Password.Length > 0;

            string hint = _kind switch
            {
                ProtectionService.LockKind.Vault => $"Encrypted with {FileVault.Describe(_path)}. The original file is restored next to it.",
                ProtectionService.LockKind.Zip => "The contents are extracted into a new folder next to the archive.",
                _ => "Saves an unlocked copy next to the original. The original keeps its password."
            };
            SetStatus(Password.Length == 0 ? "Enter the password that currently opens this file." : hint, ok: null);
        }

        private void SetStatus(string text, bool? ok)
        {
            var brush = (Brush)FindResource(ok switch { true => "SuccessBrush", false => "DangerBrush", _ => "TextSecondaryBrush" });
            StatusText.Text = text;
            StatusText.Foreground = brush;
            StatusIcon.Text = ok switch { true => "\uE73E", false => "\uE783", _ => "\uE946" };
            StatusIcon.Foreground = brush;
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            if (!ConfirmButton.IsEnabled) return;

            // Check here so a typo can be fixed without starting over (Office files are checked by the job itself)
            Mouse.OverrideCursor = Cursors.Wait;
            bool correct;
            try { correct = ProtectionService.CheckPassword(_path, Password); }
            finally { Mouse.OverrideCursor = null; }

            if (!correct)
            {
                SetStatus("That password is incorrect.", ok: false);
                (Showing ? (UIElement)PasswordVisible : PasswordInput).Focus();
                PasswordInput.SelectAll();
                return;
            }

            Result = new ToolParameters { Password = Password };
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
