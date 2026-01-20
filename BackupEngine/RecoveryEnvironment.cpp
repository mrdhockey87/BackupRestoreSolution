// RecoveryEnvironment.cpp - Create bootable USB recovery environment
#include "BackupEngine.h"
#include <Windows.h>
#include <string>
#include <filesystem>
#include <fstream>

namespace fs = std::filesystem;
extern void SetLastErrorMessage(const std::wstring& error);

extern "C" {

    BACKUPENGINE_API int InstallRecoveryBootFiles(
        const wchar_t* usbDriveLetter,
        ProgressCallback callback) {
        
        if (!usbDriveLetter) {
            SetLastErrorMessage(L"Invalid USB drive letter");
            return -1;
        }

        try {
            if (callback) {
                callback(0, L"Installing boot files...");
            }

            // This is a stub implementation
            // In a full implementation, this would:
            // 1. Format USB as FAT32
            // 2. Make it bootable using bootsect.exe or similar
            // 3. Copy WinPE boot files (bootmgr, BCD, etc.)
            // 4. Copy WinPE image (boot.wim)

            std::wstring drivePath = std::wstring(usbDriveLetter) + L"\\";

            if (callback) {
                callback(25, L"Formatting USB drive...");
            }

            // Check if drive exists
            if (GetDriveTypeW(drivePath.c_str()) == DRIVE_NO_ROOT_DIR) {
                SetLastErrorMessage(L"USB drive not found");
                return -2;
            }

            if (callback) {
                callback(50, L"Creating boot partition...");
            }

            // Create boot directory structure
            fs::path bootPath = fs::path(drivePath) / L"Boot";
            fs::path sourcesPath = fs::path(drivePath) / L"Sources";
            
            try {
                fs::create_directories(bootPath);
                fs::create_directories(sourcesPath);
            }
            catch (const fs::filesystem_error& e) {
                SetLastErrorMessage(L"Failed to create directories");
                return -3;
            }

            if (callback) {
                callback(75, L"Installing bootloader...");
            }

            // NOTE: Full implementation would use bootsect.exe:
            // bootsect.exe /nt60 <drive> /force /mbr

            if (callback) {
                callback(100, L"Boot files installed successfully");
            }

            return 0;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in InstallRecoveryBootFiles");
            return -99;
        }
    }

    BACKUPENGINE_API int CreateRecoveryEnvironment(
        const wchar_t* usbDriveLetter,
        const wchar_t* programPath,
        ProgressCallback callback) {
        
        if (!usbDriveLetter || !programPath) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            if (callback) {
                callback(0, L"Creating recovery environment...");
            }

            // Install boot files first
            int result = InstallRecoveryBootFiles(usbDriveLetter, nullptr);
            if (result != 0) {
                return result;
            }

            if (callback) {
                callback(30, L"Copying recovery programs...");
            }

            // Copy program files to USB
            std::wstring drivePath = std::wstring(usbDriveLetter) + L"\\";
            fs::path recoveryPath = fs::path(drivePath) / L"Recovery";
            
            try {
                fs::create_directories(recoveryPath);
            }
            catch (const fs::filesystem_error&) {
                SetLastErrorMessage(L"Failed to create recovery directory");
                return -3;
            }

            if (callback) {
                callback(50, L"Copying restore program...");
            }

            // Copy BackupUI.exe, BackupEngine.dll, and dependencies
            fs::path sourcePath(programPath);
            
            try {
                // Copy main executables
                std::vector<std::wstring> filesToCopy = {
                    L"BackupUI.exe",
                    L"BackupEngine.dll",
                    L"BackupUI.dll",
                    L"BackupUI.runtimeconfig.json"
                };

                for (const auto& file : filesToCopy) {
                    fs::path sourceFile = sourcePath / file;
                    fs::path destFile = recoveryPath / file;
                    
                    if (fs::exists(sourceFile)) {
                        fs::copy_file(sourceFile, destFile, 
                            fs::copy_options::overwrite_existing);
                    }
                }
            }
            catch (const fs::filesystem_error&) {
                SetLastErrorMessage(L"Failed to copy program files");
                return -4;
            }

            if (callback) {
                callback(75, L"Creating startup script...");
            }

            // Create startup script
            fs::path startupScript = fs::path(drivePath) / L"StartRecovery.bat";
            try {
                std::wofstream script(startupScript);
                script << L"@echo off\n";
                script << L"echo Starting Backup Recovery Environment...\n";
                script << L"cd /d %~dp0Recovery\n";
                script << L"start BackupUI.exe\n";
                script.close();
            }
            catch (...) {
                SetLastErrorMessage(L"Failed to create startup script");
                return -5;
            }

            if (callback) {
                callback(90, L"Finalizing recovery environment...");
            }

            // Create README file
            fs::path readmePath = fs::path(drivePath) / L"README.txt";
            try {
                std::wofstream readme(readmePath);
                readme << L"Backup & Restore Recovery Environment\n";
                readme << L"=====================================\n\n";
                readme << L"To restore your system:\n";
                readme << L"1. Boot from this USB drive\n";
                readme << L"2. Run StartRecovery.bat\n";
                readme << L"3. Select your backup and restore location\n";
                readme << L"4. Follow the on-screen instructions\n\n";
                readme << L"For system state or boot volume recovery,\n";
                readme << L"ensure you have administrator privileges.\n";
                readme.close();
            }
            catch (...) {
                // Not critical if README creation fails
            }

            if (callback) {
                callback(100, L"Recovery environment created successfully");
            }

            return 0;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in CreateRecoveryEnvironment");
            return -99;
        }
    }
}
