// BackupEngine_Exports.cpp - Main export implementations
// This file contains the exported C functions that interface with C#

#include "BackupEngine.h"
#include <Windows.h>
#include <string>
#include <thread>

// Thread-local error storage
thread_local std::wstring g_lastError;

void SetLastErrorMessage(const std::wstring& error) {
    g_lastError = error;
}

extern "C" {

// Get last error message
BACKUPENGINE_API void GetLastErrorMessage(wchar_t* buffer, int bufferSize) {
    if (buffer && bufferSize > 0) {
        wcsncpy_s(buffer, bufferSize, g_lastError.c_str(), _TRUNCATE);
    }
}

    // Get Windows version
    BACKUPENGINE_API int GetWindowsVersion(int* major, int* minor, int* build) {
        if (!major || !minor || !build) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        OSVERSIONINFOEXW osvi = { sizeof(osvi) };
        NTSTATUS(WINAPI * RtlGetVersion)(LPOSVERSIONINFOEXW);
        
        *(FARPROC*)&RtlGetVersion = GetProcAddress(GetModuleHandleA("ntdll"), "RtlGetVersion");

        if (RtlGetVersion && RtlGetVersion(&osvi) == 0) {
            *major = osvi.dwMajorVersion;
            *minor = osvi.dwMinorVersion;
            *build = osvi.dwBuildNumber;
            return 0;
        }

        SetLastErrorMessage(L"Failed to get Windows version");
        return -1;
    }

    // Backup volume - implementation in BackupManager.cpp
    BACKUPENGINE_API int BackupVolume(
        const wchar_t* volumePath,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        ProgressCallback callback);

    // Backup disk - implementation in BackupManager.cpp
    BACKUPENGINE_API int BackupDisk(
        int diskNumber,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        ProgressCallback callback);

    // Create incremental backup - implementation in BackupManager.cpp
    BACKUPENGINE_API int CreateIncrementalBackup(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t* baseBackupPath,
        ProgressCallback callback);

    // Create differential backup - implementation in BackupManager.cpp
    BACKUPENGINE_API int CreateDifferentialBackup(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t* fullBackupPath,
        ProgressCallback callback);

    // Restore volume - implementation in RestoreEngine.cpp
    BACKUPENGINE_API int RestoreVolume(
        const wchar_t* backupPath,
        const wchar_t* targetVolume,
        bool restoreSystemState,
        ProgressCallback callback);

    // Restore disk - implementation in RestoreEngine.cpp
    BACKUPENGINE_API int RestoreDisk(
        const wchar_t* backupPath,
        int targetDiskNumber,
        bool restoreSystemState,
        ProgressCallback callback);

    // Restore boot disk as Hyper-V - implementation in HyperVRestore.cpp
    BACKUPENGINE_API int RestoreBootDiskAsHyperV(
        const wchar_t* backupPath,
        const wchar_t* vmName,
        const wchar_t* vmStoragePath,
        bool startAfterRestore,
        ProgressCallback callback);

    // Get backup info - implementation in BackupVerification.cpp
    BACKUPENGINE_API int GetBackupInfo(
        const wchar_t* backupPath,
        wchar_t* buffer,
        int bufferSize);

    // Enumerate volumes - implementation in VSSManager.cpp
    BACKUPENGINE_API int EnumerateVolumes(
        wchar_t* buffer,
        int bufferSize);

    // Enumerate disks - implementation in VSSManager.cpp
    BACKUPENGINE_API int EnumerateDisks(
        wchar_t* buffer,
        int bufferSize);

    // Check if volume is boot volume - implementation in VSSManager.cpp
    BACKUPENGINE_API int IsBootVolume(
        const wchar_t* volumePath,
        bool* isBootVolume);

    // Install recovery boot files - implementation in RecoveryEnvironment.cpp
    BACKUPENGINE_API int InstallRecoveryBootFiles(
        const wchar_t* usbDriveLetter,
        ProgressCallback callback);

    // Create recovery environment - implementation in RecoveryEnvironment.cpp
    BACKUPENGINE_API int CreateRecoveryEnvironment(
        const wchar_t* usbDriveLetter,
        const wchar_t* programPath,
        ProgressCallback callback);
}
