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
    }
}
