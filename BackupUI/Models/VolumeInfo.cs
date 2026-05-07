using System;

namespace SecureServerBackup.Models
{
    /// <summary>
    /// Represents volume information for configuration and resizing
    /// </summary>
    public class VolumeInfo
    {
        public string Label { get; set; } = string.Empty;
        public long Size { get; set; }
        public long UsedSpace { get; set; }
        public long FreeSpace => Size - UsedSpace;
        public bool IsResizable { get; set; }
        public bool IsSystemVolume { get; set; }
        public string FileSystem { get; set; } = string.Empty;
        public int AllocationUnitSize { get; set; }

        // --- Partition ordering / restore metadata ---

        /// <summary>Zero-based index within the backup image list (used for ordered restore).</summary>
        public int ImageIndex { get; set; }

        /// <summary>Partition number on the source disk (1-based).</summary>
        public uint PartitionNumber { get; set; }

        /// <summary>Byte offset of the partition on the source disk.</summary>
        public ulong PartitionOffsetBytes { get; set; }

        /// <summary>Byte length of the partition on the source disk.</summary>
        public ulong PartitionLengthBytes { get; set; }

        /// <summary>Partition style: GPT, MBR, or empty.</summary>
        public string PartitionStyle { get; set; } = string.Empty;

        /// <summary>GPT/MBR partition type GUID or byte value.</summary>
        public string PartitionType { get; set; } = string.Empty;

        /// <summary>Volume GUID path (\\?\Volume{...}\).</summary>
        public string SourceVolumeGuidPath { get; set; } = string.Empty;

        /// <summary>Drive-letter or mount path on the source machine (e.g. C:\).</summary>
        public string SourceVolumeMountPath { get; set; } = string.Empty;

        /// <summary>True when this volume holds the OS boot loader.</summary>
        public bool IsBootVolume { get; set; }

        /// <summary>User-chosen target size in bytes (set after VolumeConfigurationWindow).</summary>
        public long TargetSize { get; set; }

        /// <summary>Minimum restore size: actual used space plus 10 % overhead.</summary>
        public long MinimumSize => UsedSpace > 0 ? UsedSpace + (long)(UsedSpace * 0.10) : Size;
    }
}
