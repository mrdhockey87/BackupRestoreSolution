using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
    public void GetVirtualDiskClonePath_WhenCloneJobConfigured_UsesJobNamedVhdx()
    {
        var job = new BackupJob
        {
            Name = "System Backup Job",
            DestinationPath = @"X:\HyperVClones"
        };

        string clonePath = job.GetVirtualDiskClonePath();

        Assert.Equal(@"X:\HyperVClones\System Backup Job.vhdx", clonePath);
    }

    [Fact]
    public void ShouldCloneToVirtualDiskAsDisk_WhenTargetIsDisk_ReturnsTrue()
    {
        var job = new BackupJob
        {
            Target = BackupTarget.Disk,
            SourcePaths = [@"\\.\PHYSICALDRIVE2"]
        };

        bool result = job.ShouldCloneToVirtualDiskAsDisk();

        Assert.True(result);
    }

    [Fact]
    public void ShouldCloneToVirtualDiskAsDisk_WhenTargetIsSingleVolume_ReturnsFalse()
    {
        var job = new BackupJob
        {
            Target = BackupTarget.Volume,
            SourcePaths = [@"\\?\Volume{1234}\"]
        };

        bool result = job.ShouldCloneToVirtualDiskAsDisk();

        Assert.False(result);
    }

    [Fact]
    public void ShouldCloneToVirtualDiskAsDisk_WhenMultipleVolumesSelected_ReturnsTrue()
    {
        var job = new BackupJob
        {
            Target = BackupTarget.Volume,
            SourcePaths = [@"\\?\Volume{1111}\", @"\\?\Volume{2222}\"]
        };

        bool result = job.ShouldCloneToVirtualDiskAsDisk();

        Assert.True(result);
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
    public void BuildSelectedFileBackupBatches_WhenSelectionsShareVolumeRoot_UsesPersistedRoot()
    {
        IReadOnlyList<BackupExecutor.FileBackupBatch> batches = InvokeBuildSelectedFileBackupBatches(
            [@"C:\"],
            [
                @"C:\Users\Mark\Documents\Budget.xlsx",
                @"C:\Users\Mark\Pictures\Vacation\img1.jpg"
            ]);

        BackupExecutor.FileBackupBatch batch = Assert.Single(batches);
        Assert.Equal(@"C:\", batch.SourceRoot);
        Assert.Equal(2, batch.SelectedPaths.Count);
    }

    [Fact]
    public void BuildSelectedFileBackupBatches_WhenPersistedRootsMissing_ReturnsNoBatches()
    {
        IReadOnlyList<BackupExecutor.FileBackupBatch> batches = InvokeBuildSelectedFileBackupBatches(
            Array.Empty<string>(),
            [@"C:\Users\Mark\Documents\Budget.xlsx"]);

        Assert.Empty(batches);
    }

    [Fact]
    public void BuildSelectedFileBackupBatches_WhenSelectionDoesNotMapToPersistedRoot_ReturnsNoBatches()
    {
        IReadOnlyList<BackupExecutor.FileBackupBatch> batches = InvokeBuildSelectedFileBackupBatches(
            [@"D:\"],
            [@"C:\Users\Mark\Documents\Budget.xlsx"]);

        Assert.Empty(batches);
    }

    [Fact]
    public void BuildSelectedFileBackupBatches_WhenHyperVGuestSelectionsShareEncodedVolumeRoot_UsesPersistedGuestRoot()
    {
        string guestRoot = HyperVGuestSelectionPath.Encode(
            HyperVGuestSelectionKind.Volume,
            "VmOne",
            @"D:\Guests\VmOne.vhdx",
            2,
            string.Empty);
        string guestFile = HyperVGuestSelectionPath.Encode(
            HyperVGuestSelectionKind.File,
            "VmOne",
            @"D:\Guests\VmOne.vhdx",
            2,
            @"Users\Mark\Documents\Budget.xlsx");

        IReadOnlyList<BackupExecutor.FileBackupBatch> batches = InvokeBuildSelectedFileBackupBatches(
            [guestRoot],
            [guestFile]);

        BackupExecutor.FileBackupBatch batch = Assert.Single(batches);
        Assert.Equal(guestRoot, batch.SourceRoot);
        Assert.Single(batch.SelectedPaths);
        Assert.Equal(guestFile, batch.SelectedPaths[0]);
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

    [Fact]
    public void ResolveSelectedFilePaths_WhenSomeSelectionsMissing_ReturnsPresentAndMissingPaths()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string existingFolder = Path.Combine(tempRoot, "Docs");
            Directory.CreateDirectory(existingFolder);
            string existingFile = Path.Combine(existingFolder, "report.docx");
            File.WriteAllText(existingFile, "data");
            string missingFile = Path.Combine(existingFolder, "missing.txt");

            object resolution = InvokePrivateStaticMethod(
                nameof(BackupExecutor),
                "ResolveSelectedFilePathsForExecution",
                [new[] { existingFile, missingFile }])!;

            IReadOnlyList<string> resolvedPaths = GetResolutionPaths(resolution, "ResolvedPaths");
            IReadOnlyList<string> missingPaths = GetResolutionPaths(resolution, "MissingPaths");

            Assert.Single(resolvedPaths);
            Assert.Contains(existingFile, resolvedPaths, StringComparer.OrdinalIgnoreCase);
            Assert.Single(missingPaths);
            Assert.Contains(missingFile, missingPaths, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void HasAnyRuntimeSelections_WhenSelectedFilesJobHasNoPersistedSelections_ReturnsFalse()
    {
        var job = new BackupJob
        {
            Type = BackupType.SelectedFilesAndFolders,
            Target = BackupTarget.FilesAndFolders
        };

        bool hasSelections = (bool)InvokePrivateStaticMethod(
            nameof(BackupExecutor),
            "HasAnyRuntimeSelections",
            [job, Array.Empty<string>()])!;

        Assert.False(hasSelections);
    }

    [Fact]
    public void ShouldUseSelectedFileBatching_WhenDiskJobUsesSelectedFilesType_ReturnsFalse()
    {
        var job = new BackupJob
        {
            Type = BackupType.SelectedFilesAndFolders,
            Target = BackupTarget.Disk,
            SourcePaths = [@"\\.\PHYSICALDRIVE5"],
            SelectedFilesSourceRoots = [@"\\.\PHYSICALDRIVE5"]
        };

        bool shouldUseSelectedFileBatching = (bool)InvokePrivateStaticMethod(
            nameof(BackupExecutor),
            "ShouldUseSelectedFileBatching",
            [job])!;

        Assert.False(shouldUseSelectedFileBatching);
    }

    [Fact]
    public void ShouldUseSelectedFileBatching_WhenVolumeJobUsesSelectedFilesType_ReturnsFalse()
    {
        var job = new BackupJob
        {
            Type = BackupType.SelectedFilesAndFolders,
            Target = BackupTarget.Volume,
            SourcePaths = [@"\\?\Volume{12345678-1234-1234-1234-123456789abc}\"],
            SelectedFilesSourceRoots = [@"\\?\Volume{12345678-1234-1234-1234-123456789abc}\"]
        };

        bool shouldUseSelectedFileBatching = (bool)InvokePrivateStaticMethod(
            nameof(BackupExecutor),
            "ShouldUseSelectedFileBatching",
            [job])!;

        Assert.False(shouldUseSelectedFileBatching);
    }

    [Fact]
    public void ShouldUseSelectedFileBatching_WhenSelectedFilesJobUsesFilesAndFoldersTarget_ReturnsTrue()
    {
        var job = new BackupJob
        {
            Type = BackupType.SelectedFilesAndFolders,
            Target = BackupTarget.FilesAndFolders,
            SourcePaths = [@"C:\Users\Mark\Documents\Budget.xlsx"],
            SelectedFilesSourceRoots = [@"C:\"]
        };

        bool shouldUseSelectedFileBatching = (bool)InvokePrivateStaticMethod(
            nameof(BackupExecutor),
            "ShouldUseSelectedFileBatching",
            [job])!;

        Assert.True(shouldUseSelectedFileBatching);
    }

    [Fact]
    public void ResolveSourcePaths_WhenDiskJobUsesSelectedFilesType_ReturnsOriginalSourcePaths()
    {
        var job = new BackupJob
        {
            Name = Guid.NewGuid().ToString("N"),
            Type = BackupType.SelectedFilesAndFolders,
            Target = BackupTarget.Disk,
            SourcePaths = [@"\\.\PHYSICALDRIVE5"]
        };

        IReadOnlyList<string> sourcePaths = (IReadOnlyList<string>)InvokePrivateStaticMethod(
            nameof(BackupExecutor),
            "ResolveSourcePaths",
            [job])!;

        string sourcePath = Assert.Single(sourcePaths);
        Assert.Equal(@"\\.\PHYSICALDRIVE5", sourcePath);
    }

    [Fact]
    public void ResolveSourcePaths_WhenVolumeJobUsesSelectedFilesType_ReturnsOriginalSourcePaths()
    {
        var job = new BackupJob
        {
            Name = Guid.NewGuid().ToString("N"),
            Type = BackupType.SelectedFilesAndFolders,
            Target = BackupTarget.Volume,
            SourcePaths = [@"\\?\Volume{12345678-1234-1234-1234-123456789abc}\"]
        };

        IReadOnlyList<string> sourcePaths = (IReadOnlyList<string>)InvokePrivateStaticMethod(
            nameof(BackupExecutor),
            "ResolveSourcePaths",
            [job])!;

        string sourcePath = Assert.Single(sourcePaths);
        Assert.Equal(@"\\?\Volume{12345678-1234-1234-1234-123456789abc}\", sourcePath);
    }

    [Fact]
    public void HasAnyRuntimeSelections_WhenHyperVJobHasNoSelectedVms_ReturnsFalse()
    {
        var job = new BackupJob
        {
            Target = BackupTarget.HyperV,
            IsHyperVBackup = true
        };

        bool hasSelections = (bool)InvokePrivateStaticMethod(
            nameof(BackupExecutor),
            "HasAnyRuntimeSelections",
            [job, Array.Empty<string>()])!;

        Assert.False(hasSelections);
    }

    private static object? InvokePrivateStaticMethod(string typeName, string methodName, object?[] args)
    {
        Type type = typeof(BackupExecutor);
        MethodInfo method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on '{typeName}'.");

        return method.Invoke(null, args);
    }

    private static IReadOnlyList<BackupExecutor.FileBackupBatch> InvokeBuildSelectedFileBackupBatches(
        IEnumerable<string> sourceRoots,
        IEnumerable<string> selectedPaths)
    {
        return (IReadOnlyList<BackupExecutor.FileBackupBatch>)(InvokePrivateStaticMethod(
            nameof(BackupExecutor),
            "BuildSelectedFileBackupBatches",
            [sourceRoots, selectedPaths])
            ?? throw new InvalidOperationException("BuildSelectedFileBackupBatches returned null."));
    }

    private static IReadOnlyList<string> GetResolutionPaths(object resolution, string propertyName)
    {
        PropertyInfo property = resolution.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' was not found on '{resolution.GetType().Name}'.");

        return (IReadOnlyList<string>)(property.GetValue(resolution)
            ?? throw new InvalidOperationException($"Property '{propertyName}' returned null."));
    }
}
