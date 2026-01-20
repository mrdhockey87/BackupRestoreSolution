using System;
using System.Collections.Generic;

namespace BackupUI.Models
{
    public class RestoreJob
    {
        public Guid Id { get; set; }
        public string BackupPath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public bool OverwriteExisting { get; set; }
        public bool RestoreAsHyperV { get; set; }
        public string? HyperVMachineName { get; set; }
        public string? HyperVStoragePath { get; set; }
        public bool StartVMAfterRestore { get; set; }
        public bool RestoreSystemState { get; set; }
        public List<string> SelectedFiles { get; set; } = new();
    }
}
