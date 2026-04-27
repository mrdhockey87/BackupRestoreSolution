using System;

namespace SecureServerBackup.Models
{
    public class BackupDateItem
    {
        public DateTime Date { get; set; }
        public string DisplayDate => Date.ToString("MM/dd/yyyy hh:mm tt");
        public string BackupType { get; set; } = "Full Backup";
        public string Size { get; set; } = "";
        public string BackupPath { get; set; } = "";
    }
}
