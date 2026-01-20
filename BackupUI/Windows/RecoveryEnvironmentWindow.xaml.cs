using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using BackupUI.Services;

namespace BackupUI.Windows
{
    public partial class RecoveryEnvironmentWindow : Window
    {
        public RecoveryEnvironmentWindow()
        {
            InitializeComponent();
            RefreshDrives();
        }

        private void RefreshDrives_Click(object sender, RoutedEventArgs e)
        {
            RefreshDrives();
        }

        private void RefreshDrives()
        {
            cmbUSBDrives.Items.Clear();
            txtDriveInfo.Text = "";

            try
            {
                // Find removable drives
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                    .ToList();

                if (drives.Any())
                {
                    foreach (var drive in drives)
                    {
                        var info = $"{drive.Name} - {drive.VolumeLabel} ({FormatBytes(drive.TotalSize)})";
                        cmbUSBDrives.Items.Add(info);
                    }

                    cmbUSBDrives.SelectedIndex = 0;
                }
                else
                {
                    txtDriveInfo.Text = "No USB drives detected. Please insert a USB drive.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to enumerate USB drives: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        private async void CreateRecovery_Click(object sender, RoutedEventArgs e)
        {
            if (cmbUSBDrives.SelectedItem == null)
            {
                MessageBox.Show("Please select a USB drive.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (chkFormatDrive.IsChecked == true)
            {
                var result = MessageBox.Show(
                    "WARNING: This will format the USB drive and all data will be lost!\n\nAre you sure you want to continue?",
                    "Confirm Format", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            var driveInfo = cmbUSBDrives.SelectedItem.ToString();
            var driveLetter = driveInfo!.Substring(0, 2);

            await CreateRecoveryEnvironment(driveLetter);
        }

        private async Task CreateRecoveryEnvironment(string driveLetter)
        {
            progressBar.Visibility = Visibility.Visible;
            txtProgress.Visibility = Visibility.Visible;

            try
            {
                BackupEngineInterop.ProgressCallback callback = (percentage, message) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = percentage;
                        txtProgress.Text = message;
                    });
                };

                await Task.Run(() =>
                {
                    var programPath = AppDomain.CurrentDomain.BaseDirectory;
                    int result = BackupEngineInterop.CreateRecoveryEnvironment(
                        driveLetter, programPath, callback);

                    if (result != 0)
                    {
                        var error = new StringBuilder(1024);
                        BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
                        throw new Exception($"Failed to create recovery environment: {error}");
                    }
                });

                MessageBox.Show(
                    "Recovery USB created successfully!\n\nYou can now use this USB drive to boot and restore a system.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create recovery environment: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                txtProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
