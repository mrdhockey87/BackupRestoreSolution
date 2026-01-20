// BackupInfo_Implementation.cpp - Get backup information and list contents
#include "BackupEngine.h"
#include <Windows.h>
#include <string>
#include <filesystem>
#include <sstream>
#include <fstream>
#include <vector>

namespace fs = std::filesystem;
extern void SetLastErrorMessage(const std::wstring& error);

extern "C" {

    BACKUPENGINE_API int GetBackupInfo(
        const wchar_t* backupPath,
        wchar_t* buffer,
        int bufferSize) {

        if (!backupPath || !buffer || bufferSize <= 0) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            if (!fs::exists(backupPath)) {
                SetLastErrorMessage(L"Backup path does not exist");
                return -2;
            }

            std::wostringstream info;

            // Read backup_info.txt if it exists
            std::wstring infoFile = std::wstring(backupPath) + L"\\backup_info.txt";
            if (fs::exists(infoFile)) {
                std::wifstream file(infoFile);
                if (file.is_open()) {
                    std::wstring line;
                    while (std::getline(file, line)) {
                        info << line << L"\n";
                    }
                    file.close();
                }
            }
            else {
                // Generate basic info
                info << L"Backup Information\n";
                info << L"==================\n\n";
                info << L"Location: " << backupPath << L"\n";

                // Count files and calculate size
                size_t fileCount = 0;
                uintmax_t totalSize = 0;

                for (const auto& entry : fs::recursive_directory_iterator(backupPath)) {
                    if (entry.is_regular_file()) {
                        // Skip metadata files
                        if (entry.path().filename() != L"backup_metadata.dat" &&
                            entry.path().filename() != L"backup_info.txt") {
                            fileCount++;
                            try {
                                totalSize += entry.file_size();
                            }
                            catch (...) {}
                        }
                    }
                }

                info << L"Files: " << fileCount << L"\n";
                info << L"Size: " << (totalSize / (1024 * 1024)) << L" MB\n";

                // Get backup date from directory creation time
                auto ftime = fs::last_write_time(backupPath);
                info << L"Type: " << (fs::exists(std::wstring(backupPath) + L"\\backup_metadata.dat") 
                    ? L"File Backup" : L"Unknown") << L"\n";
            }

            std::wstring result = info.str();
            if (result.length() >= (size_t)bufferSize) {
                SetLastErrorMessage(L"Buffer too small");
                return -3;
            }

            wcscpy_s(buffer, bufferSize, result.c_str());
            return 0;
        }
        catch (const fs::filesystem_error&) {
            SetLastErrorMessage(L"Filesystem error reading backup info");
            return -4;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in GetBackupInfo");
            return -99;
        }
    }

    BACKUPENGINE_API int ListBackupContents(
        const wchar_t* backupPath,
        wchar_t* buffer,
        int bufferSize) {

        if (!backupPath || !buffer || bufferSize <= 0) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            if (!fs::exists(backupPath)) {
                SetLastErrorMessage(L"Backup path does not exist");
                return -2;
            }

            std::wostringstream contents;
            std::vector<std::wstring> files;

            // Enumerate all files in backup
            for (const auto& entry : fs::recursive_directory_iterator(backupPath)) {
                if (entry.is_regular_file()) {
                    // Skip metadata files
                    std::wstring filename = entry.path().filename().wstring();
                    if (filename == L"backup_metadata.dat" || filename == L"backup_info.txt") {
                        continue;
                    }

                    // Get relative path from backup root
                    fs::path relativePath = fs::relative(entry.path(), backupPath);
                    
                    // Get file size
                    uintmax_t size = 0;
                    try {
                        size = entry.file_size();
                    }
                    catch (...) {}

                    // Format: relativepath (size KB)
                    std::wstring fileInfo = relativePath.wstring();
                    
                    if (size < 1024) {
                        fileInfo += L" (" + std::to_wstring(size) + L" B)";
                    }
                    else if (size < 1024 * 1024) {
                        fileInfo += L" (" + std::to_wstring(size / 1024) + L" KB)";
                    }
                    else {
                        fileInfo += L" (" + std::to_wstring(size / (1024 * 1024)) + L" MB)";
                    }

                    files.push_back(fileInfo);
                }
            }

            // Sort files alphabetically
            std::sort(files.begin(), files.end());

            // Build output
            for (const auto& file : files) {
                contents << file << L"\n";
            }

            std::wstring result = contents.str();

            if (result.empty()) {
                result = L"(No files in backup)\n";
            }

            if (result.length() >= (size_t)bufferSize) {
                SetLastErrorMessage(L"Buffer too small - too many files in backup");
                
                // Return partial list with indicator
                result = result.substr(0, bufferSize - 50);
                result += L"\n... (list truncated, buffer too small)\n";
                
                wcscpy_s(buffer, bufferSize, result.c_str());
                return -3;
            }

            wcscpy_s(buffer, bufferSize, result.c_str());
            return 0;
        }
        catch (const fs::filesystem_error&) {
            SetLastErrorMessage(L"Filesystem error listing backup contents");
            return -4;
        }
        catch (...) {
            SetLastErrorMessage(L"Exception in ListBackupContents");
            return -99;
        }
    }
}
