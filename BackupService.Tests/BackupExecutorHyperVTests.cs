using System;
using System.Collections.Generic;
using System.IO;
using SecureServerBackupCommon;
using SecureServerBackupService;
using Xunit;

namespace SecureServerBackupService.Tests;

public sealed class BackupExecutorHyperVTests
{
    [Fact]
    public void NormalizeHyperVVirtualMachineName_WhenStateSuffixExists_RemovesSuffix()
    {
        string result = BackupExecutor.NormalizeHyperVVirtualMachineName("Win10OEM (Running)");

        Assert.Equal("Win10OEM", result);
    }

    [Fact]
    public void NormalizeHyperVVirtualMachineName_WhenStateSuffixMissing_ReturnsTrimmedName()
    {
        string result = BackupExecutor.NormalizeHyperVVirtualMachineName("  Win10OEM  ");

        Assert.Equal("Win10OEM", result);
    }

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
    public void GetHyperVBackupMode_WhenDifferentialWithoutAnyBackup_ReturnsFull()
    {
        string mode = BackupExecutor.GetHyperVBackupMode(BackupType.Differential, hasExistingFullBackup: false, hasAnyExistingBackup: false);

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

    [Fact]
    public void HasAnyHyperVBackupPoint_WhenMatchingDirectoryExists_ReturnsTrue()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "Full_20260430_125628.ssb"));

            bool result = BackupExecutor.HasAnyHyperVBackupPoint(tempRoot);

            Assert.True(result);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void HasFullHyperVBackupPoint_WhenArchiveFileExists_ReturnsFalse()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string archivePath = Path.Combine(tempRoot, "Win10OEM.ssb");
            File.WriteAllText(archivePath, string.Empty);

            bool result = BackupExecutor.HasFullHyperVBackupPoint(archivePath);

            Assert.False(result);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void HasAnyHyperVBackupPoint_WhenArchiveFileExists_ReturnsTrue()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "Win10OEM.ssb"), string.Empty);

            bool result = BackupExecutor.HasAnyHyperVBackupPoint(Path.Combine(tempRoot, "Win10OEM.ssb"));

            Assert.True(result);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void HasFullHyperVBackupPoint_WhenOnlyIncrementalDirectoryExists_ReturnsFalse()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "Incremental_20260430_125628.ssb"));

            bool result = BackupExecutor.HasFullHyperVBackupPoint(tempRoot);

            Assert.False(result);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void HasFullHyperVBackupPoint_WhenLegacyFullBackupPointDirectoryPassed_ReturnsTrue()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string fullBackupPointPath = Path.Combine(tempRoot, "Full_20260430_142402.ssb");
            Directory.CreateDirectory(fullBackupPointPath);

            bool result = BackupExecutor.HasFullHyperVBackupPoint(fullBackupPointPath);

            Assert.True(result);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShouldReplaceExistingFullFileArchive_WhenSelectedFilesBackupUsesJobNameArchive_ReturnsTrue()
    {
        var job = new BackupJob
        {
            Type = BackupType.SelectedFilesAndFolders,
            Target = BackupTarget.FilesAndFolders
        };

        bool result = BackupExecutor.ShouldReplaceExistingFullFileArchive(job, isHyperVBackup: false, isSelectedFilesHistoryBackup: true);

        Assert.True(result);
    }

    [Fact]
    public void ShouldReplaceExistingFullFileArchive_WhenFullFilesBackup_ReturnsTrue()
    {
        var job = new BackupJob
        {
            Type = BackupType.Full,
            Target = BackupTarget.FilesAndFolders
        };

        bool result = BackupExecutor.ShouldReplaceExistingFullFileArchive(job, isHyperVBackup: false, isSelectedFilesHistoryBackup: false);

        Assert.True(result);
    }

    [Fact]
    public void GetSelectedFilesHistoryArchivePath_WhenSelectedFilesBackup_UsesJobNameArchive()
    {
        var job = new BackupJob
        {
            Name = "Files-Folder",
            DestinationPath = @"X:\BackupApplications\Files-Folders"
        };

        string archivePath = BackupExecutor.GetSelectedFilesHistoryArchivePathForTest(job, new DateTime(2026, 5, 16, 14, 35, 15));

        Assert.Equal(@"X:\BackupApplications\Files-Folders\Files-Folder.ssb", archivePath);
    }

    [Fact]
    public void BuildFileBackupBatches_WhenSelectionsShareParentFolder_GroupsUnderOneRoot()
    {
        string[] sourcePaths =
        [
            @"C:\Data\Docs\Quarterly\report.docx",
            @"C:\Data\Docs\Quarterly\notes.txt"
        ];

        IReadOnlyList<BackupExecutor.FileBackupBatch> batches = BackupExecutor.BuildFileBackupBatches(sourcePaths);

        BackupExecutor.FileBackupBatch batch = Assert.Single(batches);
        Assert.Equal(@"C:\Data\Docs\Quarterly", batch.SourceRoot);
        Assert.Equal(2, batch.SelectedPaths.Count);
    }

    [Fact]
    public void BuildFileBackupBatches_WhenSelectionsSpanFolders_KeepsSeparateRoots()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string firstFolder = Path.Combine(tempRoot, "Docs", "Quarterly");
            string secondFolder = Path.Combine(tempRoot, "Photos", "Vacation");
            Directory.CreateDirectory(firstFolder);
            Directory.CreateDirectory(secondFolder);

            string[] sourcePaths =
            [
                Path.Combine(firstFolder, "report.docx"),
                secondFolder
            ];

            IReadOnlyList<BackupExecutor.FileBackupBatch> batches = BackupExecutor.BuildFileBackupBatches(sourcePaths);

            Assert.Equal(2, batches.Count);
            Assert.Contains(batches, batch => batch.SourceRoot == firstFolder);
            Assert.Contains(batches, batch => batch.SourceRoot == secondFolder);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
