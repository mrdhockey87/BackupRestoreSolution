using System;
using System.Windows;
using System.Windows.Media;

namespace BackupUI
{
    /// <summary>
    /// Custom themed dialog window to replace MessageBox
    /// </summary>
    public partial class CustomDialog : Window
    {
        public CustomDialogResult Result { get; private set; }

        public CustomDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Configure the dialog with message, title, buttons, and icon
        /// </summary>
        public void Configure(string message, string title, DialogButtons buttons, DialogIcon icon)
        {
            txtMessage.Text = message;
            txtTitle.Text = title;

            // Set icon and colors based on type
            ConfigureIcon(icon);

            // Configure buttons
            ConfigureButtons(buttons);
        }

        private void ConfigureIcon(DialogIcon icon)
        {
            switch (icon)
            {
                case DialogIcon.Information:
                    txtIcon.Text = "ℹ️";
                    txtIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 128)); // Navy blue
                    break;

                case DialogIcon.Warning:
                    txtIcon.Text = "⚠️";
                    txtIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)); // Dark orange
                    break;

                case DialogIcon.Error:
                    txtIcon.Text = "❌";
                    txtIcon.Foreground = new SolidColorBrush(Color.FromRgb(139, 0, 0)); // Dark red
                    break;

                case DialogIcon.Question:
                    txtIcon.Text = "❓";
                    txtIcon.Foreground = new SolidColorBrush(Color.FromRgb(32, 178, 170)); // Light sea green
                    break;

                case DialogIcon.Success:
                    txtIcon.Text = "✅";
                    txtIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 100, 0)); // Dark green
                    break;

                case DialogIcon.None:
                    txtIcon.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void ConfigureButtons(DialogButtons buttons)
        {
            switch (buttons)
            {
                case DialogButtons.OK:
                    btnPrimary.Content = "OK";
                    btnPrimary.Visibility = Visibility.Visible;
                    btnSecondary.Visibility = Visibility.Collapsed;
                    btnTertiary.Visibility = Visibility.Collapsed;
                    break;

                case DialogButtons.OKCancel:
                    btnPrimary.Content = "OK";
                    btnPrimary.Visibility = Visibility.Visible;
                    btnSecondary.Content = "Cancel";
                    btnSecondary.Visibility = Visibility.Visible;
                    btnTertiary.Visibility = Visibility.Collapsed;
                    break;

                case DialogButtons.YesNo:
                    btnPrimary.Content = "Yes";
                    btnPrimary.Visibility = Visibility.Visible;
                    btnSecondary.Content = "No";
                    btnSecondary.Visibility = Visibility.Visible;
                    btnTertiary.Visibility = Visibility.Collapsed;
                    break;

                case DialogButtons.YesNoCancel:
                    btnPrimary.Content = "Yes";
                    btnPrimary.Visibility = Visibility.Visible;
                    btnSecondary.Content = "Cancel";
                    btnSecondary.Visibility = Visibility.Visible;
                    btnTertiary.Content = "No";
                    btnTertiary.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void BtnPrimary_Click(object sender, RoutedEventArgs e)
        {
            Result = btnPrimary.Content.ToString() == "Yes" ? CustomDialogResult.Yes : CustomDialogResult.OK;
            base.DialogResult = true;
        }

        private void BtnSecondary_Click(object sender, RoutedEventArgs e)
        {
            Result = btnSecondary.Content.ToString() == "No" ? CustomDialogResult.No : CustomDialogResult.Cancel;
            base.DialogResult = false;
        }

        private void BtnTertiary_Click(object sender, RoutedEventArgs e)
        {
            Result = CustomDialogResult.No;
            base.DialogResult = false;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Result = CustomDialogResult.Cancel;
            base.DialogResult = false;
        }
    }

    /// <summary>
    /// Dialog button configurations
    /// </summary>
    public enum DialogButtons
    {
        OK,
        OKCancel,
        YesNo,
        YesNoCancel
    }

    /// <summary>
    /// Dialog icon types
    /// </summary>
    public enum DialogIcon
    {
        None,
        Information,
        Warning,
        Error,
        Question,
        Success
    }

    /// <summary>
    /// Dialog result values
    /// </summary>
    public enum CustomDialogResult
    {
        None,
        OK,
        Cancel,
        Yes,
        No
    }
}
