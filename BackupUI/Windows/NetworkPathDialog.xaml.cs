using System;
using System.IO;
using System.Windows;

namespace SecureServerBackup.Windows
{
    public partial class NetworkPathDialog : Window
    {
        public string NetworkPath { get; private set; } = string.Empty;

        public NetworkPathDialog()
        {
            InitializeComponent();
            txtNetworkPath.Focus();
            txtNetworkPath.SelectAll();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            var path = txtNetworkPath.Text.Trim();

            // Validate UNC path format
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("Please enter a network path.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!path.StartsWith("\\\\"))
            {
                MessageBox.Show("Network path must start with \\\\ (UNC format).\n\nExample: \\\\server\\share",
                    "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check if path is accessible
            try
            {
                if (!Directory.Exists(path))
                {
                    var result = MessageBox.Show(
                        $"Cannot access network path:\n{path}\n\nThe path may not exist or you may not have permissions.\n\nAdd anyway?",
                        "Network Path Not Accessible",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                        return;
                }
            }
            catch (Exception ex)
            {
                var result = MessageBox.Show(
                    $"Error checking network path:\n{ex.Message}\n\nAdd anyway?",
                    "Network Path Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            NetworkPath = path;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
