using System;
using System.Windows;

namespace BackupUI
{
    /// <summary>
    /// Service for showing custom themed dialogs throughout the application
    /// Replaces MessageBox with turquoise-themed dialogs
    /// </summary>
    public static class CustomDialogService
    {
        /// <summary>
        /// Shows an information dialog
        /// </summary>
        public static void ShowInfo(string message, string title = "Information")
        {
            Show(message, title, DialogButtons.OK, DialogIcon.Information);
        }

        /// <summary>
        /// Shows a success dialog
        /// </summary>
        public static void ShowSuccess(string message, string title = "Success")
        {
            Show(message, title, DialogButtons.OK, DialogIcon.Success);
        }

        /// <summary>
        /// Shows a warning dialog
        /// </summary>
        public static void ShowWarning(string message, string title = "Warning")
        {
            Show(message, title, DialogButtons.OK, DialogIcon.Warning);
        }

        /// <summary>
        /// Shows an error dialog
        /// </summary>
        public static void ShowError(string message, string title = "Error")
        {
            Show(message, title, DialogButtons.OK, DialogIcon.Error);
        }

        /// <summary>
        /// Shows a question dialog with Yes/No buttons
        /// </summary>
        public static CustomDialogResult ShowQuestion(string message, string title = "Question")
        {
            return Show(message, title, DialogButtons.YesNo, DialogIcon.Question);
        }

        /// <summary>
        /// Shows a confirmation dialog with Yes/No/Cancel buttons
        /// </summary>
        public static CustomDialogResult ShowConfirmation(string message, string title = "Confirm")
        {
            return Show(message, title, DialogButtons.YesNoCancel, DialogIcon.Question);
        }

        /// <summary>
        /// Shows a dialog with OK/Cancel buttons
        /// </summary>
        public static CustomDialogResult ShowOKCancel(string message, string title = "Confirm", DialogIcon icon = DialogIcon.Question)
        {
            return Show(message, title, DialogButtons.OKCancel, icon);
        }

        /// <summary>
        /// Shows a custom dialog with specified parameters
        /// </summary>
        public static CustomDialogResult Show(string message, string title, DialogButtons buttons, DialogIcon icon)
        {
            try
            {
                var dialog = new CustomDialog();
                
                // Set owner to main window if available
                if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsLoaded)
                {
                    dialog.Owner = Application.Current.MainWindow;
                }

                dialog.Configure(message, title, buttons, icon);

                // Show dialog and return result
                dialog.ShowDialog();
                return dialog.Result;
            }
            catch (Exception ex)
            {
                // Fallback to MessageBox if custom dialog fails
                System.Diagnostics.Debug.WriteLine($"CustomDialog failed: {ex.Message}");
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
                return CustomDialogResult.OK;
            }
        }

        /// <summary>
        /// Shows dialog with owner window specified
        /// </summary>
        public static CustomDialogResult Show(Window owner, string message, string title, DialogButtons buttons, DialogIcon icon)
        {
            try
            {
                var dialog = new CustomDialog
                {
                    Owner = owner
                };

                dialog.Configure(message, title, buttons, icon);

                // Show dialog and return result
                dialog.ShowDialog();
                return dialog.Result;
            }
            catch (Exception ex)
            {
                // Fallback to MessageBox if custom dialog fails
                System.Diagnostics.Debug.WriteLine($"CustomDialog failed: {ex.Message}");
                MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
                return CustomDialogResult.OK;
            }
        }

        /// <summary>
        /// Converts MessageBoxResult to CustomDialogResult for compatibility
        /// </summary>
        public static CustomDialogResult FromMessageBoxResult(MessageBoxResult result)
        {
            return result switch
            {
                MessageBoxResult.OK => CustomDialogResult.OK,
                MessageBoxResult.Cancel => CustomDialogResult.Cancel,
                MessageBoxResult.Yes => CustomDialogResult.Yes,
                MessageBoxResult.No => CustomDialogResult.No,
                _ => CustomDialogResult.None
            };
        }

        /// <summary>
        /// Converts CustomDialogResult to MessageBoxResult for compatibility
        /// </summary>
        public static MessageBoxResult ToMessageBoxResult(CustomDialogResult result)
        {
            return result switch
            {
                CustomDialogResult.OK => MessageBoxResult.OK,
                CustomDialogResult.Cancel => MessageBoxResult.Cancel,
                CustomDialogResult.Yes => MessageBoxResult.Yes,
                CustomDialogResult.No => MessageBoxResult.No,
                _ => MessageBoxResult.None
            };
        }
    }
}
