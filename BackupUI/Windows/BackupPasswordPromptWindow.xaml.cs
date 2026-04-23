using System.Windows;

namespace BackupUI.Windows
{
    public partial class BackupPasswordPromptWindow : Window
    {
        private bool _isUpdating;

        public string EnteredPassword { get; private set; } = string.Empty;

        public BackupPasswordPromptWindow(string backupName)
        {
            InitializeComponent();
            Title = $"Backup Password - {backupName}";
            pwdPassword.Focus();
        }

        public void SetError(string message)
        {
            txtError.Text = message;
            txtError.Visibility = Visibility.Visible;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string password = chkShowPassword.IsChecked == true ? txtPasswordVisible.Text : pwdPassword.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                SetError("Please enter the backup password.");
                return;
            }

            EnteredPassword = password;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowPassword_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdating)
            {
                return;
            }

            _isUpdating = true;
            if (chkShowPassword.IsChecked == true)
            {
                txtPasswordVisible.Text = pwdPassword.Password;
                txtPasswordVisible.Visibility = Visibility.Visible;
                pwdPassword.Visibility = Visibility.Collapsed;
                txtPasswordVisible.Focus();
                txtPasswordVisible.CaretIndex = txtPasswordVisible.Text.Length;
            }
            else
            {
                pwdPassword.Password = txtPasswordVisible.Text;
                pwdPassword.Visibility = Visibility.Visible;
                txtPasswordVisible.Visibility = Visibility.Collapsed;
                pwdPassword.Focus();
            }
            _isUpdating = false;
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdating)
            {
                return;
            }

            _isUpdating = true;
            txtPasswordVisible.Text = pwdPassword.Password;
            _isUpdating = false;
        }

        private void VisiblePassword_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isUpdating)
            {
                return;
            }

            _isUpdating = true;
            pwdPassword.Password = txtPasswordVisible.Text;
            _isUpdating = false;
        }
    }
}
