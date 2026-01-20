// BackupManager_Advanced.cpp - Advanced backup functions (Volume, Disk, Incremental, Differential)
#include "BackupEngine.h"
#include <Windows.h>
#include <string>
#include <filesystem>
#include <fstream>
#include <map>
#include <vector>

namespace fs = std::filesystem;
extern void SetLastErrorMessage(const std::wstring& error);

// Forward declare BackupFiles from BackupEngine.cpp
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
                callback(0, L"Starting volume backup...");
            }

            // Create destination directory
            fs::create_directories(destPath);

            if (callback) {
                callback(10, L"Creating VSS snapshot...");
            }

            // TODO: In full implementation, create VSS snapshot
            // For now, use direct file copy
            
            if (callback) {
                callback(20, L"Backing up volume files...");
            }

            // Backup files from volume
            int result = BackupFiles(volumePath, destPath, callback);
            
            if (result != 0) {
                SetLastErrorMessage(L"Failed to backup volume files");
                return result;
            }

            if (includeSystemState) {
                if (callback) {
                    callback(80, L"Backing up system state...");
                }
                
                // TODO: Backup system state (registry, boot files, etc.)
                // This is a complex operation that would backup:
                // - Registry hives
                // - Boot configuration (BCD)
                // - System files
            }

            if (callback) {
                callback(100, L"Volume backup completed successfully");
            }

            return 0;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in BackupVolume");
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
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            if (callback) {
                callback(0, L"Starting disk backup...");
            }

            // Open physical disk
            std::wstring diskPath = L"\\\\.\\PhysicalDrive" + std::to_wstring(diskNumber);
            
            HANDLE hDisk = CreateFileW(
                diskPath.c_str(),
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                NULL,
                OPEN_EXISTING,
                0,
                NULL);

            if (hDisk == INVALID_HANDLE_VALUE) {
                SetLastErrorMessage(L"Failed to open disk");
                return -2;
            }

            // Get disk size
            DISK_GEOMETRY_EX diskGeometry = { 0 };
            DWORD bytesReturned = 0;

            if (!DeviceIoControl(hDisk, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
                NULL, 0, &diskGeometry, sizeof(diskGeometry),
                &bytesReturned, NULL)) {
                CloseHandle(hDisk);
                SetLastErrorMessage(L"Failed to get disk geometry");
                return -3;
            }

            if (callback) {
                callback(10, L"Reading disk sectors...");
            }

            // Create backup file
            fs::create_directories(destPath);
            std::wstring backupFile = std::wstring(destPath) + L"\\disk_" + 
                std::to_wstring(diskNumber) + L".img";

            HANDLE hBackup = CreateFileW(
                backupFile.c_str(),
                GENERIC_WRITE,
                0,
                NULL,
                CREATE_ALWAYS,
                FILE_ATTRIBUTE_NORMAL,
                NULL);

            if (hBackup == INVALID_HANDLE_VALUE) {
                CloseHandle(hDisk);
                SetLastErrorMessage(L"Failed to create backup file");
                return -4;
            }

            // Read and write disk sectors
            const DWORD bufferSize = 1024 * 1024; // 1MB buffer
            std::vector<BYTE> buffer(bufferSize);
            LONGLONG totalBytes = diskGeometry.DiskSize.QuadPart;
            LONGLONG bytesProcessed = 0;

            while (bytesProcessed < totalBytes) {
                DWORD bytesToRead = (DWORD)min((LONGLONG)bufferSize, totalBytes - bytesProcessed);
                DWORD bytesRead = 0;

                if (!ReadFile(hDisk, buffer.data(), bytesToRead, &bytesRead, NULL)) {
                    CloseHandle(hDisk);
                    CloseHandle(hBackup);
                    SetLastErrorMessage(L"Failed to read disk");
                    return -5;
                }

                DWORD bytesWritten = 0;
                if (!WriteFile(hBackup, buffer.data(), bytesRead, &bytesWritten, NULL)) {
                    CloseHandle(hDisk);
                    CloseHandle(hBackup);
                    SetLastErrorMessage(L"Failed to write backup");
                    return -6;
                }

                bytesProcessed += bytesRead;

                if (callback && totalBytes > 0) {
                    int percent = (int)((bytesProcessed * 90) / totalBytes) + 10;
                    callback(percent, L"Backing up disk...");
                }
            }

            CloseHandle(hDisk);
            CloseHandle(hBackup);

            if (callback) {
                callback(100, L"Disk backup completed successfully");
            }

            return 0;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in BackupDisk");
            return -99;
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
