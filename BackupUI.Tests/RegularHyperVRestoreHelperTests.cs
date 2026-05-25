using System;
using System.Linq;
using System.IO;
using SecureServerBackupCommon;
using SecureServerBackup.Windows;
using Xunit;

namespace SecureServerBackup.Tests;

public sealed class RegularHyperVRestoreHelperTests
{
    [Fact]
    public void ParseVirtualDiskEnumeration_WhenLinesContainValidEntries_ReturnsNormalizedDiskInfos()
    {
        string output = "VmOne\tVmOne (Running)\t\"C:\\HyperV\\Disk1.vhdx\"\nVmTwo\tVmTwo\tD:\\VMs\\Disk2.vhdx\n";

        var result = BackupWindowNew.HyperVBackupTreeHelper.ParseVirtualDiskEnumeration(output);

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal("VmOne", first.VirtualMachineName);
                Assert.Equal("VmOne (Running)", first.VirtualMachineDisplayName);
                Assert.Equal(@"C:\HyperV\Disk1.vhdx", first.VirtualDiskPath);
            },
            second =>
            {
                Assert.Equal("VmTwo", second.VirtualMachineName);
                Assert.Equal("VmTwo", second.VirtualMachineDisplayName);
                Assert.Equal(@"D:\VMs\Disk2.vhdx", second.VirtualDiskPath);
            });
    }

    [Fact]
    public void ParseVirtualDiskEnumeration_WhenLinesAreInvalid_SkipsThem()
    {
        string output = "VmOne\tOnlyTwoColumns\n\t\t\nVmTwo\tVmTwo\t \n";

        var result = BackupWindowNew.HyperVBackupTreeHelper.ParseVirtualDiskEnumeration(output);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("Microsoft:Hyper-V:Virtual Hard Disk")]
    [InlineData("Virtual Hard Disk")]
    [InlineData("Microsoft:Hyper-V:Virtual Hard Disk Drive")]
    public void IsVirtualDiskResource_WhenSubtypeMatches_ReturnsTrue(string resourceSubType)
    {
        bool result = BackupWindowNew.HyperVBackupTreeHelper.IsVirtualDiskResource(resourceSubType);

        Assert.True(result);
    }

    [Fact]
    public void GetHostResources_WhenArrayContainsValues_ReturnsNonEmptyEntries()
    {
        object input = new object?[] { @"C:\HyperV\Disk1.vhdx", null, " ", @"D:\VMs\Disk2.vhdx" };

        var result = BackupWindowNew.HyperVBackupTreeHelper.GetHostResources(input).ToArray();

        Assert.Equal(new[] { @"C:\HyperV\Disk1.vhdx", @"D:\VMs\Disk2.vhdx" }, result);
    }

    [Theory]
    [InlineData(2, "VmOne (Running)")]
    [InlineData(3, "VmOne (Off)")]
    [InlineData(32768, "VmOne (Paused)")]
    [InlineData(32769, "VmOne (Saved)")]
    [InlineData(42, "VmOne (Unknown State)")]
    public void BuildVmDisplayName_WhenStateProvided_ReturnsExpectedSuffix(int state, string expected)
    {
        string result = BackupWindowNew.HyperVBackupTreeHelper.BuildVmDisplayName("VmOne", state);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void SelectMountableVirtualDiskPath_WhenChainContainsParents_ReturnsDeepestParent()
    {
        string result = BackupWindowNew.HyperVBackupTreeHelper.SelectMountableVirtualDiskPath(
            @"D:\Vm\Active.avhdx",
            new[]
            {
                @"D:\Vm\Active.avhdx",
                @"D:\Vm\Previous.avhdx",
                @"D:\Vm\Base.vhdx"
            });

        Assert.Equal(@"D:\Vm\Base.vhdx", result);
    }

    [Fact]
    public void SelectMountableVirtualDiskPath_WhenChainIsMissing_ReturnsRequestedPath()
    {
        string result = BackupWindowNew.HyperVBackupTreeHelper.SelectMountableVirtualDiskPath(
            @"D:\Vm\Active.avhdx",
            null);

        Assert.Equal(@"D:\Vm\Active.avhdx", result);
    }

    [Theory]
    [InlineData(@"\\?\Volume{1234}\")]
    [InlineData("C:\\")]
    [InlineData("PHYSICALDRIVE0")]
    [InlineData("SystemState")]
    public void SupportsHyperVVirtualDiskRestore_WhenBackupItemIsRestorableSurface_ReturnsTrue(string selectedItemText)
    {
        bool result = RestoreWindowNew.RegularHyperVRestoreHelper.SupportsHyperVVirtualDiskRestore(selectedItemText);

        Assert.True(result);
    }

    [Fact]
    public void SupportsHyperVVirtualDiskRestore_WhenBackupItemIsRegularFile_ReturnsFalse()
    {
        bool result = RestoreWindowNew.RegularHyperVRestoreHelper.SupportsHyperVVirtualDiskRestore(@"Folder\file.txt");

        Assert.False(result);
    }

    [Fact]
    public void NormalizeHyperVVmName_WhenStateSuffixExists_RemovesSuffix()
    {
        string result = RestoreWindowNew.RegularHyperVRestoreHelper.NormalizeHyperVVmName("Test VM (Running)");

        Assert.Equal("Test VM", result);
    }

    [Fact]
    public void NormalizeHyperVVmName_WhenStateSuffixMissing_ReturnsOriginalName()
    {
        string result = RestoreWindowNew.RegularHyperVRestoreHelper.NormalizeHyperVVmName("Test VM");

        Assert.Equal("Test VM", result);
    }

    [Fact]
    public void GetDefaultHyperVVmName_WhenVirtualDiskPathProvided_ReturnsFileNameWithoutExtension()
    {
        string result = RestoreWindowNew.RegularHyperVRestoreHelper.GetDefaultHyperVVmName(@"D:\HyperV\RestoredServer.vhdx");

        Assert.Equal("RestoredServer", result);
    }

    [Fact]
    public void GetDefaultHyperVVmStoragePath_WhenVirtualDiskPathProvided_ReturnsContainingDirectory()
    {
        string result = RestoreWindowNew.RegularHyperVRestoreHelper.GetDefaultHyperVVmStoragePath(@"D:\HyperV\RestoredServer.vhdx");

        Assert.Equal(@"D:\HyperV", result);
    }

    [Fact]
    public void BuildDefaultHyperVVirtualDiskPath_WhenBackupNameProvided_UsesJobNamedSingleVhdxFile()
    {
        string result = RestoreWindowNew.RegularHyperVRestoreHelper.BuildDefaultHyperVVirtualDiskPath(@"D:\HyperV\Disks", "System Backup Job");

        Assert.Equal(@"D:\HyperV\Disks\System Backup Job.vhdx", result);
    }

    [Fact]
    public void BuildDefaultHyperVVirtualDiskPath_WhenBackupNameContainsInvalidCharacters_SanitizesFileName()
    {
        string result = RestoreWindowNew.RegularHyperVRestoreHelper.BuildDefaultHyperVVirtualDiskPath(@"D:\HyperV\Disks", "System:Backup/Job*");

        Assert.Equal(@"D:\HyperV\Disks\System_Backup_Job_.vhdx", result);
    }

    [Fact]
    public void BuildDefaultHyperVVirtualDiskPath_WhenBackupNameMatchesCloneJob_UsesSingleJobNamedVhdx()
    {
        string result = RestoreWindowNew.RegularHyperVRestoreHelper.BuildDefaultHyperVVirtualDiskPath(@"D:\VM Clones", "SingleVolumeClone");

        Assert.Equal(@"D:\VM Clones\SingleVolumeClone.vhdx", result);
    }

    [Fact]
    public void GetDefaultHyperVVmName_WhenSingleVhdxPathProvided_UsesJobNamedFileStem()
    {
        string result = RestoreWindowNew.RegularHyperVRestoreHelper.GetDefaultHyperVVmName(@"D:\HyperV\Disks\System Backup Job.vhdx");

        Assert.Equal("System Backup Job", result);
    }

    [Fact]
    public void BuildCreateVirtualMachineScript_WhenGenerationTwoAndAutoStartEnabled_UsesFirmwareAndStartCommands()
    {
        string result = RestoreWindowNew.RegularHyperVRestoreHelper.BuildCreateVirtualMachineScript(
            "Restored VM",
            @"D:\HyperV\VMs",
            @"D:\HyperV\Disks\Restored VM.vhdx",
            2,
            true);

        Assert.Contains("New-VM -Name $vmName -Generation 2", result);
        Assert.Contains("Add-VMHardDiskDrive -VMName $vmName -ControllerType SCSI", result);
        Assert.Contains("Set-VMFirmware -VMName 'Restored VM' -FirstBootDevice $bootDisk -EnableSecureBoot Off -ErrorAction Stop", result);
        Assert.Contains("Start-VM -Name 'Restored VM' -ErrorAction Stop | Out-Null", result);
    }

    [Fact]
    public void BuildCreateVirtualMachineScript_WhenGenerationOneAndAutoStartDisabled_UsesIdeAndOmitsFirmwareAndStartCommands()
    {
        string result = RestoreWindowNew.RegularHyperVRestoreHelper.BuildCreateVirtualMachineScript(
            "Legacy VM",
            @"E:\Legacy",
            @"E:\Legacy\Legacy VM.vhdx",
            1,
            false);

        Assert.Contains("New-VM -Name $vmName -Generation 1", result);
        Assert.Contains("Add-VMHardDiskDrive -VMName $vmName -ControllerType IDE", result);
        Assert.DoesNotContain("Set-VMFirmware", result);
        Assert.DoesNotContain("Start-VM", result);
    }

    [Fact]
    public void BuildRegenerateMacAddressScript_WhenVmNameProvided_UsesDynamicMacAddressWhileVmIsOff()
    {
        string result = RestoreWindowNew.RegularHyperVRestoreHelper.BuildRegenerateMacAddressScript("Clone VM");

        Assert.Contains("Get-VM -Name $vmName -ErrorAction Stop", result);
        Assert.Contains("$vm.State -notin @('Off','Saved')", result);
        Assert.Contains("Set-VMNetworkAdapter -VMName $vmName", result);
        Assert.Contains("-DynamicMacAddress", result);
    }

    [Fact]
    public void ShouldScheduleSetupCl_WhenRenameRequested_ReturnsTrue()
    {
        bool result = BackupWindowNew.HyperVBackupTreeHelper.ShouldScheduleSetupCl(
            renameHyperVSystem: true,
            renameHyperVSystemName: "RenamedClone",
            target: SecureServerBackupCommon.BackupTarget.HyperV,
            sourcePaths: null);

        Assert.True(result);
    }

    [Fact]
    public void ShouldScheduleSetupCl_WhenDiskCloneSourceSelected_ReturnsTrue()
    {
        bool result = BackupWindowNew.HyperVBackupTreeHelper.ShouldScheduleSetupCl(
            renameHyperVSystem: false,
            renameHyperVSystemName: string.Empty,
            target: SecureServerBackupCommon.BackupTarget.Disk,
            sourcePaths: new[] { @"\\.\PHYSICALDRIVE2" },
            protectedDiskIndexes: new[] { 2 });

        Assert.True(result);
    }

    [Fact]
    public void ShouldScheduleSetupCl_WhenDiskCloneSourceIsNotProtected_ReturnsFalse()
    {
        bool result = BackupWindowNew.HyperVBackupTreeHelper.ShouldScheduleSetupCl(
            renameHyperVSystem: false,
            renameHyperVSystemName: string.Empty,
            target: SecureServerBackupCommon.BackupTarget.Disk,
            sourcePaths: new[] { @"\\.\PHYSICALDRIVE3" },
            protectedDiskIndexes: new[] { 2 });

        Assert.False(result);
    }

    [Fact]
    public void ShouldScheduleSetupCl_WhenNeitherRenameNorSystemDiskClone_ReturnsFalse()
    {
        bool result = BackupWindowNew.HyperVBackupTreeHelper.ShouldScheduleSetupCl(
            renameHyperVSystem: false,
            renameHyperVSystemName: string.Empty,
            target: SecureServerBackupCommon.BackupTarget.HyperV,
            sourcePaths: new[] { @"C:\Backups\VmExport" },
            protectedDiskIndexes: new[] { 0 });

        Assert.False(result);
    }

    [Fact]
    public void CreateCloneHyperVPaths_WhenRenameRequested_UsesRenamedLayout()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "SecureServerBackupTests", Guid.NewGuid().ToString("N"));

        try
        {
            var job = new BackupJob
            {
                Name = "Clone Job",
                DestinationPath = rootPath,
                RenameHyperVSystem = true,
                RenameHyperVSystemName = "RenamedClone"
            };

            BackupWindowNew.CloneHyperVPaths result = BackupWindowNew.CreateCloneHyperVPaths(job);

            Assert.Equal(Path.Combine(rootPath, "RenamedClone"), result.RootDirectory);
            Assert.Equal(Path.Combine(result.RootDirectory, "HyperVSys"), result.HyperVSystemDirectory);
            Assert.Equal(Path.Combine(result.RootDirectory, "HyperVDisk"), result.HyperVDiskDirectory);
            Assert.Equal(Path.Combine(result.HyperVDiskDirectory, "RenamedClone.vhdx"), result.VirtualDiskPath);
            Assert.Equal("RenamedClone", result.VmName);
            Assert.True(Directory.Exists(result.HyperVSystemDirectory));
            Assert.True(Directory.Exists(result.HyperVDiskDirectory));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateCloneHyperVPaths_WhenDiskCloneWithoutRename_UsesJobFolderAndCurrentSystemVmName()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "SecureServerBackupTests", Guid.NewGuid().ToString("N"));

        try
        {
            var job = new BackupJob
            {
                Name = "Disk Clone Job",
                DestinationPath = rootPath,
                Target = BackupTarget.Disk,
                SourcePaths = new() { @"\\.\PHYSICALDRIVE0" },
                RenameHyperVSystem = false,
                RenameHyperVSystemName = string.Empty
            };

            BackupWindowNew.CloneHyperVPaths result = BackupWindowNew.CreateCloneHyperVPaths(job);

            Assert.Equal(Path.Combine(rootPath, "Disk Clone Job"), result.RootDirectory);
            Assert.Equal(Environment.MachineName, result.VmName);
            Assert.Equal(Path.Combine(result.HyperVDiskDirectory, $"{Environment.MachineName}.vhdx"), result.VirtualDiskPath);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateCloneHyperVPaths_WhenRenameNotRequested_UsesJobNameLayout()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "SecureServerBackupTests", Guid.NewGuid().ToString("N"));

        try
        {
            var job = new BackupJob
            {
                Name = "Clone Job",
                DestinationPath = rootPath,
                RenameHyperVSystem = false,
                RenameHyperVSystemName = string.Empty
            };

            BackupWindowNew.CloneHyperVPaths result = BackupWindowNew.CreateCloneHyperVPaths(job);

            Assert.Equal(Path.Combine(rootPath, "Clone Job"), result.RootDirectory);
            Assert.Equal(Path.Combine(result.HyperVDiskDirectory, "Clone Job.vhdx"), result.VirtualDiskPath);
            Assert.Equal("Clone Job", result.VmName);
            Assert.True(Directory.Exists(result.RootDirectory));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
