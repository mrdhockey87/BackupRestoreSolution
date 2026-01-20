// VolumeEnumeration.cpp - Volume and disk enumeration functions
#include "BackupEngine.h"
#include <Windows.h>
#include <string>
#include <vector>
#include <sstream>

extern void SetLastErrorMessage(const std::wstring& error);

extern "C" {

    BACKUPENGINE_API int EnumerateVolumes(wchar_t* buffer, int bufferSize) {
        if (!buffer || bufferSize <= 0) {
            SetLastErrorMessage(L"Invalid buffer");
            return -1;
        }

        try {
            std::wostringstream result;
            wchar_t volumeName[MAX_PATH];
            HANDLE hFind = FindFirstVolumeW(volumeName, ARRAYSIZE(volumeName));

            if (hFind == INVALID_HANDLE_VALUE) {
                SetLastErrorMessage(L"FindFirstVolume failed");
                return -1;
            }

            do {
                // Get volume path names (drive letters)
                wchar_t pathNames[MAX_PATH];
                DWORD pathLen = 0;

                if (GetVolumePathNamesForVolumeNameW(volumeName, pathNames, ARRAYSIZE(pathNames), &pathLen)) {
                    if (pathLen > 0 && pathNames[0] != L'\0') {
                        // Get volume information
                        wchar_t volumeLabel[MAX_PATH] = { 0 };
                        DWORD serialNumber = 0;
                        DWORD maxComponentLen = 0;
                        DWORD fileSystemFlags = 0;
                        wchar_t fileSystemName[MAX_PATH] = { 0 };

                        ULARGE_INTEGER totalBytes, freeBytes;
                        totalBytes.QuadPart = 0;
                        freeBytes.QuadPart = 0;

                        GetVolumeInformationW(pathNames, volumeLabel, ARRAYSIZE(volumeLabel),
                            &serialNumber, &maxComponentLen, &fileSystemFlags,
                            fileSystemName, ARRAYSIZE(fileSystemName));

                        GetDiskFreeSpaceExW(pathNames, NULL, &totalBytes, &freeBytes);

                        // Format: "C:\ [System] - NTFS - 500 GB"
                        result << pathNames;
                        if (wcslen(volumeLabel) > 0) {
                            result << L" [" << volumeLabel << L"]";
                        }
                        result << L" - " << fileSystemName;
                        
                        double totalGB = totalBytes.QuadPart / (1024.0 * 1024.0 * 1024.0);
                        result << L" - " << std::fixed << static_cast<int>(totalGB) << L" GB";
                        result << L"\n";
                    }
                }

            } while (FindNextVolumeW(hFind, volumeName, ARRAYSIZE(volumeName)));

            FindVolumeClose(hFind);

            std::wstring resultStr = result.str();
            if (resultStr.length() >= (size_t)bufferSize) {
                SetLastErrorMessage(L"Buffer too small");
                return -2;
            }

            wcscpy_s(buffer, bufferSize, resultStr.c_str());
            return 0;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in EnumerateVolumes");
            return -99;
        }
    }

    BACKUPENGINE_API int EnumerateDisks(wchar_t* buffer, int bufferSize) {
        if (!buffer || bufferSize <= 0) {
            SetLastErrorMessage(L"Invalid buffer");
            return -1;
        }

        try {
            std::wostringstream result;

            // Enumerate physical drives (\\.\PhysicalDrive0, \\.\PhysicalDrive1, etc.)
            for (int i = 0; i < 32; i++) {
                std::wstring drivePath = L"\\\\.\\PhysicalDrive" + std::to_wstring(i);
                
                HANDLE hDrive = CreateFileW(
                    drivePath.c_str(),
                    0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    NULL,
                    OPEN_EXISTING,
                    0,
                    NULL);

                if (hDrive != INVALID_HANDLE_VALUE) {
                    // Get disk geometry
                    DISK_GEOMETRY_EX diskGeometry = { 0 };
                    DWORD bytesReturned = 0;

                    if (DeviceIoControl(
                        hDrive,
                        IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
                        NULL, 0,
                        &diskGeometry, sizeof(diskGeometry),
                        &bytesReturned,
                        NULL)) {

                        double sizeGB = diskGeometry.DiskSize.QuadPart / (1024.0 * 1024.0 * 1024.0);
                        
                        result << L"Disk " << i << L" - ";
                        result << std::fixed << static_cast<int>(sizeGB) << L" GB";
                        result << L"\n";
                    }
                    else {
                        result << L"Disk " << i << L" - Unknown size\n";
                    }

                    CloseHandle(hDrive);
                }
            }

            std::wstring resultStr = result.str();
            if (resultStr.empty()) {
                SetLastErrorMessage(L"No disks found");
                return -3;
            }

            if (resultStr.length() >= (size_t)bufferSize) {
                SetLastErrorMessage(L"Buffer too small");
                return -2;
            }

            wcscpy_s(buffer, bufferSize, resultStr.c_str());
            return 0;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in EnumerateDisks");
            return -99;
        }
    }

    BACKUPENGINE_API int IsBootVolume(const wchar_t* volumePath, bool* isBootVolume) {
        if (!volumePath || !isBootVolume) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            *isBootVolume = false;

            // Check if volume contains Windows boot files
            std::wstring bootmgrPath = std::wstring(volumePath) + L"\\bootmgr";
            std::wstring bcdPath = std::wstring(volumePath) + L"\\Boot\\BCD";
            std::wstring windowsPath = std::wstring(volumePath) + L"\\Windows";

            DWORD bootmgrAttr = GetFileAttributesW(bootmgrPath.c_str());
            DWORD bcdAttr = GetFileAttributesW(bcdPath.c_str());
            DWORD windowsAttr = GetFileAttributesW(windowsPath.c_str());

            // If Windows directory exists or boot files exist, it's likely a boot volume
            if ((windowsAttr != INVALID_FILE_ATTRIBUTES) ||
                (bootmgrAttr != INVALID_FILE_ATTRIBUTES) ||
                (bcdAttr != INVALID_FILE_ATTRIBUTES)) {
                *isBootVolume = true;
            }

            return 0;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in IsBootVolume");
            return -99;
        }
    }
}
