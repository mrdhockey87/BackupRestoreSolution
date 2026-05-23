using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SecureServerBackupCommon
{
    public class BackupJob
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public BackupType Type { get; set; }
        public BackupTarget Target { get; set; }
        public List<string> SourcePaths { get; set; } = new();
        public List<string> SelectedFilesSourceRoots { get; set; } = new();
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
        public int SelectedFilesRetentionDays { get; set; } = 7; // Keep Selected Files history points for this many days

        // Retry tracking
        public int ConsecutiveFailures { get; set; } = 0; // Track consecutive backup failures for retry limit

        // Scheduling and execution tracking
        public DateTime? NextScheduledRun { get; set; } // When this job should next execute (replaces Schedule.NextRunTime)
        public bool IsCurrentlyRunning { get; set; } = false; // True if backup is currently executing (prevents concurrent runs)

        // Auto-recovery: Force full backup on next run if incremental/differential verification fails
        public bool ForceFullBackupOnNextRun { get; set; } = false;

        // Import support
        public bool IsImported { get; set; } = false; // True if imported from external backup
        public bool UseCompression { get; set; } = true; // True to create .brs (compressed), false for .wim

        // User-defined exclusions (editable by user)
        public List<string> UserExclusions { get; set; } = new(); // Files, folders, or patterns (*.tmp) to exclude from backup

        // Optional AES-128 backup encryption settings
        public bool EncryptBackup { get; set; } = false;
        public string ProtectedEncryptionPassword { get; set; } = string.Empty;

        // Optional Clone Hyper-V System rename settings
        public bool RenameHyperVSystem { get; set; } = false;
        public string RenameHyperVSystemName { get; set; } = string.Empty;

        public string GetVirtualDiskClonePath()
        {
            string fileName = string.IsNullOrWhiteSpace(Name)
                ? "Clone.vhdx"
                : $"{SanitizeFileName(Name)}.vhdx";

            return Path.Combine(DestinationPath, fileName);
        }

        public bool ShouldCloneToVirtualDiskAsDisk()
        {
            if (Target == BackupTarget.Disk)
            {
                return true;
            }

            if (Target != BackupTarget.Volume)
            {
                return false;
            }

            return SourcePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Clone";
            }

            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalidCharacters.Contains(ch) ? '_' : ch).ToArray());
        }
    }
}
