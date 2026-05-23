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

    // Logging callback for C++ to send log entries to C#
    // level: 0=Info, 1=Success, 2=Warning, 3=Error
    typedef void(__cdecl* LogCallback)(int level, const wchar_t* message, const wchar_t* details);

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
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback,
        LogCallback logCallback);

    // Backup selected files/folders from a single source root using an include-path list
    BACKUPENGINE_API int BackupFilesBySelections(
        const wchar_t* sourceRoot,
        const wchar_t* destPath,
        const wchar_t** includePaths,
        int includePathCount,
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback,
        LogCallback logCallback);

    // Backup an entire volume (with optional system state)
    BACKUPENGINE_API int BackupVolume(
        const wchar_t* volumePath,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback,
        LogCallback logCallback);

    // Backup an entire disk by disk number
    BACKUPENGINE_API int BackupDisk(
        int diskNumber,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback,
        LogCallback logCallback);

    // Backup an entire disk incrementally by appending referential images to an existing archive
    BACKUPENGINE_API int BackupDiskIncremental(
        int diskNumber,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback,
        LogCallback logCallback);

    // Backup an entire disk differentially by appending images relative to the base archive
    BACKUPENGINE_API int BackupDiskDifferential(
        int diskNumber,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback,
        LogCallback logCallback);

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

    // Backup a Hyper-V Virtual Machine incrementally by creating a new point linked to the last backup
    BACKUPENGINE_API int BackupHyperVVMIncremental(
        const wchar_t* vmName,
        const wchar_t* destPath,
        ProgressCallback callback);

    // Backup a Hyper-V Virtual Machine differentially by creating a new point linked to the last full backup
    BACKUPENGINE_API int BackupHyperVVMDifferential(
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

    // Schedule SetupCl against an offline SYSTEM hive for cloned Hyper-V first boot
    BACKUPENGINE_API int ScheduleOfflineSystemSetupCl(
        const wchar_t* systemHivePath);

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

    // Verify SSB/WIM archive integrity after creation (enhanced verification)
    // Returns: 0 = success, negative = error code
    // errorMsg: receives detailed error message if verification fails
    BACKUPENGINE_API int VerifyWimArchive(
        const wchar_t* archivePath,
        int expectedImageCount,
        wchar_t* errorMsg,
        int errorMsgSize,
        ProgressCallback callback);

    // Check the health of a backup image using DISM
    // Returns: 0 = Healthy, 1 = Repairable, 2 = NonRepairable, negative = error
    BACKUPENGINE_API int CheckBackupImageHealth(
        const wchar_t* backupPath,
        int imageIndex,
        bool scanImage,
        wchar_t* healthMessage,
        int healthMessageSize,
        ProgressCallback callback);

    // Repair a backup image using DISM RestoreHealth
    // Returns: 0 = Healthy after repair, 1 = Repairable, 2 = NonRepairable, negative = error
    BACKUPENGINE_API int RestoreBackupImageHealth(
        const wchar_t* backupPath,
        int imageIndex,
        const wchar_t** sourcePaths,
        int sourcePathCount,
        bool limitAccess,
        wchar_t* healthMessage,
        int healthMessageSize,
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

    // Enumerate Hyper-V virtual machine disk paths as tab-delimited lines:
    // <vmName>\t<vmDisplayName>\t<virtualDiskPath>
    BACKUPENGINE_API int EnumerateHyperVVirtualMachineDisks(
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

    // ====================
    // Job Context Functions
    // ====================

    // Set the current job name for logging context
    // C++ engine will log to {JobName}.json instead of engine.json
    BACKUPENGINE_API void SetCurrentJobName(const wchar_t* jobName);

    // Clear the current job name (reverts to engine.json logging)
    BACKUPENGINE_API void ClearCurrentJobName();

    // ====================
    // Enhanced Restore Functions (Version 4.7.0.0)
    // ====================

    // Enumerate backup dates/snapshots in a backup folder
    // Returns: Date|Type|Size|Path separated by newlines
    // Example: 2026-01-30 14:30:00|Full|2.5 GB|D:\Backups\Full_20260130_143000
    BACKUPENGINE_API int EnumerateBackupDates(
        const wchar_t* backupPath,
        wchar_t* buffer,
        int bufferSize);

    // Restore selected items from backup using a manifest
    // Manifest format: One path per line (drives, volumes, folders, or files)
    BACKUPENGINE_API int RestoreWithManifest(
        const wchar_t* backupPath,
        const wchar_t* destPath,
        const wchar_t* manifest,
        bool overwriteExisting,
        bool restoreSystemState,
        bool preservePermissions,
        ProgressCallback callback);
}
