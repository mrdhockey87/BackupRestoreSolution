// BackupVerification.cpp
// 
// NOTE: ListBackupContents has been moved to BackupInfo_Implementation.cpp
// This file now only contains VerifyBackup implementation
//
#include "BackupEngine.h"
#include <Windows.h>
#include <filesystem>
#include <sstream>

namespace fs = std::filesystem;

// ListBackupContents is now in BackupInfo_Implementation.cpp
// Commented out to avoid duplicate symbol

/*
extern "C" {
    BACKUPENGINE_API int ListBackupContents(
        const wchar_t* backupPath,
        wchar_t* buffer,
        int bufferSize) {

        try {
            std::wstringstream ss;

            if (!fs::exists(backupPath)) {
                wcscpy_s(buffer, bufferSize, L"Backup path does not exist");
                return -1;
            }

            for (const auto& entry : fs::recursive_directory_iterator(backupPath)) {
                if (entry.is_regular_file()) {
                    ss << entry.path().filename().wstring() << L"\n";
                }
            }

            std::wstring result = ss.str();
            if (result.length() >= (size_t)bufferSize) {
                wcscpy_s(buffer, bufferSize, L"Buffer too small");
                return -2;
            }

            wcscpy_s(buffer, bufferSize, result.c_str());
            return 0;
        }
        catch (...) {
            wcscpy_s(buffer, bufferSize, L"Error listing backup contents");
            return -99;
        }
    }
}
*/

// VerifyBackup implementation
extern "C" {
    BACKUPENGINE_API int VerifyBackup(
        const wchar_t* backupPath,
        ProgressCallback callback) {

        try {
            if (callback) {
                callback(0, L"Starting backup verification...");
            }

            if (!fs::exists(backupPath)) {
                if (callback) {
                    callback(0, L"Backup path does not exist");
                }
                return -1;
            }

            size_t totalFiles = 0;
            size_t verifiedFiles = 0;

            // Count files
            for (const auto& entry : fs::recursive_directory_iterator(backupPath)) {
                if (entry.is_regular_file()) {
                    totalFiles++;
                }
            }

            if (callback) {
                std::wstring msg = L"Verifying " + std::to_wstring(totalFiles) + L" files...";
                callback(10, msg.c_str());
            }

            // Verify each file can be read
            for (const auto& entry : fs::recursive_directory_iterator(backupPath)) {
                if (entry.is_regular_file()) {
                    HANDLE hFile = CreateFileW(
                        entry.path().wstring().c_str(),
                        GENERIC_READ,
                        FILE_SHARE_READ,
                        NULL,
                        OPEN_EXISTING,
                        FILE_ATTRIBUTE_NORMAL,
                        NULL);

                    if (hFile == INVALID_HANDLE_VALUE) {
                        if (callback) {
                            std::wstring msg = L"Failed to verify: " +
                                entry.path().filename().wstring();
                            callback(0, msg.c_str());
                        }
                        return -2;
                    }

                    CloseHandle(hFile);
                    verifiedFiles++;

                    if (callback && totalFiles > 0) {
                        int percent = 10 + (int)((verifiedFiles * 90) / totalFiles);
                        std::wstring msg = L"Verified " + std::to_wstring(verifiedFiles) +
                            L" of " + std::to_wstring(totalFiles) + L" files";
                        callback(percent, msg.c_str());
                    }
                }
            }

            if (callback) {
                callback(100, L"Backup verification completed successfully");
            }

            return 0;
        }
        catch (...) {
            if (callback) {
                callback(0, L"Error during backup verification");
            }
            return -99;
        }
    }
}
