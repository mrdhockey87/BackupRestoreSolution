using SecureServerBackup.Windows;
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
}
