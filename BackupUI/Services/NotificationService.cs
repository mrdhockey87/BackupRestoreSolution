using System;
using System.Windows;

namespace SecureServerBackup.Services
{
    public static class NotificationService
    {
        private static bool notificationsEnabled = true;

        public static void Initialize()
        {
            // Notification system initialized
        }

        public static void ShowBackupFailureNotification(string jobName, string message)
        {
            if (!notificationsEnabled)
                return;

            try
            {
                // Show tray balloon notification (fallback for now)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"Backup Failed!\n\nJob: {jobName}\n\n{message}\n\nCheck the Activity tab for details.",
                        "?? Backup Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification failed: {ex.Message}");
            }
        }

        public static void ShowValidationFailureNotification(string jobName, string backupPath)
        {
            if (!notificationsEnabled)
                return;

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var result = MessageBox.Show(
                        $"Backup Validation Failed!\n\nJob: {jobName}\n\nAuto-recovery initiated.\nFailed backup renamed and new full backup will be created.\n\nView Activity tab for details?",
                        "?? Backup Validation Failed",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        var mainWindow = Application.Current.MainWindow as MainWindow;
                        mainWindow?.ShowActivityTab();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification failed: {ex.Message}");
            }
        }

        public static void ShowBackupSuccessNotification(string jobName)
        {
            if (!notificationsEnabled)
                return;

            try
            {
                // Success notifications are less intrusive - just log
                System.Diagnostics.Debug.WriteLine($"Backup completed successfully: {jobName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification failed: {ex.Message}");
            }
        }

        public static void SetNotificationsEnabled(bool enabled)
        {
            notificationsEnabled = enabled;
        }
    }
}

