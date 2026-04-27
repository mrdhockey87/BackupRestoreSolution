namespace SecureServerBackupCommon
{
    public enum BackupType
    {
        Full,
        Incremental,
        Differential,
        CloneToDisk,
        CloneToVirtualDisk,
        CloneHyperVSystem
    }
}
