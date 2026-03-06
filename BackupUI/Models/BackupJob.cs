using System;
using System.Collections.Generic;

namespace BackupUI.Models
{
    public class BackupJob
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public BackupType Type { get; set; }
        public BackupTarget Target { get; set; }
        public List<string> SourcePaths { get; set; } = new();
        public string DestinationPath { get; set; } = string.Empty;
        public bool IncludeSystemState { get; set; }
        public bool CompressData { get; set; }
        public bool VerifyAfterBackup { get; set; }
        public DateTime? LastRunTime { get; set; }
        public BackupSchedule? Schedule { get; set; }
        public bool IsHyperVBackup { get; set; }
        public List<string> HyperVMachines { get; set; } = new();
        
        // Retention settings
        public int RetainFullBackupCount { get; set; } = 1; // Default: keep only 1 full backup

        // Retry tracking
        public int ConsecutiveFailures { get; set; } = 0; // Track consecutive backup failures for retry limit

        // Import support
        public bool IsImported { get; set; } = false; // True if imported from external backup
        public bool UseCompression { get; set; } = true; // True to create .brs (compressed), false for .wim
    }
}
