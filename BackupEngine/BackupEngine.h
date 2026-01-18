// BackupEngine.h - Main API for Backup & Restore operations
// All core backup/restore logic is implemented in C++ for performance and IP protection

#pragma once

#ifdef BACKUPENGINE_EXPORTS
#define BACKUPENGINE_API __declspec(dllexport)
#else
#define BACKUPENGINE_API __declspec(dllimport)
#endif

extern "C" {
    // Progress callback for UI updates
    typedef void (*ProgressCallback)(int percentage, const wchar_t* message);
    
    // Backup Functions
    BACKUPENGINE_API int CreateVolumeSnapshot(const wchar_t* volume, wchar_t* snapshotPath, int pathSize);
    BACKUPENGINE_API int DeleteSnapshot(const wchar_t* snapshotId);
    BACKUPENGINE_API int BackupFiles(const wchar_t* sourcePath, const wchar_t* destPath, ProgressCallback callback);
    BACKUPENGINE_API int BackupHyperVVM(const wchar_t* vmName, const wchar_t* destPath, ProgressCallback callback);
    
    // Restore Functions
    BACKUPENGINE_API int RestoreFiles(const wchar_t* sourcePath, const wchar_t* destPath, bool overwriteExisting, ProgressCallback callback);
    BACKUPENGINE_API int RestoreHyperVVM(const wchar_t* backupPath, const wchar_t* vmName, const wchar_t* vmStoragePath, bool startAfterRestore, ProgressCallback callback);
    BACKUPENGINE_API int RestoreSystemState(const wchar_t* backupPath, const wchar_t* targetVolume, ProgressCallback callback);
    
    // Verification Functions
    BACKUPENGINE_API int VerifyBackup(const wchar_t* backupPath, ProgressCallback callback);
    BACKUPENGINE_API int ListBackupContents(const wchar_t* backupPath, wchar_t* buffer, int bufferSize);
    
    // Error Handling
    BACKUPENGINE_API void GetLastError(wchar_t* buffer, int bufferSize);
}
