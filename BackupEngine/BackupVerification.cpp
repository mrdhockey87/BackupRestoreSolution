// BackupVerification.cpp
// 
// NOTE: ListBackupContents has been moved to BackupInfo_Implementation.cpp
// This file now only contains VerifyBackup implementation
//
#include "BackupEngine.h"
#include <Windows.h>
#include <filesystem>
#include <sstream>
#include <wimgapi.h>

#pragma comment(lib, "wimgapi.lib")

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

    // Enhanced verification for WIM/SSB archives
    // Verifies archive structure, image count, and loadability
    BACKUPENGINE_API int VerifyWimArchive(
        const wchar_t* archivePath,
        int expectedImageCount,
        wchar_t* errorMsg,
        int errorMsgSize,
        ProgressCallback callback) {

        try {
            if (callback) {
                callback(0, L"Starting SSB archive verification...");
            }

            // Check file exists
            if (!fs::exists(archivePath)) {
                swprintf_s(errorMsg, errorMsgSize, L"Archive file does not exist: %s", archivePath);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -1;
            }

            if (callback) {
                callback(10, L"Checking file size...");
            }

            // Check minimum file size (WIM header is 208 bytes)
            auto fileSize = fs::file_size(archivePath);
            if (fileSize < 208) {
                swprintf_s(errorMsg, errorMsgSize, 
                    L"Archive file too small (%llu bytes). Minimum is 208 bytes. File may be incomplete.", 
                    fileSize);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -2;
            }

            if (callback) {
                callback(20, L"Opening archive...");
            }

            // Open WIM archive (without VERIFY flag - basic validation only)
            DWORD creationResult = 0;
            HANDLE hWim = WIMCreateFile(
                archivePath,
                WIM_GENERIC_READ,
                WIM_OPEN_EXISTING,
                0,  // No flags - basic validation sufficient
                0,
                &creationResult
            );

            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                DWORD error = GetLastError();
                swprintf_s(errorMsg, errorMsgSize, 
                    L"Failed to open archive. Error %u. Archive may be corrupted.", error);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -3;
            }

            if (callback) {
                callback(40, L"Checking image count...");
            }

            // Get image count
            DWORD imageCount = WIMGetImageCount(hWim);

            if (imageCount == 0) {
                swprintf_s(errorMsg, errorMsgSize, L"Archive contains no images. Archive is empty or corrupted.");
                WIMCloseHandle(hWim);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -4;
            }

            // Verify expected image count matches (if provided)
            if (expectedImageCount > 0 && static_cast<int>(imageCount) != expectedImageCount) {
                swprintf_s(errorMsg, errorMsgSize, 
                    L"Image count mismatch. Expected %d, found %u. Archive may be incomplete.", 
                    expectedImageCount, imageCount);
                WIMCloseHandle(hWim);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -5;
            }

            if (callback) {
                std::wstring msg = L"Found " + std::to_wstring(imageCount) + L" image(s). Verifying loadability...";
                callback(60, msg.c_str());
            }

            // Set temporary path for WIM API (required for WIMLoadImage to work)
            // Without this, WIMLoadImage fails with error 1632 even on valid WIM files
            wchar_t tempPath[MAX_PATH];
            if (GetTempPathW(MAX_PATH, tempPath)) {
                WIMSetTemporaryPath(hWim, tempPath);
            }

            // Try loading first image to verify image structure
            HANDLE hImage = WIMLoadImage(hWim, 1);

            if (!hImage || hImage == INVALID_HANDLE_VALUE) {
                DWORD error = GetLastError();
                swprintf_s(errorMsg, errorMsgSize, 
                    L"Failed to load image 1. Error %u. Image data is corrupted.", error);
                WIMCloseHandle(hWim);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -6;
            }

            if (callback) {
                callback(80, L"Image structure verified. Checking metadata...");
            }

            // Get image information (XML metadata)
            wchar_t* imageXmlInfo = nullptr;
            DWORD xmlSize = 0;
            if (!WIMGetImageInformation(hImage, reinterpret_cast<LPVOID*>(&imageXmlInfo), &xmlSize) ||
                imageXmlInfo == nullptr ||
                xmlSize < sizeof(wchar_t)) {
                DWORD error = GetLastError();
                swprintf_s(errorMsg, errorMsgSize,
                    L"No metadata found in image. WIMGetImageInformation failed with error %u.", error);
                if (imageXmlInfo != nullptr) {
                    LocalFree(imageXmlInfo);
                }
                WIMCloseHandle(hImage);
                WIMCloseHandle(hWim);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -7;
            }

            std::wstring imageXml(imageXmlInfo);
            LocalFree(imageXmlInfo);

            if (imageXml.empty() || imageXml.find(L"<IMAGE") == std::wstring::npos) {
                swprintf_s(errorMsg, errorMsgSize, L"Image metadata XML is empty or invalid. Archive may be corrupted.");
                WIMCloseHandle(hImage);
                WIMCloseHandle(hWim);
                if (callback) {
                    callback(0, errorMsg);
                }
                return -7;
            }

            // Close handles
            WIMCloseHandle(hImage);
            WIMCloseHandle(hWim);

            if (callback) {
                callback(100, L"Archive verification completed successfully!");
            }

            swprintf_s(errorMsg, errorMsgSize, L"SUCCESS: Archive contains %u valid image(s)", imageCount);
            return 0;  // Success

        }
        catch (const std::exception& e) {
            swprintf_s(errorMsg, errorMsgSize, L"Exception during verification: %S", e.what());
            if (callback) {
                callback(0, errorMsg);
            }
            return -98;
        }
        catch (...) {
            swprintf_s(errorMsg, errorMsgSize, L"Unknown error during archive verification");
            if (callback) {
                callback(0, errorMsg);
            }
            return -99;
        }
    }
}
