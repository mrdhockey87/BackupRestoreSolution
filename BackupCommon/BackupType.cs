namespace SecureServerBackupCommon
{
    public enum BackupType
    {
        Full,
        Incremental,
        Differential,
        SelectedFilesAndFolders,
        CloneToDisk,
        CloneToVirtualDisk,
        CloneHyperVSystem
    }
}
