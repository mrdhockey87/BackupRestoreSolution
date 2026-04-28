using System;
using SecureServerBackupCommon;
using Xunit;

namespace SecureServerBackupCommon.Tests;

public sealed class BackupJobTests
{
    [Fact]
    public void Constructor_InitializesCollectionsAndDefaults()
    {
        BackupJob job = new();

        Assert.NotNull(job.SourcePaths);
        Assert.NotNull(job.HyperVMachines);
        Assert.NotNull(job.UserExclusions);
        Assert.Equal(1, job.RetainFullBackupCount);
        Assert.Equal(0, job.ConsecutiveFailures);
        Assert.False(job.IsCurrentlyRunning);
        Assert.False(job.ForceFullBackupOnNextRun);
        Assert.True(job.UseCompression);
        Assert.False(job.EncryptBackup);
    }
}
