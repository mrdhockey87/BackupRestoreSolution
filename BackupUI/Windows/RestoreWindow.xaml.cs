using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BackupUI.Services;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace BackupUI.Windows
{
    public partial class RestoreWindow : Window
    {
        public RestoreWindow()
        {
            InitializeComponent();
        }

        private void BrowseBackup_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select Backup Source",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtBackupSource.Text = dialog.SelectedPath;
            }
        }

        private void LoadBackup_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtBackupSource.Text))
            {
                MessageBox.Show("Please select a backup source.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    var buffer = new StringBuilder(32768);
                    int result = BackupEngineInterop.ListBackupContents(
                        txtBackupSource.Text, buffer, buffer.Capacity);

                    if (result == 0)
                    {
                        var contents = buffer.ToString();
                        Dispatcher.Invoke(() =>
                        {
                            PopulateTreeView(contents);
                            txtBackupInfo.Text = $"Backup loaded from: {txtBackupSource.Text}";
                        });
                    }
                    else
                    {
                        var error = new StringBuilder(1024);
                        BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
                        Dispatcher.Invoke(() =>
                            MessageBox.Show($"Failed to load backup: {error}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error));
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                        MessageBox.Show($"Failed to load backup: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }

        private void PopulateTreeView(string contents)
        {
            treeBackupContents.Items.Clear();

            var lines = contents.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var root = new TreeViewItem { Header = "Backup Contents" };
            treeBackupContents.Items.Add(root);

            foreach (var line in lines)
            {
                var item = new TreeViewItem { Header = line.Trim() };
                root.Items.Add(item);
            }

            root.IsExpanded = true;
        }

        private void RestoreLocation_Changed(object sender, RoutedEventArgs e)
        {
            pnlCustomLocation.Visibility = chkRestoreToOriginal.IsChecked == true
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select Restore Destination",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtRestoreDestination.Text = dialog.SelectedPath;
            }
        }

        private void HyperV_CheckedChanged(object sender, RoutedEventArgs e)
        {
            pnlHyperV.Visibility = chkRestoreAsHyperV.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BrowseVMStorage_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select VM Storage Path",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtVMStorage.Text = dialog.SelectedPath;
            }
        }

        private async void StartRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            await ExecuteRestore();
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(txtBackupSource.Text))
            {
                MessageBox.Show("Please select a backup source.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (chkRestoreToOriginal.IsChecked == false &&
                string.IsNullOrWhiteSpace(txtRestoreDestination.Text))
            {
                MessageBox.Show("Please select a restore destination.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (chkRestoreAsHyperV.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(txtVMName.Text))
                {
                    MessageBox.Show("Please enter a VM name.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(txtVMStorage.Text))
                {
                    MessageBox.Show("Please select VM storage path.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            return true;
        }

        private async Task ExecuteRestore()
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
                    int result;
                    var sourcePath = txtBackupSource.Text;
                    var destPath = chkRestoreToOriginal.IsChecked == true
                        ? ""
                        : txtRestoreDestination.Text;

                    if (chkRestoreAsHyperV.IsChecked == true)
                    {
                        result = BackupEngineInterop.RestoreHyperVVM(
                            sourcePath,
                            txtVMName.Text,
                            txtVMStorage.Text,
                            chkStartVM.IsChecked == true,
                            callback);
                    }
                    else if (chkRestoreSystemState.IsChecked == true)
                    {
                        result = BackupEngineInterop.RestoreSystemState(
                            sourcePath,
                            destPath,
                            callback);
                    }
                    else
                    {
                        result = BackupEngineInterop.RestoreFiles(
                            sourcePath,
                            destPath,
                            chkOverwrite.IsChecked == true,
                            callback);
                    }

                    if (result != 0)
                    {
                        var error = new StringBuilder(1024);
                        BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
                        throw new Exception($"Restore failed: {error}");
                    }
                });

                MessageBox.Show("Restore completed successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Restore failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
