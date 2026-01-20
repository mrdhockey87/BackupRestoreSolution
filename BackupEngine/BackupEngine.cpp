// RestoreEngine.cpp
#include "BackupEngine.h"
#include <Windows.h>
#include <filesystem>
#include <queue>
#include <string>

namespace fs = std::filesystem;

class FileRestorer {
private:
    ProgressCallback progressCallback;
    std::wstring lastError;

    struct FileEntry {
        std::wstring source;
        std::wstring dest;
        DWORD attributes;
    };

    bool CopyFileWithProgress(const std::wstring& source,
        const std::wstring& dest,
        bool overwrite) {
        DWORD flags = overwrite ? 0 : COPY_FILE_FAIL_IF_EXISTS;

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

                    FileRestorer* restorer = (FileRestorer*)context;
                    if (restorer && restorer->progressCallback && totalSize.QuadPart > 0) {
                        int percent = (int)((totalTransferred.QuadPart * 100) / totalSize.QuadPart);
                        // Update handled per-file; aggregate in main function
                    }
                    return PROGRESS_CONTINUE;
            },
            this,
            NULL,
            flags);

        return result != 0;
    }

public:
    FileRestorer(ProgressCallback callback) : progressCallback(callback) {}

    int RestoreDirectory(const std::wstring& source,
        const std::wstring& dest,
        bool overwrite) {
        try {
            if (!fs::exists(source)) {
                lastError = L"Source path does not exist: " + source;
                return -1;
            }

            // Create destination directory
            if (!fs::exists(dest)) {
                fs::create_directories(dest);
            }

            // Collect all files first to calculate total
            std::queue<FileEntry> fileQueue;
            size_t totalFiles = 0;
            size_t totalSize = 0;

            if (progressCallback) {
                progressCallback(0, L"Scanning backup files...");
            }

            for (const auto& entry : fs::recursive_directory_iterator(source)) {
                if (entry.is_regular_file()) {
                    FileEntry fe;
                    fe.source = entry.path().wstring();

                    // Calculate relative path
                    fs::path relativePath = fs::relative(entry.path(), source);
                    fe.dest = (fs::path(dest) / relativePath).wstring();
                    fe.attributes = GetFileAttributesW(fe.source.c_str());

                    fileQueue.push(fe);
                    totalFiles++;
                    totalSize += entry.file_size();
                }
            }

            if (progressCallback) {
                std::wstring msg = L"Restoring " + std::to_wstring(totalFiles) + L" files...";
                progressCallback(0, msg.c_str());
            }

            // Restore files
            size_t processedFiles = 0;
            size_t processedSize = 0;

            while (!fileQueue.empty()) {
                FileEntry fe = fileQueue.front();
                fileQueue.pop();

                // Create destination directory if needed
                fs::path destDir = fs::path(fe.dest).parent_path();
                if (!fs::exists(destDir)) {
                    fs::create_directories(destDir);
                }

                // Copy file
                if (!CopyFileWithProgress(fe.source, fe.dest, overwrite)) {
                    DWORD error = ::GetLastError();
                    if (error == ERROR_FILE_EXISTS && !overwrite) {
                        // Skip existing files if not overwriting
                    }
                    else {
                        lastError = L"Failed to restore file: " + fe.dest;
                        return -2;
                    }
                }

                // Restore file attributes
                SetFileAttributesW(fe.dest.c_str(), fe.attributes);

                processedFiles++;

                if (progressCallback && totalFiles > 0) {
                    int percent = (int)((processedFiles * 100) / totalFiles);
                    std::wstring msg = L"Restored " + std::to_wstring(processedFiles) +
                        L" of " + std::to_wstring(totalFiles) + L" files";
                    progressCallback(percent, msg.c_str());
                }
            }

            if (progressCallback) {
                progressCallback(100, L"Restore completed successfully");
            }

            return 0;
        }
        catch (const fs::filesystem_error& e) {
            lastError = L"Filesystem error: " + std::wstring(e.what(), e.what() + strlen(e.what()));
            return -3;
        }
        catch (...) {
            lastError = L"Unknown error during restore";
            return -99;
        }
    }

    const std::wstring& GetLastError() const { return lastError; }
};

extern "C" {
    BACKUPENGINE_API int RestoreFiles(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        bool overwriteExisting,
        ProgressCallback callback) {

        try {
            FileRestorer restorer(callback);
            return restorer.RestoreDirectory(sourcePath, destPath, overwriteExisting);
        }
        catch (...) {
            return -99;
        }
    }
}