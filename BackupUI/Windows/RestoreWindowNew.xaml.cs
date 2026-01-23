using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using BackupUI.Services;
using MessageBox = System.Windows.MessageBox;

namespace BackupUI.Windows
{
    public partial class RestoreWindowNew : Window
    {
        private ObservableCollection<RestorePoint> restorePoints = new();
        private List<string> backupFiles = new();

        public RestoreWindowNew()
        {
            InitializeComponent();
            rbRestoreSelected.Checked += (s, e) => pnlItemSelection.Visibility = Visibility.Visible;
            rbRestoreAll.Checked += (s, e) => pnlItemSelection.Visibility = Visibility.Collapsed;
        }

        private void BrowseBackup_Click(object sender, RoutedEventArgs e)
        {
            // Allow selection of either folder or file
            using var dialog = new OpenFileDialog
            {
                Title = "Select Backup File or Folder",
                Filter = "Backup Files (*.bak;*.backup)|*.bak;*.backup|All Files (*.*)|*.*",
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false
            };

            // Allow folder selection too
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var path = dialog.FileName;
                
                // If user selected a file, get its directory
                if (File.Exists(path))
                {
                    txtBackupSource.Text = path;
                }
                else
                {
                    // Try folder selection
                    using var folderDialog = new FolderBrowserDialog
                    {
                        Description = "Select Backup Folder",
                        ShowNewFolderButton = false
                    };

                    if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        txtBackupSource.Text = folderDialog.SelectedPath;
                    }
                }
            }
        }

        private async void ScanBackup_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtBackupSource.Text))
            {
                MessageBox.Show("Please select a backup source.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                pnlProgress.Visibility = Visibility.Visible;
                txtProgress.Text = "Scanning backup files...";
                progressBar.IsIndeterminate = true;

                await ScanBackupSet(txtBackupSource.Text);

                pnlBackupInfo.Visibility = Visibility.Visible;
                grpRestoreOptions.IsEnabled = true;
                btnRestore.IsEnabled = true;

                progressBar.IsIndeterminate = false;
                pnlProgress.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                progressBar.IsIndeterminate = false;
                pnlProgress.Visibility = Visibility.Collapsed;
                
                MessageBox.Show($"Error scanning backup: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ScanBackupSet(string path)
        {
            await Task.Run(() =>
            {
                backupFiles.Clear();
                restorePoints.Clear();

                try
                {
                    // Determine if it's a file or directory
                    if (File.Exists(path))
                    {
                        // Single file - find all parts
                        var directory = Path.GetDirectoryName(path) ?? "";
                        var fileName = Path.GetFileNameWithoutExtension(path);
                        
                        // Look for split files (name.001, name.002, etc.)
                        var allFiles = Directory.GetFiles(directory, $"{fileName}*")
                            .OrderBy(f => f)
                            .ToList();

                        backupFiles.AddRange(allFiles);
                    }
                    else if (Directory.Exists(path))
                    {
                        // Directory - find all backup files
                        backupFiles.AddRange(Directory.GetFiles(path, "*.bak", SearchOption.AllDirectories));
                        backupFiles.AddRange(Directory.GetFiles(path, "*.backup", SearchOption.AllDirectories));
                    }

                    // Analyze backup files
                    AnalyzeBackupFiles();

                    Dispatcher.Invoke(() =>
                    {
                        UpdateBackupInfo();
                        lstRestorePoints.ItemsSource = restorePoints;
                        if (restorePoints.Count > 0)
                        {
                            lstRestorePoints.SelectedIndex = restorePoints.Count - 1; // Select latest
                        }
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                        MessageBox.Show($"Error scanning backup: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }

        private void AnalyzeBackupFiles()
        {
            // Group files by backup type
            var fullBackups = backupFiles.Where(f => f.Contains("full", StringComparison.OrdinalIgnoreCase)).ToList();
            var incrementalBackups = backupFiles.Where(f => f.Contains("incremental", StringComparison.OrdinalIgnoreCase)).ToList();
            var differentialBackups = backupFiles.Where(f => f.Contains("differential", StringComparison.OrdinalIgnoreCase)).ToList();

            // Create restore points
            int pointNumber = 1;

            // Full backups
            foreach (var fullBackup in fullBackups.OrderBy(f => File.GetCreationTime(f)))
            {
                restorePoints.Add(new RestorePoint
                {
                    DisplayName = $"Point {pointNumber}: Full Backup",
                    Description = $"Created: {File.GetCreationTime(fullBackup):yyyy-MM-dd HH:mm:ss}",
                    BackupType = "Full",
                    FilePath = fullBackup,
                    Timestamp = File.GetCreationTime(fullBackup)
                });
                pointNumber++;
            }

            // Incremental backups
            foreach (var incBackup in incrementalBackups.OrderBy(f => File.GetCreationTime(f)))
            {
                restorePoints.Add(new RestorePoint
                {
                    DisplayName = $"Point {pointNumber}: Incremental Backup",
                    Description = $"Created: {File.GetCreationTime(incBackup):yyyy-MM-dd HH:mm:ss}",
                    BackupType = "Incremental",
                    FilePath = incBackup,
                    Timestamp = File.GetCreationTime(incBackup)
                });
                pointNumber++;
            }

            // Differential backups
            foreach (var diffBackup in differentialBackups.OrderBy(f => File.GetCreationTime(f)))
            {
                restorePoints.Add(new RestorePoint
                {
                    DisplayName = $"Point {pointNumber}: Differential Backup",
                    Description = $"Created: {File.GetCreationTime(diffBackup):yyyy-MM-dd HH:mm:ss}",
                    BackupType = "Differential",
                    FilePath = diffBackup,
                    Timestamp = File.GetCreationTime(diffBackup)
                });
                pointNumber++;
            }

            // If no specific types found, add all files as restore points
            if (restorePoints.Count == 0 && backupFiles.Count > 0)
            {
                foreach (var file in backupFiles.OrderBy(f => File.GetCreationTime(f)))
                {
                    restorePoints.Add(new RestorePoint
                    {
                        DisplayName = $"Point {pointNumber}: Backup",
                        Description = $"File: {Path.GetFileName(file)} - {File.GetCreationTime(file):yyyy-MM-dd HH:mm:ss}",
                        BackupType = "Unknown",
                        FilePath = file,
                        Timestamp = File.GetCreationTime(file)
                    });
                    pointNumber++;
                }
            }
        }

        private void UpdateBackupInfo()
        {
            var totalSize = backupFiles.Sum(f => new FileInfo(f).Length);
            var sizeGB = totalSize / (1024.0 * 1024.0 * 1024.0);

            txtBackupInfo.Text = $"Found {backupFiles.Count} backup file(s)\n" +
                                $"Total size: {sizeGB:F2} GB\n" +
                                $"Restore points available: {restorePoints.Count}";
        }

        private async void RestorePoints_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (lstRestorePoints.SelectedItem is RestorePoint point)
            {
                await LoadBackupContents(point.FilePath);
            }
        }

        private async Task LoadBackupContents(string backupFile)
        {
            await Task.Run(() =>
            {
                try
                {
                    var buffer = new StringBuilder(32768);
                    int result = BackupEngineInterop.ListBackupContents(backupFile, buffer, buffer.Capacity);

                    if (result == 0)
                    {
                        var items = buffer.ToString()
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .ToList();

                        Dispatcher.Invoke(() =>
                        {
                            lstBackupItems.Items.Clear();
                            foreach (var item in items)
                            {
                                lstBackupItems.Items.Add(item);
                            }
                        });
                    }
                }
                catch { }
            });
        }

        private void RestoreLocation_Changed(object sender, RoutedEventArgs e)
        {
            pnlAlternateLocation.Visibility = rbAlternateLocation.IsChecked == true 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        private void BrowseRestoreDestination_Click(object sender, RoutedEventArgs e)
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

        private async void StartRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateRestore())
                return;

            var result = MessageBox.Show(
                "Are you sure you want to restore? This may overwrite existing files.",
                "Confirm Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                pnlProgress.Visibility = Visibility.Visible;
                progressBar.IsIndeterminate = false;
                progressBar.Value = 0;

                await PerformRestore();

                MessageBox.Show("Restore completed successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Restore failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                pnlProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async Task PerformRestore()
        {
            var selectedPoint = lstRestorePoints.SelectedItem as RestorePoint;
            if (selectedPoint == null) return;

            var destination = rbAlternateLocation.IsChecked == true
                ? txtRestoreDestination.Text
                : ""; // Original location

            await Task.Run(() =>
            {
                var result = BackupEngineInterop.RestoreFiles(
                    selectedPoint.FilePath,
                    destination,
                    chkOverwrite.IsChecked == true,
                    (percent, message) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            progressBar.Value = percent;
                            txtProgress.Text = message;
                        });
                    });

                if (result != 0)
                {
                    var error = new StringBuilder(1024);
                    BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
                    throw new Exception($"Restore failed: {error}");
                }
            });
        }

        private bool ValidateRestore()
        {
            if (lstRestorePoints.SelectedItem == null)
            {
                MessageBox.Show("Please select a restore point.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (rbAlternateLocation.IsChecked == true && string.IsNullOrWhiteSpace(txtRestoreDestination.Text))
            {
                MessageBox.Show("Please select a restore destination.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class RestorePoint
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string BackupType { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
