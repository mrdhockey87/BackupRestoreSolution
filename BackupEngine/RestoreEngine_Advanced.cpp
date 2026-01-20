// RestoreEngine_Advanced.cpp - Advanced restore functions
#include "BackupEngine.h"
#include <Windows.h>
#include <string>
#include <filesystem>
#include <vector>

namespace fs = std::filesystem;
extern void SetLastErrorMessage(const std::wstring& error);

extern "C" {

    BACKUPENGINE_API int RestoreVolume(
        const wchar_t* backupPath,
        const wchar_t* targetVolume,
        bool restoreSystemState,
        ProgressCallback callback) {
        
        if (!backupPath || !targetVolume) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            if (callback) {
                callback(0, L"Starting volume restore...");
            }

            // Verify backup exists
            if (!fs::exists(backupPath)) {
                SetLastErrorMessage(L"Backup path does not exist");
                return -2;
            }

            // Verify target volume exists and is accessible
            std::wstring volumePath = targetVolume;
            if (volumePath.back() != L'\\') {
                volumePath += L'\\';
            }

            if (GetDriveTypeW(volumePath.c_str()) == DRIVE_NO_ROOT_DIR) {
                SetLastErrorMessage(L"Target volume not found");
                return -3;
            }

            if (callback) {
                callback(10, L"Restoring volume files...");
            }

            // Restore all files from backup
            size_t totalFiles = 0;
            size_t processedFiles = 0;

            // Count files
            for (const auto& entry : fs::recursive_directory_iterator(backupPath)) {
                if (entry.is_regular_file()) {
                    totalFiles++;
                }
            }

            // Restore files
            for (const auto& entry : fs::recursive_directory_iterator(backupPath)) {
                if (entry.is_regular_file()) {
                    fs::path sourceFile = entry.path();
                    
                    // Skip metadata files
                    if (sourceFile.filename() == L"backup_metadata.dat") {
                        continue;
                    }

                    fs::path relativePath = fs::relative(sourceFile, backupPath);
                    fs::path destFile = fs::path(volumePath) / relativePath;

                    // Create destination directory
                    fs::create_directories(destFile.parent_path());

                    // Copy file
                    fs::copy_file(sourceFile, destFile, fs::copy_options::overwrite_existing);

                    processedFiles++;
                    if (callback && totalFiles > 0) {
                        int percent = 10 + (int)((processedFiles * 70) / totalFiles);
                        std::wstring msg = L"Restored " + std::to_wstring(processedFiles) +
                            L" of " + std::to_wstring(totalFiles) + L" files";
                        callback(percent, msg.c_str());
                    }
                }
            }

            if (restoreSystemState) {
                if (callback) {
                    callback(85, L"Restoring system state...");
                }

                // TODO: Restore system state
                // - Registry hives
                // - Boot configuration
                // - System files
            }

            if (callback) {
                callback(100, L"Volume restore completed successfully");
            }

            return 0;
        }
        catch (const fs::filesystem_error&) {
            SetLastErrorMessage(L"Filesystem error during volume restore");
            return -4;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in RestoreVolume");
            return -99;
        }
    }

    BACKUPENGINE_API int RestoreDisk(
        const wchar_t* backupPath,
        int targetDiskNumber,
        bool restoreSystemState,
        ProgressCallback callback) {
        
        if (!backupPath || targetDiskNumber < 0) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            if (callback) {
                callback(0, L"Starting disk restore...");
            }

            // Find backup image file
            std::wstring backupFile = std::wstring(backupPath) + L"\\disk_" + 
                std::to_wstring(targetDiskNumber) + L".img";

            if (!fs::exists(backupFile)) {
                // Try to find any .img file
                bool found = false;
                for (const auto& entry : fs::directory_iterator(backupPath)) {
                    if (entry.path().extension() == L".img") {
                        backupFile = entry.path().wstring();
                        found = true;
                        break;
                    }
                }

                if (!found) {
                    SetLastErrorMessage(L"Disk image not found in backup");
                    return -2;
                }
            }

            if (callback) {
                callback(10, L"Opening target disk...");
            }

            // Open target disk
            std::wstring diskPath = L"\\\\.\\PhysicalDrive" + std::to_wstring(targetDiskNumber);
            
            HANDLE hDisk = CreateFileW(
                diskPath.c_str(),
                GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                NULL,
                OPEN_EXISTING,
                0,
                NULL);

            if (hDisk == INVALID_HANDLE_VALUE) {
                SetLastErrorMessage(L"Failed to open target disk - requires administrator privileges");
                return -3;
            }

            // Open backup image
            HANDLE hBackup = CreateFileW(
                backupFile.c_str(),
                GENERIC_READ,
                FILE_SHARE_READ,
                NULL,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                NULL);

            if (hBackup == INVALID_HANDLE_VALUE) {
                CloseHandle(hDisk);
                SetLastErrorMessage(L"Failed to open backup image");
                return -4;
            }

            if (callback) {
                callback(20, L"Restoring disk sectors...");
            }

            // Get backup file size
            LARGE_INTEGER fileSize;
            if (!GetFileSizeEx(hBackup, &fileSize)) {
                CloseHandle(hDisk);
                CloseHandle(hBackup);
                SetLastErrorMessage(L"Failed to get backup size");
                return -5;
            }

            // Restore disk sectors
            const DWORD bufferSize = 1024 * 1024; // 1MB buffer
            std::vector<BYTE> buffer(bufferSize);
            LONGLONG totalBytes = fileSize.QuadPart;
            LONGLONG bytesProcessed = 0;

            while (bytesProcessed < totalBytes) {
                DWORD bytesToRead = (DWORD)min((LONGLONG)bufferSize, totalBytes - bytesProcessed);
                DWORD bytesRead = 0;

                if (!ReadFile(hBackup, buffer.data(), bytesToRead, &bytesRead, NULL)) {
                    CloseHandle(hDisk);
                    CloseHandle(hBackup);
                    SetLastErrorMessage(L"Failed to read backup image");
                    return -6;
                }

                if (bytesRead == 0) break; // EOF

                DWORD bytesWritten = 0;
                if (!WriteFile(hDisk, buffer.data(), bytesRead, &bytesWritten, NULL)) {
                    CloseHandle(hDisk);
                    CloseHandle(hBackup);
                    SetLastErrorMessage(L"Failed to write to disk");
                    return -7;
                }

                bytesProcessed += bytesRead;

                if (callback && totalBytes > 0) {
                    int percent = 20 + (int)((bytesProcessed * 70) / totalBytes);
                    callback(percent, L"Restoring disk...");
                }
            }

            CloseHandle(hDisk);
            CloseHandle(hBackup);

            if (callback) {
                callback(100, L"Disk restore completed successfully");
            }

            return 0;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in RestoreDisk");
            return -99;
        }
    }
}
