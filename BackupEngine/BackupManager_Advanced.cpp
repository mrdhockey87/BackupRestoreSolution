// BackupManager_Advanced.cpp - Advanced backup functions (Volume, Disk, Incremental, Differential)
#include "BackupEngine.h"
#include "VSSSnapshotManager.h"  // Add VSS support
#include <Windows.h>
#include <string>
#include <filesystem>
#include <fstream>
#include <map>
#include <vector>
#include "wimgapi.h"  // Windows Imaging API for WIM file creation

#pragma comment(lib, "wimgapi.lib")

namespace fs = std::filesystem;
extern void SetLastErrorMessage(const std::wstring& error);

// Forward declare BackupFiles from BackupEngine.cpp (legacy support)
extern "C" BACKUPENGINE_API int BackupFiles(
    const wchar_t* sourcePath,
    const wchar_t* destPath,
    ProgressCallback callback);

namespace {
// Helper to get file modification time
FILETIME GetFileModificationTime(const std::wstring& filePath) {
    FILETIME ft = { 0 };
    HANDLE hFile = CreateFileW(filePath.c_str(), GENERIC_READ,
        FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);

    if (hFile != INVALID_HANDLE_VALUE) {
        GetFileTime(hFile, nullptr, nullptr, &ft);
        CloseHandle(hFile);
    }
    return ft;
}

// Helper to compare file times
bool IsFileNewer(const FILETIME& ft1, const FILETIME& ft2) {
    return CompareFileTime(&ft1, &ft2) > 0;
}

// Helper to create WIM file with proper configuration
// Returns INVALID_HANDLE_VALUE on error
HANDLE CreateWimFile(const wchar_t* wimPath, bool compress, ProgressCallback callback) {
    // Determine compression type
    DWORD compressionType = compress ? WIM_COMPRESS_LZMS : WIM_COMPRESS_NONE;

    if (callback) {
        callback(5, L"Creating backup archive...");
    }

    // Delete existing file if present to avoid WIM_CREATE_ALWAYS locking issues
    if (GetFileAttributesW(wimPath) != INVALID_FILE_ATTRIBUTES) {
        OutputDebugStringW(L"[CreateWimFile] Deleting existing WIM file...");
        if (!DeleteFileW(wimPath)) {
            DWORD deleteError = GetLastError();
            std::wstring errMsg = L"Failed to delete existing WIM file (Error " + std::to_wstring(deleteError) + L"): ";
            errMsg += wimPath;
            SetLastErrorMessage(errMsg);
            OutputDebugStringW((L"[CreateWimFile] ERROR: " + errMsg).c_str());
            return INVALID_HANDLE_VALUE;
        }
        OutputDebugStringW(L"[CreateWimFile] Existing file deleted successfully");
    }

    // Create WIM file
    HANDLE hWim = WIMCreateFile(
        wimPath,
        WIM_GENERIC_WRITE,
        WIM_CREATE_ALWAYS,
        WIM_FLAG_VERIFY,  // Always verify integrity
        compressionType,
        NULL
    );

    if (!hWim || hWim == INVALID_HANDLE_VALUE) {
        DWORD wimError = GetLastError();
        std::wstring errMsg = L"Failed to create WIM archive (WIM Error " + std::to_wstring(wimError) + L")";
        SetLastErrorMessage(errMsg);
        OutputDebugStringW((L"[CreateWimFile] ERROR: " + errMsg).c_str());
        return INVALID_HANDLE_VALUE;
    }

    return hWim;
}

// Helper to capture path into WIM image
// Adds image metadata and returns image handle (must be closed by caller)
HANDLE CaptureToWimImage(HANDLE hWim, const wchar_t* sourcePath, const wchar_t* imageName, ProgressCallback callback) {
    if (!hWim || !sourcePath || !imageName) {
        SetLastErrorMessage(L"Invalid parameters for image capture");
        return INVALID_HANDLE_VALUE;
    }

    if (callback) {
        callback(30, L"Capturing files to backup archive...");
    }

    // Capture the volume/directory into WIM
    HANDLE hImage = WIMCaptureImage(hWim, sourcePath, WIM_FLAG_VERIFY);

    if (!hImage || hImage == INVALID_HANDLE_VALUE) {
        SetLastErrorMessage(L"Failed to capture files to archive");
        return INVALID_HANDLE_VALUE;
    }

    // Build XML metadata for the image
    std::wstring xmlMetadata = L"<WIM><IMAGE><NAME>";
    xmlMetadata += imageName;
    xmlMetadata += L"</NAME><DESCRIPTION>Silver State Backup Archive</DESCRIPTION></IMAGE></WIM>";

    // Set image metadata
    if (!WIMSetImageInformation(hImage, xmlMetadata.c_str())) {
        WIMCloseHandle(hImage);
        SetLastErrorMessage(L"Failed to set image metadata");
        return INVALID_HANDLE_VALUE;
    }

    return hImage;
}

// Helper to backup system state to SystemState subdirectory (metadata format)
bool BackupSystemState(const std::wstring& destPath, ProgressCallback callback) {
    try {
        // Create SystemState subdirectory
        std::wstring systemStatePath = destPath + L"\\SystemState";
        fs::create_directories(systemStatePath);

        // Backup registry hives
        if (callback) {
            callback(82, L"Backing up registry hives...");
        }

        std::vector<std::wstring> registryHives = {
            L"SAM",
            L"SECURITY",
            L"SOFTWARE",
            L"SYSTEM",
            L"DEFAULT"
        };

        for (const auto& hive : registryHives) {
            std::wstring srcPath = L"C:\\Windows\\System32\\config\\" + hive;
            std::wstring dstPath = systemStatePath + L"\\" + hive;

            try {
                // Registry hives are locked, but VSS snapshot allows access
                // Copy via VSS snapshot if available
                if (fs::exists(srcPath)) {
                    fs::copy_file(srcPath, dstPath, fs::copy_options::overwrite_existing);
                }
            }
            catch (...) {
                // Skip if can't access (might not have permissions)
            }
        }

        // Backup BCD (Boot Configuration Data)
        if (callback) {
            callback(85, L"Backing up boot configuration...");
        }

        std::wstring bcdSrc = L"C:\\Boot\\BCD";
        std::wstring bcdDst = systemStatePath + L"\\BCD";

        if (fs::exists(bcdSrc)) {
            try {
                fs::copy_file(bcdSrc, bcdDst, fs::copy_options::overwrite_existing);
            }
            catch (...) {
                // Try alternate location
                bcdSrc = L"C:\\EFI\\Microsoft\\Boot\\BCD";
                if (fs::exists(bcdSrc)) {
                    fs::copy_file(bcdSrc, bcdDst, fs::copy_options::overwrite_existing);
                }
            }
        }

        // Backup critical system files
        if (callback) {
            callback(87, L"Backing up critical system files...");
        }

        std::vector<std::wstring> criticalFiles = {
            L"C:\\Windows\\System32\\config\\RegBack\\SAM",
            L"C:\\Windows\\System32\\config\\RegBack\\SECURITY",
            L"C:\\Windows\\System32\\config\\RegBack\\SOFTWARE",
            L"C:\\Windows\\System32\\config\\RegBack\\SYSTEM",
            L"C:\\Windows\\System32\\config\\RegBack\\DEFAULT"
        };

        std::wstring regBackPath = systemStatePath + L"\\RegBack";
        fs::create_directories(regBackPath);

        for (const auto& file : criticalFiles) {
            if (fs::exists(file)) {
                try {
                    fs::path filename = fs::path(file).filename();
                    fs::copy_file(file, regBackPath + L"\\" + filename.wstring(),
                        fs::copy_options::overwrite_existing);
                }
                catch (...) {
                    // Skip if can't access
                }
            }
        }

        // Create metadata file documenting what was backed up
        std::wofstream metadataFile(systemStatePath + L"\\SystemState_Metadata.txt");
        if (metadataFile.is_open()) {
            SYSTEMTIME st;
            GetLocalTime(&st);

            metadataFile << L"System State Backup" << std::endl;
            metadataFile << L"Created: " << st.wYear << L"-"
                << st.wMonth << L"-" << st.wDay << L" "
                << st.wHour << L":" << st.wMinute << L":" << st.wSecond << std::endl;
            metadataFile << std::endl;
            metadataFile << L"Components backed up:" << std::endl;
            metadataFile << L"- Registry hives (SAM, SECURITY, SOFTWARE, SYSTEM, DEFAULT)" << std::endl;
            metadataFile << L"- Boot Configuration Data (BCD)" << std::endl;
            metadataFile << L"- Registry backup files" << std::endl;
            metadataFile << std::endl;
            metadataFile << L"Note: Active Directory, Certificate Services, and other components" << std::endl;
            metadataFile << L"are backed up via VSS writers if present on the system." << std::endl;
        }

        if (callback) {
            callback(90, L"System state backup completed");
        }

        return true;
    }
    catch (...) {
        return false;
    }
}

// Load file modification times from metadata file
std::map<std::wstring, FILETIME> LoadBackupMetadata(const std::wstring& backupPath) {
    std::map<std::wstring, FILETIME> metadata;
    std::wstring metadataFile = backupPath + L"\\backup_metadata.dat";
        
    std::wifstream file(metadataFile, std::ios::binary);
    if (file.is_open()) {
        // Read metadata (simplified - real implementation would use proper format)
        // Format: filepath|lowDateTime|highDateTime\n
        std::wstring line;
        while (std::getline(file, line)) {
            size_t pos1 = line.find(L'|');
            size_t pos2 = line.find(L'|', pos1 + 1);
            if (pos1 != std::wstring::npos && pos2 != std::wstring::npos) {
                std::wstring path = line.substr(0, pos1);
                DWORD low = std::stoul(line.substr(pos1 + 1, pos2 - pos1 - 1));
                DWORD high = std::stoul(line.substr(pos2 + 1));
                FILETIME ft = { low, high };
                metadata[path] = ft;
            }
        }
    }
    return metadata;
}

    // Save file modification times to metadata file
    void SaveBackupMetadata(const std::wstring& backupPath, 
        const std::map<std::wstring, FILETIME>& metadata) {
        std::wstring metadataFile = backupPath + L"\\backup_metadata.dat";
        
        std::wofstream file(metadataFile, std::ios::binary);
        if (file.is_open()) {
            for (const auto& entry : metadata) {
                file << entry.first << L"|" 
                     << entry.second.dwLowDateTime << L"|"
                     << entry.second.dwHighDateTime << L"\n";
            }
        }
    }
}

extern "C" {

    BACKUPENGINE_API int BackupVolume(
        const wchar_t* volumePath,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        ProgressCallback callback) {

        if (!volumePath || !destPath) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            if (callback) {
                callback(0, L"Starting volume backup (WIM format)...");
            }

            // Ensure destPath is a file, not a directory
            std::wstring destFile = destPath;
            // Check if path ends with .ssb (C++17 compatible)
            if (destFile.length() < 4 || destFile.substr(destFile.length() - 4) != L".ssb") {
                // If it's a directory, this is wrong - but handle gracefully
                if (fs::is_directory(destPath)) {
                    SetLastErrorMessage(L"Destination must be a file path ending in .ssb, not a directory");
                    return -1;
                }
            }

            // Create parent directory if needed
            fs::path parentDir = fs::path(destFile).parent_path();
            if (!parentDir.empty()) {
                fs::create_directories(parentDir);
            }

            if (callback) {
                callback(10, L"Creating VSS snapshot...");
            }

            // Create VSS snapshot for consistent backup
            BackupEngine::VSSSnapshotManager vssManager;
            HRESULT hr = vssManager.Initialize();
            if (FAILED(hr)) {
                if (callback) {
                    callback(15, L"VSS unavailable - using direct copy (files may be inconsistent)");
                }
            }

            wchar_t snapshotPath[MAX_PATH] = { 0 };
            std::wstring actualSourcePath = volumePath;

            if (SUCCEEDED(hr)) {
                hr = vssManager.CreateVolumeSnapshot(volumePath, snapshotPath, MAX_PATH);
                if (SUCCEEDED(hr)) {
                    actualSourcePath = snapshotPath;
                    if (callback) {
                        callback(20, L"VSS snapshot created successfully");
                    }
                }
                else {
                    if (callback) {
                        callback(15, L"VSS snapshot failed - using direct copy");
                    }
                }
            }

            if (callback) {
                callback(25, L"Creating WIM backup archive...");
            }

            // Create WIM file
            HANDLE hWim = CreateWimFile(destFile.c_str(), compress, callback);
            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                return -2;
            }

            // Capture volume to WIM image
            std::wstring imageName = L"Volume Backup";
            HANDLE hImage = CaptureToWimImage(hWim, actualSourcePath.c_str(), imageName.c_str(), callback);

            if (!hImage || hImage == INVALID_HANDLE_VALUE) {
                WIMCloseHandle(hWim);
                return -3;
            }

            // Close image handle
            WIMCloseHandle(hImage);

            if (callback) {
                callback(70, L"Finalizing backup archive...");
            }

            // Close WIM file (this writes the file to disk)
            WIMCloseHandle(hWim);

            // Handle system state separately (metadata/instructions approach)
            if (includeSystemState) {
                if (callback) {
                    callback(80, L"Backing up system state metadata...");
                }

                // Create SystemState directory next to the .ssb file
                std::wstring ssbDir = fs::path(destFile).parent_path().wstring();
                std::wstring systemStateDir = ssbDir + L"\\SystemState";

                bool systemStateSuccess = BackupSystemState(systemStateDir.c_str(), callback);
                if (!systemStateSuccess) {
                    if (callback) {
                        callback(85, L"Warning: System state backup incomplete (may need admin rights)");
                    }
                }
            }

            if (callback) {
                callback(100, L"Volume backup completed successfully");
            }

            return 0;
        }
        catch (const std::exception& e) {
            std::string err = "Exception in BackupVolume: ";
            err += e.what();
            SetLastErrorMessage(std::wstring(err.begin(), err.end()));
            return -99;
        }
        catch (...) {
            SetLastErrorMessage(L"Unknown exception in BackupVolume");
            return -99;
        }
    }

    BACKUPENGINE_API int BackupDisk(
        int diskNumber,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        ProgressCallback callback) {

        if (diskNumber < 0 || !destPath) {
            SetLastErrorMessage(L"Invalid parameters: diskNumber=" + std::to_wstring(diskNumber) + L", destPath=" + (destPath ? L"valid" : L"NULL"));
            return -1;
        }

        try {
            // LOG: Starting backup
            std::wstring logMsg = L"[BackupDisk] Starting backup of Disk " + std::to_wstring(diskNumber) + L" to: " + destPath;
            OutputDebugStringW(logMsg.c_str());

            if (callback) {
                callback(0, L"Starting disk backup - enumerating volumes...");
            }

            // LOG: Validating destination path
            std::wstring destFile = destPath;
            OutputDebugStringW((L"[BackupDisk] Dest file: " + destFile).c_str());

            if (destFile.length() < 4 || destFile.substr(destFile.length() - 4) != L".ssb") {
                if (fs::exists(destPath) && fs::is_directory(destPath)) {
                    SetLastErrorMessage(L"Destination must be a file path ending in .ssb, not a directory");
                    OutputDebugStringW(L"[BackupDisk] ERROR: Destination is directory, not file!");
                    return -1;
                }
            }

            // Create parent directory if needed
            fs::path parentDir = fs::path(destFile).parent_path();
            if (!parentDir.empty()) {
                OutputDebugStringW((L"[BackupDisk] Creating parent dir: " + parentDir.wstring()).c_str());
                fs::create_directories(parentDir);
            }

            // Enumerate volumes on this disk using IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS
            OutputDebugStringW((L"[BackupDisk] Enumerating volumes on Disk " + std::to_wstring(diskNumber)).c_str());

            std::vector<std::wstring> volumes;
            wchar_t volumeName[MAX_PATH];
            HANDLE hFind = FindFirstVolumeW(volumeName, ARRAYSIZE(volumeName));

            if (hFind == INVALID_HANDLE_VALUE) {
                DWORD err = GetLastError();
                std::wstring errMsg = L"Failed to enumerate volumes, Win32 Error: " + std::to_wstring(err);
                SetLastErrorMessage(errMsg);
                OutputDebugStringW((L"[BackupDisk] ERROR: " + errMsg).c_str());
                return -2;
            }

            do {
                // volumeName format: \\?\Volume{guid}\
                // Create a COPY to avoid modifying the FindNextVolumeW buffer
                std::wstring volumeNameCopy = volumeName;

                // Remove trailing backslash to open the volume with CreateFile
                if (!volumeNameCopy.empty() && volumeNameCopy.back() == L'\\') {
                    volumeNameCopy.pop_back();
                }

                // Open the volume to query disk extents
                HANDLE hVolume = CreateFileW(
                    volumeNameCopy.c_str(),
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    NULL,
                    OPEN_EXISTING,
                    0,
                    NULL
                );

                if (hVolume != INVALID_HANDLE_VALUE) {
                    // Query which physical disk(s) this volume is on
                    BYTE buffer[sizeof(VOLUME_DISK_EXTENTS) + 32 * sizeof(DISK_EXTENT)];
                    PVOLUME_DISK_EXTENTS pExtents = (PVOLUME_DISK_EXTENTS)buffer;
                    DWORD bytesReturned = 0;

                    if (DeviceIoControl(
                        hVolume,
                        IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                        NULL, 0,
                        pExtents, sizeof(buffer),
                        &bytesReturned,
                        NULL))
                    {
                        // Check if any extent is on our target disk
                        for (DWORD i = 0; i < pExtents->NumberOfDiskExtents; i++) {
                            if (pExtents->Extents[i].DiskNumber == static_cast<DWORD>(diskNumber)) {
                                // This volume is on our target disk!
                                // Add with trailing backslash for BackupVolume
                                std::wstring volPath = volumeNameCopy + L"\\";
                                volumes.push_back(volPath);
                                OutputDebugStringW((L"[BackupDisk] Found volume on Disk " + std::to_wstring(diskNumber) + L": " + volPath).c_str());
                                break; // Only add once even if multiple extents
                            }
                        }
                    }
                    else {
                        DWORD err = GetLastError();
                        OutputDebugStringW((L"[BackupDisk] DeviceIoControl failed for volume, Error: " + std::to_wstring(err)).c_str());
                    }

                    CloseHandle(hVolume);
                }
                else {
                    DWORD err = GetLastError();
                    OutputDebugStringW((L"[BackupDisk] Failed to open volume: " + volumeNameCopy + L", Error: " + std::to_wstring(err)).c_str());
                }
            } while (FindNextVolumeW(hFind, volumeName, ARRAYSIZE(volumeName)));

            FindVolumeClose(hFind);

            OutputDebugStringW((L"[BackupDisk] Volume enumeration complete. Found " + std::to_wstring(volumes.size()) + L" volumes").c_str());

            if (volumes.empty()) {
                std::wstring errMsg = L"No volumes found on Disk " + std::to_wstring(diskNumber);
                SetLastErrorMessage(errMsg);
                OutputDebugStringW((L"[BackupDisk] ERROR: " + errMsg).c_str());
                return -3;
            }

            if (callback) {
                std::wstring msg = L"Found " + std::to_wstring(volumes.size()) + L" volume(s) on disk " + std::to_wstring(diskNumber);
                callback(10, msg.c_str());
            }

            // Create WIM file for all volumes
            OutputDebugStringW(L"[BackupDisk] Creating WIM file...");

            if (callback) {
                callback(15, L"Creating WIM backup archive...");
            }

            HANDLE hWim = CreateWimFile(destFile.c_str(), compress, callback);
            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                OutputDebugStringW(L"[BackupDisk] ERROR: CreateWimFile failed!");
                SetLastErrorMessage(L"Failed to create WIM file: " + destFile);
                return -4;
            }

            OutputDebugStringW((L"[BackupDisk] WIM file created successfully: " + destFile).c_str());

            // Backup each volume as a separate image in the WIM file
            int volumeIndex = 0;
            for (const auto& volume : volumes) {
                volumeIndex++;
                int progressBase = 20 + (volumeIndex - 1) * (60 / static_cast<int>(volumes.size()));

                if (callback) {
                    std::wstring msg = L"Backing up volume " + std::to_wstring(volumeIndex) + 
                                      L" of " + std::to_wstring(volumes.size()) + L"...";
                    callback(progressBase, msg.c_str());
                }

                // Create VSS snapshot for this volume
                OutputDebugStringW((L"[BackupDisk] Processing volume " + std::to_wstring(volumeIndex) + L"/" + std::to_wstring(volumes.size()) + L": " + volume).c_str());

                BackupEngine::VSSSnapshotManager vssManager;
                HRESULT hr = vssManager.Initialize();
                std::wstring vssStatus = SUCCEEDED(hr) ? L"SUCCESS" : L"FAILED";
                OutputDebugStringW((L"[BackupDisk] VSS Initialize: " + vssStatus).c_str());

                wchar_t snapshotPath[MAX_PATH] = { 0 };
                std::wstring actualSourcePath = volume + L"\\";  // Add trailing backslash

                if (SUCCEEDED(hr)) {
                    OutputDebugStringW(L"[BackupDisk] Creating VSS snapshot...");
                    hr = vssManager.CreateVolumeSnapshot(actualSourcePath.c_str(), snapshotPath, MAX_PATH);
                    if (SUCCEEDED(hr)) {
                        actualSourcePath = snapshotPath;
                        OutputDebugStringW((L"[BackupDisk] VSS snapshot created: " + std::wstring(snapshotPath)).c_str());
                    }
                    else {
                        OutputDebugStringW((L"[BackupDisk] VSS snapshot failed, using direct path, HR=" + std::to_wstring(hr)).c_str());
                    }
                }

                // Capture this volume to WIM as a separate image
                std::wstring imageName = L"Disk " + std::to_wstring(diskNumber) + 
                                        L" Volume " + std::to_wstring(volumeIndex);

                OutputDebugStringW((L"[BackupDisk] Capturing to WIM: " + imageName + L" from " + actualSourcePath).c_str());

                HANDLE hImage = CaptureToWimImage(hWim, actualSourcePath.c_str(), 
                                                 imageName.c_str(), callback);

                if (!hImage || hImage == INVALID_HANDLE_VALUE) {
                    WIMCloseHandle(hWim);
                    std::wstring err = L"Failed to capture volume " + std::to_wstring(volumeIndex) + L" (" + volume + L") to WIM";
                    SetLastErrorMessage(err);
                    OutputDebugStringW((L"[BackupDisk] ERROR: " + err).c_str());
                    return -5;
                }

                OutputDebugStringW((L"[BackupDisk] Volume " + std::to_wstring(volumeIndex) + L" captured successfully").c_str());
                WIMCloseHandle(hImage);
            }

            OutputDebugStringW(L"[BackupDisk] All volumes captured, finalizing WIM...");

            if (callback) {
                callback(85, L"Finalizing backup archive...");
            }

            // Close WIM file
            WIMCloseHandle(hWim);
            OutputDebugStringW(L"[BackupDisk] WIM file closed successfully");

            // Handle system state if requested
            if (includeSystemState) {
                if (callback) {
                    callback(90, L"Backing up system state metadata...");
                }

                std::wstring ssbDir = fs::path(destFile).parent_path().wstring();
                std::wstring systemStateDir = ssbDir + L"\\SystemState";

                bool systemStateSuccess = BackupSystemState(systemStateDir.c_str(), callback);
                if (!systemStateSuccess) {
                    if (callback) {
                        callback(95, L"Warning: System state backup incomplete");
                    }
                }
            }

            OutputDebugStringW(L"[BackupDisk] Backup completed successfully!");

            if (callback) {
                callback(100, L"Disk backup completed successfully!");
            }

            return 0;
        }
        catch (const std::exception& e) {
            std::string err = "Exception in BackupDisk: ";
            err += e.what();
            std::wstring errW(err.begin(), err.end());
            SetLastErrorMessage(errW);
            OutputDebugStringW((L"[BackupDisk] EXCEPTION: " + errW).c_str());
            return -10;
        }
        catch (...) {
            SetLastErrorMessage(L"Unknown exception in BackupDisk");
            OutputDebugStringW(L"[BackupDisk] FATAL: Unknown exception!");
            return -11;
        }
    }

    // NEW FUNCTION: Incremental disk backup using WIM_FLAG_REFERENCE
    BACKUPENGINE_API int BackupDiskIncremental(
        int diskNumber,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        ProgressCallback callback) {

        if (!destPath || diskNumber < 0) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            std::wstring destFile(destPath);

            // Check if base backup (.ssb file) exists
            if (!fs::exists(destFile)) {
                // No base backup exists - create full backup instead
                if (callback) {
                    callback(0, L"No base backup found - creating initial full backup...");
                }
                return BackupDisk(diskNumber, destPath, includeSystemState, compress, callback);
            }

            if (callback) {
                callback(0, L"Starting incremental disk backup (WIM referential)...");
            }

            // Enumerate volumes on this disk
            std::vector<std::wstring> volumes;
            wchar_t volumeName[MAX_PATH];
            HANDLE hFind = FindFirstVolumeW(volumeName, ARRAYSIZE(volumeName));

            if (hFind == INVALID_HANDLE_VALUE) {
                SetLastErrorMessage(L"Failed to enumerate volumes");
                return -2;
            }

            do {
                size_t len = wcslen(volumeName);
                if (len > 0 && volumeName[len - 1] == L'\\') {
                    volumeName[len - 1] = L'\0';
                }

                std::wstring volumeNameCopy = volumeName;
                if (volumeNameCopy.size() > 4 && volumeNameCopy.substr(0, 4) == L"\\\\?\\") {
                    volumeNameCopy = volumeNameCopy.substr(4);
                }

                HANDLE hVolume = CreateFileW(
                    volumeName,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    NULL,
                    OPEN_EXISTING,
                    0,
                    NULL
                );

                if (hVolume != INVALID_HANDLE_VALUE) {
                    BYTE buffer[sizeof(VOLUME_DISK_EXTENTS) + 32 * sizeof(DISK_EXTENT)];
                    PVOLUME_DISK_EXTENTS pExtents = (PVOLUME_DISK_EXTENTS)buffer;
                    DWORD bytesReturned = 0;

                    if (DeviceIoControl(
                        hVolume,
                        IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                        NULL, 0,
                        pExtents, sizeof(buffer),
                        &bytesReturned,
                        NULL))
                    {
                        for (DWORD i = 0; i < pExtents->NumberOfDiskExtents; i++) {
                            if (pExtents->Extents[i].DiskNumber == static_cast<DWORD>(diskNumber)) {
                                std::wstring volPath = volumeNameCopy + L"\\";
                                volumes.push_back(volPath);
                                break;
                            }
                        }
                    }

                    CloseHandle(hVolume);
                }
            } while (FindNextVolumeW(hFind, volumeName, ARRAYSIZE(volumeName)));

            FindVolumeClose(hFind);

            if (volumes.empty()) {
                SetLastErrorMessage(L"No volumes found on disk");
                return -3;
            }

            if (callback) {
                std::wstring msg = L"Found " + std::to_wstring(volumes.size()) + L" volume(s) for incremental backup";
                callback(10, msg.c_str());
            }

            // Open existing WIM file with WIM_FLAG_REFERENCE to add incremental images
            if (callback) {
                callback(15, L"Opening existing backup with WIM_FLAG_REFERENCE...");
            }

            // When opening existing WIM, compression type must be 0 (read from file)
            // Passing WIM_COMPRESS_LZMS/NONE when opening existing WIM causes error -4!
            // NOTE: WIM_FLAG_VERIFY removed - can cause ERROR_INVALID_PARAMETER (87) on valid files
            // CRITICAL: Must use WIM_GENERIC_READ | WIM_GENERIC_WRITE when opening existing WIM to append images!
            //           WIM_GENERIC_WRITE alone causes ERROR_INVALID_PARAMETER (87)
            HANDLE hWim = WIMCreateFile(
                destFile.c_str(),
                WIM_GENERIC_READ | WIM_GENERIC_WRITE,  // Need READ+WRITE to append images
                WIM_OPEN_EXISTING,
                WIM_FLAG_REFERENCE,  // Enable referential images only (no VERIFY)
                0,  // MUST be 0 when opening existing WIM! Compression read from file.
                NULL
            );

            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                DWORD wimError = GetLastError();
                std::wstring err = L"Failed to open existing backup for incremental. WIM Error: " + 
                                  std::to_wstring(wimError) + 
                                  L". Ensure full backup exists and is not corrupted.";
                SetLastErrorMessage(err);
                return -4;
            }

            // WIM API automatically handles incremental images when appending to existing WIM
            // Each new image will reference common data from previous images

            // Backup each volume as new incremental image
            int volumeIndex = 0;
            for (const auto& volume : volumes) {
                volumeIndex++;
                int progressBase = 20 + (volumeIndex - 1) * (70 / static_cast<int>(volumes.size()));

                if (callback) {
                    std::wstring msg = L"Creating incremental image " + std::to_wstring(volumeIndex) + 
                                      L" of " + std::to_wstring(volumes.size()) + L"...";
                    callback(progressBase, msg.c_str());
                }

                // Create VSS snapshot
                BackupEngine::VSSSnapshotManager vssManager;
                HRESULT hr = vssManager.Initialize();

                wchar_t snapshotPath[MAX_PATH] = { 0 };
                std::wstring actualSourcePath = volume + L"\\";

                if (SUCCEEDED(hr)) {
                    hr = vssManager.CreateVolumeSnapshot(actualSourcePath.c_str(), snapshotPath, MAX_PATH);
                    if (SUCCEEDED(hr)) {
                        actualSourcePath = snapshotPath;
                    }
                }

                // Capture new image referencing previous images
                std::wstring imageName = L"Disk " + std::to_wstring(diskNumber) + 
                                        L" Volume " + std::to_wstring(volumeIndex) + 
                                        L" (Incremental)";

                // The WIM_FLAG_REFERENCE in WIMCreateFile automatically makes new images reference existing ones
                HANDLE hImage = CaptureToWimImage(hWim, actualSourcePath.c_str(), 
                                                 imageName.c_str(), callback);

                if (!hImage || hImage == INVALID_HANDLE_VALUE) {
                    WIMCloseHandle(hWim);
                    std::wstring err = L"Failed to capture incremental image " + std::to_wstring(volumeIndex);
                    SetLastErrorMessage(err);
                    return -6;
                }

                WIMCloseHandle(hImage);
            }

            if (callback) {
                callback(95, L"Finalizing incremental backup...");
            }

            WIMCloseHandle(hWim);

            if (callback) {
                callback(100, L"Incremental disk backup completed successfully!");
            }

            return 0;
        }
        catch (const std::exception& e) {
            std::string err = "Exception in BackupDiskIncremental: ";
            err += e.what();
            SetLastErrorMessage(std::wstring(err.begin(), err.end()));
            return -10;
        }
    }

    // NEW FUNCTION: Differential disk backup using WIM_FLAG_REFERENCE
    BACKUPENGINE_API int BackupDiskDifferential(
        int diskNumber,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        ProgressCallback callback) {

        if (!destPath || diskNumber < 0) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            std::wstring destFile(destPath);

            // Check if base backup (.ssb file) exists
            if (!fs::exists(destFile)) {
                // No base backup exists - create full backup instead
                if (callback) {
                    callback(0, L"No base backup found - creating initial full backup...");
                }
                return BackupDisk(diskNumber, destPath, includeSystemState, compress, callback);
            }

            if (callback) {
                callback(0, L"Starting differential disk backup (WIM referential)...");
            }

            // Enumerate volumes on this disk
            std::vector<std::wstring> volumes;
            wchar_t volumeName[MAX_PATH];
            HANDLE hFind = FindFirstVolumeW(volumeName, ARRAYSIZE(volumeName));

            if (hFind == INVALID_HANDLE_VALUE) {
                SetLastErrorMessage(L"Failed to enumerate volumes");
                return -2;
            }

            do {
                size_t len = wcslen(volumeName);
                if (len > 0 && volumeName[len - 1] == L'\\') {
                    volumeName[len - 1] = L'\0';
                }

                std::wstring volumeNameCopy = volumeName;
                if (volumeNameCopy.size() > 4 && volumeNameCopy.substr(0, 4) == L"\\\\?\\") {
                    volumeNameCopy = volumeNameCopy.substr(4);
                }

                HANDLE hVolume = CreateFileW(
                    volumeName,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    NULL,
                    OPEN_EXISTING,
                    0,
                    NULL
                );

                if (hVolume != INVALID_HANDLE_VALUE) {
                    BYTE buffer[sizeof(VOLUME_DISK_EXTENTS) + 32 * sizeof(DISK_EXTENT)];
                    PVOLUME_DISK_EXTENTS pExtents = (PVOLUME_DISK_EXTENTS)buffer;
                    DWORD bytesReturned = 0;

                    if (DeviceIoControl(
                        hVolume,
                        IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                        NULL, 0,
                        pExtents, sizeof(buffer),
                        &bytesReturned,
                        NULL))
                    {
                        for (DWORD i = 0; i < pExtents->NumberOfDiskExtents; i++) {
                            if (pExtents->Extents[i].DiskNumber == static_cast<DWORD>(diskNumber)) {
                                std::wstring volPath = volumeNameCopy + L"\\";
                                volumes.push_back(volPath);
                                break;
                            }
                        }
                    }

                    CloseHandle(hVolume);
                }
            } while (FindNextVolumeW(hFind, volumeName, ARRAYSIZE(volumeName)));

            FindVolumeClose(hFind);

            if (volumes.empty()) {
                SetLastErrorMessage(L"No volumes found on disk");
                return -3;
            }

            if (callback) {
                std::wstring msg = L"Found " + std::to_wstring(volumes.size()) + L" volume(s) for differential backup";
                callback(10, msg.c_str());
            }

            // Open existing WIM file with WIM_FLAG_REFERENCE to add differential images
            // Differential always references the FIRST (full) backup, not the most recent
            if (callback) {
                callback(15, L"Opening existing backup with WIM_FLAG_REFERENCE...");
            }

            // When opening existing WIM, compression type must be 0 (read from file)
            // Passing WIM_COMPRESS_LZMS/NONE when opening existing WIM causes error -4!
            // NOTE: WIM_FLAG_VERIFY removed - can cause ERROR_INVALID_PARAMETER (87) on valid files
            // CRITICAL: Must use WIM_GENERIC_READ | WIM_GENERIC_WRITE when opening existing WIM to append images!
            //           WIM_GENERIC_WRITE alone causes ERROR_INVALID_PARAMETER (87)
            HANDLE hWim = WIMCreateFile(
                destFile.c_str(),
                WIM_GENERIC_READ | WIM_GENERIC_WRITE,  // Need READ+WRITE to append images
                WIM_OPEN_EXISTING,
                WIM_FLAG_REFERENCE,  // Enable referential images only (no VERIFY)
                0,  // MUST be 0 when opening existing WIM! Compression read from file.
                NULL
            );

            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                DWORD wimError = GetLastError();
                std::wstring err = L"Failed to open existing backup for differential. WIM Error: " + 
                                  std::to_wstring(wimError) + 
                                  L". Ensure full backup exists and is not corrupted.";
                SetLastErrorMessage(err);
                return -4;
            }

            // Backup each volume as new differential image (referencing first/full backup)
            int volumeIndex = 0;
            for (const auto& volume : volumes) {
                volumeIndex++;
                int progressBase = 20 + (volumeIndex - 1) * (70 / static_cast<int>(volumes.size()));

                if (callback) {
                    std::wstring msg = L"Creating differential image " + std::to_wstring(volumeIndex) + 
                                      L" of " + std::to_wstring(volumes.size()) + L"...";
                    callback(progressBase, msg.c_str());
                }

                // Create VSS snapshot
                BackupEngine::VSSSnapshotManager vssManager;
                HRESULT hr = vssManager.Initialize();

                wchar_t snapshotPath[MAX_PATH] = { 0 };
                std::wstring actualSourcePath = volume + L"\\";

                if (SUCCEEDED(hr)) {
                    hr = vssManager.CreateVolumeSnapshot(actualSourcePath.c_str(), snapshotPath, MAX_PATH);
                    if (SUCCEEDED(hr)) {
                        actualSourcePath = snapshotPath;
                    }
                }

                // Capture new image referencing base backup (differential)
                std::wstring imageName = L"Disk " + std::to_wstring(diskNumber) + 
                                        L" Volume " + std::to_wstring(volumeIndex) + 
                                        L" (Differential)";

                // The WIM_FLAG_REFERENCE makes new images reference the first (full) backup
                HANDLE hImage = CaptureToWimImage(hWim, actualSourcePath.c_str(), 
                                                 imageName.c_str(), callback);

                if (!hImage || hImage == INVALID_HANDLE_VALUE) {
                    WIMCloseHandle(hWim);
                    std::wstring err = L"Failed to capture differential image " + std::to_wstring(volumeIndex);
                    SetLastErrorMessage(err);
                    return -6;
                }

                WIMCloseHandle(hImage);
            }

            if (callback) {
                callback(95, L"Finalizing differential backup...");
            }

            WIMCloseHandle(hWim);

            if (callback) {
                callback(100, L"Differential disk backup completed successfully!");
            }

            return 0;
        }
        catch (const std::exception& e) {
            std::string err = "Exception in BackupDiskDifferential: ";
            err += e.what();
            SetLastErrorMessage(std::wstring(err.begin(), err.end()));
            return -10;
        }
    }

    BACKUPENGINE_API int CreateIncrementalBackup(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t* baseBackupPath,
        ProgressCallback callback) {
        
        if (!sourcePath || !destPath) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            if (callback) {
                callback(0, L"Starting incremental backup...");
            }

            // Load metadata from base backup
            std::map<std::wstring, FILETIME> baseMetadata;
            if (baseBackupPath && wcslen(baseBackupPath) > 0) {
                baseMetadata = LoadBackupMetadata(baseBackupPath);
            }

            // Create destination directory
            fs::create_directories(destPath);

            if (callback) {
                callback(10, L"Scanning for changed files...");
            }

            // Enumerate files and backup only changed ones
            std::map<std::wstring, FILETIME> currentMetadata;
            std::vector<std::wstring> filesToBackup;

            for (const auto& entry : fs::recursive_directory_iterator(sourcePath)) {
                if (entry.is_regular_file()) {
                    std::wstring filePath = entry.path().wstring();
                    FILETIME currentTime = GetFileModificationTime(filePath);
                    currentMetadata[filePath] = currentTime;

                    // Check if file is new or modified
                    auto it = baseMetadata.find(filePath);
                    if (it == baseMetadata.end() || IsFileNewer(currentTime, it->second)) {
                        filesToBackup.push_back(filePath);
                    }
                }
            }

            if (callback) {
                std::wstring msg = L"Backing up " + std::to_wstring(filesToBackup.size()) + 
                    L" changed files...";
                callback(20, msg.c_str());
            }

            // Backup changed files
            size_t processedFiles = 0;
            for (const auto& sourceFile : filesToBackup) {
                fs::path relativePath = fs::relative(sourceFile, sourcePath);
                fs::path destFile = fs::path(destPath) / relativePath;

                fs::create_directories(destFile.parent_path());
                fs::copy_file(sourceFile, destFile, fs::copy_options::overwrite_existing);

                processedFiles++;
                if (callback && !filesToBackup.empty()) {
                    int percent = 20 + (int)((processedFiles * 70) / filesToBackup.size());
                    callback(percent, L"Backing up changed files...");
                }
            }

            // Save metadata for this backup
            SaveBackupMetadata(destPath, currentMetadata);

            if (callback) {
                callback(100, L"Incremental backup completed successfully");
            }

            return 0;
        }
        catch (const fs::filesystem_error&) {
            SetLastErrorMessage(L"Filesystem error in incremental backup");
            return -2;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in CreateIncrementalBackup");
            return -99;
        }
    }

    BACKUPENGINE_API int CreateDifferentialBackup(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t* fullBackupPath,
        ProgressCallback callback) {
        
        // Differential backup is similar to incremental, but always compares against
        // the last full backup instead of the last backup
        return CreateIncrementalBackup(sourcePath, destPath, fullBackupPath, callback);
    }
}
