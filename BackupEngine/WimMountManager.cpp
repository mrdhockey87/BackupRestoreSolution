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

    // Static callback for WIM API that forwards to user callback
    static DWORD WINAPI WimProgressCallback(DWORD msgId, WPARAM wParam, LPARAM lParam, PVOID pvIgnored) {
        ProgressCallback userCallback = (ProgressCallback)pvIgnored;

        switch (msgId) {
            case WIM_MSG_PROCESS:
            {
                // WIM_MSG_PROCESS - file being processed
                // wParam = path to file (LPCWSTR)
                if (wParam && userCallback) {
                    const wchar_t* filePath = (const wchar_t*)wParam;

                    // Extract just the filename for cleaner display
                    const wchar_t* fileName = wcsrchr(filePath, L'\\');
                    if (fileName) {
                        fileName++; // Skip the backslash
                    } else {
                        fileName = filePath;
                    }

                    // Report file being processed
                    std::wstring message = L"Processing: ";
                    message += fileName;
                    userCallback(50, message.c_str());
                }
                return WIM_MSG_SUCCESS;
            }

            case WIM_MSG_PROGRESS:
            {
                // WIM_MSG_PROGRESS - overall progress
                // wParam = estimated percentage complete
                if (userCallback) {
                    int percentage = (int)wParam;
                    // Scale to 50-90% range (mount operation is 50-90% of total)
                    percentage = 50 + (percentage * 40 / 100);
                    userCallback(percentage, L"Mounting image...");
                }
                return WIM_MSG_SUCCESS;
            }

            case WIM_MSG_SETRANGE:
            {
                // WIM_MSG_SETRANGE - total number of files
                // wParam = estimated total number of files
                if (userCallback) {
                    std::wstring message = L"Preparing to mount ";
                    message += std::to_wstring((DWORD)wParam);
                    message += L" files...";
                    userCallback(45, message.c_str());
                }
                return WIM_MSG_SUCCESS;
            }

            default:
                return WIM_MSG_SUCCESS;
        }
    }

    bool WimMountManager::MountWim(
        const wchar_t* wimPath,
        const wchar_t* backupName,
        const wchar_t* backupType,
        int imageIndex,  // Image index parameter
        wchar_t* mountPath,
        int mountPathSize,
        wchar_t* errorMsg,
        int errorMsgSize,
        ProgressCallback callback,  // Progress callback parameter
        const wchar_t* userTempPath  // User-specified temp path
    ) {
        EnterCriticalSection(&cs);

        try {
            // Validate image index
            if (imageIndex < 1) {
                swprintf_s(errorMsg, errorMsgSize, L"Invalid image index: %d (must be >= 1)", imageIndex);
                LeaveCriticalSection(&cs);
                return false;
            }

            // Create unique mount point (include image index for uniqueness)
            // Pass user temp path so mount point is created under user's selected location
            std::wstring mountPoint = CreateMountPoint(backupName, imageIndex, userTempPath);

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
            // NOTE: Removed WIM_FLAG_VERIFY - can cause issues with some WIM files
            // Even if file opens in other WIM viewers, VERIFY flag can cause error 1632
            DWORD creationResult = 0;
            HANDLE wimHandle = WIMCreateFile(
                wimPath,
                WIM_GENERIC_READ,
                WIM_OPEN_EXISTING,
                0,                  // No flags - open for basic read access
                0,                  // dwCompressionType (not used for opening)
                &creationResult     // result
            );

            if (!wimHandle || wimHandle == INVALID_HANDLE_VALUE) {
                swprintf_s(errorMsg, errorMsgSize, L"Failed to open SSB archive: %d", GetLastError());
                RemoveDirectoryW(mountPoint.c_str());
                LeaveCriticalSection(&cs);
                return false;
            }

            // Get image count to validate index
            DWORD imageCount = WIMGetImageCount(wimHandle);
            if (imageCount == 0) {
                swprintf_s(errorMsg, errorMsgSize, L"No images found in SSB archive");
                WIMCloseHandle(wimHandle);
                RemoveDirectoryW(mountPoint.c_str());
                LeaveCriticalSection(&cs);
                return false;
            }

            if (imageIndex > static_cast<int>(imageCount)) {
                swprintf_s(errorMsg, errorMsgSize, L"Image index %d exceeds available images (%d)", imageIndex, imageCount);
                WIMCloseHandle(wimHandle);
                RemoveDirectoryW(mountPoint.c_str());
                LeaveCriticalSection(&cs);
                return false;
            }

            // Get WIM information for diagnostics
            DWORD wimInfoSize = 0;
            WIMGetImageInformation(wimHandle, nullptr, &wimInfoSize);

            // Log SSB archive info for diagnostics
            std::wstring diagMsg = L"[WimMount] SSB archive has " + std::to_wstring(imageCount) + 
                                   L" image(s), attempting to load image " + std::to_wstring(imageIndex);
            OutputDebugStringW(diagMsg.c_str());

            // CRITICAL: Set temporary path for WIM operations
            // WIM API requires temp directory for extracting/processing image data
            // Without this, WIMLoadImage can fail with error 1632
            wchar_t tempPath[MAX_PATH];

            OutputDebugStringW(L"[WimMount] About to check userTempPath parameter...");
            if (userTempPath)
            {
                OutputDebugStringW((L"[WimMount] userTempPath is NOT NULL, length: " + std::to_wstring(wcslen(userTempPath))).c_str());
            }
            else
            {
                OutputDebugStringW(L"[WimMount] userTempPath is NULL!");
            }

            // Use user-provided temp path if available, otherwise get system temp
            if (userTempPath && wcslen(userTempPath) > 0) {
                wcscpy_s(tempPath, MAX_PATH, userTempPath);
                WIMSetTemporaryPath(wimHandle, tempPath);
                OutputDebugStringW((L"[WimMount] Using user-specified temp path: " + std::wstring(tempPath)).c_str());
            }
            else if (GetTempPathW(MAX_PATH, tempPath) > 0) {
                WIMSetTemporaryPath(wimHandle, tempPath);
                OutputDebugStringW((L"[WimMount] Using system temp path: " + std::wstring(tempPath)).c_str());
            }
            else {
                OutputDebugStringW(L"[WimMount] Warning: Failed to get temp path, using default");
            }

            // Register progress callback if provided
            if (callback) {
                WIMRegisterMessageCallback(wimHandle, (FARPROC)WimProgressCallback, callback);
                callback(0, L"Preparing to load image...");
            }

            // Update progress - starting image load
            if (callback) {
                callback(10, L"Opening SSB archive...");
            }

            // Mount the specified image (read-only)
            HANDLE imageHandle = WIMLoadImage(wimHandle, imageIndex);
            if (!imageHandle || imageHandle == INVALID_HANDLE_VALUE) {
                DWORD loadError = GetLastError();

                // Enhanced error message with diagnostics
                std::wstring detailedError = L"Failed to load SSB archive image " + std::to_wstring(imageIndex) + 
                                            L" of " + std::to_wstring(imageCount) + 
                                            L". Error code: " + std::to_wstring(loadError);

                // Check common error codes
                if (loadError == 1632) {
                    detailedError += L" (ERROR_INSTALL_SERVICE_FAILURE/Invalid SSB archive)";
                    detailedError += L"\n\nPossible causes:\n";
                    detailedError += L"- SSB archive is corrupted or incomplete\n";
                    detailedError += L"- Backup was interrupted during creation\n";
                    detailedError += L"- Disk space was exhausted during backup\n";
                    detailedError += L"- File system errors on backup drive\n\n";
                    detailedError += L"Try running a new Full backup to create a fresh backup file.";
                } else if (loadError == 5) {
                    detailedError += L" (ERROR_ACCESS_DENIED)";
                } else if (loadError == 32) {
                    detailedError += L" (ERROR_SHARING_VIOLATION - file in use)";
                }

                swprintf_s(errorMsg, errorMsgSize, L"%s", detailedError.c_str());
                OutputDebugStringW((L"[WimMount ERROR] " + detailedError).c_str());

                // Unregister callback on failure
                if (callback) {
                    WIMUnregisterMessageCallback(wimHandle, (FARPROC)callback);
                }

                WIMCloseHandle(wimHandle);
                RemoveDirectoryW(mountPoint.c_str());
                LeaveCriticalSection(&cs);
                return false;
            }

            // Update progress - image loaded successfully
            if (callback) {
                callback(30, L"Image loaded successfully...");
            }

            // Update progress - preparing to mount
            if (callback) {
                callback(50, L"Mounting image to folder...");
            }

            // Mount the image (read-only, no admin required)
            // NOTE: WIMMountImage is synchronous and does NOT support callbacks
            // This operation can take 30-60 seconds with no progress feedback
            if (!WIMMountImage(mountPoint.c_str(), wimPath, imageIndex, nullptr)) {
                DWORD err = GetLastError();
                swprintf_s(errorMsg, errorMsgSize, L"Failed to mount WIM image: %d", err);

                // Unregister callback on failure
                if (callback) {
                    WIMUnregisterMessageCallback(wimHandle, (FARPROC)callback);
                }

                WIMCloseHandle(imageHandle);
                WIMCloseHandle(wimHandle);
                RemoveDirectoryW(mountPoint.c_str());
                LeaveCriticalSection(&cs);
                return false;
            }

            // Update progress
            if (callback) {
                callback(90, L"Finalizing mount...");
            }

            WIMCloseHandle(imageHandle);

            // Unregister callback after successful mount
            if (callback) {
                WIMUnregisterMessageCallback(wimHandle, (FARPROC)WimProgressCallback);
                callback(100, L"Mount completed successfully!");
            }

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
        int errorMsgSize,
        ProgressCallback callback  // Progress callback
    ) {
        EnterCriticalSection(&cs);

        try {
            if (callback) {
                callback(0, L"Starting unmount operation...");
            }

            auto it = mountedWims.find(mountPath);
            if (it == mountedWims.end()) {
                swprintf_s(errorMsg, errorMsgSize, L"Mount path not found");
                LeaveCriticalSection(&cs);
                return false;
            }

            MountedWimInfo& info = it->second;

            if (callback) {
                callback(25, L"Unmounting WIM image...");
            }

            // Log mount details for diagnostics
            OutputDebugStringW((L"[WimMount] Attempting unmount: " + std::wstring(mountPath)).c_str());
            OutputDebugStringW((L"[WimMount] WIM file: " + info.wimPath).c_str());

            // Check if mount point still exists
            DWORD attributes = GetFileAttributesW(mountPath);
            if (attributes == INVALID_FILE_ATTRIBUTES) {
                DWORD err = GetLastError();
                OutputDebugStringW((L"[WimMount] Warning: Mount point doesn't exist or is inaccessible (error " + std::to_wstring(err) + L")").c_str());
                // Continue anyway - WIM API might still need to clean up
            }

            // Unmount the WIM - use WIM_MOUNT_FLAG_NO_APPLY to prevent errors if already unmounted
            // First parameter: mount point path
            // Second parameter: WIM file path (can be NULL if mount point is sufficient)
            // Third parameter: image index (1-based)
            // Fourth parameter: flags (0 = normal unmount)
            if (!WIMUnmountImage(mountPath, NULL, 0, 0)) {
                DWORD err = GetLastError();
                // FIXED: Use %u for unsigned DWORD, not %d (which caused negative numbers)
                swprintf_s(errorMsg, errorMsgSize, 
                    L"Failed to unmount WIM: %u (0x%X)\n\n"
                    L"Common causes:\n"
                    L"• Files still open in Explorer (close all windows showing backup)\n"
                    L"• Another program accessing mounted files\n"
                    L"• Mount point in use\n\n"
                    L"Try closing Explorer windows and retry.", 
                    err, err);
                OutputDebugStringW((L"[WimMount] Unmount error: " + std::to_wstring(err) + L" (0x" + std::to_wstring(err) + L")").c_str());
                OutputDebugStringW((L"[WimMount] Mount path: " + std::wstring(mountPath)).c_str());
                OutputDebugStringW((L"[WimMount] WIM path: " + info.wimPath).c_str());
                LeaveCriticalSection(&cs);
                return false;
            }

            if (callback) {
                callback(50, L"Closing WIM handle...");
            }

            // Close the WIM handle
            if (info.wimHandle && info.wimHandle != INVALID_HANDLE_VALUE) {
                WIMCloseHandle(info.wimHandle);
            }

            if (callback) {
                callback(75, L"Cleaning up mount directory...");
            }

            // Enumerate and delete files in mount directory with progress
            WIN32_FIND_DATAW findData;
            std::wstring searchPath = std::wstring(mountPath) + L"\\*";
            HANDLE hFind = FindFirstFileW(searchPath.c_str(), &findData);

            if (hFind != INVALID_HANDLE_VALUE) {
                int fileCount = 0;
                do {
                    // Skip . and ..
                    if (wcscmp(findData.cFileName, L".") != 0 && wcscmp(findData.cFileName, L"..") != 0) {
                        fileCount++;

                        if (callback && fileCount % 10 == 0) {
                            // Update progress every 10 files
                            std::wstring message = L"Cleaning up: ";
                            message += findData.cFileName;
                            callback(75 + (fileCount % 100) / 10, message.c_str());
                        }

                        // Log file being deleted
                        OutputDebugStringW((L"[WimMount] Deleting: " + std::wstring(findData.cFileName)).c_str());
                    }
                } while (FindNextFileW(hFind, &findData));

                FindClose(hFind);

                if (callback && fileCount > 0) {
                    std::wstring message = L"Removed ";
                    message += std::to_wstring(fileCount);
                    message += L" files from mount directory";
                    callback(85, message.c_str());
                }
            }

            // Remove the mount directory
            RemoveDirectoryW(mountPath);

            // Remove from tracked mounts
            mountedWims.erase(it);

            if (callback) {
                callback(100, L"Unmount completed successfully!");
            }

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

    bool WimMountManager::ValidateWim(
        const wchar_t* wimPath,
        int* imageCount,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        OutputDebugStringW(L"[WimMount] Validating WIM file...");

        // Check if file exists
        if (GetFileAttributesW(wimPath) == INVALID_FILE_ATTRIBUTES) {
            swprintf_s(errorMsg, errorMsgSize, L"WIM file not found: %s", wimPath);
            return false;
        }

        // Get file size
        WIN32_FILE_ATTRIBUTE_DATA fileInfo;
        if (!GetFileAttributesExW(wimPath, GetFileExInfoStandard, &fileInfo)) {
            swprintf_s(errorMsg, errorMsgSize, L"Cannot access WIM file: %d", GetLastError());
            return false;
        }

        LARGE_INTEGER fileSize;
        fileSize.LowPart = fileInfo.nFileSizeLow;
        fileSize.HighPart = fileInfo.nFileSizeHigh;

        // Check if file is too small to be valid WIM (WIM header is at least 208 bytes)
        if (fileSize.QuadPart < 208) {
            swprintf_s(errorMsg, errorMsgSize, 
                L"WIM file is too small (%lld bytes). File may be incomplete or corrupted.",
                fileSize.QuadPart);
            return false;
        }

        OutputDebugStringW((L"[WimMount] File size: " + std::to_wstring(fileSize.QuadPart) + L" bytes").c_str());

        // Try to open WIM file WITHOUT verification flag
        // WIM_FLAG_VERIFY can cause error 1632 even with valid WIM files
        DWORD creationResult = 0;
        HANDLE wimHandle = WIMCreateFile(
            wimPath,
            WIM_GENERIC_READ,
            WIM_OPEN_EXISTING,
            0,  // No flags - basic validation only
            0,
            &creationResult
        );

        if (!wimHandle || wimHandle == INVALID_HANDLE_VALUE) {
            DWORD openError = GetLastError();
            if (openError == 5) {
                swprintf_s(errorMsg, errorMsgSize, L"Access denied to WIM file");
            } else if (openError == 32) {
                swprintf_s(errorMsg, errorMsgSize, L"WIM file is in use by another process");
            } else if (openError == 1632) {
                swprintf_s(errorMsg, errorMsgSize, 
                    L"WIM file is invalid or corrupted (Error 1632).\n\n"
                    L"This usually means:\n"
                    L"- Backup was interrupted during creation\n"
                    L"- Disk space was exhausted\n"
                    L"- File system errors on backup drive\n\n"
                    L"Try running a new Full backup.");
            } else {
                swprintf_s(errorMsg, errorMsgSize, L"Failed to open WIM file: %d", openError);
            }
            return false;
        }

        // Set temporary path for WIM operations (required for some WIM API calls)
        wchar_t tempPath[MAX_PATH];
        if (GetTempPathW(MAX_PATH, tempPath) > 0) {
            WIMSetTemporaryPath(wimHandle, tempPath);
        }

        // Get image count
        DWORD count = WIMGetImageCount(wimHandle);
        if (imageCount) {
            *imageCount = static_cast<int>(count);
        }

        OutputDebugStringW((L"[WimMount] Validation successful - " + std::to_wstring(count) + L" image(s) found").c_str());

        WIMCloseHandle(wimHandle);

        if (count == 0) {
            swprintf_s(errorMsg, errorMsgSize, L"WIM file contains no images");
            return false;
        }

        return true;
    }

    std::wstring WimMountManager::CreateMountPoint(const wchar_t* backupName, int imageIndex, const wchar_t* userTempPath) {
        std::wstring mountBase;

        // Use user-selected temp path if provided, otherwise system temp
        if (userTempPath && wcslen(userTempPath) > 0) {
            // User selected temp path - create mount subfolder under it
            mountBase = std::wstring(userTempPath);

            // Ensure path ends with backslash
            if (mountBase.back() != L'\\') {
                mountBase += L'\\';
            }

            mountBase += L"BackupMounts\\";
            OutputDebugStringW((L"[WimMount] Using user-specified mount base: " + mountBase).c_str());
        }
        else {
            // System temp - use old logic
            wchar_t tempPath[MAX_PATH];
            GetTempPathW(MAX_PATH, tempPath);
            mountBase = std::wstring(tempPath) + L"BackupMounts\\";
            OutputDebugStringW((L"[WimMount] Using system temp mount base: " + mountBase).c_str());
        }

        // Create BackupMounts subdirectory - CreateDirectoryW returns TRUE if created, FALSE if already exists
        // We don't care about the result - if it already exists, that's fine
        CreateDirectoryW(mountBase.c_str(), nullptr);

        // Create unique mount point using backup name + image index + timestamp
        SYSTEMTIME st;
        GetSystemTime(&st);

        std::wstringstream ss;
        ss << mountBase << backupName << L"_Image" << imageIndex << L"_"
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
        int imageIndex,  // Image index to mount
        wchar_t* mountPath,
        int mountPathSize,
        wchar_t* errorMsg,
        int errorMsgSize,
        ProgressCallback callback,  // Progress callback from C#
        const wchar_t* userTempPath  // User-specified temp path
    ) {
        if (!WimMountManager::Initialize()) {
            swprintf_s(errorMsg, errorMsgSize, L"Failed to initialize WimMountManager");
            return false;
        }

        return WimMountManager::MountWim(
            wimPath, backupName, backupType, imageIndex,
            mountPath, mountPathSize,
            errorMsg, errorMsgSize,
            callback,  // Pass callback through
            userTempPath  // Pass temp path through
        );
    }

    BACKUPENGINE_API bool WimMount_UnmountWim(
        const wchar_t* mountPath,
        wchar_t* errorMsg,
        int errorMsgSize,
        ProgressCallback callback  // Progress callback
    ) {
        return WimMountManager::UnmountWim(mountPath, errorMsg, errorMsgSize, callback);
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

    // NEW: Get WIM image count (exported for mount image selection)
    BACKUPENGINE_API int WimMount_GetImageCount(
        const wchar_t* wimPath,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        if (!wimPath) {
            if (errorMsg) swprintf_s(errorMsg, errorMsgSize, L"Invalid WIM path");
            return -1;
        }

        // Open WIM to get image count
        HANDLE hWim = WIMCreateFile(
            wimPath,
            WIM_GENERIC_READ,
            WIM_OPEN_EXISTING,
            0,
            WIM_COMPRESS_NONE,
            NULL
        );

        if (!hWim || hWim == INVALID_HANDLE_VALUE) {
            if (errorMsg) swprintf_s(errorMsg, errorMsgSize, L"Failed to open WIM file");
            return -1;
        }

        DWORD imageCount = WIMGetImageCount(hWim);
        WIMCloseHandle(hWim);

        return static_cast<int>(imageCount);
    }

    // NEW: Get WIM image info by index (exported for mount image selection)
    BACKUPENGINE_API bool WimMount_GetImageInfo(
        const wchar_t* wimPath,
        int imageIndex,
        wchar_t* imageName,
        int imageNameSize,
        wchar_t* imageDescription,
        int imageDescriptionSize,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        if (!wimPath || imageIndex < 1) {
            if (errorMsg) swprintf_s(errorMsg, errorMsgSize, L"Invalid parameters");
            return false;
        }

        // Open WIM file
        HANDLE hWim = WIMCreateFile(
            wimPath,
            WIM_GENERIC_READ,
            WIM_OPEN_EXISTING,
            0,
            WIM_COMPRESS_NONE,
            NULL
        );

        if (!hWim || hWim == INVALID_HANDLE_VALUE) {
            if (errorMsg) swprintf_s(errorMsg, errorMsgSize, L"Failed to open WIM file");
            return false;
        }

        // Set temporary path for WIM API (required for WIMLoadImage)
        wchar_t tempPath[MAX_PATH];
        if (GetTempPathW(MAX_PATH, tempPath)) {
            WIMSetTemporaryPath(hWim, tempPath);
        }

        // Load the image
        HANDLE hImage = WIMLoadImage(hWim, imageIndex);
        if (!hImage || hImage == INVALID_HANDLE_VALUE) {
            WIMCloseHandle(hWim);
            if (errorMsg) swprintf_s(errorMsg, errorMsgSize, L"Failed to load image %d", imageIndex);
            return false;
        }

        // Get image information XML
        wchar_t* xmlInfo = nullptr;
        DWORD xmlSize = 0;

        if (!WIMGetImageInformation(hImage, (LPVOID*)&xmlInfo, &xmlSize)) {
            WIMCloseHandle(hImage);
            WIMCloseHandle(hWim);
            if (errorMsg) swprintf_s(errorMsg, errorMsgSize, L"Failed to get image information");
            return false;
        }

        // Parse XML to extract name and description
        std::wstring xml(xmlInfo);

        // Extract name
        size_t nameStart = xml.find(L"<NAME>");
        size_t nameEnd = xml.find(L"</NAME>");
        if (nameStart != std::wstring::npos && nameEnd != std::wstring::npos) {
            nameStart += 6; // Skip "<NAME>"
            std::wstring name = xml.substr(nameStart, nameEnd - nameStart);
            wcscpy_s(imageName, imageNameSize, name.c_str());
        } else {
            swprintf_s(imageName, imageNameSize, L"Image %d", imageIndex);
        }

        // Extract description
        size_t descStart = xml.find(L"<DESCRIPTION>");
        size_t descEnd = xml.find(L"</DESCRIPTION>");
        if (descStart != std::wstring::npos && descEnd != std::wstring::npos) {
            descStart += 13; // Skip "<DESCRIPTION>"
            std::wstring desc = xml.substr(descStart, descEnd - descStart);
            wcscpy_s(imageDescription, imageDescriptionSize, desc.c_str());
        } else {
            wcscpy_s(imageDescription, imageDescriptionSize, L"No description");
        }

        WIMCloseHandle(hImage);
        WIMCloseHandle(hWim);
        return true;
    }

    // NEW: Validate WIM file integrity (exported for pre-mount validation)
    BACKUPENGINE_API bool WimMount_ValidateWim(
        const wchar_t* wimPath,
        int* imageCount,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        return WimMountManager::ValidateWim(wimPath, imageCount, errorMsg, errorMsgSize);
    }
}
