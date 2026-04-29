using SecureServerBackupCommon;
using SecureServerBackupService;
using Xunit;

namespace SecureServerBackupService.Tests;

public sealed class BackupExecutorHyperVTests
{
    [Fact]
    public void GetHyperVBackupMode_WhenIncrementalAndExistingBackup_ReturnsIncremental()
    {
        string mode = BackupExecutor.GetHyperVBackupMode(BackupType.Incremental, hasExistingFullBackup: true, hasAnyExistingBackup: true);

        Assert.Equal("Incremental", mode);
    }

    [Fact]
    public void GetHyperVBackupMode_WhenIncrementalWithoutExistingBackup_ReturnsFull()
    {
        string mode = BackupExecutor.GetHyperVBackupMode(BackupType.Incremental, hasExistingFullBackup: false, hasAnyExistingBackup: false);

        Assert.Equal("Full", mode);
    }

    [Fact]
    public void GetHyperVBackupMode_WhenDifferentialWithFullBackup_ReturnsDifferential()
    {
        string mode = BackupExecutor.GetHyperVBackupMode(BackupType.Differential, hasExistingFullBackup: true, hasAnyExistingBackup: true);

        Assert.Equal("Differential", mode);
    }

    [Fact]
    public void GetHyperVBackupMode_WhenDifferentialWithoutFullBackup_ReturnsFull()
    {
        string mode = BackupExecutor.GetHyperVBackupMode(BackupType.Differential, hasExistingFullBackup: false, hasAnyExistingBackup: true);

        Assert.Equal("Full", mode);
    }

    [Fact]
    public void GetHyperVBackupMode_WhenFullRequested_ReturnsFull()
    {
        string mode = BackupExecutor.GetHyperVBackupMode(BackupType.Full, hasExistingFullBackup: true, hasAnyExistingBackup: true);

        Assert.Equal("Full", mode);
    }
}
