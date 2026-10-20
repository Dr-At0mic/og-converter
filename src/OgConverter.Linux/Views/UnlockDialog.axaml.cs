using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OgConverter.Models;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>Asks for the current password of a protected PDF, Office file, .ogx or zip, and checks it before closing.</summary>
    public partial class UnlockDialog : DialogWindow
    {
        private readonly string _path = string.Empty;
        private readonly ProtectionService.LockKind _kind;

        public ToolParameters? Result { get; private set; }

        /// <summary>For the XAML previewer.</summary>
        public UnlockDialog() => InitializeComponent();

        public UnlockDialog(string path)
        {
            InitializeComponent();
            _path = path;
            _kind = ProtectionService.DetectLock(path);

            FileInfoText.Text = Path.GetFileName(path);
            ToolTip.SetTip(FileInfoText, path);
            (HeaderTitle.Text, ConfirmButton.Content) = _kind switch
            {
                ProtectionService.LockKind.Vault => ("Decrypt file", "Decrypt"),
                ProtectionService.LockKind.Zip => ("Extract protected archive", "Extract"),
                _ => ("Remove the password", "Unlock")
            };

            Opened += (_, _) =>
            {
                PasswordInput.Focus();
                Validate();
            };
        }

        private string Password => PasswordInput.Text ?? string.Empty;

        private void OnInputChanged(object? sender, TextChangedEventArgs e) => Validate();

        private void OnShowPasswordChanged(object? sender, RoutedEventArgs e)
        {
            PasswordInput.RevealPassword = ShowPasswordCheck.IsChecked == true;
            PasswordInput.Focus();
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
            SetStatusLine(StatusIcon, StatusText, Password.Length == 0 ? "Enter the password that currently opens this file." : hint, ok: null);
        }

        private void OnConfirmClick(object? sender, RoutedEventArgs e)
        {
            if (!ConfirmButton.IsEnabled) return;

            // Check here so a typo can be fixed without starting over (Office files are checked by the job itself)
            SetBusy(true);
            bool correct;
            try { correct = ProtectionService.CheckPassword(_path, Password); }
            finally { SetBusy(false); }

            if (!correct)
            {
                SetStatusLine(StatusIcon, StatusText, "That password is incorrect.", ok: false);
                PasswordInput.Focus();
                PasswordInput.SelectAll();
                return;
            }

            Result = new ToolParameters { Password = Password };
            Finish(true);
        }
    }
}
