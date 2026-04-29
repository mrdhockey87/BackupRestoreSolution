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
}
