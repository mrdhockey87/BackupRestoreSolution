using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SecureServerBackupCommon;
using SecureServerBackup.Windows;
using SecureServerBackup.Models;
using Xunit;

namespace SecureServerBackup.Tests;

public sealed class RestoreWindowNewTests
{
    [Fact]
    public void ShouldEnableRestoreOptions_WhenFileOrFolderRestore_ReturnsTrue()
    {
        bool result = RestoreWindowNew.ShouldEnableRestoreOptions(RestoreTargetKind.FileOrFolder);

        Assert.True(result);
    }

public sealed class BackupWindowNewTreeTests : IDisposable
{
    private readonly string _tempDirectory;

    public BackupWindowNewTreeTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SecureServerBackupTests", nameof(BackupWindowNewTreeTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void BuildDirectoryChildItems_WhenDirectoryContainsFoldersAndFiles_ReturnsBoth()
    {
        string childFolder = Path.Combine(_tempDirectory, "ChildFolder");
        string childFile = Path.Combine(_tempDirectory, "ChildFile.txt");
        Directory.CreateDirectory(childFolder);
        File.WriteAllText(childFile, "content");

        DriveTreeItem parentItem = new()
        {
            Name = "C: (Data)",
            FullPath = _tempDirectory,
            ResolvedPath = _tempDirectory,
            ItemType = DriveTreeItemType.Volume
        };

        List<DriveTreeItem> children = BackupWindowNew.BuildDirectoryChildItems(parentItem, _tempDirectory);

        Assert.Contains(children, child => child.ItemType == DriveTreeItemType.Folder && child.ResolvedPath == childFolder);
        Assert.Contains(children, child => child.ItemType == DriveTreeItemType.File && child.ResolvedPath == childFile);
    }

    [Fact]
    public void BuildDirectoryChildItems_WhenFolderContainsFiles_AddsFileNodes()
    {
        string childFile = Path.Combine(_tempDirectory, "Nested.log");
        File.WriteAllText(childFile, "content");

        DriveTreeItem parentItem = new()
        {
            Name = "Folder",
            FullPath = _tempDirectory,
            ResolvedPath = _tempDirectory,
            ItemType = DriveTreeItemType.Folder
        };

        List<DriveTreeItem> children = BackupWindowNew.BuildDirectoryChildItems(parentItem, _tempDirectory);

        DriveTreeItem fileItem = Assert.Single(children, child => child.ItemType == DriveTreeItemType.File);
        Assert.Equal("Nested.log", fileItem.Name);
        Assert.Equal(childFile, fileItem.FullPath);
        Assert.Equal(_tempDirectory, fileItem.Parent?.ResolvedPath);
    }

    [Fact]
    public void BuildDirectoryChildItems_WhenParentIsHyperVGuest_EncodesFileAndFolderPaths()
    {
        string childFolder = Path.Combine(_tempDirectory, "GuestFolder");
        string childFile = Path.Combine(_tempDirectory, "GuestFile.txt");
        Directory.CreateDirectory(childFolder);
        File.WriteAllText(childFile, "content");

        DriveTreeItem parentItem = new()
        {
            Name = "Partition 1",
            FullPath = HyperVGuestSelectionPath.Encode(HyperVGuestSelectionKind.Volume, "VmOne", @"D:\Guests\VmOne.vhdx", 1, string.Empty),
            ResolvedPath = _tempDirectory,
            VirtualMachineName = "VmOne",
            VirtualDiskPath = @"D:\Guests\VmOne.vhdx",
            PartitionNumber = 1,
            ItemType = DriveTreeItemType.HyperVVolume
        };

        List<DriveTreeItem> children = BackupWindowNew.BuildDirectoryChildItems(parentItem, _tempDirectory);

        DriveTreeItem folderItem = Assert.Single(children, child => child.ItemType == DriveTreeItemType.Folder);
        DriveTreeItem fileItem = Assert.Single(children, child => child.ItemType == DriveTreeItemType.File);

        Assert.True(HyperVGuestSelectionPath.TryParse(folderItem.FullPath, out HyperVGuestSelectionInfo? folderSelection));
        Assert.Equal(HyperVGuestSelectionKind.Folder, folderSelection!.Kind);
        Assert.Equal("GuestFolder", folderSelection.RelativePath);

        Assert.True(HyperVGuestSelectionPath.TryParse(fileItem.FullPath, out HyperVGuestSelectionInfo? fileSelection));
        Assert.Equal(HyperVGuestSelectionKind.File, fileSelection!.Kind);
        Assert.Equal("GuestFile.txt", fileSelection.RelativePath);
    }

    [Fact]
    public void IsSelectedFilesAndFoldersSelectionAllowed_WhenOnlyFolderAndFileSelections_ReturnsTrue()
    {
        List<DriveTreeItem> selectedItems =
        [
            new DriveTreeItem
            {
                Name = "Folder",
                FullPath = Path.Combine(_tempDirectory, "Folder"),
                ResolvedPath = Path.Combine(_tempDirectory, "Folder"),
                ItemType = DriveTreeItemType.Folder
            },
            new DriveTreeItem
            {
                Name = "File.txt",
                FullPath = Path.Combine(_tempDirectory, "File.txt"),
                ResolvedPath = Path.Combine(_tempDirectory, "File.txt"),
                ItemType = DriveTreeItemType.File
            }
        ];

        bool allowed = BackupWindowNew.IsSelectedFilesAndFoldersSelectionAllowed(selectedItems);

        Assert.True(allowed);
    }

    [Fact]
    public void IsSelectedFilesAndFoldersSelectionAllowed_WhenDiskSelected_ReturnsFalse()
    {
        List<DriveTreeItem> selectedItems =
        [
            new DriveTreeItem
            {
                Name = "Disk 0",
                FullPath = @"\\.\PHYSICALDRIVE0",
                ItemType = DriveTreeItemType.Disk
            }
        ];

        bool allowed = BackupWindowNew.IsSelectedFilesAndFoldersSelectionAllowed(selectedItems);

        Assert.False(allowed);
    }

    [Fact]
    public void IsSelectedFilesAndFoldersSelectionAllowed_WhenHyperVEncodedGuestFolderSelected_ReturnsTrue()
    {
        DriveTreeItem hyperVVolume = new()
        {
            Name = "Partition 1",
            FullPath = HyperVGuestSelectionPath.Encode(HyperVGuestSelectionKind.Volume, "VmOne", @"D:\Guests\VmOne.vhdx", 1, string.Empty),
            ResolvedPath = _tempDirectory,
            VirtualMachineName = "VmOne",
            VirtualDiskPath = @"D:\Guests\VmOne.vhdx",
            PartitionNumber = 1,
            ItemType = DriveTreeItemType.HyperVVolume
        };

        List<DriveTreeItem> selectedItems =
        [
            new DriveTreeItem
            {
                Name = "GuestFolder",
                FullPath = HyperVGuestSelectionPath.Encode(HyperVGuestSelectionKind.Folder, "VmOne", @"D:\Guests\VmOne.vhdx", 1, "GuestFolder"),
                ResolvedPath = Path.Combine(_tempDirectory, "GuestFolder"),
                VirtualMachineName = "VmOne",
                VirtualDiskPath = @"D:\Guests\VmOne.vhdx",
                PartitionNumber = 1,
                ItemType = DriveTreeItemType.Folder,
                Parent = hyperVVolume
            }
        ];

        bool allowed = BackupWindowNew.IsSelectedFilesAndFoldersSelectionAllowed(selectedItems);

        Assert.True(allowed);
    }

    [Fact]
    public void RequiresLazyLoad_WhenVolumeNotLoaded_ReturnsTrue()
    {
        DriveTreeItem item = new()
        {
            Name = "C: (Data)",
            FullPath = @"C:\",
            ResolvedPath = @"C:\",
            ItemType = DriveTreeItemType.Volume,
            ChildrenLoaded = false
        };

        bool requiresLazyLoad = InvokeRequiresLazyLoad(item);

        Assert.True(requiresLazyLoad);
    }

    [Fact]
    public void RequiresLazyLoad_WhenFolderWithoutResolvedPath_ReturnsFalse()
    {
        DriveTreeItem item = new()
        {
            Name = "Folder",
            FullPath = "Folder",
            ResolvedPath = string.Empty,
            ItemType = DriveTreeItemType.Folder,
            ChildrenLoaded = false
        };

        bool requiresLazyLoad = InvokeRequiresLazyLoad(item);

        Assert.False(requiresLazyLoad);
    }

    [Fact]
    public void RequiresLazyLoad_WhenChildrenAlreadyLoaded_ReturnsFalse()
    {
        DriveTreeItem item = new()
        {
            Name = "Disk.vhdx",
            FullPath = "Disk.vhdx",
            VirtualDiskPath = @"D:\Guests\Disk.vhdx",
            VirtualMachineName = "VmOne",
            ItemType = DriveTreeItemType.HyperVVirtualDisk,
            ChildrenLoaded = true
        };

        bool requiresLazyLoad = InvokeRequiresLazyLoad(item);

        Assert.False(requiresLazyLoad);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private static bool InvokeRequiresLazyLoad(DriveTreeItem item)
    {
        MethodInfo method = typeof(BackupWindowNew).GetMethod("RequiresLazyLoad", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BackupWindowNew.RequiresLazyLoad was not found.");

        return Assert.IsType<bool>(method.Invoke(null, [item]));
    }
}

    [Fact]
    public void ShouldEnableRestoreOptions_WhenDiskRestore_ReturnsFalse()
    {
        bool result = RestoreWindowNew.ShouldEnableRestoreOptions(RestoreTargetKind.Disk);

        Assert.False(result);
    }

    [Fact]
    public void ShouldEnableRestoreOptions_WhenVolumeRestore_ReturnsFalse()
    {
        bool result = RestoreWindowNew.ShouldEnableRestoreOptions(RestoreTargetKind.Volume);

        Assert.False(result);
    }

    [Fact]
    public void ShouldEnableRestoreTargetSelection_WhenFileOrFolderRestore_ReturnsFalse()
    {
        bool result = RestoreWindowNew.ShouldEnableRestoreTargetSelection(RestoreTargetKind.FileOrFolder);

        Assert.False(result);
    }

    [Fact]
    public void ShouldEnableRestoreTargetSelection_WhenVolumeRestore_ReturnsTrue()
    {
        bool result = RestoreWindowNew.ShouldEnableRestoreTargetSelection(RestoreTargetKind.Volume);

        Assert.True(result);
    }

    [Fact]
    public void DetermineRestoreTargetKind_WhenSelectedFilesBackup_ReturnsFileOrFolder()
    {
        RestoreTargetKind result = RestoreWindowNew.DetermineRestoreTargetKind(
            backupType: "Selected Files",
            filePath: @"C:\Backups\JobName.ssb",
            backupItems: [@"C:\Users\me\Documents\file.txt"]);

        Assert.Equal(RestoreTargetKind.FileOrFolder, result);
    }

    [Fact]
    public void DetermineRestoreTargetKind_WhenSelectedFilesHistoryBackupType_ReturnsFileOrFolder()
    {
        RestoreTargetKind result = RestoreWindowNew.DetermineRestoreTargetKind(
            backupType: "Selected Files History",
            filePath: @"C:\Backups\JobName_SelectedFiles_20260516.ssb",
            backupItems: [@"C:\Users\me\Documents\file.txt"]);

        Assert.Equal(RestoreTargetKind.FileOrFolder, result);
    }

    [Fact]
    public void DetermineRestoreTargetKind_WhenBackupItemsContainRegularPaths_ReturnsFileOrFolder()
    {
        RestoreTargetKind result = RestoreWindowNew.DetermineRestoreTargetKind(
            backupType: "Unknown",
            filePath: @"C:\Backups\DiskNamedJob.ssb",
            backupItems: [@"C:\Users\me\Documents", @"C:\Users\me\Documents\file.txt"]);

        Assert.Equal(RestoreTargetKind.FileOrFolder, result);
    }

    [Fact]
    public void DetermineRestoreTargetKind_WhenBackupItemsContainOnlyVolumeRoots_ReturnsVolume()
    {
        RestoreTargetKind result = RestoreWindowNew.DetermineRestoreTargetKind(
            backupType: "Unknown",
            filePath: @"C:\Backups\JobName.ssb",
            backupItems: [@"C:\", @"D:\"]);

        Assert.Equal(RestoreTargetKind.Volume, result);
    }

    [Fact]
    public void ShouldShowRestoreTargetGroup_WhenHyperVVmRestore_ReturnsFalse()
    {
        bool result = RestoreWindowNew.ShouldShowRestoreTargetGroup(RestoreTargetKind.HyperVVm);

        Assert.False(result);
    }

    [Fact]
    public void ShouldShowRestoreTargetGroup_WhenVolumeRestore_ReturnsTrue()
    {
        bool result = RestoreWindowNew.ShouldShowRestoreTargetGroup(RestoreTargetKind.Volume);

        Assert.True(result);
    }

    [Fact]
    public void ShouldReuseExistingTargetVolumeLayout_WhenRequestedMatchesVolume_ReturnsTrue()
    {
        bool result = RestoreWindowNew.ShouldReuseExistingTargetVolumeLayout(
            requestedSizeBytes: 1000,
            targetVolumeSizeBytes: 1000,
            targetDiskSizeBytes: 2000);

        Assert.True(result);
    }

    [Fact]
    public void ShouldKeepRestoreCompletionWindowOpen_WhenFileRestoreDoesNotIncludeBootedSource_ReturnsFalse()
    {
        bool result = RestoreWindowNew.ShouldKeepRestoreCompletionWindowOpen(
            RestoreTargetKind.FileOrFolder,
            requireAlternateDestination: false,
            selectedRestoreVolume: null,
            selectedRestoreDiskGroup: null);

        Assert.False(result);
    }

    [Fact]
    public void ShouldReuseExistingTargetVolumeLayout_WhenRequestedMatchesDisk_ReturnsTrue()
    {
        bool result = RestoreWindowNew.ShouldReuseExistingTargetVolumeLayout(
            requestedSizeBytes: 2000,
            targetVolumeSizeBytes: 1000,
            targetDiskSizeBytes: 2000);

        Assert.True(result);
    }

    [Fact]
    public void ShouldReuseExistingTargetVolumeLayout_WhenRequestedDoesNotMatch_ReturnsFalse()
    {
        bool result = RestoreWindowNew.ShouldReuseExistingTargetVolumeLayout(
            requestedSizeBytes: 1_000_000_000,
            targetVolumeSizeBytes: 2_000_000_000,
            targetDiskSizeBytes: 4_000_000_000);

        Assert.False(result);
    }

    [Fact]
    public void ShouldExpandLastPartition_WhenTargetCapacityUnknown_ReturnsTrue()
    {
        bool result = RestoreWindowNew.ShouldExpandLastPartition(
            requestedTotalBytes: 1500,
            targetDiskCapacityBytes: -1);

        Assert.True(result);
    }

    [Fact]
    public void ShouldExpandLastPartition_WhenRequestedMatchesTarget_ReturnsTrue()
    {
        bool result = RestoreWindowNew.ShouldExpandLastPartition(
            requestedTotalBytes: 2000,
            targetDiskCapacityBytes: 2000);

        Assert.True(result);
    }

    [Fact]
    public void ShouldExpandLastPartition_WhenRequestedDoesNotMatchTarget_ReturnsFalse()
    {
        bool result = RestoreWindowNew.ShouldExpandLastPartition(
            requestedTotalBytes: 1_000_000_000,
            targetDiskCapacityBytes: 3_000_000_000);

        Assert.False(result);
    }

    [Fact]
    public void ShouldKeepRestoreCompletionWindowOpen_WhenFileRestoreIncludesBootedSource_ReturnsTrue()
    {
        bool result = RestoreWindowNew.ShouldKeepRestoreCompletionWindowOpen(
            RestoreTargetKind.FileOrFolder,
            requireAlternateDestination: true,
            selectedRestoreVolume: null,
            selectedRestoreDiskGroup: null);

        Assert.True(result);
    }

    [Fact]
    public void ShouldKeepRestoreCompletionWindowOpen_WhenSelectedVolumeIsBootVolume_ReturnsTrue()
    {
        bool result = RestoreWindowNew.ShouldKeepRestoreCompletionWindowOpen(
            RestoreTargetKind.Volume,
            requireAlternateDestination: false,
            selectedRestoreVolume: new VolumeInfo { IsBootVolume = true },
            selectedRestoreDiskGroup: null);

        Assert.True(result);
    }

    [Fact]
    public void ShouldKeepRestoreCompletionWindowOpen_WhenDiskGroupContainsBootVolume_ReturnsTrue()
    {
        bool result = RestoreWindowNew.ShouldKeepRestoreCompletionWindowOpen(
            RestoreTargetKind.Disk,
            requireAlternateDestination: false,
            selectedRestoreVolume: null,
            selectedRestoreDiskGroup:
            [
                new VolumeInfo { IsBootVolume = false },
                new VolumeInfo { IsBootVolume = true }
            ]);

        Assert.True(result);
    }

    [Fact]
    public void ShouldKeepRestoreCompletionWindowOpen_WhenRestoreDoesNotIncludeBootVolume_ReturnsFalse()
    {
        bool result = RestoreWindowNew.ShouldKeepRestoreCompletionWindowOpen(
            RestoreTargetKind.Volume,
            requireAlternateDestination: false,
            selectedRestoreVolume: new VolumeInfo { IsBootVolume = false },
            selectedRestoreDiskGroup: null);

        Assert.False(result);
    }

    [Fact]
    public void ShouldKeepRestoreCompletionWindowOpen_WhenDiskGroupDoesNotIncludeBootVolume_ReturnsFalse()
    {
        bool result = RestoreWindowNew.ShouldKeepRestoreCompletionWindowOpen(
            RestoreTargetKind.Disk,
            requireAlternateDestination: false,
            selectedRestoreVolume: null,
            selectedRestoreDiskGroup:
            [
                new VolumeInfo { IsBootVolume = false },
                new VolumeInfo { IsBootVolume = false }
            ]);

        Assert.False(result);
    }

    [Fact]
    public void CreateRestorePointsForBackupFile_WhenArchiveContainsTwoVolumesWithRestoreMetadata_ReturnsSinglePoint()
    {
        DateTime timestamp = new(2026, 5, 18, 10, 30, 0);
        IReadOnlyList<RestorePointArchiveImage> archiveImages =
        [
            new RestorePointArchiveImage
            {
                ImageIndex = 2,
                VolumeLabel = "Data",
                PartitionOffsetBytes = 2048,
                VolumeIndex = 2,
                CollapseToSingleRestorePoint = true
            },
            new RestorePointArchiveImage
            {
                ImageIndex = 1,
                VolumeLabel = "System",
                PartitionOffsetBytes = 1024,
                VolumeIndex = 1,
                CollapseToSingleRestorePoint = true
            }
        ];

        IReadOnlyList<RestorePoint> restorePoints = RestoreWindowNew.CreateRestorePointsForBackupFile(
            @"C:\Backups\WDrive_incremental.ssb",
            "Incremental",
            timestamp,
            1,
            archiveImages);

        RestorePoint restorePoint = Assert.Single(restorePoints);
        Assert.Equal("Point 1: Incremental Backup", restorePoint.DisplayName);
        Assert.Equal(0, restorePoint.ImageIndex);
    }

    [Fact]
    public void CreateRestorePointsForBackupFile_WhenArchiveContainsTwoVolumesWithoutRestoreMetadata_ReturnsOnePointPerVolume()
    {
        DateTime timestamp = new(2026, 5, 18, 10, 30, 0);
        IReadOnlyList<RestorePointArchiveImage> archiveImages =
        [
            new RestorePointArchiveImage
            {
                ImageIndex = 2,
                VolumeLabel = "Data",
                PartitionOffsetBytes = 2048,
                VolumeIndex = 2
            },
            new RestorePointArchiveImage
            {
                ImageIndex = 1,
                VolumeLabel = "System",
                PartitionOffsetBytes = 1024,
                VolumeIndex = 1
            }
        ];

        IReadOnlyList<RestorePoint> restorePoints = RestoreWindowNew.CreateRestorePointsForBackupFile(
            @"C:\Backups\WDrive_incremental.ssb",
            "Incremental",
            timestamp,
            1,
            archiveImages);

        Assert.Equal(2, restorePoints.Count);
        Assert.Equal("Point 1: Incremental Backup - System", restorePoints[0].DisplayName);
        Assert.Equal(1, restorePoints[0].ImageIndex);
        Assert.Equal("Point 2: Incremental Backup - Data", restorePoints[1].DisplayName);
        Assert.Equal(2, restorePoints[1].ImageIndex);
    }

    [Fact]
    public void CreateRestorePointsForBackupFile_WhenArchiveImageMetadataIsUnavailable_ReturnsSinglePoint()
    {
        DateTime timestamp = new(2026, 5, 18, 10, 30, 0);

        IReadOnlyList<RestorePoint> restorePoints = RestoreWindowNew.CreateRestorePointsForBackupFile(
            @"C:\Backups\WDrive_incremental.ssb",
            "Incremental",
            timestamp,
            3,
            Array.Empty<RestorePointArchiveImage>());

        RestorePoint restorePoint = Assert.Single(restorePoints);
        Assert.Equal("Point 3: Incremental Backup", restorePoint.DisplayName);
        Assert.Equal(0, restorePoint.ImageIndex);
    }

    [Fact]
    public void GetRestorePointTimestamp_WhenArchiveMetadataContainsBackupStartTime_ReturnsMetadataTimestamp()
    {
        DateTime expectedTimestamp = new(2026, 5, 18, 9, 15, 0);
        IReadOnlyList<RestorePointArchiveImage> archiveImages =
        [
            new RestorePointArchiveImage
            {
                ImageIndex = 1,
                BackupStartTime = expectedTimestamp.AddMinutes(2),
                CollapseToSingleRestorePoint = true
            },
            new RestorePointArchiveImage
            {
                ImageIndex = 2,
                BackupStartTime = expectedTimestamp,
                CollapseToSingleRestorePoint = true
            }
        ];

        DateTime actualTimestamp = InvokeGetRestorePointTimestamp(@"C:\Backups\DiskBackup.ssb", archiveImages);

        Assert.Equal(expectedTimestamp, actualTimestamp);
    }

    [Fact]
    public void GetRestorePointTimestamp_WhenFileBackupMetadataContainsBackupStartTime_ReturnsMetadataTimestamp()
    {
        string backupDirectory = Path.Combine(Path.GetTempPath(), nameof(RestoreWindowNewTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);

        try
        {
            DateTime expectedTimestamp = new(2026, 5, 18, 8, 45, 0);
            File.WriteAllText(
                Path.Combine(backupDirectory, "backup_metadata.dat"),
                $"#BACKUP_START_TIME|{expectedTimestamp:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}sample.txt|1|2{Environment.NewLine}");

            DateTime actualTimestamp = InvokeGetRestorePointTimestamp(backupDirectory, Array.Empty<RestorePointArchiveImage>());

            Assert.Equal(expectedTimestamp, actualTimestamp);
        }
        finally
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }

    private static DateTime InvokeGetRestorePointTimestamp(string backupPath, IReadOnlyList<RestorePointArchiveImage> archiveImages)
    {
        MethodInfo method = typeof(RestoreWindowNew).GetMethod("GetRestorePointTimestamp", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("GetRestorePointTimestamp method was not found.");

        return Assert.IsType<DateTime>(method.Invoke(null, [backupPath, archiveImages]));
    }
}
