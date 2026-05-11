using System;
using System.IO;
using SecureServerBackup.Services;
using SecureServerBackup.Windows;
using Xunit;

namespace SecureServerBackup.Tests;

public sealed class PreparedBackupFileTests : IDisposable
{
    private readonly string _tempDirectory;

    public PreparedBackupFileTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SecureServerBackupTests", nameof(PreparedBackupFileTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Dispose_WhenTemporaryFile_RemovesWorkingPath()
    {
        string path = Path.Combine(_tempDirectory, "temp.ssb");
        File.WriteAllText(path, "content");

        PreparedBackupFile prepared = new()
        {
            OriginalPath = path,
            WorkingPath = path,
            IsTemporary = true
        };

        prepared.Dispose();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void HasBackupArchive_WhenArchiveExists_ReturnsTrue()
    {
        string path = Path.Combine(_tempDirectory, "existing.ssb");
        File.WriteAllText(path, "content");

        bool result = typeof(BackupWindowNew)
            .GetMethod("HasBackupArchive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [ _tempDirectory, "existing" ]) as bool? ?? false;

        Assert.True(result);
    }

    [Fact]
    public void HasBackupArchive_WhenArchiveIsMissing_ReturnsFalse()
    {
        bool result = typeof(BackupWindowNew)
            .GetMethod("HasBackupArchive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [ _tempDirectory, "missing" ]) as bool? ?? false;

        Assert.False(result);
    }

    [Fact]
    public void Dispose_WhenNotTemporary_LeavesWorkingPath()
    {
        string path = Path.Combine(_tempDirectory, "temp.ssb");
        File.WriteAllText(path, "content");

        PreparedBackupFile prepared = new()
        {
            OriginalPath = path,
            WorkingPath = path,
            IsTemporary = false
        };

        prepared.Dispose();

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        string path = Path.Combine(_tempDirectory, "temp.ssb");
        File.WriteAllText(path, "content");

        PreparedBackupFile prepared = new()
        {
            OriginalPath = path,
            WorkingPath = path,
            IsTemporary = true
        };

        prepared.Dispose();
        prepared.Dispose();

        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
