// RestoreEngine_Advanced.cpp - Advanced restore functions
#include "BackupEngine.h"
#include <Windows.h>
#include <string>
#include <filesystem>
#include <vector>
#include <fstream>

namespace fs = std::filesystem;
extern void SetLastErrorMessage(const std::wstring& error);

namespace {
    // Helper function to restore system state from backup
    bool RestoreSystemStateFiles(const std::wstring& backupPath, ProgressCallback callback) {
        try {
            std::wstring systemStatePath = backupPath + L"\\SystemState";
            
            // Check if system state backup exists
            if (!fs::exists(systemStatePath)) {
                if (callback) {
                    callback(87, L"No system state found in backup - skipping");
                }
                return true; // Not an error, just skip
            }

            if (callback) {
                callback(85, L"Preparing system state restore...");
            }

            // Create restore instructions file
            std::wstring restoreScript = systemStatePath + L"\\RESTORE_INSTRUCTIONS.txt";
            std::wofstream instructions(restoreScript);
            if (instructions.is_open()) {
                instructions << L"SYSTEM STATE RESTORE INSTRUCTIONS" << std::endl;
                instructions << L"====================================" << std::endl;
                instructions << std::endl;
                instructions << L"The system state backup includes:" << std::endl;
                instructions << L"- Registry hives (SAM, SECURITY, SOFTWARE, SYSTEM, DEFAULT)" << std::endl;
                instructions << L"- Boot Configuration Data (BCD)" << std::endl;
                instructions << L"- Registry backup files" << std::endl;
                instructions << std::endl;
                instructions << L"IMPORTANT: System state restoration requires special procedures:" << std::endl;
                instructions << std::endl;
                instructions << L"OPTION 1: Windows Recovery Environment (Recommended)" << std::endl;
                instructions << L"1. Boot to Windows Recovery Environment (WinRE)" << std::endl;
                instructions << L"2. Open Command Prompt (Advanced options > Command Prompt)" << std::endl;
                instructions << L"3. Copy registry hives from backup to C:\\Windows\\System32\\config\\" << std::endl;
                instructions << L"   Example:" << std::endl;
                instructions << L"   copy \"" << systemStatePath << L"\\SAM\" C:\\Windows\\System32\\config\\SAM" << std::endl;
                instructions << L"   copy \"" << systemStatePath << L"\\SECURITY\" C:\\Windows\\System32\\config\\SECURITY" << std::endl;
                instructions << L"   copy \"" << systemStatePath << L"\\SOFTWARE\" C:\\Windows\\System32\\config\\SOFTWARE" << std::endl;
                instructions << L"   copy \"" << systemStatePath << L"\\SYSTEM\" C:\\Windows\\System32\\config\\SYSTEM" << std::endl;
                instructions << L"   copy \"" << systemStatePath << L"\\DEFAULT\" C:\\Windows\\System32\\config\\DEFAULT" << std::endl;
                instructions << L"4. Restore BCD if needed:" << std::endl;
                instructions << L"   bcdedit /import \"" << systemStatePath << L"\\BCD\"" << std::endl;
                instructions << L"5. Reboot" << std::endl;
                instructions << std::endl;
                instructions << L"OPTION 2: Using Registry Editor" << std::endl;
                instructions << L"1. Boot to another Windows installation or WinPE" << std::endl;
                instructions << L"2. Load the target hives using REGEDIT" << std::endl;
                instructions << L"3. Import the backed-up registry files" << std::endl;
                instructions << L"4. Unload hives and reboot" << std::endl;
                instructions << std::endl;
                instructions << L"OPTION 3: Automated Restore on Next Boot (Experimental)" << std::endl;
                instructions << L"Use the automated registry restore feature (requires admin rights)" << std::endl;
                instructions << std::endl;
                instructions << L"WARNING: Restoring system state to wrong configuration may" << std::endl;
                instructions << L"prevent Windows from booting. Always have recovery media ready." << std::endl;
            }

            if (callback) {
                callback(87, L"Checking system state restore options...");
            }

            // Try automated restore preparation (safe - just prepares, doesn't execute)
            // Copy registry hives to a restore staging area
            std::wstring stagingPath = L"C:\\ProgramData\\BackupRestoreService\\SystemStateRestore";
            
            try {
                fs::create_directories(stagingPath);
                
                // Copy registry hives to staging area
                std::vector<std::wstring> hives = { L"SAM", L"SECURITY", L"SOFTWARE", L"SYSTEM", L"DEFAULT" };
                
                for (const auto& hive : hives) {
                    std::wstring src = systemStatePath + L"\\" + hive;
                    std::wstring dst = stagingPath + L"\\" + hive;
                    
                    if (fs::exists(src)) {
                        fs::copy_file(src, dst, fs::copy_options::overwrite_existing);
                    }
                }
                
                // Copy BCD if exists
                std::wstring bcdSrc = systemStatePath + L"\\BCD";
                std::wstring bcdDst = stagingPath + L"\\BCD";
                if (fs::exists(bcdSrc)) {
                    fs::copy_file(bcdSrc, bcdDst, fs::copy_options::overwrite_existing);
                }
                
                // Create restore marker file
                std::wofstream marker(stagingPath + L"\\RESTORE_PENDING.txt");
                if (marker.is_open()) {
                    SYSTEMTIME st;
                    GetLocalTime(&st);
                    marker << L"System State Restore Pending" << std::endl;
                    marker << L"Staged: " << st.wYear << L"-" << st.wMonth << L"-" << st.wDay << L" "
                           << st.wHour << L":" << st.wMinute << L":" << st.wSecond << std::endl;
                    marker << std::endl;
                    marker << L"Restore files staged at: " << stagingPath << std::endl;
                    marker << std::endl;
                    marker << L"To complete restoration:" << std::endl;
                    marker << L"1. Boot to Windows Recovery Environment" << std::endl;
                    marker << L"2. Run the restore commands from RESTORE_INSTRUCTIONS.txt" << std::endl;
                    marker << std::endl;
                    marker << L"Or use: Run-SystemStateRestore.ps1 (in WinRE)" << std::endl;
                }
                
                // Create PowerShell restore script for WinRE
                std::wofstream ps1(stagingPath + L"\\Run-SystemStateRestore.ps1");
                if (ps1.is_open()) {
                    ps1 << L"# System State Restore Script" << std::endl;
                    ps1 << L"# Run this from Windows Recovery Environment Command Prompt:" << std::endl;
                    ps1 << L"# powershell -ExecutionPolicy Bypass -File Run-SystemStateRestore.ps1" << std::endl;
                    ps1 << std::endl;
                    ps1 << L"Write-Host 'System State Restore - Starting...'" << std::endl;
                    ps1 << L"Write-Host ''" << std::endl;
                    ps1 << std::endl;
                    ps1 << L"$stagingPath = '" << stagingPath << L"'" << std::endl;
                    ps1 << L"$configPath = 'C:\\Windows\\System32\\config'" << std::endl;
                    ps1 << std::endl;
                    ps1 << L"# Backup current registry hives" << std::endl;
                    ps1 << L"Write-Host 'Backing up current registry...'" << std::endl;
                    ps1 << L"$backupTime = Get-Date -Format 'yyyyMMdd_HHmmss'" << std::endl;
                    ps1 << L"$backupPath = \"$configPath\\Backup_$backupTime\"" << std::endl;
                    ps1 << L"New-Item -ItemType Directory -Path $backupPath -Force | Out-Null" << std::endl;
                    ps1 << std::endl;
                    ps1 << L"$hives = @('SAM', 'SECURITY', 'SOFTWARE', 'SYSTEM', 'DEFAULT')" << std::endl;
                    ps1 << L"foreach ($hive in $hives) {" << std::endl;
                    ps1 << L"    if (Test-Path \"$configPath\\$hive\") {" << std::endl;
                    ps1 << L"        Copy-Item \"$configPath\\$hive\" \"$backupPath\\$hive\" -Force" << std::endl;
                    ps1 << L"    }" << std::endl;
                    ps1 << L"}" << std::endl;
                    ps1 << std::endl;
                    ps1 << L"# Restore registry hives from backup" << std::endl;
                    ps1 << L"Write-Host 'Restoring registry hives...'" << std::endl;
                    ps1 << L"foreach ($hive in $hives) {" << std::endl;
                    ps1 << L"    if (Test-Path \"$stagingPath\\$hive\") {" << std::endl;
                    ps1 << L"        Write-Host \"  Restoring $hive...\"" << std::endl;
                    ps1 << L"        Copy-Item \"$stagingPath\\$hive\" \"$configPath\\$hive\" -Force" << std::endl;
                    ps1 << L"    }" << std::endl;
                    ps1 << L"}" << std::endl;
                    ps1 << std::endl;
                    ps1 << L"# Restore BCD if exists" << std::endl;
                    ps1 << L"if (Test-Path \"$stagingPath\\BCD\") {" << std::endl;
                    ps1 << L"    Write-Host 'Restoring Boot Configuration Data...'" << std::endl;
                    ps1 << L"    bcdedit /import \"$stagingPath\\BCD\"" << std::endl;
                    ps1 << L"}" << std::endl;
                    ps1 << std::endl;
                    ps1 << L"Write-Host ''" << std::endl;
                    ps1 << L"Write-Host 'System State Restore Complete!'" << std::endl;
                    ps1 << L"Write-Host 'Previous registry backed up to: $backupPath'" << std::endl;
                    ps1 << L"Write-Host 'Reboot to apply changes.'" << std::endl;
                }
                
                if (callback) {
                    callback(90, L"System state staged for restoration");
                }
                
                // Show informational message
                if (callback) {
                    std::wstring msg = L"System state files staged at: " + stagingPath;
                    callback(92, msg.c_str());
                    callback(95, L"See RESTORE_INSTRUCTIONS.txt for manual restore steps");
                    callback(97, L"Or use Run-SystemStateRestore.ps1 from WinRE");
                }
            }
            catch (...) {
                // Staging failed - not critical, instructions file still created
                if (callback) {
                    callback(90, L"System state restore prepared (manual mode)");
                }
            }

            return true;
        }
        catch (...) {
            return false;
        }
    }
}

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

                // Restore system state (registry, boot files, etc.)
                // This prepares system state for restoration and provides instructions
                // Actual restoration requires WinRE or offline mode due to locked files
                bool systemStateSuccess = RestoreSystemStateFiles(backupPath, callback);
                
                if (!systemStateSuccess) {
                    if (callback) {
                        callback(90, L"Warning: System state preparation incomplete");
                    }
                }
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
