using System;
using System.IO;
using SecureServerBackup.Windows;
using Xunit;

namespace SecureServerBackup.Tests;

public sealed class HyperVRestorePointHelperTests : IDisposable
{
    private readonly string _tempDirectory;

    public HyperVRestorePointHelperTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SecureServerBackupTests", nameof(HyperVRestorePointHelperTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void IsHyperVBackupPoint_WhenMetadataExists_ReturnsTrue()
    {
        string backupPoint = CreateBackupPointDirectory();

        bool result = RestoreWindowNew.HyperVRestorePointHelper.IsHyperVBackupPoint(backupPoint);

        Assert.True(result);
    }

    [Fact]
    public void ResolveExportPath_WhenMetadataContainsExistingPath_ReturnsMetadataPath()
    {
        string backupPoint = CreateBackupPointDirectory(createExportFolder: false);
        string exportPath = Path.Combine(_tempDirectory, "ResolvedExport");
        Directory.CreateDirectory(exportPath);
        File.WriteAllText(Path.Combine(backupPoint, "hyperv_backup_info.txt"), $"ExportPath={exportPath}{Environment.NewLine}");

        string? result = RestoreWindowNew.HyperVRestorePointHelper.ResolveExportPath(backupPoint);

        Assert.Equal(exportPath, result);
    }

    [Fact]
    public void FindPrimaryVirtualDisk_WhenMultipleDisksExist_ReturnsLargestDisk()
    {
        string backupPoint = CreateBackupPointDirectory();
        string exportPath = Path.Combine(backupPoint, "Export");
        string smallerDisk = Path.Combine(exportPath, "small.vhdx");
        string largerDisk = Path.Combine(exportPath, "large.vhdx");
        File.WriteAllBytes(smallerDisk, new byte[32]);
        File.WriteAllBytes(largerDisk, new byte[128]);

        string? result = RestoreWindowNew.HyperVRestorePointHelper.FindPrimaryVirtualDisk(backupPoint);

        Assert.Equal(largerDisk, result);
    }

    [Fact]
    public void FindPrimaryVirtualDisk_WhenMetadataExportPathExists_ReturnsLargestDiskFromResolvedExport()
    {
        string backupPoint = CreateBackupPointDirectory(createExportFolder: false);
        string exportPath = Path.Combine(_tempDirectory, "ResolvedExportPath");
        Directory.CreateDirectory(exportPath);
        File.WriteAllText(Path.Combine(backupPoint, "hyperv_backup_info.txt"), $"ExportPath={exportPath}{Environment.NewLine}");

        string smallerDisk = Path.Combine(exportPath, "guest-small.vhdx");
        string largerDisk = Path.Combine(exportPath, "guest-large.vhdx");
        File.WriteAllBytes(smallerDisk, new byte[8]);
        File.WriteAllBytes(largerDisk, new byte[64]);

        string? result = RestoreWindowNew.HyperVRestorePointHelper.FindPrimaryVirtualDisk(backupPoint);

        Assert.Equal(largerDisk, result);
    }

    [Fact]
    public void ResolveVmName_WhenNoConfigExists_ReturnsBackupPointName()
    {
        string backupPoint = CreateBackupPointDirectory();

        string result = RestoreWindowNew.HyperVRestorePointHelper.ResolveVmName(backupPoint);

        Assert.Equal("Full_20260429_120000", result);
    }

    [Fact]
    public void ResolveVmName_WhenMetadataContainsVmName_ReturnsMetadataVmName()
    {
        string backupPoint = CreateBackupPointDirectory();
        File.WriteAllText(
            Path.Combine(backupPoint, "hyperv_backup_info.txt"),
            $"Type=Full{Environment.NewLine}PointId=20260429_120000{Environment.NewLine}VmName=Win10OEM{Environment.NewLine}");

        string result = RestoreWindowNew.HyperVRestorePointHelper.ResolveVmName(backupPoint);

        Assert.Equal("Win10OEM", result);
    }

    [Fact]
    public void IsHyperVBackupPoint_WhenArchiveFileExists_ReturnsFalse()
    {
        string backupFile = Path.Combine(_tempDirectory, "Win10OEM.ssb");
        File.WriteAllText(backupFile, string.Empty);

        bool result = RestoreWindowNew.HyperVRestorePointHelper.IsHyperVBackupPoint(backupFile);

        Assert.False(result);
    }

    private string CreateBackupPointDirectory(bool createExportFolder = true)
    {
        string backupPoint = Path.Combine(_tempDirectory, "Full_20260429_120000.ssb");
        Directory.CreateDirectory(backupPoint);
        File.WriteAllText(Path.Combine(backupPoint, "hyperv_backup_info.txt"), "Type=Full" + Environment.NewLine + "PointId=20260429_120000");

        if (createExportFolder)
        {
            Directory.CreateDirectory(Path.Combine(backupPoint, "Export"));
        }

        return backupPoint;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
