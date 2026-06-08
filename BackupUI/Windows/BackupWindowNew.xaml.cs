using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using SecureServerBackupCommon;
using SecureServerBackup.Models;
using SecureServerBackup.Services;
using BackupEngineInterop = SecureServerBackup.Services.BackupEngineInterop;
using MessageBox = System.Windows.MessageBox;

namespace SecureServerBackup.Windows
{
    public partial class BackupWindowNew : Window
    {
        internal sealed record CloneHyperVPaths(string RootDirectory, string HyperVSystemDirectory, string HyperVDiskDirectory, string VirtualDiskPath, string VmName);

        public sealed record HyperVVirtualDiskInfo(string VirtualMachineName, string VirtualMachineDisplayName, string VirtualDiskPath);

        public static class HyperVBackupTreeHelper
        {
            public static string NormalizeSavedHyperVSystemName(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return string.Empty;
                }

                string normalizedValue = value.Trim();
                const string hyperVPrefix = "Hyper-V:";
                if (normalizedValue.StartsWith(hyperVPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalizedValue = normalizedValue[hyperVPrefix.Length..].Trim();
                }

                return RestoreWindowNew.RegularHyperVRestoreHelper.NormalizeHyperVVmName(normalizedValue);
            }

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

            public static bool IsVirtualDiskResource(string? resourceSubType)
            {
                if (string.IsNullOrWhiteSpace(resourceSubType))
                {
                    return false;
                }

                return resourceSubType.Contains("Virtual Hard Disk", StringComparison.OrdinalIgnoreCase) ||
                       resourceSubType.Contains("Microsoft:Hyper-V:Virtual Hard Disk", StringComparison.OrdinalIgnoreCase);
            }

            public static IEnumerable<string> GetHostResources(object? hostResourceValue)
            {
                if (hostResourceValue is string singleValue)
                {
                    yield return singleValue;
                    yield break;
                }

                if (hostResourceValue is string[] array)
                {
                    foreach (string value in array)
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            yield return value;
                        }
                    }

                    yield break;
                }

                if (hostResourceValue is Array values)
                {
                    foreach (object? value in values)
                    {
                        string? resource = value?.ToString();
                        if (!string.IsNullOrWhiteSpace(resource))
                        {
                            yield return resource;
                        }
                    }
                }
            }

            public static string BuildVmDisplayName(string vmName, object? enabledState)
            {
                if (enabledState is null || !int.TryParse(enabledState.ToString(), out int state))
                {
                    return vmName;
                }

                return state switch
                {
                    2 => $"{vmName} (Running)",
                    3 => $"{vmName} (Off)",
                    32768 => $"{vmName} (Paused)",
                    32769 => $"{vmName} (Saved)",
                    _ => $"{vmName} (Unknown State)"
                };
            }

            public static string SelectMountableVirtualDiskPath(string requestedPath, IEnumerable<string>? chainPaths)
            {
                if (string.IsNullOrWhiteSpace(requestedPath))
                {
                    return string.Empty;
                }

                string selectedPath = requestedPath;
                if (chainPaths is null)
                {
                    return selectedPath;
                }

                foreach (string chainPath in chainPaths)
                {
                    string normalizedPath = chainPath.Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(normalizedPath))
                    {
                        selectedPath = normalizedPath;
                    }
                }

                return selectedPath;
            }

            public static bool ShouldScheduleSetupCl(bool renameHyperVSystem, string? renameHyperVSystemName, BackupTarget target, IEnumerable<string>? sourcePaths, IEnumerable<int>? protectedDiskIndexes = null)
            {
                bool renamedClone = renameHyperVSystem && !string.IsNullOrWhiteSpace(renameHyperVSystemName);
                HashSet<int> protectedDisks = protectedDiskIndexes?
                    .Where(index => index >= 0)
                    .ToHashSet() ?? new HashSet<int>();

                bool clonedFromSystemDisk = target == BackupTarget.Disk && (sourcePaths?.Any(path =>
                    TryGetPhysicalDriveNumber(path, out int diskNumber) && protectedDisks.Contains(diskNumber)) ?? false);

                return renamedClone || clonedFromSystemDisk;
            }

            private static bool TryGetPhysicalDriveNumber(string? path, out int diskNumber)
            {
                diskNumber = -1;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                const string physicalDrivePrefix = "PHYSICALDRIVE";
                int prefixIndex = path.LastIndexOf(physicalDrivePrefix, StringComparison.OrdinalIgnoreCase);
                if (prefixIndex < 0)
                {
                    return false;
                }

                string suffix = path[(prefixIndex + physicalDrivePrefix.Length)..].Trim();
                return int.TryParse(suffix, out diskNumber);
            }
        }

        private const double DefaultWindowHeight = 850;
        private const double EncryptionExpandedWindowHeight = 980;
        private static readonly string SavedNetworkPathsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BackupRestoreApp",
            "saved-network-paths.json");

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
        private bool _isLoadingDrives;
        private bool _reloadDrivesAfterLoad;
        private bool _isInitializingJobData;
        private bool _hasCompletedInitialDriveLoad;
        private readonly Dictionary<string, MountedHyperVGuestTreeDisk> _hyperVDiskMountDirectories = new(StringComparer.OrdinalIgnoreCase);
        private string? _hyperVGuestMountRoot;
        private HashSet<string> _pendingSelectionPaths = new(StringComparer.OrdinalIgnoreCase);
        private List<string> _missingSavedSelectionPaths = new();

        private sealed record MountedHyperVGuestExecutionPartition(int PartitionNumber, string MountPath, bool CreatedMountDirectory);

        private sealed record MountedHyperVGuestTreeDisk(string MountedDiskPath, List<string> MountDirectories);

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
                    RunPowerShell($"Dismount-VHD -Path '{EscapePowerShellSingleQuotedString(VirtualDiskPath)}' -ErrorAction SilentlyContinue | Out-Null");
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
        private bool _showHiddenPartitions = false;

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

                // Set MaxHeight early to prevent window from exceeding work area
                MaxHeight = SystemParameters.WorkArea.Height - 20;

                InitializeScheduleControls();
                AdjustWindowHeightForEncryption();
                EnsureWindowWithinScreenBounds();

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
            _isInitializingJobData = true;

            try
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
                case BackupType.SelectedFilesAndFolders:
                    rbSelectedFilesAndFolders.IsChecked = true;
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
            cmbSelectedFilesRetentionCount.Text = Math.Clamp(job.SelectedFilesRetentionCount, 1, 30).ToString();
            cmbCloneRetentionCount.Text = Math.Clamp(job.CloneRetentionCount, 1, 30).ToString();
            chkEncryptBackup.IsChecked = job.EncryptBackup;

            _hasSavedEncryptionPassword = job.EncryptBackup && !string.IsNullOrWhiteSpace(job.ProtectedEncryptionPassword);
            UpdateEncryptionUiState();

            if (chkRenameHyperVSystem != null)
            {
                chkRenameHyperVSystem.IsChecked = job.RenameHyperVSystem;
            }

            if (txtRenameHyperVSystemName != null)
            {
                txtRenameHyperVSystemName.Text = job.RenameHyperVSystemName;
            }

            UpdateCloneHyperVRenameOptions();

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

            if (pnlSelectedFilesRetention != null)
            {
                pnlSelectedFilesRetention.Visibility = job.Type == BackupType.SelectedFilesAndFolders ? Visibility.Visible : Visibility.Collapsed;
            }

            if (pnlCloneRetention != null)
            {
                pnlCloneRetention.Visibility = job.Type == BackupType.CloneToVirtualDisk || job.Type == BackupType.CloneHyperVSystem ? Visibility.Visible : Visibility.Collapsed;
            }

            // Store job data for pre-selection after tree loads
            IReadOnlyList<string> replayPaths = GetReplayPathsForJob(job);
            if (replayPaths.Count > 0)
            {
                _pathsToPreselect = new List<string>(replayPaths);
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
            finally
            {
                _isInitializingJobData = false;
            }
        }

        private async void BackupWindowNew_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ensure window is positioned within screen bounds on initial load
                EnsureWindowWithinScreenBounds();

                await LoadDrives();
                _hasCompletedInitialDriveLoad = true;

                // Pre-select items if editing a job
                if (_pathsToPreselect != null && _pathsToPreselect.Count > 0)
                {
                    await PreSelectItemsAsync(_pathsToPreselect);
                }

                ShowMissingSavedSelectionsWarning();

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
        private async Task PreSelectItemsAsync(List<string> pathsToSelect)
        {
            _missingSavedSelectionPaths.Clear();
            _pendingSelectionPaths = pathsToSelect
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeSelectionPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var path in pathsToSelect)
            {
                if (HyperVGuestSelectionPath.TryParse(path, out HyperVGuestSelectionInfo? guestSelection) && guestSelection != null)
                {
                    await PreSelectHyperVGuestItemAsync(guestSelection);
                }
                else if (PreSelectHyperVSystemByName(path))
                {
                    // Matched a saved Hyper-V VM name — done for this entry
                }
                else
                {
                    bool matched = await PreSelectStandardItemAsync(path);
                    if (!matched)
                    {
                        _missingSavedSelectionPaths.Add(path);
                    }
                }
            }

            RefreshLoadedSelectionStates();
        }

        private void ShowMissingSavedSelectionsWarning()
        {
            if (_editingJob == null || _missingSavedSelectionPaths.Count == 0)
            {
                return;
            }

            List<string> missingSelections = _missingSavedSelectionPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missingSelections.Count == 0)
            {
                return;
            }

            string message = BuildMissingSavedSelectionsWarningMessage(_editingJob.Type, missingSelections);
            _missingSavedSelectionPaths.Clear();

            CustomDialogService.ShowWarning(this,
                message,
                "Missing Saved Selections");
        }

        internal static string BuildMissingSavedSelectionsWarningMessage(BackupType backupType, IReadOnlyList<string> missingSelections)
        {
            ArgumentNullException.ThrowIfNull(missingSelections);

            string summary = string.Join(Environment.NewLine, missingSelections.Take(10));
            if (missingSelections.Count > 10)
            {
                summary += Environment.NewLine + "...";
            }

            string intro = backupType == BackupType.SelectedFilesAndFolders
                ? "Some saved file or folder selections could not be found and were removed from the current selection list. The tree now shows only the selections that are still present."
                : "Some saved backup selections could not be found. The current selection list was cleared, so select something to back up before saving.";

            return intro + Environment.NewLine + Environment.NewLine + summary;
        }

        internal static string? GetSelectionValidationMessage(bool selectedFilesBackup, int selectedFilesCount, int selectedHyperVCount, int selectedNonHyperVCount)
        {
            if (selectedFilesBackup)
            {
                return selectedFilesCount == 0
                    ? "Please select at least one file, folder, or network share to back up."
                    : null;
            }

            return selectedHyperVCount == 0 && selectedNonHyperVCount == 0
                ? "Please select at least one drive, volume, folder, or Hyper-V system to backup."
                : null;
        }

        private async Task<bool> PreSelectStandardItemAsync(string pathToSelect)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pathToSelect);

            foreach (DriveTreeItem item in driveItems)
            {
                if (await PreSelectItemRecursiveAsync(item, pathToSelect))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<bool> PreSelectItemRecursiveAsync(DriveTreeItem item, string pathToSelect)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(pathToSelect);

            string targetPath = NormalizeSelectionPath(pathToSelect);
            string itemPath = NormalizeSelectionPath(item.FullPath);
            string resolvedPath = NormalizeSelectionPath(item.ResolvedPath);

            if ((!string.IsNullOrWhiteSpace(itemPath) && string.Equals(itemPath, targetPath, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(resolvedPath) && string.Equals(resolvedPath, targetPath, StringComparison.OrdinalIgnoreCase)))
            {
                item.IsChecked = true;
                return true;
            }

            bool itemPathCanContainSelection = PathCouldContainSelection(itemPath, targetPath);
            bool resolvedPathCanContainSelection = PathCouldContainSelection(resolvedPath, targetPath);
            if (!itemPathCanContainSelection && !resolvedPathCanContainSelection)
            {
                foreach (DriveTreeItem child in item.Children)
                {
                    if (await PreSelectItemRecursiveAsync(child, pathToSelect))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (item.IsChecked != true)
            {
                item.IsChecked = null;
            }

            if (RequiresLazyLoad(item) && !item.ChildrenLoaded)
            {
                if (item.ItemType == DriveTreeItemType.HyperVVirtualDisk)
                {
                    await LoadHyperVVirtualDiskChildrenAsync(item);
                }
                else
                {
                    List<DriveTreeItem> childItems = await Task.Run(() => BuildVolumeChildItems(item));
                    ReplaceChildren(item, childItems);
                }

                item.ChildrenLoaded = true;
                ApplyPendingSelectionState(item);
            }

            foreach (DriveTreeItem child in item.Children)
            {
                if (await PreSelectItemRecursiveAsync(child, pathToSelect))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeSelectionPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool PathCouldContainSelection(string containerPath, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(containerPath) || string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            return string.Equals(containerPath, targetPath, StringComparison.OrdinalIgnoreCase) ||
                   targetPath.StartsWith(containerPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   targetPath.StartsWith(containerPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Selects the HyperVSystem tree node whose normalized VirtualMachineName matches the saved name.
        /// Returns true if a match was found and selected.
        /// </summary>
        private bool PreSelectHyperVSystemByName(string savedVmName)
        {
            string normalizedSavedVmName = HyperVBackupTreeHelper.NormalizeSavedHyperVSystemName(savedVmName);
            if (string.IsNullOrWhiteSpace(normalizedSavedVmName))
                return false;

            DriveTreeItem? match = driveItems.FirstOrDefault(item =>
                item.ItemType == DriveTreeItemType.HyperVSystem &&
                (string.Equals(HyperVBackupTreeHelper.NormalizeSavedHyperVSystemName(item.VirtualMachineName), normalizedSavedVmName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(HyperVBackupTreeHelper.NormalizeSavedHyperVSystemName(item.FullPath), normalizedSavedVmName, StringComparison.OrdinalIgnoreCase)));

            if (match == null)
                return false;

            match.IsChecked = true;
            return true;
        }

        private async Task PreSelectHyperVGuestItemAsync(HyperVGuestSelectionInfo selection)
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
                await LoadHyperVVirtualDiskChildrenAsync(virtualDiskItem);
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
                    List<DriveTreeItem> childItems = await Task.Run(() => BuildVolumeChildItems(current));
                    ReplaceChildren(current, childItems);
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

        private static void ReplaceChildren(DriveTreeItem parentItem, IEnumerable<DriveTreeItem> childItems)
        {
            ArgumentNullException.ThrowIfNull(parentItem);
            ArgumentNullException.ThrowIfNull(childItems);

            parentItem.Children.Clear();
            foreach (DriveTreeItem childItem in childItems)
            {
                parentItem.Children.Add(childItem);
            }

            if (parentItem.IsChecked == true)
            {
                foreach (DriveTreeItem childItem in parentItem.Children.Where(child => child.ParticipatesInCheckState))
                {
                    childItem.IsChecked = true;
                }

                parentItem.RefreshCheckStateFromChildren();
            }
        }

        private void ApplyPendingSelectionState(DriveTreeItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            foreach (DriveTreeItem child in item.Children)
            {
                ApplyPendingSelectionStateRecursive(child);
            }

            item.RefreshCheckStateFromChildren();
        }

        private void ApplyPendingSelectionStateRecursive(DriveTreeItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (item.IsLoadingPlaceholder)
            {
                return;
            }

            GetPendingSelectionMatchState(item, out bool hasExactMatch, out bool hasDescendantMatch);

            if (hasExactMatch)
            {
                item.IsChecked = true;
                return;
            }

            if (hasDescendantMatch && item.IsChecked != true)
            {
                item.IsChecked = null;
            }

            foreach (DriveTreeItem child in item.Children)
            {
                ApplyPendingSelectionStateRecursive(child);
            }

            item.RefreshCheckStateFromChildren();
        }

        private void GetPendingSelectionMatchState(DriveTreeItem item, out bool hasExactMatch, out bool hasDescendantMatch)
        {
            ArgumentNullException.ThrowIfNull(item);

            hasExactMatch = false;
            hasDescendantMatch = false;

            string itemPath = NormalizeSelectionPath(item.FullPath);
            string resolvedPath = NormalizeSelectionPath(item.ResolvedPath);

            foreach (string pendingPath in _pendingSelectionPaths)
            {
                if ((!string.IsNullOrWhiteSpace(itemPath) && string.Equals(itemPath, pendingPath, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(resolvedPath) && string.Equals(resolvedPath, pendingPath, StringComparison.OrdinalIgnoreCase)))
                {
                    hasExactMatch = true;
                    hasDescendantMatch = true;
                    return;
                }

                if (PathCouldContainSelection(itemPath, pendingPath) || PathCouldContainSelection(resolvedPath, pendingPath))
                {
                    hasDescendantMatch = true;
                }
            }
        }

        private void RefreshLoadedSelectionStates()
        {
            foreach (DriveTreeItem rootItem in driveItems)
            {
                RefreshLoadedSelectionStateRecursive(rootItem);
            }
        }

        private static void RefreshLoadedSelectionStateRecursive(DriveTreeItem item)
        {
            foreach (DriveTreeItem child in item.Children)
            {
                RefreshLoadedSelectionStateRecursive(child);
            }

            item.RefreshCheckStateFromChildren();
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
                    RunPowerShell($"Dismount-VHD -Path '{EscapePowerShellSingleQuotedString(_hyperVDiskMountDirectories[virtualDiskPath].MountedDiskPath)}' -ErrorAction SilentlyContinue | Out-Null");
                }
                catch
                {
                }
            }

            foreach (MountedHyperVGuestTreeDisk mountedDisk in _hyperVDiskMountDirectories.Values)
            {
                foreach (string directory in mountedDisk.MountDirectories)
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
            ArgumentException.ThrowIfNullOrWhiteSpace(script);

            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
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
                string message = string.IsNullOrWhiteSpace(errors)
                    ? "The PowerShell command failed."
                    : StripCliXml(errors).Trim();
                throw new InvalidOperationException(message);
            }

            return output;
        }

        /// <summary>
        /// Extracts readable text from a PowerShell CLIXML error stream.
        /// If the string does not contain CLIXML markup, it is returned unchanged.
        /// </summary>
        private static string StripCliXml(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw;

            // PowerShell stderr starts with "#< CLIXML" when it wraps errors in XML
            const string clixmlMarker = "#< CLIXML";
            if (!raw.Contains(clixmlMarker, StringComparison.OrdinalIgnoreCase))
                return raw;

            try
            {
                // Extract all <S S="Error">...</S> text nodes — these carry the human-readable message
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    raw,
                    @"<S S=""Error"">(?<msg>.*?)</S>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                var lines = matches
                    .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups["msg"].Value)
                        .Replace("_x000D__x000A_", "\n", StringComparison.Ordinal)
                        .Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                return lines.Count > 0 ? string.Join("\n", lines) : raw;
            }
            catch
            {
                return raw;
            }
        }

        private static bool IsHyperVVirtualDiskSharingViolation(Exception ex)
        {
            // Walk the full exception chain so exceptions wrapped in AggregateException
            // or re-thrown with an inner cause are still caught correctly.
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
                    e.Message.Contains("0x80070020", StringComparison.OrdinalIgnoreCase) ||
                    (e is System.IO.IOException ioEx && (uint)System.Runtime.InteropServices.Marshal.GetHRForException(ioEx) == 0x80070020))
                    return true;
            }

            // AggregateException may flatten multiple inner exceptions
            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.Flatten().InnerExceptions)
                {
                    if (IsHyperVVirtualDiskSharingViolation(inner))
                        return true;
                }
            }

            return false;
        }

        private static string ResolveMountableHyperVVirtualDiskPath(string virtualDiskPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(virtualDiskPath);

            string script = $"$path = '{EscapePowerShellSingleQuotedString(virtualDiskPath)}'; while (-not [string]::IsNullOrWhiteSpace($path)) {{ $vhd = Get-VHD -Path $path -ErrorAction Stop; [Console]::WriteLine($vhd.Path); if ([string]::IsNullOrWhiteSpace($vhd.ParentPath)) {{ break }} $path = $vhd.ParentPath }}";
            string output = RunPowerShell(script);
            string resolvedPath = HyperVBackupTreeHelper.SelectMountableVirtualDiskPath(
                virtualDiskPath,
                output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

            if (!string.Equals(resolvedPath, virtualDiskPath, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"Using parent Hyper-V virtual disk fallback for {virtualDiskPath}: {resolvedPath}");
            }

            return resolvedPath;
        }

        private static List<MountedHyperVGuestExecutionPartition> MountHyperVGuestDiskReadOnlyCore(string virtualDiskPath, string mountRoot, out string mountedDiskPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(virtualDiskPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(mountRoot);

            static List<MountedHyperVGuestExecutionPartition> MountCore(string diskPath, string root)
            {
                string escapedPath = EscapePowerShellSingleQuotedString(diskPath);
                string escapedRoot = EscapePowerShellSingleQuotedString(root);
                string script = $"$vhd = Mount-VHD -Path '{escapedPath}' -ReadOnly -Passthru -ErrorAction Stop; $disk = $vhd | Get-Disk -ErrorAction Stop; Get-Partition -DiskNumber $disk.Number -ErrorAction Stop | Sort-Object PartitionNumber | ForEach-Object {{ $partition = $_; $mountPath = $null; $created = $false; if ($partition.AccessPaths) {{ $mountPath = @($partition.AccessPaths | Where-Object {{ $_ }}) | Select-Object -First 1; }} if ([string]::IsNullOrWhiteSpace($mountPath)) {{ $folder = Join-Path '{escapedRoot}' ('Partition' + $partition.PartitionNumber); New-Item -ItemType Directory -Path $folder -Force | Out-Null; Add-PartitionAccessPath -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber -AccessPath $folder -ErrorAction Stop | Out-Null; $mountPath = $folder; $created = $true; }} [Console]::WriteLine(($partition.PartitionNumber.ToString() + \"`t\" + $mountPath + \"`t\" + $created.ToString())); }}";
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

                return partitions;
            }

            mountedDiskPath = virtualDiskPath;

            try
            {
                return MountCore(virtualDiskPath, mountRoot);
            }
            catch (Exception ex) when (IsHyperVVirtualDiskSharingViolation(ex))
            {
                string fallbackDiskPath = ResolveMountableHyperVVirtualDiskPath(virtualDiskPath);
                if (string.Equals(fallbackDiskPath, virtualDiskPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw;
                }

                mountedDiskPath = fallbackDiskPath;
                return MountCore(fallbackDiskPath, mountRoot);
            }
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

            try
            {
                List<MountedHyperVGuestExecutionPartition> partitions = MountHyperVGuestDiskReadOnlyCore(virtualDiskPath, mountRoot, out string mountedDiskPath);
                return new MountedHyperVGuestExecutionDisk(mountedDiskPath, mountRoot, partitions);
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

        private async Task LoadHyperVVirtualDiskChildrenAsync(DriveTreeItem virtualDiskItem)
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

                // Run blocking PowerShell mount on a background thread
                (List<MountedHyperVGuestExecutionPartition> partitions, string mountedDiskPath) = await Task.Run(() =>
                {
                    List<MountedHyperVGuestExecutionPartition> p = MountHyperVGuestDiskReadOnlyCore(virtualDiskItem.VirtualDiskPath, diskMountRoot, out string mdp);
                    return (p, mdp);
                });

                List<string> mountDirectories = new();
                foreach (MountedHyperVGuestExecutionPartition partition in partitions)
                {
                    int partitionNumber = partition.PartitionNumber;
                    string mountPath = partition.MountPath;
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

                _hyperVDiskMountDirectories[virtualDiskItem.VirtualDiskPath] = new MountedHyperVGuestTreeDisk(mountedDiskPath, mountDirectories);

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
                if (IsHyperVVirtualDiskSharingViolation(ex))
                {
                    // Show alert and revert the node — leave no error placeholder in the tree.
                    virtualDiskItem.Children.Clear();
                    virtualDiskItem.ChildrenLoaded = false;
                    virtualDiskItem.IsExpanded = false;

                    CustomDialogService.ShowWarning(
                        "The virtual disk is locked because the VM is currently running.\n\n" +
                        "To back up individual guest partitions the VM must be stopped first.\n\n" +
                        "To back up the running VM, select the VM node in the tree instead.",
                        "Virtual Disk Locked");
                }
                else
                {
                    virtualDiskItem.Children.Add(new DriveTreeItem
                    {
                        Name = $"(Error mounting virtual disk: {ex.Message})",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = virtualDiskItem
                    });
                }
            }
        }

        private static List<DriveTreeItem> BuildVolumeChildItems(DriveTreeItem volumeItem)
        {
            try
            {
                var rootPath = string.IsNullOrWhiteSpace(volumeItem.ResolvedPath) ? volumeItem.FullPath : volumeItem.ResolvedPath;
                List<DriveTreeItem> childItems = new();
                
                System.Diagnostics.Debug.WriteLine($"=== LoadFoldersForVolume ===");
                System.Diagnostics.Debug.WriteLine($"Volume: {volumeItem.Name}");
                System.Diagnostics.Debug.WriteLine($"Path: '{rootPath}'");
                
                // Check if this is a system partition without drive letter
                if (rootPath.StartsWith("\\\\?\\Volume{"))
                {
                    childItems.Add(new DriveTreeItem
                    {
                        Name = "(System partition - cannot browse)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                    return childItems;
                }
                
                if (!Directory.Exists(rootPath))
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Directory does not exist: '{rootPath}'");
                    childItems.Add(new DriveTreeItem
                    {
                        Name = "(Volume not accessible)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                    return childItems;
                }

                System.Diagnostics.Debug.WriteLine($"Directory exists, enumerating folders and files...");

                var entriesAdded = 0;
                try
                {
                    foreach (DriveTreeItem childItem in BuildDirectoryChildItems(volumeItem, rootPath))
                    {
                        childItems.Add(childItem);
                        entriesAdded++;
                        System.Diagnostics.Debug.WriteLine($"  Added: {childItem.Name}");
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: Access denied to volume root");
                    childItems.Add(new DriveTreeItem
                    {
                        Name = "(Access Denied - Run as Administrator)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                    return childItems;
                }
                
                System.Diagnostics.Debug.WriteLine($"Total entries added: {entriesAdded}");
                
                // If no folders or files were accessible, show a message
                if (entriesAdded == 0)
                {
                    childItems.Add(new DriveTreeItem
                    {
                        Name = "(Empty or no accessible files/folders)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                }

                return childItems;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR in LoadFoldersForVolume: {ex.Message}\nStack: {ex.StackTrace}");
                return new List<DriveTreeItem>
                {
                    new()
                    {
                        Name = $"(Error: {ex.Message})",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    }
                };
            }
        }

        internal static List<DriveTreeItem> BuildDirectoryChildItems(DriveTreeItem parentItem, string rootPath)
        {
            ArgumentNullException.ThrowIfNull(parentItem);
            ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

            List<DriveTreeItem> childItems = new();

            foreach (string directory in Directory.GetDirectories(rootPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    DirectoryInfo dirInfo = new(directory);
                    childItems.Add(CreateDirectoryTreeItem(parentItem, dirInfo));
                }
                catch (UnauthorizedAccessException)
                {
                    childItems.Add(CreateAccessDeniedDirectoryTreeItem(parentItem, directory));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"  Error processing folder {directory}: {ex.Message}");
                }
            }

            foreach (string file in Directory.GetFiles(rootPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    FileInfo fileInfo = new(file);
                    childItems.Add(CreateFileTreeItem(parentItem, fileInfo));
                }
                catch (UnauthorizedAccessException)
                {
                    Debug.WriteLine($"  Access denied file: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"  Error processing file {file}: {ex.Message}");
                }
            }

            return childItems;
        }

        private static TreeViewItem CreateLoadingTreeViewItem(string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            return new TreeViewItem
            {
                Header = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Children =
                    {
                        new System.Windows.Controls.ProgressBar
                        {
                            IsIndeterminate = true,
                            Width = 16,
                            Height = 16,
                            Margin = new Thickness(0, 0, 6, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = message,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                },
                IsEnabled = false
            };
        }

        private static DriveTreeItem CreateDirectoryTreeItem(DriveTreeItem parentItem, DirectoryInfo dirInfo)
        {
            string folderName = AppendAttributesSuffix(dirInfo.Name, dirInfo.Attributes);

            DriveTreeItem folderItem = new()
            {
                Name = folderName,
                FullPath = BuildChildFullPath(parentItem, dirInfo.FullName, HyperVGuestSelectionKind.Folder),
                ResolvedPath = dirInfo.FullName,
                VirtualMachineName = parentItem.VirtualMachineName,
                VirtualDiskPath = parentItem.VirtualDiskPath,
                PartitionNumber = parentItem.PartitionNumber,
                ItemType = DriveTreeItemType.Folder,
                Parent = parentItem
            };

            try
            {
                if (Directory.GetDirectories(dirInfo.FullName).Length > 0 || Directory.GetFiles(dirInfo.FullName).Length > 0)
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
            }

            return folderItem;
        }

        private static DriveTreeItem CreateAccessDeniedDirectoryTreeItem(DriveTreeItem parentItem, string directoryPath)
        {
            return new DriveTreeItem
            {
                Name = $"{Path.GetFileName(directoryPath)} [Access Denied]",
                FullPath = BuildChildFullPath(parentItem, directoryPath, HyperVGuestSelectionKind.Folder),
                ResolvedPath = directoryPath,
                VirtualMachineName = parentItem.VirtualMachineName,
                VirtualDiskPath = parentItem.VirtualDiskPath,
                PartitionNumber = parentItem.PartitionNumber,
                ItemType = DriveTreeItemType.Folder,
                Parent = parentItem
            };
        }

        private static DriveTreeItem CreateFileTreeItem(DriveTreeItem parentItem, FileInfo fileInfo)
        {
            return new DriveTreeItem
            {
                Name = AppendAttributesSuffix(fileInfo.Name, fileInfo.Attributes),
                FullPath = BuildChildFullPath(parentItem, fileInfo.FullName, HyperVGuestSelectionKind.File),
                ResolvedPath = fileInfo.FullName,
                VirtualMachineName = parentItem.VirtualMachineName,
                VirtualDiskPath = parentItem.VirtualDiskPath,
                PartitionNumber = parentItem.PartitionNumber,
                ItemType = DriveTreeItemType.File,
                Size = fileInfo.Length,
                Parent = parentItem
            };
        }

        private static string AppendAttributesSuffix(string name, FileAttributes attributes)
        {
            if ((attributes & FileAttributes.System) == FileAttributes.System)
            {
                return name + " [System]";
            }

            if ((attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
            {
                return name + " [Hidden]";
            }

            return name;
        }

        private static string BuildChildFullPath(DriveTreeItem parentItem, string resolvedPath, HyperVGuestSelectionKind selectionKind)
        {
            if (!HyperVGuestSelectionPath.IsEncodedPath(parentItem.FullPath))
            {
                return resolvedPath;
            }

            return HyperVGuestSelectionPath.Encode(
                selectionKind,
                parentItem.VirtualMachineName,
                parentItem.VirtualDiskPath,
                parentItem.PartitionNumber,
                BuildHyperVGuestRelativePath(parentItem, resolvedPath));
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
            if (_isLoadingDrives)
            {
                _reloadDrivesAfterLoad = true;
                return;
            }

            _isLoadingDrives = true;

            // Snapshot checked paths so selections survive the tree rebuild.
            HashSet<string> checkedPaths = new(StringComparer.OrdinalIgnoreCase);
            GetCheckedItemsRecursive_FullPaths(driveItems, checkedPaths);

            try
            {
                // Show loading overlay
                if (loadingOverlay != null)
                    loadingOverlay.Visibility = Visibility.Visible;

                driveItems.Clear();
                treeViewDrives.ItemsSource = null;

                // Load physical drives and volumes
                await LoadPhysicalDrives();

                // Load Hyper-V systems
                await LoadHyperVSystems();

                // Load network locations
                await LoadNetworkDrives();

                ApplyBackupTypeSelectionRestrictions();

                // Restore previously checked state
                if (checkedPaths.Count > 0)
                    RestoreCheckedPaths(driveItems, checkedPaths);

                treeViewDrives.ItemsSource = driveItems;
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError($"Error loading drives: {ex.Message}\n\nDetails: {ex.InnerException?.Message}", "Error");
                throw; // Re-throw to be caught by caller
            }
            finally
            {
                bool reloadAfterLoad = _reloadDrivesAfterLoad;
                _reloadDrivesAfterLoad = false;
                _isLoadingDrives = false;

                // Hide loading overlay
                if (loadingOverlay != null)
                    loadingOverlay.Visibility = Visibility.Collapsed;

                if (reloadAfterLoad)
                {
                    await LoadDrives();
                }
            }
        }

        private static void GetCheckedItemsRecursive_FullPaths(IEnumerable<DriveTreeItem> items, HashSet<string> paths)
        {
            foreach (var item in items)
            {
                if (item.IsChecked == true && !string.IsNullOrWhiteSpace(item.FullPath))
                    paths.Add(item.FullPath);
                GetCheckedItemsRecursive_FullPaths(item.Children, paths);
            }
        }

        private static void RestoreCheckedPaths(IEnumerable<DriveTreeItem> items, HashSet<string> paths)
        {
            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.FullPath) && paths.Contains(item.FullPath))
                    item.IsChecked = true;
                RestoreCheckedPaths(item.Children, paths);
            }
        }

        internal static bool RequiresLazyLoad(DriveTreeItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return (item.ItemType == DriveTreeItemType.Volume ||
                    item.ItemType == DriveTreeItemType.HyperVVolume ||
                    item.ItemType == DriveTreeItemType.NetworkDrive ||
                    item.ItemType == DriveTreeItemType.NetworkShare ||
                    (item.ItemType == DriveTreeItemType.Folder && !string.IsNullOrWhiteSpace(item.ResolvedPath)) ||
                    item.ItemType == DriveTreeItemType.HyperVVirtualDisk) &&
                   !item.ChildrenLoaded;
        }

        private void TreeItemCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox checkBox || checkBox.DataContext is not DriveTreeItem item)
            {
                return;
            }

            bool isChecked = checkBox.IsChecked == true;

            item.IsChecked = isChecked;
            Debug.WriteLine($"[Checkbox] {(isChecked ? "Checked" : "Unchecked")}: {item.Name} ({item.ItemType})");

            if (item.ItemType == DriveTreeItemType.Volume || item.ItemType == DriveTreeItemType.Disk)
            {
                if (isChecked)
                {
                    hasSourceSelected = true;
                    volumeConfigShown = false;
                    Debug.WriteLine($"[Checkbox] {item.ItemType} checked, hasSourceSelected = true, hasTargetSelected = {hasTargetSelected}");
                    CheckAndShowVolumeConfiguration();
                }
                else
                {
                    hasSourceSelected = GetCheckedDriveItems().Any(i => i.ItemType == DriveTreeItemType.Volume || i.ItemType == DriveTreeItemType.Disk);
                    Debug.WriteLine($"[Checkbox] {item.ItemType} unchecked, hasSourceSelected = {hasSourceSelected}");
                }
            }

            e.Handled = true;
        }

        private void TreeItem_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: DriveTreeItem item } || item.ItemType != DriveTreeItemType.NetworkBrowser)
            {
                return;
            }

            NetworkPathDialog dialog = new();
            if (dialog.ShowDialog() == true)
            {
                AddNetworkPathToTree(dialog.NetworkPath);
            }

            e.Handled = true;
        }

        private async void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem treeViewItem || e.Source != treeViewItem || treeViewItem.DataContext is not DriveTreeItem item || !RequiresLazyLoad(item))
            {
                return;
            }

            item.IsExpanded = true;

            if (item.ItemType == DriveTreeItemType.HyperVVirtualDisk)
            {
                try
                {
                    await LoadHyperVVirtualDiskChildrenAsync(item);
                    ApplyPendingSelectionState(item);
                }
                catch (Exception ex)
                {
                    item.Children.Clear();
                    item.ChildrenLoaded = false;

                    if (!IsHyperVVirtualDiskSharingViolation(ex))
                    {
                        item.Children.Add(new DriveTreeItem
                        {
                            Name = $"(Error mounting virtual disk: {ex.Message})",
                            ItemType = DriveTreeItemType.Folder,
                            Parent = item
                        });
                    }
                }

                item.ChildrenLoaded = item.Children.Count > 0;

                if (item.Children.Count == 0)
                {
                    treeViewItem.IsExpanded = false;
                }

                return;
            }

            List<DriveTreeItem> childItems = await Task.Run(() => BuildVolumeChildItems(item));
            ReplaceChildren(item, childItems);
            item.ChildrenLoaded = true;
            ApplyPendingSelectionState(item);
        }

        private async Task LoadPhysicalDrives()
        {
            List<DriveTreeItem> disks = await Task.Run(() =>
            {
                List<DriveTreeItem> discoveredDisks = new();

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

                                discoveredDisks.Add(diskItem);
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
                    
                    System.Diagnostics.Debug.WriteLine($"=== Completed LoadPhysicalDrives: {discoveredDisks.Count} disks loaded ===");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR in LoadPhysicalDrives: {ex.Message}\nStack: {ex.StackTrace}");
                    Dispatcher.Invoke(() =>
                        CustomDialogService.ShowError($"Error loading physical drives: {ex.Message}\n\nDetails: {ex.GetType().Name}\n\nPlease check Output window for details.", 
                            "Error"));
                }

                return discoveredDisks;
            });

            foreach (DriveTreeItem disk in disks)
            {
                driveItems.Add(disk);
            }
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
                                        IsBootVolume = volumeType.Contains("EFI"),
                                        IsHiddenPartition = true
                                    };

                                    // These volumes typically can't be browsed
                                    volumeItem.Children.Add(new DriveTreeItem
                                    {
                                        Name = "(System partition - not accessible)",
                                        ItemType = DriveTreeItemType.Folder,
                                        Parent = volumeItem
                                    });

                                    if (_showHiddenPartitions)
                                    {
                                        diskItem.Children.Add(volumeItem);
                                        volumesFound = true;
                                    }

                                    System.Diagnostics.Debug.WriteLine($"      {(_showHiddenPartitions ? "Added" : "Skipped (hidden)")} system volume: {volumeType}");
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

        private static IReadOnlyList<int> GetCurrentSystemDiskIndexes()
        {
            List<int> indexes = new();

            try
            {
                string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System))?.TrimEnd('\\') ?? string.Empty;
                if (string.IsNullOrWhiteSpace(systemRoot))
                {
                    return indexes;
                }

                using var partitionSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{systemRoot}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (ManagementObject partition in partitionSearcher.Get())
                {
                    string? partitionId = partition["DeviceID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(partitionId))
                    {
                        continue;
                    }

                    using var driveSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                    foreach (ManagementObject drive in driveSearcher.Get())
                    {
                        if (int.TryParse(drive["Index"]?.ToString(), out int diskIndex) && !indexes.Contains(diskIndex))
                        {
                            indexes.Add(diskIndex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetCurrentSystemDiskIndexes warning: {ex.Message}");
            }

            return indexes;
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
            List<DriveTreeItem> hyperVSystems = await Task.Run(() =>
            {
                List<DriveTreeItem> discoveredSystems = new();

                try
                {
                    Dictionary<string, DriveTreeItem> systemLookup = new(StringComparer.OrdinalIgnoreCase);

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

                            systemLookup[normalizedVmName] = new DriveTreeItem
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
                        if (!systemLookup.TryGetValue(diskInfo.VirtualMachineName, out DriveTreeItem? hyperVItem))
                        {
                            hyperVItem = new DriveTreeItem
                            {
                                Name = $"Hyper-V: {diskInfo.VirtualMachineDisplayName}",
                                FullPath = diskInfo.VirtualMachineDisplayName,
                                VirtualMachineName = diskInfo.VirtualMachineName,
                                ItemType = DriveTreeItemType.HyperVSystem
                            };
                            systemLookup[diskInfo.VirtualMachineName] = hyperVItem;
                        }

                        AddHyperVVirtualDiskItem(hyperVItem, diskInfo);
                    }

                    foreach (DriveTreeItem hyperVSystem in systemLookup.Values)
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

                    discoveredSystems.AddRange(systemLookup.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
                }
                catch
                {
                }

                return discoveredSystems;
            });

            foreach (DriveTreeItem hyperVSystem in hyperVSystems)
            {
                driveItems.Add(hyperVSystem);
            }
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

            disks = EnumerateHyperVVirtualDiskInfosFromWmi();
            if (disks.Count > 0)
            {
                Debug.WriteLine($"Enumerated {disks.Count} Hyper-V virtual disks using managed WMI fallback.");
                return disks;
            }

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

        private IReadOnlyList<HyperVVirtualDiskInfo> EnumerateHyperVVirtualDiskInfosFromWmi()
        {
            foreach (string scopePath in new[] { @"\\.\ROOT\virtualization\v2", @"\\.\ROOT\virtualization" })
            {
                try
                {
                    var scope = new ManagementScope(scopePath);
                    scope.Connect();

                    var disks = EnumerateHyperVVirtualDiskInfosFromWmiScope(scope);
                    if (disks.Count > 0)
                    {
                        return disks;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Managed Hyper-V WMI disk fallback failed for {scopePath}: {ex.Message}");
                }
            }

            return Array.Empty<HyperVVirtualDiskInfo>();
        }

        private static IReadOnlyList<HyperVVirtualDiskInfo> EnumerateHyperVVirtualDiskInfosFromWmiScope(ManagementScope scope)
        {
            Dictionary<string, HyperVVirtualDiskInfo> disks = new(StringComparer.OrdinalIgnoreCase);
            using var vmSearcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT ElementName, EnabledState, __PATH FROM Msvm_ComputerSystem WHERE Caption='Virtual Machine'"));

            foreach (ManagementObject vm in vmSearcher.Get().OfType<ManagementObject>())
            {
                string vmName = RestoreWindowNew.RegularHyperVRestoreHelper.NormalizeHyperVVmName(vm["ElementName"]?.ToString());
                string vmPath = vm.Path?.Path ?? vm["__PATH"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(vmName) || string.IsNullOrWhiteSpace(vmPath))
                {
                    continue;
                }

                string vmDisplayName = HyperVBackupTreeHelper.BuildVmDisplayName(vmName, vm["EnabledState"]);
                string query = $"ASSOCIATORS OF {{{vmPath}}} WHERE AssocClass=Msvm_SystemDevice ResultClass=Msvm_StorageAllocationSettingData";
                using var storageSearcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
                foreach (ManagementObject storage in storageSearcher.Get().OfType<ManagementObject>())
                {
                    if (!HyperVBackupTreeHelper.IsVirtualDiskResource(storage["ResourceSubType"]?.ToString()))
                    {
                        continue;
                    }

                    foreach (string hostResource in HyperVBackupTreeHelper.GetHostResources(storage["HostResource"]))
                    {
                        if (string.IsNullOrWhiteSpace(hostResource))
                        {
                            continue;
                        }

                        string normalizedPath = hostResource.Trim().Trim('"');
                        disks[vmName + "|" + normalizedPath] = new HyperVVirtualDiskInfo(vmName, vmDisplayName, normalizedPath);
                    }
                }
            }

            return disks.Values.ToArray();
        }

        private async Task LoadNetworkDrives()
        {
            DriveTreeItem? networkRoot = await Task.Run(() =>
            {
                try
                {
                    var discoveredNetworkRoot = new DriveTreeItem
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
                                    Parent = discoveredNetworkRoot
                                };

                                // Add placeholder for folders
                                networkDrive.Children.Add(new DriveTreeItem
                                {
                                    Name = "Loading...",
                                    ItemType = DriveTreeItemType.Folder,
                                    Parent = networkDrive
                                });

                                discoveredNetworkRoot.Children.Add(networkDrive);
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
                        Parent = discoveredNetworkRoot
                    };

                    discoveredNetworkRoot.Children.Add(addNetworkPath);

                    // Only add Network Locations if there are mapped drives or the add option
                    return discoveredNetworkRoot.Children.Count > 0
                        ? discoveredNetworkRoot
                        : null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading network drives: {ex.Message}");
                }

                return null;
            });

            if (networkRoot != null)
            {
                foreach (string savedPath in LoadSavedNetworkPaths())
                {
                    TryInsertNetworkPath(networkRoot, savedPath, saveAfterInsert: false);
                }

                driveItems.Add(networkRoot);
            }
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

                if (!TryInsertNetworkPath(networkRoot, uncPath, saveAfterInsert: true))
                {
                    CustomDialogService.ShowInfo($"Network path already added:\n{uncPath}", "Duplicate Path");
                    return;
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

        private static List<string> LoadSavedNetworkPaths()
        {
            try
            {
                if (!File.Exists(SavedNetworkPathsFilePath))
                {
                    return new List<string>();
                }

                string json = File.ReadAllText(SavedNetworkPathsFilePath);
                return JsonSerializer.Deserialize<List<string>>(json)
                    ?.Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(NormalizeNetworkPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading saved network paths: {ex.Message}");
                return new List<string>();
            }
        }

        private static void SaveNetworkPaths(IEnumerable<string> networkPaths)
        {
            ArgumentNullException.ThrowIfNull(networkPaths);

            try
            {
                string? directory = Path.GetDirectoryName(SavedNetworkPathsFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                List<string> normalizedPaths = networkPaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(NormalizeNetworkPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string json = JsonSerializer.Serialize(normalizedPaths, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SavedNetworkPathsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving network paths: {ex.Message}");
            }
        }

        private static string NormalizeNetworkPath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            return path.Trim().TrimEnd('\\') + "\\";
        }

        private bool TryInsertNetworkPath(DriveTreeItem networkRoot, string uncPath, bool saveAfterInsert)
        {
            ArgumentNullException.ThrowIfNull(networkRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(uncPath);

            string normalizedPath = NormalizeNetworkPath(uncPath);
            DriveTreeItem? existing = networkRoot.Children
                .FirstOrDefault(c => c.ItemType == DriveTreeItemType.NetworkShare &&
                                     c.FullPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                return false;
            }

            string[] pathParts = normalizedPath.TrimEnd('\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            string displayName = pathParts.Length >= 2
                ? $"\\\\{pathParts[0]}\\{pathParts[1]}"
                : normalizedPath;

            DriveTreeItem networkShare = new()
            {
                Name = $"{displayName} - Network Share",
                FullPath = normalizedPath,
                ItemType = DriveTreeItemType.NetworkShare,
                IsRemovableNetworkPath = true,
                Parent = networkRoot
            };

            networkShare.Children.Add(new DriveTreeItem
            {
                Name = "Loading...",
                ItemType = DriveTreeItemType.Folder,
                Parent = networkShare
            });

            DriveTreeItem? addOption = networkRoot.Children.FirstOrDefault(c => c.ItemType == DriveTreeItemType.NetworkBrowser);
            if (addOption != null)
            {
                int index = networkRoot.Children.IndexOf(addOption);
                networkRoot.Children.Insert(index, networkShare);
            }
            else
            {
                networkRoot.Children.Add(networkShare);
            }

            if (saveAfterInsert)
            {
                PersistCurrentNetworkPaths(networkRoot);
            }

            return true;
        }

        private void RemoveNetworkPath_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: DriveTreeItem item } ||
                item.ItemType != DriveTreeItemType.NetworkShare ||
                !item.IsRemovableNetworkPath ||
                item.Parent == null)
            {
                return;
            }

            CustomDialogResult result = CustomDialogService.ShowQuestion(
                this,
                $"Do you really want to delete this saved network path?\n\n{item.FullPath}",
                "Delete Network Path");

            if (result != CustomDialogResult.Yes)
            {
                return;
            }

            item.Parent.Children.Remove(item);
            PersistCurrentNetworkPaths(item.Parent);
            RefreshTreeView();
        }

        private static void PersistCurrentNetworkPaths(DriveTreeItem networkRoot)
        {
            ArgumentNullException.ThrowIfNull(networkRoot);

            SaveNetworkPaths(networkRoot.Children
                .Where(child => child.ItemType == DriveTreeItemType.NetworkShare && child.IsRemovableNetworkPath)
                .Select(child => child.FullPath));
        }

        /// <summary>
        /// Refreshes the entire tree view
        /// </summary>
        private void RefreshTreeView()
        {
            treeViewDrives.ItemsSource = null;
            treeViewDrives.ItemsSource = driveItems;
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

        private async void ShowHiddenPartitions_Click(object sender, RoutedEventArgs e)
        {
            _showHiddenPartitions = chkShowHiddenPartitions.IsChecked == true;
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

            bool isSelectedFilesBackup = rbSelectedFilesAndFolders?.IsChecked == true;
            if (pnlSelectedFilesRetention != null)
            {
                pnlSelectedFilesRetention.Visibility = isSelectedFilesBackup ? Visibility.Visible : Visibility.Collapsed;
            }

            // Show clone retention panel for clone backup types
            bool isCloneBackup = rbCloneVirtual?.IsChecked == true || rbCloneHyperV?.IsChecked == true;
            if (pnlCloneRetention != null)
            {
                pnlCloneRetention.Visibility = isCloneBackup ? Visibility.Visible : Visibility.Collapsed;
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

            UpdateCloneHyperVRenameOptions();

            ApplyBackupTypeSelectionRestrictions();

            if (rbCloneHyperV?.IsChecked == true && !_isInitializingJobData && _hasCompletedInitialDriveLoad)
            {
                _ = ReloadDriveTreeForBackupTypeAsync();
            }
        }

        private void RenameHyperVSystem_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateCloneHyperVRenameOptions();
        }

        private void UpdateCloneHyperVRenameOptions()
        {
            if (pnlCloneHyperVRename == null || pnlRenameHyperVSystemName == null)
            {
                return;
            }

            bool isCloneHyperV = rbCloneHyperV?.IsChecked == true;
            bool renameSelected = isCloneHyperV && chkRenameHyperVSystem?.IsChecked == true;

            pnlCloneHyperVRename.Visibility = isCloneHyperV ? Visibility.Visible : Visibility.Collapsed;
            pnlRenameHyperVSystemName.Visibility = renameSelected ? Visibility.Visible : Visibility.Collapsed;

            if (!isCloneHyperV && chkRenameHyperVSystem != null)
            {
                chkRenameHyperVSystem.IsChecked = false;
            }
        }

        internal static bool IsValidWindowsComputerName(string? computerName)
        {
            if (string.IsNullOrWhiteSpace(computerName))
            {
                return false;
            }

            string trimmedName = computerName.Trim();
            if (trimmedName.Length is < 1 or > 15)
            {
                return false;
            }

            if (trimmedName.StartsWith("-", StringComparison.Ordinal) ||
                trimmedName.EndsWith("-", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (char ch in trimmedName)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private async Task ReloadDriveTreeForBackupTypeAsync()
        {
            try
            {
                await LoadDrives();
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError($"Error reloading drives: {ex.Message}", "Error");
            }
        }

        private void ApplyBackupTypeSelectionRestrictions()
        {
            bool hyperVSystemsOnly = rbCloneHyperV?.IsChecked == true;

            foreach (DriveTreeItem item in driveItems)
            {
                ApplySelectionRestrictionRecursive(item, hyperVSystemsOnly);
            }
        }

        private static void ApplySelectionRestrictionRecursive(DriveTreeItem item, bool hyperVSystemsOnly)
        {
            bool isHyperVSystem = item.ItemType == DriveTreeItemType.HyperVSystem;
            bool isSystemDiskSelection = item.ItemType == DriveTreeItemType.Disk;
            bool isNetworkBrowser = item.ItemType == DriveTreeItemType.NetworkBrowser;

            item.IsSelectionEnabled = !hyperVSystemsOnly || isHyperVSystem || isSystemDiskSelection || isNetworkBrowser;

            if (!item.IsSelectionEnabled)
            {
                item.IsChecked = false;
            }

            foreach (DriveTreeItem child in item.Children)
            {
                ApplySelectionRestrictionRecursive(child, hyperVSystemsOnly);
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
            // EnsureWindowWithinScreenBounds is now called within AdjustWindowHeightForEncryption
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
                if (chkShowEncryptionPassword != null)
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

            // Ensure window stays within screen bounds after height adjustment
            EnsureWindowWithinScreenBounds();
        }

        /// <summary>
        /// Ensures the window is positioned completely within the screen's working area,
        /// adjusting position if it extends beyond screen bounds (e.g., below taskbar)
        /// </summary>
        private void EnsureWindowWithinScreenBounds()
        {
            if (WindowState == WindowState.Maximized)
            {
                return;
            }

            double workAreaHeight = SystemParameters.WorkArea.Height;
            double workAreaTop = SystemParameters.WorkArea.Top;

            // Check if window extends below the working area
            if (Top + Height > workAreaTop + workAreaHeight)
            {
                // Reposition window so bottom aligns with work area bottom
                Top = Math.Max(workAreaTop, workAreaTop + workAreaHeight - Height);
            }

            // Ensure top of window is not above screen
            if (Top < workAreaTop)
            {
                Top = workAreaTop;
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
                    string backupArchivePath = GetBackupArchivePath(job.DestinationPath, job.Name);

                    if (job.Type == BackupType.CloneToVirtualDisk)
                    {
                        ExecuteCloneToVirtualDiskJob(job, progressCallback);

                        Dispatcher.Invoke(() =>
                        {
                            progressBar.Value = 100;
                            txtProgress.Text = $"Virtual disk clone completed!";
                        });

                        return;
                    }

                    if (job.Type == BackupType.CloneHyperVSystem)
                    {
                        ExecuteCloneHyperVSystemJob(job, progressCallback);

                        Dispatcher.Invoke(() =>
                        {
                            progressBar.Value = 100;
                            txtProgress.Text = "Clone Hyper-V System completed!";
                        });

                        return;
                    }

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
                        var diskDestPath = backupArchivePath;

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
                        var volumeDestPath = backupArchivePath;

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
                                        backupArchivePath,
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
                                    if (!HasBackupArchive(job.DestinationPath, job.Name))
                                    {
                                        result = BackupEngineInterop.BackupFiles(
                                            resolvedSourcePath,
                                            backupArchivePath,
                                            null,
                                            0,
                                            progressCallback,
                                            null);

                                        if (result != 0)
                                        {
                                            var errorBuffer = new StringBuilder(4096);
                                            BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                            throw new Exception($"Initial full backup failed: {errorBuffer}");
                                        }

                                        break;
                                    }

                                    result = BackupEngineInterop.CreateIncrementalBackup(
                                        resolvedSourcePath,
                                        backupArchivePath,
                                        backupArchivePath,
                                        progressCallback);

                                    if (result != 0)
                                    {
                                        var errorBuffer = new StringBuilder(4096);
                                        BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                        throw new Exception($"Incremental backup failed: {errorBuffer}");
                                    }
                                    break;

                                case BackupType.Differential:
                                    if (!HasBackupArchive(job.DestinationPath, job.Name))
                                    {
                                        result = BackupEngineInterop.BackupFiles(
                                            resolvedSourcePath,
                                            backupArchivePath,
                                            null,
                                            0,
                                            progressCallback,
                                            null);

                                        if (result != 0)
                                        {
                                            var errorBuffer = new StringBuilder(4096);
                                            BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                            throw new Exception($"Initial full backup failed: {errorBuffer}");
                                        }

                                        break;
                                    }

                                    result = BackupEngineInterop.CreateDifferentialBackup(
                                        resolvedSourcePath,
                                        backupArchivePath,
                                        backupArchivePath,
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
                        string password = BackupEncryptionService.UnprotectPassword(job.ProtectedEncryptionPassword);
                        BackupEncryptionService.EncryptFile(backupArchivePath, backupArchivePath, password);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Backup execution failed: {ex.Message}", ex);
                }
            });
        }

        private static string GetBackupArchivePath(string destPath, string jobName)
        {
            return Path.Combine(destPath, $"{jobName}.ssb");
        }

        public static void ExecuteCloneHyperVSystemJob(BackupJob job, BackupEngineInterop.ProgressCallback progressCallback)
        {
            CloneHyperVPaths clonePaths = CreateCloneHyperVPaths(job);
            bool renameRequested = job.RenameHyperVSystem && !string.IsNullOrWhiteSpace(job.RenameHyperVSystemName);

            progressCallback(1, $"Starting Hyper-V System Clone '{job.Name}'...");

            if (job.HyperVMachines.Count > 0)
            {
                string sourceVmName = job.HyperVMachines[0];
                progressCallback(5, $"Exporting Hyper-V VM '{sourceVmName}' for clone...");
                CreateCloneHyperVVirtualDiskFromVm(sourceVmName, clonePaths, job.RenameHyperVSystem);
            }
            else if (job.Target == BackupTarget.Disk && job.SourcePaths.Count > 0)
            {
                progressCallback(5, $"Cloning selected disk into {Path.GetFileName(clonePaths.VirtualDiskPath)}...");
                CreateCloneHyperVVirtualDiskFromDisk(job, clonePaths.VirtualDiskPath, progressCallback);
            }
            else
            {
                throw new InvalidOperationException("Clone Hyper-V System requires either a selected Hyper-V VM or a selected disk.");
            }

            bool shouldScheduleSetupCl = renameRequested;

            progressCallback(90, $"Creating Hyper-V VM '{clonePaths.VmName}'...");
            CreateCloneHyperVVirtualMachine(clonePaths.VmName, clonePaths.HyperVSystemDirectory, clonePaths.VirtualDiskPath);

            if (shouldScheduleSetupCl)
            {
                progressCallback(93, $"Scheduling SetupCl for '{clonePaths.VmName}'...");
                ScheduleSetupClPendingRequest(clonePaths.VirtualDiskPath, clonePaths.VmName);
            }

            if (renameRequested)
            {
                progressCallback(95, $"Regenerating MAC address for '{clonePaths.VmName}'...");
                RegenerateHyperVVirtualMachineMacAddress(clonePaths.VmName);
            }

            progressCallback(100, $"Clone Hyper-V System completed: {clonePaths.VmName}");
        }

        internal static CloneHyperVPaths CreateCloneHyperVPaths(BackupJob job)
        {
            ArgumentNullException.ThrowIfNull(job);

            if (string.IsNullOrWhiteSpace(job.DestinationPath))
            {
                throw new InvalidOperationException("Clone Hyper-V System requires a destination folder.");
            }

            bool renameRequested = job.RenameHyperVSystem && !string.IsNullOrWhiteSpace(job.RenameHyperVSystemName);
            bool diskClone = job.Target == BackupTarget.Disk && (job.SourcePaths?.Count > 0);
            string vmName = renameRequested
                ? job.RenameHyperVSystemName!.Trim()
                : diskClone
                    ? Environment.MachineName
                    : job.Name;
            string rootDirectoryName = renameRequested
                ? vmName
                : job.Name;
            string rootDirectory = Path.Combine(job.DestinationPath, rootDirectoryName);
            string hyperVSystemDirectory = Path.Combine(rootDirectory, "HyperVSys");
            string hyperVDiskDirectory = Path.Combine(rootDirectory, "HyperVDisk");

            Directory.CreateDirectory(rootDirectory);
            Directory.CreateDirectory(hyperVSystemDirectory);
            Directory.CreateDirectory(hyperVDiskDirectory);

            string virtualDiskPath = Path.Combine(hyperVDiskDirectory, $"{vmName}.vhdx");
            return new CloneHyperVPaths(rootDirectory, hyperVSystemDirectory, hyperVDiskDirectory, virtualDiskPath, vmName);
        }

        public static bool ExecuteCloneToVirtualDiskJob(BackupJob job, BackupEngineInterop.ProgressCallback progressCallback)
        {
            ArgumentNullException.ThrowIfNull(job);
            ArgumentNullException.ThrowIfNull(progressCallback);

            string virtualDiskPath = job.GetVirtualDiskClonePath();
            bool cloneAsDisk = job.ShouldCloneToVirtualDiskAsDisk();

            progressCallback(0, cloneAsDisk
                ? $"Cloning selected source into virtual disk {Path.GetFileName(virtualDiskPath)}..."
                : $"Cloning selected volume into virtual disk {Path.GetFileName(virtualDiskPath)}...");

            string temporaryArchiveDirectory = Path.Combine(Path.GetTempPath(), "SecureServerBackup", "VirtualDiskClone", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryArchiveDirectory);

            string temporaryArchivePath = GetBackupArchivePath(temporaryArchiveDirectory, job.Name);

            try
            {
                int result;
                if (cloneAsDisk)
                {
                    string diskPath = job.SourcePaths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                        ?? throw new InvalidOperationException("Clone to Virtual Disk requires a selected source disk.");
                    string diskNumStr = diskPath.Replace("\\\\?\\PHYSICALDRIVE", "").Replace("\\\\.\\PHYSICALDRIVE", "");
                    if (!int.TryParse(diskNumStr, out int diskNum))
                    {
                        throw new InvalidOperationException($"Invalid disk path format: {diskPath}");
                    }

                    result = BackupEngineInterop.BackupDisk(
                        diskNum,
                        temporaryArchivePath,
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
                        throw new InvalidOperationException($"Disk capture for virtual disk clone failed: {errorBuffer}");
                    }
                }
                else
                {
                    string volumePath = job.SourcePaths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                        ?? throw new InvalidOperationException("Clone to Virtual Disk requires a selected source volume.");

                    result = BackupEngineInterop.BackupVolume(
                        volumePath,
                        temporaryArchivePath,
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
                        throw new InvalidOperationException($"Volume capture for virtual disk clone failed: {errorBuffer}");
                    }
                }

                CreateHyperVVirtualDiskFromArchive(temporaryArchivePath, virtualDiskPath, cloneAsDisk, progressCallback);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temporaryArchiveDirectory))
                    {
                        Directory.Delete(temporaryArchiveDirectory, recursive: true);
                    }
                }
                catch (Exception cleanupEx)
                {
                    Debug.WriteLine($"Temporary virtual disk clone cleanup warning: {cleanupEx.Message}");
                }
            }

            progressCallback(100, $"Virtual disk clone completed: {Path.GetFileName(virtualDiskPath)}");
            return true;
        }

        private static void CreateCloneHyperVVirtualDiskFromDisk(BackupJob job, string virtualDiskPath, BackupEngineInterop.ProgressCallback progressCallback)
        {
            string temporaryArchiveDirectory = Path.Combine(Path.GetTempPath(), "SecureServerBackup", "CloneHyperVSystem", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryArchiveDirectory);

            try
            {
                string temporaryArchivePath = GetBackupArchivePath(temporaryArchiveDirectory, job.Name);
                string diskPath = job.SourcePaths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                    ?? throw new InvalidOperationException("Clone Hyper-V System requires a selected source disk.");
                string diskNumStr = diskPath.Replace("\\\\?\\PHYSICALDRIVE", "").Replace("\\\\.\\PHYSICALDRIVE", "");
                if (!int.TryParse(diskNumStr, out int diskNum))
                {
                    throw new InvalidOperationException($"Invalid disk path format: {diskPath}");
                }

                int result = BackupEngineInterop.BackupDisk(
                    diskNum,
                    temporaryArchivePath,
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
                    throw new InvalidOperationException($"Disk capture for Clone Hyper-V System failed: {errorBuffer}");
                }

                CreateHyperVVirtualDiskFromArchive(temporaryArchivePath, virtualDiskPath, restoreAsDisk: true, progressCallback);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temporaryArchiveDirectory))
                    {
                        Directory.Delete(temporaryArchiveDirectory, recursive: true);
                    }
                }
                catch (Exception cleanupEx)
                {
                    Debug.WriteLine($"Clone Hyper-V System temp cleanup warning: {cleanupEx.Message}");
                }
            }
        }

        private static void CreateCloneHyperVVirtualDiskFromVm(string sourceVmName, CloneHyperVPaths clonePaths, bool renameExportedArtifacts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceVmName);
            ArgumentNullException.ThrowIfNull(clonePaths);

            PrepareCloneHyperVExportDirectory(clonePaths.HyperVSystemDirectory);
            string exportRootPath = ExportHyperVVmWithPowerShell(sourceVmName, clonePaths.HyperVSystemDirectory);

            if (renameExportedArtifacts)
            {
                RenameCloneHyperVExportArtifacts(exportRootPath, sourceVmName, clonePaths.VmName);
            }

            string sourceDiskPath = FindPrimaryHyperVVirtualDisk(exportRootPath);
            if (string.IsNullOrWhiteSpace(sourceDiskPath))
            {
                throw new InvalidOperationException("The exported Hyper-V VM did not contain a source virtual disk.");
            }

            CopyAndMergeHyperVVirtualDisk(sourceDiskPath, clonePaths.VirtualDiskPath);
        }

        private static void PrepareCloneHyperVExportDirectory(string exportRootPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(exportRootPath);

            Directory.CreateDirectory(exportRootPath);

            foreach (string directory in Directory.EnumerateDirectories(exportRootPath))
            {
                Directory.Delete(directory, recursive: true);
            }

            foreach (string file in Directory.EnumerateFiles(exportRootPath))
            {
                File.Delete(file);
            }
        }

        private static void RenameCloneHyperVExportArtifacts(string exportRootPath, string sourceVmName, string targetVmName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(exportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceVmName);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetVmName);

            string normalizedSourceVmName = RestoreWindowNew.RegularHyperVRestoreHelper.NormalizeHyperVVmName(sourceVmName);
            if (string.IsNullOrWhiteSpace(normalizedSourceVmName) ||
                string.Equals(normalizedSourceVmName, targetVmName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (string filePath in Directory.EnumerateFiles(exportRootPath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                RenameCloneHyperVExportPath(filePath, normalizedSourceVmName, targetVmName, isDirectory: false);
            }

            foreach (string directoryPath in Directory.EnumerateDirectories(exportRootPath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                RenameCloneHyperVExportPath(directoryPath, normalizedSourceVmName, targetVmName, isDirectory: true);
            }
        }

        private static void RenameCloneHyperVExportPath(string path, string sourceVmName, string targetVmName, bool isDirectory)
        {
            string fileSystemEntryName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileSystemEntryName))
            {
                return;
            }

            string renamedEntryName = Regex.Replace(
                fileSystemEntryName,
                Regex.Escape(sourceVmName),
                targetVmName.Replace("$", "$$", StringComparison.Ordinal),
                RegexOptions.IgnoreCase);

            if (string.Equals(fileSystemEntryName, renamedEntryName, StringComparison.Ordinal))
            {
                return;
            }

            string parentDirectory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("Failed to resolve the parent directory for the exported Hyper-V artifact.");
            string targetPath = Path.Combine(parentDirectory, renamedEntryName);

            if (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                throw new InvalidOperationException($"The exported Hyper-V artifact '{targetPath}' already exists.");
            }

            if (isDirectory)
            {
                Directory.Move(path, targetPath);
            }
            else
            {
                File.Move(path, targetPath);
            }
        }

        private static void CopyAndMergeHyperVVirtualDisk(string sourceDiskPath, string targetDiskPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetDiskPath) ?? throw new InvalidOperationException("Invalid Clone Hyper-V target disk path."));

            string escapedSourcePath = EscapePowerShellSingleQuotedString(sourceDiskPath);
            string escapedTargetPath = EscapePowerShellSingleQuotedString(targetDiskPath);
            string script =
                "$ErrorActionPreference='Stop'; " +
                $"$sourcePath = '{escapedSourcePath}'; " +
                $"$targetPath = '{escapedTargetPath}'; " +
                "$currentPath = $sourcePath; " +
                "while ($currentPath.ToLowerInvariant().EndsWith('.avhdx')) { " +
                "  $mergedPath = [System.IO.Path]::ChangeExtension($currentPath, '.merged.vhdx'); " +
                "  Merge-VHD -Path $currentPath -DestinationPath $mergedPath -Force -ErrorAction Stop | Out-Null; " +
                "  $currentPath = $mergedPath; " +
                "} " +
                "Copy-Item -Path $currentPath -Destination $targetPath -Force -ErrorAction Stop";

            RunPowerShell(script);
        }

        private static void CreateCloneHyperVVirtualMachine(string vmName, string vmStoragePath, string virtualDiskPath)
        {
            string script = RestoreWindowNew.RegularHyperVRestoreHelper.BuildCreateVirtualMachineScript(vmName, vmStoragePath, virtualDiskPath, generation: 2, startAfterCreate: false);
            RunPowerShell(script);
        }

        private static void RegenerateHyperVVirtualMachineMacAddress(string vmName)
        {
            string script = RestoreWindowNew.RegularHyperVRestoreHelper.BuildRegenerateMacAddressScript(vmName);
            RunPowerShell(script);
        }

        private static void ScheduleSetupClPendingRequest(string virtualDiskPath, string vmName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(virtualDiskPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(vmName);

            string escapedVirtualDiskPath = EscapePowerShellSingleQuotedString(virtualDiskPath);
            string script =
                "$ErrorActionPreference='Stop'; " +
                $"$vhdPath = '{escapedVirtualDiskPath}'; " +
                "$vhd = Mount-VHD -Path $vhdPath -Passthru -ErrorAction Stop; " +
                "try { " +
                "  $disk = $vhd | Get-Disk -ErrorAction Stop; " +
                "  $windowsPartition = Get-Partition -DiskNumber $disk.Number -ErrorAction Stop | Sort-Object PartitionNumber | ForEach-Object { $partition = $_; $accessPath = ($partition.AccessPaths | Where-Object { $_ -match '^[A-Z]:\\$' } | Select-Object -First 1); if ([string]::IsNullOrWhiteSpace($accessPath)) { $volume = $partition | Get-Volume -ErrorAction SilentlyContinue; if ($volume -and -not [string]::IsNullOrWhiteSpace($volume.DriveLetter)) { $accessPath = $volume.DriveLetter + ':\\'; } } if (-not [string]::IsNullOrWhiteSpace($accessPath) -and (Test-Path (Join-Path $accessPath 'Windows\\System32\\Config\\SYSTEM'))) { [PSCustomObject]@{ AccessPath = $accessPath; PartitionNumber = $partition.PartitionNumber } } } | Select-Object -First 1; " +
                "  if ($null -eq $windowsPartition) { throw 'The cloned virtual disk does not contain a Windows SYSTEM hive.'; } " +
                "  [Console]::WriteLine((Join-Path $windowsPartition.AccessPath 'Windows\\System32\\Config\\SYSTEM')); " +
                "} finally { Dismount-VHD -Path $vhdPath -ErrorAction SilentlyContinue | Out-Null; }";

            string systemHivePath = RunPowerShell(script)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(systemHivePath) || !File.Exists(systemHivePath))
            {
                throw new InvalidOperationException("The offline SYSTEM hive was not found on the cloned virtual disk.");
            }

            int result = BackupEngineInterop.ScheduleOfflineSystemSetupCl(systemHivePath);
            if (result != 0)
            {
                var errorBuffer = new StringBuilder(4096);
                BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                throw new InvalidOperationException($"Failed to schedule SetupCl for the cloned Hyper-V system. {errorBuffer}".Trim());
            }
        }

        private static string ExportHyperVVmWithPowerShell(string vmName, string exportRootPath)
        {
            Directory.CreateDirectory(exportRootPath);

            string escapedVmName = EscapePowerShellSingleQuotedString(vmName);
            string escapedExportPath = EscapePowerShellSingleQuotedString(exportRootPath);
            string script =
                "$ProgressPreference='SilentlyContinue'; $VerbosePreference='SilentlyContinue'; $WarningPreference='Continue'; " +
                "Import-Module Hyper-V -ErrorAction Stop; " +
                $"$vmName = '{escapedVmName}'; " +
                $"$exportPath = '{escapedExportPath}'; " +
                "try { Export-VM -Name $vmName -Path $exportPath -CaptureLiveState CaptureDataConsistentState -ErrorAction Stop | Out-Null } " +
                "catch { Export-VM -Name $vmName -Path $exportPath -ErrorAction Stop | Out-Null }";

            RunPowerShell(script);
            return exportRootPath;
        }

        private static string FindPrimaryHyperVVirtualDisk(string exportRootPath)
        {
            if (!Directory.Exists(exportRootPath))
            {
                return string.Empty;
            }

            return Directory.EnumerateFiles(exportRootPath, "*.vhd*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.Length)
                .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(file => file.FullName)
                .FirstOrDefault() ?? string.Empty;
        }

        private static void CreateHyperVVirtualDiskFromArchive(string archivePath, string virtualDiskPath, bool restoreAsDisk, BackupEngineInterop.ProgressCallback progressCallback)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(virtualDiskPath) ?? throw new InvalidOperationException("Invalid Hyper-V virtual disk clone path."));

            PrepareHyperVVirtualDiskFile(virtualDiskPath, createFixedDisk: restoreAsDisk);

            var mountResult = BackupMountManager.MountVirtualDisk(virtualDiskPath, readOnly: false);
            if (!mountResult.Success || string.IsNullOrWhiteSpace(mountResult.DriveLetter))
            {
                throw new InvalidOperationException($"Failed to mount the Hyper-V virtual disk clone: {mountResult.Error}");
            }

            string mountedDriveRoot = mountResult.DriveLetter.EndsWith(":", StringComparison.Ordinal)
                ? mountResult.DriveLetter + "\\"
                : mountResult.DriveLetter;

            try
            {
                int result;
                if (restoreAsDisk)
                {
                    int targetDiskNumber = GetDiskNumberForDriveLetter(mountedDriveRoot);
                    result = BackupEngineInterop.RestoreDiskFromImage(archivePath, 1, targetDiskNumber, false, progressCallback);
                }
                else
                {
                    int targetDiskNumber = GetDiskNumberForDriveLetter(mountedDriveRoot);
                    string targetVolumePath = CreateVolumeOnDiskForHyperVRestore(targetDiskNumber);
                    result = BackupEngineInterop.RestoreVolumeFromImage(archivePath, 1, targetVolumePath, false, progressCallback);
                }

                if (result != 0)
                {
                    var errorBuffer = new StringBuilder(4096);
                    BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                    throw new InvalidOperationException($"Failed to create the Hyper-V virtual disk clone: {errorBuffer}");
                }
            }
            finally
            {
                BackupMountManager.UnmountVirtualDisk(virtualDiskPath);
            }
        }

        private static void PrepareHyperVVirtualDiskFile(string virtualDiskPath, bool createFixedDisk)
        {
            string diskType = createFixedDisk ? "Fixed" : "Dynamic";
            long sizeBytes = createFixedDisk ? 137438953472L : 68719476736L;
            string script = $"$path='{virtualDiskPath.Replace("'", "''")}'; if (Test-Path $path) {{ Dismount-DiskImage -ImagePath $path -ErrorAction SilentlyContinue; Remove-Item -Path $path -Force -ErrorAction SilentlyContinue; }}; New-VHD -Path $path -SizeBytes {sizeBytes} -{diskType} | Out-Null";

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

        private static int GetDiskNumberForDriveLetter(string driveLetter)
        {
            string normalizedRoot = driveLetter.Trim().TrimEnd('\\') + "\\";
            string driveName = normalizedRoot[..2];
            string escapedDriveName = driveName.Replace("'", "''", StringComparison.Ordinal);

            string script = $"$partition = Get-Partition -DriveLetter '{escapedDriveName[0]}' -ErrorAction Stop; $disk = $partition | Get-Disk -ErrorAction Stop; $disk.Number";
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
                throw new InvalidOperationException($"Failed to resolve the mounted virtual disk number. {errors}".Trim());
            }

            string diskText = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            if (!int.TryParse(diskText.Trim(), out int diskNumber))
            {
                throw new InvalidOperationException("The mounted virtual disk number could not be determined.");
            }

            return diskNumber;
        }

        private static string CreateVolumeOnDiskForHyperVRestore(int diskNumber)
        {
            string script = $"$diskNumber={diskNumber}; Clear-Disk -Number $diskNumber -RemoveData -RemoveOEM -Confirm:$false -ErrorAction Stop; Initialize-Disk -Number $diskNumber -PartitionStyle GPT -ErrorAction Stop; $partition = New-Partition -DiskNumber $diskNumber -UseMaximumSize -AssignDriveLetter -ErrorAction Stop; Format-Volume -Partition $partition -FileSystem NTFS -NewFileSystemLabel 'SSBClone' -Confirm:$false -Force -ErrorAction Stop | Out-Null; ($partition | Get-Volume).UniqueId";

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
                throw new InvalidOperationException($"Failed to prepare the virtual disk volume. {errors}".Trim());
            }

            string volumeId = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(volumeId))
            {
                throw new InvalidOperationException("The prepared virtual disk volume path could not be determined.");
            }

            return volumeId.Trim();
        }

        private static bool HasBackupArchive(string destPath, string jobName)
        {
            try
            {
                return File.Exists(GetBackupArchivePath(destPath, jobName));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking backup archive: {ex.Message}");
            }

            return false;
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
                    PersistSelectedFileList(existingJob.Name, job);
                    _hasSavedEncryptionPassword = job.EncryptBackup && !string.IsNullOrWhiteSpace(job.ProtectedEncryptionPassword);
                    UpdateEncryptionUiState();
                    CustomDialogService.ShowSuccess($"Backup job '{job.Name}' updated successfully!\n\nJob saved to:\nC:\\ProgramData\\SecureServerBackupService\\jobs.json", 
                        "Success");
                }
                else
                {
                    jobManager.AddJob(job);
                    PersistSelectedFileList(null, job);
                    _hasSavedEncryptionPassword = job.EncryptBackup && !string.IsNullOrWhiteSpace(job.ProtectedEncryptionPassword);
                    UpdateEncryptionUiState();
                    CustomDialogService.ShowSuccess($"Backup job '{job.Name}' created successfully!\n\nJob saved to:\nC:\\ProgramData\\SecureServerBackupService\\jobs.json", 
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

        private void PersistSelectedFileList(string? previousJobName, BackupJob job)
        {
            ArgumentNullException.ThrowIfNull(job);

            bool isFileSelectionJob = job.Target == BackupTarget.FilesAndFolders || job.Type == BackupType.SelectedFilesAndFolders;
            if (!isFileSelectionJob)
            {
                if (!string.IsNullOrWhiteSpace(previousJobName))
                {
                    SelectedFileListStore.Delete(previousJobName);
                }

                SelectedFileListStore.Delete(job.Name);
                return;
            }

            if (!string.IsNullOrWhiteSpace(previousJobName) &&
                !string.Equals(previousJobName, job.Name, StringComparison.OrdinalIgnoreCase))
            {
                SelectedFileListStore.Delete(previousJobName);
            }

            SelectedFileListStore.Save(job.Name, job.SourcePaths);
        }

        private static IReadOnlyList<string> LoadSelectedFileList(BackupJob job)
        {
            ArgumentNullException.ThrowIfNull(job);

            if (job.Type != BackupType.SelectedFilesAndFolders || job.Target != BackupTarget.FilesAndFolders)
            {
                return job.SourcePaths;
            }

            List<string> persistedPaths = SelectedFileListStore.Load(job.Name);
            return persistedPaths.Count > 0 ? persistedPaths : job.SourcePaths;
        }

        private static IReadOnlyList<string> GetSelectedFilesReplayPaths(BackupJob job)
        {
            ArgumentNullException.ThrowIfNull(job);

            if (job.Type != BackupType.SelectedFilesAndFolders || job.Target != BackupTarget.FilesAndFolders)
            {
                return job.SourcePaths;
            }

            List<string> replayPaths = new();
            replayPaths.AddRange(job.SelectedFilesSourceRoots.Where(path => !string.IsNullOrWhiteSpace(path)));
            replayPaths.AddRange(LoadSelectedFileList(job));

            return replayPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static IReadOnlyList<string> GetReplayPathsForJob(BackupJob job)
        {
            ArgumentNullException.ThrowIfNull(job);

            if (job.Type == BackupType.CloneHyperVSystem)
            {
                IReadOnlyList<string> cloneHyperVPaths = job.HyperVMachines.Count > 0
                    ? job.HyperVMachines
                        .Select(HyperVBackupTreeHelper.NormalizeSavedHyperVSystemName)
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .ToList()
                    : job.SourcePaths;

                return cloneHyperVPaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (job.Target == BackupTarget.Disk || job.Target == BackupTarget.Volume || job.Target == BackupTarget.FilesAndFolders)
            {
                return job.Type == BackupType.SelectedFilesAndFolders
                    ? GetSelectedFilesReplayPaths(job)
                    : job.SourcePaths
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
            }

            if (job.IsHyperVBackup)
            {
                return job.HyperVMachines
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return Array.Empty<string>();
        }

        private BackupJob CreateJobFromInput()
        {
            var backupType = GetSelectedBackupType();
            int selectedFilesRetentionCount = GetSelectedFilesRetentionCount();
            int cloneRetentionCount = GetCloneRetentionCount();
            bool renameHyperVSystem = backupType == BackupType.CloneHyperVSystem && chkRenameHyperVSystem?.IsChecked == true;
            string renameHyperVSystemName = renameHyperVSystem
                ? txtRenameHyperVSystemName?.Text?.Trim() ?? string.Empty
                : string.Empty;

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
                RetainFullBackupCount = int.TryParse(txtRetainCount.Text, out int retainCount) ? Math.Max(1, retainCount) : 1,
                SelectedFilesRetentionCount = selectedFilesRetentionCount,
                CloneRetentionCount = cloneRetentionCount,
                RenameHyperVSystem = renameHyperVSystem,
                RenameHyperVSystemName = renameHyperVSystemName
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

            if (backupType == BackupType.SelectedFilesAndFolders)
            {
                job.SelectedFilesSourceRoots = GetSelectedFilesSourceRoots();
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

        private int GetSelectedFilesRetentionCount()
        {
            string retentionText = cmbSelectedFilesRetentionCount.Text?.Trim() ?? string.Empty;
            return int.TryParse(retentionText, out int retentionCount)
                ? Math.Clamp(retentionCount, 1, 30)
                : 7;
        }

        private int GetCloneRetentionCount()
        {
            string retentionText = cmbCloneRetentionCount.Text?.Trim() ?? string.Empty;
            return int.TryParse(retentionText, out int retentionCount)
                ? Math.Clamp(retentionCount, 1, 30)
                : 7;
        }

        private void CollectSelectedHyperVMachines(BackupJob job)
        {
            job.IsHyperVBackup = true;
            job.Target = BackupTarget.HyperV;

            foreach (var drive in driveItems)
            {
                CollectHyperVMachinesRecursive(drive, job);
            }
        }

        private void CollectHyperVMachinesRecursive(DriveTreeItem item, BackupJob job)
        {
            if (item.ItemType == DriveTreeItemType.HyperVSystem && item.IsChecked == true)
            {
                string vmName = HyperVBackupTreeHelper.NormalizeSavedHyperVSystemName(
                    string.IsNullOrWhiteSpace(item.VirtualMachineName) ? item.FullPath : item.VirtualMachineName);
                if (!string.IsNullOrWhiteSpace(vmName))
                {
                    job.HyperVMachines.Add(vmName);
                }
                return; // whole VM selected; don't recurse into its children
            }

            if (item.IsChecked == null || item.IsChecked == true)
            {
                foreach (var child in item.Children)
                {
                    CollectHyperVMachinesRecursive(child, job);
                }
            }
        }

        internal static bool IsSelectedFilesAndFoldersAllowedItem(DriveTreeItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return item.ItemType switch
            {
                DriveTreeItemType.Folder => !IsDescendantOfHyperVItem(item) || HyperVGuestSelectionPath.IsEncodedPath(item.FullPath),
                DriveTreeItemType.File => !IsDescendantOfHyperVItem(item) || HyperVGuestSelectionPath.IsEncodedPath(item.FullPath),
                DriveTreeItemType.NetworkDrive => true,
                DriveTreeItemType.NetworkShare => true,
                _ => false
            };
        }

        internal static bool IsSelectedFilesAndFoldersSelectionAllowed(IEnumerable<DriveTreeItem> selectedItems)
        {
            ArgumentNullException.ThrowIfNull(selectedItems);

            bool hasAllowedSelection = false;
            foreach (DriveTreeItem item in selectedItems)
            {
                if (!IsSelectedFilesAndFoldersAllowedItem(item))
                {
                    return false;
                }

                hasAllowedSelection = true;
            }

            return hasAllowedSelection;
        }

        private List<DriveTreeItem> GetEffectiveSelectedFilesAndFoldersItems()
        {
            List<DriveTreeItem> selectedItems = new();

            foreach (DriveTreeItem drive in driveItems)
            {
                CollectEffectiveSelectedFilesAndFoldersItems(drive, selectedItems);
            }

            return selectedItems;
        }

        private List<string> GetSelectedFilesSourceRoots()
        {
            List<string> sourceRoots = new();

            foreach (DriveTreeItem selectedItem in GetEffectiveSelectedFilesAndFoldersItems())
            {
                AddSelectedFilesSourceRoots(selectedItem, sourceRoots);
            }

            return sourceRoots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddSelectedFilesSourceRoots(DriveTreeItem selectedItem, List<string> sourceRoots)
        {
            ArgumentNullException.ThrowIfNull(selectedItem);
            ArgumentNullException.ThrowIfNull(sourceRoots);

            for (DriveTreeItem? current = selectedItem; current != null; current = current.Parent)
            {
                switch (current.ItemType)
                {
                    case DriveTreeItemType.Disk:
                    case DriveTreeItemType.Volume:
                    case DriveTreeItemType.NetworkDrive:
                    case DriveTreeItemType.NetworkShare:
                    case DriveTreeItemType.HyperVVirtualDisk:
                    case DriveTreeItemType.HyperVVolume:
                        if (!string.IsNullOrWhiteSpace(current.FullPath))
                        {
                            sourceRoots.Add(current.FullPath);
                        }

                        break;
                }
            }
        }

        private static void CollectEffectiveSelectedFilesAndFoldersItems(DriveTreeItem item, List<DriveTreeItem> selectedItems)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(selectedItems);

            if (item.IsChecked == true)
            {
                if (IsSelectedFilesAndFoldersAllowedItem(item))
                {
                    selectedItems.Add(item);
                    return;
                }

                foreach (DriveTreeItem child in item.Children)
                {
                    CollectEffectiveSelectedFilesAndFoldersItems(child, selectedItems);
                }

                return;
            }

            if (item.IsChecked == null)
            {
                foreach (DriveTreeItem child in item.Children)
                {
                    CollectEffectiveSelectedFilesAndFoldersItems(child, selectedItems);
                }
            }
        }

        private BackupType GetSelectedBackupType()
        {
            if (rbFullBackup.IsChecked == true) return BackupType.Full;
            if (rbIncremental.IsChecked == true) return BackupType.Incremental;
            if (rbDifferential.IsChecked == true) return BackupType.Differential;
            if (rbSelectedFilesAndFolders.IsChecked == true) return BackupType.SelectedFilesAndFolders;
            if (rbCloneDisk.IsChecked == true) return BackupType.CloneToDisk;
            if (rbCloneVirtual.IsChecked == true) return BackupType.CloneToVirtualDisk;
            if (rbCloneHyperV.IsChecked == true) return BackupType.CloneHyperVSystem;
            
            return BackupType.Full; // Default
        }

        private void CollectSelectedItems(BackupJob job)
        {
            bool selectedFilesAndFoldersOnly = job.Type == BackupType.SelectedFilesAndFolders && job.Target == BackupTarget.FilesAndFolders;

            if (selectedFilesAndFoldersOnly)
            {
                List<DriveTreeItem> selectedFilesItems = GetEffectiveSelectedFilesAndFoldersItems();
                foreach (DriveTreeItem selectedItem in selectedFilesItems)
                {
                    if (string.IsNullOrWhiteSpace(selectedItem.FullPath))
                    {
                        continue;
                    }

                    job.Target = BackupTarget.FilesAndFolders;
                    job.SourcePaths.Add(selectedItem.FullPath);
                }

                job.SourcePaths = job.SourcePaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return;
            }

            foreach (var drive in driveItems)
            {
                if (drive.IsChecked == true)
                {
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
                    else if (drive.ItemType == DriveTreeItemType.HyperVVirtualDisk ||
                             drive.ItemType == DriveTreeItemType.HyperVVolume)
                    {
                        // Guest-disk or guest-volume path backup — treated as files/folders
                        job.Target = BackupTarget.FilesAndFolders;
                        job.SourcePaths.Add(drive.FullPath);
                    }
                    else if (drive.ItemType == DriveTreeItemType.NetworkDrive ||
                             drive.ItemType == DriveTreeItemType.NetworkShare)
                    {
                        job.Target = BackupTarget.FilesAndFolders;
                        job.SourcePaths.Add(drive.FullPath);
                    }
                    // NetworkRoot and NetworkBrowser are display-only sentinels; skip them
                }
                else if (drive.IsChecked == null && drive.Children.Count > 0)
                {
                    // Partial selection — recurse into children
                    CollectSelectedChildren(drive, job);
                }
            }

            // Determine target type if not already set
            if (job.Target == 0 && job.SourcePaths.Count > 0)
            {
                if (job.Type == BackupType.SelectedFilesAndFolders)
                {
                    job.Target = BackupTarget.FilesAndFolders;
                }
                else
                {
                    // Check if all sources are drive letters (volumes), device paths (disks), or regular paths (files/folders)
                    var firstPath = job.SourcePaths[0];

                    // Check for PHYSICALDRIVE device paths (e.g., \\.\PHYSICALDRIVE5)
                    if (firstPath.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase) &&
                        firstPath.Contains("PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
                    {
                        job.Target = BackupTarget.Disk;
                    }
                    // Check for Volume GUID paths (e.g., \\?\\Volume{guid})
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
            }

            if (job.HyperVMachines.Count > 0)
            {
                job.IsHyperVBackup = true;
                job.Target = BackupTarget.HyperV;
            }
        }

        private void CollectSelectedChildren(DriveTreeItem parent, BackupJob job)
        {
            bool selectedFilesAndFoldersOnly = job.Type == BackupType.SelectedFilesAndFolders;

            foreach (var child in parent.Children)
            {
                if (child.IsChecked == true)
                {
                    if (selectedFilesAndFoldersOnly)
                    {
                        continue;
                    }
                    else if (child.ItemType == DriveTreeItemType.Volume)
                    {
                        if (job.Target == 0) job.Target = BackupTarget.Volume;
                        job.SourcePaths.Add(child.FullPath);
                    }
                    else if (child.ItemType == DriveTreeItemType.Disk)
                    {
                        job.Target = BackupTarget.Disk;
                        job.SourcePaths.Add(child.FullPath);
                    }
                    else if (child.ItemType == DriveTreeItemType.HyperVSystem)
                    {
                        string vmName = RestoreWindowNew.RegularHyperVRestoreHelper.NormalizeHyperVVmName(child.FullPath);
                        if (!string.IsNullOrWhiteSpace(vmName))
                        {
                            job.HyperVMachines.Add(vmName);
                        }
                    }
                    else if (child.ItemType == DriveTreeItemType.HyperVVirtualDisk ||
                             child.ItemType == DriveTreeItemType.HyperVVolume)
                    {
                        job.Target = BackupTarget.FilesAndFolders;
                        job.SourcePaths.Add(child.FullPath);
                    }
                    else if (child.ItemType == DriveTreeItemType.Folder ||
                             child.ItemType == DriveTreeItemType.File)
                    {
                        // Skip placeholder nodes that are children of Hyper-V items
                        if (IsDescendantOfHyperVItem(child) && !HyperVGuestSelectionPath.IsEncodedPath(child.FullPath))
                            continue;
                        job.Target = BackupTarget.FilesAndFolders;
                        job.SourcePaths.Add(child.FullPath);
                    }
                    else if (child.ItemType == DriveTreeItemType.NetworkDrive ||
                             child.ItemType == DriveTreeItemType.NetworkShare)
                    {
                        job.Target = BackupTarget.FilesAndFolders;
                        job.SourcePaths.Add(child.FullPath);
                    }
                    // NetworkRoot and NetworkBrowser are display-only sentinels; skip them
                }
                else if (child.IsChecked == null && child.Children.Count > 0)
                {
                    CollectSelectedChildren(child, job);
                }
            }
        }

        private static bool IsHyperVItem(DriveTreeItem item) =>
            item.ItemType == DriveTreeItemType.HyperVSystem ||
            item.ItemType == DriveTreeItemType.HyperVVirtualDisk ||
            item.ItemType == DriveTreeItemType.HyperVVolume;

        private static bool IsDescendantOfHyperVItem(DriveTreeItem item)
        {
            DriveTreeItem? parent = item.Parent;
            while (parent != null)
            {
                if (IsHyperVItem(parent))
                    return true;
                parent = parent.Parent;
            }
            return false;
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
            var selectedHyperV = selectedItems.Where(IsHyperVItem).ToList();
            var selectedNonHyperV = selectedItems.Where(item =>
                !IsHyperVItem(item) &&
                !IsDescendantOfHyperVItem(item) &&
                item.ItemType != DriveTreeItemType.NetworkRoot &&
                item.ItemType != DriveTreeItemType.NetworkBrowser).ToList();
            List<DriveTreeItem> selectedFilesItems = rbSelectedFilesAndFolders?.IsChecked == true
                ? GetEffectiveSelectedFilesAndFoldersItems()
                : new List<DriveTreeItem>();

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

                if (chkRenameHyperVSystem?.IsChecked == true)
                {
                    string renameName = txtRenameHyperVSystemName?.Text?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(renameName))
                    {
                        CustomDialogService.ShowWarning("A new name is required when Rename Hyper-V System is selected.", "Validation Error");
                        return false;
                    }

                    if (!IsValidWindowsComputerName(renameName))
                    {
                        CustomDialogService.ShowWarning("The new Hyper-V system name must be a valid Windows computer name (1-15 letters, numbers, or hyphens, and cannot start or end with a hyphen).", "Validation Error");
                        return false;
                    }
                }
            }
            else
            {
                if (rbSelectedFilesAndFolders?.IsChecked == true)
                {
                    if (!IsSelectedFilesAndFoldersSelectionAllowed(selectedFilesItems))
                    {
                        CustomDialogService.ShowWarning("Selected Files & Folder backups can only include checked files, folders, or network share roots from the tree.", "Validation Error");
                        return false;
                    }

                    string? selectionValidationMessage = GetSelectionValidationMessage(
                        selectedFilesBackup: true,
                        selectedFilesCount: selectedFilesItems.Count,
                        selectedHyperVCount: selectedHyperV.Count,
                        selectedNonHyperVCount: selectedNonHyperV.Count);
                    if (!string.IsNullOrWhiteSpace(selectionValidationMessage))
                    {
                        CustomDialogService.ShowWarning(selectionValidationMessage, "Validation Error");
                        return false;
                    }
                }

                if (selectedHyperV.Count > 0 && selectedNonHyperV.Count > 0)
                {
                    var backupTypeName = GetBackupTypeName();
                    CustomDialogService.ShowWarning($"{backupTypeName} cannot combine Hyper-V systems with disks, volumes, or folders.\n\nPlease select only Hyper-V systems or only disks, volumes, and folders.", "Validation Error");
                    return false;
                }

                if (rbSelectedFilesAndFolders?.IsChecked != true)
                {
                    string? selectionValidationMessage = GetSelectionValidationMessage(
                        selectedFilesBackup: false,
                        selectedFilesCount: selectedFilesItems.Count,
                        selectedHyperVCount: selectedHyperV.Count,
                        selectedNonHyperVCount: selectedNonHyperV.Count);
                    if (!string.IsNullOrWhiteSpace(selectionValidationMessage))
                    {
                        CustomDialogService.ShowWarning(selectionValidationMessage, "Validation Error");
                        return false;
                    }
                }
            }

            if (chkEnableSchedule.IsChecked == true && !TryGetScheduledTime(out _, out _))
            {
                CustomDialogService.ShowWarning("Please enter a valid scheduled time. Minutes must be between 00 and 59.", "Validation Error");
                return false;
            }

            if (rbSelectedFilesAndFolders?.IsChecked == true)
            {
                string retentionText = cmbSelectedFilesRetentionCount.Text?.Trim() ?? string.Empty;
                if (!int.TryParse(retentionText, out int retentionCount) || retentionCount < 1 || retentionCount > 30)
                {
                    CustomDialogService.ShowWarning("Please enter a Selected Files retention value between 1 and 30 versions.", "Validation Error");
                    return false;
                }
            }

            if (rbCloneVirtual?.IsChecked == true || rbCloneHyperV?.IsChecked == true)
            {
                string retentionText = cmbCloneRetentionCount.Text?.Trim() ?? string.Empty;
                if (!int.TryParse(retentionText, out int retentionCount) || retentionCount < 1 || retentionCount > 30)
                {
                    CustomDialogService.ShowWarning("Please enter a Clone retention value between 1 and 30 clones.", "Validation Error");
                    return false;
                }
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
            if (rbSelectedFilesAndFolders.IsChecked == true) return "Selected Files & Folder";
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
