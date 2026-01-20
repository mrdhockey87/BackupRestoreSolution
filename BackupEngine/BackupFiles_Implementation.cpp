// BackupFiles_Implementation.cpp - Core file backup with progress tracking
#include "BackupEngine.h"
#include <Windows.h>
#include <string>
#include <filesystem>
#include <vector>
#include <queue>
#include <fstream>

namespace fs = std::filesystem;
extern void SetLastErrorMessage(const std::wstring& error);

namespace {
    struct FileBackupEntry {
        std::wstring sourcePath;
        std::wstring destPath;
        uintmax_t size;
        FILETIME modifiedTime;
        DWORD attributes;
    };

    bool CopyFileWithProgress(
        const std::wstring& source,
        const std::wstring& dest,
        ProgressCallback callback,
        void* context) {

        BOOL result = CopyFileExW(
            source.c_str(),
            dest.c_str(),
            [](LARGE_INTEGER totalSize,
                LARGE_INTEGER totalTransferred,
                LARGE_INTEGER streamSize,
                LARGE_INTEGER streamTransferred,
                DWORD streamNumber,
                DWORD callbackReason,
                HANDLE sourceFile,
                HANDLE destFile,
                LPVOID context) -> DWORD {

                // Individual file progress can be reported here if needed
                return PROGRESS_CONTINUE;
            },
            context,
            NULL,
            0);

        return result != 0;
    }

    void SaveBackupMetadata(
        const std::wstring& backupPath,
        const std::vector<FileBackupEntry>& files) {

        std::wstring metadataPath = backupPath + L"\\backup_metadata.dat";

        try {
            std::wofstream metadata(metadataPath, std::ios::binary);
            if (!metadata.is_open()) return;

            // Write header
            metadata << L"BACKUP_METADATA_V1\n";
            metadata << L"FileCount:" << files.size() << L"\n";
            metadata << L"---\n";

            // Write file entries
            for (const auto& file : files) {
                metadata << file.sourcePath << L"|"
                    << file.size << L"|"
                    << file.modifiedTime.dwLowDateTime << L"|"
                    << file.modifiedTime.dwHighDateTime << L"|"
                    << file.attributes << L"\n";
            }

            metadata.close();
        }
        catch (...) {
            // Non-critical error, continue
        }
    }
}

extern "C" {

    BACKUPENGINE_API int BackupFiles(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        ProgressCallback callback) {

        if (!sourcePath || !destPath) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            if (callback) {
                callback(0, L"Starting file backup...");
            }

            // Verify source exists
            if (!fs::exists(sourcePath)) {
                SetLastErrorMessage(L"Source path does not exist");
                return -2;
            }

            // Create destination directory
            fs::create_directories(destPath);

            if (callback) {
                callback(5, L"Scanning files...");
            }

            // Collect all files to backup
            std::vector<FileBackupEntry> filesToBackup;
            uintmax_t totalSize = 0;

            if (fs::is_directory(sourcePath)) {
                // Backup entire directory recursively
                for (const auto& entry : fs::recursive_directory_iterator(
                    sourcePath,
                    fs::directory_options::skip_permission_denied)) {

                    if (entry.is_regular_file()) {
                        try {
                            FileBackupEntry fileEntry;
                            fileEntry.sourcePath = entry.path().wstring();
                            fileEntry.size = entry.file_size();
                            fileEntry.attributes = GetFileAttributesW(fileEntry.sourcePath.c_str());

                            // Get modification time
                            HANDLE hFile = CreateFileW(
                                fileEntry.sourcePath.c_str(),
                                GENERIC_READ,
                                FILE_SHARE_READ,
                                NULL,
                                OPEN_EXISTING,
                                FILE_ATTRIBUTE_NORMAL,
                                NULL);

                            if (hFile != INVALID_HANDLE_VALUE) {
                                GetFileTime(hFile, nullptr, nullptr, &fileEntry.modifiedTime);
                                CloseHandle(hFile);
                            }

                            // Calculate relative path for destination
                            fs::path relativePath = fs::relative(entry.path(), sourcePath);
                            fileEntry.destPath = (fs::path(destPath) / relativePath).wstring();

                            filesToBackup.push_back(fileEntry);
                            totalSize += fileEntry.size;
                        }
                        catch (const fs::filesystem_error&) {
                            // Skip files we can't access
                            continue;
                        }
                    }
                }
            }
            else if (fs::is_regular_file(sourcePath)) {
                // Backup single file
                FileBackupEntry fileEntry;
                fileEntry.sourcePath = sourcePath;
                fileEntry.size = fs::file_size(sourcePath);
                fileEntry.attributes = GetFileAttributesW(sourcePath);

                fs::path sourceFilePath(sourcePath);
                fileEntry.destPath = (fs::path(destPath) / sourceFilePath.filename()).wstring();

                filesToBackup.push_back(fileEntry);
                totalSize = fileEntry.size;
            }
            else {
                SetLastErrorMessage(L"Source is not a valid file or directory");
                return -3;
            }

            if (filesToBackup.empty()) {
                SetLastErrorMessage(L"No files to backup");
                return -4;
            }

            if (callback) {
                std::wstring msg = L"Backing up " + std::to_wstring(filesToBackup.size()) + 
                    L" files (" + std::to_wstring(totalSize / (1024 * 1024)) + L" MB)...";
                callback(10, msg.c_str());
            }

            // Backup files
            size_t processedFiles = 0;
            uintmax_t processedBytes = 0;

            for (const auto& fileEntry : filesToBackup) {
                // Create destination directory
                fs::path destDir = fs::path(fileEntry.destPath).parent_path();
                if (!fs::exists(destDir)) {
                    fs::create_directories(destDir);
                }

                // Copy file
                if (!CopyFileWithProgress(
                    fileEntry.sourcePath,
                    fileEntry.destPath,
                    callback,
                    nullptr)) {

                    DWORD error = ::GetLastError();
                    if (error == ERROR_ACCESS_DENIED) {
                        // Skip files we can't access
                        continue;
                    }
                    else {
                        std::wstring errorMsg = L"Failed to copy file: " + fileEntry.sourcePath +
                            L" (Error: " + std::to_wstring(error) + L")";
                        SetLastErrorMessage(errorMsg);
                        // Continue with other files instead of failing completely
                        continue;
                    }
                }

                // Preserve file attributes and timestamps
                SetFileAttributesW(fileEntry.destPath.c_str(), fileEntry.attributes);

                HANDLE hDest = CreateFileW(
                    fileEntry.destPath.c_str(),
                    FILE_WRITE_ATTRIBUTES,
                    0,
                    NULL,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    NULL);

                if (hDest != INVALID_HANDLE_VALUE) {
                    SetFileTime(hDest, nullptr, nullptr, &fileEntry.modifiedTime);
                    CloseHandle(hDest);
                }

                processedFiles++;
                processedBytes += fileEntry.size;

                if (callback && !filesToBackup.empty()) {
                    int percent = 10 + (int)((processedBytes * 85) / totalSize);
                    std::wstring msg = L"Backed up " + std::to_wstring(processedFiles) +
                        L" of " + std::to_wstring(filesToBackup.size()) + L" files";
                    callback(percent, msg.c_str());
                }
            }

            // Save backup metadata
            if (callback) {
                callback(95, L"Saving backup metadata...");
            }

            SaveBackupMetadata(destPath, filesToBackup);

            // Create backup info file
            std::wstring infoPath = std::wstring(destPath) + L"\\backup_info.txt";
            try {
                std::wofstream info(infoPath);
                info << L"Backup Information\n";
                info << L"==================\n\n";
                info << L"Source: " << sourcePath << L"\n";
                info << L"Destination: " << destPath << L"\n";
                info << L"Date: " << __DATE__ << L" " << __TIME__ << L"\n";
                info << L"Total Files: " << filesToBackup.size() << L"\n";
                info << L"Total Size: " << (totalSize / (1024 * 1024)) << L" MB\n";
                info.close();
            }
            catch (...) {
                // Non-critical
            }

            if (callback) {
                callback(100, L"Backup completed successfully");
            }

            return 0;
        }
        catch (const fs::filesystem_error& e) {
            std::wstring error = L"Filesystem error: ";
            error += std::wstring(e.what(), e.what() + strlen(e.what()));
            SetLastErrorMessage(error);
            return -5;
        }
        catch (const std::exception& e) {
            std::wstring error = L"Exception: ";
            error += std::wstring(e.what(), e.what() + strlen(e.what()));
            SetLastErrorMessage(error);
            return -6;
        }
        catch (...) {
            SetLastErrorMessage(L"Unknown exception in BackupFiles");
            return -99;
        }
    }
}
