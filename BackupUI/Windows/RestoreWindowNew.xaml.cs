using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using BackupCommon;
using BackupUI.Models;
using BackupUI.Services;
using MessageBox = System.Windows.MessageBox;

namespace BackupUI.Windows
{
    internal enum RestoreTargetKind
    {
        FileOrFolder,
        Volume,
        Disk
    }

    public partial class RestoreWindowNew : Window
    {
        private ObservableCollection<RestorePoint> restorePoints = new();
        private List<string> backupFiles = new();
        private readonly AvailableBackupInfo? _preloadedBackup;
        private readonly bool _requireAlternateDestination;
        private readonly List<string> _bootProtectedTargets = new();
        private RestoreTargetKind _restoreTargetKind = RestoreTargetKind.FileOrFolder;
        private string? _selectedTargetPath;
        private int? _selectedTargetDiskNumber;

        public RestoreWindowNew()
        {
            InitializeComponent();
            rbRestoreSelected.Checked += (s, e) => pnlItemSelection.Visibility = Visibility.Visible;
            rbRestoreAll.Checked += (s, e) => pnlItemSelection.Visibility = Visibility.Collapsed;
            Loaded += RestoreWindowNew_Loaded;
        }

        public RestoreWindowNew(AvailableBackupInfo backup, bool requireAlternateDestination)
            : this()
        {
            _preloadedBackup = backup;
            _requireAlternateDestination = requireAlternateDestination;
        }

        private async void RestoreWindowNew_Loaded(object sender, RoutedEventArgs e)
        {
            if (_preloadedBackup == null)
            {
                return;
            }

            txtBackupSource.Text = _preloadedBackup.BackupPath;

            if (_requireAlternateDestination)
            {
                rbOriginalLocation.IsEnabled = false;
                rbAlternateLocation.IsChecked = true;
                rbAlternateLocation.Content = "Alternate location (required for current boot/system backup)";
            }

            UpdateSelectedRestoreTargetKind();

            await ScanBackupAsync();
        }

        private async Task ScanBackupAsync()
        {
            if (string.IsNullOrWhiteSpace(txtBackupSource.Text))
            {
                return;
            }

            pnlProgress.Visibility = Visibility.Visible;
            txtProgress.Text = "Scanning backup files...";
            progressBar.IsIndeterminate = true;

            try
            {
                await ScanBackupSet(txtBackupSource.Text);

                pnlBackupInfo.Visibility = Visibility.Visible;
                grpRestoreOptions.IsEnabled = true;
                btnRestore.IsEnabled = true;
            }
            finally
            {
                progressBar.IsIndeterminate = false;
                pnlProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void BrowseBackup_Click(object sender, RoutedEventArgs e)
        {
            // Allow selection of either folder or file
            using var dialog = new OpenFileDialog
            {
                Title = "Select Backup File or Folder",
                Filter = "Backup Files (*.ssb;*.wim;*.bak;*.backup)|*.ssb;*.wim;*.bak;*.backup|All Files (*.*)|*.*",
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
                await ScanBackupAsync();
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
                        var extension = Path.GetExtension(path);
                        if (string.Equals(extension, ".ssb", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(extension, ".wim", StringComparison.OrdinalIgnoreCase))
                        {
                            backupFiles.Add(path);
                        }
                        else
                        {
                            var directory = Path.GetDirectoryName(path) ?? "";
                            var fileName = Path.GetFileNameWithoutExtension(path);

                            var allFiles = Directory.GetFiles(directory, $"{fileName}*")
                                .OrderBy(f => f)
                                .ToList();

                            backupFiles.AddRange(allFiles);
                        }
                    }
                    else if (Directory.Exists(path))
                    {
                        // Directory - find all backup files
                        backupFiles.AddRange(Directory.GetFiles(path, "*.ssb", SearchOption.AllDirectories));
                        backupFiles.AddRange(Directory.GetFiles(path, "*.wim", SearchOption.AllDirectories));
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

            UpdateSelectedRestoreTargetKind();
        }

        private async Task LoadBackupContents(string backupFile)
        {
            await Task.Run(() =>
            {
                try
                {
                    var buffer = new StringBuilder(32768);
                    using var preparedBackup = EncryptedBackupFileService.PrepareForRead(this, backupFile, Path.GetFileNameWithoutExtension(backupFile));
                    int result = BackupEngineInterop.ListBackupContents(preparedBackup.WorkingPath, buffer, buffer.Capacity);

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

                            if (lstBackupItems.Items.Count > 0)
                            {
                                lstBackupItems.SelectedIndex = 0;
                            }

                            UpdateSelectedRestoreTargetKind();
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

            UpdateDestinationHelpText();
        }

        private void RestoreItems_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateSelectedRestoreTargetKind();
        }

        private void BrowseRestoreDestination_Click(object sender, RoutedEventArgs e)
        {
            if (_restoreTargetKind == RestoreTargetKind.Disk)
            {
                var excludedDisks = GetProtectedDiskIndexes();
                var diskWindow = new DiskSelectionWindow(excludedDisks) { Owner = this };
                if (diskWindow.ShowDialog() == true && diskWindow.SelectedDisk != null)
                {
                    _selectedTargetDiskNumber = diskWindow.SelectedDisk.DiskIndex;
                    _selectedTargetPath = diskWindow.SelectedDisk.DisplayName;
                    txtRestoreDestination.Text = diskWindow.SelectedDisk.DisplayName;
                }
                return;
            }

            if (_restoreTargetKind == RestoreTargetKind.Volume)
            {
                var volumeWindow = new VolumeSelectionWindow(_bootProtectedTargets) { Owner = this };
                if (volumeWindow.ShowDialog() == true && volumeWindow.SelectedVolume != null)
                {
                    _selectedTargetPath = volumeWindow.SelectedVolume.VolumePath;
                    txtRestoreDestination.Text = volumeWindow.SelectedVolume.DisplayName;
                }
                return;
            }

            using var dialog = new FolderBrowserDialog
            {
                Description = "Select Restore Destination",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _selectedTargetPath = dialog.SelectedPath;
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
                using var preparedBackup = EncryptedBackupFileService.PrepareForRead(this, selectedPoint.FilePath, Path.GetFileNameWithoutExtension(selectedPoint.FilePath));
                int result;

                BackupEngineInterop.ProgressCallback callback = (percent, message) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = percent;
                        txtProgress.Text = message;
                        txtCurrentRestoreItem.Text = message;
                    });
                };

                switch (_restoreTargetKind)
                {
                    case RestoreTargetKind.Disk:
                        PrepareDiskTarget();
                        result = BackupEngineInterop.RestoreDisk(
                            preparedBackup.WorkingPath,
                            _selectedTargetDiskNumber ?? -1,
                            false,
                            callback);
                        break;

                    case RestoreTargetKind.Volume:
                        PrepareVolumeTarget();
                        result = BackupEngineInterop.RestoreVolume(
                            preparedBackup.WorkingPath,
                            _selectedTargetPath ?? string.Empty,
                            false,
                            callback);
                        break;

                    default:
                        result = BackupEngineInterop.RestoreFiles(
                            preparedBackup.WorkingPath,
                            destination,
                            chkOverwrite.IsChecked == true,
                            callback);
                        break;
                }

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

            if (_requireAlternateDestination && rbOriginalLocation.IsChecked == true)
            {
                MessageBox.Show("This backup includes the currently booted system/boot drive. Restore it to a non-boot destination from Windows, or boot from the recovery disk for an in-place restore.",
                    "Recovery Environment Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            if (_requireAlternateDestination && rbAlternateLocation.IsChecked == true)
            {
                string destinationRoot = (_selectedTargetPath ?? txtRestoreDestination.Text);
                destinationRoot = Path.GetPathRoot(destinationRoot)?.TrimEnd('\\') ?? destinationRoot.TrimEnd('\\');
                string systemRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(destinationRoot) &&
                    string.Equals(destinationRoot, systemRoot, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Please select a restore destination that is not on the currently booted drive.",
                        "Validation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }
            }

            UpdateSelectedRestoreTargetKind();

            if (_restoreTargetKind == RestoreTargetKind.Disk && (!_selectedTargetDiskNumber.HasValue || string.IsNullOrWhiteSpace(txtRestoreDestination.Text)))
            {
                MessageBox.Show("Please select the target disk to restore to.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (_restoreTargetKind == RestoreTargetKind.Volume && string.IsNullOrWhiteSpace(_selectedTargetPath) && string.IsNullOrWhiteSpace(txtRestoreDestination.Text))
            {
                MessageBox.Show("Please select the target volume to restore to.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void UpdateSelectedRestoreTargetKind()
        {
            var selectedPoint = lstRestorePoints.SelectedItem as RestorePoint;
            if (selectedPoint == null)
            {
                _restoreTargetKind = RestoreTargetKind.FileOrFolder;
                UpdateDestinationHelpText();
                return;
            }

            var selectedItemText = lstBackupItems.SelectedItem?.ToString() ?? string.Empty;
            if (selectedItemText.Contains("PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
            {
                _restoreTargetKind = RestoreTargetKind.Disk;
            }
            else if (selectedItemText.StartsWith("\\?\\", StringComparison.OrdinalIgnoreCase) ||
                     selectedItemText.EndsWith(":\\", StringComparison.OrdinalIgnoreCase) ||
                     selectedItemText.EndsWith(":", StringComparison.OrdinalIgnoreCase))
            {
                _restoreTargetKind = RestoreTargetKind.Volume;
            }
            else
            {
                _restoreTargetKind = RestoreTargetKind.FileOrFolder;
            }

            UpdateDestinationHelpText();
        }

        private void UpdateDestinationHelpText()
        {
            if (txtDestinationHelp == null)
            {
                return;
            }

            txtDestinationHelp.Text = _restoreTargetKind switch
            {
                RestoreTargetKind.Disk => "Disk restore: choose a target disk. It will be formatted and repartitioned before restore.",
                RestoreTargetKind.Volume => "Volume restore: choose a target volume. It will be formatted before restore.",
                _ => "File/folder restore: choose a destination folder, or restore to the original location if allowed."
            };
        }

        private List<int> GetProtectedDiskIndexes()
        {
            var indexes = new List<int>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Index FROM Win32_DiskDrive");
                foreach (ManagementObject disk in searcher.Get())
                {
                    string deviceId = disk["DeviceID"]?.ToString() ?? string.Empty;
                    if (deviceId.IndexOf("PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        int.TryParse(disk["Index"]?.ToString(), out int index) &&
                        deviceId.IndexOf(Environment.SystemDirectory.Substring(0, 2), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        indexes.Add(index);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetProtectedDiskIndexes warning: {ex.Message}");
            }

            return indexes;
        }

        private void PrepareDiskTarget()
        {
            var confirmation = MessageBox.Show(
                "WARNING: The selected target disk will be formatted and repartitioned. ALL DATA ON THE TARGET DISK WILL BE LOST.\n\nDo you want to continue?",
                "Confirm Disk Format",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirmation != MessageBoxResult.Yes)
            {
                throw new OperationCanceledException("Disk restore cancelled by user.");
            }
        }

        private void PrepareVolumeTarget()
        {
            var confirmation = MessageBox.Show(
                "WARNING: The selected target volume will be formatted. ALL DATA ON THE TARGET VOLUME WILL BE LOST.\n\nDo you want to continue?",
                "Confirm Volume Format",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirmation != MessageBoxResult.Yes)
            {
                throw new OperationCanceledException("Volume restore cancelled by user.");
            }

            string volumePath = _selectedTargetPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(volumePath))
            {
                throw new InvalidOperationException("No target volume selected.");
            }

            string driveLetter = volumePath.TrimEnd('\\');
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c format {driveLetter} /FS:NTFS /Q /Y",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            process?.WaitForExit();
            if (process == null || process.ExitCode != 0)
            {
                throw new InvalidOperationException("Failed to format the selected target volume.");
            }
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
