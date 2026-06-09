//BackupEngine.h
// BackupEngine.h - Main interface for the Backup & Restore Engine
// Supports Windows Server 2019, 2022, and 2025
// Provides VSS snapshots, Hyper-V backup/restore, compression, system state,
// Hyper-V VM clone, disk clone, AVHDX consolidation, and post-clone verification

#pragma once

#ifdef BACKUPENGINE_EXPORTS
#define BACKUPENGINE_API __declspec(dllexport)
#else
#define BACKUPENGINE_API __declspec(dllimport)
#endif

#include <cstdint>
#include <windows.h>
#include <windef.h>

#ifndef MAX_PATH
#define MAX_PATH 260
#endif

extern "C" {

    // =========================================================================
    // EXISTING CALLBACKS  (unchanged — __cdecl as before)
    // =========================================================================

    // Progress callback — percentage + message
    typedef void (*ProgressCallback)(int percentage, const wchar_t* message);

    // Logging callback — level: 0=Info, 1=Success, 2=Warning, 3=Error
    typedef void(__cdecl* LogCallback)(
        int            level,
        const wchar_t* message,
        const wchar_t* details);

    // =========================================================================
    // NEW HVE CALLBACKS  — match __cdecl to stay consistent with your codebase
    // =========================================================================

    // Extended progress callback — adds userState passthrough for C# GCHandle
    typedef void(__cdecl* HVE_ProgressCallback)(
        int32_t        percent,
        const wchar_t* message,
        void* userState);

    // Status/event callback — phase changes, warnings, recovery events
    typedef void(__cdecl* HVE_StatusCallback)(
        int32_t        eventCode,
        const wchar_t* message,
        void* userState);

    // =========================================================================
    // HVE RESULT CODES
    // =========================================================================

    enum HVE_RESULT : int32_t
    {
        HVE_OK = 0,
        HVE_ERR_GENERAL = -1,
        HVE_ERR_WMI_CONNECT = -2,
        HVE_ERR_WMI_QUERY = -3,
        HVE_ERR_VM_NOT_FOUND = -4,
        HVE_ERR_VM_NOT_OFFLINE = -5,
        HVE_ERR_DISK_LOCKED = -6,
        HVE_ERR_MERGE_FAILED = -7,
        HVE_ERR_IMPORT_FAILED = -8,
        HVE_ERR_WIM_OPEN = -9,
        HVE_ERR_WIM_APPLY = -10,
        HVE_ERR_CANCELLED = -11,
        HVE_ERR_INVALID_ARG = -12,
        HVE_ERR_CHECKPOINT_FAIL = -13,
    };

    // =========================================================================
    // HVE STATUS EVENT CODES  (passed to HVE_StatusCallback.eventCode)
    // =========================================================================

    enum HVE_EVENT : int32_t
    {
        HVE_EVT_INFO = 0,
        HVE_EVT_WARNING = 1,
        HVE_EVT_ERROR = 2,
        HVE_EVT_PHASE_START = 3,
        HVE_EVT_PHASE_END = 4,
        HVE_EVT_RECOVERING = 5,
        HVE_EVT_RECOVERED = 6,
        HVE_EVT_FATAL = 7,
    };

    // =========================================================================
    // HVE PARAMETER & RESULT STRUCTS
    // pack(8) keeps layout identical on both sides of the P/Invoke boundary
    // =========================================================================

#pragma pack(push, 8)

    struct HVE_CloneVMParams
    {
        const wchar_t* vmName;
        const wchar_t* exportStagingPath;
        const wchar_t* targetPath;
        bool           generateNewId;
        bool           removeCheckpoints;
        const wchar_t* checkpointPath;      // nullable — pass nullptr to skip
    };

    struct HVE_CloneDiskParams
    {
        const wchar_t* sourceVhdxPath;
        const wchar_t* destinationPath;
        bool           consolidateChain;
        bool           compactAfterMerge;
    };

    struct HVE_DiskEntry
    {
        wchar_t  path[MAX_PATH];
        wchar_t  parentPath[MAX_PATH];
        int32_t  depthFromBase;             // 0 = base VHDX, 1+ = AVHDX levels
        uint64_t sizeBytes;
        bool     isBase;
    };

    // ── Verify structs ────────────────────────────────────────────────────────

    enum HVE_VERIFY_STATUS : int32_t
    {
        HVE_VERIFY_PASS = 0,
        HVE_VERIFY_FAIL = 1,
        HVE_VERIFY_WARNING = 2,
        HVE_VERIFY_SKIPPED = 3,
    };

    struct HVE_VerifyCheckResult
    {
        wchar_t             checkName[128];
        HVE_VERIFY_STATUS   status;
        wchar_t             detail[512];
        uint64_t            elapsedMs;
    };

    struct HVE_VerifyReport
    {
        // Overall result
        bool     overallPass;
        int32_t  totalChecks;
        int32_t  passCount;
        int32_t  failCount;
        int32_t  warnCount;

        // Per-check results (fixed 32 slots — matches C# MarshalAs SizeConst)
        HVE_VerifyCheckResult checks[32];

        // Disk size comparison
        uint64_t sourceVhdxBytes;
        uint64_t cloneVhdxBytes;
        bool     checksumMatch;
        wchar_t  sourceChecksum[65];        // hex SHA-256 null terminated
        wchar_t  cloneChecksum[65];

        // Optional boot test result
        bool     vmBootTestPerformed;
        bool     vmBootedCleanly;
        uint32_t vmBootTimeMs;

        // First failure detail for quick UI display
        wchar_t  firstFailureDetail[512];
    };

    struct HVE_VerifyParams
    {
        const wchar_t* sourceVmName;
        const wchar_t* cloneVmName;
        const wchar_t* sourceVhdxPath;
        const wchar_t* cloneVhdxPath;
        const wchar_t* cloneExportPath;
        bool           performBootTest;
        bool           performChecksumVerify;
        uint32_t       bootTestTimeoutSec;
    };

#pragma pack(pop)

    // =========================================================================
    // EXISTING BACKUP FUNCTIONS  (unchanged)
    // =========================================================================

    BACKUPENGINE_API int CreateVolumeSnapshot(
        const wchar_t* volume,
        wchar_t* snapshotPath,
        int            pathSize);

    BACKUPENGINE_API int BackupFiles(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t** userExclusions,
        int             userExclusionCount,
        ProgressCallback callback,
        LogCallback      logCallback);

    BACKUPENGINE_API int BackupFilesBySelections(
        const wchar_t* sourceRoot,
        const wchar_t* destPath,
        const wchar_t** includePaths,
        int             includePathCount,
        const wchar_t** userExclusions,
        int             userExclusionCount,
        ProgressCallback callback,
        LogCallback      logCallback);

    BACKUPENGINE_API int BackupVolume(
        const wchar_t* volumePath,
        const wchar_t* destPath,
        bool            includeSystemState,
        bool            compress,
        const wchar_t** userExclusions,
        int             userExclusionCount,
        ProgressCallback callback,
        LogCallback      logCallback);

    BACKUPENGINE_API int BackupDisk(
        int             diskNumber,
        const wchar_t* destPath,
        bool            includeSystemState,
        bool            compress,
        const wchar_t** userExclusions,
        int             userExclusionCount,
        ProgressCallback callback,
        LogCallback      logCallback);

    BACKUPENGINE_API int BackupDiskIncremental(
        int             diskNumber,
        const wchar_t* destPath,
        bool            includeSystemState,
        bool            compress,
        const wchar_t** userExclusions,
        int             userExclusionCount,
        ProgressCallback callback,
        LogCallback      logCallback);

    BACKUPENGINE_API int BackupDiskDifferential(
        int             diskNumber,
        const wchar_t* destPath,
        bool            includeSystemState,
        bool            compress,
        const wchar_t** userExclusions,
        int             userExclusionCount,
        ProgressCallback callback,
        LogCallback      logCallback);

    BACKUPENGINE_API int CreateIncrementalBackup(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t* baseBackupPath,
        ProgressCallback callback);

    BACKUPENGINE_API int CreateDifferentialBackup(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t* fullBackupPath,
        ProgressCallback callback);

    BACKUPENGINE_API int BackupHyperVVM(
        const wchar_t* vmName,
        const wchar_t* destPath,
        ProgressCallback callback);

    BACKUPENGINE_API int BackupHyperVVMIncremental(
        const wchar_t* vmName,
        const wchar_t* destPath,
        ProgressCallback callback);

    BACKUPENGINE_API int BackupHyperVVMDifferential(
        const wchar_t* vmName,
        const wchar_t* destPath,
        ProgressCallback callback);

    BACKUPENGINE_API int DeleteSnapshot(
        const wchar_t* snapshotId);

    // =========================================================================
    // EXISTING RESTORE FUNCTIONS  (unchanged)
    // =========================================================================

    BACKUPENGINE_API int RestoreFiles(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        bool             overwriteExisting,
        ProgressCallback callback);

    BACKUPENGINE_API int RestoreVolume(
        const wchar_t* backupPath,
        const wchar_t* targetVolume,
        bool             restoreSystemState,
        ProgressCallback callback);

    BACKUPENGINE_API int RestoreDisk(
        const wchar_t* backupPath,
        int              targetDiskNumber,
        bool             restoreSystemState,
        ProgressCallback callback);

    BACKUPENGINE_API int RestoreHyperVVM(
        const wchar_t* backupPath,
        const wchar_t* vmName,
        const wchar_t* vmStoragePath,
        bool             startAfterRestore,
        ProgressCallback callback);

    BACKUPENGINE_API int RestoreSystemState(
        const wchar_t* backupPath,
        const wchar_t* targetVolume,
        ProgressCallback callback);

    BACKUPENGINE_API int RestoreBootDiskAsHyperV(
        const wchar_t* backupPath,
        const wchar_t* vmName,
        const wchar_t* vmStoragePath,
        bool             startAfterRestore,
        ProgressCallback callback);

    BACKUPENGINE_API int ScheduleOfflineSystemSetupCl(
        const wchar_t* systemHivePath);

    // =========================================================================
    // EXISTING VERIFICATION & UTILITY FUNCTIONS  (unchanged)
    // =========================================================================

    BACKUPENGINE_API int ListBackupContents(
        const wchar_t* backupPath,
        wchar_t* buffer,
        int            bufferSize);

    BACKUPENGINE_API int VerifyBackup(
        const wchar_t* backupPath,
        ProgressCallback callback);

    BACKUPENGINE_API int VerifyWimArchive(
        const wchar_t* archivePath,
        int              expectedImageCount,
        wchar_t* errorMsg,
        int              errorMsgSize,
        ProgressCallback callback);

    BACKUPENGINE_API int CheckBackupImageHealth(
        const wchar_t* backupPath,
        int              imageIndex,
        bool             scanImage,
        wchar_t* healthMessage,
        int              healthMessageSize,
        ProgressCallback callback);

    BACKUPENGINE_API int RestoreBackupImageHealth(
        const wchar_t* backupPath,
        int              imageIndex,
        const wchar_t** sourcePaths,
        int              sourcePathCount,
        bool             limitAccess,
        wchar_t* healthMessage,
        int              healthMessageSize,
        ProgressCallback callback);

    BACKUPENGINE_API int EnumerateVolumes(
        wchar_t* buffer,
        int      bufferSize);

    BACKUPENGINE_API int EnumerateDisks(
        wchar_t* buffer,
        int      bufferSize);

    BACKUPENGINE_API int EnumerateHyperVMachines(
        wchar_t* buffer,
        int      bufferSize);

    BACKUPENGINE_API int EnumerateHyperVVirtualMachineDisks(
        wchar_t* buffer,
        int      bufferSize);

    BACKUPENGINE_API int IsBootVolume(
        const wchar_t* volumePath,
        bool* isBootVolume);

    BACKUPENGINE_API int GetBackupInfo(
        const wchar_t* backupPath,
        wchar_t* buffer,
        int            bufferSize);

    // =========================================================================
    // EXISTING RECOVERY ENVIRONMENT FUNCTIONS  (unchanged)
    // =========================================================================

    BACKUPENGINE_API int CreateRecoveryEnvironment(
        const wchar_t* usbDriveLetter,
        const wchar_t* programPath,
        ProgressCallback callback);

    BACKUPENGINE_API int InstallRecoveryBootFiles(
        const wchar_t* usbDriveLetter,
        ProgressCallback callback);

    // =========================================================================
    // EXISTING ERROR HANDLING & VERSION  (unchanged)
    // =========================================================================

    BACKUPENGINE_API void GetLastErrorMessage(
        wchar_t* buffer,
        int      bufferSize);

    BACKUPENGINE_API int GetWindowsVersion(
        int* major,
        int* minor,
        int* build);

    // =========================================================================
    // EXISTING JOB CONTEXT FUNCTIONS  (unchanged)
    // =========================================================================

    BACKUPENGINE_API void SetCurrentJobName(const wchar_t* jobName);

    BACKUPENGINE_API void ClearCurrentJobName();

    // =========================================================================
    // EXISTING ENHANCED RESTORE FUNCTIONS v4.7.0.0  (unchanged)
    // =========================================================================

    BACKUPENGINE_API int EnumerateBackupDates(
        const wchar_t* backupPath,
        wchar_t* buffer,
        int            bufferSize);

    BACKUPENGINE_API int RestoreWithManifest(
        const wchar_t* backupPath,
        const wchar_t* destPath,
        const wchar_t* manifest,
        bool             overwriteExisting,
        bool             restoreSystemState,
        bool             preservePermissions,
        ProgressCallback callback);

    // =========================================================================
    // NEW — HVE LIFECYCLE
    // =========================================================================

    // Must be called once before any HVE_* function.
    // Initialises COM, WMI connection, and internal engine state.
    BACKUPENGINE_API HVE_RESULT HVE_Initialize();

    // Must be called on shutdown to release COM and WMI resources.
    BACKUPENGINE_API HVE_RESULT HVE_Shutdown();

    // Returns a static version string — do NOT free this pointer.
    BACKUPENGINE_API const wchar_t* HVE_GetEngineVersion();

    // =========================================================================
    // NEW — HVE CALLBACK REGISTRATION
    // Call before any HVE operation so the engine can report back to C#.
    // userState is an opaque pointer — pass your GCHandle.ToIntPtr() from C#.
    // =========================================================================

    BACKUPENGINE_API void HVE_SetProgressCallback(
        HVE_ProgressCallback cb,
        void* userState);

    BACKUPENGINE_API void HVE_SetStatusCallback(
        HVE_StatusCallback cb,
        void* userState);

    // =========================================================================
    // NEW — HVE CLONE OPERATIONS
    // =========================================================================

    // Full VM clone: export → consolidate AVHDX chain → import with new GUID.
    // Writes a recovery checkpoint if params->checkpointPath is non-null.
    BACKUPENGINE_API HVE_RESULT HVE_CloneVM(
        const HVE_CloneVMParams* params);

    // Standalone disk clone: copies a VHDX/AVHDX chain to a new consolidated VHDX.
    BACKUPENGINE_API HVE_RESULT HVE_CloneDisk(
        const HVE_CloneDiskParams* params);

    // Consolidate all AVHDX differencing disks in exportPath into a single VHDX
    // written to outputVhdxPath.
    BACKUPENGINE_API HVE_RESULT HVE_ConsolidateAVHDX(
        const wchar_t* exportPath,
        const wchar_t* outputVhdxPath);

    // Import an exported/consolidated VM into Hyper-V Manager.
    BACKUPENGINE_API HVE_RESULT HVE_ImportVM(
        const wchar_t* exportPath,
        const wchar_t* targetPath,
        bool           generateNewId);

    // =========================================================================
    // NEW — HVE DISK CHAIN QUERY
    // Two-call pattern: first call with outEntries=nullptr to get count,
    // then allocate and call again to fill entries.
    // =========================================================================

    BACKUPENGINE_API HVE_RESULT HVE_GetDiskChain(
        const wchar_t* vmPath,
        HVE_DiskEntry* outEntries,  // pass nullptr first to get count only
        int32_t* outCount);

    // =========================================================================
    // NEW — HVE POST-CLONE VERIFICATION
    // Runs up to 11 checks (VHDX integrity, chain, VMCX, WIM, boot test, etc.)
    // and returns a full report. outReport is filled even on partial failure.
    // =========================================================================

    BACKUPENGINE_API HVE_RESULT HVE_VerifyClone(
        const HVE_VerifyParams* params,
        HVE_VerifyReport* outReport);

    // =========================================================================
    // NEW — HVE CONTROL & ERROR
    // =========================================================================

    // Signal the engine to abort the current HVE operation at the next safe point.
    BACKUPENGINE_API void HVE_CancelOperation();

    // Returns a heap-allocated wchar_t* describing the last HVE error.
    // MUST be freed with HVE_FreeStringHVE() after use — never pass to free().
    BACKUPENGINE_API const wchar_t* HVE_GetLastError();

    // Free a string returned by HVE_GetLastError().
    BACKUPENGINE_API void HVE_FreeStringHVE(const wchar_t* str);

} // extern "C"