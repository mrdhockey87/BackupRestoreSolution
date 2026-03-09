#pragma once
#include <windows.h>
#include <wimgapi.h>
#include <string>
#include <vector>
#include <map>

#pragma comment(lib, "wimgapi.lib")

// Progress callback typedef for C# interop
typedef void(__cdecl* ProgressCallback)(int percentage, const wchar_t* message);

namespace BackupEngine {

    // Mounted WIM information
    struct MountedWimInfo {
        std::wstring wimPath;
        std::wstring mountPath;
        std::wstring backupName;
        std::wstring backupType;
        HANDLE wimHandle;
        SYSTEMTIME mountTime;
    };

    class WimMountManager {
    private:
        static std::map<std::wstring, MountedWimInfo> mountedWims;
        static CRITICAL_SECTION cs;
        static bool initialized;

    public:
        // Initialize the manager
        static bool Initialize();
        
        // Cleanup
        static void Cleanup();

        // Mount a WIM file to a directory (read-only, no admin required)
        static bool MountWim(
            const wchar_t* wimPath,
            const wchar_t* backupName,
            const wchar_t* backupType,
            int imageIndex,           // Which image to mount (1-based)
            wchar_t* mountPath,      // OUT: where it was mounted
            int mountPathSize,
            wchar_t* errorMsg,
            int errorMsgSize,
            ProgressCallback callback = nullptr,  // Optional progress callback
            const wchar_t* userTempPath = nullptr  // Optional user-specified temp path
        );

        // Unmount a WIM by mount path
        static bool UnmountWim(
            const wchar_t* mountPath,
            wchar_t* errorMsg,
            int errorMsgSize,
            ProgressCallback callback = nullptr  // Optional progress callback
        );

        // Unmount all mounted WIMs
        static void UnmountAll();

        // Get list of mounted WIMs
        static std::vector<MountedWimInfo> GetMountedWims();

        // Check if path is a mounted WIM
        static bool IsMountedWim(const wchar_t* path);

        // Get mount info by path
        static bool GetMountInfo(const wchar_t* path, MountedWimInfo& info);

        // Validate WIM file integrity
        static bool ValidateWim(
            const wchar_t* wimPath,
            int* imageCount,
            wchar_t* errorMsg,
            int errorMsgSize
        );

    private:
        // Create a unique mount point directory
        static std::wstring CreateMountPoint(const wchar_t* backupName, int imageIndex);
        
        // WIMGAPI callback for progress
        static DWORD WINAPI WimMessageCallback(
            DWORD msgId,
            WPARAM wParam,
            LPARAM lParam,
            PVOID userData
        );
    };

} // namespace BackupEngine
