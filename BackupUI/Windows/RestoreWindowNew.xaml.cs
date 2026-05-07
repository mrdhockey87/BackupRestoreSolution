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
using System.Windows.Controls;
using System.Xml.Linq;
using System.Windows.Forms;
using SecureServerBackupCommon;
using SecureServerBackup.Models;
using SecureServerBackup.Services;
using MessageBox = System.Windows.MessageBox;

namespace SecureServerBackup.Windows
{
    internal enum RestoreTargetKind
    {
        FileOrFolder,
        Volume,
        Disk,
        HyperVVm,
        HyperVVirtualDisk
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
        private NativeBackupMountManager.RestoreDiskPlan? _diskRestorePlan;
        private bool _isHyperVBackupPoint;

        // Restore target drive tree items
        private readonly ObservableCollection<DriveTreeItem> _restoreTargetItems = new();

        // Selected volumes/disk-group from the restore-volume selection dialog
        private VolumeInfo? _selectedRestoreVolume;
        private IReadOnlyList<VolumeInfo>? _selectedRestoreDiskGroup;

        public static class RegularHyperVRestoreHelper
        {
            public static bool SupportsHyperVVirtualDiskRestore(string selectedItemText)
            {
                if (string.IsNullOrWhiteSpace(selectedItemText))
                {
                    return false;
                }

                return selectedItemText.Contains("PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase) ||
                       selectedItemText.StartsWith("\\?\\", StringComparison.OrdinalIgnoreCase) ||
                       selectedItemText.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ||
                       selectedItemText.EndsWith(":\\", StringComparison.OrdinalIgnoreCase) ||
                       selectedItemText.EndsWith(":", StringComparison.OrdinalIgnoreCase) ||
                       selectedItemText.Contains("SystemState", StringComparison.OrdinalIgnoreCase);
            }

            public static string NormalizeHyperVVmName(string? displayText)
            {
                if (string.IsNullOrWhiteSpace(displayText))
                {
                    return string.Empty;
                }

                int stateIndex = displayText.LastIndexOf(" (", StringComparison.Ordinal);
                return stateIndex > 0 ? displayText[..stateIndex].Trim() : displayText.Trim();
            }

            public static string GetDefaultHyperVVmName(string virtualDiskPath)
            {
                if (string.IsNullOrWhiteSpace(virtualDiskPath))
                {
                    return string.Empty;
                }

                return Path.GetFileNameWithoutExtension(virtualDiskPath)?.Trim() ?? string.Empty;
            }

            public static string GetDefaultHyperVVmStoragePath(string virtualDiskPath)
            {
                if (string.IsNullOrWhiteSpace(virtualDiskPath))
                {
                    return string.Empty;
                }

                return Path.GetDirectoryName(virtualDiskPath)?.Trim() ?? string.Empty;
            }

            public static string BuildCreateVirtualMachineScript(string vmName, string vmStoragePath, string virtualDiskPath, int generation, bool startAfterCreate)
            {
                string escapedVmName = EscapePowerShellSingleQuotedString(vmName);
                string escapedVmStoragePath = EscapePowerShellSingleQuotedString(vmStoragePath);
                string escapedVirtualDiskPath = EscapePowerShellSingleQuotedString(virtualDiskPath);
                string controllerType = generation == 1 ? "IDE" : "SCSI";
                string firmwareCommand = generation == 2
                    ? $"; $bootDisk = Get-VMHardDiskDrive -VMName '{escapedVmName}' | Where-Object {{ $_.Path -eq '{escapedVirtualDiskPath}' }} | Select-Object -First 1; if ($null -eq $bootDisk) {{ throw 'The restored virtual disk could not be located on the new virtual machine.'; }}; Set-VMFirmware -VMName '{escapedVmName}' -FirstBootDevice $bootDisk -EnableSecureBoot Off -ErrorAction Stop"
                    : string.Empty;
                string startCommand = startAfterCreate
                    ? $"; Start-VM -Name '{escapedVmName}' -ErrorAction Stop | Out-Null"
                    : string.Empty;

                return $"$vmName='{escapedVmName}'; $vmPath='{escapedVmStoragePath}'; $diskPath='{escapedVirtualDiskPath}'; if ([string]::IsNullOrWhiteSpace($vmName)) {{ throw 'A virtual machine name is required.'; }}; if ([string]::IsNullOrWhiteSpace($vmPath)) {{ throw 'A virtual machine storage path is required.'; }}; if ([string]::IsNullOrWhiteSpace($diskPath)) {{ throw 'A Hyper-V virtual disk path is required.'; }}; New-Item -ItemType Directory -Path $vmPath -Force | Out-Null; if (Get-VM -Name $vmName -ErrorAction SilentlyContinue) {{ throw \"A Hyper-V virtual machine named '$vmName' already exists.\"; }}; New-VM -Name $vmName -Generation {generation} -Path $vmPath -MemoryStartupBytes 2GB -ErrorAction Stop | Out-Null; Add-VMHardDiskDrive -VMName $vmName -ControllerType {controllerType} -ControllerNumber 0 -ControllerLocation 0 -Path $diskPath -ErrorAction Stop | Out-Null{firmwareCommand}{startCommand}";
            }

            private static string EscapePowerShellSingleQuotedString(string value)
            {
                return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
            }
        }

        public static class HyperVRestorePointHelper
        {
            private static string? ReadMetadataValue(string backupPointPath, string key)
            {
                string metadataPath = Path.Combine(backupPointPath, "hyperv_backup_info.txt");
                if (!File.Exists(metadataPath))
                {
                    return null;
                }

                foreach (string line in File.ReadLines(metadataPath))
                {
                    string[] parts = line.Split('=', 2);
                    if (parts.Length == 2 && string.Equals(parts[0].Trim(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        string value = parts[1].Trim();
                        return string.IsNullOrWhiteSpace(value) ? null : value;
                    }
                }

                return null;
            }

            public static bool IsHyperVBackupPoint(string path)
            {
                if (!Directory.Exists(path))
                {
                    return false;
                }

                return File.Exists(Path.Combine(path, "hyperv_backup_info.txt")) ||
                       Directory.Exists(Path.Combine(path, "Export"));
            }

            public static string? ResolveExportPath(string backupPointPath)
            {
                if (!Directory.Exists(backupPointPath))
                {
                    return null;
                }

                string exportFolder = Path.Combine(backupPointPath, "Export");
                if (Directory.Exists(exportFolder))
                {
                    return exportFolder;
                }

                string? exportPath = ReadMetadataValue(backupPointPath, "ExportPath");
                return !string.IsNullOrWhiteSpace(exportPath) && Directory.Exists(exportPath)
                    ? exportPath
                    : null;
            }

            public static string? FindPrimaryVirtualDisk(string backupPointPath)
            {
                string? exportPath = ResolveExportPath(backupPointPath);
                if (string.IsNullOrWhiteSpace(exportPath) || !Directory.Exists(exportPath))
                {
                    return null;
                }

                string[] virtualDisks = Directory.GetFiles(exportPath, "*.vhd*", SearchOption.AllDirectories);
                if (virtualDisks.Length == 0)
                {
                    return null;
                }

                return virtualDisks
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.Length)
                    .ThenBy(file => file.FullName)
                    .Select(file => file.FullName)
                    .FirstOrDefault();
            }

            public static string ResolveVmName(string backupPointPath)
            {
                try
                {
                    string? vmNameFromMetadata = ReadMetadataValue(backupPointPath, "VmName");
                    if (!string.IsNullOrWhiteSpace(vmNameFromMetadata))
                    {
                        return vmNameFromMetadata;
                    }

                    string? exportPath = ResolveExportPath(backupPointPath);
                    if (string.IsNullOrWhiteSpace(exportPath) || !Directory.Exists(exportPath))
                    {
                        return Path.GetFileNameWithoutExtension(backupPointPath);
                    }

                    string? configFile = Directory.GetFiles(exportPath, "*.xml", SearchOption.AllDirectories)
                        .FirstOrDefault(file =>
                            string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), "Virtual Machines", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file) ?? string.Empty)), "Virtual Machines", StringComparison.OrdinalIgnoreCase));

                    if (string.IsNullOrWhiteSpace(configFile) || !File.Exists(configFile))
                    {
                        return Path.GetFileNameWithoutExtension(backupPointPath);
                    }

                    XDocument document = XDocument.Load(configFile);
                    string? vmName = document.Descendants()
                        .FirstOrDefault(element => string.Equals(element.Name.LocalName, "Name", StringComparison.OrdinalIgnoreCase))
                        ?.Value;

                    return string.IsNullOrWhiteSpace(vmName)
                        ? Path.GetFileNameWithoutExtension(backupPointPath)
                        : vmName.Trim();
                }
                catch
                {
                    return Path.GetFileNameWithoutExtension(backupPointPath);
                }
            }
        }

        private sealed class MountedVirtualDiskScope : IDisposable
        {
            private readonly string _virtualDiskPath;
            private bool _disposed;

            public MountedVirtualDiskScope(string virtualDiskPath, string driveRoot)
            {
                _virtualDiskPath = virtualDiskPath;
                DriveRoot = driveRoot;
            }

            public string DriveRoot { get; }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                BackupMountManager.UnmountVirtualDisk(_virtualDiskPath);
                _disposed = true;
            }
        }

        public RestoreWindowNew()
        {
            InitializeComponent();
            rbRestoreSelected.Checked += (s, e) => pnlItemSelection.Visibility = Visibility.Visible;
            rbRestoreAll.Checked += (s, e) => pnlItemSelection.Visibility = Visibility.Collapsed;
            Loaded += RestoreWindowNew_Loaded;
        }

        private static DateTime GetEntryTimestamp(string path)
        {
            return File.Exists(path)
                ? File.GetCreationTime(path)
                : Directory.GetCreationTime(path);
        }

        private MountedVirtualDiskScope MountPrimaryHyperVVirtualDisk(string backupPointPath)
        {
            string? virtualDiskPath = HyperVRestorePointHelper.FindPrimaryVirtualDisk(backupPointPath);
            if (string.IsNullOrWhiteSpace(virtualDiskPath))
            {
                throw new InvalidOperationException("No VHD or VHDX guest disk was found in the selected Hyper-V backup point.");
            }

            var mountResult = BackupMountManager.MountVirtualDiskReadOnly(virtualDiskPath);
            if (!mountResult.Success || string.IsNullOrWhiteSpace(mountResult.DriveLetter))
            {
                throw new InvalidOperationException($"Failed to mount the Hyper-V guest disk: {mountResult.Error}");
            }

            string driveRoot = mountResult.DriveLetter.EndsWith(":", StringComparison.Ordinal)
                ? mountResult.DriveLetter + "\\"
                : mountResult.DriveLetter;

            return new MountedVirtualDiskScope(virtualDiskPath, driveRoot);
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
                Filter = "Backup Files (*.ssb;*.bak;*.backup)|*.ssb;*.bak;*.backup|All Files (*.*)|*.*",
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
                        if (string.Equals(extension, ".ssb", StringComparison.OrdinalIgnoreCase))
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
                        // Directory - find all backup files and Hyper-V backup point folders
                        backupFiles.AddRange(Directory.GetFiles(path, "*.ssb", SearchOption.AllDirectories));
                        backupFiles.AddRange(Directory.GetDirectories(path, "*.ssb", SearchOption.AllDirectories)
                            .Where(HyperVRestorePointHelper.IsHyperVBackupPoint));
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
            foreach (var fullBackup in fullBackups.OrderBy(GetEntryTimestamp))
            {
                DateTime timestamp = GetEntryTimestamp(fullBackup);
                restorePoints.Add(new RestorePoint
                {
                    DisplayName = $"Point {pointNumber}: Full Backup",
                    Description = $"Created: {timestamp:yyyy-MM-dd HH:mm:ss}",
                    BackupType = "Full",
                    FilePath = fullBackup,
                    Timestamp = timestamp
                });
                pointNumber++;
            }

            // Incremental backups
            foreach (var incBackup in incrementalBackups.OrderBy(GetEntryTimestamp))
            {
                DateTime timestamp = GetEntryTimestamp(incBackup);
                restorePoints.Add(new RestorePoint
                {
                    DisplayName = $"Point {pointNumber}: Incremental Backup",
                    Description = $"Created: {timestamp:yyyy-MM-dd HH:mm:ss}",
                    BackupType = "Incremental",
                    FilePath = incBackup,
                    Timestamp = timestamp
                });
                pointNumber++;
            }

            // Differential backups
            foreach (var diffBackup in differentialBackups.OrderBy(GetEntryTimestamp))
            {
                DateTime timestamp = GetEntryTimestamp(diffBackup);
                restorePoints.Add(new RestorePoint
                {
                    DisplayName = $"Point {pointNumber}: Differential Backup",
                    Description = $"Created: {timestamp:yyyy-MM-dd HH:mm:ss}",
                    BackupType = "Differential",
                    FilePath = diffBackup,
                    Timestamp = timestamp
                });
                pointNumber++;
            }

            // If no specific types found, add all files as restore points
            if (restorePoints.Count == 0 && backupFiles.Count > 0)
            {
                foreach (var file in backupFiles.OrderBy(GetEntryTimestamp))
                {
                    DateTime timestamp = GetEntryTimestamp(file);
                    restorePoints.Add(new RestorePoint
                    {
                        DisplayName = $"Point {pointNumber}: Backup",
                        Description = $"File: {Path.GetFileName(file)} - {timestamp:yyyy-MM-dd HH:mm:ss}",
                        BackupType = "Unknown",
                        FilePath = file,
                        Timestamp = timestamp
                    });
                    pointNumber++;
                }
            }
        }

        private void UpdateBackupInfo()
        {
            long totalSize = 0;
            foreach (var backupFile in backupFiles)
            {
                if (File.Exists(backupFile))
                {
                    totalSize += new FileInfo(backupFile).Length;
                }
                else if (Directory.Exists(backupFile))
                {
                    totalSize += Directory.EnumerateFiles(backupFile, "*", SearchOption.AllDirectories)
                        .Select(file => new FileInfo(file).Length)
                        .Sum();
                }
            }

            var sizeGB = totalSize / (1024.0 * 1024.0 * 1024.0);

            txtBackupInfo.Text = $"Found {backupFiles.Count} backup file(s)\n" +
                                $"Total size: {sizeGB:F2} GB\n" +
                                $"Restore points available: {restorePoints.Count}";
        }

        private string CreateVolumeOnDiskForHyperVRestore(int diskNumber)
        {
            var script = $"$diskNumber={diskNumber}; Clear-Disk -Number $diskNumber -RemoveData -RemoveOEM -Confirm:$false -ErrorAction Stop; Initialize-Disk -Number $diskNumber -PartitionStyle GPT -ErrorAction Stop; $partition = New-Partition -DiskNumber $diskNumber -UseMaximumSize -AssignDriveLetter -ErrorAction Stop; Format-Volume -Partition $partition -FileSystem NTFS -NewFileSystemLabel 'SSBRestore' -Confirm:$false -Force -ErrorAction Stop | Out-Null; ($partition | Get-Volume).DriveLetter";

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            string output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
            string errors = process?.StandardError.ReadToEnd() ?? string.Empty;
            process?.WaitForExit();

            if (process == null || process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to prepare the target disk for Hyper-V guest restore. {errors}".Trim());
            }

            string driveLetter = output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(driveLetter))
            {
                throw new InvalidOperationException("The target disk was prepared, but no drive letter was assigned to the new restore volume.");
            }

            return driveLetter.EndsWith(":", StringComparison.Ordinal) ? driveLetter + "\\" : driveLetter + ":\\";
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
                    _isHyperVBackupPoint = HyperVRestorePointHelper.IsHyperVBackupPoint(backupFile);
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

                            if (_isHyperVBackupPoint && string.IsNullOrWhiteSpace(txtHyperVVmName.Text))
                            {
                                txtHyperVVmName.Text = HyperVRestorePointHelper.ResolveVmName(backupFile);
                            }

                            if (!_isHyperVBackupPoint)
                            {
                                LoadAvailableHyperVVirtualMachines();
                            }

                            UpdateHyperVRestoreMode();
                            LoadDiskRestorePlan(preparedBackup.WorkingPath);
                            UpdateSelectedRestoreTargetKind();
                        });
                    }
                }
                catch { }
            });
        }

        private void LoadDiskRestorePlan(string backupPath)
        {
            try
            {
                var planResult = NativeBackupMountManager.BuildDiskRestorePlan(backupPath);
                _diskRestorePlan = planResult.Success ? planResult.Plan : null;

                if (planResult.Success && planResult.Plan.HasMetadata)
                {
                    txtDestinationHelp.Text = $"Disk reconstruction metadata detected for Disk {planResult.Plan.SourceDiskNumber}. Select a target disk/volume to map the restored layout.";
                }
                else if (!string.IsNullOrWhiteSpace(planResult.Error))
                {
                    Debug.WriteLine($"LoadDiskRestorePlan: {planResult.Error}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadDiskRestorePlan exception: {ex.Message}");
                _diskRestorePlan = null;
            }
        }

        private void RestoreLocation_Changed(object sender, RoutedEventArgs e)
        {
            if (pnlAlternateLocation == null || rbAlternateLocation == null)
                return;

            pnlAlternateLocation.Visibility = rbAlternateLocation.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;

            UpdateDestinationHelpText();
        }

        private void HyperVVmRestoreMode_Changed(object sender, RoutedEventArgs e)
        {
            UpdateHyperVVmRestoreOptions();
        }

        private void UpdateHyperVVmRestoreOptions()
        {
            if (pnlHyperVReplaceExistingOptions == null || pnlHyperVDirectoryOptions == null)
                return;

            bool replaceMode = rbHyperVReplaceExisting?.IsChecked == true;
            pnlHyperVReplaceExistingOptions.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;
            pnlHyperVDirectoryOptions.Visibility = replaceMode ? Visibility.Collapsed : Visibility.Visible;

            if (replaceMode && cmbHyperVVmToReplace.Items.Count == 0)
            {
                LoadNonRunningHyperVVms();
            }
        }

        private void LoadNonRunningHyperVVms()
        {
            if (cmbHyperVVmToReplace == null)
                return;

            cmbHyperVVmToReplace.Items.Clear();

            try
            {
                // List VMs that are Off or Saved (not Running or Paused)
                string script = "Get-VM | Where-Object { $_.State -notin @('Running','Paused') } | Select-Object -ExpandProperty Name";
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });

                string output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
                process?.WaitForExit();

                foreach (string vm in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = vm.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        cmbHyperVVmToReplace.Items.Add(trimmed);
                }

                if (cmbHyperVVmToReplace.Items.Count > 0)
                    cmbHyperVVmToReplace.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadNonRunningHyperVVms warning: {ex.Message}");
            }
        }

        /// <summary>Shows the correct destination panel based on the current restore target kind.</summary>
        private void UpdateLocationPanelVisibility()
        {
            if (pnlLocationChoice == null)
                return;

            bool isFileFolder = _restoreTargetKind == RestoreTargetKind.FileOrFolder;
            bool isDisk = _restoreTargetKind == RestoreTargetKind.Disk;
            bool isHyperVVm = _restoreTargetKind == RestoreTargetKind.HyperVVm;
            bool isHyperVVirtualDisk = _restoreTargetKind == RestoreTargetKind.HyperVVirtualDisk;

            // The drive tree is shown for every restore kind except Hyper-V VM and Hyper-V virtual disk
            // (those have their own dedicated destination panels).
            bool showTree = !isHyperVVm && !isHyperVVirtualDisk;

            // File/folder restores also keep the original/alternate location choice visible
            // so the user can still restore to the original path or type a custom folder path.
            pnlLocationChoice.Visibility = isFileFolder ? Visibility.Visible : Visibility.Collapsed;
            pnlDriveTargetTree.Visibility = showTree ? Visibility.Visible : Visibility.Collapsed;

            // For Hyper-V VM restores the sub-options panel handles destination; hide generic pickers
            if (isHyperVVm)
            {
                pnlLocationChoice.Visibility = Visibility.Collapsed;
                pnlDriveTargetTree.Visibility = Visibility.Collapsed;
            }

            // Update the tree help text and trigger a load when first shown
            if (showTree && pnlDriveTargetTree.Visibility == Visibility.Visible)
            {
                txtDriveTreeHelp.Text = isDisk
                    ? "Choose the target disk to restore onto. The boot/system disk is shown but cannot be selected."
                    : isFileFolder
                        ? "Select a target drive or volume to restore files to. Clicking a drive sets it as the Alternate Location above. The boot disk is shown but cannot be selected."
                        : "Choose the target volume to restore onto. Boot/system volumes cannot be selected.";

                // Only reload when items are not already present
                if (treeViewRestoreTarget.Items.Count == 0)
                    _ = LoadRestoreTargetDrivesAsync();
            }
        }

        private void RegularHyperVRestoreOption_Changed(object sender, RoutedEventArgs e)
        {
            UpdateRegularHyperVRestoreMode();
            UpdateSelectedRestoreTargetKind();
        }

        private void ExistingHyperVVmAttach_Changed(object sender, RoutedEventArgs e)
        {
            UpdateExistingHyperVVmOptions();
        }

        private void HyperVRestoreTarget_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSelectedRestoreTargetKind();
        }

        private void UpdateHyperVRestoreMode()
        {
            if (pnlHyperVRestoreMode == null || pnlHyperVVmOptions == null)
            {
                return;
            }

            pnlHyperVRestoreMode.Visibility = _isHyperVBackupPoint ? Visibility.Visible : Visibility.Collapsed;

            if (!_isHyperVBackupPoint)
            {
                pnlHyperVVmOptions.Visibility = Visibility.Collapsed;
                UpdateLocationPanelVisibility();
                return;
            }

            if (cmbHyperVRestoreTarget.SelectedItem is ComboBoxItem selectedItem)
            {
                bool isHyperVVm = string.Equals(selectedItem.Tag?.ToString(), "HyperVVm", StringComparison.OrdinalIgnoreCase);
                pnlHyperVVmOptions.Visibility = isHyperVVm ? Visibility.Visible : Visibility.Collapsed;

                if (isHyperVVm)
                    UpdateHyperVVmRestoreOptions();
            }

            UpdateRegularHyperVRestoreMode();
            UpdateLocationPanelVisibility();
        }

        private void UpdateRegularHyperVRestoreMode()
        {
            if (chkRestoreToHyperVDisk == null || pnlRegularHyperVRestore == null)
            {
                return;
            }

            string selectedItemText = lstBackupItems.SelectedItem?.ToString() ?? string.Empty;
            bool supportsRegularHyperV = !_isHyperVBackupPoint && RegularHyperVRestoreHelper.SupportsHyperVVirtualDiskRestore(selectedItemText);

            chkRestoreToHyperVDisk.Visibility = supportsRegularHyperV ? Visibility.Visible : Visibility.Collapsed;

            if (!supportsRegularHyperV)
            {
                chkRestoreToHyperVDisk.IsChecked = false;
            }

            pnlRegularHyperVRestore.Visibility = supportsRegularHyperV && chkRestoreToHyperVDisk.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (pnlRegularHyperVRestore.Visibility == Visibility.Visible)
            {
                ApplyDefaultNewHyperVVmSettings();
            }

            UpdateExistingHyperVVmOptions();
        }

        private void UpdateExistingHyperVVmOptions()
        {
            if (pnlExistingHyperVVmOptions == null || pnlNewHyperVVmOptions == null || chkAttachToExistingHyperVVm == null || rbCreateNewHyperVVm == null)
            {
                return;
            }

            bool isHyperVRestoreVisible = pnlRegularHyperVRestore.Visibility == Visibility.Visible;
            bool attachExistingVisible = isHyperVRestoreVisible && chkAttachToExistingHyperVVm.IsChecked == true;
            bool createNewVisible = isHyperVRestoreVisible && rbCreateNewHyperVVm.IsChecked == true;

            pnlExistingHyperVVmOptions.Visibility = attachExistingVisible ? Visibility.Visible : Visibility.Collapsed;
            pnlNewHyperVVmOptions.Visibility = createNewVisible ? Visibility.Visible : Visibility.Collapsed;

            if (createNewVisible)
            {
                ApplyDefaultNewHyperVVmSettings();
            }
        }

        private void ApplyDefaultNewHyperVVmSettings()
        {
            if (txtHyperVVirtualDiskPath == null || txtNewHyperVVmName == null || txtNewHyperVVmPath == null)
            {
                return;
            }

            string virtualDiskPath = txtHyperVVirtualDiskPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(virtualDiskPath))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(txtNewHyperVVmName.Text))
            {
                txtNewHyperVVmName.Text = RegularHyperVRestoreHelper.GetDefaultHyperVVmName(virtualDiskPath);
            }

            if (string.IsNullOrWhiteSpace(txtNewHyperVVmPath.Text))
            {
                txtNewHyperVVmPath.Text = RegularHyperVRestoreHelper.GetDefaultHyperVVmStoragePath(virtualDiskPath);
            }
        }

        private void LoadAvailableHyperVVirtualMachines()
        {
            if (cmbExistingHyperVVm == null)
            {
                return;
            }

            cmbExistingHyperVVm.Items.Clear();

            try
            {
                var buffer = new StringBuilder(32768);
                int result = BackupEngineInterop.EnumerateHyperVMachines(buffer, buffer.Capacity);
                if (result != 0)
                {
                    return;
                }

                foreach (string vm in buffer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    cmbExistingHyperVVm.Items.Add(vm.Trim());
                }

                if (cmbExistingHyperVVm.Items.Count > 0)
                {
                    cmbExistingHyperVVm.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadAvailableHyperVVirtualMachines warning: {ex.Message}");
            }
        }

        private void RestoreItems_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateSelectedRestoreTargetKind();
        }

        private void BrowseRestoreDestination_Click(object sender, RoutedEventArgs e)
        {
            // File / folder restore: browse for alternate folder
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select Restore Destination",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _selectedTargetPath = dialog.SelectedPath;
                txtFolderRestoreDestination.Text = dialog.SelectedPath;
            }
        }

        private void BrowseHyperVRestoreDirectory_Click(object sender, RoutedEventArgs e)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Select an empty directory to restore the Hyper-V virtual machine into",
                ShowNewFolderButton = true
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _selectedTargetPath = folderDialog.SelectedPath;
                txtHyperVRestoreDirectory.Text = folderDialog.SelectedPath;
            }
        }

        private void BrowseHyperVVirtualDisk_Click(object sender, RoutedEventArgs e)
        {
            using var saveDialog = new SaveFileDialog
            {
                Title = "Select Hyper-V virtual disk destination",
                Filter = "Hyper-V Virtual Disk (*.vhdx)|*.vhdx",
                DefaultExt = "vhdx",
                AddExtension = true,
                OverwritePrompt = false,
                CheckPathExists = true
            };

            if (saveDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _selectedTargetPath = saveDialog.FileName;
                txtHyperVVirtualDiskPath.Text = saveDialog.FileName;
                ApplyDefaultNewHyperVVmSettings();
            }
        }

        private void BrowseNewHyperVVmLocation_Click(object sender, RoutedEventArgs e)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Select Hyper-V virtual machine storage folder",
                ShowNewFolderButton = true
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtNewHyperVVmPath.Text = folderDialog.SelectedPath;
            }
        }

        private async void StartRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateRestore())
                return;

            // For disk and Hyper-V backups with metadata, prompt for volume/group selection
            // and let the user size each partition before confirming the restore.
            if (!await PromptVolumeSelectionAndSizingAsync())
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

        /// <summary>
        /// For multi-image disk/Hyper-V backups: shows the volume-selection dialog followed by
        /// the partition-sizing window.  Returns false if the user cancels at any step.
        /// </summary>
        private async Task<bool> PromptVolumeSelectionAndSizingAsync()
        {
            // Only needed for disk and Hyper-V restore kinds with restore metadata.
            if (_restoreTargetKind != RestoreTargetKind.Disk &&
                _restoreTargetKind != RestoreTargetKind.Volume)
            {
                return true;
            }

            if (_diskRestorePlan == null || !_diskRestorePlan.HasMetadata)
                return true;

            if (_diskRestorePlan.Volumes.Count <= 1 && !_isHyperVBackupPoint)
                return true;

            // Build VolumeInfo list from the restore plan, ordered by partition offset
            var planVolumes = _diskRestorePlan.Volumes
                .OrderBy(v => v.PartitionOffsetBytes)
                .ThenBy(v => v.PartitionNumber)
                .ToList();

            var volumes = planVolumes.Select((v, idx) => new VolumeInfo
            {
                ImageIndex            = idx + 1, // 1-based image index into the SSB archive
                Label                 = !string.IsNullOrWhiteSpace(v.SourceVolumeLabel) ? v.SourceVolumeLabel : $"Volume {v.PartitionNumber}",
                Size                  = (long)v.PartitionLengthBytes,
                UsedSpace             = 0,  // Unknown until mounted; resize window guards on Size
                PartitionNumber       = v.PartitionNumber,
                PartitionOffsetBytes  = v.PartitionOffsetBytes,
                PartitionLengthBytes  = v.PartitionLengthBytes,
                PartitionStyle        = v.PartitionStyle,
                PartitionType         = v.PartitionType,
                SourceVolumeGuidPath  = v.SourceVolumeGuidPath,
                SourceVolumeMountPath = v.SourceVolumeMountPath,
                IsBootVolume          = v.IsBootVolume,
                IsSystemVolume        = v.IsSystemVolume,
                FileSystem            = v.SourceFileSystem,
                IsResizable           = true,
                TargetSize            = (long)v.PartitionLengthBytes
            }).ToList();

            bool isDiskOrHyperV = _restoreTargetKind == RestoreTargetKind.Disk || _isHyperVBackupPoint;

            // Step 1 – volume selection dialog
            var selectionDialog = new RestoreVolumeSelectionDialog(
                volumes,
                isDiskOrHyperV,
                isDiskOrHyperV
                    ? "This backup contains multiple volumes. Select a single volume or the entire disk group to restore."
                    : "Select the volume to restore from this backup.") { Owner = this };

            if (selectionDialog.ShowDialog() != true || !selectionDialog.Confirmed)
                return false;

            IReadOnlyList<VolumeInfo> volumesToResize;
            bool isGroupRestore = selectionDialog.SelectedDiskGroup != null;

            if (isGroupRestore)
                volumesToResize = selectionDialog.SelectedDiskGroup!;
            else
                volumesToResize = new[] { selectionDialog.SelectedVolume! };

            // Step 2 – partition sizing (requires a target disk to know capacity)
            long targetDiskSizeBytes = await GetTargetDiskSizeBytesAsync();
            if (targetDiskSizeBytes <= 0)
            {
                // Can't determine target size; skip resize and proceed with original sizes
                _selectedRestoreVolume  = isGroupRestore ? null : (VolumeInfo?)selectionDialog.SelectedVolume;
                _selectedRestoreDiskGroup = isGroupRestore ? selectionDialog.SelectedDiskGroup : null;
                return true;
            }

            var sizingWindow = new VolumeConfigurationWindow(
                volumesToResize.ToList(),
                targetDiskSizeBytes,
                sourceAUS: 4096,
                targetAUS: 4096)
            { Owner = this };

            if (sizingWindow.ShowDialog() != true || sizingWindow.FinalConfiguration == null)
                return false;

            // Ordered by partition offset (preserved from the input list ordering)
            var resized = sizingWindow.FinalConfiguration
                .OrderBy(v => v.PartitionOffsetBytes)
                .ThenBy(v => v.PartitionNumber)
                .ToList();

            if (isGroupRestore)
            {
                _selectedRestoreDiskGroup = resized;
                _selectedRestoreVolume    = null;
            }
            else
            {
                _selectedRestoreVolume    = resized.FirstOrDefault();
                _selectedRestoreDiskGroup = null;
            }

            return true;
        }

        /// <summary>Returns the size of the target disk in bytes, or -1 when it cannot be determined.</summary>
        private async Task<long> GetTargetDiskSizeBytesAsync()
        {
            if (_selectedTargetDiskNumber.HasValue)
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        using var searcher = new ManagementObjectSearcher(
                            $"SELECT Size FROM Win32_DiskDrive WHERE Index = {_selectedTargetDiskNumber.Value}");
                        foreach (ManagementObject disk in searcher.Get())
                        {
                            if (long.TryParse(disk["Size"]?.ToString(), out long size))
                                return size;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"GetTargetDiskSizeBytesAsync: {ex.Message}");
                    }
                    return -1L;
                });
            }

            if (!string.IsNullOrWhiteSpace(_selectedTargetPath))
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        string drive = Path.GetPathRoot(_selectedTargetPath)?.TrimEnd('\\') ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(drive)) return -1L;

                        using var searcher = new ManagementObjectSearcher(
                            $"SELECT Size FROM Win32_LogicalDisk WHERE DeviceID = '{drive}'");
                        foreach (ManagementObject vol in searcher.Get())
                        {
                            if (long.TryParse(vol["Size"]?.ToString(), out long size))
                                return size;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"GetTargetDiskSizeBytesAsync (volume): {ex.Message}");
                    }
                    return -1L;
                });
            }

            return -1L;
        }


        private async Task PerformRestore()
        {
            var selectedPoint = lstRestorePoints.SelectedItem as RestorePoint;
            if (selectedPoint == null) return;

            var destination = _restoreTargetKind == RestoreTargetKind.FileOrFolder
                ? (rbAlternateLocation.IsChecked == true ? txtFolderRestoreDestination.Text : string.Empty)
                : (_selectedTargetPath ?? string.Empty);

            await Task.Run(() =>
            {
                using var preparedBackup = EncryptedBackupFileService.PrepareForRead(this, selectedPoint.FilePath, Path.GetFileNameWithoutExtension(selectedPoint.FilePath));
                int result;

                BackupEngineInterop.ProgressCallback callback = (percent, message) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = percent;
                        if (!string.IsNullOrWhiteSpace(message) && message.StartsWith("Restoring:", StringComparison.OrdinalIgnoreCase))
                        {
                            txtCurrentRestoreItem.Text = message;
                        }
                        else
                        {
                            txtProgress.Text = message;
                            if (!string.IsNullOrWhiteSpace(message) &&
                                !message.Contains("Restoring", StringComparison.OrdinalIgnoreCase) &&
                                !message.Contains("Processing", StringComparison.OrdinalIgnoreCase))
                            {
                                txtCurrentRestoreItem.Text = string.Empty;
                            }
                        }
                    });
                };

                switch (_restoreTargetKind)
                {
                    case RestoreTargetKind.Disk:
                        result = RestoreDiskTarget(preparedBackup.WorkingPath, selectedPoint.FilePath, callback);
                        break;

                    case RestoreTargetKind.Volume:
                        result = RestoreVolumeTarget(preparedBackup.WorkingPath, selectedPoint.FilePath, callback);
                        break;

                    case RestoreTargetKind.HyperVVm:
                        result = ReplaceOrRestoreHyperVVm(preparedBackup.WorkingPath, callback);
                        break;

                    case RestoreTargetKind.HyperVVirtualDisk:
                        RestoreToHyperVVirtualDisk(preparedBackup.WorkingPath, callback);
                        result = 0;
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

        private int RestoreDiskTarget(string preparedBackupPath, string selectedBackupPath, BackupEngineInterop.ProgressCallback callback)
        {
            PrepareDiskTarget();

            // When a disk-group was resized, restore volumes in partition-offset order
            if (_selectedRestoreDiskGroup != null && _selectedRestoreDiskGroup.Count > 0)
            {
                int lastResult = 0;
                int targetDisk = _selectedTargetDiskNumber ?? -1;
                var ordered = _selectedRestoreDiskGroup
                    .OrderBy(v => v.PartitionOffsetBytes)
                    .ThenBy(v => v.PartitionNumber)
                    .ToList();

                for (int i = 0; i < ordered.Count; i++)
                {
                    var vol = ordered[i];
                    int pct = (int)((i / (double)ordered.Count) * 90);
                    callback(pct, $"Restoring partition {i + 1} of {ordered.Count}: {vol.Label}…");
                    lastResult = BackupEngineInterop.RestoreDiskFromImage(
                        preparedBackupPath, vol.ImageIndex, targetDisk, vol.IsBootVolume || vol.IsSystemVolume, callback);
                    if (lastResult != 0)
                        return lastResult;
                }
                callback(100, "All partitions restored.");
                return 0;
            }

            if (_isHyperVBackupPoint)
            {
                using var mountedDisk = MountPrimaryHyperVVirtualDisk(selectedBackupPath);
                int mountedDiskNumber = GetDiskNumberForDriveLetter(mountedDisk.DriveRoot);
                int imageIndex = _selectedRestoreVolume?.ImageIndex ?? _diskRestorePlan?.ImageIndex ?? 1;
                return BackupEngineInterop.RestoreDiskFromImage(preparedBackupPath, imageIndex, mountedDiskNumber, false, callback);
            }

            {
                int imageIndex = _selectedRestoreVolume?.ImageIndex ?? _diskRestorePlan?.ImageIndex ?? -1;
                return _diskRestorePlan?.HasMetadata == true
                    ? BackupEngineInterop.RestoreDiskFromImage(preparedBackupPath, imageIndex, _selectedTargetDiskNumber ?? -1, false, callback)
                    : BackupEngineInterop.RestoreDisk(preparedBackupPath, _selectedTargetDiskNumber ?? -1, false, callback);
            }
        }

        private int RestoreVolumeTarget(string preparedBackupPath, string selectedBackupPath, BackupEngineInterop.ProgressCallback callback)
        {
            PrepareVolumeTarget();

            string targetPath = _selectedTargetPath ?? string.Empty;

            if (_isHyperVBackupPoint)
            {
                using var mountedDisk = MountPrimaryHyperVVirtualDisk(selectedBackupPath);
                targetPath = mountedDisk.DriveRoot;
                int imageIndex = _selectedRestoreVolume?.ImageIndex ?? _diskRestorePlan?.ImageIndex ?? 1;
                return BackupEngineInterop.RestoreVolumeFromImage(preparedBackupPath, imageIndex, targetPath, false, callback);
            }

            {
                int imageIndex = _selectedRestoreVolume?.ImageIndex ?? _diskRestorePlan?.ImageIndex ?? -1;
                return _diskRestorePlan?.HasMetadata == true
                    ? BackupEngineInterop.RestoreVolumeFromImage(preparedBackupPath, imageIndex, targetPath, false, callback)
                    : BackupEngineInterop.RestoreVolume(preparedBackupPath, targetPath, false, callback);
            }
        }

        private bool ValidateRestore()
        {
            if (lstRestorePoints.SelectedItem == null)
            {
                MessageBox.Show("Please select a restore point.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (_restoreTargetKind == RestoreTargetKind.FileOrFolder)
            {
                if (rbAlternateLocation.IsChecked == true && string.IsNullOrWhiteSpace(txtFolderRestoreDestination.Text))
                {
                    MessageBox.Show("Please select a restore destination folder.", "Validation Error",
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
                    string destinationRoot = (_selectedTargetPath ?? txtFolderRestoreDestination.Text);
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
            }

            UpdateSelectedRestoreTargetKind();

            if (_restoreTargetKind == RestoreTargetKind.Disk && !_selectedTargetDiskNumber.HasValue)
            {
                MessageBox.Show("Please select the target disk to restore to.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (_restoreTargetKind == RestoreTargetKind.HyperVVm)
            {
                bool isReplaceMode = rbHyperVReplaceExisting?.IsChecked == true;
                if (isReplaceMode)
                {
                    if (cmbHyperVVmToReplace.SelectedItem == null || string.IsNullOrWhiteSpace(cmbHyperVVmToReplace.SelectedItem.ToString()))
                    {
                        MessageBox.Show("Please select a non-running Hyper-V virtual machine to replace.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(txtHyperVRestoreDirectory?.Text))
                    {
                        MessageBox.Show("Please select an empty directory to restore the Hyper-V virtual machine into.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }

                    string restoreDir = txtHyperVRestoreDirectory.Text.Trim();
                    if (Directory.Exists(restoreDir) && Directory.EnumerateFileSystemEntries(restoreDir).Any())
                    {
                        MessageBox.Show("The selected restore directory is not empty. Please choose an empty directory.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }

                if (string.IsNullOrWhiteSpace(txtHyperVVmName.Text))
                {
                    MessageBox.Show("Please enter a virtual machine name for the restored VM.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            if (_restoreTargetKind == RestoreTargetKind.HyperVVirtualDisk)
            {
                if (string.IsNullOrWhiteSpace(txtHyperVVirtualDiskPath.Text))
                {
                    MessageBox.Show("Please select the Hyper-V virtual disk file to create or update.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (chkAttachToExistingHyperVVm.IsChecked == true)
                {
                    string selectedVm = cmbExistingHyperVVm.SelectedItem?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(selectedVm))
                    {
                        MessageBox.Show("Please select the existing Hyper-V virtual machine that should receive the restored virtual disk.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }

                if (rbCreateNewHyperVVm.IsChecked == true)
                {
                    if (string.IsNullOrWhiteSpace(txtNewHyperVVmName.Text))
                    {
                        MessageBox.Show("Please enter the name for the new Hyper-V virtual machine.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(txtNewHyperVVmPath.Text))
                    {
                        MessageBox.Show("Please select the storage folder for the new Hyper-V virtual machine.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
            }

            if (_restoreTargetKind == RestoreTargetKind.Volume && string.IsNullOrWhiteSpace(_selectedTargetPath))
            {
                MessageBox.Show("Please select the target volume to restore to.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (_isHyperVBackupPoint && (_restoreTargetKind == RestoreTargetKind.Disk || _restoreTargetKind == RestoreTargetKind.Volume) && HyperVRestorePointHelper.FindPrimaryVirtualDisk(((RestorePoint)lstRestorePoints.SelectedItem!).FilePath) == null)
            {
                MessageBox.Show("The selected Hyper-V backup point does not contain a guest VHD or VHDX file that can be restored to a disk or volume target.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (_diskRestorePlan?.HasMetadata == true)
            {
                txtDestinationHelp.Text = $"Disk reconstruction plan loaded for Disk {_diskRestorePlan.SourceDiskNumber}. The target layout will be rebuilt from metadata and user-selected partition sizing.";
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

            if (_isHyperVBackupPoint && cmbHyperVRestoreTarget?.SelectedItem is ComboBoxItem selectedRestoreMode)
            {
                string restoreMode = selectedRestoreMode.Tag?.ToString() ?? "Files";
                _restoreTargetKind = restoreMode switch
                {
                    "Disk" => RestoreTargetKind.Disk,
                    "Volume" => RestoreTargetKind.Volume,
                    "HyperVVm" => RestoreTargetKind.HyperVVm,
                    _ => RestoreTargetKind.FileOrFolder
                };

                UpdateHyperVRestoreMode();
                UpdateDestinationHelpText();
                return;
            }

            if (!_isHyperVBackupPoint && chkRestoreToHyperVDisk?.IsChecked == true)
            {
                _restoreTargetKind = RestoreTargetKind.HyperVVirtualDisk;
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
            UpdateLocationPanelVisibility();
        }

        private void UpdateDestinationHelpText()
        {
            if (txtDestinationHelp == null)
            {
                return;
            }

            string hyperVVmSubText = string.Empty;
            if (_restoreTargetKind == RestoreTargetKind.HyperVVm)
            {
                hyperVVmSubText = rbHyperVReplaceExisting?.IsChecked == true
                    ? " Select a non-running virtual machine to replace."
                    : " Select an empty directory as the restore destination."; 
            }

            txtDestinationHelp.Text = _restoreTargetKind switch
            {
                RestoreTargetKind.Disk => "Disk restore: choose a target hard drive. It will be formatted and repartitioned. The currently booted system disk cannot be selected.",
                RestoreTargetKind.Volume => "Volume restore: choose a target volume. It will be formatted before restore. Boot/system volumes are excluded.",
                RestoreTargetKind.HyperVVm => $"Hyper-V VM restore: import the selected Hyper-V backup as a virtual machine.{hyperVVmSubText}",
                RestoreTargetKind.HyperVVirtualDisk => "Hyper-V virtual disk restore: write the restored backup into a .vhdx file, then optionally attach that disk to an existing Hyper-V VM.",
                _ => "File/folder restore: choose a destination folder, or restore to the original location if allowed."
            };
        }

        /// <summary>
        /// Handles Hyper-V system restore: either replaces an existing non-running VM or
        /// imports the backup into an empty directory as a new VM.
        /// </summary>
        private int ReplaceOrRestoreHyperVVm(string preparedBackupPath, BackupEngineInterop.ProgressCallback callback)
        {
            string vmName = txtHyperVVmName.Text.Trim();
            bool isReplaceMode = rbHyperVReplaceExisting?.IsChecked == true;

            if (isReplaceMode)
            {
                string targetVmName = cmbHyperVVmToReplace.SelectedItem?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(targetVmName))
                    throw new InvalidOperationException("No target Hyper-V virtual machine was selected for replacement.");

                callback(5, $"Removing existing Hyper-V virtual machine '{targetVmName}'...");
                RemoveHyperVVm(targetVmName);

                // Resolve the VM's storage path before removal for the import destination
                string vmStoragePath = GetHyperVVmStoragePath(targetVmName) ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Hyper-V", "Virtual Machines");

                callback(15, $"Importing Hyper-V backup as '{vmName}'...");
                return BackupEngineInterop.RestoreHyperVVM(
                    preparedBackupPath,
                    string.IsNullOrWhiteSpace(vmName) ? targetVmName : vmName,
                    vmStoragePath,
                    chkStartHyperVVm.IsChecked == true,
                    callback);
            }
            else
            {
                string restoreDir = txtHyperVRestoreDirectory.Text.Trim();
                if (string.IsNullOrWhiteSpace(restoreDir))
                    throw new InvalidOperationException("No restore directory was specified.");

                Directory.CreateDirectory(restoreDir);
                callback(5, $"Restoring Hyper-V virtual machine to '{restoreDir}'...");
                return BackupEngineInterop.RestoreHyperVVM(
                    preparedBackupPath,
                    vmName,
                    restoreDir,
                    chkStartHyperVVm.IsChecked == true,
                    callback);
            }
        }

        private static void RemoveHyperVVm(string vmName)
        {
            string escaped = vmName.Replace("'", "''");
            string script = $"Remove-VM -Name '{escaped}' -Force -ErrorAction Stop";
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            string errors = process?.StandardError.ReadToEnd() ?? string.Empty;
            process?.WaitForExit();
            if (process == null || process.ExitCode != 0)
                throw new InvalidOperationException($"Failed to remove the existing Hyper-V virtual machine. {errors}".Trim());
        }

        private static string? GetHyperVVmStoragePath(string vmName)
        {
            try
            {
                string escaped = vmName.Replace("'", "''");
                string script = $"(Get-VM -Name '{escaped}' -ErrorAction SilentlyContinue).Path";
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                string output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
                process?.WaitForExit();
                string path = output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
                return string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
            }
            catch
            {
                return null;
            }
        }

        private void RestoreToHyperVVirtualDisk(string preparedBackupPath, BackupEngineInterop.ProgressCallback callback)
        {
            string virtualDiskPath = txtHyperVVirtualDiskPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(virtualDiskPath))
            {
                throw new InvalidOperationException("No Hyper-V virtual disk path was selected.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(virtualDiskPath) ?? throw new InvalidOperationException("Invalid Hyper-V virtual disk path."));

            string selectedItemText = lstBackupItems.SelectedItem?.ToString() ?? string.Empty;
            bool restoreAsDisk = selectedItemText.Contains("PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase);
            bool restoreAsVolume = !restoreAsDisk && (selectedItemText.StartsWith("\\?\\", StringComparison.OrdinalIgnoreCase) ||
                selectedItemText.EndsWith(":\\", StringComparison.OrdinalIgnoreCase) ||
                selectedItemText.EndsWith(":", StringComparison.OrdinalIgnoreCase));

            callback(5, "Creating or preparing Hyper-V virtual disk...");
            PrepareHyperVVirtualDiskFile(virtualDiskPath, restoreAsDisk);

            callback(10, "Mounting Hyper-V virtual disk...");
            var mountResult = BackupMountManager.MountVirtualDisk(virtualDiskPath, readOnly: false);
            if (!mountResult.Success || string.IsNullOrWhiteSpace(mountResult.DriveLetter))
            {
                throw new InvalidOperationException($"Failed to mount the Hyper-V virtual disk: {mountResult.Error}");
            }

            string mountedDriveRoot = mountResult.DriveLetter.EndsWith(":", StringComparison.Ordinal)
                ? mountResult.DriveLetter + "\\"
                : mountResult.DriveLetter;

            try
            {
                string targetVolumePath;
                if (restoreAsDisk)
                {
                    callback(12, "Preparing virtual disk volume layout...");
                    targetVolumePath = CreateVolumeOnDiskForHyperVRestore(GetDiskNumberForDriveLetter(mountedDriveRoot));
                }
                else
                {
                    targetVolumePath = mountedDriveRoot;
                }

                int result = restoreAsDisk
                    ? (_diskRestorePlan?.HasMetadata == true
                        ? BackupEngineInterop.RestoreDiskFromImage(preparedBackupPath, _diskRestorePlan.ImageIndex, GetDiskNumberForDriveLetter(mountedDriveRoot), false, callback)
                        : BackupEngineInterop.RestoreDisk(preparedBackupPath, GetDiskNumberForDriveLetter(mountedDriveRoot), false, callback))
                    : (restoreAsVolume
                        ? (_diskRestorePlan?.HasMetadata == true
                            ? BackupEngineInterop.RestoreVolumeFromImage(preparedBackupPath, _diskRestorePlan.ImageIndex, targetVolumePath, false, callback)
                            : BackupEngineInterop.RestoreVolume(preparedBackupPath, targetVolumePath, false, callback))
                        : BackupEngineInterop.RestoreFiles(preparedBackupPath, targetVolumePath, chkOverwrite.IsChecked == true, callback));

                if (result != 0)
                {
                    var error = new StringBuilder(1024);
                    BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
                    throw new InvalidOperationException($"Restore to Hyper-V virtual disk failed: {error}");
                }
            }
            finally
            {
                BackupMountManager.UnmountVirtualDisk(virtualDiskPath);
            }

            if (chkAttachToExistingHyperVVm.IsChecked == true)
            {
                string vmName = RegularHyperVRestoreHelper.NormalizeHyperVVmName(cmbExistingHyperVVm.SelectedItem?.ToString() ?? string.Empty);
                if (string.IsNullOrWhiteSpace(vmName))
                {
                    throw new InvalidOperationException("No Hyper-V virtual machine was selected for disk attachment.");
                }

                callback(95, $"Attaching restored virtual disk to Hyper-V VM '{vmName}'...");
                AttachVirtualDiskToExistingHyperVVm(vmName, virtualDiskPath);
            }
            else if (rbCreateNewHyperVVm.IsChecked == true)
            {
                string vmName = txtNewHyperVVmName.Text.Trim();
                string vmStoragePath = txtNewHyperVVmPath.Text.Trim();
                int generation = GetSelectedNewHyperVVmGeneration();

                callback(95, $"Creating Hyper-V VM '{vmName}'...");
                CreateNewHyperVVm(vmName, vmStoragePath, virtualDiskPath, generation, chkStartCreatedHyperVVm.IsChecked == true);
            }
        }

        private int GetSelectedNewHyperVVmGeneration()
        {
            if (cmbNewHyperVGeneration?.SelectedItem is ComboBoxItem selectedItem &&
                int.TryParse(selectedItem.Tag?.ToString(), out int generation))
            {
                return generation;
            }

            return 2;
        }

        private static void PrepareHyperVVirtualDiskFile(string virtualDiskPath, bool createFixedDisk)
        {
            string diskType = createFixedDisk ? "Fixed" : "Dynamic";
            long sizeBytes = createFixedDisk ? 137438953472L : 68719476736L;
            string script = $"$path='{virtualDiskPath.Replace("'", "''")}'; if (Test-Path $path) {{ Dismount-DiskImage -ImagePath $path -ErrorAction SilentlyContinue; }} else {{ New-VHD -Path $path -SizeBytes {sizeBytes} -{diskType} | Out-Null; }}";

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });

            string errors = process?.StandardError.ReadToEnd() ?? string.Empty;
            process?.WaitForExit();

            if (process == null || process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to prepare the Hyper-V virtual disk file. {errors}".Trim());
            }
        }

        private static int GetDiskNumberForDriveLetter(string driveRoot)
        {
            string normalizedDrive = driveRoot.TrimEnd('\\');
            string script = $"$partition = Get-Partition -DriveLetter '{normalizedDrive[0]}' -ErrorAction Stop; $partition.DiskNumber";

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            string output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
            string errors = process?.StandardError.ReadToEnd() ?? string.Empty;
            process?.WaitForExit();

            if (process == null || process.ExitCode != 0 || !int.TryParse(output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault(), out int diskNumber))
            {
                throw new InvalidOperationException($"Failed to resolve the mounted Hyper-V virtual disk number. {errors}".Trim());
            }

            return diskNumber;
        }

        private static void AttachVirtualDiskToExistingHyperVVm(string vmName, string virtualDiskPath)
        {
            string escapedVmName = vmName.Replace("'", "''");
            string escapedDiskPath = virtualDiskPath.Replace("'", "''");
            string script = $"Add-VMHardDiskDrive -VMName '{escapedVmName}' -Path '{escapedDiskPath}' -ErrorAction Stop";

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            string errors = process?.StandardError.ReadToEnd() ?? string.Empty;
            process?.WaitForExit();

            if (process == null || process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to attach the restored virtual disk to the selected Hyper-V virtual machine. {errors}".Trim());
            }
        }

        private static void CreateNewHyperVVm(string vmName, string vmStoragePath, string virtualDiskPath, int generation, bool startAfterCreate)
        {
            string script = RegularHyperVRestoreHelper.BuildCreateVirtualMachineScript(vmName, vmStoragePath, virtualDiskPath, generation, startAfterCreate);

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            string errors = process?.StandardError.ReadToEnd() ?? string.Empty;
            process?.WaitForExit();

            if (process == null || process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to create the new Hyper-V virtual machine. {errors}".Trim());
            }
        }

        // -----------------------------------------------------------------------
        //  Restore target drive tree
        // -----------------------------------------------------------------------

        private async void RefreshRestoreTarget_Click(object sender, RoutedEventArgs e)
        {
            await LoadRestoreTargetDrivesAsync();
        }

        /// <summary>
        /// Populates the restore-target tree with physical disks and their volumes.
        /// Boot/system disk items are shown greyed-out and unselectable.
        /// </summary>
        private async Task LoadRestoreTargetDrivesAsync()
        {
            loadingTargetOverlay.Visibility = Visibility.Visible;
            treeViewRestoreTarget.Items.Clear();
            _restoreTargetItems.Clear();
            _selectedTargetPath = null;
            _selectedTargetDiskNumber = null;
            txtSelectedTargetLabel.Text = "No target selected";

            try
            {
                var protectedIndexes = GetProtectedDiskIndexes();
                bool diskMode = _restoreTargetKind == RestoreTargetKind.Disk;

                await Task.Run(() =>
                {
                    try
                    {
                        using var diskSearcher = new ManagementObjectSearcher(
                            "SELECT Index, Model, Size FROM Win32_DiskDrive ORDER BY Index");

                        foreach (ManagementObject disk in diskSearcher.Get())
                        {
                            if (!int.TryParse(disk["Index"]?.ToString(), out int diskIdx))
                                continue;

                            bool isProtected = protectedIndexes.Contains(diskIdx);
                            long diskBytes = long.TryParse(disk["Size"]?.ToString(), out long db) ? db : 0;
                            double diskGb = diskBytes / (1024.0 * 1024.0 * 1024.0);
                            string model = disk["Model"]?.ToString()?.Trim() ?? $"Disk {diskIdx}";
                            string diskLabel = $"Disk {diskIdx}:  {model}  ({diskGb:F1} GB){(isProtected ? "  [Boot — cannot restore]" : string.Empty)}";

                            var diskItem = new DriveTreeItem
                            {
                                Name = diskLabel,
                                FullPath = $"\\\\.\\PHYSICALDRIVE{diskIdx}",
                                ItemType = DriveTreeItemType.Disk,
                                PartitionNumber = diskIdx,
                                Size = diskBytes,
                                IsBootVolume = isProtected
                            };

                            // Load child volumes via WMI association
                            using var partSearcher = new ManagementObjectSearcher(
                                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='\\\\\\\\.\\\\PHYSICALDRIVE{diskIdx}'}} " +
                                "WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                            foreach (ManagementObject partition in partSearcher.Get())
                            {
                                using var logicalSearcher = new ManagementObjectSearcher(
                                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} " +
                                    "WHERE AssocClass=Win32_LogicalDiskToPartition");

                                foreach (ManagementObject logical in logicalSearcher.Get())
                                {
                                    string? driveLetter = logical["DeviceID"]?.ToString();
                                    if (string.IsNullOrWhiteSpace(driveLetter))
                                        continue;

                                    long volBytes = long.TryParse(logical["Size"]?.ToString(), out long vb) ? vb : 0;
                                    double volGb = volBytes / (1024.0 * 1024.0 * 1024.0);
                                    string volLabel = logical["VolumeName"]?.ToString()?.Trim() ?? "Local Disk";
                                    string volDisplay = $"{driveLetter}\\  ({volLabel})  {volGb:F1} GB{(isProtected ? "  [Boot]" : string.Empty)}";

                                    diskItem.Children.Add(new DriveTreeItem
                                    {
                                        Name = volDisplay,
                                        FullPath = driveLetter + "\\",
                                        ItemType = DriveTreeItemType.Volume,
                                        Size = volBytes,
                                        IsBootVolume = isProtected,
                                        Parent = diskItem,
                                        PartitionNumber = diskIdx
                                    });
                                }
                            }

                            Dispatcher.Invoke(() =>
                            {
                                _restoreTargetItems.Add(diskItem);
                                var tvi = CreateRestoreTargetTreeItem(diskItem, diskMode, isProtected);
                                treeViewRestoreTarget.Items.Add(tvi);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                            Debug.WriteLine($"LoadRestoreTargetDrivesAsync inner: {ex.Message}"));
                    }
                });
            }
            finally
            {
                loadingTargetOverlay.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Creates a tree view item for the restore-target tree.
        /// Both disk and volume items are selectable unless boot-protected.
        /// Boot/protected items are greyed and not selectable.
        /// </summary>
        private TreeViewItem CreateRestoreTargetTreeItem(DriveTreeItem item, bool diskMode, bool isProtected)
        {
            var tvi = new TreeViewItem { IsExpanded = true };

            var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

            // Disks are selectable in disk-restore mode; volumes are always selectable
            // (they cover both volume-restore and file/folder "pick a drive" modes).
            bool isSelectable = !isProtected &&
                (item.ItemType == DriveTreeItemType.Volume ||
                 (diskMode && item.ItemType == DriveTreeItemType.Disk));

            var rb = new System.Windows.Controls.RadioButton
            {
                GroupName = "RestoreTarget",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = isSelectable,
                Visibility = (item.ItemType == DriveTreeItemType.Disk || item.ItemType == DriveTreeItemType.Volume)
                    ? Visibility.Visible : Visibility.Collapsed
            };

            rb.Checked += (s, e) => OnRestoreTargetSelected(item);

            var txt = new TextBlock
            {
                Text = item.Name,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (!isSelectable && (item.ItemType == DriveTreeItemType.Disk || item.ItemType == DriveTreeItemType.Volume))
                    ? System.Windows.SystemColors.GrayTextBrush
                    : System.Windows.SystemColors.ControlTextBrush
            };

            panel.Children.Add(rb);
            panel.Children.Add(txt);
            tvi.Header = panel;

            foreach (var child in item.Children)
            {
                tvi.Items.Add(CreateRestoreTargetTreeItem(child, diskMode, child.IsBootVolume));
            }

            return tvi;
        }

        private void OnRestoreTargetSelected(DriveTreeItem item)
        {
            _selectedTargetPath = item.FullPath;
            _selectedTargetDiskNumber = item.ItemType == DriveTreeItemType.Disk
                ? item.PartitionNumber
                : item.Parent?.PartitionNumber;

            txtSelectedTargetLabel.Text = $"Selected: {item.Name.Split(new[] { "  [" }, StringSplitOptions.None)[0].Trim()}";

            // For file/folder restores the tree acts as a drive picker for the alternate-location
            // path: auto-check Alternate Location and pre-fill the destination text box so the
            // existing validation and restore code picks it up without any extra changes.
            if (_restoreTargetKind == RestoreTargetKind.FileOrFolder
                && rbAlternateLocation != null
                && txtFolderRestoreDestination != null)
            {
                rbAlternateLocation.IsChecked = true;
                txtFolderRestoreDestination.Text = item.FullPath;
            }
        }

        private List<int> GetProtectedDiskIndexes()
        {
            var indexes = new List<int>();
            try
            {
                // Find which disk number backs the currently booted OS partition via WMI.
                // Win32_DiskPartition.BootPartition identifies the active boot partition; from there
                // we walk the association to Win32_DiskDrive to get the physical disk Index.
                using var partSearcher = new ManagementObjectSearcher(
                    "SELECT DiskIndex FROM Win32_DiskPartition WHERE BootPartition = TRUE");

                foreach (ManagementObject part in partSearcher.Get())
                {
                    if (int.TryParse(part["DiskIndex"]?.ToString(), out int diskIdx))
                    {
                        if (!indexes.Contains(diskIdx))
                            indexes.Add(diskIdx);
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
