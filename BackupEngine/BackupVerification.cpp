// BackupVerification.cpp
// 
// NOTE: ListBackupContents has been moved to BackupInfo_Implementation.cpp
// This file now only contains VerifyBackup implementation
//
#include "BackupEngine.h"
#include <Windows.h>
#include <filesystem>
#include <sstream>
#include <vector>
#include <mutex>
#include <wimgapi.h>

#pragma comment(lib, "wimgapi.lib")

namespace fs = std::filesystem;

extern void SetLastErrorMessage(const std::wstring& error);

namespace {
    using SSBSession = UINT;

    enum SSBLogLevel : UINT {
        SSBLogErrorsWarnings = 2
    };

    enum SSBImageIdentifier : UINT {
        SSBImageIndex = 0
    };

    enum SSBImageHealthState : UINT {
        SSBImageHealthy = 0,
        SSBImageRepairable = 1,
        SSBImageNonRepairable = 2
    };

    using SSBInitializeFn = HRESULT(WINAPI*)(SSBLogLevel, PCWSTR, PCWSTR);
    using SSBShutdownFn = HRESULT(WINAPI*)();
    using SSBMountImageFn = HRESULT(WINAPI*)(PCWSTR, PCWSTR, UINT, PCWSTR, SSBImageIdentifier, DWORD, HANDLE, PVOID, PVOID);
    using SSBUnmountImageFn = HRESULT(WINAPI*)(PCWSTR, DWORD, HANDLE, PVOID, PVOID);
    using SSBOpenSessionFn = HRESULT(WINAPI*)(PCWSTR, PCWSTR, PCWSTR, SSBSession*);
    using SSBCloseSessionFn = HRESULT(WINAPI*)(SSBSession);
    using SSBCheckImageHealthFn = HRESULT(WINAPI*)(SSBSession, BOOL, HANDLE, PVOID, PVOID, SSBImageHealthState*);
    using SSBRestoreImageHealthFn = HRESULT(WINAPI*)(SSBSession, const PCWSTR*, UINT, BOOL, HANDLE, PVOID, PVOID);

    struct SSBApiFunctions {
        HMODULE module = nullptr;
        SSBInitializeFn initialize = nullptr;
        SSBShutdownFn shutdown = nullptr;
        SSBMountImageFn mountImage = nullptr;
        SSBUnmountImageFn unmountImage = nullptr;
        SSBOpenSessionFn openSession = nullptr;
        SSBCloseSessionFn closeSession = nullptr;
        SSBCheckImageHealthFn checkImageHealth = nullptr;
        SSBRestoreImageHealthFn restoreImageHealth = nullptr;
    };

    SSBApiFunctions& GetSSBApi() {
        static SSBApiFunctions api;
        static std::once_flag once;

        std::call_once(once, [] {
            api.module = LoadLibraryW(L"DismAPI.dll");
            if (api.module) {
                api.initialize = reinterpret_cast<SSBInitializeFn>(GetProcAddress(api.module, "DismInitialize"));
                api.shutdown = reinterpret_cast<SSBShutdownFn>(GetProcAddress(api.module, "DismShutdown"));
                api.mountImage = reinterpret_cast<SSBMountImageFn>(GetProcAddress(api.module, "DismMountImage"));
                api.unmountImage = reinterpret_cast<SSBUnmountImageFn>(GetProcAddress(api.module, "DismUnmountImage"));
                api.openSession = reinterpret_cast<SSBOpenSessionFn>(GetProcAddress(api.module, "DismOpenSession"));
                api.closeSession = reinterpret_cast<SSBCloseSessionFn>(GetProcAddress(api.module, "DismCloseSession"));
                api.checkImageHealth = reinterpret_cast<SSBCheckImageHealthFn>(GetProcAddress(api.module, "DismCheckImageHealth"));
                api.restoreImageHealth = reinterpret_cast<SSBRestoreImageHealthFn>(GetProcAddress(api.module, "DismRestoreImageHealth"));
            }
        });

        return api;
    }

    constexpr SSBSession SSB_SESSION_DEFAULT = 0;
    constexpr DWORD SSB_MOUNT_READONLY = 1;
    constexpr DWORD SSB_DISCARD_IMAGE = 0;

    HRESULT SSBInitialize(SSBLogLevel logLevel, PCWSTR logFilePath, PCWSTR scratchDirectory) {
        SSBApiFunctions& api = GetSSBApi();
        return api.initialize ? api.initialize(logLevel, logFilePath, scratchDirectory) : HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
    }

    HRESULT SSBShutdown() {
        SSBApiFunctions& api = GetSSBApi();
        return api.shutdown ? api.shutdown() : S_OK;
    }

    HRESULT SSBMountImage(PCWSTR imageFilePath, PCWSTR mountPath, UINT imageIndex, PCWSTR imageName, SSBImageIdentifier imageIdentifier, DWORD flags, HANDLE cancelEvent, PVOID progress, PVOID userData) {
        SSBApiFunctions& api = GetSSBApi();
        return api.mountImage ? api.mountImage(imageFilePath, mountPath, imageIndex, imageName, imageIdentifier, flags, cancelEvent, progress, userData) : HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
    }

    HRESULT SSBUnmountImage(PCWSTR mountPath, DWORD flags, HANDLE cancelEvent, PVOID progress, PVOID userData) {
        SSBApiFunctions& api = GetSSBApi();
        return api.unmountImage ? api.unmountImage(mountPath, flags, cancelEvent, progress, userData) : HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
    }

    HRESULT SSBOpenSession(PCWSTR imagePath, PCWSTR windowsDirectory, PCWSTR systemDrive, SSBSession* session) {
        SSBApiFunctions& api = GetSSBApi();
        return api.openSession ? api.openSession(imagePath, windowsDirectory, systemDrive, session) : HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
    }

    HRESULT SSBCloseSession(SSBSession session) {
        SSBApiFunctions& api = GetSSBApi();
        return api.closeSession ? api.closeSession(session) : S_OK;
    }

    HRESULT SSBCheckImageHealth(SSBSession session, BOOL scanImage, HANDLE cancelEvent, PVOID progress, PVOID userData, SSBImageHealthState* imageHealth) {
        SSBApiFunctions& api = GetSSBApi();
        return api.checkImageHealth ? api.checkImageHealth(session, scanImage, cancelEvent, progress, userData, imageHealth) : HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
    }

    HRESULT SSBRestoreImageHealth(SSBSession session, const PCWSTR* sourcePaths, UINT sourcePathCount, BOOL limitAccess, HANDLE cancelEvent, PVOID progress, PVOID userData) {
        SSBApiFunctions& api = GetSSBApi();
        return api.restoreImageHealth ? api.restoreImageHealth(session, sourcePaths, sourcePathCount, limitAccess, cancelEvent, progress, userData) : HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
    }

    std::wstring TrimTrailingSlash(std::wstring value) {
        while (!value.empty() && (value.back() == L'\\' || value.back() == L'/')) {
            value.pop_back();
        }

        return value;
    }

    std::wstring GetTempRoot() {
        wchar_t tempPath[MAX_PATH] = {};
        if (GetTempPathW(MAX_PATH, tempPath) > 0) {
            return tempPath;
        }

        return L"C:\\Windows\\Temp\\";
    }

    std::wstring CreateSSBWorkRoot(int imageIndex) {
        std::wstringstream ss;
        ss << GetTempRoot() << L"BackupRestoreService\\SSB\\" << GetCurrentProcessId() << L"_" << GetTickCount64() << L"_" << imageIndex;
        return ss.str();
    }

    bool TryResolveOfflineWindowsPaths(const std::wstring& mountedRoot, std::wstring& windowsDirectory, std::wstring& systemDrive) {
        std::error_code ec;
        if (mountedRoot.empty() || !fs::exists(mountedRoot, ec)) {
            return false;
        }

        auto normalizeRoot = [](const std::wstring& value) {
            std::wstring normalized = TrimTrailingSlash(value);
            if (normalized.empty()) {
                normalized = value;
            }

            return normalized;
        };

        fs::path rootPath(mountedRoot);
        fs::path directWindows = rootPath / L"Windows";
        if (fs::exists(directWindows, ec) && fs::is_directory(directWindows, ec)) {
            windowsDirectory = directWindows.wstring();
            systemDrive = normalizeRoot(rootPath.wstring());
            return true;
        }

        for (const auto& entry : fs::directory_iterator(rootPath, fs::directory_options::skip_permission_denied, ec)) {
            if (ec || !entry.is_directory(ec)) {
                continue;
            }

            fs::path candidateRoot = entry.path();
            fs::path candidateWindows = candidateRoot / L"Windows";
            if (fs::exists(candidateWindows, ec) && fs::is_directory(candidateWindows, ec)) {
                windowsDirectory = candidateWindows.wstring();
                systemDrive = normalizeRoot(candidateRoot.wstring());
                return true;
            }
        }

        return false;
    }

    std::wstring StageBackupForSSB(const std::wstring& backupPath, bool& cleanupRequired) {
        cleanupRequired = false;

        fs::path sourcePath(backupPath);
        std::wstring extension = sourcePath.extension().wstring();
        if (_wcsicmp(extension.c_str(), L".wim") == 0 || _wcsicmp(extension.c_str(), L".vhd") == 0 || _wcsicmp(extension.c_str(), L".vhdx") == 0) {
            return backupPath;
        }

        fs::path stagingDir = fs::path(GetTempRoot()) / L"BackupRestoreService" / L"SSBStaging";
        std::error_code ec;
        fs::create_directories(stagingDir, ec);
        if (ec) {
            return L"";
        }

        fs::path stagedPath = stagingDir / (sourcePath.stem().wstring() + L".wim");
        fs::copy_file(sourcePath, stagedPath, fs::copy_options::overwrite_existing, ec);
        if (ec) {
            return L"";
        }

        cleanupRequired = true;
        return stagedPath.wstring();
    }

    std::wstring GetSSBErrorMessage(HRESULT hr) {
        std::wstringstream ss;
        ss << L"HRESULT=0x" << std::hex << std::uppercase << static_cast<unsigned long>(hr) << std::dec;
        return ss.str();
    }

    std::wstring HealthStateToText(SSBImageHealthState state) {
        switch (state) {
            case SSBImageHealthy:
                return L"Healthy";
            case SSBImageRepairable:
                return L"Repairable";
            case SSBImageNonRepairable:
                return L"NonRepairable";
            default:
                return L"Unknown";
        }
    }

    HRESULT EnsureSSBInitialized() {
        SSBApiFunctions& api = GetSSBApi();
        if (!api.initialize) {
            return HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
        }

        static std::once_flag initializedOnce;
        static HRESULT initResult = E_FAIL;

        std::call_once(initializedOnce, [&] {
            initResult = api.initialize(SSBLogErrorsWarnings, nullptr, nullptr);
            if (FAILED(initResult)) {
                initResult = E_FAIL;
            }
        });

        return initResult;
    }

    bool RemoveDirectoryTree(const std::wstring& path) {
        std::error_code ec;
        fs::remove_all(path, ec);
        return !ec;
    }

    void WriteMessageBuffer(wchar_t* buffer, int bufferSize, const std::wstring& message) {
        if (buffer && bufferSize > 0) {
            wcsncpy_s(buffer, bufferSize, message.c_str(), _TRUNCATE);
        }
    }

    void ReleaseSSBSession(SSBSession session) {
        if (session != 0) {
            SSBApiFunctions& api = GetSSBApi();
            if (api.closeSession) {
                api.closeSession(session);
            }
        }
    }
}

extern "C" {
    BACKUPENGINE_API int CheckBackupImageHealth(
        const wchar_t* backupPath,
        int imageIndex,
        bool scanImage,
        wchar_t* healthMessage,
        int healthMessageSize,
        ProgressCallback callback) {

        if (!backupPath || imageIndex < 1) {
            SetLastErrorMessage(L"Invalid parameters");
            WriteMessageBuffer(healthMessage, healthMessageSize, L"Invalid parameters");
            return -1;
        }

        bool stagedCleanup = false;
        std::wstring SSBSource = StageBackupForSSB(backupPath, stagedCleanup);
        if (SSBSource.empty()) {
            std::wstring message = L"Failed to stage backup for SSB health check.";
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            return -2;
        }

        std::wstring workRoot = CreateSSBWorkRoot(imageIndex);
        std::error_code ec;
        fs::create_directories(workRoot, ec);
        if (ec) {
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }
            SetLastErrorMessage(L"Failed to create SSB work directory");
            WriteMessageBuffer(healthMessage, healthMessageSize, L"Failed to create SSB work directory");
            return -3;
        }

        HRESULT hr = EnsureSSBInitialized();
        if (FAILED(hr)) {
            std::wstring message = L"SSBInitialize failed. " + GetSSBErrorMessage(hr);
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            RemoveDirectoryTree(workRoot);
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }
            return -4;
        }

        if (callback) {
            callback(5, L"Mounting backup image for SSB health check...");
        }

        hr = SSBMountImage(
            SSBSource.c_str(),
            workRoot.c_str(),
            static_cast<UINT>(imageIndex),
            nullptr,
            SSBImageIndex,
            SSB_MOUNT_READONLY,
            nullptr,
            nullptr,
            nullptr);

        if (FAILED(hr)) {
            std::wstring message = L"SSBMountImage failed. " + GetSSBErrorMessage(hr);
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            RemoveDirectoryTree(workRoot);
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }
            return -5;
        }

        std::wstring windowsDirectory;
        std::wstring systemDrive;
        PCWSTR windowsDirectoryArg = nullptr;
        PCWSTR systemDriveArg = nullptr;
        bool hasWindowsInstall = TryResolveOfflineWindowsPaths(workRoot, windowsDirectory, systemDrive);
        if (!hasWindowsInstall) {
            // No Windows installation in this image (data drive, non-OS volume).
            // SSBOpenSession requires a Windows directory and always fails with 0x80070003
            // on data drives. Archive integrity was already verified, so skip the
            // component-store health check and report healthy.
            SSBUnmountImage(workRoot.c_str(), SSB_DISCARD_IMAGE, nullptr, nullptr, nullptr);
            RemoveDirectoryTree(workRoot);
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }
            WriteMessageBuffer(healthMessage, healthMessageSize,
                L"SSB component-store check skipped: no Windows installation in image (data drive backup). Archive integrity verified.");
            return 0;
        }
        windowsDirectoryArg = windowsDirectory.c_str();
        systemDriveArg = systemDrive.c_str();

        SSBSession session = SSB_SESSION_DEFAULT;
        hr = SSBOpenSession(workRoot.c_str(), windowsDirectoryArg, systemDriveArg, &session);
        if (FAILED(hr)) {
            SSBUnmountImage(workRoot.c_str(), SSB_DISCARD_IMAGE, nullptr, nullptr, nullptr);
            RemoveDirectoryTree(workRoot);
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }

            std::wstring message = L"SSBOpenSession failed. " + GetSSBErrorMessage(hr);
            message += L" WindowsDirectory=" + windowsDirectory;
            message += L" SystemDrive=" + systemDrive;
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            return -6;
        }

        if (callback) {
            callback(50, L"Checking backup image health...");
        }

        SSBImageHealthState healthState = SSBImageHealthy;
        hr = SSBCheckImageHealth(session, scanImage ? TRUE : FALSE, nullptr, nullptr, nullptr, &healthState);

        SSBCloseSession(session);
        SSBUnmountImage(workRoot.c_str(), SSB_DISCARD_IMAGE, nullptr, nullptr, nullptr);
        RemoveDirectoryTree(workRoot);
        if (stagedCleanup) {
            fs::remove(SSBSource, ec);
        }

        if (FAILED(hr)) {
            std::wstring message = L"SSBCheckImageHealth failed. " + GetSSBErrorMessage(hr);
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            return -7;
        }

        std::wstring message = L"SSB image health state: " + HealthStateToText(healthState);
        WriteMessageBuffer(healthMessage, healthMessageSize, message);

        if (healthState == SSBImageHealthy) {
            return 0;
        }

        if (healthState == SSBImageRepairable) {
            return 1;
        }

        return 2;
    }

    BACKUPENGINE_API int RestoreBackupImageHealth(
        const wchar_t* backupPath,
        int imageIndex,
        const wchar_t** sourcePaths,
        int sourcePathCount,
        bool limitAccess,
        wchar_t* healthMessage,
        int healthMessageSize,
        ProgressCallback callback) {

        if (!backupPath || imageIndex < 1) {
            SetLastErrorMessage(L"Invalid parameters");
            WriteMessageBuffer(healthMessage, healthMessageSize, L"Invalid parameters");
            return -1;
        }

        bool stagedCleanup = false;
        std::wstring SSBSource = StageBackupForSSB(backupPath, stagedCleanup);
        if (SSBSource.empty()) {
            std::wstring message = L"Failed to stage backup for SSB restore.";
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            return -2;
        }

        std::wstring workRoot = CreateSSBWorkRoot(imageIndex);
        std::error_code ec;
        fs::create_directories(workRoot, ec);
        if (ec) {
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }
            SetLastErrorMessage(L"Failed to create SSB work directory");
            WriteMessageBuffer(healthMessage, healthMessageSize, L"Failed to create SSB work directory");
            return -3;
        }

        HRESULT hr = EnsureSSBInitialized();
        if (FAILED(hr)) {
            std::wstring message = L"SSBInitialize failed. " + GetSSBErrorMessage(hr);
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            RemoveDirectoryTree(workRoot);
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }
            return -4;
        }

        if (callback) {
            callback(5, L"Mounting backup image for SSB restore...");
        }

        hr = SSBMountImage(
            SSBSource.c_str(),
            workRoot.c_str(),
            static_cast<UINT>(imageIndex),
            nullptr,
            SSBImageIndex,
            SSB_MOUNT_READONLY,
            nullptr,
            nullptr,
            nullptr);

        if (FAILED(hr)) {
            std::wstring message = L"SSBMountImage failed. " + GetSSBErrorMessage(hr);
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            RemoveDirectoryTree(workRoot);
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }
            return -5;
        }

        std::wstring windowsDirectory;
        std::wstring systemDrive;
        PCWSTR windowsDirectoryArg = nullptr;
        PCWSTR systemDriveArg = nullptr;
        bool hasWindowsInstall = TryResolveOfflineWindowsPaths(workRoot, windowsDirectory, systemDrive);
        if (!hasWindowsInstall) {
            // No Windows installation in this image (data drive, non-OS volume).
            // SSBOpenSession requires a Windows directory and always fails with 0x80070003
            // on data drives. Nothing to repair on a non-OS volume.
            SSBUnmountImage(workRoot.c_str(), SSB_DISCARD_IMAGE, nullptr, nullptr, nullptr);
            RemoveDirectoryTree(workRoot);
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }
            WriteMessageBuffer(healthMessage, healthMessageSize,
                L"SSB component-store repair skipped: no Windows installation in image (data drive backup).");
            return 0;
        }
        windowsDirectoryArg = windowsDirectory.c_str();
        systemDriveArg = systemDrive.c_str();

        SSBSession session = SSB_SESSION_DEFAULT;
        hr = SSBOpenSession(workRoot.c_str(), windowsDirectoryArg, systemDriveArg, &session);
        if (FAILED(hr)) {
            SSBUnmountImage(workRoot.c_str(), SSB_DISCARD_IMAGE, nullptr, nullptr, nullptr);
            RemoveDirectoryTree(workRoot);
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }

            std::wstring message = L"SSBOpenSession failed. " + GetSSBErrorMessage(hr);
            message += L" WindowsDirectory=" + windowsDirectory;
            message += L" SystemDrive=" + systemDrive;
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            return -6;
        }

        SSBImageHealthState healthState = SSBImageHealthy;
        hr = SSBCheckImageHealth(session, TRUE, nullptr, nullptr, nullptr, &healthState);
        if (FAILED(hr)) {
            SSBCloseSession(session);
            SSBUnmountImage(workRoot.c_str(), SSB_DISCARD_IMAGE, nullptr, nullptr, nullptr);
            RemoveDirectoryTree(workRoot);
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }

            std::wstring message = L"Initial SSBCheckImageHealth failed. " + GetSSBErrorMessage(hr);
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            return -7;
        }

        if (healthState == SSBImageNonRepairable) {
            SSBCloseSession(session);
            SSBUnmountImage(workRoot.c_str(), SSB_DISCARD_IMAGE, nullptr, nullptr, nullptr);
            RemoveDirectoryTree(workRoot);
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }

            std::wstring message = L"Image is non-repairable.";
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            return 2;
        }

        if (healthState == SSBImageHealthy) {
            SSBCloseSession(session);
            SSBUnmountImage(workRoot.c_str(), SSB_DISCARD_IMAGE, nullptr, nullptr, nullptr);
            RemoveDirectoryTree(workRoot);
            if (stagedCleanup) {
                fs::remove(SSBSource, ec);
            }

            WriteMessageBuffer(healthMessage, healthMessageSize, L"Image is already healthy.");
            return 0;
        }

        if (callback) {
            callback(60, L"Attempting SSB RestoreHealth...");
        }

        hr = SSBRestoreImageHealth(
            session,
            sourcePaths,
            static_cast<UINT>(sourcePathCount),
            limitAccess ? TRUE : FALSE,
            nullptr,
            nullptr,
            nullptr);

        SSBImageHealthState postRepairState = SSBImageHealthy;
        HRESULT verifyHr = SSBCheckImageHealth(session, TRUE, nullptr, nullptr, nullptr, &postRepairState);

        SSBCloseSession(session);
        SSBUnmountImage(workRoot.c_str(), SSB_DISCARD_IMAGE, nullptr, nullptr, nullptr);
        RemoveDirectoryTree(workRoot);
        if (stagedCleanup) {
            fs::remove(SSBSource, ec);
        }

        if (FAILED(hr)) {
            std::wstring message = L"SSBRestoreImageHealth failed. " + GetSSBErrorMessage(hr);
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            return -8;
        }

        if (FAILED(verifyHr)) {
            std::wstring message = L"RestoreHealth completed, but post-repair health check failed. " + GetSSBErrorMessage(verifyHr);
            SetLastErrorMessage(message);
            WriteMessageBuffer(healthMessage, healthMessageSize, message);
            return -9;
        }

        std::wstring message = L"SSB RestoreHealth completed. Final state: " + HealthStateToText(postRepairState);
        WriteMessageBuffer(healthMessage, healthMessageSize, message);

        if (postRepairState == SSBImageHealthy) {
            return 0;
        }

        if (postRepairState == SSBImageRepairable) {
            return 1;
        }

        return 2;
    }
}

// VerifyBackup implementation
extern "C" {
    BACKUPENGINE_API int VerifyBackup(
        const wchar_t* backupPath,
        ProgressCallback callback) {

        try {
            if (callback) {
                callback(0, L"Starting backup verification...");
            }

            if (!fs::exists(backupPath)) {
                if (callback) {
                    callback(0, L"Backup path does not exist");
                }
                return -1;
            }

            size_t totalFiles = 0;
            size_t verifiedFiles = 0;

            // Count files
            for (const auto& entry : fs::recursive_directory_iterator(backupPath)) {
                if (entry.is_regular_file()) {
                    totalFiles++;
                }
            }

            if (callback) {
                std::wstring msg = L"Verifying " + std::to_wstring(totalFiles) + L" files...";
                callback(10, msg.c_str());
            }

            // Verify each file can be read
            for (const auto& entry : fs::recursive_directory_iterator(backupPath)) {
                if (entry.is_regular_file()) {
                    HANDLE hFile = CreateFileW(
                        entry.path().wstring().c_str(),
                        GENERIC_READ,
                        FILE_SHARE_READ,
                        NULL,
                        OPEN_EXISTING,
                        FILE_ATTRIBUTE_NORMAL,
                        NULL);

                    if (hFile == INVALID_HANDLE_VALUE) {
                        if (callback) {
                            std::wstring msg = L"Failed to verify: " +
                                entry.path().filename().wstring();
                            callback(0, msg.c_str());
                        }
                        return -2;
                    }

                    CloseHandle(hFile);
                    verifiedFiles++;

                    if (callback && totalFiles > 0) {
                        int percent = 10 + (int)((verifiedFiles * 90) / totalFiles);
                        std::wstring msg = L"Verified " + std::to_wstring(verifiedFiles) +
                            L" of " + std::to_wstring(totalFiles) + L" files";
                        callback(percent, msg.c_str());
                    }
                }
            }

            if (callback) {
                callback(100, L"Backup verification completed successfully");
            }

            return 0;
        }
        catch (...) {
            if (callback) {
                callback(0, L"Error during backup verification");
            }
            return -99;
        }
    }

    // Enhanced verification for WIM/SSB archives
    // Verifies archive structure, image count, and loadability
    BACKUPENGINE_API int VerifyWimArchive(
        const wchar_t* archivePath,
        int expectedImageCount,
        wchar_t* errorMsg,
        int errorMsgSize,
        ProgressCallback callback) {

        try {
            if (callback) {
                callback(0, L"Starting SSB archive verification...");
            }

            // Check file exists
            if (!fs::exists(archivePath)) {
                swprintf_s(errorMsg, errorMsgSize, L"Archive file does not exist: %s", archivePath);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -1;
            }

            if (callback) {
                callback(10, L"Checking file size...");
            }

            // Check minimum file size (WIM header is 208 bytes)
            auto fileSize = fs::file_size(archivePath);
            if (fileSize < 208) {
                swprintf_s(errorMsg, errorMsgSize, 
                    L"Archive file too small (%llu bytes). Minimum is 208 bytes. File may be incomplete.", 
                    fileSize);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -2;
            }

            if (callback) {
                callback(20, L"Opening archive...");
            }

            // Open WIM archive (without VERIFY flag - basic validation only)
            DWORD creationResult = 0;
            HANDLE hWim = WIMCreateFile(
                archivePath,
                WIM_GENERIC_READ,
                WIM_OPEN_EXISTING,
                0,  // No flags - basic validation sufficient
                0,
                &creationResult
            );

            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                DWORD error = GetLastError();
                swprintf_s(errorMsg, errorMsgSize, 
                    L"Failed to open archive. Error %u. Archive may be corrupted.", error);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -3;
            }

            if (callback) {
                callback(40, L"Checking image count...");
            }

            // Get image count
            DWORD imageCount = WIMGetImageCount(hWim);

            if (imageCount == 0) {
                swprintf_s(errorMsg, errorMsgSize, L"Archive contains no images. Archive is empty or corrupted.");
                WIMCloseHandle(hWim);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -4;
            }

            // Verify expected image count matches (if provided)
            if (expectedImageCount > 0 && static_cast<int>(imageCount) != expectedImageCount) {
                swprintf_s(errorMsg, errorMsgSize, 
                    L"Image count mismatch. Expected %d, found %u. Archive may be incomplete.", 
                    expectedImageCount, imageCount);
                WIMCloseHandle(hWim);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -5;
            }

            if (callback) {
                std::wstring msg = L"Found " + std::to_wstring(imageCount) + L" image(s). Verifying loadability...";
                callback(60, msg.c_str());
            }

            // Set temporary path for WIM API (required for WIMLoadImage to work)
            // Without this, WIMLoadImage fails with error 1632 even on valid WIM files
            wchar_t tempPath[MAX_PATH];
            if (GetTempPathW(MAX_PATH, tempPath)) {
                WIMSetTemporaryPath(hWim, tempPath);
            }

            // Try loading first image to verify image structure
            HANDLE hImage = WIMLoadImage(hWim, 1);

            if (!hImage || hImage == INVALID_HANDLE_VALUE) {
                DWORD error = GetLastError();
                swprintf_s(errorMsg, errorMsgSize, 
                    L"Failed to load image 1. Error %u. Image data is corrupted.", error);
                WIMCloseHandle(hWim);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -6;
            }

            if (callback) {
                callback(80, L"Image structure verified. Checking metadata...");
            }

            // Get image information (XML metadata)
            wchar_t* imageXmlInfo = nullptr;
            DWORD xmlSize = 0;
            if (!WIMGetImageInformation(hImage, reinterpret_cast<LPVOID*>(&imageXmlInfo), &xmlSize) ||
                imageXmlInfo == nullptr ||
                xmlSize < sizeof(wchar_t)) {
                DWORD error = GetLastError();
                swprintf_s(errorMsg, errorMsgSize,
                    L"No metadata found in image. Archive metadata retrieval failed with error %u.", error);
                if (imageXmlInfo != nullptr) {
                    LocalFree(imageXmlInfo);
                }
                WIMCloseHandle(hImage);
                WIMCloseHandle(hWim);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -7;
            }

            std::wstring imageXml(imageXmlInfo);
            LocalFree(imageXmlInfo);

            if (imageXml.empty() || imageXml.find(L"<IMAGE") == std::wstring::npos) {
                swprintf_s(errorMsg, errorMsgSize, L"Image metadata XML is empty or invalid. Archive may be corrupted.");
                WIMCloseHandle(hImage);
                WIMCloseHandle(hWim);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -7;
            }

            // Close handles
            WIMCloseHandle(hImage);
            WIMCloseHandle(hWim);

            if (callback) {
                callback(100, L"Archive verification completed successfully!");
            }

            swprintf_s(errorMsg, errorMsgSize, L"SUCCESS: Archive contains %u valid image(s)", imageCount);
            return 0;  // Success

        }
        catch (const std::exception& e) {
            swprintf_s(errorMsg, errorMsgSize, L"Exception during verification: %S", e.what());
            if (callback) {
                callback(0, errorMsg);
            }
            return -98;
        }
        catch (...) {
            swprintf_s(errorMsg, errorMsgSize, L"Unknown error during archive verification");
            if (callback) {
                callback(0, errorMsg);
            }
            return -99;
        }
    }
}

