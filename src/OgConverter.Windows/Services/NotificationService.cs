using System.Windows;
using OgConverter.Models;
using OgConverter.Views;

namespace OgConverter.Services
{
    public class NotificationService
    {
        /// <summary>Shows the result. Pass the progress toast of the same job to turn it into the result toast.</summary>
        public void ShowCompleted(ConversionJob job, ToastNotificationWindow? progressToast = null)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var toast = progressToast is { IsClosed: false } ? progressToast : new ToastNotificationWindow();
                toast.ShowJob(job);
            });
        }

        /// <summary>
        /// A toast with a progress bar for long-running work; update it with ReportProgress (or ReportBatch).
        /// With <paramref name="onCancel"/>, it has a Cancel button.
        /// </summary>
        public ToastNotificationWindow? ShowProgress(string title, string sourcePath, System.Action? onCancel = null)
        {
            ToastNotificationWindow? toast = null;
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                toast = new ToastNotificationWindow();
                toast.ShowProgress(title, sourcePath, onCancel);
            });
            return toast;
        }
    }
}
