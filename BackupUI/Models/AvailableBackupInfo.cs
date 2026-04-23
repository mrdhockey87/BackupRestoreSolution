using System;
using System.Collections.Generic;

namespace BackupUI.Models
{
    /// <summary>
    /// Represents an available backup that can be mounted
    /// </summary>
    public class AvailableBackupInfo
    {
        public string BackupName { get; set; } = "";
        public string BackupType { get; set; } = ""; // Full, Incremental, Differential
        public DateTime BackupDate { get; set; }
        public string BackupPath { get; set; } = "";
        public bool IsEncrypted { get; set; }
        public string ProtectedEncryptionPassword { get; set; } = "";
        public string EncryptionStatus => IsEncrypted ? "Yes / Password" : "No";
        public List<BackupPoint> BackupPoints { get; set; } = new();
    }

    /// <summary>
    /// Represents a specific backup point in time (for Inc/Diff backups)
    /// </summary>
    public class BackupPoint
    {
        public DateTime PointDate { get; set; }
        public string PointType { get; set; } = ""; // Full, Incremental, Differential
        public string VhdxPath { get; set; } = "";
        
        public string DisplayName => $"{PointDate:yyyy-MM-dd HH:mm} ({PointType})";
    }
}
