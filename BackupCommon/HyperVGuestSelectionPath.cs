using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SecureServerBackupCommon;

public enum HyperVGuestSelectionKind
{
    VirtualDisk,
    Volume,
    Folder,
    File
}

public sealed record HyperVGuestSelectionInfo(
    HyperVGuestSelectionKind Kind,
    string VirtualMachineName,
    string VirtualDiskPath,
    int PartitionNumber,
    string RelativePath);

public sealed record HyperVGuestMountedPartition(int PartitionNumber, string MountPath);

public static class HyperVGuestSelectionPath
{
    private const string Prefix = "hypervguest:";

    /// <summary>
    /// Encodes a Hyper-V guest selection into a stable string that can be persisted in backup jobs.
    /// </summary>
    public static string Encode(
        HyperVGuestSelectionKind kind,
        string virtualMachineName,
        string virtualDiskPath,
        int partitionNumber,
        string? relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualMachineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualDiskPath);

        string normalizedRelativePath = NormalizeRelativePath(relativePath);

        return Prefix +
               Uri.EscapeDataString(kind.ToString()) + "|" +
               Uri.EscapeDataString(virtualMachineName) + "|" +
               Uri.EscapeDataString(virtualDiskPath) + "|" +
               partitionNumber.ToString(CultureInfo.InvariantCulture) + "|" +
               Uri.EscapeDataString(normalizedRelativePath);
    }

    /// <summary>
    /// Returns true when the supplied path contains an encoded Hyper-V guest selection.
    /// </summary>
    public static bool IsEncodedPath(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Attempts to parse a persisted Hyper-V guest selection path.
    /// </summary>
    public static bool TryParse(string? value, out HyperVGuestSelectionInfo? selection)
    {
        selection = null;
        if (!IsEncodedPath(value))
        {
            return false;
        }

        string[] parts = value![Prefix.Length..].Split('|');
        if (parts.Length != 5)
        {
            return false;
        }

        if (!Enum.TryParse(Uri.UnescapeDataString(parts[0]), ignoreCase: true, out HyperVGuestSelectionKind kind))
        {
            return false;
        }

        string virtualMachineName = Uri.UnescapeDataString(parts[1]);
        string virtualDiskPath = Uri.UnescapeDataString(parts[2]);
        if (string.IsNullOrWhiteSpace(virtualMachineName) || string.IsNullOrWhiteSpace(virtualDiskPath))
        {
            return false;
        }

        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int partitionNumber) || partitionNumber < 0)
        {
            return false;
        }

        selection = new HyperVGuestSelectionInfo(
            kind,
            virtualMachineName,
            virtualDiskPath,
            partitionNumber,
            NormalizeRelativePath(Uri.UnescapeDataString(parts[4])));
        return true;
    }

    /// <summary>
    /// Resolves candidate mounted source paths for a persisted Hyper-V guest selection.
    /// </summary>
    public static IReadOnlyList<string> GetCandidateSourcePaths(HyperVGuestSelectionInfo selection, IEnumerable<HyperVGuestMountedPartition> partitions)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(partitions);

        HyperVGuestMountedPartition[] mountedPartitions = partitions.ToArray();
        if (selection.Kind == HyperVGuestSelectionKind.VirtualDisk)
        {
            return mountedPartitions
                .Select(partition => partition.MountPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        HyperVGuestMountedPartition? partition = mountedPartitions.FirstOrDefault(candidate => candidate.PartitionNumber == selection.PartitionNumber);
        if (partition == null || string.IsNullOrWhiteSpace(partition.MountPath))
        {
            return Array.Empty<string>();
        }

        string resolvedPath = string.IsNullOrWhiteSpace(selection.RelativePath)
            ? partition.MountPath
            : Path.Combine(partition.MountPath, selection.RelativePath);

        return new[] { resolvedPath };
    }

    /// <summary>
    /// Normalizes a persisted guest-relative path so equivalent selections compare consistently.
    /// </summary>
    public static string NormalizeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Replace('/', Path.DirectorySeparatorChar).Trim();
        return normalized.TrimStart(Path.DirectorySeparatorChar);
    }
}
