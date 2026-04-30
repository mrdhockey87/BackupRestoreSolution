using SecureServerBackup.Windows;
using Xunit;

namespace SecureServerBackup.Tests;

public sealed class RegularHyperVRestoreHelperTests
{
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
}
