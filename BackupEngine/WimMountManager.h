#pragma once
#include <windows.h>
#include <wimgapi.h>
#include <string>
#include <vector>
#include <map>

#pragma comment(lib, "wimgapi.lib")

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
            wchar_t* mountPath,      // OUT: where it was mounted
            int mountPathSize,
            wchar_t* errorMsg,
            int errorMsgSize
        );

        // Unmount a WIM by mount path
        static bool UnmountWim(
            const wchar_t* mountPath,
            wchar_t* errorMsg,
            int errorMsgSize
        );

        // Unmount all mounted WIMs
        static void UnmountAll();

        // Get list of mounted WIMs
        static std::vector<MountedWimInfo> GetMountedWims();

        // Check if path is a mounted WIM
        static bool IsMountedWim(const wchar_t* path);

        // Get mount info by path
        static bool GetMountInfo(const wchar_t* path, MountedWimInfo& info);

    private:
        // Create a unique mount point directory
        static std::wstring CreateMountPoint(const wchar_t* backupName);
        
        // WIMGAPI callback for progress
        static DWORD WINAPI WimMessageCallback(
            DWORD msgId,
            WPARAM wParam,
            LPARAM lParam,
            PVOID userData
        );
    };

} // namespace BackupEngine
