// BackupEngine.h - Main interface for the Backup & Restore Engine
// Supports Windows Server 2019, 2022, and 2025
// Provides VSS snapshots, Hyper-V backup/restore, compression, and system state operations

#pragma once

#ifdef BACKUPENGINE_EXPORTS
#define BACKUPENGINE_API __declspec(dllexport)
#else
#define BACKUPENGINE_API __declspec(dllimport)
#endif

extern "C" {
    // Callback for progress updates
    typedef void (*ProgressCallback)(int percentage, const wchar_t* message);

    // ====================
    // Backup Functions
    // ====================

    // Create a VSS snapshot of a volume
    BACKUPENGINE_API int CreateVolumeSnapshot(
        const wchar_t* volume,
        wchar_t* snapshotPath,
        int pathSize);

    // Backup files/folders to destination with optional compression
    BACKUPENGINE_API int BackupFiles(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        ProgressCallback callback);

    // Backup an entire volume (with optional system state)
    BACKUPENGINE_API int BackupVolume(
        const wchar_t* volumePath,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        ProgressCallback callback);

    // Backup an entire disk by disk number
    BACKUPENGINE_API int BackupDisk(
        int diskNumber,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        ProgressCallback callback);

    // Create incremental backup (only changed files since last backup)
    BACKUPENGINE_API int CreateIncrementalBackup(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t* baseBackupPath,
        ProgressCallback callback);

    // Create differential backup (all changes since last full backup)
    BACKUPENGINE_API int CreateDifferentialBackup(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t* fullBackupPath,
        ProgressCallback callback);

    // Backup a Hyper-V Virtual Machine
    BACKUPENGINE_API int BackupHyperVVM(
        const wchar_t* vmName,
        const wchar_t* destPath,
        ProgressCallback callback);

    // Delete a VSS snapshot
    BACKUPENGINE_API int DeleteSnapshot(
        const wchar_t* snapshotId);

    // ====================
    // Restore Functions
    // ====================

    // Restore files from backup
    BACKUPENGINE_API int RestoreFiles(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        bool overwriteExisting,
        ProgressCallback callback);

    // Restore volume from backup
    BACKUPENGINE_API int RestoreVolume(
        const wchar_t* backupPath,
        const wchar_t* targetVolume,
        bool restoreSystemState,
        ProgressCallback callback);

    // Restore disk from backup
    BACKUPENGINE_API int RestoreDisk(
        const wchar_t* backupPath,
        int targetDiskNumber,
        bool restoreSystemState,
        ProgressCallback callback);

    // Restore a Hyper-V VM from backup
    BACKUPENGINE_API int RestoreHyperVVM(
        const wchar_t* backupPath,
        const wchar_t* vmName,
        const wchar_t* vmStoragePath,
        bool startAfterRestore,
        ProgressCallback callback);

    // Restore system state (registry, boot files, system files)
    BACKUPENGINE_API int RestoreSystemState(
        const wchar_t* backupPath,
        const wchar_t* targetVolume,
        ProgressCallback callback);

    // Restore a Windows boot disk as a Hyper-V bootable disk
    BACKUPENGINE_API int RestoreBootDiskAsHyperV(
        const wchar_t* backupPath,
        const wchar_t* vmName,
        const wchar_t* vmStoragePath,
        bool startAfterRestore,
        ProgressCallback callback);

    // ====================
    // Verification & Utility Functions
    // ====================

    // List contents of a backup
    BACKUPENGINE_API int ListBackupContents(
        const wchar_t* backupPath,
        wchar_t* buffer,
        int bufferSize);

    // Verify backup integrity
    BACKUPENGINE_API int VerifyBackup(
        const wchar_t* backupPath,
        ProgressCallback callback);

    // Enumerate all volumes on the system
    BACKUPENGINE_API int EnumerateVolumes(
        wchar_t* buffer,
        int bufferSize);

    // Enumerate all physical disks
    BACKUPENGINE_API int EnumerateDisks(
        wchar_t* buffer,
        int bufferSize);

    // Enumerate Hyper-V virtual machines
    BACKUPENGINE_API int EnumerateHyperVMachines(
        wchar_t* buffer,
        int bufferSize);

    // Check if a volume is a boot volume
    BACKUPENGINE_API int IsBootVolume(
        const wchar_t* volumePath,
        bool* isBootVolume);

    // Get detailed backup information
    BACKUPENGINE_API int GetBackupInfo(
        const wchar_t* backupPath,
        wchar_t* buffer,
        int bufferSize);

    // ====================
    // Recovery Environment Functions
    // ====================

    // Create a bootable USB recovery environment
    BACKUPENGINE_API int CreateRecoveryEnvironment(
        const wchar_t* usbDriveLetter,
        const wchar_t* programPath,
        ProgressCallback callback);

    // Install WinPE recovery boot files to USB
    BACKUPENGINE_API int InstallRecoveryBootFiles(
        const wchar_t* usbDriveLetter,
        ProgressCallback callback);

    // ====================
    // Error Handling
    // ====================

    // Get last error message
    BACKUPENGINE_API void GetLastErrorMessage(
        wchar_t* buffer,
        int bufferSize);

    // Get Windows version information
    BACKUPENGINE_API int GetWindowsVersion(
        int* major,
        int* minor,
        int* build);
}