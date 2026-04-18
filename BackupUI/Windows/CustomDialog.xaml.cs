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

            // Ensure dialog stays on top and follows parent window behavior
            this.Topmost = true;
            this.ShowInTaskbar = false;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Handle parent window state changes
            this.Loaded += CustomDialog_Loaded;
        }

        private void CustomDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // Subscribe to owner window state changes if owner is set
            if (this.Owner != null)
            {
                this.Owner.StateChanged += Owner_StateChanged;
                this.Owner.Activated += Owner_Activated;

                // Ensure we're positioned correctly relative to owner
                this.Left = this.Owner.Left + (this.Owner.Width - this.Width) / 2;
                this.Top = this.Owner.Top + (this.Owner.Height - this.Height) / 2;
            }
        }

        private void Owner_StateChanged(object? sender, EventArgs e)
        {
            if (this.Owner != null)
            {
                // When owner is minimized, hide this dialog
                if (this.Owner.WindowState == WindowState.Minimized)
                {
                    this.Hide();
                }
                // When owner is restored, show this dialog again on top
                else if (this.Owner.WindowState == WindowState.Normal || this.Owner.WindowState == WindowState.Maximized)
                {
                    if (!this.IsVisible)
                    {
                        this.Show();
                    }
                    this.Activate();
                    this.Topmost = true;
                }
            }
        }

        private void Owner_Activated(object? sender, EventArgs e)
        {
            // When owner window is activated, ensure dialog is on top
            if (this.IsVisible)
            {
                this.Activate();
                this.Topmost = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up event subscriptions
            if (this.Owner != null)
            {
                this.Owner.StateChanged -= Owner_StateChanged;
                this.Owner.Activated -= Owner_Activated;
            }
            base.OnClosed(e);
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
