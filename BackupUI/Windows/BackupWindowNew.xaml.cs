using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using SecureServerBackupCommon;
using SecureServerBackup.Models;
using SecureServerBackup.Services;
using MessageBox = System.Windows.MessageBox;

namespace SecureServerBackup.Windows
{
    public partial class BackupWindowNew : Window
    {
        public sealed record HyperVVirtualDiskInfo(string VirtualMachineName, string VirtualMachineDisplayName, string VirtualDiskPath);

        public static class HyperVBackupTreeHelper
        {
            public static IReadOnlyList<HyperVVirtualDiskInfo> ParseVirtualDiskEnumeration(string? output)
            {
                if (string.IsNullOrWhiteSpace(output))
                {
                    return Array.Empty<HyperVVirtualDiskInfo>();
                }

                List<HyperVVirtualDiskInfo> disks = new();
                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 3)
                    {
                        continue;
                    }

                    string vmName = RestoreWindowNew.RegularHyperVRestoreHelper.NormalizeHyperVVmName(parts[0].Trim());
                    string vmDisplayName = string.IsNullOrWhiteSpace(parts[1]) ? vmName : parts[1].Trim();
                    string virtualDiskPath = parts[2].Trim().Trim('"');

                    if (string.IsNullOrWhiteSpace(vmName) || string.IsNullOrWhiteSpace(virtualDiskPath))
                    {
                        continue;
                    }

                    disks.Add(new HyperVVirtualDiskInfo(vmName, vmDisplayName, virtualDiskPath));
                }

                return disks;
            }
        }

        private const double DefaultWindowHeight = 850;
        private const double EncryptionExpandedWindowHeight = 980;

        private ObservableCollection<DriveTreeItem> driveItems = new();
        private readonly JobManager jobManager = new();
        private BackupJob? existingJob = null;
        private BackupJob? _editingJob = null;  // Track job being edited
        private List<string>? _pathsToPreselect = null;  // Paths to pre-select after tree loads
        private List<string>? _tempUserExclusions = null;  // Temporary storage for exclusions before job created
        private bool _hasSavedEncryptionPassword;
        private bool _isUpdatingEncryptionPasswordDisplay;
        private string? _decryptedEncryptionPassword;
        private bool _windowHeightWasAutoAdjusted;
        private readonly Dictionary<string, List<string>> _hyperVDiskMountDirectories = new(StringComparer.OrdinalIgnoreCase);
        private string? _hyperVGuestMountRoot;

        private sealed record MountedHyperVGuestExecutionPartition(int PartitionNumber, string MountPath, bool CreatedMountDirectory);

        private sealed class MountedHyperVGuestExecutionDisk : IDisposable
        {
            private readonly List<MountedHyperVGuestExecutionPartition> _partitions;
            private readonly string _mountRoot;

            public MountedHyperVGuestExecutionDisk(string virtualDiskPath, string mountRoot, List<MountedHyperVGuestExecutionPartition> partitions)
            {
                VirtualDiskPath = virtualDiskPath;
                _mountRoot = mountRoot;
                _partitions = partitions;
            }

            public string VirtualDiskPath { get; }

            public IReadOnlyList<MountedHyperVGuestExecutionPartition> Partitions => _partitions;

            public void Dispose()
            {
                try
                {
                    RunPowerShell($"Dismount-DiskImage -ImagePath '{EscapePowerShellSingleQuotedString(VirtualDiskPath)}' -ErrorAction SilentlyContinue | Out-Null");
                }
                catch
                {
                }

                foreach (MountedHyperVGuestExecutionPartition partition in _partitions.Where(partition => partition.CreatedMountDirectory))
                {
                    try
                    {
                        if (Directory.Exists(partition.MountPath))
                        {
                            Directory.Delete(partition.MountPath, recursive: true);
                        }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    if (Directory.Exists(_mountRoot))
                    {
                        Directory.Delete(_mountRoot, recursive: true);
                    }
                }
                catch
                {
                }
            }
        }

        // Volume configuration tracking
        private bool hasSourceSelected = false;
        private bool hasTargetSelected = false;
        private bool volumeConfigShown = false;

        public BackupWindowNew()
        {
            InitializeWindow();
        }

        public BackupWindowNew(BackupJob job)
        {
            existingJob = job;
            InitializeWindow();
            LoadJobData(job);
        }

        private void InitializeWindow()
        {
            try
            {
                InitializeComponent();
                InitializeScheduleControls();
                AdjustWindowHeightForEncryption();
                
                // Load drives after window is fully loaded
                Loaded += BackupWindowNew_Loaded;
                Closed += BackupWindowNew_Closed;
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError($"Error initializing backup window: {ex.Message}\n\nStack Trace: {ex.StackTrace}", 
                    "Initialization Error");
            }
        }

        private void BackupWindowNew_Closed(object? sender, EventArgs e)
        {
            CleanupHyperVGuestMounts();
        }

        private void LoadJobData(BackupJob job)
        {
            // Set window title
            this.Title = $"Edit Backup - {job.Name}";

            // Store reference to job being edited
            _editingJob = job;

            // Load basic info
            txtBackupName.Text = job.Name;
            txtDestination.Text = job.DestinationPath;

            // Update exclusions button text if exclusions exist
            if (job.UserExclusions != null && job.UserExclusions.Count > 0)
            {
                btnManageExclusions.Content = $"Manage Exclusions... ({job.UserExclusions.Count})";
            }

            // Set backup type
            switch (job.Type)
            {
                case BackupType.Full:
                    rbFullBackup.IsChecked = true;
                    break;
                case BackupType.Incremental:
                    rbIncremental.IsChecked = true;
                    break;
                case BackupType.Differential:
                    rbDifferential.IsChecked = true;
                    break;
                case BackupType.CloneToDisk:
                    rbCloneDisk.IsChecked = true;
                    break;
                case BackupType.CloneToVirtualDisk:
                    rbCloneVirtual.IsChecked = true;
                    break;
                case BackupType.CloneHyperVSystem:
                    rbCloneHyperV.IsChecked = true;
                    break;
            }

            // Set options
            chkCompress.IsChecked = job.CompressData;
            chkVerify.IsChecked = job.VerifyAfterBackup;
            txtRetainCount.Text = job.RetainFullBackupCount.ToString();
            chkEncryptBackup.IsChecked = job.EncryptBackup;

            _hasSavedEncryptionPassword = job.EncryptBackup && !string.IsNullOrWhiteSpace(job.ProtectedEncryptionPassword);
            UpdateEncryptionUiState();

            if (_hasSavedEncryptionPassword)
            {
                pwdEncryptionPassword.Password = "********";
                txtEncryptionPasswordVisible.Text = "********";
                pnlVerifyEncryptionPassword.Visibility = Visibility.Collapsed;
            }

            // Show retention settings if Full backup type
            if (pnlRetentionSettings != null)
            {
                pnlRetentionSettings.Visibility = job.Type == BackupType.Full ? Visibility.Visible : Visibility.Collapsed;
            }

            // Store job data for pre-selection after tree loads
            if (job.Target == BackupTarget.Disk || job.Target == BackupTarget.Volume || job.Target == BackupTarget.FilesAndFolders)
            {
                // Pre-selection will happen in BackupWindowNew_Loaded after drives are loaded
                // Store the paths to select
                _pathsToPreselect = new List<string>(job.SourcePaths);
            }
            else if (job.IsHyperVBackup)
            {
                _pathsToPreselect = new List<string>(job.HyperVMachines);
            }

            // Load schedule
            if (job.Schedule != null)
            {
                chkEnableSchedule.IsChecked = job.Schedule.Enabled;
                cmbFrequency.SelectedIndex = (int)job.Schedule.Frequency;
                
                // Convert 24-hour to 12-hour format with AM/PM
                int hour24 = job.Schedule.Time.Hours;
                int hour12;
                string ampm;
                
                if (hour24 == 0)
                {
                    hour12 = 12;
                    ampm = "AM";
                }
                else if (hour24 < 12)
                {
                    hour12 = hour24;
                    ampm = "AM";
                }
                else if (hour24 == 12)
                {
                    hour12 = 12;
                    ampm = "PM";
                }
                else
                {
                    hour12 = hour24 - 12;
                    ampm = "PM";
                }
                
                cmbHour.SelectedItem = hour12.ToString();
                cmbMinute.SelectedItem = job.Schedule.Time.Minutes.ToString("D2");
                cmbAmPm.SelectedIndex = ampm == "AM" ? 0 : 1;

                if (job.Schedule.Frequency == ScheduleFrequency.Monthly)
                {
                    cmbDayOfMonth.SelectedItem = job.Schedule.DayOfMonth;
                }
                else if (job.Schedule.Frequency == ScheduleFrequency.Weekly)
                {
                    chkMonday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Monday);
                    chkTuesday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Tuesday);
                    chkWednesday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Wednesday);
                    chkThursday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Thursday);
                    chkFriday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Friday);
                    chkSaturday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Saturday);
                    chkSunday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Sunday);
                }
            }
        }

        private async void BackupWindowNew_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadDrives();
                
                // Pre-select items if editing a job
                if (_pathsToPreselect != null && _pathsToPreselect.Count > 0)
                {
                    PreSelectItems(_pathsToPreselect);
                }

                // Update retention settings visibility based on initially selected backup type
                // This ensures the panel shows correctly when Full Backup is preselected
                if (pnlRetentionSettings != null)
                {
                    bool isFullBackup = rbFullBackup?.IsChecked == true;
                    pnlRetentionSettings.Visibility = isFullBackup ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError($"Error loading drives: {ex.Message}\n\nStack Trace: {ex.StackTrace}", 
                    "Error");
            }
        }

        /// <summary>
        /// Pre-selects items in the tree based on saved paths
        /// </summary>
        private void PreSelectItems(List<string> pathsToSelect)
        {
            foreach (var path in pathsToSelect)
            {
                if (HyperVGuestSelectionPath.TryParse(path, out HyperVGuestSelectionInfo? guestSelection) && guestSelection != null)
                {
                    PreSelectHyperVGuestItem(guestSelection);
                }
                else
                {
                    PreSelectItemRecursive(driveItems, path);
                }
            }
        }

        private void PreSelectHyperVGuestItem(HyperVGuestSelectionInfo selection)
        {
            DriveTreeItem? hyperVSystem = driveItems.FirstOrDefault(item =>
                item.ItemType == DriveTreeItemType.HyperVSystem &&
                string.Equals(item.VirtualMachineName, selection.VirtualMachineName, StringComparison.OrdinalIgnoreCase));
            if (hyperVSystem == null)
            {
                return;
            }

            DriveTreeItem? virtualDiskItem = hyperVSystem.Children.FirstOrDefault(item =>
                item.ItemType == DriveTreeItemType.HyperVVirtualDisk &&
                string.Equals(item.VirtualDiskPath, selection.VirtualDiskPath, StringComparison.OrdinalIgnoreCase));
            if (virtualDiskItem == null)
            {
                return;
            }

            if (selection.Kind == HyperVGuestSelectionKind.VirtualDisk)
            {
                virtualDiskItem.IsChecked = true;
                return;
            }

            if (!virtualDiskItem.ChildrenLoaded)
            {
                LoadHyperVVirtualDiskChildren(virtualDiskItem);
                virtualDiskItem.ChildrenLoaded = true;
            }

            DriveTreeItem? volumeItem = virtualDiskItem.Children.FirstOrDefault(item =>
                item.ItemType == DriveTreeItemType.HyperVVolume &&
                item.PartitionNumber == selection.PartitionNumber);
            if (volumeItem == null)
            {
                return;
            }

            if (selection.Kind == HyperVGuestSelectionKind.Volume || string.IsNullOrWhiteSpace(selection.RelativePath))
            {
                volumeItem.IsChecked = true;
                return;
            }

            DriveTreeItem current = volumeItem;
            foreach (string segment in selection.RelativePath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!current.ChildrenLoaded)
                {
                    LoadFoldersForVolume(current);
                    current.ChildrenLoaded = true;
                }

                DriveTreeItem? next = current.Children.FirstOrDefault(item =>
                    item.ItemType == DriveTreeItemType.Folder &&
                    item.ResolvedPath.EndsWith(segment, StringComparison.OrdinalIgnoreCase));
                if (next == null)
                {
                    return;
                }

                current = next;
            }

            current.IsChecked = true;
        }

        /// <summary>
        /// Recursively searches tree and selects matching items
        /// </summary>
        private bool PreSelectItemRecursive(IEnumerable<DriveTreeItem> items, string pathToSelect)
        {
            foreach (var item in items)
            {
                // Normalize paths for comparison
                var itemPath = item.FullPath?.TrimEnd('\\');
                var targetPath = pathToSelect?.TrimEnd('\\');
                
                if (string.Equals(itemPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.IsChecked = true;
                    return true;
                }
                
                
                // Check children
                if (item.Children.Count > 0 && !string.IsNullOrEmpty(pathToSelect))
                {
                    if (PreSelectItemRecursive(item.Children, pathToSelect))
                    {
                        // Parent should be partially checked if child is selected
                        return true;
                    }
                }
            }
            
            return false;
        }

        private string EnsureHyperVGuestMountRoot()
        {
            if (!string.IsNullOrWhiteSpace(_hyperVGuestMountRoot))
            {
                Directory.CreateDirectory(_hyperVGuestMountRoot);
                return _hyperVGuestMountRoot;
            }

            _hyperVGuestMountRoot = Path.Combine(Path.GetTempPath(), "SecureServerBackup", "HyperVGuestMounts", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_hyperVGuestMountRoot);
            return _hyperVGuestMountRoot;
        }

        private void CleanupHyperVGuestMounts()
        {
            foreach (var virtualDiskPath in _hyperVDiskMountDirectories.Keys.ToList())
            {
                try
                {
                    RunPowerShell($"Dismount-DiskImage -ImagePath '{EscapePowerShellSingleQuotedString(virtualDiskPath)}' -ErrorAction SilentlyContinue | Out-Null");
                }
                catch
                {
                }
            }

            foreach (var mountDirectories in _hyperVDiskMountDirectories.Values)
            {
                foreach (string directory in mountDirectories)
                {
                    try
                    {
                        if (Directory.Exists(directory))
                        {
                            Directory.Delete(directory, recursive: true);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            _hyperVDiskMountDirectories.Clear();

            if (!string.IsNullOrWhiteSpace(_hyperVGuestMountRoot))
            {
                try
                {
                    if (Directory.Exists(_hyperVGuestMountRoot))
                    {
                        Directory.Delete(_hyperVGuestMountRoot, recursive: true);
                    }
                }
                catch
                {
                }
            }
        }

        private static string EscapePowerShellSingleQuotedString(string value)
            => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

        private static string RunPowerShell(string script)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(errors) ? "The PowerShell command failed." : errors.Trim());
            }

            return output;
        }

        private static string BuildHyperVGuestRelativePath(DriveTreeItem parentItem, string childResolvedPath)
        {
            if (!HyperVGuestSelectionPath.TryParse(parentItem.FullPath, out HyperVGuestSelectionInfo? parentSelection) || parentSelection == null)
            {
                return string.Empty;
            }

            string childName = Path.GetFileName(childResolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(childName))
            {
                return parentSelection.RelativePath;
            }

            return HyperVGuestSelectionPath.NormalizeRelativePath(Path.Combine(parentSelection.RelativePath, childName));
        }

        private static MountedHyperVGuestExecutionDisk MountHyperVGuestExecutionDiskReadOnly(string vmName, string virtualDiskPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(vmName);
            ArgumentException.ThrowIfNullOrWhiteSpace(virtualDiskPath);

            string mountRoot = Path.Combine(Path.GetTempPath(), "SecureServerBackup", "HyperVGuestMounts", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(mountRoot);

            string escapedPath = EscapePowerShellSingleQuotedString(virtualDiskPath);
            string escapedRoot = EscapePowerShellSingleQuotedString(mountRoot);
            string script = $"$image = Mount-DiskImage -ImagePath '{escapedPath}' -Access ReadOnly -PassThru -ErrorAction Stop; $disk = $image | Get-Disk -ErrorAction Stop; Get-Partition -DiskNumber $disk.Number -ErrorAction Stop | Sort-Object PartitionNumber | ForEach-Object {{ $partition = $_; $mountPath = $null; $created = $false; if ($partition.AccessPaths) {{ $mountPath = @($partition.AccessPaths | Where-Object {{ $_ }}) | Select-Object -First 1; }} if ([string]::IsNullOrWhiteSpace($mountPath)) {{ $folder = Join-Path '{escapedRoot}' ('Partition' + $partition.PartitionNumber); New-Item -ItemType Directory -Path $folder -Force | Out-Null; Add-PartitionAccessPath -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber -AccessPath $folder -ErrorAction Stop | Out-Null; $mountPath = $folder; $created = $true; }} [Console]::WriteLine(($partition.PartitionNumber.ToString() + \"`t\" + $mountPath + \"`t\" + $created.ToString())); }}";

            try
            {
                string output = RunPowerShell(script);
                List<MountedHyperVGuestExecutionPartition> partitions = new();
                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 3 ||
                        !int.TryParse(parts[0], out int partitionNumber) ||
                        string.IsNullOrWhiteSpace(parts[1]))
                    {
                        continue;
                    }

                    partitions.Add(new MountedHyperVGuestExecutionPartition(
                        partitionNumber,
                        parts[1].Trim(),
                        bool.TryParse(parts[2], out bool createdMountDirectory) && createdMountDirectory));
                }

                return new MountedHyperVGuestExecutionDisk(virtualDiskPath, mountRoot, partitions);
            }
            catch
            {
                try
                {
                    if (Directory.Exists(mountRoot))
                    {
                        Directory.Delete(mountRoot, recursive: true);
                    }
                }
                catch
                {
                }

                throw;
            }
        }

        private static IReadOnlyList<string> ResolveHyperVGuestExecutionSourcePaths(HyperVGuestSelectionInfo selection, MountedHyperVGuestExecutionDisk mountedDisk)
        {
            return HyperVGuestSelectionPath.GetCandidateSourcePaths(
                    selection,
                    mountedDisk.Partitions.Select(partition => new HyperVGuestMountedPartition(partition.PartitionNumber, partition.MountPath)))
                .Where(Directory.Exists)
                .ToArray();
        }

        private void LoadHyperVVirtualDiskChildren(DriveTreeItem virtualDiskItem)
        {
            virtualDiskItem.Children.Clear();

            if (string.IsNullOrWhiteSpace(virtualDiskItem.VirtualDiskPath) || string.IsNullOrWhiteSpace(virtualDiskItem.VirtualMachineName))
            {
                virtualDiskItem.Children.Add(new DriveTreeItem
                {
                    Name = "(Hyper-V virtual disk information is missing)",
                    ItemType = DriveTreeItemType.Folder,
                    Parent = virtualDiskItem
                });
                return;
            }

            try
            {
                string root = EnsureHyperVGuestMountRoot();
                string diskFolderName = Regex.Replace(Path.GetFileNameWithoutExtension(virtualDiskItem.VirtualDiskPath) ?? "HyperVDisk", "[^A-Za-z0-9._-]", "_");
                string diskMountRoot = Path.Combine(root, diskFolderName + "_" + Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(virtualDiskItem.VirtualDiskPath)).ToString("X8"));
                Directory.CreateDirectory(diskMountRoot);

                string script = $"$image = Mount-DiskImage -ImagePath '{EscapePowerShellSingleQuotedString(virtualDiskItem.VirtualDiskPath)}' -Access ReadOnly -PassThru -ErrorAction Stop; $disk = $image | Get-Disk -ErrorAction Stop; Get-Partition -DiskNumber $disk.Number -ErrorAction Stop | Sort-Object PartitionNumber | ForEach-Object {{ $partition = $_; $mountPath = $null; if ($partition.AccessPaths) {{ $mountPath = @($partition.AccessPaths | Where-Object {{ $_ }}) | Select-Object -First 1; }} if ([string]::IsNullOrWhiteSpace($mountPath)) {{ $folder = Join-Path '{EscapePowerShellSingleQuotedString(diskMountRoot)}' ('Partition' + $partition.PartitionNumber); New-Item -ItemType Directory -Path $folder -Force | Out-Null; Add-PartitionAccessPath -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber -AccessPath $folder -ErrorAction Stop | Out-Null; $mountPath = $folder; }} [Console]::WriteLine(($partition.PartitionNumber.ToString() + \"`t\" + $mountPath)); }}";
                string output = RunPowerShell(script);

                List<string> mountDirectories = new();
                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 2 || !int.TryParse(parts[0], out int partitionNumber))
                    {
                        continue;
                    }

                    string mountPath = parts[1].Trim();
                    if (string.IsNullOrWhiteSpace(mountPath) || !Directory.Exists(mountPath))
                    {
                        continue;
                    }

                    if (mountPath.StartsWith(diskMountRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        mountDirectories.Add(mountPath);
                    }

                    string encodedPath = HyperVGuestSelectionPath.Encode(
                        HyperVGuestSelectionKind.Volume,
                        virtualDiskItem.VirtualMachineName,
                        virtualDiskItem.VirtualDiskPath,
                        partitionNumber,
                        string.Empty);

                    var volumeItem = new DriveTreeItem
                    {
                        Name = $"Partition {partitionNumber}",
                        FullPath = encodedPath,
                        ResolvedPath = mountPath,
                        VirtualMachineName = virtualDiskItem.VirtualMachineName,
                        VirtualDiskPath = virtualDiskItem.VirtualDiskPath,
                        PartitionNumber = partitionNumber,
                        ItemType = DriveTreeItemType.HyperVVolume,
                        Parent = virtualDiskItem
                    };

                    if (Directory.GetDirectories(mountPath).Length > 0)
                    {
                        volumeItem.Children.Add(new DriveTreeItem
                        {
                            Name = "Loading...",
                            ItemType = DriveTreeItemType.Folder,
                            Parent = volumeItem
                        });
                    }

                    virtualDiskItem.Children.Add(volumeItem);
                }

                _hyperVDiskMountDirectories[virtualDiskItem.VirtualDiskPath] = mountDirectories;

                if (virtualDiskItem.Children.Count == 0)
                {
                    virtualDiskItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(No accessible guest partitions)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = virtualDiskItem
                    });
                }
            }
            catch (Exception ex)
            {
                virtualDiskItem.Children.Add(new DriveTreeItem
                {
                    Name = $"(Error mounting virtual disk: {ex.Message})",
                    ItemType = DriveTreeItemType.Folder,
                    Parent = virtualDiskItem
                });
            }
        }

        private void LoadFoldersForVolume(DriveTreeItem volumeItem)
        {
            try
            {
                // Remove the placeholder "Loading..." item
                volumeItem.Children.Clear();
                
                var rootPath = string.IsNullOrWhiteSpace(volumeItem.ResolvedPath) ? volumeItem.FullPath : volumeItem.ResolvedPath;
                
                System.Diagnostics.Debug.WriteLine($"=== LoadFoldersForVolume ===");
                System.Diagnostics.Debug.WriteLine($"Volume: {volumeItem.Name}");
                System.Diagnostics.Debug.WriteLine($"Path: '{rootPath}'");
                
                // Check if this is a system partition without drive letter
                if (rootPath.StartsWith("\\\\?\\Volume{"))
                {
                    volumeItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(System partition - cannot browse)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                    return;
                }
                
                if (!Directory.Exists(rootPath))
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Directory does not exist: '{rootPath}'");
                    volumeItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(Volume not accessible)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Directory exists, enumerating folders...");

                // Add top-level folders
                var foldersAdded = 0;
                try
                {
                    var directories = Directory.GetDirectories(rootPath);
                    System.Diagnostics.Debug.WriteLine($"Found {directories.Length} directories");
                    
                    foreach (var directory in directories)
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(directory);
                            
                            // Show ALL folders including hidden and system
                            // Mark them differently but don't skip them
                            var isHidden = (dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
                            var isSystem = (dirInfo.Attributes & FileAttributes.System) == FileAttributes.System;
                            
                            var folderName = dirInfo.Name;
                            if (isSystem)
                                folderName += " [System]";
                            else if (isHidden)
                                folderName += " [Hidden]";

                            var folderItem = new DriveTreeItem
                            {
                                Name = folderName,
                                FullPath = HyperVGuestSelectionPath.IsEncodedPath(volumeItem.FullPath)
                                    ? HyperVGuestSelectionPath.Encode(
                                        HyperVGuestSelectionKind.Folder,
                                        volumeItem.VirtualMachineName,
                                        volumeItem.VirtualDiskPath,
                                        volumeItem.PartitionNumber,
                                        BuildHyperVGuestRelativePath(volumeItem, dirInfo.FullName))
                                    : dirInfo.FullName,
                                ResolvedPath = dirInfo.FullName,
                                VirtualMachineName = volumeItem.VirtualMachineName,
                                VirtualDiskPath = volumeItem.VirtualDiskPath,
                                PartitionNumber = volumeItem.PartitionNumber,
                                ItemType = DriveTreeItemType.Folder,
                                Parent = volumeItem
                            };

                            // Add a dummy child if this folder has subfolders (for expand arrow)
                            try
                            {
                                if (Directory.GetDirectories(directory).Length > 0 || 
                                    Directory.GetFiles(directory).Length > 0)
                                {
                                    folderItem.Children.Add(new DriveTreeItem 
                                    { 
                                        Name = "Loading...", 
                                        ItemType = DriveTreeItemType.Folder,
                                        Parent = folderItem
                                    });
                                }
                            }
                            catch
                            {
                                // Can't access subfolder info
                            }

                            volumeItem.Children.Add(folderItem);
                            foldersAdded++;
                            
                            System.Diagnostics.Debug.WriteLine($"  Added: {folderName}");
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Add a marker for inaccessible folders
                            var folderName = $"{Path.GetFileName(directory)} [Access Denied]";
                            string folderFullPath = HyperVGuestSelectionPath.IsEncodedPath(volumeItem.FullPath)
                                ? HyperVGuestSelectionPath.Encode(
                                    HyperVGuestSelectionKind.Folder,
                                    volumeItem.VirtualMachineName,
                                    volumeItem.VirtualDiskPath,
                                    volumeItem.PartitionNumber,
                                    BuildHyperVGuestRelativePath(volumeItem, directory))
                                : directory;

                            volumeItem.Children.Add(new DriveTreeItem
                            {
                                Name = folderName,
                                FullPath = folderFullPath,
                                ResolvedPath = directory,
                                VirtualMachineName = volumeItem.VirtualMachineName,
                                VirtualDiskPath = volumeItem.VirtualDiskPath,
                                PartitionNumber = volumeItem.PartitionNumber,
                                ItemType = DriveTreeItemType.Folder,
                                Parent = volumeItem
                            });
                            foldersAdded++;
                            System.Diagnostics.Debug.WriteLine($"  Access denied: {Path.GetFileName(directory)}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"  Error processing folder {directory}: {ex.Message}");
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: Access denied to volume root");
                    volumeItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(Access Denied - Run as Administrator)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"Total folders added: {foldersAdded}");
                
                // If no folders were accessible, show a message
                if (foldersAdded == 0)
                {
                    volumeItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(Empty or no accessible folders)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR in LoadFoldersForVolume: {ex.Message}\nStack: {ex.StackTrace}");
                volumeItem.Children.Clear();
                volumeItem.Children.Add(new DriveTreeItem
                {
                    Name = $"(Error: {ex.Message})",
                    ItemType = DriveTreeItemType.Folder,
                    Parent = volumeItem
                });
            }
        }

        private void InitializeScheduleControls()
        {
            // Populate hours (1-12) for 12-hour format
            for (int i = 1; i <= 12; i++)
            {
                cmbHour.Items.Add(i.ToString());
            }
            cmbHour.SelectedIndex = 1; // 2 (default)

            // Populate minutes
            for (int i = 0; i < 60; i++)
            {
                cmbMinute.Items.Add(i.ToString("D2"));
            }
            cmbMinute.Text = "00";

            // Set default AM/PM (AM)
            cmbAmPm.SelectedIndex = 0; // AM

            // Populate days of month
            for (int i = 1; i <= 31; i++)
            {
                cmbDayOfMonth.Items.Add(i.ToString());
            }
            cmbDayOfMonth.SelectedIndex = 0;
        }

        private async Task LoadDrives()
        {
            try
            {
                // Show loading overlay
                if (loadingOverlay != null)
                    loadingOverlay.Visibility = Visibility.Visible;

                driveItems.Clear();
                treeViewDrives.Items.Clear();

                // Load physical drives and volumes
                await LoadPhysicalDrives();

                // Load Hyper-V systems
                await LoadHyperVSystems();

                // Load network locations
                await LoadNetworkDrives();

                // Manually create TreeViewItems for proper hierarchical display
                foreach (var drive in driveItems)
                {
                    var treeItem = CreateTreeViewItem(drive);
                    treeViewDrives.Items.Add(treeItem);
                }
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError($"Error loading drives: {ex.Message}\n\nDetails: {ex.InnerException?.Message}", "Error");
                throw; // Re-throw to be caught by caller
            }
            finally
            {
                // Hide loading overlay
                if (loadingOverlay != null)
                    loadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private TreeViewItem CreateTreeViewItem(DriveTreeItem item)
        {
            var treeViewItem = new TreeViewItem();
            
            // Create the header with checkbox and text
            var panel = new System.Windows.Controls.StackPanel 
            { 
                Orientation = System.Windows.Controls.Orientation.Horizontal 
            };
            
            
            var checkbox = new System.Windows.Controls.CheckBox
            {
                IsChecked = item.IsChecked,
                IsThreeState = true,  // Keep for visual representation
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            
            // Handle click to prevent three-state cycling
            checkbox.Click += (s, e) =>
            {
                // On click, toggle between checked and unchecked only
                // Skip the indeterminate state for user clicks
                if (item.IsChecked == true)
                {
                    item.IsChecked = false;
                    System.Diagnostics.Debug.WriteLine($"[Checkbox] Unchecked: {item.Name} ({item.ItemType})");
                }
                else
                {
                    item.IsChecked = true;
                    System.Diagnostics.Debug.WriteLine($"[Checkbox] Checked: {item.Name} ({item.ItemType})");
                }
                e.Handled = true;  // Prevent default three-state behavior

                // Track source selection for clone operations (volumes OR disks)
                if ((item.ItemType == DriveTreeItemType.Volume || item.ItemType == DriveTreeItemType.Disk) && item.IsChecked == true)
                {
                    hasSourceSelected = true;
                    volumeConfigShown = false; // Reset to allow showing again
                    System.Diagnostics.Debug.WriteLine($"[Checkbox] {item.ItemType} checked, hasSourceSelected = true, hasTargetSelected = {hasTargetSelected}");
                    CheckAndShowVolumeConfiguration();
                }
                else if (item.ItemType == DriveTreeItemType.Volume || item.ItemType == DriveTreeItemType.Disk)
                {
                    // Check if any volumes/disks are still selected
                    hasSourceSelected = GetCheckedDriveItems().Any(i => i.ItemType == DriveTreeItemType.Volume || i.ItemType == DriveTreeItemType.Disk);
                    System.Diagnostics.Debug.WriteLine($"[Checkbox] {item.ItemType} unchecked, hasSourceSelected = {hasSourceSelected}");
                }
            };
            
            // Update checkbox when model changes (allows indeterminate from parent updates)
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(item.IsChecked))
                {
                    checkbox.IsChecked = item.IsChecked;
                }
            };
            
            var textBlock = new TextBlock
            {
                Text = item.DisplayName,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            panel.Children.Add(checkbox);
            panel.Children.Add(textBlock);
            treeViewItem.Header = panel;
            
            // Debug: Log item creation
            System.Diagnostics.Debug.WriteLine($"Creating TreeViewItem for: {item.Name}, Children: {item.Children.Count}");
            
            // Add children FIRST (before setting up events)
            foreach (var child in item.Children)
            {
                treeViewItem.Items.Add(CreateTreeViewItem(child));
            }
            
            // Bind expansion state AFTER adding children
            treeViewItem.IsExpanded = item.IsExpanded;
            treeViewItem.Expanded += (s, e) =>
            {
                if (e.Source == treeViewItem)
                {
                    item.IsExpanded = true;
                    
                    // Handle "Add Network Path..." click
                    if (item.ItemType == DriveTreeItemType.NetworkBrowser)
                    {
                        var dialog = new NetworkPathDialog();
                        if (dialog.ShowDialog() == true)
                        {
                            AddNetworkPathToTree(dialog.NetworkPath);
                        }
                        e.Handled = true;
                        return;
                    }
                    
                    if (item.ItemType == DriveTreeItemType.HyperVVirtualDisk && !item.ChildrenLoaded)
                    {
                        LoadHyperVVirtualDiskChildren(item);
                        item.ChildrenLoaded = true;

                        treeViewItem.Items.Clear();
                        foreach (var child in item.Children)
                        {
                            treeViewItem.Items.Add(CreateTreeViewItem(child));
                        }
                    }
                    // Load folders for volumes when expanded
                    else if ((item.ItemType == DriveTreeItemType.Volume || 
                              item.ItemType == DriveTreeItemType.HyperVVolume ||
                              item.ItemType == DriveTreeItemType.NetworkDrive || 
                              item.ItemType == DriveTreeItemType.NetworkShare ||
                              (item.ItemType == DriveTreeItemType.Folder && !string.IsNullOrWhiteSpace(item.ResolvedPath))) && 
                             !item.ChildrenLoaded)
                    {
                        LoadFoldersForVolume(item);
                        item.ChildrenLoaded = true;
                        
                        // Rebuild children
                        treeViewItem.Items.Clear();
                        foreach (var child in item.Children)
                        {
                            treeViewItem.Items.Add(CreateTreeViewItem(child));
                        }
                    }
                }
            };
            
            treeViewItem.Collapsed += (s, e) =>
            {
                if (e.Source == treeViewItem)
                {
                    item.IsExpanded = false;
                }
            };
            
            return treeViewItem;
        }

        private async Task LoadPhysicalDrives()
        {
            await Task.Run(() =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("=== Starting LoadPhysicalDrives ===");
                    
                    // Try with ORDER BY first
                    ManagementObjectSearcher searcher;
                    try
                    {
                        searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive ORDER BY Index");
                        var testCount = searcher.Get().Count;
                        System.Diagnostics.Debug.WriteLine($"Found {testCount} disks with ORDER BY");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ORDER BY failed: {ex.Message}, trying without ORDER BY");
                        searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                    }
                    
                    using (searcher)
                    {
                        foreach (ManagementObject disk in searcher.Get())
                        {
                            try
                            {
                                // Safely get properties with fallbacks
                                int diskIndex = 0;
                                try
                                {
                                    var indexObj = disk["Index"];
                                    if (indexObj != null)
                                        diskIndex = Convert.ToInt32(indexObj);
                                    else
                                        System.Diagnostics.Debug.WriteLine("Warning: Index property is null");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error getting Index: {ex.Message}, using 0");
                                }
                                
                                var model = disk["Model"]?.ToString() ?? "Unknown Model";
                                var deviceId = disk["DeviceID"]?.ToString() ?? "";
                                long size = 0;
                                
                                try
                                {
                                    var sizeObj = disk["Size"];
                                    if (sizeObj != null)
                                        size = Convert.ToInt64(sizeObj);
                                }
                                catch { }
                                
                                var diskItem = new DriveTreeItem
                                {
                                    Name = $"Disk {diskIndex} - {model}",
                                    FullPath = deviceId,
                                    ItemType = DriveTreeItemType.Disk,
                                    Size = size
                                };

                                System.Diagnostics.Debug.WriteLine($"=== Found Disk {diskIndex}: {model} ({deviceId}) ===");

                                // Get volumes on this disk using the Index property
                                LoadVolumesForDisk(diskItem, diskIndex);
                                
                                System.Diagnostics.Debug.WriteLine($"Disk {diskIndex}: Found {diskItem.Children.Count} volumes");

                                if (diskItem.Children.Count == 0)
                                {
                                    diskItem.Children.Add(new DriveTreeItem
                                    {
                                        Name = "(No accessible volumes)",
                                        ItemType = DriveTreeItemType.Volume,
                                        Parent = diskItem
                                    });
                                }

                                Dispatcher.Invoke(() => driveItems.Add(diskItem));
                            }
                            catch (Exception diskEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error processing individual disk: {diskEx.Message}");
                                Dispatcher.Invoke(() =>
                                    CustomDialogService.ShowWarning($"Error processing a disk: {diskEx.Message}\n\nContinuing with remaining disks...", 
                                        "Warning"));
                            }
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"=== Completed LoadPhysicalDrives: {driveItems.Count} disks loaded ===");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR in LoadPhysicalDrives: {ex.Message}\nStack: {ex.StackTrace}");
                    Dispatcher.Invoke(() =>
                        CustomDialogService.ShowError($"Error loading physical drives: {ex.Message}\n\nDetails: {ex.GetType().Name}\n\nPlease check Output window for details.", 
                            "Error"));
                }
            });
        }

        private void LoadVolumesForDisk(DriveTreeItem diskItem, int diskNum)
        {
            var volumesFound = false;
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Loading volumes for Disk {diskNum}: {diskItem.FullPath} ===");
                
                // Try method 1: WMI Associators (most accurate but sometimes fails)
                volumesFound = TryLoadVolumesViaWMI(diskItem, diskNum);
                
                // Try method 2: Alternative WMI query if method 1 failed
                if (!volumesFound)
                {
                    System.Diagnostics.Debug.WriteLine($"Method 1 failed, trying alternative WMI query for disk {diskNum}");
                    volumesFound = TryLoadVolumesViaAlternativeWMI(diskItem, diskNum);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LoadVolumesForDisk for disk {diskNum}: {ex.Message}");
            }
            
            // If WMI didn't find any volumes, use fallback
            if (!volumesFound)
            {
                System.Diagnostics.Debug.WriteLine($"All WMI methods failed for disk {diskNum}, using fallback");
                LoadVolumesSimpleFallback(diskItem, diskNum);
            }
        }

        private bool TryLoadVolumesViaWMI(DriveTreeItem diskItem, int diskNum)
        {
            var volumesFound = false;
            
            try
            {
                var deviceId = diskItem.FullPath.Replace("\\", "\\\\");
                var partitionQuery = $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{deviceId}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition";
                
                System.Diagnostics.Debug.WriteLine($"Query 1: {partitionQuery}");
                
                using var partitionSearcher = new ManagementObjectSearcher(partitionQuery);
                var partitions = partitionSearcher.Get();
                
                System.Diagnostics.Debug.WriteLine($"Query 1 returned {partitions.Count} partitions");
                
                foreach (ManagementObject partition in partitions)
                {
                    var partitionDeviceId = partition["DeviceID"]?.ToString();
                    System.Diagnostics.Debug.WriteLine($"  Partition: {partitionDeviceId}");
                    
                    var logicalQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionDeviceId}'}} WHERE AssocClass = Win32_LogicalDiskToPartition";
                    
                    using var logicalSearcher = new ManagementObjectSearcher(logicalQuery);
                    var logicalDisks = logicalSearcher.Get();
                    
                    System.Diagnostics.Debug.WriteLine($"  Query 2 returned {logicalDisks.Count} logical disks");
                    
                    foreach (ManagementObject logical in logicalDisks)
                    {
                        var driveLetter = logical["DeviceID"]?.ToString();
                        if (string.IsNullOrEmpty(driveLetter)) continue;

                        System.Diagnostics.Debug.WriteLine($"    Found: {driveLetter}");

                        if (AddVolumeToTree(diskItem, driveLetter))
                            volumesFound = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryLoadVolumesViaWMI failed: {ex.Message}");
            }
            
            return volumesFound;
        }

        private bool TryLoadVolumesViaAlternativeWMI(DriveTreeItem diskItem, int diskNum)
        {
            var volumesFound = false;
            
            try
            {
                // Query all partitions on this disk
                var query = $"SELECT * FROM Win32_DiskPartition WHERE DiskIndex = {diskNum}";
                
                System.Diagnostics.Debug.WriteLine($"Alternative query: {query}");
                
                using var searcher = new ManagementObjectSearcher(query);
                var partitions = searcher.Get();
                
                System.Diagnostics.Debug.WriteLine($"Alternative query found {partitions.Count} partitions");
                
                foreach (ManagementObject partition in partitions)
                {
                    var partitionDeviceId = partition["DeviceID"]?.ToString();
                    var partitionSize = Convert.ToInt64(partition["Size"] ?? 0);
                    System.Diagnostics.Debug.WriteLine($"  Partition: {partitionDeviceId} ({partitionSize / (1024.0 * 1024.0 * 1024.0):F2} GB)");
                    
                    // Try to find logical disk for this partition
                    var logicalQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionDeviceId}'}} WHERE AssocClass = Win32_LogicalDiskToPartition";
                    
                    using var logicalSearcher = new ManagementObjectSearcher(logicalQuery);
                    var logicalDisks = logicalSearcher.Get();
                    
                    if (logicalDisks.Count > 0)
                    {
                        // Has drive letter
                        foreach (ManagementObject logical in logicalDisks)
                        {
                            var driveLetter = logical["DeviceID"]?.ToString();
                            if (string.IsNullOrEmpty(driveLetter)) continue;

                            System.Diagnostics.Debug.WriteLine($"    Found logical disk: {driveLetter}");

                            if (AddVolumeToTree(diskItem, driveLetter))
                                volumesFound = true;
                        }
                    }
                    else
                    {
                        // No drive letter - query Win32_Volume directly
                        System.Diagnostics.Debug.WriteLine($"    No logical disk, checking Win32_Volume...");
                        
                        // Query volumes by DiskNumber (Win32_Volume has DeviceID that includes partition info)
                        var volumeQuery = $"SELECT * FROM Win32_Volume WHERE DriveType = 3"; // Fixed disk
                        using var volumeSearcher = new ManagementObjectSearcher(volumeQuery);
                        
                        foreach (ManagementObject volume in volumeSearcher.Get())
                        {
                            try
                            {
                                var volumeDeviceId = volume["DeviceID"]?.ToString();
                                var volumeName = volume["Label"]?.ToString() ?? "";
                                var volumeCapacity = Convert.ToInt64(volume["Capacity"] ?? 0);
                                
                                // Check if this volume's size matches the partition
                                if (Math.Abs(volumeCapacity - partitionSize) < 1024 * 1024 * 100) // Within 100MB
                                {
                                    var volumeType = "Unknown";
                                    if (volumeName.Contains("EFI", StringComparison.OrdinalIgnoreCase) || 
                                        volumeDeviceId?.Contains("EFI", StringComparison.OrdinalIgnoreCase) == true)
                                    {
                                        volumeType = "EFI System Partition";
                                    }
                                    else if (volumeName.Contains("Recovery", StringComparison.OrdinalIgnoreCase) ||
                                             volumeDeviceId?.Contains("Recovery", StringComparison.OrdinalIgnoreCase) == true)
                                    {
                                        volumeType = "Recovery Partition";
                                    }
                                    else
                                    {
                                        volumeType = string.IsNullOrEmpty(volumeName) ? "System Reserved" : volumeName;
                                    }

                                    var volumeItem = new DriveTreeItem
                                    {
                                        Name = $"(No Letter) {volumeType} ({volumeCapacity / (1024.0 * 1024.0 * 1024.0):F2} GB)",
                                        FullPath = volumeDeviceId ?? "",
                                        ItemType = DriveTreeItemType.Volume,
                                        Size = volumeCapacity,
                                        Parent = diskItem,
                                        IsBootVolume = volumeType.Contains("EFI")
                                    };

                                    // These volumes typically can't be browsed
                                    volumeItem.Children.Add(new DriveTreeItem
                                    {
                                        Name = "(System partition - not accessible)",
                                        ItemType = DriveTreeItemType.Folder,
                                        Parent = volumeItem
                                    });

                                    diskItem.Children.Add(volumeItem);
                                    volumesFound = true;
                                    
                                    System.Diagnostics.Debug.WriteLine($"      Added system volume: {volumeType}");
                                    break; // Found matching volume
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"      Error checking volume: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryLoadVolumesViaAlternativeWMI failed: {ex.Message}");
            }
            
            return volumesFound;
        }

        private bool AddVolumeToTree(DriveTreeItem diskItem, string driveLetter)
        {
            try
            {
                var driveInfo = new DriveInfo(driveLetter);
                if (!driveInfo.IsReady)
                {
                    System.Diagnostics.Debug.WriteLine($"      Drive {driveLetter} not ready");
                    return false;
                }

                var volumeLabel = string.IsNullOrEmpty(driveInfo.VolumeLabel) 
                    ? "Local Disk" 
                    : driveInfo.VolumeLabel;

                // Ensure the FullPath has a trailing backslash for directory enumeration
                var volumePath = driveLetter.TrimEnd('\\') + "\\";

                var volumeItem = new DriveTreeItem
                {
                    Name = $"{driveLetter} ({volumeLabel})",
                    FullPath = volumePath,  // Changed: Now includes trailing backslash (e.g., "E:\")
                    ItemType = DriveTreeItemType.Volume,
                    Size = driveInfo.TotalSize,
                    Parent = diskItem,
                    IsBootVolume = IsBootVolume(driveLetter),
                    IsWindowsServer = IsWindowsServerVolume(driveLetter)
                };

                volumeItem.Children.Add(new DriveTreeItem
                {
                    Name = "Loading...",
                    ItemType = DriveTreeItemType.Folder,
                    Parent = volumeItem
                });

                diskItem.Children.Add(volumeItem);
                System.Diagnostics.Debug.WriteLine($"      Added {driveLetter} to tree (path: {volumePath})");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"      Error adding {driveLetter}: {ex.Message}");
                return false;
            }
        }

        private void LoadVolumesSimpleFallback(DriveTreeItem diskItem, int diskNum)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Using simple fallback for disk {diskNum}");
                
                // Simple approach: Show all fixed drives
                // We can't determine which disk they're on, so we'll add them to the first disk
                if (diskNum == 0)
                {
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                                continue;

                            var volumeLabel = string.IsNullOrEmpty(drive.VolumeLabel) 
                                ? "Local Disk" 
                                : drive.VolumeLabel;

                            // Ensure trailing backslash for directory enumeration
                            var volumePath = drive.Name.TrimEnd('\\') + "\\";
                            var displayName = drive.Name.TrimEnd('\\');

                            var volumeItem = new DriveTreeItem
                            {
                                Name = $"{displayName} ({volumeLabel})",
                                FullPath = volumePath,  // Changed: Now includes trailing backslash
                                ItemType = DriveTreeItemType.Volume,
                                Size = drive.TotalSize,
                                Parent = diskItem,
                                IsBootVolume = IsBootVolume(drive.Name),
                                IsWindowsServer = IsWindowsServerVolume(drive.Name)
                            };

                            // Add placeholder for folders
                            volumeItem.Children.Add(new DriveTreeItem
                            {
                                Name = "Loading...",
                                ItemType = DriveTreeItemType.Folder,
                                Parent = volumeItem
                            });

                            diskItem.Children.Add(volumeItem);
                            System.Diagnostics.Debug.WriteLine($"Fallback: Added {drive.Name} to disk {diskNum} (path: {volumePath})");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error adding drive in fallback: {ex.Message}");
                        }
                    }
                }
                else
                {
                    // For other disks, just show a message
                    diskItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(Cannot map volumes - see Disk 0 for all volumes)",
                        ItemType = DriveTreeItemType.Volume,
                        Parent = diskItem
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in fallback method: {ex.Message}");
            }
        }

        private bool IsBootVolume(string driveLetter)
        {
            try
            {
                var systemDrive = Environment.GetFolderPath(Environment.SpecialFolder.System);
                return driveLetter.TrimEnd('\\').Equals(Path.GetPathRoot(systemDrive)?.TrimEnd('\\'), 
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsWindowsServerVolume(string driveLetter)
        {
            try
            {
                var systemDrive = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (!driveLetter.TrimEnd('\\').Equals(Path.GetPathRoot(systemDrive)?.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Check if it's Windows Server
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
                foreach (ManagementObject os in searcher.Get())
                {
                    var caption = os["Caption"]?.ToString() ?? "";
                    return caption.Contains("Server", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            return false;
        }

        private async Task LoadHyperVSystems()
        {
            await Task.Run(() =>
            {
                try
                {
                    Dictionary<string, DriveTreeItem> hyperVSystems = new(StringComparer.OrdinalIgnoreCase);

                    var vmBuffer = new StringBuilder(4096);
                    var vmResult = BackupEngineInterop.EnumerateHyperVMachines(vmBuffer, vmBuffer.Capacity);
                    if (vmResult == 0)
                    {
                        foreach (string vm in vmBuffer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            string normalizedVmName = RestoreWindowNew.RegularHyperVRestoreHelper.NormalizeHyperVVmName(vm);
                            if (string.IsNullOrWhiteSpace(normalizedVmName))
                            {
                                continue;
                            }

                            hyperVSystems[normalizedVmName] = new DriveTreeItem
                            {
                                Name = $"Hyper-V: {vm}",
                                FullPath = vm,
                                VirtualMachineName = normalizedVmName,
                                ItemType = DriveTreeItemType.HyperVSystem
                            };
                        }
                    }

                    foreach (HyperVVirtualDiskInfo diskInfo in EnumerateHyperVVirtualDiskInfos())
                    {
                        if (!hyperVSystems.TryGetValue(diskInfo.VirtualMachineName, out DriveTreeItem? hyperVItem))
                        {
                            hyperVItem = new DriveTreeItem
                            {
                                Name = $"Hyper-V: {diskInfo.VirtualMachineDisplayName}",
                                FullPath = diskInfo.VirtualMachineDisplayName,
                                VirtualMachineName = diskInfo.VirtualMachineName,
                                ItemType = DriveTreeItemType.HyperVSystem
                            };
                            hyperVSystems[diskInfo.VirtualMachineName] = hyperVItem;
                        }

                        AddHyperVVirtualDiskItem(hyperVItem, diskInfo);
                    }

                    foreach (DriveTreeItem hyperVSystem in hyperVSystems.Values)
                    {
                        if (hyperVSystem.Children.Count == 0)
                        {
                            hyperVSystem.Children.Add(new DriveTreeItem
                            {
                                Name = "(No guest disks found)",
                                ItemType = DriveTreeItemType.Folder,
                                Parent = hyperVSystem
                            });
                        }
                    }

                    foreach (DriveTreeItem hyperVSystem in hyperVSystems.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        Dispatcher.Invoke(() => driveItems.Add(hyperVSystem));
                    }
                }
                catch { }
            });
        }

        private void AddHyperVVirtualDiskItem(DriveTreeItem hyperVItem, HyperVVirtualDiskInfo diskInfo)
        {
            if (hyperVItem.Children.Any(item =>
                    item.ItemType == DriveTreeItemType.HyperVVirtualDisk &&
                    string.Equals(item.VirtualDiskPath, diskInfo.VirtualDiskPath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var virtualDiskItem = new DriveTreeItem
            {
                Name = Path.GetFileName(diskInfo.VirtualDiskPath),
                FullPath = HyperVGuestSelectionPath.Encode(HyperVGuestSelectionKind.VirtualDisk, diskInfo.VirtualMachineName, diskInfo.VirtualDiskPath, 0, string.Empty),
                VirtualMachineName = diskInfo.VirtualMachineName,
                VirtualDiskPath = diskInfo.VirtualDiskPath,
                ItemType = DriveTreeItemType.HyperVVirtualDisk,
                Parent = hyperVItem
            };
            virtualDiskItem.Children.Add(new DriveTreeItem
            {
                Name = "Loading...",
                ItemType = DriveTreeItemType.Folder,
                Parent = virtualDiskItem
            });
            hyperVItem.Children.Add(virtualDiskItem);
        }

        private IReadOnlyList<HyperVVirtualDiskInfo> EnumerateHyperVVirtualDiskInfos()
        {
            var diskBuffer = new StringBuilder(32768);
            var diskResult = BackupEngineInterop.EnumerateHyperVVirtualMachineDisks(diskBuffer, diskBuffer.Capacity);
            IReadOnlyList<HyperVVirtualDiskInfo> disks = HyperVBackupTreeHelper.ParseVirtualDiskEnumeration(diskBuffer.ToString());
            if (disks.Count > 0)
            {
                return disks;
            }

            var error = new StringBuilder(1024);
            BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
            Debug.WriteLine($"EnumerateHyperVVirtualMachineDisks returned {diskResult}: {error}");

            try
            {
                string fallbackOutput = RunPowerShell("$ErrorActionPreference='Stop'; Get-VM -ErrorAction Stop | ForEach-Object { $vm = $_; $displayName = $vm.Name; if ($vm.State) { $displayName += ' (' + $vm.State.ToString() + ')'; } Get-VMHardDiskDrive -VMName $vm.Name -ErrorAction SilentlyContinue | ForEach-Object { if (-not [string]::IsNullOrWhiteSpace($_.Path)) { [Console]::WriteLine(($vm.Name + \"`t\" + $displayName + \"`t\" + $_.Path)); } } }");
                disks = HyperVBackupTreeHelper.ParseVirtualDiskEnumeration(fallbackOutput);
                if (disks.Count > 0)
                {
                    Debug.WriteLine($"Enumerated {disks.Count} Hyper-V virtual disks using PowerShell fallback.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PowerShell Hyper-V disk fallback failed: {ex.Message}");
            }

            return disks;
        }

        private async Task LoadNetworkDrives()
        {
            await Task.Run(() =>
            {
                try
                {
                    var networkRoot = new DriveTreeItem
                    {
                        Name = "Network Locations",
                        FullPath = "",
                        ItemType = DriveTreeItemType.NetworkRoot
                    };

                    // Enumerate mapped network drives
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (drive.DriveType == DriveType.Network && drive.IsReady)
                            {
                                var driveName = drive.Name.TrimEnd('\\');
                                var volumeLabel = string.IsNullOrEmpty(drive.VolumeLabel)
                                    ? "Network Drive"
                                    : drive.VolumeLabel;

                                var networkDrive = new DriveTreeItem
                                {
                                    Name = $"{driveName} ({volumeLabel}) - Mapped",
                                    FullPath = drive.Name,
                                    ItemType = DriveTreeItemType.NetworkDrive,
                                    Size = drive.TotalSize,
                                    Parent = networkRoot
                                };

                                // Add placeholder for folders
                                networkDrive.Children.Add(new DriveTreeItem
                                {
                                    Name = "Loading...",
                                    ItemType = DriveTreeItemType.Folder,
                                    Parent = networkDrive
                                });

                                networkRoot.Children.Add(networkDrive);
                            }
                        }
                        catch
                        {
                            // Skip drives that can't be accessed
                        }
                    }

                    // Add "Add Network Path..." option
                    var addNetworkPath = new DriveTreeItem
                    {
                        Name = "?? Add Network Path...",
                        FullPath = "",
                        ItemType = DriveTreeItemType.NetworkBrowser,
                        Parent = networkRoot
                    };

                    networkRoot.Children.Add(addNetworkPath);

                    // Only add Network Locations if there are mapped drives or the add option
                    if (networkRoot.Children.Count > 0)
                    {
                        Dispatcher.Invoke(() => driveItems.Add(networkRoot));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading network drives: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Adds a UNC network path to the tree
        /// </summary>
        private void AddNetworkPathToTree(string uncPath)
        {
            try
            {
                // Find the Network Locations root
                var networkRoot = driveItems.FirstOrDefault(d => d.ItemType == DriveTreeItemType.NetworkRoot);
                
                if (networkRoot == null)
                {
                    CustomDialogService.ShowError("Network Locations node not found.", "Error");
                    return;
                }

                // Check if this path already exists
                var existing = networkRoot.Children
                    .FirstOrDefault(c => c.FullPath.Equals(uncPath, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    CustomDialogService.ShowInfo($"Network path already added:\n{uncPath}", "Duplicate Path");
                    return;
                }

                // Extract server and share name for display
                var pathParts = uncPath.TrimEnd('\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var displayName = pathParts.Length >= 2
                    ? $"\\\\{pathParts[0]}\\{pathParts[1]}"
                    : uncPath;

                // Create network share item
                var networkShare = new DriveTreeItem
                {
                    Name = $"{displayName} - Network Share",
                    FullPath = uncPath.TrimEnd('\\') + "\\",  // Ensure trailing backslash
                    ItemType = DriveTreeItemType.NetworkShare,
                    Parent = networkRoot
                };

                // Add placeholder for folders
                networkShare.Children.Add(new DriveTreeItem
                {
                    Name = "Loading...",
                    ItemType = DriveTreeItemType.Folder,
                    Parent = networkShare
                });

                // Insert before "Add Network Path..." option
                var addOption = networkRoot.Children
                    .FirstOrDefault(c => c.ItemType == DriveTreeItemType.NetworkBrowser);

                if (addOption != null)
                {
                    var index = networkRoot.Children.IndexOf(addOption);
                    networkRoot.Children.Insert(index, networkShare);
                }
                else
                {
                    networkRoot.Children.Add(networkShare);
                }

                // Refresh the tree view
                RefreshTreeView();

                CustomDialogService.ShowSuccess($"Network path added successfully:\n{uncPath}", "Success");
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError($"Error adding network path:\n{ex.Message}", "Error");
            }
        }

        /// <summary>
        /// Refreshes the entire tree view
        /// </summary>
        private void RefreshTreeView()
        {
            treeViewDrives.Items.Clear();
            foreach (var drive in driveItems)
            {
                var treeItem = CreateTreeViewItem(drive);
                treeViewDrives.Items.Add(treeItem);
            }
        }

        private async void RefreshDrives_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadDrives();
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError($"Error refreshing drives: {ex.Message}", "Error");
            }
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllExpanded(driveItems, true);
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllExpanded(driveItems, false);
        }

        private void SetAllExpanded(ObservableCollection<DriveTreeItem> items, bool expanded)
        {
            foreach (var item in items)
            {
                item.IsExpanded = expanded;
                if (item.Children.Count > 0)
                {
                    SetAllExpanded(new ObservableCollection<DriveTreeItem>(item.Children), expanded);
                }
            }
        }

        private void BackupType_Changed(object sender, RoutedEventArgs e)
        {
            if (pnlCloneOptions == null || pnlBackupDestination == null) 
                return;

            // Check if Full Backup is selected to show/hide retention settings
            bool isFullBackup = rbFullBackup?.IsChecked == true;
            if (pnlRetentionSettings != null)
            {
                pnlRetentionSettings.Visibility = isFullBackup ? Visibility.Visible : Visibility.Collapsed;
            }

            // Clone to Disk: Show ONLY Clone to Physical Disk field
            if (rbCloneDisk?.IsChecked == true)
            {
                pnlCloneOptions.Visibility = Visibility.Visible;
                pnlBackupDestination.Visibility = Visibility.Collapsed;
                txtCloneDestinationLabel.Text = "Clone to Physical Disk:";
            }
            // Clone to Virtual Disk (Hyper-V): Show ONLY Backup Destination field
            else if (rbCloneVirtual?.IsChecked == true)
            {
                pnlCloneOptions.Visibility = Visibility.Collapsed;
                pnlBackupDestination.Visibility = Visibility.Visible;
            }
            // Clone Hyper-V System: Show ONLY Backup Destination field
            else if (rbCloneHyperV?.IsChecked == true)
            {
                pnlCloneOptions.Visibility = Visibility.Collapsed;
                pnlBackupDestination.Visibility = Visibility.Visible;
            }
            // All other backup types: Show ONLY Backup Destination field
            else
            {
                pnlCloneOptions.Visibility = Visibility.Collapsed;
                pnlBackupDestination.Visibility = Visibility.Visible;
            }
        }

        private void BrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select backup destination folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtDestination.Text = dialog.SelectedPath;
            }
        }

        private void BrowseCloneDestination_Click(object sender, RoutedEventArgs e)
        {
            bool isCloneToDisk = rbCloneDisk?.IsChecked == true;
            
            if (isCloneToDisk)
            {
                // For "Clone to Disk", show disk selection dialog
                try
                {
                    // Get source disk indexes to exclude
                    var sourceDiskIndexes = GetSelectedDiskIndexes();
                    
                    var diskDialog = new DiskSelectionWindow(sourceDiskIndexes);
                    diskDialog.Owner = this;
                    bool? result = diskDialog.ShowDialog();
                    
                    if (result == true && diskDialog.SelectedDisk != null)
                    {
                        var disk = diskDialog.SelectedDisk;
                        txtCloneDestination.Text = $"Disk {disk.DiskIndex}: {disk.Model} ({FormatSize(disk.SizeBytes)})";
                        txtCloneDestination.Tag = disk; // Store disk info for later use
                        
                        hasTargetSelected = true;
                        
                        System.Diagnostics.Debug.WriteLine($"[BrowseCloneDestination] ========== DISK SELECTED ==========");
                        System.Diagnostics.Debug.WriteLine($"[BrowseCloneDestination] Target: Disk {disk.DiskIndex}");
                        System.Diagnostics.Debug.WriteLine($"[BrowseCloneDestination] hasSourceSelected: {hasSourceSelected}");
                        System.Diagnostics.Debug.WriteLine($"[BrowseCloneDestination] hasTargetSelected: {hasTargetSelected}");
                        System.Diagnostics.Debug.WriteLine($"[BrowseCloneDestination] volumeConfigShown: {volumeConfigShown}");
                        System.Diagnostics.Debug.WriteLine($"[BrowseCloneDestination] About to call CheckAndShowVolumeConfiguration()");
                        System.Diagnostics.Debug.WriteLine($"[BrowseCloneDestination] =======================================");
                        
                        // Check if we should show volume configuration
                        CheckAndShowVolumeConfiguration();
                        
                        System.Diagnostics.Debug.WriteLine($"[BrowseCloneDestination] CheckAndShowVolumeConfiguration() returned");
                    }
                }
                catch (Exception ex)
                {
                    CustomDialogService.ShowError($"Error selecting disk: {ex.Message}",
                        "Error");
                }
            }
            else
            {
                // For "Clone to Virtual Disk", use folder browser
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Select folder for virtual disk clone",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtCloneDestination.Text = dialog.SelectedPath;
                    hasTargetSelected = true;
                    
                    System.Diagnostics.Debug.WriteLine($"[BrowseCloneDestination] Target selected: {dialog.SelectedPath}, Source selected: {hasSourceSelected}");
                    
                    // Check if we should show volume configuration
                    CheckAndShowVolumeConfiguration();
                }
            }
        }

        private void ManageExclusions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get current job's exclusions (or empty list for new jobs)
                var currentExclusions = _editingJob?.UserExclusions ?? new List<string>();

                // Open exclusions management window
                var exclusionsWindow = new ExclusionsManagementWindow(currentExclusions);
                exclusionsWindow.Owner = this;

                if (exclusionsWindow.ShowDialog() == true)
                {
                    // User clicked OK - save exclusions
                    if (_editingJob != null)
                    {
                        _editingJob.UserExclusions = exclusionsWindow.Exclusions;
                    }
                    else
                    {
                        // For new jobs, store exclusions temporarily until job is created
                        _tempUserExclusions = exclusionsWindow.Exclusions;
                    }

                    // Update button text to show exclusion count
                    if (exclusionsWindow.Exclusions.Count > 0)
                    {
                        btnManageExclusions.Content = $"Manage Exclusions... ({exclusionsWindow.Exclusions.Count})";
                    }
                    else
                    {
                        btnManageExclusions.Content = "Manage Exclusions...";
                    }
                }
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError($"Error managing exclusions: {ex.Message}",
                    "Error");
            }
        }

        /// <summary>
        /// Gets the disk indexes of selected source volumes
        /// </summary>
        private List<int> GetSelectedDiskIndexes()
        {
            var diskIndexes = new List<int>();
            
            try
            {
                var checkedItems = GetCheckedDriveItems();
                
                foreach (var item in checkedItems)
                {
                    if (item.ItemType == DriveTreeItemType.Disk)
                    {
                        // Extract disk index from item (e.g., "Disk 0" -> 0)
                        if (int.TryParse(item.Name.Replace("Disk", "").Trim(), out int diskIndex))
                        {
                            diskIndexes.Add(diskIndex);
                        }
                    }
                    else if (item.ItemType == DriveTreeItemType.Volume && item.Parent != null)
                    {
                        // Get parent disk index
                        if (int.TryParse(item.Parent.Name.Replace("Disk", "").Trim(), out int diskIndex))
                        {
                            if (!diskIndexes.Contains(diskIndex))
                            {
                                diskIndexes.Add(diskIndex);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting disk indexes: {ex.Message}");
            }
            
            return diskIndexes;
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Checks if both source and target are selected for clone operations and shows volume configuration modal
        /// </summary>
        private void CheckAndShowVolumeConfiguration()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[CheckAndShowVolumeConfig] Called");
                
                // Only for clone operations
                bool isCloneToDisk = rbCloneDisk?.IsChecked == true;
                bool isCloneToVirtual = rbCloneVirtual?.IsChecked == true;
                
                System.Diagnostics.Debug.WriteLine($"[CheckAndShowVolumeConfig] IsCloneToDisk: {isCloneToDisk}, IsCloneToVirtual: {isCloneToVirtual}");
                
                if (!isCloneToDisk && !isCloneToVirtual)
                {
                    System.Diagnostics.Debug.WriteLine($"[CheckAndShowVolumeConfig] Not a clone operation, returning");
                    return;
                }

                // Check if both source and target are selected
                System.Diagnostics.Debug.WriteLine($"[CheckAndShowVolumeConfig] hasSourceSelected: {hasSourceSelected}, hasTargetSelected: {hasTargetSelected}");
                
                if (!hasSourceSelected || !hasTargetSelected)
                {
                    System.Diagnostics.Debug.WriteLine($"[CheckAndShowVolumeConfig] Both not selected yet, returning");
                    return;
                }

                // Don't show multiple times
                if (volumeConfigShown)
                {
                    System.Diagnostics.Debug.WriteLine($"[CheckAndShowVolumeConfig] Already shown, returning");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[CheckAndShowVolumeConfig] All checks passed, preparing to show modal");
                volumeConfigShown = true;

                // Get selected volumes
                var selectedVolumes = GetSelectedVolumesForVolumeConfig();
                
                System.Diagnostics.Debug.WriteLine($"[CheckAndShowVolumeConfig] Selected volumes count: {selectedVolumes.Count}");
                
                if (selectedVolumes.Count == 0)
                {
                    CustomDialogService.ShowWarning("Please select at least one volume to clone.",
                        "No Source Selected");
                    volumeConfigShown = false;
                    return;
                }

                // Get target disk size
                long targetDiskSize = GetTargetDiskSize();
                
                System.Diagnostics.Debug.WriteLine($"[CheckAndShowVolumeConfig] Target disk size: {targetDiskSize}");
                
                if (targetDiskSize <= 0)
                {
                    CustomDialogService.ShowError("Unable to determine target disk size.",
                        "Invalid Target");
                    volumeConfigShown = false;
                    return;
                }

                // Get allocation unit sizes
                int sourceAUS = GetAllocationUnitSize(selectedVolumes[0].FileSystem);
                int targetAUS = GetTargetAllocationUnitSize();

                System.Diagnostics.Debug.WriteLine($"[CheckAndShowVolumeConfig] Showing modal window");
                
                // Show volume configuration modal
                var configWindow = new VolumeConfigurationWindow(
                    selectedVolumes,
                    targetDiskSize,
                    sourceAUS,
                    targetAUS
                );

                configWindow.Owner = this;
                bool? result = configWindow.ShowDialog();

                if (result == true)
                {
                    // User accepted configuration - can proceed with clone
                    System.Diagnostics.Debug.WriteLine("Volume configuration accepted");
                }
                else
                {
                    // User cancelled - reset target selection
                    hasTargetSelected = false;
                    txtCloneDestination.Text = string.Empty;
                    volumeConfigShown = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CheckAndShowVolumeConfig] ERROR: {ex.Message}\n{ex.StackTrace}");
                CustomDialogService.ShowError($"Error showing volume configuration: {ex.Message}",
                    "Error");
                volumeConfigShown = false;
            }
        }

        /// <summary>
        /// Gets the list of selected volumes for volume configuration
        /// </summary>
        private List<VolumeInfo> GetSelectedVolumesForVolumeConfig()
        {
            var volumes = new List<VolumeInfo>();

            try
            {
                var checkedItems = GetCheckedDriveItems();

                foreach (var item in checkedItems)
                {
                    if (item.ItemType == DriveTreeItemType.Volume)
                    {
                        // Get volume info
                        var (totalSize, usedSpace, fileSystem) = GetVolumeInfo(item.FullPath);
                        bool isSystemVolume = IsSystemVolume(item.FullPath);
                        int aus = GetAllocationUnitSize(fileSystem);

                        volumes.Add(new VolumeInfo
                        {
                            Label = item.Name,
                            Size = totalSize,
                            UsedSpace = usedSpace,
                            FileSystem = fileSystem,
                            IsSystemVolume = isSystemVolume,
                            AllocationUnitSize = aus,
                            IsResizable = false // Will be determined by VolumeConfigurationWindow
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting selected volumes: {ex.Message}");
            }

            return volumes;
        }

        /// <summary>
        /// Gets comprehensive volume information
        /// </summary>
        private (long TotalSize, long UsedSpace, string FileSystem) GetVolumeInfo(string volumePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(volumePath))
                    return (0, 0, "Unknown");

                var driveInfo = new DriveInfo(volumePath);
                long totalSize = driveInfo.TotalSize;
                long usedSpace = totalSize - driveInfo.AvailableFreeSpace;
                string fileSystem = driveInfo.DriveFormat; // "NTFS", "FAT32", etc.

                return (totalSize, usedSpace, fileSystem);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting volume info for {volumePath}: {ex.Message}");
                return (100L * 1024 * 1024 * 1024, 50L * 1024 * 1024 * 1024, "NTFS");
            }
        }

        /// <summary>
        /// Checks if a volume is a system volume
        /// </summary>
        private bool IsSystemVolume(string volumePath)
        {
            try
            {
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                return winDir.StartsWith(volumePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the allocation unit size for a file system
        /// </summary>
        private int GetAllocationUnitSize(string fileSystem)
        {
            // Default allocation unit sizes by file system type
            return fileSystem.ToUpperInvariant() switch
            {
                "NTFS" => 4096,      // 4 KB (most common for NTFS)
                "FAT32" => 4096,     // 4 KB
                "EXFAT" => 32768,    // 32 KB
                "REFS" => 65536,     // 64 KB (ReFS default)
                _ => 4096            // Default to 4 KB
            };
        }

        /// <summary>
        /// Gets the target disk allocation unit size
        /// </summary>
        private int GetTargetAllocationUnitSize()
        {
            try
            {
                string targetPath = rbCloneDisk?.IsChecked == true 
                    ? txtCloneDestination.Text 
                    : txtDestination.Text;

                if (string.IsNullOrWhiteSpace(targetPath))
                    return 4096;

                string? rootPath = Path.GetPathRoot(targetPath);
                if (string.IsNullOrEmpty(rootPath))
                    return 4096;
                    
                var driveInfo = new DriveInfo(rootPath);
                return GetAllocationUnitSize(driveInfo.DriveFormat);
            }
            catch
            {
                return 4096; // Default to 4 KB
            }
        }

        /// <summary>
        /// Gets the target disk size from the clone destination
        /// </summary>
        private long GetTargetDiskSize()
        {
            try
            {
                bool isCloneToDisk = rbCloneDisk?.IsChecked == true;
                bool isCloneToVirtual = rbCloneVirtual?.IsChecked == true;

                if (isCloneToDisk)
                {
                    // For physical disk clones, get size from selected disk
                    if (txtCloneDestination.Tag is DiskSelectionWindow.DiskInfo disk)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetTargetDiskSize] Using disk size from DiskInfo: {disk.SizeBytes}");
                        return disk.SizeBytes;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[GetTargetDiskSize] No DiskInfo found in tag");
                    return 500L * 1024 * 1024 * 1024; // Default: 500GB
                }
                else if (isCloneToVirtual)
                {
                    // For virtual disk clones, default to 500GB (user can adjust)
                    // In a full implementation, you'd allow the user to specify VHDX size
                    System.Diagnostics.Debug.WriteLine($"[GetTargetDiskSize] Using default for virtual disk: 500GB");
                    return 500L * 1024 * 1024 * 1024; // Default: 500GB
                }

                return 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting target disk size: {ex.Message}");
                return 500L * 1024 * 1024 * 1024; // Default: 500GB
            }
        }

        /// <summary>
        /// Gets all checked items from the drive tree
        /// </summary>
        private List<DriveTreeItem> GetCheckedDriveItems()
        {
            var checkedItems = new List<DriveTreeItem>();
            
            foreach (var item in driveItems)
            {
                GetCheckedItemsRecursive(item, checkedItems);
            }
            
            return checkedItems;
        }

        /// <summary>
        /// Recursively gets checked items from the tree
        /// </summary>
        private void GetCheckedItemsRecursive(DriveTreeItem item, List<DriveTreeItem> checkedItems)
        {
            if (item.IsChecked == true)
            {
                checkedItems.Add(item);
            }
            
            foreach (var child in item.Children)
            {
                GetCheckedItemsRecursive(child, checkedItems);
            }
        }

        private void Schedule_CheckedChanged(object sender, RoutedEventArgs e)
        {
            pnlSchedule.IsEnabled = chkEnableSchedule.IsChecked == true;
        }

        private void EncryptBackup_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateEncryptionUiState();
            AdjustWindowHeightForEncryption();
        }

        private void UpdateEncryptionUiState()
        {
            if (pnlEncryptionSettings == null)
            {
                return;
            }

            bool encryptionEnabled = chkEncryptBackup.IsChecked == true;
            pnlEncryptionSettings.Visibility = encryptionEnabled ? Visibility.Visible : Visibility.Collapsed;

            bool passwordLocked = encryptionEnabled && _hasSavedEncryptionPassword;
            if (pwdEncryptionPassword != null)
            {
                pwdEncryptionPassword.IsEnabled = !passwordLocked;
            }

            if (txtEncryptionPasswordVisible != null)
            {
                txtEncryptionPasswordVisible.IsReadOnly = passwordLocked;
            }

            if (chkShowEncryptionPassword != null)
            {
                chkShowEncryptionPassword.IsEnabled = encryptionEnabled;
            }

            if (pnlVerifyEncryptionPassword != null)
            {
                pnlVerifyEncryptionPassword.Visibility = encryptionEnabled && !_hasSavedEncryptionPassword
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (!encryptionEnabled)
            {
                chkShowEncryptionPassword.IsChecked = false;
                ClearEncryptionPasswordEntry();
            }
        }

        private void AdjustWindowHeightForEncryption()
        {
            double targetHeight = chkEncryptBackup.IsChecked == true
                ? EncryptionExpandedWindowHeight
                : DefaultWindowHeight;

            double maxHeight = SystemParameters.WorkArea.Height - 20;
            MaxHeight = maxHeight;
            double adjustedHeight = Math.Min(targetHeight, maxHeight);

            if (chkEncryptBackup.IsChecked == true)
            {
                if (Height < adjustedHeight)
                {
                    Height = adjustedHeight;
                    _windowHeightWasAutoAdjusted = true;
                }
            }
            else if (_windowHeightWasAutoAdjusted)
            {
                Height = adjustedHeight;
                _windowHeightWasAutoAdjusted = false;
            }
        }

        private void ShowEncryptionPassword_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingEncryptionPasswordDisplay)
            {
                return;
            }

            _isUpdatingEncryptionPasswordDisplay = true;
            try
            {
                bool showPassword = chkShowEncryptionPassword.IsChecked == true;

                if (showPassword)
                {
                    if (_hasSavedEncryptionPassword && _editingJob != null && string.IsNullOrWhiteSpace(_decryptedEncryptionPassword))
                    {
                        _decryptedEncryptionPassword = BackupEncryptionService.UnprotectPassword(_editingJob.ProtectedEncryptionPassword);
                    }

                    txtEncryptionPasswordVisible.Text = _hasSavedEncryptionPassword
                        ? _decryptedEncryptionPassword ?? string.Empty
                        : pwdEncryptionPassword.Password;
                    txtEncryptionPasswordVisible.Visibility = Visibility.Visible;
                    pwdEncryptionPassword.Visibility = Visibility.Collapsed;
                }
                else
                {
                    pwdEncryptionPassword.Visibility = Visibility.Visible;
                    txtEncryptionPasswordVisible.Visibility = Visibility.Collapsed;

                    if (_hasSavedEncryptionPassword)
                    {
                        pwdEncryptionPassword.Password = "********";
                        txtEncryptionPasswordVisible.Text = "********";
                    }
                    else
                    {
                        pwdEncryptionPassword.Password = txtEncryptionPasswordVisible.Text;
                    }
                }
            }
            finally
            {
                _isUpdatingEncryptionPasswordDisplay = false;
            }
        }

        private void EncryptionPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingEncryptionPasswordDisplay || _hasSavedEncryptionPassword)
            {
                return;
            }

            _isUpdatingEncryptionPasswordDisplay = true;
            txtEncryptionPasswordVisible.Text = pwdEncryptionPassword.Password;
            _isUpdatingEncryptionPasswordDisplay = false;
        }

        private void EncryptionPasswordVisible_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingEncryptionPasswordDisplay || _hasSavedEncryptionPassword)
            {
                return;
            }

            _isUpdatingEncryptionPasswordDisplay = true;
            pwdEncryptionPassword.Password = txtEncryptionPasswordVisible.Text;
            _isUpdatingEncryptionPasswordDisplay = false;
        }

        private void ClearEncryptionPasswordEntry()
        {
            pwdEncryptionPassword.Password = string.Empty;
            txtEncryptionPasswordVisible.Text = string.Empty;
            pwdVerifyEncryptionPassword.Password = string.Empty;
        }

        private string GetEnteredEncryptionPassword()
        {
            if (_hasSavedEncryptionPassword && _editingJob != null)
            {
                return _editingJob.ProtectedEncryptionPassword;
            }

            return BackupEncryptionService.ProtectPassword(pwdEncryptionPassword.Password);
        }

        private void Frequency_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbFrequency == null || pnlWeekly == null || pnlMonthly == null) 
                return;

            pnlWeekly.Visibility = cmbFrequency.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            pnlMonthly.Visibility = cmbFrequency.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void StartBackup_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs())
                return;

            try
            {
                progressBar.Visibility = Visibility.Visible;
                txtProgress.Visibility = Visibility.Visible;
                progressBar.Value = 0;

                var job = CreateJobFromInput();

                await ExecuteBackupJob(job);

                CustomDialogService.ShowSuccess("Backup completed successfully!", "Success");

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError($"Backup failed: {ex.Message}", "Error");
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                txtProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async Task ExecuteBackupJob(BackupJob job)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Create progress callback
                    BackupEngineInterop.ProgressCallback progressCallback = (percentage, message) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            progressBar.Value = percentage;
                            txtProgress.Text = message ?? $"Progress: {percentage}%";
                        });
                    };

                    int result = -1;

                    // Execute based on job type
                    if (job.IsHyperVBackup && job.HyperVMachines.Count > 0)
                    {
                        var vmDestPath = job.DestinationPath;

                        foreach (var vmName in job.HyperVMachines)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                txtProgress.Text = $"Backing up Hyper-V VM: {vmName}...";
                            });

                            result = job.Type switch
                            {
                                BackupType.Incremental => BackupEngineInterop.BackupHyperVVMIncremental(
                                    vmName,
                                    vmDestPath,
                                    progressCallback),
                                BackupType.Differential => BackupEngineInterop.BackupHyperVVMDifferential(
                                    vmName,
                                    vmDestPath,
                                    progressCallback),
                                _ => BackupEngineInterop.BackupHyperVVM(
                                    vmName,
                                    vmDestPath,
                                    progressCallback)
                            };

                            if (result != 0)
                            {
                                var errorBuffer = new StringBuilder(4096);
                                BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                throw new Exception($"Hyper-V backup failed: {errorBuffer}");
                            }
                        }
                    }
                    else if (job.Target == BackupTarget.Disk)
                    {
                        // Disk backup - use job name for .ssb file (matches service behavior)
                        var diskDestPath = Path.Combine(job.DestinationPath, $"{job.Name}.ssb");

                        // Extract disk number for logging
                        foreach (var diskPath in job.SourcePaths)
                        {
                            var diskNumStr = diskPath.Replace("\\\\?\\PHYSICALDRIVE", "").Replace("\\\\.\\PHYSICALDRIVE", "");
                            if (int.TryParse(diskNumStr, out int diskNum))
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    txtProgress.Text = $"Backing up Disk {diskNum}...";
                                });

                                result = BackupEngineInterop.BackupDisk(
                                    diskNum,
                                    diskDestPath,
                                    job.IncludeSystemState,
                                    job.CompressData,
                                    null,
                                    0,
                                    progressCallback,
                                    null);

                                if (result != 0)
                                {
                                    var errorBuffer = new StringBuilder(4096);
                                    BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                    throw new Exception($"Disk backup failed: {errorBuffer}");
                                }
                            }
                        }
                    }
                    else if (job.Target == BackupTarget.Volume)
                    {
                        // Volume backup - use job name for .ssb file (matches service behavior)
                        var volumeDestPath = Path.Combine(job.DestinationPath, $"{job.Name}.ssb");

                        foreach (var volumePath in job.SourcePaths)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                txtProgress.Text = $"Backing up volume {volumePath}...";
                            });

                            result = BackupEngineInterop.BackupVolume(
                                volumePath,
                                volumeDestPath,
                                job.IncludeSystemState,
                                job.CompressData,
                                null,
                                0,
                                progressCallback,
                                null);

                            if (result != 0)
                            {
                                var errorBuffer = new StringBuilder(4096);
                                BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                throw new Exception($"Volume backup failed: {errorBuffer}");
                            }
                        }
                    }
                    else if (job.Target == BackupTarget.FilesAndFolders)
                    {
                        void ExecuteFileBackupOperation(string resolvedSourcePath)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                txtProgress.Text = $"Backing up files from {resolvedSourcePath}...";
                            });

                            switch (job.Type)
                            {
                                case BackupType.Full:
                                    result = BackupEngineInterop.BackupFiles(
                                        resolvedSourcePath,
                                        job.DestinationPath,
                                        null,
                                        0,
                                        progressCallback,
                                        null);

                                    if (result != 0)
                                    {
                                        var errorBuffer = new StringBuilder(4096);
                                        BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                        throw new Exception($"File backup failed: {errorBuffer}");
                                    }
                                    break;

                                case BackupType.Incremental:
                                    var lastBackup = FindLastBackup(job.DestinationPath);

                                    result = BackupEngineInterop.CreateIncrementalBackup(
                                        resolvedSourcePath,
                                        job.DestinationPath,
                                        lastBackup ?? job.DestinationPath,
                                        progressCallback);

                                    if (result != 0)
                                    {
                                        var errorBuffer = new StringBuilder(4096);
                                        BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                        throw new Exception($"Incremental backup failed: {errorBuffer}");
                                    }
                                    break;

                                case BackupType.Differential:
                                    var fullBackup = FindFullBackup(job.DestinationPath);

                                    result = BackupEngineInterop.CreateDifferentialBackup(
                                        resolvedSourcePath,
                                        job.DestinationPath,
                                        fullBackup ?? job.DestinationPath,
                                        progressCallback);

                                    if (result != 0)
                                    {
                                        var errorBuffer = new StringBuilder(4096);
                                        BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                        throw new Exception($"Differential backup failed: {errorBuffer}");
                                    }
                                    break;
                            }
                        }

                        foreach (var sourcePath in job.SourcePaths)
                        {
                            if (HyperVGuestSelectionPath.TryParse(sourcePath, out HyperVGuestSelectionInfo? guestSelection) && guestSelection != null)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    txtProgress.Text = $"Mounting Hyper-V guest selection from {guestSelection.VirtualMachineName}...";
                                });

                                using var mountedDisk = MountHyperVGuestExecutionDiskReadOnly(guestSelection.VirtualMachineName, guestSelection.VirtualDiskPath);
                                IReadOnlyList<string> resolvedSourcePaths = ResolveHyperVGuestExecutionSourcePaths(guestSelection, mountedDisk);
                                if (resolvedSourcePaths.Count == 0)
                                {
                                    throw new Exception($"Hyper-V guest selection could not be resolved: {sourcePath}");
                                }

                                foreach (string resolvedSourcePath in resolvedSourcePaths)
                                {
                                    ExecuteFileBackupOperation(resolvedSourcePath);
                                }
                            }
                            else
                            {
                                ExecuteFileBackupOperation(sourcePath);
                            }
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = 100;
                        txtProgress.Text = "Backup completed!";
                    });

                    if (job.EncryptBackup && job.Target != BackupTarget.HyperV)
                    {
                        string backupPath = Path.Combine(job.DestinationPath, $"{job.Name}.ssb");
                        string password = BackupEncryptionService.UnprotectPassword(job.ProtectedEncryptionPassword);
                        BackupEncryptionService.EncryptFile(backupPath, backupPath, password);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Backup execution failed: {ex.Message}", ex);
                }
            });
        }

        private string? FindLastBackup(string destPath)
        {
            try
            {
                if (!Directory.Exists(destPath))
                    return null;

                // Look for backup folders with date pattern: Full_YYYYMMDD_HHMMSS, Incremental_YYYYMMDD_HHMMSS, etc.
                var backupFolders = Directory.GetDirectories(destPath)
                    .Where(dir =>
                    {
                        var folderName = Path.GetFileName(dir);
                        return folderName.StartsWith("Full_") ||
                               folderName.StartsWith("Incremental_") ||
                               folderName.StartsWith("Differential_");
                    })
                    .OrderByDescending(dir => Directory.GetCreationTime(dir))
                    .ToList();

                if (backupFolders.Count > 0)
                {
                    var lastBackupPath = backupFolders[0];
                    System.Diagnostics.Debug.WriteLine($"Found last backup: {lastBackupPath}");
                    return lastBackupPath;
                }

                // If no dated folders found, check for any subdirectories
                var allFolders = Directory.GetDirectories(destPath)
                    .OrderByDescending(dir => Directory.GetCreationTime(dir))
                    .ToList();

                if (allFolders.Count > 0)
                {
                    return allFolders[0];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding last backup: {ex.Message}");
            }

            return null;
        }

        private string? FindFullBackup(string destPath)
        {
            try
            {
                if (!Directory.Exists(destPath))
                    return null;

                // Look specifically for Full backup folders
                var fullBackupFolders = Directory.GetDirectories(destPath)
                    .Where(dir =>
                    {
                        var folderName = Path.GetFileName(dir);
                        return folderName.StartsWith("Full_", StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderByDescending(dir => Directory.GetCreationTime(dir))
                    .ToList();

                if (fullBackupFolders.Count > 0)
                {
                    var fullBackupPath = fullBackupFolders[0];
                    System.Diagnostics.Debug.WriteLine($"Found full backup: {fullBackupPath}");
                    return fullBackupPath;
                }

                // Fallback: If no "Full_" folders, look for oldest backup (likely the base)
                var allFolders = Directory.GetDirectories(destPath)
                    .Where(dir =>
                    {
                        var folderName = Path.GetFileName(dir);
                        return folderName.StartsWith("Full_") ||
                               folderName.StartsWith("Incremental_") ||
                               folderName.StartsWith("Differential_");
                    })
                    .OrderBy(dir => Directory.GetCreationTime(dir))  // Oldest first
                    .ToList();

                if (allFolders.Count > 0)
                {
                    var oldestBackup = allFolders[0];
                    System.Diagnostics.Debug.WriteLine($"Using oldest backup as full backup: {oldestBackup}");
                    return oldestBackup;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding full backup: {ex.Message}");
            }

            return null;
        }

        private void SaveJob_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs())
                return;

            try
            {
                var job = CreateJobFromInput();

                // If editing, preserve the ID
                if (existingJob != null)
                {
                    job.Id = existingJob.Id;
                    jobManager.UpdateJob(job);
                    _hasSavedEncryptionPassword = job.EncryptBackup && !string.IsNullOrWhiteSpace(job.ProtectedEncryptionPassword);
                    UpdateEncryptionUiState();
                    CustomDialogService.ShowSuccess($"Backup job '{job.Name}' updated successfully!\n\nJob saved to:\nC:\\ProgramData\\BackupRestoreService\\jobs.json", 
                        "Success");
                }
                else
                {
                    jobManager.AddJob(job);
                    _hasSavedEncryptionPassword = job.EncryptBackup && !string.IsNullOrWhiteSpace(job.ProtectedEncryptionPassword);
                    UpdateEncryptionUiState();
                    CustomDialogService.ShowSuccess($"Backup job '{job.Name}' created successfully!\n\nJob saved to:\nC:\\ProgramData\\BackupRestoreService\\jobs.json", 
                        "Success");
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError($"ERROR: Failed to save backup job!\n\n{ex.Message}\n\nPlease check:\n" +
                    "1. You have administrator rights\n" +
                    "2. C:\\ProgramData folder is accessible\n" +
                    "3. Antivirus is not blocking the save\n\n" +
                    $"Technical details:\n{ex.InnerException?.Message}", 
                    "Save Failed");
                
                System.Diagnostics.Debug.WriteLine($"SaveJob failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private BackupJob CreateJobFromInput()
        {
            var backupType = GetSelectedBackupType();
            
            var job = new BackupJob
            {
                Id = Guid.NewGuid(),
                Name = txtBackupName.Text,
                Type = backupType,
                // For Clone to Disk, use Clone destination; for all others, use Backup destination
                DestinationPath = rbCloneDisk?.IsChecked == true ? txtCloneDestination.Text : txtDestination.Text,
                CompressData = chkCompress.IsChecked == true,
                VerifyAfterBackup = chkVerify.IsChecked == true,
                EncryptBackup = chkEncryptBackup.IsChecked == true,
                ProtectedEncryptionPassword = chkEncryptBackup.IsChecked == true ? GetEnteredEncryptionPassword() : string.Empty,
                RetainFullBackupCount = int.TryParse(txtRetainCount.Text, out int retainCount) ? Math.Max(1, retainCount) : 1
            };

            // For Clone Hyper-V System, create subdirectories
            if (backupType == BackupType.CloneHyperVSystem)
            {
                job.IsHyperVBackup = true;
                // The subdirectories HVconfig and HVDisks will be created during backup execution
                // Collect selected Hyper-V VMs from tree
                CollectSelectedHyperVMachines(job);
            }
            else
            {
                // Collect selected items from tree for normal backups
                CollectSelectedItems(job);
            }

            // Schedule
            if (chkEnableSchedule.IsChecked == true)
            {
                if (!TryGetScheduledTime(out var hour24, out var minute))
                {
                    throw new InvalidOperationException("The scheduled time is invalid.");
                }
                
                job.Schedule = new BackupSchedule
                {
                    JobId = job.Id,
                    Enabled = true,
                    Frequency = (ScheduleFrequency)cmbFrequency.SelectedIndex,
                    Time = new TimeSpan(
                        hour24,
                        minute,
                        0)
                };

                if (job.Schedule.Frequency == ScheduleFrequency.Weekly)
                {
                    if (chkMonday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Monday);
                    if (chkTuesday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Tuesday);
                    if (chkWednesday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Wednesday);
                    if (chkThursday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Thursday);
                    if (chkFriday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Friday);
                    if (chkSaturday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Saturday);
                    if (chkSunday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Sunday);
                }
                else if (job.Schedule.Frequency == ScheduleFrequency.Monthly)
                {
                    job.Schedule.DayOfMonth = int.Parse(cmbDayOfMonth.SelectedItem?.ToString() ?? "1");
                }
            }

            // Assign user exclusions
            if (_editingJob != null && _editingJob.UserExclusions != null)
            {
                // Editing existing job - use its exclusions
                job.UserExclusions = new List<string>(_editingJob.UserExclusions);
            }
            else if (_tempUserExclusions != null)
            {
                // New job - use temporary exclusions from "Manage Exclusions" button
                job.UserExclusions = new List<string>(_tempUserExclusions);
            }
            else
            {
                // No exclusions defined
                job.UserExclusions = new List<string>();
            }

            return job;
        }

        private void CollectSelectedHyperVMachines(BackupJob job)
        {
            job.IsHyperVBackup = true;
            job.Target = BackupTarget.HyperV;

            foreach (var drive in driveItems)
            {
                if (drive.ItemType == DriveTreeItemType.HyperVSystem && drive.IsChecked == true)
                {
                    string vmName = RestoreWindowNew.RegularHyperVRestoreHelper.NormalizeHyperVVmName(drive.FullPath);
                    if (!string.IsNullOrWhiteSpace(vmName))
                    {
                        job.HyperVMachines.Add(vmName);
                    }
                }
            }
        }

        private BackupType GetSelectedBackupType()
        {
            if (rbFullBackup.IsChecked == true) return BackupType.Full;
            if (rbIncremental.IsChecked == true) return BackupType.Incremental;
            if (rbDifferential.IsChecked == true) return BackupType.Differential;
            if (rbCloneDisk.IsChecked == true) return BackupType.CloneToDisk;
            if (rbCloneVirtual.IsChecked == true) return BackupType.CloneToVirtualDisk;
            if (rbCloneHyperV.IsChecked == true) return BackupType.CloneHyperVSystem;
            
            return BackupType.Full; // Default
        }

        private void CollectSelectedItems(BackupJob job)
        {
            foreach (var drive in driveItems)
            {
                if (drive.IsChecked == true)
                {
                    // Whole disk selected
                    if (drive.ItemType == DriveTreeItemType.Disk)
                    {
                        job.Target = BackupTarget.Disk;
                        job.SourcePaths.Add(drive.FullPath);
                    }
                    else if (drive.ItemType == DriveTreeItemType.HyperVSystem)
                    {
                        job.Target = BackupTarget.HyperV;
                        string vmName = RestoreWindowNew.RegularHyperVRestoreHelper.NormalizeHyperVVmName(drive.FullPath);
                        if (!string.IsNullOrWhiteSpace(vmName))
                        {
                            job.HyperVMachines.Add(vmName);
                        }
                    }
                    else if (drive.ItemType == DriveTreeItemType.HyperVVirtualDisk || drive.ItemType == DriveTreeItemType.HyperVVolume)
                    {
                        job.Target = BackupTarget.FilesAndFolders;
                        job.SourcePaths.Add(drive.FullPath);
                    }
                }
                else if (drive.IsChecked == null && drive.Children.Count > 0)
                {
                    // Partial selection - check children
                    CollectSelectedChildren(drive, job);
                }
            }

            // Determine target type if not already set
            if (job.Target == 0 && job.SourcePaths.Count > 0)
            {
                // Check if all sources are drive letters (volumes), device paths (disks), or regular paths (files/folders)
                var firstPath = job.SourcePaths[0];

                // Check for PHYSICALDRIVE device paths (e.g., \\.\PHYSICALDRIVE5)
                if (firstPath.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase) &&
                    firstPath.Contains("PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
                {
                    job.Target = BackupTarget.Disk;
                }
                // Check for Volume GUID paths (e.g., \\?\Volume{guid})
                else if (firstPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) &&
                         firstPath.Contains("Volume{", StringComparison.OrdinalIgnoreCase))
                {
                    job.Target = BackupTarget.Volume;
                }
                // Check for simple drive letters (e.g., C:, E:, W:)
                else if (firstPath.Length <= 3 && firstPath.EndsWith(":"))
                {
                    job.Target = BackupTarget.Volume;
                }
                // Everything else is files/folders
                else
                {
                    job.Target = BackupTarget.FilesAndFolders;
                }
            }

            if (job.HyperVMachines.Count > 0)
            {
                job.IsHyperVBackup = true;
                job.Target = BackupTarget.HyperV;
            }
        }

        private void CollectSelectedChildren(DriveTreeItem parent, BackupJob job)
        {
            foreach (var child in parent.Children)
            {
                if (child.IsChecked == true)
                {
                    if (child.ItemType == DriveTreeItemType.Volume)
                    {
                        if (job.Target == 0) job.Target = BackupTarget.Volume;
                        job.SourcePaths.Add(child.FullPath);
                    }
                    else if (child.ItemType == DriveTreeItemType.HyperVVirtualDisk || child.ItemType == DriveTreeItemType.HyperVVolume)
                    {
                        job.Target = BackupTarget.FilesAndFolders;
                        job.SourcePaths.Add(child.FullPath);
                    }
                    else if (child.ItemType == DriveTreeItemType.Folder)
                    {
                        job.Target = BackupTarget.FilesAndFolders;
                        job.SourcePaths.Add(child.FullPath);
                    }
                }
                else if (child.IsChecked == null && child.Children.Count > 0)
                {
                    CollectSelectedChildren(child, job);
                }
            }
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(txtBackupName.Text))
            {
                CustomDialogService.ShowWarning("Please enter a backup name.", "Validation Error");
                return false;
            }

            // For Clone to Disk, check Clone to Physical Disk field
            if (rbCloneDisk?.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(txtCloneDestination.Text))
                {
                    CustomDialogService.ShowWarning("Please select a physical disk destination.", "Validation Error");
                    return false;
                }
            }
            // For all other types, check Backup Destination field
            else
            {
                if (string.IsNullOrWhiteSpace(txtDestination.Text))
                {
                    CustomDialogService.ShowWarning("Please select a backup destination.", "Validation Error");
                    return false;
                }
            }

            // Validate selections based on backup type
            var selectedItems = GetCheckedDriveItems();
            var selectedHyperV = selectedItems.Where(item => item.ItemType == DriveTreeItemType.HyperVSystem).ToList();
            var selectedNonHyperV = selectedItems.Where(item => item.ItemType != DriveTreeItemType.HyperVSystem).ToList();

            if (rbCloneHyperV?.IsChecked == true)
            {
                // Clone Hyper-V System: Must have at least one Hyper-V system selected, and ONLY Hyper-V systems
                if (selectedHyperV.Count == 0)
                {
                    CustomDialogService.ShowWarning("Please select at least one Hyper-V system to clone.", "Validation Error");
                    return false;
                }
                
                if (selectedNonHyperV.Count > 0)
                {
                    CustomDialogService.ShowWarning("Clone Hyper-V System can only clone Hyper-V systems.\n\nPlease unselect disks, volumes, and folders.", "Validation Error");
                    return false;
                }
            }
            else
            {
                if (selectedHyperV.Count > 0 && selectedNonHyperV.Count > 0)
                {
                    var backupTypeName = GetBackupTypeName();
                    CustomDialogService.ShowWarning($"{backupTypeName} cannot combine Hyper-V systems with disks, volumes, or folders.\n\nPlease select only Hyper-V systems or only disks, volumes, and folders.", "Validation Error");
                    return false;
                }

                if (selectedHyperV.Count == 0 && selectedNonHyperV.Count == 0)
                {
                    CustomDialogService.ShowWarning("Please select at least one drive, volume, folder, or Hyper-V system to backup.", "Validation Error");
                    return false;
                }
            }

            if (chkEnableSchedule.IsChecked == true && !TryGetScheduledTime(out _, out _))
            {
                CustomDialogService.ShowWarning("Please enter a valid scheduled time. Minutes must be between 00 and 59.", "Validation Error");
                return false;
            }

            if (chkEncryptBackup.IsChecked == true && !_hasSavedEncryptionPassword)
            {
                if (string.IsNullOrWhiteSpace(pwdEncryptionPassword.Password))
                {
                    CustomDialogService.ShowWarning("Please enter a password for backup encryption.", "Validation Error");
                    return false;
                }

                if (pwdEncryptionPassword.Password != pwdVerifyEncryptionPassword.Password)
                {
                    CustomDialogService.ShowWarning("The encryption passwords do not match.", "Validation Error");
                    return false;
                }
            }

            return true;
        }

        private bool TryGetScheduledTime(out int hour24, out int minute)
        {
            hour24 = 0;
            minute = 0;

            var hourText = cmbHour.Text;
            if (string.IsNullOrWhiteSpace(hourText))
            {
                hourText = cmbHour.SelectedItem?.ToString();
            }

            if (!int.TryParse(hourText, out var hour12) || hour12 < 1 || hour12 > 12)
            {
                return false;
            }

            if (!int.TryParse(cmbMinute.Text, out minute) || minute < 0 || minute > 59)
            {
                return false;
            }

            var ampm = ((ComboBoxItem)cmbAmPm.SelectedItem)?.Content?.ToString() ?? "AM";

            if (ampm == "AM")
            {
                hour24 = hour12 == 12 ? 0 : hour12;
            }
            else
            {
                hour24 = hour12 == 12 ? 12 : hour12 + 12;
            }

            cmbMinute.Text = minute.ToString("D2");
            return true;
        }

        private string GetBackupTypeName()
        {
            if (rbFullBackup.IsChecked == true) return "Full Backup";
            if (rbIncremental.IsChecked == true) return "Incremental Backup";
            if (rbDifferential.IsChecked == true) return "Differential Backup";
            if (rbCloneDisk.IsChecked == true) return "Clone to Disk";
            if (rbCloneVirtual.IsChecked == true) return "Clone to Virtual Disk";
            return "This backup type";
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
