#include "WimMountManager.h"
#include "BackupEngine.h"
#include <shlobj.h>
#include <sstream>
#include <iomanip>

namespace BackupEngine {

    std::map<std::wstring, MountedWimInfo> WimMountManager::mountedWims;
    CRITICAL_SECTION WimMountManager::cs;
    bool WimMountManager::initialized = false;

    bool WimMountManager::Initialize() {
        if (!initialized) {
            InitializeCriticalSection(&cs);
            initialized = true;
        }
        return true;
    }

    void WimMountManager::Cleanup() {
        if (initialized) {
            UnmountAll();
            DeleteCriticalSection(&cs);
            initialized = false;
        }
    }

    bool WimMountManager::MountWim(
        const wchar_t* wimPath,
        const wchar_t* backupName,
        const wchar_t* backupType,
        wchar_t* mountPath,
        int mountPathSize,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        EnterCriticalSection(&cs);

        try {
            // Create unique mount point
            std::wstring mountPoint = CreateMountPoint(backupName);

            // Create the mount directory
            if (!CreateDirectoryW(mountPoint.c_str(), nullptr)) {
                DWORD err = GetLastError();
                if (err != ERROR_ALREADY_EXISTS) {
                    swprintf_s(errorMsg, errorMsgSize, L"Failed to create mount directory: %d", err);
                    LeaveCriticalSection(&cs);
                    return false;
                }
            }

            // Open the WIM file
            DWORD creationResult = 0;
            HANDLE wimHandle = WIMCreateFile(
                wimPath,
                WIM_GENERIC_READ,
                WIM_OPEN_EXISTING,
                WIM_FLAG_VERIFY,
                0,                  // dwCompressionType (not used for opening)
                &creationResult     // result
            );

            if (!wimHandle || wimHandle == INVALID_HANDLE_VALUE) {
                swprintf_s(errorMsg, errorMsgSize, L"Failed to open WIM file: %d", GetLastError());
                RemoveDirectoryW(mountPoint.c_str());
                LeaveCriticalSection(&cs);
                return false;
            }

            // Get image count (usually 1 for backups)
            DWORD imageCount = WIMGetImageCount(wimHandle);  // Returns count directly
            if (imageCount == 0) {
                swprintf_s(errorMsg, errorMsgSize, L"No images found in WIM file");
                WIMCloseHandle(wimHandle);
                RemoveDirectoryW(mountPoint.c_str());
                LeaveCriticalSection(&cs);
                return false;
            }


            // Mount the first image (index 1) read-only
            HANDLE imageHandle = WIMLoadImage(wimHandle, 1);
            if (!imageHandle || imageHandle == INVALID_HANDLE_VALUE) {
                swprintf_s(errorMsg, errorMsgSize, L"Failed to load WIM image: %d", GetLastError());
                WIMCloseHandle(wimHandle);
                RemoveDirectoryW(mountPoint.c_str());
                LeaveCriticalSection(&cs);
                return false;
            }

            // Mount the image (read-only, no admin required)
            if (!WIMMountImage(mountPoint.c_str(), wimPath, 1, nullptr)) {
                DWORD err = GetLastError();
                swprintf_s(errorMsg, errorMsgSize, L"Failed to mount WIM image: %d", err);
                WIMCloseHandle(imageHandle);
                WIMCloseHandle(wimHandle);
                RemoveDirectoryW(mountPoint.c_str());
                LeaveCriticalSection(&cs);
                return false;
            }

            WIMCloseHandle(imageHandle);

            // Store mount information
            MountedWimInfo info;
            info.wimPath = wimPath;
            info.mountPath = mountPoint;
            info.backupName = backupName;
            info.backupType = backupType;
            info.wimHandle = wimHandle;
            GetSystemTime(&info.mountTime);

            mountedWims[mountPoint] = info;

            // Return mount path
            wcscpy_s(mountPath, mountPathSize, mountPoint.c_str());

            LeaveCriticalSection(&cs);
            return true;
        }
        catch (...) {
            swprintf_s(errorMsg, errorMsgSize, L"Exception during WIM mount");
            LeaveCriticalSection(&cs);
            return false;
        }
    }

    bool WimMountManager::UnmountWim(
        const wchar_t* mountPath,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        EnterCriticalSection(&cs);

        try {
            auto it = mountedWims.find(mountPath);
            if (it == mountedWims.end()) {
                swprintf_s(errorMsg, errorMsgSize, L"Mount path not found");
                LeaveCriticalSection(&cs);
                return false;
            }

            MountedWimInfo& info = it->second;

            // Unmount the WIM
            if (!WIMUnmountImage(mountPath, info.wimPath.c_str(), 1, FALSE)) {
                DWORD err = GetLastError();
                swprintf_s(errorMsg, errorMsgSize, L"Failed to unmount WIM: %d", err);
                LeaveCriticalSection(&cs);
                return false;
            }

            // Close the WIM handle
            if (info.wimHandle && info.wimHandle != INVALID_HANDLE_VALUE) {
                WIMCloseHandle(info.wimHandle);
            }

            // Remove the mount directory
            RemoveDirectoryW(mountPath);

            // Remove from tracked mounts
            mountedWims.erase(it);

            LeaveCriticalSection(&cs);
            return true;
        }
        catch (...) {
            swprintf_s(errorMsg, errorMsgSize, L"Exception during WIM unmount");
            LeaveCriticalSection(&cs);
            return false;
        }
    }

    void WimMountManager::UnmountAll() {
        EnterCriticalSection(&cs);

        auto it = mountedWims.begin();
        while (it != mountedWims.end()) {
            wchar_t errorMsg[256];
            UnmountWim(it->first.c_str(), errorMsg, 256);
            it = mountedWims.begin(); // Restart since UnmountWim modifies the map
        }

        LeaveCriticalSection(&cs);
    }

    std::vector<MountedWimInfo> WimMountManager::GetMountedWims() {
        EnterCriticalSection(&cs);

        std::vector<MountedWimInfo> result;
        for (const auto& pair : mountedWims) {
            result.push_back(pair.second);
        }

        LeaveCriticalSection(&cs);
        return result;
    }

    bool WimMountManager::IsMountedWim(const wchar_t* path) {
        EnterCriticalSection(&cs);
        bool result = mountedWims.find(path) != mountedWims.end();
        LeaveCriticalSection(&cs);
        return result;
    }

    bool WimMountManager::GetMountInfo(const wchar_t* path, MountedWimInfo& info) {
        EnterCriticalSection(&cs);

        auto it = mountedWims.find(path);
        if (it != mountedWims.end()) {
            info = it->second;
            LeaveCriticalSection(&cs);
            return true;
        }

        LeaveCriticalSection(&cs);
        return false;
    }

    std::wstring WimMountManager::CreateMountPoint(const wchar_t* backupName) {
        // Get temp directory
        wchar_t tempPath[MAX_PATH];
        GetTempPathW(MAX_PATH, tempPath);

        // Create BackupMounts subdirectory
        std::wstring mountsDir = std::wstring(tempPath) + L"BackupMounts\\";
        CreateDirectoryW(mountsDir.c_str(), nullptr);

        // Create unique mount point using backup name + timestamp
        SYSTEMTIME st;
        GetSystemTime(&st);

        std::wstringstream ss;
        ss << mountsDir << backupName << L"_"
           << st.wYear << std::setw(2) << std::setfill(L'0') << st.wMonth
           << std::setw(2) << st.wDay << L"_"
           << std::setw(2) << st.wHour << std::setw(2) << st.wMinute
           << std::setw(2) << st.wSecond;

        return ss.str();
    }

    DWORD WINAPI WimMountManager::WimMessageCallback(
        DWORD msgId,
        WPARAM wParam,
        LPARAM lParam,
        PVOID userData
    ) {
        // Handle progress callbacks if needed
        switch (msgId) {
            case WIM_MSG_PROGRESS:
                // Update progress UI
                break;
            case WIM_MSG_PROCESS:
                // Processing file
                break;
        }
        return WIM_MSG_SUCCESS;
    }

} // namespace BackupEngine

// C exports for P/Invoke from C#
extern "C" {
    using namespace BackupEngine;

    BACKUPENGINE_API bool WimMount_MountWim(
        const wchar_t* wimPath,
        const wchar_t* backupName,
        const wchar_t* backupType,
        wchar_t* mountPath,
        int mountPathSize,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        if (!WimMountManager::Initialize()) {
            swprintf_s(errorMsg, errorMsgSize, L"Failed to initialize WimMountManager");
            return false;
        }

        return WimMountManager::MountWim(
            wimPath, backupName, backupType,
            mountPath, mountPathSize,
            errorMsg, errorMsgSize
        );
    }

    BACKUPENGINE_API bool WimMount_UnmountWim(
        const wchar_t* mountPath,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        return WimMountManager::UnmountWim(mountPath, errorMsg, errorMsgSize);
    }

    BACKUPENGINE_API void WimMount_UnmountAll() {
        WimMountManager::UnmountAll();
    }

    BACKUPENGINE_API int WimMount_GetMountedCount() {
        auto mounts = WimMountManager::GetMountedWims();
        return static_cast<int>(mounts.size());
    }

    BACKUPENGINE_API bool WimMount_GetMountedInfo(
        int index,
        wchar_t* wimPath,
        int wimPathSize,
        wchar_t* mountPath,
        int mountPathSize,
        wchar_t* backupName,
        int backupNameSize,
        wchar_t* backupType,
        int backupTypeSize,
        SYSTEMTIME* mountTime  // ? NEW: Return mount time
    ) {
        auto mounts = WimMountManager::GetMountedWims();

        if (index < 0 || index >= static_cast<int>(mounts.size())) {
            return false;
        }

        const auto& info = mounts[index];

        wcscpy_s(wimPath, wimPathSize, info.wimPath.c_str());
        wcscpy_s(mountPath, mountPathSize, info.mountPath.c_str());
        wcscpy_s(backupName, backupNameSize, info.backupName.c_str());
        wcscpy_s(backupType, backupTypeSize, info.backupType.c_str());

        // Copy mount time
        if (mountTime) {
            *mountTime = info.mountTime;
        }

        return true;
    }
}
