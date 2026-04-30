using System.Collections.Generic;
using System.IO;
using SecureServerBackupCommon;
using Xunit;

namespace SecureServerBackupService.Tests;

public sealed class HyperVGuestSelectionPathTests
{
    [Fact]
    public void EncodeAndParse_WhenVirtualDiskSelection_RoundTrips()
    {
        string encoded = HyperVGuestSelectionPath.Encode(
            HyperVGuestSelectionKind.VirtualDisk,
            "VM One",
            @"D:\HyperV\VM One\Disk 1.vhdx",
            0,
            string.Empty);

        bool parsed = HyperVGuestSelectionPath.TryParse(encoded, out HyperVGuestSelectionInfo? selection);

        Assert.True(parsed);
        Assert.NotNull(selection);
        Assert.Equal(HyperVGuestSelectionKind.VirtualDisk, selection!.Kind);
        Assert.Equal("VM One", selection.VirtualMachineName);
        Assert.Equal(@"D:\HyperV\VM One\Disk 1.vhdx", selection.VirtualDiskPath);
        Assert.Equal(0, selection.PartitionNumber);
        Assert.Equal(string.Empty, selection.RelativePath);
    }

    [Fact]
    public void EncodeAndParse_WhenFolderSelection_RoundTripsNormalizedRelativePath()
    {
        string encoded = HyperVGuestSelectionPath.Encode(
            HyperVGuestSelectionKind.Folder,
            "VM Two",
            @"E:\HyperV\VM Two\Data.vhdx",
            3,
            @"\Users\Public/Documents");

        bool parsed = HyperVGuestSelectionPath.TryParse(encoded, out HyperVGuestSelectionInfo? selection);

        Assert.True(parsed);
        Assert.NotNull(selection);
        Assert.Equal(HyperVGuestSelectionKind.Folder, selection!.Kind);
        Assert.Equal("Users\\Public\\Documents", selection.RelativePath);
    }

    [Fact]
    public void NormalizeRelativePath_WhenMixedSeparatorsAndWhitespace_NormalizesConsistently()
    {
        string normalized = HyperVGuestSelectionPath.NormalizeRelativePath(@"  \Users/Public\Documents\  ");

        Assert.Equal("Users\\Public\\Documents\\", normalized);
    }

    [Fact]
    public void EncodeAndParse_WhenChildGuestFolderSelected_PreservesStableRelativePath()
    {
        string encoded = HyperVGuestSelectionPath.Encode(
            HyperVGuestSelectionKind.Folder,
            "VM Three",
            @"F:\HyperV\VM Three\Data.vhdx",
            2,
            @"ProgramData\Secure Folder");

        bool parsed = HyperVGuestSelectionPath.TryParse(encoded, out HyperVGuestSelectionInfo? selection);

        Assert.True(parsed);
        Assert.NotNull(selection);
        Assert.Equal(HyperVGuestSelectionKind.Folder, selection!.Kind);
        Assert.Equal("VM Three", selection.VirtualMachineName);
        Assert.Equal(@"F:\HyperV\VM Three\Data.vhdx", selection.VirtualDiskPath);
        Assert.Equal(2, selection.PartitionNumber);
        Assert.Equal("ProgramData\\Secure Folder", selection.RelativePath);
    }

    [Fact]
    public void GetCandidateSourcePaths_WhenVirtualDiskSelected_ReturnsDistinctPartitionMounts()
    {
        HyperVGuestSelectionInfo selection = new(
            HyperVGuestSelectionKind.VirtualDisk,
            "VM Four",
            @"G:\HyperV\VM Four\Data.vhdx",
            0,
            string.Empty);

        HyperVGuestMountedPartition[] partitions =
        [
            new(1, @"M:\"),
            new(2, @"N:\"),
            new(3, @"M:\")
        ];

        IReadOnlyList<string> resolvedPaths = HyperVGuestSelectionPath.GetCandidateSourcePaths(selection, partitions);

        Assert.Equal(2, resolvedPaths.Count);
        Assert.Equal(@"M:\", resolvedPaths[0]);
        Assert.Equal(@"N:\", resolvedPaths[1]);
    }

    [Fact]
    public void GetCandidateSourcePaths_WhenFolderSelected_AppendsRelativePathToMatchingPartition()
    {
        HyperVGuestSelectionInfo selection = new(
            HyperVGuestSelectionKind.Folder,
            "VM Five",
            @"H:\HyperV\VM Five\Data.vhdx",
            4,
            @"Users\Admin");

        HyperVGuestMountedPartition[] partitions =
        [
            new(4, @"X:\")
        ];

        IReadOnlyList<string> resolvedPaths = HyperVGuestSelectionPath.GetCandidateSourcePaths(selection, partitions);

        Assert.Single(resolvedPaths);
        Assert.Equal(Path.Combine(@"X:\", @"Users\Admin"), resolvedPaths[0]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:\\Temp")]
    [InlineData("hypervguest:bad|data")]
    public void TryParse_WhenValueInvalid_ReturnsFalse(string? value)
    {
        bool parsed = HyperVGuestSelectionPath.TryParse(value, out HyperVGuestSelectionInfo? selection);

        Assert.False(parsed);
        Assert.Null(selection);
    }
}
