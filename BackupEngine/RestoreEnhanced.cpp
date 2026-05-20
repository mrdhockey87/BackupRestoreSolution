// RestoreEnhanced.cpp - Enhanced restore functionality for Version 4.7.0.0
// Implements backup date enumeration and selective restore with manifests

#include <string>
#include <vector>
#include <filesystem>
#include <sstream>
#include <windows.h>
#include <algorithm>
#include "BackupEngine.h"
#include "WimMountManager.h"

namespace fs = std::filesystem;

extern std::wstring g_lastError;

namespace {
    struct MountedArchiveScope {
        std::wstring mountPath;

        ~MountedArchiveScope() {
            if (!mountPath.empty()) {
                wchar_t errorBuffer[512] = {};
                BackupEngine::WimMountManager::UnmountWim(mountPath.c_str(), errorBuffer, static_cast<int>(_countof(errorBuffer)));
            }
        }
    };

    bool IsArchiveFilePath(const std::wstring& path) {
        std::wstring extension = fs::path(path).extension().wstring();
        return _wcsicmp(extension.c_str(), L".ssb") == 0 ||
               _wcsicmp(extension.c_str(), L".wim") == 0;
    }

    bool TryResolveBackupContentRoot(const std::wstring& backupPath, std::wstring& contentRoot, MountedArchiveScope& mountedArchive) {
        std::error_code ec;
        if (fs::exists(backupPath, ec) && fs::is_directory(backupPath, ec)) {
            contentRoot = backupPath;
            return true;
        }

        if (!IsArchiveFilePath(backupPath)) {
            g_lastError = L"Backup path does not exist";
            return false;
        }

        if (!BackupEngine::WimMountManager::Initialize()) {
            g_lastError = L"Failed to initialize archive mount manager";
            return false;
        }

        wchar_t mountPathBuffer[MAX_PATH] = {};
        wchar_t errorBuffer[512] = {};
        std::wstring backupName = fs::path(backupPath).stem().wstring();
        if (backupName.empty()) {
            backupName = L"Backup";
        }

        if (!BackupEngine::WimMountManager::MountWim(
            backupPath.c_str(),
            backupName.c_str(),
            L"Restore",
            1,
            mountPathBuffer,
            static_cast<int>(_countof(mountPathBuffer)),
            errorBuffer,
            static_cast<int>(_countof(errorBuffer)))) {
            g_lastError = errorBuffer[0] == L'\0' ? L"Failed to mount backup archive" : errorBuffer;
            return false;
        }

        mountedArchive.mountPath = mountPathBuffer;
        contentRoot = mountedArchive.mountPath;
        return true;
    }

    std::wstring NormalizeManifestItem(std::wstring item) {
        item.erase(0, item.find_first_not_of(L" \t\r\n"));
        item.erase(item.find_last_not_of(L" \t\r\n") + 1);
        std::replace(item.begin(), item.end(), L'/', L'\\');
        return item;
    }

    std::wstring CombineRestorePath(const std::wstring& basePath, const std::wstring& relativePath) {
        fs::path combined = fs::path(basePath) / fs::path(relativePath);
        return combined.lexically_normal().wstring();
    }
}

// Helper function to get file size formatted as string
std::wstring FormatFileSize(uintmax_t bytes) {
    const wchar_t* units[] = { L"B", L"KB", L"MB", L"GB", L"TB" };
    int unitIndex = 0;
    double size = static_cast<double>(bytes);

    while (size >= 1024.0 && unitIndex < 4) {
        size /= 1024.0;
        unitIndex++;
    }

    wchar_t buffer[64];
    swprintf_s(buffer, L"%.2f %s", size, units[unitIndex]);
    return buffer;
}

// Helper function to get backup type from folder name
std::wstring GetBackupType(const std::wstring& folderName) {
    if (folderName.find(L"Full") != std::wstring::npos) {
        return L"Full";
    }
    else if (folderName.find(L"Incremental") != std::wstring::npos) {
        return L"Incremental";
    }
    else if (folderName.find(L"Differential") != std::wstring::npos) {
        return L"Differential";
    }
    return L"Full"; // Default
}

bool IsHyperVBackupPointDirectory(const fs::path& folderPath) {
    std::error_code ec;
    return fs::is_directory(folderPath, ec) && fs::exists(folderPath / L"hyperv_backup_info.txt", ec);
}

// Helper function to parse date from folder name
// Format: Full_20260130_143000 or Incremental_20260130_143000
bool ParseDateFromFolderName(const std::wstring& folderName, SYSTEMTIME& st) {
    // Find date pattern YYYYMMDD_HHMMSS
    size_t pos = folderName.find_last_of(L'_');
    if (pos == std::wstring::npos || pos < 9) return false;

    std::wstring dateStr = folderName.substr(pos - 8, 8); // YYYYMMDD
    std::wstring timeStr = folderName.substr(pos + 1, 6); // HHMMSS

    if (dateStr.length() != 8 || timeStr.length() != 6) return false;

    try {
        st.wYear = static_cast<WORD>(std::stoi(dateStr.substr(0, 4)));
        st.wMonth = static_cast<WORD>(std::stoi(dateStr.substr(4, 2)));
        st.wDay = static_cast<WORD>(std::stoi(dateStr.substr(6, 2)));
        st.wHour = static_cast<WORD>(std::stoi(timeStr.substr(0, 2)));
        st.wMinute = static_cast<WORD>(std::stoi(timeStr.substr(2, 2)));
        st.wSecond = static_cast<WORD>(std::stoi(timeStr.substr(4, 2)));
        st.wMilliseconds = 0;
        st.wDayOfWeek = 0;
        return true;
    }
    catch (...) {
        return false;
    }
}

// Enumerate backup dates/snapshots in a backup folder
BACKUPENGINE_API int EnumerateBackupDates(
    const wchar_t* backupPath,
    wchar_t* buffer,
    int bufferSize)
{
    if (!backupPath || !buffer || bufferSize <= 0) {
        g_lastError = L"Invalid parameters";
        return -1;
    }

    try {
        std::wostringstream result;
        fs::path backupDir(backupPath);

        if (!fs::exists(backupDir) || !fs::is_directory(backupDir)) {
            g_lastError = L"Backup path does not exist or is not a directory";
            return -1;
        }

        // Scan for backup folders
        std::vector<fs::directory_entry> backupFolders;
        for (const auto& entry : fs::directory_iterator(backupDir)) {
            if (entry.is_directory()) {
                std::wstring folderName = entry.path().filename().wstring();
                // Check if it looks like a backup folder
                if (folderName.find(L"Full") != std::wstring::npos ||
                    folderName.find(L"Incremental") != std::wstring::npos ||
                    folderName.find(L"Differential") != std::wstring::npos ||
                    IsHyperVBackupPointDirectory(entry.path())) {
                    backupFolders.push_back(entry);
                }
            }
        }

        // Sort by modification time (newest first)
        std::sort(backupFolders.begin(), backupFolders.end(),
            [](const fs::directory_entry& a, const fs::directory_entry& b) {
                return fs::last_write_time(a) > fs::last_write_time(b);
            });

        // Build output string
        for (const auto& entry : backupFolders) {
            std::wstring folderName = entry.path().filename().wstring();
            std::wstring fullPath = entry.path().wstring();

            // Get backup type
            std::wstring backupType = GetBackupType(folderName);

            // Get folder size
            uintmax_t totalSize = 0;
            try {
                for (const auto& file : fs::recursive_directory_iterator(entry.path())) {
                    if (fs::is_regular_file(file)) {
                        totalSize += fs::file_size(file);
                    }
                }
            }
            catch (...) {}

            std::wstring sizeStr = FormatFileSize(totalSize);

            // Parse date from folder name
            SYSTEMTIME st = {};
            if (!ParseDateFromFolderName(folderName, st)) {
                // Fall back to file modification time
                auto ftime = fs::last_write_time(entry);
                auto sctp = std::chrono::time_point_cast<std::chrono::system_clock::duration>(
                    ftime - fs::file_time_type::clock::now() + std::chrono::system_clock::now());
                time_t cftime = std::chrono::system_clock::to_time_t(sctp);
                struct tm timeinfo;
                localtime_s(&timeinfo, &cftime);
                st.wYear = static_cast<WORD>(timeinfo.tm_year + 1900);
                st.wMonth = static_cast<WORD>(timeinfo.tm_mon + 1);
                st.wDay = static_cast<WORD>(timeinfo.tm_mday);
                st.wHour = static_cast<WORD>(timeinfo.tm_hour);
                st.wMinute = static_cast<WORD>(timeinfo.tm_min);
                st.wSecond = static_cast<WORD>(timeinfo.tm_sec);
            }

            // Format: Date|Type|Size|Path
            wchar_t dateStr[128];
            swprintf_s(dateStr, L"%04d-%02d-%02d %02d:%02d:%02d",
                st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);

            result << dateStr << L"|" << backupType << L"|" << sizeStr << L"|" << fullPath << L"\n";
        }

        // Copy result to buffer
        std::wstring resultStr = result.str();
        if (resultStr.length() >= static_cast<size_t>(bufferSize)) {
            g_lastError = L"Buffer too small";
            return -1;
        }

        wcscpy_s(buffer, bufferSize, resultStr.c_str());
        return 0;
    }
    catch (const std::exception& ex) {
        g_lastError = L"Failed to enumerate backup dates: " +
            std::wstring(ex.what(), ex.what() + strlen(ex.what()));
        return -1;
    }
    catch (...) {
        g_lastError = L"Unknown error enumerating backup dates";
        return -1;
    }
}

// Restore selected items from backup using a manifest
BACKUPENGINE_API int RestoreWithManifest(
    const wchar_t* backupPath,
    const wchar_t* destPath,
    const wchar_t* manifest,
    bool overwriteExisting,
    bool restoreSystemState,
    bool preservePermissions,
    ProgressCallback callback)
{
    if (!backupPath || !manifest) {
        g_lastError = L"Invalid parameters";
        return -1;
    }

    try {
        MountedArchiveScope mountedArchive;
        std::wstring contentRoot;
        if (!TryResolveBackupContentRoot(backupPath, contentRoot, mountedArchive)) {
            return -1;
        }

        // Parse manifest (one path per line)
        std::wistringstream manifestStream(manifest);
        std::vector<std::wstring> itemsToRestore;
        std::wstring line;

        while (std::getline(manifestStream, line)) {
            std::wstring normalizedItem = NormalizeManifestItem(line);
            if (!normalizedItem.empty()) {
                itemsToRestore.push_back(normalizedItem);
            }
        }

        if (itemsToRestore.empty()) {
            g_lastError = L"No items specified in manifest";
            return -1;
        }

        // Determine destination
        std::wstring destination = destPath ? destPath : L"";
        bool restoreToOriginal = destination.empty();

        int totalItems = static_cast<int>(itemsToRestore.size());
        int currentItem = 0;

        // Restore each item
        for (const auto& item : itemsToRestore) {
            // Calculate progress percentage
            int percentage = (currentItem * 100) / totalItems;
            
            if (callback) {
                std::wstring msg = L"Restoring: " + item;
                callback(percentage, msg.c_str());
            }

            // Determine item type and call appropriate restore function
            std::wstring sourcePath = CombineRestorePath(contentRoot, item);
            std::wstring targetPath = restoreToOriginal ? item : (destination + L"\\" + item);

            // Check what type of item this is
            if (fs::exists(sourcePath)) {
                if (fs::is_directory(sourcePath)) {
                    // Check if it's a volume backup (contains SystemState, disk images, etc.)
                    std::wstring systemStatePath = sourcePath + L"\\SystemState";
                    std::wstring diskImagePath = sourcePath + L"\\disk_";
                    
                    bool hasSystemState = fs::exists(systemStatePath);
                    bool hasDiskImage = false;
                    
                    // Check for disk images
                    try {
                        for (const auto& entry : fs::directory_iterator(sourcePath)) {
                            if (entry.path().extension() == L".img") {
                                hasDiskImage = true;
                                break;
                            }
                        }
                    }
                    catch (...) {}

                    if (hasDiskImage) {
                        // This is a disk backup - extract disk number from target
                        int diskNumber = 0;
                        
                        // Try to extract disk number from target path
                        size_t diskPos = targetPath.find(L"PhysicalDrive");
                        if (diskPos != std::wstring::npos) {
                            try {
                                diskNumber = std::stoi(targetPath.substr(diskPos + 13));
                            }
                            catch (...) {
                                diskNumber = 0; // Default to disk 0
                            }
                        }
                        
                        // Restore entire disk
                        int result = RestoreDisk(
                            sourcePath.c_str(),
                            diskNumber,
                            restoreSystemState,
                            callback
                        );
                        
                        if (result != 0) {
                            // Log error but continue
                            if (callback) {
                                callback(percentage, L"Warning: Failed to restore disk");
                            }
                        }
                    }
                    else if (hasSystemState || targetPath.length() <= 3) {
                        // This is a volume backup (has SystemState or target is a drive letter like C:\)
                        // Restore entire volume
                        int result = RestoreVolume(
                            sourcePath.c_str(),
                            targetPath.c_str(),
                            restoreSystemState,
                            callback
                        );
                        
                        if (result != 0) {
                            // Log error but continue
                            if (callback) {
                                callback(percentage, L"Warning: Failed to restore volume");
                            }
                        }
                    }
                    else {
                        // Regular directory - restore files
                        int result = RestoreFiles(
                            sourcePath.c_str(),
                            targetPath.c_str(),
                            overwriteExisting,
                            callback
                        );
                        
                        if (result != 0) {
                            // Log error but continue
                            if (callback) {
                                callback(percentage, L"Warning: Failed to restore directory");
                            }
                        }
                    }
                }
                else {
                    // Single file - restore it
                    // Ensure target directory exists
                    fs::path targetFilePath(targetPath);
                    fs::path targetDir = targetFilePath.parent_path();
                    
                    try {
                        fs::create_directories(targetDir);
                    }
                    catch (...) {}
                    
                    // Copy single file
                    try {
                        auto copyOptions = overwriteExisting ?
                            fs::copy_options::overwrite_existing :
                            fs::copy_options::skip_existing;
                        
                        fs::copy_file(sourcePath, targetPath, copyOptions);
                    }
                    catch (const std::exception& ex) {
                        if (callback) {
                            std::wstring errMsg = L"Warning: Failed to restore file: " +
                                std::wstring(ex.what(), ex.what() + strlen(ex.what()));
                            callback(percentage, errMsg.c_str());
                        }
                    }
                }
            }
            else {
                // Source doesn't exist
                if (callback) {
                    std::wstring msg = L"Warning: Source not found: " + item;
                    callback(percentage, msg.c_str());
                }
            }

            currentItem++;
        }

        if (callback) {
            callback(100, L"Restore completed");
        }

        return 0;
    }
    catch (const std::exception& ex) {
        g_lastError = L"Failed to restore with manifest: " +
            std::wstring(ex.what(), ex.what() + strlen(ex.what()));
        return -1;
    }
    catch (...) {
        g_lastError = L"Unknown error restoring with manifest";
        return -1;
    }
}
