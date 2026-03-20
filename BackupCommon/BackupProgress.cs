using System;

namespace BackupCommon
{
    /// <summary>
    /// Shared DTO for backup progress information between BackupService and BackupUI
    /// Version 6.0.1.19: Added CurrentFile to display real-time file names
    /// </summary>
    public class BackupProgress
    {
        public Guid JobId { get; set; }
        public bool IsRunning { get; set; }
        public int Percentage { get; set; }
        public string Message { get; set; } = "";
        public string CurrentFile { get; set; } = "";  // v6.0.1.19: Current file being backed up
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
