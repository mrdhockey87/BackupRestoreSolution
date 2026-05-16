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
}
