using SecureServerBackup.Windows;
using SecureServerBackup.Models;
using Xunit;

namespace SecureServerBackup.Tests;

public sealed class RestoreWindowNewTests
{
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
