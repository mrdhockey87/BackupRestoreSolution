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
        private const string RestoreLogJobName = "[Restore]";
        private ObservableCollection<RestorePoint> restorePoints = new();
        private List<string> backupFiles = new();
        private readonly AvailableBackupInfo? _preloadedBackup;
        private readonly RestoreSelectionContext? _preselectedRestore;
        private readonly bool _requireAlternateDestination;
        private readonly List<string> _bootProtectedTargets = new();
        private RestoreTargetKind _restoreTargetKind = RestoreTargetKind.FileOrFolder;
        private string? _selectedTargetPath;
        private int? _selectedTargetDiskNumber;
        private NativeBackupMountManager.RestoreDiskPlan? _diskRestorePlan;
        private bool _isHyperVBackupPoint;
        private bool _suppressRestorePointSelectionChanged;
        private RestorePoint? _activeRestorePoint;

        // Restore target drive tree items
        private readonly ObservableCollection<DriveTreeItem> _restoreTargetItems = new();
        private bool _showHiddenPartitionsTarget;
        private bool _isLoadingTargets;
        private bool _reloadRestoreTargetsAfterLoad;
        private RestoreTargetKind _lastBuiltTargetKind = RestoreTargetKind.FileOrFolder;

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

            public static string BuildDefaultHyperVVirtualDiskPath(string? directoryPath, string? backupName)
            {
                string sanitizedBackupName = SanitizeFileName(backupName);
                string fileName = string.IsNullOrWhiteSpace(sanitizedBackupName)
                    ? "RestoredBackup.vhdx"
                    : sanitizedBackupName + ".vhdx";

                return string.IsNullOrWhiteSpace(directoryPath)
                    ? fileName
                    : Path.Combine(directoryPath.Trim(), fileName);
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

            private static string SanitizeFileName(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return string.Empty;
                }

                char[] invalidChars = Path.GetInvalidFileNameChars();
                var builder = new StringBuilder(value.Length);

                foreach (char character in value.Trim())
                {
                    builder.Append(invalidChars.Contains(character) ? '_' : character);
                }

                return builder.ToString().Trim().TrimEnd('.');
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

        private static DateTime GetRestorePointTimestamp(string backupPath, IReadOnlyList<RestorePointArchiveImage> archiveImages)
        {
            if (TryGetBackupStartTime(backupPath, archiveImages, out DateTime backupStartTime))
            {
                return backupStartTime;
            }

            return GetEntryTimestamp(backupPath);
        }

        private static bool TryGetBackupStartTime(string backupPath, IReadOnlyList<RestorePointArchiveImage> archiveImages, out DateTime backupStartTime)
        {
            if (TryGetArchiveBackupStartTime(archiveImages, out backupStartTime))
            {
                return true;
            }

            return TryGetFileBackupStartTime(backupPath, out backupStartTime);
        }

        private static bool TryGetArchiveBackupStartTime(IReadOnlyList<RestorePointArchiveImage> archiveImages, out DateTime backupStartTime)
        {
            backupStartTime = default;

            if (archiveImages == null)
            {
                return false;
            }

            DateTime? earliestStartTime = archiveImages
                .Select(image => image.BackupStartTime)
                .Where(timestamp => timestamp.HasValue)
                .OrderBy(timestamp => timestamp)
                .FirstOrDefault();

            if (!earliestStartTime.HasValue)
            {
                return false;
            }

            backupStartTime = earliestStartTime.Value;
            return true;
        }

        private static IReadOnlyList<string> ParseListedBackupItems(string listedContents)
        {
            if (string.IsNullOrWhiteSpace(listedContents))
            {
                return Array.Empty<string>();
            }

            return listedContents
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item) && !string.Equals(item, "(No files in backup)", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static bool TryGetFileBackupStartTime(string backupPath, out DateTime backupStartTime)
        {
            backupStartTime = default;

            string metadataPath = Path.Combine(backupPath, "backup_metadata.dat");
            if (!Directory.Exists(backupPath) || !File.Exists(metadataPath))
            {
                return false;
            }

            foreach (string line in File.ReadLines(metadataPath))
            {
                if (!line.StartsWith("#BACKUP_START_TIME|", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string timestampText = line["#BACKUP_START_TIME|".Length..].Trim();
                if (DateTime.TryParse(timestampText, out backupStartTime))
                {
                    return true;
                }

                return false;
            }

            return false;
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

        public RestoreWindowNew(RestoreSelectionContext restoreSelection)
            : this(restoreSelection?.Backup ?? throw new ArgumentNullException(nameof(restoreSelection)), restoreSelection.RequireAlternateDestination)
        {
            _preselectedRestore = restoreSelection;
        }

        private async void RestoreWindowNew_Loaded(object sender, RoutedEventArgs e)
        {
            // Always pre-load the restore target tree so it is ready when the user selects a backup
            _ = LoadRestoreTargetDrivesAsync();

            if (_preloadedBackup == null)
            {
                return;
            }

            txtBackupSource.Text = _preloadedBackup.BackupPath;

            UpdateSelectedRestoreTargetKind();

            await ScanBackupAsync();

            if (_preselectedRestore != null)
            {
                ApplyPreselectedRestoreContext();
            }
        }

        internal static IReadOnlyList<RestorePoint> GetRestorePointsForBackup(string backupPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

            IReadOnlyList<string> backupItems = GetBackupItemsForArchive(backupPath);
            string backupType = DetermineBackupTypeForArchive(backupPath, backupItems);
            IReadOnlyList<RestorePointArchiveImage> archiveImages = GetRestorePointArchiveImages(backupPath);
            bool forceSingleRestorePoint = ShouldTreatBackupAsSingleFileRestorePoint(backupPath, backupItems);
            DateTime timestamp = GetRestorePointTimestamp(backupPath, archiveImages);

            return CreateRestorePointsForBackupFile(
                backupPath,
                backupType,
                timestamp,
                startingPointNumber: 1,
                archiveImages,
                forceSingleRestorePoint);
        }

        private static string DetermineBackupTypeForPath(string backupPath)
        {
            string fileName = Path.GetFileName(backupPath);
            if (fileName.Contains("_SelectedFiles_", StringComparison.OrdinalIgnoreCase))
            {
                return "Selected Files";
            }

            if (fileName.Contains("incremental", StringComparison.OrdinalIgnoreCase))
            {
                return "Incremental";
            }

            if (fileName.Contains("differential", StringComparison.OrdinalIgnoreCase))
            {
                return "Differential";
            }

            if (fileName.Contains("full", StringComparison.OrdinalIgnoreCase))
            {
                return "Full";
            }

            return "Unknown";
        }

        internal static string DetermineBackupTypeForArchive(string backupPath, IReadOnlyList<string> backupItems)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
            ArgumentNullException.ThrowIfNull(backupItems);

            return ShouldTreatBackupAsSingleFileRestorePoint(backupPath, backupItems)
                ? "Selected Files"
                : DetermineBackupTypeForPath(backupPath);
        }

        private void ApplyPreselectedRestoreContext()
        {
            if (_preselectedRestore == null)
            {
                return;
            }

            RestorePoint? matchingRestorePoint = restorePoints.FirstOrDefault(point =>
                string.Equals(point.FilePath, _preselectedRestore.RestorePoint.FilePath, StringComparison.OrdinalIgnoreCase) &&
                point.ImageIndex == _preselectedRestore.RestorePoint.ImageIndex &&
                point.Timestamp == _preselectedRestore.RestorePoint.Timestamp);

            if (matchingRestorePoint == null)
            {
                matchingRestorePoint = _preselectedRestore.RestorePoint;
                restorePoints.Clear();
                restorePoints.Add(matchingRestorePoint);
                lstRestorePoints.ItemsSource = restorePoints;
            }

            _activeRestorePoint = matchingRestorePoint;
            _suppressRestorePointSelectionChanged = true;
            lstRestorePoints.SelectedItem = matchingRestorePoint;
            _suppressRestorePointSelectionChanged = false;

            if (_preselectedRestore.ScopeKind == RestoreScopeKind.All)
            {
                rbRestoreAll.IsChecked = true;
                lstBackupItems.SelectAll();
            }
            else
            {
                rbRestoreSelected.IsChecked = true;
                lstBackupItems.SelectedItems.Clear();
                foreach (object item in lstBackupItems.Items)
                {
                    if (item is string backupItem && _preselectedRestore.SelectedItems.Contains(backupItem, StringComparer.OrdinalIgnoreCase))
                    {
                        lstBackupItems.SelectedItems.Add(item);
                    }
                }

                _selectedRestoreDiskGroup = _preselectedRestore.SelectedVolumes.Count > 0
                    ? _preselectedRestore.SelectedVolumes
                    : null;
                _selectedRestoreVolume = _preselectedRestore.SelectedVolumes.Count == 1
                    ? _preselectedRestore.SelectedVolumes[0]
                    : null;
            }

            ApplyPreselectedRestoreUi();

            UpdateSelectedRestoreTargetKind();
            UpdateRestoreActionState();
        }

        private void ApplyPreselectedRestoreUi()
        {
            if (_preselectedRestore == null || _activeRestorePoint == null)
            {
                return;
            }

            if (btnBrowseBackup != null)
            {
                btnBrowseBackup.Visibility = Visibility.Collapsed;
            }

            if (btnScanBackup != null)
            {
                btnScanBackup.Visibility = Visibility.Collapsed;
            }

            if (txtRestorePointPrompt != null)
            {
                txtRestorePointPrompt.Visibility = Visibility.Collapsed;
            }

            if (lstRestorePoints != null)
            {
                lstRestorePoints.Visibility = Visibility.Collapsed;
            }

            if (txtPreselectedRestorePointSummary != null)
            {
                txtPreselectedRestorePointSummary.Text = $"Restore point: {_activeRestorePoint.DisplayName} — {_activeRestorePoint.Description}";
                txtPreselectedRestorePointSummary.Visibility = Visibility.Visible;
            }

            if (txtWhatToRestoreLabel != null)
            {
                txtWhatToRestoreLabel.Visibility = Visibility.Collapsed;
            }

            if (rbRestoreAll != null)
            {
                rbRestoreAll.Visibility = Visibility.Collapsed;
            }

            if (rbRestoreSelected != null)
            {
                rbRestoreSelected.Visibility = Visibility.Collapsed;
            }

            if (pnlItemSelection != null)
            {
                pnlItemSelection.Visibility = Visibility.Collapsed;
            }

            if (txtPreselectedScopeSummary != null)
            {
                txtPreselectedScopeSummary.Text = _preselectedRestore.ScopeKind switch
                {
                    RestoreScopeKind.All => "Restore scope: all files or volumes from the selected restore point.",
                    RestoreScopeKind.SelectedVolumes => BuildSelectedVolumeSummary(),
                    RestoreScopeKind.SelectedItems => BuildSelectedItemSummary(),
                    _ => string.Empty
                };
                txtPreselectedScopeSummary.Visibility = Visibility.Visible;
            }
        }

        private string BuildSelectedItemSummary()
        {
            if (_preselectedRestore == null || _preselectedRestore.SelectedItems.Count == 0)
            {
                return "Restore scope: selected files or folders.";
            }

            int itemCount = _preselectedRestore.SelectedItems.Count;
            string preview = _preselectedRestore.SelectedItems[0];
            return itemCount == 1
                ? $"Restore scope: selected item — {preview}"
                : $"Restore scope: {itemCount} selected files or folders.";
        }

        private string BuildSelectedVolumeSummary()
        {
            if (_preselectedRestore == null || _preselectedRestore.SelectedVolumes.Count == 0)
            {
                return "Restore scope: selected volumes.";
            }

            int volumeCount = _preselectedRestore.SelectedVolumes.Count;
            string firstLabel = _preselectedRestore.SelectedVolumes[0].Label;
            return volumeCount == 1
                ? $"Restore scope: selected volume — {firstLabel}"
                : $"Restore scope: {volumeCount} selected volumes.";
        }

        internal static IReadOnlyList<string> GetBackupItemsForRestorePoint(RestorePoint restorePoint)
        {
            ArgumentNullException.ThrowIfNull(restorePoint);

            try
            {
                using var preparedBackup = EncryptedBackupFileService.PrepareForRead(null, restorePoint.FilePath, Path.GetFileNameWithoutExtension(restorePoint.FilePath));
                var buffer = new StringBuilder(32768);
                int result = BackupEngineInterop.ListBackupContents(preparedBackup.WorkingPath, buffer, buffer.Capacity);
                if (result != 0)
                {
                    return Array.Empty<string>();
                }

                return ParseListedBackupItems(buffer.ToString());
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        internal static IReadOnlyList<VolumeInfo> GetBackupVolumesForRestorePoint(RestorePoint restorePoint)
        {
            ArgumentNullException.ThrowIfNull(restorePoint);

            try
            {
                var imagesWithMetadata = NativeBackupMountManager.GetImageInfoWithRestoreMetadata(restorePoint.FilePath);
                if (!imagesWithMetadata.Success || imagesWithMetadata.Images.Count == 0)
                {
                    return Array.Empty<VolumeInfo>();
                }

                return imagesWithMetadata.Images
                    .Where(image => image.ImageIndex > 0)
                    .Select((image, idx) => new VolumeInfo
                    {
                        ImageIndex = image.ImageIndex,
                        Label = !string.IsNullOrWhiteSpace(image.RestoreMetadata?.SourceVolumeLabel)
                            ? image.RestoreMetadata.SourceVolumeLabel
                            : (!string.IsNullOrWhiteSpace(image.Name) ? image.Name : $"Volume {idx + 1}"),
                        Size = image.RestoreMetadata?.PartitionLengthBytes > 0
                            ? (long)image.RestoreMetadata.PartitionLengthBytes
                            : 0,
                        UsedSpace = image.RestoreMetadata?.SourceUsedSpaceBytes > 0
                            ? (long)image.RestoreMetadata.SourceUsedSpaceBytes
                            : 0,
                        PartitionNumber = image.RestoreMetadata?.PartitionNumber ?? 0,
                        PartitionOffsetBytes = image.RestoreMetadata?.PartitionOffsetBytes ?? 0,
                        PartitionLengthBytes = image.RestoreMetadata?.PartitionLengthBytes ?? 0,
                        PartitionStyle = image.RestoreMetadata?.PartitionStyle ?? string.Empty,
                        PartitionType = image.RestoreMetadata?.PartitionType ?? string.Empty,
                        SourceVolumeGuidPath = image.RestoreMetadata?.SourceVolumeGuidPath ?? string.Empty,
                        SourceVolumeMountPath = image.RestoreMetadata?.SourceVolumeMountPath ?? string.Empty,
                        IsBootVolume = image.RestoreMetadata?.IsBootVolume == true,
                        IsSystemVolume = image.RestoreMetadata?.IsSystemVolume == true,
                        FileSystem = image.RestoreMetadata?.SourceFileSystem ?? string.Empty,
                        IsResizable = true,
                        TargetSize = image.RestoreMetadata?.PartitionLengthBytes > 0
                            ? (long)image.RestoreMetadata.PartitionLengthBytes
                            : 0
                    })
                    .OrderBy(volume => volume.PartitionOffsetBytes)
                    .ThenBy(volume => volume.PartitionNumber)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<VolumeInfo>();
            }
        }

        private async Task ScanBackupAsync()
        {
            if (string.IsNullOrWhiteSpace(txtBackupSource.Text))
            {
                return;
            }

            string originalTitle = Title;
            Title = "Restore Backup - Scanning...";
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            try
            {
                await ScanBackupSet(txtBackupSource.Text);

                pnlBackupInfo.Visibility = Visibility.Visible;
                grpRestoreOptions.IsEnabled = true;
                UpdateSelectedRestoreTargetKind();
                UpdateRestoreActionState();
            }
            finally
            {
                Title = originalTitle;
                System.Windows.Input.Mouse.OverrideCursor = null;
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
                ShowOwnedMessage("Please select a backup source.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                await ScanBackupAsync();
            }
            catch (Exception ex)
            {
                ShowOwnedMessage($"Error scanning backup: {ex.Message}", "Error",
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
                        lstRestorePoints.SelectedItem = null;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                        ShowOwnedMessage($"Error scanning backup: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }

        private void AnalyzeBackupFiles()
        {
            var selectedFilesBackups = new List<string>();
            var fullBackups = new List<string>();
            var incrementalBackups = new List<string>();
            var differentialBackups = new List<string>();
            var unknownBackups = new List<string>();

            foreach (string backup in backupFiles)
            {
                if (ShouldTreatBackupAsSingleFileRestorePoint(backup, GetBackupItemsForArchive(backup)))
                {
                    selectedFilesBackups.Add(backup);
                    continue;
                }

                string fileName = Path.GetFileName(backup);
                if (fileName.Contains("incremental", StringComparison.OrdinalIgnoreCase))
                {
                    incrementalBackups.Add(backup);
                    continue;
                }

                if (fileName.Contains("differential", StringComparison.OrdinalIgnoreCase))
                {
                    differentialBackups.Add(backup);
                    continue;
                }

                if (fileName.Contains("full", StringComparison.OrdinalIgnoreCase))
                {
                    fullBackups.Add(backup);
                    continue;
                }

                unknownBackups.Add(backup);
            }

            // Create restore points
            int pointNumber = 1;

            // Full backups
            AddRestorePoints(fullBackups, "Full", ref pointNumber);

            // Selected Files backups
            AddRestorePoints(selectedFilesBackups, "Selected Files", ref pointNumber, expandArchiveImages: false, forceSingleRestorePoint: true);

            // Incremental backups
            AddRestorePoints(incrementalBackups, "Incremental", ref pointNumber);

            // Differential backups
            AddRestorePoints(differentialBackups, "Differential", ref pointNumber);

            // If no specific types found, add all files as restore points
            if (restorePoints.Count == 0 && unknownBackups.Count > 0)
            {
                AddRestorePoints(unknownBackups, "Unknown", ref pointNumber);
            }
        }

        internal static bool IsSelectedFilesBackupArchive(string backupPath)
        {
            IReadOnlyList<string> backupItems = GetBackupItemsForArchive(backupPath);

            return ShouldTreatBackupAsSingleFileRestorePoint(backupPath, backupItems);
        }

        internal static bool ShouldTreatBackupAsSingleFileRestorePoint(string backupPath, IReadOnlyList<string> backupItems)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
            ArgumentNullException.ThrowIfNull(backupItems);

            if (!string.Equals(Path.GetExtension(backupPath), ".ssb", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fileName = Path.GetFileName(backupPath);
            if (fileName.Contains("_SelectedFiles_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return backupItems.Count > 0 &&
                   DetermineRestoreTargetKind("Unknown", backupPath, backupItems) == RestoreTargetKind.FileOrFolder;
        }

        private static IReadOnlyList<string> GetBackupItemsForArchive(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
            {
                return Array.Empty<string>();
            }

            try
            {
                using var preparedBackup = EncryptedBackupFileService.PrepareForRead(null, backupPath, Path.GetFileNameWithoutExtension(backupPath));
                var buffer = new StringBuilder(32768);
                int result = BackupEngineInterop.ListBackupContents(preparedBackup.WorkingPath, buffer, buffer.Capacity);
                if (result != 0)
                {
                    return Array.Empty<string>();
                }

                return buffer.ToString()
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private void AddRestorePoints(IEnumerable<string> backups, string backupType, ref int pointNumber, bool expandArchiveImages = true, bool forceSingleRestorePoint = false)
        {
            foreach (string backup in backups.OrderBy(GetEntryTimestamp))
            {
                IReadOnlyList<RestorePointArchiveImage> archiveImages = expandArchiveImages
                    ? GetRestorePointArchiveImages(backup)
                    : Array.Empty<RestorePointArchiveImage>();

                if (forceSingleRestorePoint)
                {
                    archiveImages = Array.Empty<RestorePointArchiveImage>();
                }

                DateTime timestamp = GetRestorePointTimestamp(backup, archiveImages);

                IReadOnlyList<RestorePoint> pointsForBackup = CreateRestorePointsForBackupFile(
                    backup,
                    backupType,
                    timestamp,
                    pointNumber,
                    archiveImages,
                    forceSingleRestorePoint);

                foreach (RestorePoint restorePoint in pointsForBackup)
                {
                    restorePoints.Add(restorePoint);
                }

                pointNumber += pointsForBackup.Count;
            }
        }

        private static IReadOnlyList<RestorePointArchiveImage> GetRestorePointArchiveImages(string backupPath)
        {
            if (!string.Equals(Path.GetExtension(backupPath), ".ssb", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<RestorePointArchiveImage>();
            }

            try
            {
                var imagesWithMetadata = NativeBackupMountManager.GetImageInfoWithRestoreMetadata(backupPath);
                if (imagesWithMetadata.Success && imagesWithMetadata.Images.Count > 0)
                {
                    return OrderRestorePointArchiveImages(imagesWithMetadata.Images.Select(image => new RestorePointArchiveImage
                    {
                        ImageIndex = image.ImageIndex,
                        Name = image.Name,
                        VolumeLabel = image.RestoreMetadata?.SourceVolumeLabel ?? string.Empty,
                        SourceVolumeMountPath = image.RestoreMetadata?.SourceVolumeMountPath ?? string.Empty,
                        PartitionOffsetBytes = image.RestoreMetadata?.PartitionOffsetBytes ?? 0,
                        VolumeIndex = image.RestoreMetadata?.VolumeIndex ?? 0,
                        BackupStartTime = image.RestoreMetadata?.BackupStartTime,
                        CollapseToSingleRestorePoint = image.RestoreMetadata is not null
                    }).ToList());
                }

                var images = NativeBackupMountManager.GetImageInfo(backupPath);
                if (images.Success && images.Images.Count > 0)
                {
                    return OrderRestorePointArchiveImages(images.Images.Select(image => new RestorePointArchiveImage
                    {
                        ImageIndex = image.ImageIndex,
                        Name = image.Name
                    }).ToList());
                }
            }
            catch
            {
            }

            return Array.Empty<RestorePointArchiveImage>();
        }

        internal static IReadOnlyList<RestorePoint> CreateRestorePointsForBackupFile(
            string backupPath,
            string backupType,
            DateTime timestamp,
            int startingPointNumber,
            IReadOnlyList<RestorePointArchiveImage>? archiveImages = null,
            bool forceSingleRestorePoint = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(backupType);

            var restorePointsForBackup = new List<RestorePoint>();
            IReadOnlyList<RestorePointArchiveImage> orderedArchiveImages = OrderRestorePointArchiveImages(archiveImages);

            if (forceSingleRestorePoint ||
                orderedArchiveImages.Count == 0 ||
                ShouldCollapseArchiveImagesToSingleRestorePoint(orderedArchiveImages))
            {
                restorePointsForBackup.Add(new RestorePoint
                {
                    DisplayName = $"Point {startingPointNumber}: {backupType} Backup",
                    Description = $"Created: {timestamp:yyyy-MM-dd HH:mm:ss}",
                    BackupType = backupType,
                    FilePath = backupPath,
                    Timestamp = timestamp
                });

                return restorePointsForBackup;
            }

            int pointNumber = startingPointNumber;
            foreach (RestorePointArchiveImage archiveImage in orderedArchiveImages)
            {
                string volumeDisplay = GetRestorePointVolumeDisplayName(archiveImage);
                restorePointsForBackup.Add(new RestorePoint
                {
                    DisplayName = $"Point {pointNumber}: {backupType} Backup - {volumeDisplay}",
                    Description = $"Created: {timestamp:yyyy-MM-dd HH:mm:ss}",
                    BackupType = backupType,
                    FilePath = backupPath,
                    Timestamp = timestamp,
                    ImageIndex = archiveImage.ImageIndex
                });

                pointNumber++;
            }

            return restorePointsForBackup;
        }

        private static bool ShouldCollapseArchiveImagesToSingleRestorePoint(IReadOnlyList<RestorePointArchiveImage> archiveImages)
        {
            if (archiveImages == null || archiveImages.Count == 0)
            {
                return false;
            }

            return archiveImages.Any(image => image.CollapseToSingleRestorePoint);
        }

        private static IReadOnlyList<RestorePointArchiveImage> OrderRestorePointArchiveImages(IReadOnlyList<RestorePointArchiveImage>? archiveImages)
        {
            if (archiveImages == null || archiveImages.Count == 0)
            {
                return Array.Empty<RestorePointArchiveImage>();
            }

            return archiveImages
                .Where(image => image.ImageIndex > 0)
                .OrderBy(image => image.PartitionOffsetBytes)
                .ThenBy(image => image.VolumeIndex <= 0 ? int.MaxValue : image.VolumeIndex)
                .ThenBy(image => image.ImageIndex)
                .ToList();
        }

        private static string GetRestorePointVolumeDisplayName(RestorePointArchiveImage archiveImage)
        {
            ArgumentNullException.ThrowIfNull(archiveImage);

            if (!string.IsNullOrWhiteSpace(archiveImage.VolumeLabel))
            {
                return archiveImage.VolumeLabel.Trim();
            }

            if (!string.IsNullOrWhiteSpace(archiveImage.SourceVolumeMountPath))
            {
                return archiveImage.SourceVolumeMountPath.TrimEnd('\\');
            }

            if (archiveImage.VolumeIndex > 0)
            {
                return $"Volume {archiveImage.VolumeIndex}";
            }

            if (!string.IsNullOrWhiteSpace(archiveImage.Name))
            {
                return archiveImage.Name.Trim();
            }

            return $"Image {archiveImage.ImageIndex}";
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

            txtBackupFileCount.Text = $"Found {backupFiles.Count} backup file(s)";
            txtBackupTotalSize.Text = $"Total size: {sizeGB:F2} GB";
            txtBackupRestorePointCount.Text = $"Restore points available: {restorePoints.Count}";
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
            if (_suppressRestorePointSelectionChanged)
            {
                UpdateSelectedRestoreTargetKind();
                return;
            }

            if (lstRestorePoints.SelectedItem is RestorePoint point)
            {
                _activeRestorePoint = point;
                await LoadBackupContents(point.FilePath);
            }

            UpdateSelectedRestoreTargetKind();
        }

        private void RestorePointItem_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not ListBoxItem item)
            {
                return;
            }

            if (ReferenceEquals(lstRestorePoints.SelectedItem, item.DataContext))
            {
                _suppressRestorePointSelectionChanged = true;
                item.IsSelected = false;
                lstRestorePoints.SelectedItem = null;
                _suppressRestorePointSelectionChanged = false;
                e.Handled = true;
            }
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
                        var items = ParseListedBackupItems(buffer.ToString());

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
            UpdateDestinationHelpText();
            UpdateRestoreActionState();
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

            // File/folder restores show the direct target-path entry.
            pnlLocationChoice.Visibility = isFileFolder ? Visibility.Visible : Visibility.Collapsed;

            if (grpRestoreOptions != null)
            {
                grpRestoreOptions.IsEnabled = ShouldEnableRestoreOptions(_restoreTargetKind);
            }

            if (pnlHyperVCloneDestination != null)
                pnlHyperVCloneDestination.Visibility = isHyperVVm ? Visibility.Visible : Visibility.Collapsed;

            // The right-side target tree:
            //   - Visible for all non-Hyper-V VM restores so the workflow remains consistent
            //   - Disabled for file/folder restores because those now use the direct target-path entry
            //   - Hidden only for Hyper-V VM clone restores where the tree is irrelevant
            bool showTree = ShouldShowRestoreTargetGroup(_restoreTargetKind);
            if (grpRestoreTarget != null)
            {
                grpRestoreTarget.Visibility = showTree ? Visibility.Visible : Visibility.Collapsed;
                grpRestoreTarget.IsEnabled = showTree && ShouldEnableRestoreTargetSelection(_restoreTargetKind);
            }

            // Update help text in the right-side panel
            if (showTree && txtDriveTreeHelp != null)
            {
                txtDriveTreeHelp.Text = isFileFolder
                    ? "Files/folders restore to the Target Location path on the left. Restore-target selection is disabled for this restore type."
                    : isDisk
                        ? "Choose the target disk to restore onto. You may select a disk (full repartition) or an individual volume. The boot/system disk cannot be selected."
                        : isHyperVVirtualDisk
                            ? "Choose the target volume for the Hyper-V virtual disk restore."
                            : "Choose the target disk or volume to restore onto. Selecting a disk will repartition it to accept the restored volume. The boot/system disk cannot be selected.";
            }

            // Auto-load drives on first show; also rebuild when the restore kind changes in a way
            // that affects which nodes are selectable (disk mode on vs off).
            bool needsDiskSelectability  = _restoreTargetKind == RestoreTargetKind.Disk ||
                                           _restoreTargetKind == RestoreTargetKind.Volume;
            bool hadDiskSelectability    = _lastBuiltTargetKind == RestoreTargetKind.Disk ||
                                           _lastBuiltTargetKind == RestoreTargetKind.Volume;
            bool selectabilityChanged    = needsDiskSelectability != hadDiskSelectability;

            if (showTree && treeViewRestoreTarget != null &&
                (treeViewRestoreTarget.Items.Count == 0 || selectabilityChanged))
            {
                if (_isLoadingTargets)
                {
                    _reloadRestoreTargetsAfterLoad = true;
                }
                else
                {
                    _ = LoadRestoreTargetDrivesAsync();
                }
            }

            UpdateRestoreActionButtonText();
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
                {
                    UpdateHyperVVmRestoreOptions();
                }
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

            System.Windows.Controls.TextBox hyperVVirtualDiskPathTextBox = txtHyperVVirtualDiskPath;
            System.Windows.Controls.TextBox newHyperVVmNameTextBox = txtNewHyperVVmName;
            System.Windows.Controls.TextBox newHyperVVmPathTextBox = txtNewHyperVVmPath;

            if (string.IsNullOrWhiteSpace(hyperVVirtualDiskPathTextBox.Text))
            {
                string defaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                hyperVVirtualDiskPathTextBox.Text = RegularHyperVRestoreHelper.BuildDefaultHyperVVirtualDiskPath(defaultDirectory, _preloadedBackup?.BackupName ?? string.Empty);
            }

            string virtualDiskPath = hyperVVirtualDiskPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(virtualDiskPath))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(newHyperVVmNameTextBox.Text))
            {
                newHyperVVmNameTextBox.Text = RegularHyperVRestoreHelper.GetDefaultHyperVVmName(virtualDiskPath);
            }

            if (string.IsNullOrWhiteSpace(newHyperVVmPathTextBox.Text))
            {
                newHyperVVmPathTextBox.Text = RegularHyperVRestoreHelper.GetDefaultHyperVVmStoragePath(virtualDiskPath);
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

        private void TargetLocation_TextChanged(object sender, TextChangedEventArgs e)
        {
            _selectedTargetPath = txtFolderRestoreDestination?.Text?.Trim();
            UpdateRestoreActionState();
        }

        private void BrowseRestoreDestination_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Multiselect = false,
                Title = "Select a Drive or Folder Path"
            };

            if (dialog.ShowDialog() == true)
            {
                _selectedTargetPath = dialog.FolderName;
                txtFolderRestoreDestination.Text = dialog.FolderName;
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
            if (txtHyperVVirtualDiskPath == null)
            {
                return;
            }

            System.Windows.Controls.TextBox hyperVVirtualDiskPathTextBox = txtHyperVVirtualDiskPath;

            string currentPath = hyperVVirtualDiskPathTextBox.Text?.Trim() ?? string.Empty;
            string initialDirectory = !string.IsNullOrWhiteSpace(currentPath)
                ? Path.GetDirectoryName(currentPath) ?? string.Empty
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string initialFileName = !string.IsNullOrWhiteSpace(currentPath)
                ? Path.GetFileName(currentPath)
                : Path.GetFileName(RegularHyperVRestoreHelper.BuildDefaultHyperVVirtualDiskPath(initialDirectory, _preloadedBackup?.BackupName));

            using var saveDialog = new SaveFileDialog
            {
                Title = "Select Hyper-V virtual disk destination",
                Filter = "Hyper-V Virtual Disk (*.vhdx)|*.vhdx",
                DefaultExt = "vhdx",
                AddExtension = true,
                OverwritePrompt = false,
                CheckPathExists = true,
                InitialDirectory = initialDirectory,
                FileName = initialFileName
            };

            if (saveDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _selectedTargetPath = saveDialog.FileName;
                hyperVVirtualDiskPathTextBox.Text = saveDialog.FileName;
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

            if ((_restoreTargetKind == RestoreTargetKind.Disk || _restoreTargetKind == RestoreTargetKind.Volume) &&
                !await PromptVolumeSelectionAndSizingAsync())
            {
                return;
            }

            if (_restoreTargetKind == RestoreTargetKind.FileOrFolder && chkOverwrite.IsChecked == true)
            {
                bool confirmed = ShowStartRestoreConfirmation(
                    "WARNING: Existing files in the target location may be overwritten when the restore starts.\n\nDo you want to continue?",
                    "Confirm File Restore");

                if (!confirmed)
                    return;
            }
            else if (_restoreTargetKind == RestoreTargetKind.Disk)
            {
                bool confirmed = ShowStartRestoreConfirmation(
                    "WARNING: The selected target disk will be formatted and repartitioned. ALL DATA ON THE TARGET DISK WILL BE LOST.\n\nDo you want to continue?",
                    "Confirm Disk Format");

                if (!confirmed)
                    return;
            }
            else if (_restoreTargetKind == RestoreTargetKind.Volume)
            {
                bool confirmed = ShowStartRestoreConfirmation(
                    "WARNING: The selected target volume will be formatted. ALL DATA ON THE TARGET VOLUME WILL BE LOST.\n\nDo you want to continue?",
                    "Confirm Volume Format");

                if (!confirmed)
                    return;
            }

            bool keepWindowOpen = ShouldKeepRestoreCompletionWindowOpen(
                _restoreTargetKind,
                _requireAlternateDestination,
                _selectedRestoreVolume,
                _selectedRestoreDiskGroup);

            ShowRestoreProgressWindow(keepWindowOpen);
        }

        /// <summary>
        /// For multi-image disk/Hyper-V backups: shows the volume-selection dialog followed by
        /// the partition-sizing window.  Returns false if the user cancels at any step.
        /// </summary>
        private async Task<bool> PromptVolumeSelectionAndSizingAsync()
        {
            // Only needed for disk and volume restore kinds with restore metadata.
            if (_restoreTargetKind != RestoreTargetKind.Disk &&
                _restoreTargetKind != RestoreTargetKind.Volume)
            {
                return true;
            }

            if (_restoreTargetKind == RestoreTargetKind.Disk && !EnsureDiskRestorePlanLoaded())
            {
                if (await TryPromptSingleVolumeFallbackSizingAsync())
                {
                    return true;
                }

                ShowOwnedMessage(
                    "This disk backup does not contain the reconstruction metadata required to size partitions and rebuild the target disk. Disk restore cannot continue.",
                    "Restore Metadata Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            if (_diskRestorePlan == null || !_diskRestorePlan.HasMetadata)
            {
                if (_restoreTargetKind == RestoreTargetKind.Volume)
                {
                    await TryPromptSingleVolumeFallbackSizingAsync();
                }

                return true;
            }

            // Build VolumeInfo list from the restore plan, ordered by partition offset
            var planVolumes = _diskRestorePlan.Volumes
                .OrderBy(v => v.PartitionOffsetBytes)
                .ThenBy(v => v.PartitionNumber)
                .ToList();

            var volumes = planVolumes.Select((v, idx) => new VolumeInfo
            {
                ImageIndex            = v.ImageIndex > 0 ? v.ImageIndex : idx + 1,
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

            // Step 1 – volume selection dialog (skip only when not needed)
            IReadOnlyList<VolumeInfo> volumesToResize;
            bool isGroupRestore;

            if (_restoreTargetKind == RestoreTargetKind.Volume && volumes.Count == 1)
            {
                // Single-volume restore: no selection prompt needed, go straight to sizing.
                volumesToResize = volumes;
                isGroupRestore = false;
                _selectedRestoreVolume = volumes[0];
                _selectedRestoreDiskGroup = null;
            }
            else
            {
                var selectionDialog = new RestoreVolumeSelectionDialog(
                    volumes,
                    isDiskOrHyperV,
                    isDiskOrHyperV
                        ? "This backup contains multiple volumes. Select a single volume or the entire disk group to restore."
                        : "Select the volume to restore from this backup.") { Owner = this };

                if (selectionDialog.ShowDialog() != true || !selectionDialog.Confirmed)
                    return false;

                isGroupRestore = selectionDialog.SelectedDiskGroup != null;
                if (isGroupRestore)
                    volumesToResize = selectionDialog.SelectedDiskGroup!;
                else
                    volumesToResize = new[] { selectionDialog.SelectedVolume! };
            }

            // Step 2 – partition sizing (requires a target disk to know capacity)
            long targetDiskSizeBytes = await GetTargetDiskSizeBytesAsync();
            if (targetDiskSizeBytes <= 0)
            {
                // Can't determine target size; skip resize and proceed with original sizes
                if (isGroupRestore)
                {
                    _selectedRestoreDiskGroup = volumesToResize.ToList();
                    _selectedRestoreVolume = null;
                }
                else
                {
                    _selectedRestoreVolume = volumesToResize.FirstOrDefault();
                    _selectedRestoreDiskGroup = null;
                }

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
                _selectedRestoreVolume = null;
            }
            else
            {
                _selectedRestoreVolume = resized.FirstOrDefault();
                _selectedRestoreDiskGroup = null;
            }

            return true;
        }

        private async Task<bool> TryPromptSingleVolumeFallbackSizingAsync()
        {
            if (_restoreTargetKind != RestoreTargetKind.Disk &&
                _restoreTargetKind != RestoreTargetKind.Volume)
            {
                return false;
            }

            if (_activeRestorePoint is not RestorePoint selectedPoint)
            {
                return false;
            }

            try
            {
                using var preparedBackup = EncryptedBackupFileService.PrepareForRead(this, selectedPoint.FilePath, Path.GetFileNameWithoutExtension(selectedPoint.FilePath));
                var imageInfoResult = NativeBackupMountManager.GetImageInfoWithRestoreMetadata(preparedBackup.WorkingPath);
                if (!imageInfoResult.Success || imageInfoResult.Images.Count != 1)
                {
                    return false;
                }

                long targetCapacityBytes = await GetTargetDiskSizeBytesAsync();
                if (targetCapacityBytes <= 0)
                {
                    return false;
                }

                var fallbackVolume = CreateSingleVolumeFallbackVolumeInfo(imageInfoResult.Images[0], targetCapacityBytes);
                var sizingWindow = new VolumeConfigurationWindow(
                    new List<VolumeInfo> { fallbackVolume },
                    targetCapacityBytes,
                    sourceAUS: 4096,
                    targetAUS: 4096)
                { Owner = this };

                if (sizingWindow.ShowDialog() != true || sizingWindow.FinalConfiguration == null)
                {
                    return false;
                }

                _selectedRestoreVolume = sizingWindow.FinalConfiguration.FirstOrDefault() ?? fallbackVolume;
                _selectedRestoreDiskGroup = null;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryPromptSingleVolumeFallbackSizingAsync exception: {ex.Message}");
                return false;
            }
        }

        private static VolumeInfo CreateSingleVolumeFallbackVolumeInfo(NativeBackupMountManager.SsbImageInfoResult imageInfo, long targetCapacityBytes)
        {
            ArgumentNullException.ThrowIfNull(imageInfo);

            long capacityBytes = Math.Max(targetCapacityBytes, 1);
            string label = !string.IsNullOrWhiteSpace(imageInfo.Name) ? imageInfo.Name : "Volume 1";

            return new VolumeInfo
            {
                ImageIndex = imageInfo.ImageIndex > 0 ? imageInfo.ImageIndex : 1,
                Label = label,
                Size = capacityBytes,
                UsedSpace = 0,
                IsResizable = true,
                FileSystem = "NTFS",
                PartitionNumber = 1,
                TargetSize = capacityBytes
            };
        }

        private bool EnsureDiskRestorePlanLoaded()
        {
            if (_diskRestorePlan?.HasMetadata == true)
            {
                return true;
            }

            if (_activeRestorePoint is not RestorePoint selectedPoint)
            {
                return false;
            }

            try
            {
                using var preparedBackup = EncryptedBackupFileService.PrepareForRead(this, selectedPoint.FilePath, Path.GetFileNameWithoutExtension(selectedPoint.FilePath));
                LoadDiskRestorePlan(preparedBackup.WorkingPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureDiskRestorePlanLoaded exception: {ex.Message}");
            }

            return _diskRestorePlan?.HasMetadata == true;
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


        private async Task PerformRestoreAsync(Window owner, BackupEngineInterop.ProgressCallback? progressCallback = null)
        {
            ArgumentNullException.ThrowIfNull(owner);

            var selectedPoint = _activeRestorePoint;
            if (selectedPoint == null) return;

            var destination = _restoreTargetKind == RestoreTargetKind.FileOrFolder
                ? txtFolderRestoreDestination.Text.Trim()
                : (_selectedTargetPath ?? string.Empty);

            await Task.Run(() =>
            {
                using var preparedBackup = EncryptedBackupFileService.PrepareForRead(owner, selectedPoint.FilePath, Path.GetFileNameWithoutExtension(selectedPoint.FilePath));
                int result;
                int lastLoggedPercent = -1;
                bool detailedFileProgressLogging = _restoreTargetKind == RestoreTargetKind.FileOrFolder ||
                                                  _restoreTargetKind == RestoreTargetKind.HyperVVirtualDisk ||
                                                  _restoreTargetKind == RestoreTargetKind.HyperVVm;

                BackupLogger.LogInfo(
                    RestoreLogJobName,
                    "Restore started",
                    $"Restore point: {selectedPoint.DisplayName}; Type: {_restoreTargetKind}; Source: {selectedPoint.FilePath}; Destination: {destination}");

                BackupEngineInterop.ProgressCallback callback = (percent, message) =>
                {
                    progressCallback?.Invoke(percent, message);

                    if (percent >= 0 && percent != lastLoggedPercent)
                    {
                        lastLoggedPercent = percent;
                        BackupLogger.LogInfo(RestoreLogJobName, $"Restore progress {percent}%", message ?? string.Empty);
                    }
                    else if (detailedFileProgressLogging && !string.IsNullOrWhiteSpace(message) &&
                             (message.StartsWith("Restore warning:", StringComparison.OrdinalIgnoreCase) ||
                              message.StartsWith("Restore error:", StringComparison.OrdinalIgnoreCase) ||
                              message.StartsWith("Restoring:", StringComparison.OrdinalIgnoreCase) ||
                              message.StartsWith("Processing:", StringComparison.OrdinalIgnoreCase)))
                    {
                        BackupLogger.LogInfo(RestoreLogJobName, message, $"Progress: {percent}%");
                    }
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
                    {
                        if (_preselectedRestore?.ScopeKind == RestoreScopeKind.SelectedItems && _preselectedRestore.SelectedItems.Count > 0)
                        {
                            string manifest = string.Join(Environment.NewLine, _preselectedRestore.SelectedItems);
                            result = BackupEngineInterop.RestoreWithManifest(
                                preparedBackup.WorkingPath,
                                destination,
                                manifest,
                                chkOverwrite.IsChecked == true,
                                restoreSystemState: false,
                                preservePermissions: false,
                                callback);
                        }
                        else
                        {
                            result = BackupEngineInterop.RestoreFiles(
                                preparedBackup.WorkingPath,
                                destination,
                                chkOverwrite.IsChecked == true,
                                callback);
                        }
                        break;
                    }
                }

                if (result != 0)
                {
                    var error = new StringBuilder(1024);
                    BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
                    BackupLogger.LogError(RestoreLogJobName, "Restore failed", error.ToString());
                    throw new Exception($"Restore failed: {error}");
                }

                BackupLogger.LogSuccess(
                    RestoreLogJobName,
                    "Restore completed successfully",
                    selectedPoint.FilePath,
                    $"Type: {_restoreTargetKind}; Destination: {destination}");
            });
        }

        private void ShowRestoreProgressWindow(bool keepWindowOpen)
        {
            if (_activeRestorePoint is not RestorePoint selectedPoint)
            {
                throw new InvalidOperationException("A restore point must be selected before starting the restore.");
            }

            Window parentWindow = Owner ?? this;
            var progressWindow = new RestoreProgressWindow(
                selectedPoint.DisplayName,
                keepWindowOpen,
                callback => PerformRestoreAsync(parentWindow, callback));

            WindowPositionManager.SetChildWindowPosition(progressWindow, parentWindow);

            Hide();

            try
            {
                progressWindow.ShowDialog();
            }
            finally
            {
                DialogResult = progressWindow.RestoreSucceeded;
                Close();
            }
        }

        private int RestoreDiskTarget(string preparedBackupPath, string selectedBackupPath, BackupEngineInterop.ProgressCallback callback)
        {
            BackupLogger.LogInfo(RestoreLogJobName, "Disk restore target validation started", $"Disk: {_selectedTargetDiskNumber}");
            PrepareDiskTarget();

            int targetDisk = _selectedTargetDiskNumber ?? -1;

            if (_diskRestorePlan?.HasMetadata == true)
            {
                BackupLogger.LogInfo(RestoreLogJobName, "Disk restore layout preparation started", $"Target disk: {targetDisk}; Metadata volumes: {_diskRestorePlan.Volumes.Count}");
                var ordered = (_selectedRestoreDiskGroup?.Count > 0
                        ? _selectedRestoreDiskGroup
                        : _selectedRestoreVolume is not null
                            ? new[] { _selectedRestoreVolume }
                            : _diskRestorePlan.Volumes.Select((v, idx) => new VolumeInfo
                            {
                                ImageIndex = v.ImageIndex > 0 ? v.ImageIndex : idx + 1,
                                Label = !string.IsNullOrWhiteSpace(v.SourceVolumeLabel) ? v.SourceVolumeLabel : $"Volume {v.PartitionNumber}",
                                Size = (long)v.PartitionLengthBytes,
                                UsedSpace = 0,
                                PartitionNumber = v.PartitionNumber,
                                PartitionOffsetBytes = v.PartitionOffsetBytes,
                                PartitionLengthBytes = v.PartitionLengthBytes,
                                PartitionStyle = v.PartitionStyle,
                                PartitionType = v.PartitionType,
                                SourceVolumeGuidPath = v.SourceVolumeGuidPath,
                                SourceVolumeMountPath = v.SourceVolumeMountPath,
                                IsBootVolume = v.IsBootVolume,
                                IsSystemVolume = v.IsSystemVolume,
                                FileSystem = v.SourceFileSystem,
                                IsResizable = true,
                                TargetSize = (long)v.PartitionLengthBytes
                            }))
                    .OrderBy(v => v.PartitionOffsetBytes)
                    .ThenBy(v => v.PartitionNumber)
                    .ToList();

                if (ordered.Count == 0)
                {
                    throw new InvalidOperationException("No restore volumes are available for the selected disk backup.");
                }

                var targetVolumePaths = TryReuseExistingTargetVolumes(targetDisk, ordered)
                    ?? PrepareDiskTargetVolumes(targetDisk, ordered);
                BackupLogger.LogInfo(RestoreLogJobName, "Disk restore target volumes ready", $"Target disk: {targetDisk}; Volume targets: {string.Join(", ", targetVolumePaths)}");
                int lastResult = 0;
                for (int i = 0; i < ordered.Count; i++)
                {
                    var vol = ordered[i];
                    int pct = (int)((i / (double)ordered.Count) * 90);
                    callback(pct, $"Restoring partition {i + 1} of {ordered.Count}: {vol.Label}…");
                    BackupLogger.LogInfo(RestoreLogJobName, "Disk restore partition started", $"Partition {i + 1} of {ordered.Count}; Label: {vol.Label}; ImageIndex: {vol.ImageIndex}; Target: {targetVolumePaths[i]}");
                    lastResult = BackupEngineInterop.RestoreVolumeFromImage(
                        preparedBackupPath,
                        vol.ImageIndex,
                        targetVolumePaths[i],
                        false,
                        callback);
                    if (lastResult != 0)
                    {
                        BackupLogger.LogError(RestoreLogJobName, "Disk restore partition failed", $"Partition {i + 1} of {ordered.Count}; Label: {vol.Label}; Target: {targetVolumePaths[i]}; Result: {lastResult}");
                        return lastResult;
                    }

                    BackupLogger.LogInfo(RestoreLogJobName, "Disk restore partition completed", $"Partition {i + 1} of {ordered.Count}; Label: {vol.Label}; Target: {targetVolumePaths[i]}");
                }

                BackupLogger.LogInfo(RestoreLogJobName, "Disk restore completed all partitions", $"Target disk: {targetDisk}; Partition count: {ordered.Count}");
                callback(100, "All partitions restored.");
                return 0;
            }

            if (_selectedRestoreVolume is not null)
            {
                BackupLogger.LogInfo(RestoreLogJobName, "Single-volume disk restore target preparation started", $"Target disk: {targetDisk}; Volume label: {_selectedRestoreVolume.Label}");
                var singleVolumeLayout = new[] { _selectedRestoreVolume };
                var targetVolumePaths = TryReuseExistingTargetVolumes(targetDisk, singleVolumeLayout)
                    ?? PrepareDiskTargetVolumes(targetDisk, singleVolumeLayout);

                int imageIndex = _selectedRestoreVolume.ImageIndex > 0 ? _selectedRestoreVolume.ImageIndex : 1;
                BackupLogger.LogInfo(RestoreLogJobName, "Single-volume disk restore started", $"ImageIndex: {imageIndex}; Target: {targetVolumePaths[0]}");
                callback(5, $"Restoring single volume to {targetVolumePaths[0]}...");
                return BackupEngineInterop.RestoreVolumeFromImage(
                    preparedBackupPath,
                    imageIndex,
                    targetVolumePaths[0],
                    false,
                    callback);
            }

            if (_isHyperVBackupPoint)
            {
                BackupLogger.LogInfo(RestoreLogJobName, "Hyper-V guest disk restore preparation started", $"Selected backup: {selectedBackupPath}");
                using var mountedDisk = MountPrimaryHyperVVirtualDisk(selectedBackupPath);
                int mountedDiskNumber = GetDiskNumberForDriveLetter(mountedDisk.DriveRoot);
                int imageIndex = _selectedRestoreVolume?.ImageIndex ?? _diskRestorePlan?.ImageIndex ?? 1;
                BackupLogger.LogInfo(RestoreLogJobName, "Hyper-V guest disk restore started", $"Mounted disk number: {mountedDiskNumber}; ImageIndex: {imageIndex}");
                return BackupEngineInterop.RestoreDiskFromImage(preparedBackupPath, imageIndex, mountedDiskNumber, false, callback);
            }

            {
                int imageIndex = _selectedRestoreVolume?.ImageIndex ?? _diskRestorePlan?.ImageIndex ?? 1;

                bool isArchiveFile = !string.IsNullOrWhiteSpace(preparedBackupPath) &&
                                     string.Equals(Path.GetExtension(preparedBackupPath), ".ssb", StringComparison.OrdinalIgnoreCase);

                if (isArchiveFile)
                {
                    BackupLogger.LogInfo(RestoreLogJobName, "Archive-based disk restore started", $"Target disk: {_selectedTargetDiskNumber}; ImageIndex: {imageIndex}");
                    return BackupEngineInterop.RestoreDiskFromImage(
                        preparedBackupPath,
                        imageIndex,
                        _selectedTargetDiskNumber ?? -1,
                        false,
                        callback);
                }

                BackupLogger.LogInfo(RestoreLogJobName, "Disk restore started", $"Target disk: {_selectedTargetDiskNumber}; ImageIndex: {imageIndex}; Metadata plan: {_diskRestorePlan?.HasMetadata == true}");

                return _diskRestorePlan?.HasMetadata == true
                    ? BackupEngineInterop.RestoreDiskFromImage(preparedBackupPath, imageIndex, _selectedTargetDiskNumber ?? -1, false, callback)
                    : BackupEngineInterop.RestoreDisk(preparedBackupPath, _selectedTargetDiskNumber ?? -1, false, callback);
            }
        }

        private List<string> PrepareDiskTargetVolumes(int targetDiskNumber, IReadOnlyList<VolumeInfo> orderedVolumes)
        {
            ArgumentNullException.ThrowIfNull(orderedVolumes);

            if (orderedVolumes.Count == 0)
            {
                throw new InvalidOperationException("No restore volumes were selected for the target disk.");
            }

            string partitionStyle = orderedVolumes
                .Select(volume => volume.PartitionStyle)
                .FirstOrDefault(style => !string.IsNullOrWhiteSpace(style)) ?? "GPT";

            long[] targetSizes = orderedVolumes
                .Select(volume => volume.TargetSize > 0 ? volume.TargetSize : volume.Size)
                .ToArray();

            string[] fileSystems = orderedVolumes
                .Select(GetSupportedRestoreFileSystem)
                .ToArray();

            string[] labels = orderedVolumes
                .Select(volume => string.IsNullOrWhiteSpace(volume.Label) ? $"Restore{volume.PartitionNumber}" : volume.Label)
                .ToArray();

            string[] partitionTypes = orderedVolumes
                .Select(volume => volume.PartitionType ?? string.Empty)
                .ToArray();

            long targetDiskCapacityBytes = GetTargetDiskCapacityBytes(targetDiskNumber);
            long requestedTotalBytes = targetSizes.Sum();
            bool expandLastPartition = targetDiskCapacityBytes <= 0 || AreSizesEquivalent(requestedTotalBytes, targetDiskCapacityBytes);

            string sizeArray = string.Join(",", targetSizes.Select(size => $"{size}L"));
            string fileSystemArray = string.Join(",", fileSystems.Select(value => $"'{EscapePowerShellSingleQuotedString(value)}'"));
            string labelArray = string.Join(",", labels.Select(value => $"'{EscapePowerShellSingleQuotedString(value)}'"));
            string partitionTypeArray = string.Join(",", partitionTypes.Select(value => $"'{EscapePowerShellSingleQuotedString(value)}'"));

            string script =
                $"$diskNumber={targetDiskNumber}; " +
                $"$partitionStyle='{EscapePowerShellSingleQuotedString(partitionStyle)}'; " +
                $"$sizes=@({sizeArray}); " +
                $"$fileSystems=@({fileSystemArray}); " +
                $"$labels=@({labelArray}); " +
                $"$partitionTypes=@({partitionTypeArray}); " +
                $"$expandLast={(expandLastPartition ? "$true" : "$false")}; " +
                "$created=@(); " +
                "Clear-Disk -Number $diskNumber -RemoveData -RemoveOEM -Confirm:$false -ErrorAction Stop; " +
                "Initialize-Disk -Number $diskNumber -PartitionStyle $partitionStyle -ErrorAction Stop; " +
                "for ($i = 0; $i -lt $sizes.Count; $i++) { " +
                "$partitionType = $partitionTypes[$i]; " +
                "$newPartitionParams = @{ DiskNumber = $diskNumber; AssignDriveLetter = $true; ErrorAction = 'Stop' }; " +
                "if ($i -eq $sizes.Count - 1 -and $expandLast) { $newPartitionParams['UseMaximumSize'] = $true; } else { $newPartitionParams['Size'] = [Int64]$sizes[$i]; } " +
                "if ($partitionStyle -eq 'GPT' -and -not [string]::IsNullOrWhiteSpace($partitionType)) { $newPartitionParams['GptType'] = $partitionType; } " +
                "$partition = New-Partition @newPartitionParams; " +
                "$fileSystem = $fileSystems[$i]; " +
                "if ([string]::IsNullOrWhiteSpace($fileSystem)) { $fileSystem = 'NTFS'; } " +
                "$label = $labels[$i]; " +
                "if ([string]::IsNullOrWhiteSpace($label)) { $label = 'SSBRestore'; } " +
                "Format-Volume -Partition $partition -FileSystem $fileSystem -NewFileSystemLabel $label -Confirm:$false -Force -ErrorAction Stop | Out-Null; " +
                "$driveLetter = ($partition | Get-Volume).DriveLetter; " +
                "if ([string]::IsNullOrWhiteSpace($driveLetter)) { throw 'Failed to assign a drive letter to a restored partition.'; } " +
                "$created += ($driveLetter + ':\\'); " +
                "}; " +
                "$created -join '|'";

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
                throw new InvalidOperationException($"Failed to partition and format the target disk. {errors}".Trim());
            }

            var createdVolumes = output
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();

            if (createdVolumes.Count != orderedVolumes.Count)
            {
                throw new InvalidOperationException("The target disk layout was created, but the number of formatted restore volumes did not match the selected layout.");
            }

            return createdVolumes;
        }

        private static string GetSupportedRestoreFileSystem(VolumeInfo volume)
        {
            string fileSystem = volume.FileSystem?.Trim() ?? string.Empty;
            return fileSystem.ToUpperInvariant() switch
            {
                "NTFS" => "NTFS",
                "FAT32" => "FAT32",
                "EXFAT" => "exFAT",
                "REFS" => "ReFS",
                _ => "NTFS"
            };
        }

        private static string EscapePowerShellSingleQuotedString(string value)
        {
            return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
        }

        private int RestoreVolumeTarget(string preparedBackupPath, string selectedBackupPath, BackupEngineInterop.ProgressCallback callback)
        {
            BackupLogger.LogInfo(RestoreLogJobName, "Volume restore target preparation started", $"Target path: {_selectedTargetPath}");
            PrepareVolumeTarget();

            string targetPath = _selectedTargetPath ?? string.Empty;

            if (_isHyperVBackupPoint)
            {
                BackupLogger.LogInfo(RestoreLogJobName, "Hyper-V guest volume restore preparation started", $"Selected backup: {selectedBackupPath}");
                using var mountedDisk = MountPrimaryHyperVVirtualDisk(selectedBackupPath);
                targetPath = mountedDisk.DriveRoot;
                int imageIndex = _selectedRestoreVolume?.ImageIndex ?? _diskRestorePlan?.ImageIndex ?? 1;
                BackupLogger.LogInfo(RestoreLogJobName, "Hyper-V guest volume restore started", $"ImageIndex: {imageIndex}; Target: {targetPath}");
                return BackupEngineInterop.RestoreVolumeFromImage(preparedBackupPath, imageIndex, targetPath, false, callback);
            }

            {
                int imageIndex = _selectedRestoreVolume?.ImageIndex ?? _diskRestorePlan?.ImageIndex ?? -1;
                if (imageIndex > 0)
                {
                    BackupLogger.LogInfo(RestoreLogJobName, "Volume restore from image started", $"ImageIndex: {imageIndex}; Target: {targetPath}");
                    return BackupEngineInterop.RestoreVolumeFromImage(preparedBackupPath, imageIndex, targetPath, false, callback);
                }

                BackupLogger.LogInfo(RestoreLogJobName, "Volume restore started", $"Target: {targetPath}");
                return BackupEngineInterop.RestoreVolume(preparedBackupPath, targetPath, false, callback);
            }
        }

        private bool ValidateRestore()
        {
            if (_activeRestorePoint == null)
            {
                ShowOwnedMessage("Please select a restore point.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (_restoreTargetKind == RestoreTargetKind.FileOrFolder)
            {
                string destinationPath = txtFolderRestoreDestination.Text.Trim();
                if (string.IsNullOrWhiteSpace(destinationPath))
                {
                    ShowOwnedMessage("Please enter a target location for the file or folder restore.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (!IsValidRestoreTargetPath(destinationPath))
                {
                    ShowOwnedMessage("Please enter a valid target path. Local and network paths are supported.", "Validation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                if (_requireAlternateDestination)
                {
                    string destinationRoot = destinationPath;
                    destinationRoot = Path.GetPathRoot(destinationRoot)?.TrimEnd('\\') ?? destinationRoot.TrimEnd('\\');
                    string systemRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(destinationRoot) &&
                        string.Equals(destinationRoot, systemRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        ShowOwnedMessage("Please select a restore destination that is not on the currently booted drive.",
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
                ShowOwnedMessage("Please select the target disk to restore to.", "Validation Error",
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
                        ShowOwnedMessage("Please select a non-running Hyper-V virtual machine to replace.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(txtHyperVRestoreDirectory?.Text))
                    {
                        ShowOwnedMessage("Please select an empty directory to restore the Hyper-V virtual machine into.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }

                    string restoreDir = txtHyperVRestoreDirectory.Text.Trim();
                    if (Directory.Exists(restoreDir) && Directory.EnumerateFileSystemEntries(restoreDir).Any())
                    {
                        ShowOwnedMessage("The selected restore directory is not empty. Please choose an empty directory.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }

                if (string.IsNullOrWhiteSpace(txtHyperVVmName.Text))
                {
                    ShowOwnedMessage("Please enter a virtual machine name for the restored VM.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            if (_restoreTargetKind == RestoreTargetKind.HyperVVirtualDisk)
            {
                if (string.IsNullOrWhiteSpace(txtHyperVVirtualDiskPath.Text))
                {
                    ShowOwnedMessage("Please select the Hyper-V virtual disk file to create or update.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (chkAttachToExistingHyperVVm.IsChecked == true)
                {
                    string selectedVm = cmbExistingHyperVVm.SelectedItem?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(selectedVm))
                    {
                        ShowOwnedMessage("Please select the existing Hyper-V virtual machine that should receive the restored virtual disk.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }

                if (rbCreateNewHyperVVm.IsChecked == true)
                {
                    if (string.IsNullOrWhiteSpace(txtNewHyperVVmName.Text))
                    {
                        ShowOwnedMessage("Please enter the name for the new Hyper-V virtual machine.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(txtNewHyperVVmPath.Text))
                    {
                        ShowOwnedMessage("Please select the storage folder for the new Hyper-V virtual machine.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
            }

            if (_restoreTargetKind == RestoreTargetKind.Volume && string.IsNullOrWhiteSpace(_selectedTargetPath))
            {
                ShowOwnedMessage("Please select the target disk or volume to restore to.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (_isHyperVBackupPoint && (_restoreTargetKind == RestoreTargetKind.Disk || _restoreTargetKind == RestoreTargetKind.Volume) && _activeRestorePoint != null && HyperVRestorePointHelper.FindPrimaryVirtualDisk(_activeRestorePoint.FilePath) == null)
            {
                ShowOwnedMessage("The selected Hyper-V backup point does not contain a guest VHD or VHDX file that can be restored to a disk or volume target.", "Validation Error",
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
            var selectedPoint = _activeRestorePoint;
            if (selectedPoint == null)
            {
                _restoreTargetKind = restorePoints.Count > 0
                    ? RestoreTargetKind.Disk
                    : RestoreTargetKind.FileOrFolder;

                UpdateDestinationHelpText();
                UpdateLocationPanelVisibility();
                UpdateRestoreActionState();
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
                UpdateRestoreActionState();
                return;
            }

            if (!_isHyperVBackupPoint && chkRestoreToHyperVDisk?.IsChecked == true)
            {
                _restoreTargetKind = RestoreTargetKind.HyperVVirtualDisk;
                UpdateDestinationHelpText();
                UpdateRestoreActionState();
                return;
            }

            // Use disk restore plan metadata as the strongest classification signal
            if (_diskRestorePlan?.HasMetadata == true)
            {
                _restoreTargetKind = _diskRestorePlan.Volumes.Count > 1
                    ? RestoreTargetKind.Disk
                    : RestoreTargetKind.Volume;
                UpdateDestinationHelpText();
                UpdateLocationPanelVisibility();
                UpdateRestoreActionState();
                return;
            }

            _restoreTargetKind = DetermineRestoreTargetKind(
                selectedPoint.BackupType,
                selectedPoint.FilePath,
                lstBackupItems.Items.Cast<object>().Select(item => item?.ToString() ?? string.Empty));

            UpdateDestinationHelpText();
            UpdateLocationPanelVisibility();
            UpdateRestoreActionState();
        }

        internal static RestoreTargetKind DetermineRestoreTargetKind(string? backupType, string? filePath, IEnumerable<string> backupItems)
        {
            ArgumentNullException.ThrowIfNull(backupItems);

            if (string.Equals(backupType, "Selected Files", StringComparison.OrdinalIgnoreCase))
            {
                return RestoreTargetKind.FileOrFolder;
            }

            bool hasItems = false;
            bool hasDisk = false;
            bool hasVolume = false;
            bool hasNonDiskOrVolumeItem = false;

            foreach (string itemText in backupItems.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                hasItems = true;

                if (IsDiskBackupSurface(itemText))
                {
                    hasDisk = true;
                    continue;
                }

                if (IsVolumeBackupSurface(itemText))
                {
                    hasVolume = true;
                    continue;
                }

                hasNonDiskOrVolumeItem = true;
                break;
            }

            if (hasNonDiskOrVolumeItem)
            {
                return RestoreTargetKind.FileOrFolder;
            }

            if (hasDisk)
            {
                return RestoreTargetKind.Disk;
            }

            if (hasVolume)
            {
                return RestoreTargetKind.Volume;
            }

            if (!hasItems)
            {
                string normalizedFilePath = filePath ?? string.Empty;
                if (normalizedFilePath.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
                    normalizedFilePath.Contains("drive", StringComparison.OrdinalIgnoreCase) ||
                    normalizedFilePath.Contains("physical", StringComparison.OrdinalIgnoreCase))
                {
                    return RestoreTargetKind.Disk;
                }

                if (normalizedFilePath.Contains("volume", StringComparison.OrdinalIgnoreCase) ||
                    normalizedFilePath.Contains("partition", StringComparison.OrdinalIgnoreCase))
                {
                    return RestoreTargetKind.Volume;
                }
            }

            return RestoreTargetKind.FileOrFolder;
        }

        private static bool IsDiskBackupSurface(string itemText)
        {
            return itemText.Contains("PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVolumeBackupSurface(string itemText)
        {
            if (string.IsNullOrWhiteSpace(itemText))
            {
                return false;
            }

            string trimmed = itemText.Trim();
            return trimmed.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("\\?\\Volume{", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith(@":\", StringComparison.OrdinalIgnoreCase) ||
                   (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':');
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
                _ => "File/folder restore: enter the target location on the left. Local and network paths are supported."
            };
        }

        private static bool IsValidRestoreTargetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                _ = Path.GetFullPath(path);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }
        }

        private void UpdateRestoreActionButtonText()
        {
            if (btnRestore == null)
            {
                return;
            }

            btnRestore.Content = _restoreTargetKind is RestoreTargetKind.Disk or RestoreTargetKind.Volume
                ? "Next"
                : "Start Restore";
        }

        private bool ShowStartRestoreConfirmation(string message, string title)
        {
            var dialog = new CustomDialog
            {
                Owner = this
            };

            dialog.Configure(
                message,
                title,
                DialogButtons.OKCancel,
                DialogIcon.Warning,
                primaryButtonText: "Start Restore",
                secondaryButtonText: "Cancel");

            dialog.ShowDialog();
            return dialog.Result == CustomDialogResult.OK;
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
                // Directory-mode: prefer the new clone destination panel fields, then fall back
                // to the existing txtHyperVRestoreDirectory field (from the HyperV VM options pane).
                string restoreDir;
                if (rbHyperVCloneAlternate?.IsChecked == true
                    && !string.IsNullOrWhiteSpace(txtHyperVCloneVmFolder?.Text))
                {
                    restoreDir = txtHyperVCloneVmFolder.Text.Trim();
                }
                else if (rbHyperVCloneDefault?.IsChecked == true || string.IsNullOrWhiteSpace(txtHyperVRestoreDirectory?.Text))
                {
                    restoreDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                        "Hyper-V", "Virtual Machines");
                }
                else
                {
                    restoreDir = txtHyperVRestoreDirectory.Text.Trim();
                }

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

            bool restoreAsDisk = _restoreTargetKind == RestoreTargetKind.Disk;

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

                int imageIndex = _diskRestorePlan?.ImageIndex ?? 1;

                int result = restoreAsDisk
                    ? (_diskRestorePlan?.HasMetadata == true
                        ? BackupEngineInterop.RestoreDiskFromImage(preparedBackupPath, imageIndex, GetDiskNumberForDriveLetter(mountedDriveRoot), false, callback)
                        : BackupEngineInterop.RestoreDisk(preparedBackupPath, GetDiskNumberForDriveLetter(mountedDriveRoot), false, callback))
                    : BackupEngineInterop.RestoreVolumeFromImage(preparedBackupPath, imageIndex, targetVolumePath, false, callback);

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

        private void ExpandAllTarget_Click(object sender, RoutedEventArgs e)
        {
            foreach (TreeViewItem tvi in treeViewRestoreTarget.Items)
                SetTreeViewItemExpanded(tvi, true);
        }

        private void CollapseAllTarget_Click(object sender, RoutedEventArgs e)
        {
            foreach (TreeViewItem tvi in treeViewRestoreTarget.Items)
                SetTreeViewItemExpanded(tvi, false);
        }

        private static void SetTreeViewItemExpanded(TreeViewItem tvi, bool expanded)
        {
            tvi.IsExpanded = expanded;
            foreach (TreeViewItem child in tvi.Items.OfType<TreeViewItem>())
                SetTreeViewItemExpanded(child, expanded);
        }

        private async void ShowHiddenPartitionsTarget_Click(object sender, RoutedEventArgs e)
        {
            _showHiddenPartitionsTarget = chkShowHiddenPartitionsTarget.IsChecked == true;
            await LoadRestoreTargetDrivesAsync();
        }

        private void HyperVCloneDestination_Changed(object sender, RoutedEventArgs e)
        {
            if (pnlHyperVCloneAlternate == null)
                return;
            pnlHyperVCloneAlternate.Visibility = rbHyperVCloneAlternate?.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BrowseHyperVCloneVmFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new FolderBrowserDialog { Description = "Select virtual machine config folder", ShowNewFolderButton = true };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                txtHyperVCloneVmFolder.Text = dlg.SelectedPath;
        }

        private void BrowseHyperVCloneDiskFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new FolderBrowserDialog { Description = "Select virtual disk data folder", ShowNewFolderButton = true };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                txtHyperVCloneDiskFolder.Text = dlg.SelectedPath;
        }

        /// <summary>
        /// Populates the restore-target tree with physical disks, their volumes, and Hyper-V VMs.
        /// Boot/system disk items are shown greyed-out and unselectable.
        /// </summary>
        private async Task LoadRestoreTargetDrivesAsync()
        {
            if (_isLoadingTargets)
                return;

            _isLoadingTargets = true;
            _reloadRestoreTargetsAfterLoad = false;
            loadingTargetOverlay.Visibility = Visibility.Visible;
            treeViewRestoreTarget.Items.Clear();
            _restoreTargetItems.Clear();
            _selectedTargetPath = null;
            _selectedTargetDiskNumber = null;
            txtSelectedTargetLabel.Text = "No target selected";
            btnRestore.IsEnabled = false;

            RestoreTargetKind buildTargetKind = _restoreTargetKind;

            try
            {
                var protectedIndexes = GetProtectedDiskIndexes();
                // Disk nodes are selectable for both Disk and Volume restores (volume restore to an entire disk
                // repartitions the target disk to accept the restored volume). Protected/hidden never selectable.
                bool diskMode = buildTargetKind == RestoreTargetKind.Disk ||
                                buildTargetKind == RestoreTargetKind.Volume;
                bool showHidden = _showHiddenPartitionsTarget;

                await Task.Run(() =>
                {
                    try
                    {
                        // ── Physical disks ──────────────────────────────────────────────────
                        ManagementObjectSearcher diskSearcher;
                        try
                        {
                            diskSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive ORDER BY Index");
                            _ = diskSearcher.Get().Count; // test
                        }
                        catch
                        {
                            diskSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                        }

                        using (diskSearcher)
                        {
                            foreach (ManagementObject disk in diskSearcher.Get())
                            {
                                try
                                {
                                    if (!int.TryParse(disk["Index"]?.ToString(), out int diskIdx))
                                        continue;

                                    bool isProtected = protectedIndexes.Contains(diskIdx);
                                    string model = disk["Model"]?.ToString()?.Trim() ?? $"Disk {diskIdx}";
                                    long diskBytes = 0;
                                    try { diskBytes = Convert.ToInt64(disk["Size"] ?? 0); } catch { }
                                    double diskGb = diskBytes / (1024.0 * 1024.0 * 1024.0);

                                    string diskLabel = $"Disk {diskIdx} — {model}  ({diskGb:F1} GB)" +
                                        (isProtected ? "  [Boot — cannot restore]" : string.Empty);

                                    var diskItem = new DriveTreeItem
                                    {
                                        Name = diskLabel,
                                        FullPath = $"\\\\.\\PHYSICALDRIVE{diskIdx}",
                                        ItemType = DriveTreeItemType.Disk,
                                        PartitionNumber = diskIdx,
                                        Size = diskBytes,
                                        IsBootVolume = isProtected,
                                        IsExpanded = true
                                    };

                                    // Layer 1: ASSOCIATORS via DeviceID
                                    bool volumesFound = TargetTryLoadVolumesViaWMI(diskItem, diskIdx, isProtected, showHidden);

                                    // Layer 2: DiskIndex query fallback
                                    if (!volumesFound)
                                        volumesFound = TargetTryLoadVolumesViaAltWMI(diskItem, diskIdx, isProtected, showHidden);

                                    // Layer 3: DriveInfo fallback — attach all fixed drives to disk 0
                                    if (!volumesFound)
                                        TargetLoadVolumesFallback(diskItem, diskIdx);

                                    if (diskItem.Children.Count == 0)
                                        diskItem.Children.Add(new DriveTreeItem
                                        {
                                            Name = "(No accessible volumes)",
                                            ItemType = DriveTreeItemType.Volume,
                                            Parent = diskItem
                                        });

                                    Dispatcher.Invoke(() =>
                                    {
                                        _restoreTargetItems.Add(diskItem);
                                        treeViewRestoreTarget.Items.Add(
                                            CreateRestoreTargetTreeItem(diskItem, diskMode, isProtected));
                                    });
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Target tree — error processing disk: {ex.Message}");
                                }
                            }
                        }

                        // ── Hyper-V VMs (PowerShell CSV) ───────────────────────────────────
                        string hvOutput = string.Empty;
                        try
                        {
                            var ps = Process.Start(new ProcessStartInfo
                            {
                                FileName = "powershell.exe",
                                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " +
                                    "\"Get-VM | Select-Object -Property Name,State | ConvertTo-Csv -NoTypeInformation\"",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                CreateNoWindow = true
                            });
                            hvOutput = ps?.StandardOutput.ReadToEnd() ?? string.Empty;
                            ps?.WaitForExit();
                        }
                        catch { }

                        if (!string.IsNullOrWhiteSpace(hvOutput))
                        {
                            var hvRoot = new DriveTreeItem
                            {
                                Name = "Hyper-V Virtual Machines",
                                FullPath = string.Empty,
                                ItemType = DriveTreeItemType.HyperVSystem,
                                IsExpanded = true
                            };

                            foreach (string csvLine in hvOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
                            {
                                var parts = csvLine.Trim().Trim('"').Split(new[] { "\",\"" }, StringSplitOptions.None);
                                if (parts.Length < 2) continue;

                                string vmName  = parts[0].Trim().Trim('"');
                                string vmState = parts[1].Trim().Trim('"');
                                if (string.IsNullOrWhiteSpace(vmName)) continue;

                                bool isRunning = string.Equals(vmState, "Running", StringComparison.OrdinalIgnoreCase);

                                hvRoot.Children.Add(new DriveTreeItem
                                {
                                    Name = isRunning ? $"{vmName}  (Running)" : vmName,
                                    FullPath = vmName,
                                    ItemType = DriveTreeItemType.HyperVSystem,
                                    Parent = hvRoot
                                });
                            }

                            if (hvRoot.Children.Count > 0)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    _restoreTargetItems.Add(hvRoot);
                                    treeViewRestoreTarget.Items.Add(
                                        CreateRestoreTargetTreeItem(hvRoot, diskMode, false));
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                            ShowOwnedMessage(
                                $"Error loading restore targets: {ex.Message}",
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error));
                    }
                });
            }
            finally
            {
                bool reloadAfterLoad = _reloadRestoreTargetsAfterLoad || buildTargetKind != _restoreTargetKind;
                _isLoadingTargets = false;
                _reloadRestoreTargetsAfterLoad = false;
                _lastBuiltTargetKind = buildTargetKind;
                loadingTargetOverlay.Visibility = Visibility.Collapsed;

                if (reloadAfterLoad)
                {
                    _ = LoadRestoreTargetDrivesAsync();
                }
            }
        }

        // ── Volume-loading helpers for the restore target tree

        /// <summary>Layer 1: ASSOCIATORS query via DeviceID.</summary>
        private bool TargetTryLoadVolumesViaWMI(DriveTreeItem diskItem, int diskIdx, bool isProtected, bool showHidden)
        {
            bool found = false;
            try
            {
                string deviceId = diskItem.FullPath.Replace("\\", "\\\\");
                using var partSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{deviceId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                foreach (ManagementObject partition in partSearcher.Get())
                {
                    string? partId = partition["DeviceID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(partId)) continue;

                    using var logSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partId}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                    bool hasLogical = false;
                    foreach (ManagementObject logical in logSearcher.Get())
                    {
                        hasLogical = true;
                        string? dl = logical["DeviceID"]?.ToString();
                        if (string.IsNullOrWhiteSpace(dl)) continue;
                        if (TargetAddVolume(diskItem, dl, diskIdx)) found = true;
                    }

                    if (!hasLogical && showHidden)
                        found |= TargetAddHiddenPartition(diskItem, partition, diskIdx, isProtected);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"TargetTryLoadVolumesViaWMI: {ex.Message}"); }
            return found;
        }

        /// <summary>Layer 2: DiskIndex partition query.</summary>
        private bool TargetTryLoadVolumesViaAltWMI(DriveTreeItem diskItem, int diskIdx, bool isProtected, bool showHidden)
        {
            bool found = false;
            try
            {
                using var partSearcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_DiskPartition WHERE DiskIndex = {diskIdx}");

                foreach (ManagementObject partition in partSearcher.Get())
                {
                    string? partId = partition["DeviceID"]?.ToString();
                    using var logSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partId}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                    bool hasLogical = false;
                    foreach (ManagementObject logical in logSearcher.Get())
                    {
                        hasLogical = true;
                        string? dl = logical["DeviceID"]?.ToString();
                        if (string.IsNullOrWhiteSpace(dl)) continue;
                        if (TargetAddVolume(diskItem, dl, diskIdx)) found = true;
                    }

                    if (!hasLogical && showHidden)
                        found |= TargetAddHiddenPartition(diskItem, partition, diskIdx, isProtected);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"TargetTryLoadVolumesViaAltWMI: {ex.Message}"); }
            return found;
        }

        /// <summary>Layer 3: DriveInfo fallback — attach all fixed drives to disk 0.</summary>
        private static void TargetLoadVolumesFallback(DriveTreeItem diskItem, int diskIdx)
        {
            if (diskIdx != 0) return;
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                    TargetAddVolumeFromDriveInfo(diskItem, drive, diskIdx);
                }
                catch { }
            }
        }

        private static bool TargetAddVolume(DriveTreeItem diskItem, string driveLetter, int diskIdx)
        {
            try
            {
                var di = new DriveInfo(driveLetter);
                if (!di.IsReady) return false;
                return TargetAddVolumeFromDriveInfo(diskItem, di, diskIdx);
            }
            catch { return false; }
        }

        private static bool TargetAddVolumeFromDriveInfo(DriveTreeItem diskItem, DriveInfo di, int diskIdx)
        {
            string systemRoot = Path.GetPathRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? string.Empty;
            // Only the volume that actually contains the running OS is the boot volume.
            // isProtected (disk-level) is used to grey the disk node; it must NOT propagate the
            // [Boot] label to every data volume that happens to share the same physical disk.
            bool isBootVol = di.Name.TrimEnd('\\').Equals(
                systemRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

            string label = string.IsNullOrEmpty(di.VolumeLabel) ? "Local Disk" : di.VolumeLabel;
            double gb = di.TotalSize / (1024.0 * 1024.0 * 1024.0);
            string name = $"{di.Name.TrimEnd('\\')}  ({label})  {gb:F1} GB" +
                (isBootVol ? "  [Boot]" : string.Empty);

            diskItem.Children.Add(new DriveTreeItem
            {
                Name = name,
                FullPath = di.Name.TrimEnd('\\') + "\\",
                ItemType = DriveTreeItemType.Volume,
                Size = di.TotalSize,
                IsBootVolume = isBootVol,
                IsHiddenPartition = false,
                Parent = diskItem,
                PartitionNumber = diskIdx
            });
            return true;
        }

        private static bool TargetAddHiddenPartition(DriveTreeItem diskItem, ManagementObject partition, int diskIdx, bool isProtected)
        {
            try
            {
                // This method is only called when showHidden == true and the partition has no
                // associated logical disk (no drive letter). Show all such partitions; derive a
                // friendly type label from the WMI Type string.
                string partType = partition["Type"]?.ToString() ?? string.Empty;

                // Produce a short, readable label: "GPT: System" -> "EFI System",
                // "GPT: Microsoft Reserved" -> "MSR", etc.
                string label = partType switch
                {
                    var t when t.Contains("System", StringComparison.OrdinalIgnoreCase)     => "EFI System",
                    var t when t.Contains("Reserved", StringComparison.OrdinalIgnoreCase)   => "MSR",
                    var t when t.Contains("Recovery", StringComparison.OrdinalIgnoreCase)   => "Recovery",
                    var t when t.Contains("Basic Data", StringComparison.OrdinalIgnoreCase) => "Data (no letter)",
                    var t when !string.IsNullOrWhiteSpace(t)                                 => t,
                    _                                                                         => "Hidden Partition"
                };

                long ps = 0;
                try { ps = Convert.ToInt64(partition["Size"] ?? 0); } catch { }
                double gb = ps / (1024.0 * 1024.0 * 1024.0);

                diskItem.Children.Add(new DriveTreeItem
                {
                    Name = $"{label}  (Hidden)  {gb:F1} GB",
                    FullPath = string.Empty,
                    ItemType = DriveTreeItemType.Volume,
                    IsBootVolume = isProtected,
                    IsHiddenPartition = true,
                    Parent = diskItem,
                    PartitionNumber = diskIdx
                });
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Creates a tree view item for the restore-target tree mirroring the backup source tree style.
        /// Disk and volume items use CheckBox controls for selection (single-select enforced on click).
        /// Boot/protected items are greyed and not selectable.
        /// HyperVSystem root nodes are bold group headers; their children are selectable.
        /// Stops at the volume level — no folder expansion.
        /// </summary>
        private TreeViewItem CreateRestoreTargetTreeItem(DriveTreeItem item, bool diskMode, bool isProtected)
        {
            var tvi = new TreeViewItem { IsExpanded = item.IsExpanded };

            var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

            bool isHvRoot = item.ItemType == DriveTreeItemType.HyperVSystem && item.Parent == null;
            bool isHvVm  = item.ItemType == DriveTreeItemType.HyperVSystem && item.Parent != null;
            bool isHidden = item.IsHiddenPartition;

            // Disks are selectable for disk or volume restores (volume restore to a whole disk
            // repartitions the target to accept the restored volume). Protected/hidden never selectable.
            bool isSelectable = !isProtected && !isHvRoot && !isHidden &&
                (isHvVm ||
                 item.ItemType == DriveTreeItemType.Volume ||
                 (diskMode && item.ItemType == DriveTreeItemType.Disk));

            if (!isHvRoot)
            {
                var cb = new System.Windows.Controls.CheckBox
                {
                    IsChecked  = item.IsChecked,
                    IsEnabled  = isSelectable,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };

                cb.Click += (s, e) =>
                {
                    // Single-select: uncheck every other node first
                    if (cb.IsChecked == true)
                    {
                        UncheckAllRestoreTargetItems(treeViewRestoreTarget, cb);
                        cb.IsChecked = true;
                        item.IsChecked = true;
                        OnRestoreTargetSelected(item);
                    }
                    else
                    {
                        item.IsChecked = false;
                        _selectedTargetPath = null;
                        _selectedTargetDiskNumber = null;
                        txtSelectedTargetLabel.Text = "No target selected";
                        UpdateRestoreActionState();
                    }
                    e.Handled = true;
                };

                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(item.IsChecked))
                        cb.IsChecked = item.IsChecked;
                };

                panel.Children.Add(cb);
            }

            var txt = new TextBlock
            {
                Text = item.Name,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = isHvRoot ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = (!isSelectable && !isHvRoot)
                    ? System.Windows.SystemColors.GrayTextBrush
                    : System.Windows.SystemColors.ControlTextBrush
            };

            panel.Children.Add(txt);
            tvi.Header = panel;

            foreach (var child in item.Children)
                tvi.Items.Add(CreateRestoreTargetTreeItem(child, diskMode, child.IsBootVolume));

            tvi.IsExpanded = item.IsExpanded;
            tvi.Expanded  += (s, e) => { if (e.Source == tvi) item.IsExpanded = true; };
            tvi.Collapsed += (s, e) => { if (e.Source == tvi) item.IsExpanded = false; };

            return tvi;
        }

        private void OnRestoreTargetSelected(DriveTreeItem item)
        {
            if (item.ItemType == DriveTreeItemType.HyperVSystem)
            {
                // item.Name contains "VmName (Running)" or just "VmName"
                bool isRunning = item.Name.EndsWith(" (Running)", StringComparison.OrdinalIgnoreCase);
                string vmName = item.FullPath; // stored as the normalized VM name

                if (isRunning)
                {
                    var result = ShowOwnedMessage(
                        $"The virtual machine '{vmName}' is currently running.\n\n" +
                        "It must be shut down before the restore can proceed. " +
                        "All unsaved data inside the VM will be lost.\n\n" +
                        "Do you want to shut it down now and continue?",
                        "Virtual Machine Running",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.OK)
                    {
                        UncheckAllRestoreTargetItems(treeViewRestoreTarget);
                        _selectedTargetPath = null;
                        _selectedTargetDiskNumber = null;
                        txtSelectedTargetLabel.Text = "No target selected";
                        UpdateRestoreActionState();
                        return;
                    }

                    // Shut down the VM synchronously (short timeout)
                    try
                    {
                        var ps = Process.Start(new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Stop-VM -Name '{vmName.Replace("'", "''")}' -Force -ErrorAction Stop\"",
                            UseShellExecute = false,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        });
                        ps?.WaitForExit(30000);
                    }
                    catch (Exception ex)
                    {
                        ShowOwnedMessage(
                            $"Failed to shut down '{vmName}': {ex.Message}\n\nPlease stop the VM manually and try again.",
                            "Shutdown Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        UncheckAllRestoreTargetItems(treeViewRestoreTarget);
                        _selectedTargetPath = null;
                        _selectedTargetDiskNumber = null;
                        txtSelectedTargetLabel.Text = "No target selected";
                        UpdateRestoreActionState();
                        return;
                    }
                }

                _selectedTargetPath = vmName;
                _selectedTargetDiskNumber = null;
                txtSelectedTargetLabel.Text = $"Selected: {vmName}";
                UpdateRestoreActionState();
                return;
            }

            _selectedTargetPath = item.FullPath;
            _selectedTargetDiskNumber = item.ItemType == DriveTreeItemType.Disk
                ? item.PartitionNumber
                : item.Parent?.PartitionNumber;

            txtSelectedTargetLabel.Text = $"Selected: {item.Name.Split(new[] { "  [" }, StringSplitOptions.None)[0].Trim()}";
            
            UpdateRestoreActionState();
        }

        /// <summary>Walks every tree item and unchecks any RestoreTarget radio button.</summary>
        /// <summary>
        /// Unchecks all CheckBox controls in the restore target tree, optionally skipping one.
        /// </summary>
        private static void UncheckAllRestoreTargetItems(ItemsControl parent, System.Windows.Controls.CheckBox? except = null)
        {
            foreach (var obj in parent.Items)
            {
                if (obj is not TreeViewItem tvi)
                    continue;

                if (tvi.Header is StackPanel sp)
                {
                    foreach (var cb in sp.Children.OfType<System.Windows.Controls.CheckBox>())
                    {
                        if (!ReferenceEquals(cb, except))
                            cb.IsChecked = false;
                    }
                }

                UncheckAllRestoreTargetItems(tvi, except);
            }
        }

        private List<int> GetProtectedDiskIndexes()
        {
            var indexes = new List<int>();
            try
            {
                string systemRoot = Path.GetPathRoot(
                    Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? string.Empty;
                string osDriveLetter = systemRoot.TrimEnd('\\').TrimEnd(':');

                if (string.IsNullOrWhiteSpace(osDriveLetter))
                    return indexes;

                // Map only the active OS logical drive (for example C:) to its backing physical disk.
                using var ldSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{osDriveLetter}:'}} " +
                    "WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (ManagementObject partition in ldSearcher.Get())
                {
                    string? partId = partition["DeviceID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(partId))
                        continue;

                    using var ddSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partId}'}} " +
                        "WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                    foreach (ManagementObject drive in ddSearcher.Get())
                    {
                        if (int.TryParse(drive["Index"]?.ToString(), out int diskIdx) &&
                            !indexes.Contains(diskIdx))
                        {
                            indexes.Add(diskIdx);
                        }
                    }
                }

                // Fallback if WMI association fails: resolve from the Win32_LogicalDiskToPartition mapping string.
                if (indexes.Count == 0)
                {
                    using var relSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{osDriveLetter}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                    foreach (ManagementObject partition in relSearcher.Get())
                    {
                        if (int.TryParse(partition["DiskIndex"]?.ToString(), out int diskIdx) &&
                            !indexes.Contains(diskIdx))
                        {
                            indexes.Add(diskIdx);
                        }
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
            if (!_selectedTargetDiskNumber.HasValue)
            {
                throw new InvalidOperationException("No target disk selected.");
            }

            BackupLogger.LogInfo(RestoreLogJobName, "Disk target selected", $"Disk number: {_selectedTargetDiskNumber}");
        }

        private void PrepareVolumeTarget()
        {
            string volumePath = _selectedTargetPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(volumePath))
            {
                throw new InvalidOperationException("No target volume selected.");
            }

            if (_selectedRestoreVolume?.TargetSize > 0)
            {
                BackupLogger.LogInfo(RestoreLogJobName, "Volume resize started", $"Target: {volumePath}; Requested size: {_selectedRestoreVolume.TargetSize}");
                ResizeTargetVolumeIfNeeded(volumePath, _selectedRestoreVolume.TargetSize);
                BackupLogger.LogInfo(RestoreLogJobName, "Volume resize completed", $"Target: {volumePath}");
            }

            BackupLogger.LogInfo(RestoreLogJobName, "Volume format started", $"Target: {volumePath}; File system: {GetSelectedRestoreFileSystem()}; Label: {_selectedRestoreVolume?.Label}");
            FormatTargetVolume(volumePath, GetSelectedRestoreFileSystem(), _selectedRestoreVolume?.Label);
            BackupLogger.LogInfo(RestoreLogJobName, "Volume format completed", $"Target: {volumePath}");
        }

        private List<string>? TryReuseExistingTargetVolumes(int targetDiskNumber, IReadOnlyList<VolumeInfo> orderedVolumes)
        {
            if (orderedVolumes.Count == 0)
            {
                return null;
            }

            var diskItem = _restoreTargetItems.FirstOrDefault(item =>
                item.ItemType == DriveTreeItemType.Disk &&
                item.PartitionNumber == targetDiskNumber);
            if (diskItem == null)
            {
                return null;
            }

            var targetVolumes = diskItem.Children
                .Where(child => child.ItemType == DriveTreeItemType.Volume && !string.IsNullOrWhiteSpace(child.FullPath))
                .ToList();

            if (targetVolumes.Count == 0)
            {
                return null;
            }

            if (orderedVolumes.Count == 1 && targetVolumes.Count == 1)
            {
                long requestedSize = GetRequestedRestoreSize(orderedVolumes[0]);
                if (ShouldReuseExistingTargetVolumeLayout(requestedSize, targetVolumes[0].Size, diskItem.Size))
                {
                    string targetPath = EnsureTrailingSlash(targetVolumes[0].FullPath);
                    BackupLogger.LogInfo(RestoreLogJobName, "Reusing existing target volume layout", $"Disk: {targetDiskNumber}; Target: {targetPath}");
                    BackupLogger.LogInfo(RestoreLogJobName, "Volume format started", $"Target: {targetPath}; File system: {GetSupportedRestoreFileSystem(orderedVolumes[0])}; Label: {orderedVolumes[0].Label}");
                    FormatTargetVolume(targetPath, GetSupportedRestoreFileSystem(orderedVolumes[0]), orderedVolumes[0].Label);
                    BackupLogger.LogInfo(RestoreLogJobName, "Volume format completed", $"Target: {targetPath}");
                    return new List<string> { targetPath };
                }

                return null;
            }

            if (targetVolumes.Count != orderedVolumes.Count)
            {
                return null;
            }

            for (int i = 0; i < orderedVolumes.Count; i++)
            {
                if (!AreSizesEquivalent(GetRequestedRestoreSize(orderedVolumes[i]), targetVolumes[i].Size))
                {
                    return null;
                }
            }

            var matchedPaths = new List<string>(orderedVolumes.Count);
            for (int i = 0; i < orderedVolumes.Count; i++)
            {
                string targetPath = EnsureTrailingSlash(targetVolumes[i].FullPath);
                BackupLogger.LogInfo(RestoreLogJobName, "Reusing existing target volume layout", $"Disk: {targetDiskNumber}; Target: {targetPath}; Volume {i + 1} of {orderedVolumes.Count}");
                BackupLogger.LogInfo(RestoreLogJobName, "Volume format started", $"Target: {targetPath}; File system: {GetSupportedRestoreFileSystem(orderedVolumes[i])}; Label: {orderedVolumes[i].Label}");
                FormatTargetVolume(targetPath, GetSupportedRestoreFileSystem(orderedVolumes[i]), orderedVolumes[i].Label);
                BackupLogger.LogInfo(RestoreLogJobName, "Volume format completed", $"Target: {targetPath}");
                matchedPaths.Add(targetPath);
            }

            return matchedPaths;
        }

        private long GetTargetDiskCapacityBytes(int targetDiskNumber)
        {
            var diskItem = _restoreTargetItems.FirstOrDefault(item =>
                item.ItemType == DriveTreeItemType.Disk &&
                item.PartitionNumber == targetDiskNumber);
            if (diskItem != null && diskItem.Size > 0)
            {
                return diskItem.Size;
            }

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT Size FROM Win32_DiskDrive WHERE Index = {targetDiskNumber}");
                foreach (ManagementObject disk in searcher.Get())
                {
                    if (long.TryParse(disk["Size"]?.ToString(), out long size))
                    {
                        return size;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTargetDiskCapacityBytes: {ex.Message}");
            }

            return -1;
        }

        private static long GetRequestedRestoreSize(VolumeInfo volume)
        {
            ArgumentNullException.ThrowIfNull(volume);
            return volume.TargetSize > 0 ? volume.TargetSize : volume.Size;
        }

        private static bool AreSizesEquivalent(long expectedBytes, long actualBytes)
        {
            if (expectedBytes <= 0 || actualBytes <= 0)
            {
                return false;
            }

            long toleranceBytes = Math.Max(256L * 1024 * 1024, (long)(Math.Max(expectedBytes, actualBytes) * 0.02));
            return Math.Abs(expectedBytes - actualBytes) <= toleranceBytes;
        }

        private static string EnsureTrailingSlash(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.EndsWith("\\", StringComparison.Ordinal) ? path : path + "\\";
        }

        internal static bool ShouldReuseExistingTargetVolumeLayout(long requestedSizeBytes, long targetVolumeSizeBytes, long targetDiskSizeBytes)
        {
            return AreSizesEquivalent(requestedSizeBytes, targetVolumeSizeBytes) ||
                   AreSizesEquivalent(requestedSizeBytes, targetDiskSizeBytes);
        }

        internal static bool ShouldExpandLastPartition(long requestedTotalBytes, long targetDiskCapacityBytes)
        {
            return targetDiskCapacityBytes <= 0 || AreSizesEquivalent(requestedTotalBytes, targetDiskCapacityBytes);
        }

        private string GetSelectedRestoreFileSystem()
        {
            if (_selectedRestoreVolume != null)
            {
                return GetSupportedRestoreFileSystem(_selectedRestoreVolume);
            }

            if (_selectedRestoreDiskGroup?.Count > 0)
            {
                return GetSupportedRestoreFileSystem(_selectedRestoreDiskGroup[0]);
            }

            return "NTFS";
        }

        private static void ResizeTargetVolumeIfNeeded(string volumePath, long desiredSizeBytes)
        {
            string driveLetter = volumePath.TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(driveLetter) || driveLetter.Length < 2 || driveLetter[1] != ':')
            {
                return;
            }

            var driveInfo = new DriveInfo(driveLetter);
            if (!driveInfo.IsReady || desiredSizeBytes <= 0 || desiredSizeBytes >= driveInfo.TotalSize || AreSizesEquivalent(desiredSizeBytes, driveInfo.TotalSize))
            {
                return;
            }

            string script =
                $"$partition = Get-Partition -DriveLetter '{driveLetter[0]}' -ErrorAction Stop; " +
                $"Resize-Partition -InputObject $partition -Size {desiredSizeBytes} -ErrorAction Stop | Out-Null";

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
                throw new InvalidOperationException($"Failed to resize the selected target volume. {errors}".Trim());
            }
        }

        private static void FormatTargetVolume(string volumePath, string fileSystem, string? label)
        {
            string driveLetter = volumePath.TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(driveLetter) || driveLetter.Length < 2 || driveLetter[1] != ':')
            {
                throw new InvalidOperationException("The selected target volume does not have a valid drive letter.");
            }

            string supportedFileSystem = string.IsNullOrWhiteSpace(fileSystem) ? "NTFS" : fileSystem;
            string volumeLabel = string.IsNullOrWhiteSpace(label) ? "SSBRestore" : label.Trim();
            string script =
                $"Get-Volume -DriveLetter '{driveLetter[0]}' -ErrorAction Stop | " +
                $"Format-Volume -FileSystem '{EscapePowerShellSingleQuotedString(supportedFileSystem)}' -NewFileSystemLabel '{EscapePowerShellSingleQuotedString(volumeLabel)}' -Confirm:$false -Force -ErrorAction Stop | Out-Null";

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
                throw new InvalidOperationException($"Failed to format the selected target volume. {errors}".Trim());
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void UpdateRestoreActionState()
        {
            if (btnRestore == null)
                return;

            bool hasRestorePoint = lstRestorePoints?.SelectedItem is RestorePoint;
            bool hasTargetSelection = _restoreTargetKind switch
            {
                RestoreTargetKind.FileOrFolder => IsValidRestoreTargetPath(txtFolderRestoreDestination?.Text?.Trim() ?? string.Empty),
                RestoreTargetKind.Disk or RestoreTargetKind.Volume => !string.IsNullOrWhiteSpace(_selectedTargetPath),
                _ => true
            };

            UpdateRestoreActionButtonText();
            btnRestore.IsEnabled = hasRestorePoint && hasTargetSelection;
        }

        private MessageBoxResult ShowOwnedMessage(
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            if (Dispatcher.CheckAccess())
            {
                return defaultResult == MessageBoxResult.None
                    ? MessageBox.Show(this, messageBoxText, caption, button, icon)
                    : MessageBox.Show(this, messageBoxText, caption, button, icon, defaultResult);
            }

            return Dispatcher.Invoke(() =>
                defaultResult == MessageBoxResult.None
                    ? MessageBox.Show(this, messageBoxText, caption, button, icon)
                    : MessageBox.Show(this, messageBoxText, caption, button, icon, defaultResult));
        }

        internal static bool ShouldKeepRestoreCompletionWindowOpen(
            RestoreTargetKind restoreTargetKind,
            bool requireAlternateDestination,
            VolumeInfo? selectedRestoreVolume,
            IReadOnlyList<VolumeInfo>? selectedRestoreDiskGroup)
        {
            if (restoreTargetKind == RestoreTargetKind.FileOrFolder)
            {
                return requireAlternateDestination;
            }

            if (selectedRestoreVolume?.IsBootVolume == true)
            {
                return true;
            }

            return selectedRestoreDiskGroup?.Any(volume => volume.IsBootVolume) == true;
        }

        internal static bool ShouldEnableRestoreOptions(RestoreTargetKind restoreTargetKind)
        {
            return restoreTargetKind == RestoreTargetKind.FileOrFolder;
        }

        internal static bool ShouldEnableRestoreTargetSelection(RestoreTargetKind restoreTargetKind)
        {
            return restoreTargetKind != RestoreTargetKind.FileOrFolder &&
                   restoreTargetKind != RestoreTargetKind.HyperVVm;
        }

        internal static bool ShouldShowRestoreTargetGroup(RestoreTargetKind restoreTargetKind)
        {
            return restoreTargetKind != RestoreTargetKind.HyperVVm;
        }
        
    }

    public class RestorePoint
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string BackupType { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int ImageIndex { get; set; }
    }

    internal sealed class RestorePointArchiveImage
    {
        public int ImageIndex { get; set; }
        public DateTime? BackupStartTime { get; set; }
        public string Name { get; set; } = string.Empty;
        public string VolumeLabel { get; set; } = string.Empty;
        public string SourceVolumeMountPath { get; set; } = string.Empty;
        public ulong PartitionOffsetBytes { get; set; }
        public int VolumeIndex { get; set; }
        public bool CollapseToSingleRestorePoint { get; set; }
    }
}
