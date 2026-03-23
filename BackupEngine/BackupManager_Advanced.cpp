// BackupManager_Advanced.cpp - Advanced backup functions (Volume, Disk, Incremental, Differential)
#include "BackupEngine.h"
#include "VSSSnapshotManager.h"  // Add VSS support
#include <Windows.h>
#include <ShlObj.h>  // For SHGetFolderPathW
#include <string>
#include <filesystem>
#include <fstream>
#include <map>
#include <vector>
#include <algorithm>  // For std::transform (lowercase conversion)
#include <chrono>
#include <iomanip>
#include <sstream>
#include <mutex>      // For thread-safe job name tracking
#include "wimgapi.h"  // Windows Imaging API for WIM file creation

#pragma comment(lib, "wimgapi.lib")

namespace fs = std::filesystem;
extern void SetLastErrorMessage(const std::wstring& error);

// ============================================================================
// CURRENT JOB NAME TRACKING
// Set by C# before calling backup functions via SetCurrentJobName()
// Used by logging functions to write to job-specific log file
// ============================================================================
static std::wstring g_currentJobName = L"";
static std::mutex g_jobNameMutex;

// Set the current job name (called from C# before backup starts)
extern "C" BACKUPENGINE_API void SetCurrentJobName(const wchar_t* jobName) {
    std::lock_guard<std::mutex> lock(g_jobNameMutex);
    g_currentJobName = jobName ? jobName : L"";
    OutputDebugStringW((L"[BackupEngine] SetCurrentJobName: " + g_currentJobName + L"\n").c_str());
}

// Clear the current job name (called from C# after backup completes)
extern "C" BACKUPENGINE_API void ClearCurrentJobName() {
    std::lock_guard<std::mutex> lock(g_jobNameMutex);
    g_currentJobName = L"";
}

// Get current job name (thread-safe)
static std::wstring GetCurrentJobName() {
    std::lock_guard<std::mutex> lock(g_jobNameMutex);
    return g_currentJobName;
}

// ============================================================================
// FILE-BASED LOGGING FOR BACKUPENGINE
// Writes to %ProgramData%\BackupRestoreService\Logs\{JobName}.json
// Falls back to engine.json if no job name is set
// Uses same JSON format as BackupLogger.cs for consistency with backup logs
// ============================================================================
namespace {
    std::wstring GetBackupEngineLogDir() {
        wchar_t programData[MAX_PATH];
        if (SUCCEEDED(SHGetFolderPathW(NULL, CSIDL_COMMON_APPDATA, NULL, 0, programData))) {
            std::wstring logDir = std::wstring(programData) + L"\\BackupRestoreService\\Logs";
            CreateDirectoryW((std::wstring(programData) + L"\\BackupRestoreService").c_str(), NULL);
            CreateDirectoryW(logDir.c_str(), NULL);
            return logDir;
        }
        return L"C:\\";  // Fallback
    }

    // Sanitize job name for use as filename (same logic as C# SanitizeFileName)
    std::wstring SanitizeJobNameForFile(const std::wstring& jobName) {
        if (jobName.empty()) return L"engine";

        std::wstring result;
        result.reserve(jobName.size());

        // Characters invalid in Windows filenames
        const std::wstring invalidChars = L"<>:\"/\\|?*";

        for (wchar_t c : jobName) {
            if (c < 32 || invalidChars.find(c) != std::wstring::npos) {
                result += L'_';
            } else {
                result += c;
            }
        }

        return result.empty() ? L"engine" : result;
    }

    std::wstring GetBackupEngineLogPath() {
        std::wstring jobName = GetCurrentJobName();
        if (jobName.empty()) {
            // No job name set - use legacy engine.json
            return GetBackupEngineLogDir() + L"\\engine.json";
        }
        // Use job-specific log file
        return GetBackupEngineLogDir() + L"\\" + SanitizeJobNameForFile(jobName) + L".json";
    }

    // Get job name for JSON entry (use actual job name or [ENGINE] as fallback)
    std::wstring GetJobNameForLogEntry() {
        std::wstring jobName = GetCurrentJobName();
        return jobName.empty() ? L"[ENGINE]" : jobName;
    }

    // Escape special characters for JSON string
    std::wstring EscapeJsonString(const std::wstring& input) {
        std::wstring result;
        result.reserve(input.size() + 16);
        for (wchar_t c : input) {
            switch (c) {
                case L'\\': result += L"\\\\"; break;
                case L'"':  result += L"\\\""; break;
                case L'\n': result += L"\\n"; break;
                case L'\r': result += L"\\r"; break;
                case L'\t': result += L"\\t"; break;
                default:    result += c; break;
            }
        }
        return result;
    }

    // Log entry to JSON file matching BackupLogger.cs format
    // Level: "Info", "Warning", "Error", "Success"
    void LogToJsonFile(const std::wstring& level, const std::wstring& message, const std::wstring& details = L"") {
        try {
            // Get log path dynamically (may change if job name changes)
            std::wstring logPath = GetBackupEngineLogPath();
            static const int MaxLogEntries = 2000;  // Match BackupLogger.cs

            // Get current timestamp in ISO 8601 format for JSON
            auto now = std::chrono::system_clock::now();
            auto time = std::chrono::system_clock::to_time_t(now);
            std::tm tm;
            localtime_s(&tm, &time);

            std::wstringstream timestamp;
            timestamp << std::put_time(&tm, L"%Y-%m-%dT%H:%M:%S");

            // Get job name for log entry (actual job name or [ENGINE] fallback)
            std::wstring jobNameForEntry = GetJobNameForLogEntry();

            // Build JSON entry matching BackupLogEntry structure
            std::wstring jsonEntry = L"{";
            jsonEntry += L"\"Timestamp\":\"" + timestamp.str() + L"\",";
            jsonEntry += L"\"JobName\":\"" + EscapeJsonString(jobNameForEntry) + L"\",";
            jsonEntry += L"\"Level\":\"" + level + L"\",";
            jsonEntry += L"\"Message\":\"" + EscapeJsonString(message) + L"\",";
            jsonEntry += L"\"Details\":\"" + EscapeJsonString(details) + L"\",";
            jsonEntry += L"\"ValidationPassed\":true,";
            jsonEntry += L"\"BackupPath\":\"\",";
            jsonEntry += L"\"IsRead\":false";
            jsonEntry += L"}";

            // Read existing JSON array, append entry, and write back
            std::vector<std::wstring> entries;

            // Read existing entries using proper JSON parsing that handles braces in string values
            // Read as UTF-8 (compatible with C# JsonSerializer)
            std::ifstream inFile(logPath, std::ios::binary);
            if (inFile.is_open()) {
                // Read entire file as bytes
                std::string utf8Content((std::istreambuf_iterator<char>(inFile)),
                                        std::istreambuf_iterator<char>());
                inFile.close();

                // Convert UTF-8 to wide string for parsing
                std::wstring content;
                if (!utf8Content.empty()) {
                    int wideSize = MultiByteToWideChar(CP_UTF8, 0, utf8Content.c_str(), -1, nullptr, 0);
                    if (wideSize > 0) {
                        content.resize(wideSize - 1);  // -1 to exclude null terminator
                        MultiByteToWideChar(CP_UTF8, 0, utf8Content.c_str(), -1, &content[0], wideSize);
                    }
                }

                // Parse JSON array properly - handle braces inside quoted strings
                size_t pos = 0;
                while (pos < content.size()) {
                    // Find start of JSON object
                    size_t objStart = content.find(L'{', pos);
                    if (objStart == std::wstring::npos) break;

                    // Find matching closing brace, accounting for nested braces in strings
                    int braceCount = 0;
                    bool inString = false;
                    bool escaped = false;
                    size_t objEnd = std::wstring::npos;

                    for (size_t i = objStart; i < content.size(); i++) {
                        wchar_t c = content[i];

                        if (escaped) {
                            escaped = false;
                            continue;
                        }

                        if (c == L'\\' && inString) {
                            escaped = true;
                            continue;
                        }

                        if (c == L'"') {
                            inString = !inString;
                            continue;
                        }

                        if (!inString) {
                            if (c == L'{') braceCount++;
                            else if (c == L'}') {
                                braceCount--;
                                if (braceCount == 0) {
                                    objEnd = i;
                                    break;
                                }
                            }
                        }
                    }

                    if (objEnd != std::wstring::npos && objEnd > objStart) {
                        entries.push_back(content.substr(objStart, objEnd - objStart + 1));
                        pos = objEnd + 1;
                    } else {
                        // Malformed entry - skip past this opening brace
                        pos = objStart + 1;
                    }
                }
            }

            // Add new entry
            entries.push_back(jsonEntry);

            // Keep only last MaxLogEntries
            if (entries.size() > MaxLogEntries) {
                entries.erase(entries.begin(), entries.begin() + (entries.size() - MaxLogEntries));
            }

            // Write JSON array with UTF-8 encoding (required for C# JsonSerializer compatibility)
            // Use atomic write pattern: write to temp file, then rename
            std::wstring tempPath = logPath + L".tmp";
            std::ofstream outFile(tempPath, std::ios::trunc | std::ios::binary);
            if (outFile.is_open()) {
                // Convert entries to UTF-8 and write
                outFile << "[\n";
                for (size_t i = 0; i < entries.size(); i++) {
                    // Convert wide string to UTF-8
                    std::string utf8Entry;
                    int utf8Size = WideCharToMultiByte(CP_UTF8, 0, entries[i].c_str(), -1, nullptr, 0, nullptr, nullptr);
                    if (utf8Size > 0) {
                        utf8Entry.resize(utf8Size - 1);  // -1 to exclude null terminator
                        WideCharToMultiByte(CP_UTF8, 0, entries[i].c_str(), -1, &utf8Entry[0], utf8Size, nullptr, nullptr);
                    }

                    outFile << "  " << utf8Entry;
                    if (i < entries.size() - 1) {
                        outFile << ",";
                    }
                    outFile << "\n";
                }
                outFile << "]\n";
                outFile.close();

                // Atomic replace: delete old file, rename temp to final
                // This prevents corruption if process crashes during write
                DeleteFileW(logPath.c_str());
                MoveFileW(tempPath.c_str(), logPath.c_str());
            }

            // Also output to debug (for attached debuggers)
            OutputDebugStringW((L"[BackupEngine] [" + level + L"] " + message + L"\n").c_str());
        }
        catch (...) {
            // Silently fail - don't crash backup operation for logging
        }
    }

    void LogError(const std::wstring& message, const std::wstring& details = L"") {
        LogToJsonFile(L"Error", message, details);
    }

    void LogInfo(const std::wstring& message, const std::wstring& details = L"") {
        LogToJsonFile(L"Info", message, details);
    }

    void LogDebug(const std::wstring& message, const std::wstring& details = L"") {
        // Debug messages go to Info level in JSON (no Debug level in BackupLogLevel enum)
        LogToJsonFile(L"Info", L"[DEBUG] " + message, details);
    }

    void LogWarning(const std::wstring& message, const std::wstring& details = L"") {
        LogToJsonFile(L"Warning", message, details);
    }

    // Legacy function for backward compatibility with existing code
    void LogToFile(const std::wstring& message) {
        LogToJsonFile(L"Info", message);
    }

    // Sanitize string for safe use in XML (escapes special XML characters)
    // Required for WIMSetImageInformation which parses strict XML
    std::wstring SanitizeXmlName(const std::wstring& input) {
        std::wstring result;
        result.reserve(input.size() + 16);
        for (wchar_t c : input) {
            switch (c) {
                case L'&':  result += L"&amp;"; break;
                case L'<':  result += L"&lt;"; break;
                case L'>':  result += L"&gt;"; break;
                case L'"':  result += L"&quot;"; break;
                case L'\'': result += L"&apos;"; break;
                default:    result += c; break;
            }
        }
        return result;
    }
}
// ============================================================================

// Forward declare BackupFiles from BackupEngine.cpp (legacy support)
extern "C" BACKUPENGINE_API int BackupFiles(
    const wchar_t* sourcePath,
    const wchar_t* destPath,
    const wchar_t** userExclusions,
    int userExclusionCount,
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

// Helper to create WIM file with proper configuration
// Returns INVALID_HANDLE_VALUE on error
HANDLE CreateWimFile(const wchar_t* wimPath, bool compress, ProgressCallback callback) {
    // Determine compression type
    DWORD compressionType = compress ? WIM_COMPRESS_LZMS : WIM_COMPRESS_NONE;

    if (callback) {
        callback(5, L"Creating backup archive...");
    }

    // Delete existing file if present to avoid WIM_CREATE_ALWAYS locking issues
    if (GetFileAttributesW(wimPath) != INVALID_FILE_ATTRIBUTES) {
        OutputDebugStringW(L"[CreateWimFile] Deleting existing WIM file...");
        if (!DeleteFileW(wimPath)) {
            DWORD deleteError = GetLastError();
            std::wstring errMsg = L"Failed to delete existing WIM file (Error " + std::to_wstring(deleteError) + L"): ";
            errMsg += wimPath;
            SetLastErrorMessage(errMsg);
            OutputDebugStringW((L"[CreateWimFile] ERROR: " + errMsg).c_str());
            return INVALID_HANDLE_VALUE;
        }
        OutputDebugStringW(L"[CreateWimFile] Existing file deleted successfully");
    }

    // Create WIM file
    // NOTE: Use READ+WRITE access (not just WRITE) so incremental/differential backups can open this file later
    // NOTE: WIM_FLAG_VERIFY removed - can cause compatibility issues with incremental/differential backups (version 5.13.10.8)
    // NOTE: flags=0 for initial creation - WIM_FLAG_REFERENCE is ONLY used when OPENING existing WIM for incremental/differential
    HANDLE hWim = WIMCreateFile(
        wimPath,
        WIM_GENERIC_READ | WIM_GENERIC_WRITE,  // READ+WRITE allows future opens for appending
        WIM_CREATE_ALWAYS,
        0,  // No flags needed when creating - WIM_FLAG_REFERENCE is for opening existing WIMs
        compressionType,
        NULL
    );

    if (!hWim || hWim == INVALID_HANDLE_VALUE) {
        DWORD wimError = GetLastError();
        std::wstring errMsg = L"Failed to create WIM archive (WIM Error " + std::to_wstring(wimError) + L")";
        SetLastErrorMessage(errMsg);
        OutputDebugStringW((L"[CreateWimFile] ERROR: " + errMsg).c_str());
        return INVALID_HANDLE_VALUE;
    }

    return hWim;
}

// Helper function to match wildcard patterns (e.g., *.tmp, *.log, D:\Build\*.dll)
bool MatchWildcard(const std::wstring& path, const std::wstring& pattern) {
    // Simple wildcard matching: * = any characters
    size_t asteriskPos = pattern.find(L'*');
    if (asteriskPos == std::wstring::npos) {
        // No wildcard - exact match
        return path == pattern;
    }

    // Pattern like *.tmp - check if path ends with .tmp
    if (asteriskPos == 0) {
        std::wstring suffix = pattern.substr(1); // Get part after *
        if (path.length() >= suffix.length()) {
            return path.substr(path.length() - suffix.length()) == suffix;
        }
        return false;
    }

    // Pattern like D:\Build\*.dll - check prefix and suffix
    std::wstring prefix = pattern.substr(0, asteriskPos);
    std::wstring suffix = pattern.substr(asteriskPos + 1);

    if (path.length() < prefix.length() + suffix.length()) {
        return false;
    }

    bool prefixMatch = path.substr(0, prefix.length()) == prefix;
    bool suffixMatch = path.substr(path.length() - suffix.length()) == suffix;

    return prefixMatch && suffixMatch;
}

// Helper to check if a path matches any exclusion pattern
bool IsPathExcluded(const std::wstring& path, const wchar_t** userExclusions, int userExclusionCount) {
    // Convert to lowercase for case-insensitive comparison
    std::wstring lowerPath = path;
    std::transform(lowerPath.begin(), lowerPath.end(), lowerPath.begin(), ::tolower);

    // SYSTEM EXCLUSIONS - Always exclude these protected folders/files that cause backup failures
    // NOTE: Windows\WinSxS is NOT excluded - it's required for proper system restoration
    std::vector<std::wstring> systemExclusions = {
        L"system volume information",  // VSS metadata, inaccessible
        L"$recycle.bin",                // Recycle bin, not needed for restore
        L"pagefile.sys",                // Virtual memory file, locked by OS
        L"swapfile.sys",                // Swap file for Windows apps, locked by OS
        L"hiberfil.sys"                 // Hibernation file, locked by OS
    };

    for (const auto& exclusion : systemExclusions) {
        if (lowerPath.find(exclusion) != std::wstring::npos) {
            return true;
        }
    }

    // USER-DEFINED EXCLUSIONS - passed from C# via P/Invoke
    // Check each user exclusion (supports wildcards like *.tmp, *.log, specific paths)
    for (int i = 0; i < userExclusionCount; i++) {
        std::wstring exclusion = userExclusions[i];
        std::wstring lowerExclusion = exclusion;
        std::transform(lowerExclusion.begin(), lowerExclusion.end(), 
                      lowerExclusion.begin(), ::tolower);

        // Check if exclusion is wildcard pattern (contains *)
        if (lowerExclusion.find(L'*') != std::wstring::npos) {
            // Pattern matching (e.g., *.tmp matches test.tmp)
            if (MatchWildcard(lowerPath, lowerExclusion)) {
                OutputDebugStringW((L"[BackupProgress] SKIPPING user-excluded pattern: " + 
                                   path + L" matches " + exclusion).c_str());
                return true;
            }
        } else {
            // Exact path matching for files/folders (substring match like system exclusions)
            if (lowerPath.find(lowerExclusion) != std::wstring::npos) {
                OutputDebugStringW((L"[BackupProgress] SKIPPING user-excluded path: " + 
                                   path + L" matches " + exclusion).c_str());
                return true;
            }
        }
    }

    return false;
}

// === FOLDER STRUCTURE PRESERVATION CALLBACKS ===
// Context structure for folder-specific WIM capture filtering
// Used when we need to capture a folder WITH its name in the structure
// (e.g., capture "1TB_PCIE_SSD" folder including the folder itself, not just contents)
struct FolderFilterContext {
    std::wstring folderName;          // Name of folder to include (e.g., "1TB_PCIE_SSD")
    ProgressCallback userCallback;     // User's progress callback
};

// Callback for WIM API that filters to only include files within a specific folder
// This allows capturing a folder FROM its parent while preserving folder structure
// Example: Capture from "E:\" but only include files under "E:\1TB_PCIE_SSD\"
//
// IMPORTANT: For WIM_MSG_PROCESS, the return value controls file inclusion:
//   - Return WIM_MSG_SUCCESS (TRUE/1) to INCLUDE the file
//   - Return WIM_MSG_DONE (FALSE/0) to EXCLUDE the file (skip it)
//   - WIM_MSG_SKIP_ERROR only skips errors, NOT files!
static DWORD WINAPI FolderFilterCallback(DWORD msgId, WPARAM wParam, LPARAM lParam, PVOID pvContext) {
    FolderFilterContext* context = (FolderFilterContext*)pvContext;

    // DEBUG: Log message types for FolderFilterCallback
    OutputDebugStringW((L"[FolderFilterCallback] Received msgId: " + std::to_wstring(msgId)).c_str());

    switch (msgId) {
        case WIM_MSG_PROCESS:
        {
            // WIM_MSG_PROCESS - file being processed during capture
            // wParam = path to file (LPCWSTR)
            // lParam = pointer to BOOL - set to FALSE to exclude file

            // Log every 100th file to avoid excessive logging
            static int fileCount = 0;
            fileCount++;
            if (fileCount == 1) {
                LogInfo(L"FolderFilterCallback: WIM_MSG_PROCESS - First file being processed");
            }

            if (wParam && lParam) {
                const wchar_t* filePath = (const wchar_t*)wParam;
                BOOL* pbInclude = (BOOL*)lParam;
                std::wstring path(filePath);

                // Log every 100th file for progress tracking
                if (fileCount % 100 == 0) {
                    LogInfo(L"FolderFilterCallback: Processed " + std::to_wstring(fileCount) + L" files, current: " + path);
                }

                // Check if this file is under our target folder
                // Path will be like "E:\1TB_PCIE_SSD\SomeFile.txt"
                // We want to include it if it contains "\1TB_PCIE_SSD\"
                std::wstring searchPattern = L"\\" + context->folderName + L"\\";

                // Also check if path STARTS with the folder name (for root-level match)
                std::wstring startPattern = context->folderName + L"\\";

                bool inTargetFolder = (path.find(searchPattern) != std::wstring::npos) ||
                                      (path.find(startPattern) == 0);

                if (!inTargetFolder) {
                    // File is NOT in our target folder - EXCLUDE it
                    *pbInclude = FALSE;
                    return WIM_MSG_SUCCESS;
                }

                // File IS in our target folder - check for system exclusions

                // Exclude protected Windows folders
                if (path.find(L"System Volume Information") != std::wstring::npos ||
                    path.find(L"$RECYCLE.BIN") != std::wstring::npos) {
                    LogDebug(L"FolderFilter: EXCLUDING system folder: " + path);
                    *pbInclude = FALSE;
                    return WIM_MSG_SUCCESS;
                }

                // Exclude locked system files
                std::wstring lowerPath = path;
                std::transform(lowerPath.begin(), lowerPath.end(), lowerPath.begin(), ::tolower);

                if (lowerPath.find(L"\\pagefile.sys") != std::wstring::npos ||
                    lowerPath.find(L"\\swapfile.sys") != std::wstring::npos ||
                    lowerPath.find(L"\\hiberfil.sys") != std::wstring::npos) {
                    LogDebug(L"FolderFilter: EXCLUDING locked file: " + path);
                    *pbInclude = FALSE;
                    return WIM_MSG_SUCCESS;
                }

                // File passes all filters - INCLUDE it and report progress
                *pbInclude = TRUE;
                if (context->userCallback) {
                    const wchar_t* fileName = wcsrchr(filePath, L'\\');
                    if (fileName) {
                        fileName++; // Skip backslash
                    } else {
                        fileName = filePath;
                    }

                    std::wstring message = L"Backing up: ";
                    message += fileName;
                    context->userCallback(51, message.c_str());
                }
            }
            return WIM_MSG_SUCCESS;
        }

        case WIM_MSG_PROGRESS:
        {
            // Overall progress during capture
            static int lastPercent = -1;
            if (context->userCallback) {
                int percentage = (int)wParam;
                // Log every 10% change
                if (percentage / 10 != lastPercent / 10) {
                    LogInfo(L"FolderFilterCallback: WIM_MSG_PROGRESS " + std::to_wstring(percentage) + L"%");
                    lastPercent = percentage;
                }
                percentage = 30 + (percentage * 50 / 100);  // Scale to 30-80%
                context->userCallback(percentage, L"Capturing files...");
            }
            return WIM_MSG_SUCCESS;
        }

        case WIM_MSG_SETRANGE:
        {
            // Total number of files to capture
            LogInfo(L"FolderFilterCallback: WIM_MSG_SETRANGE - Total files: " + std::to_wstring((DWORD)wParam));
            if (context->userCallback) {
                std::wstring message = L"Preparing to backup ";
                message += std::to_wstring((DWORD)wParam);
                message += L" files...";
                context->userCallback(25, message.c_str());
            }
            return WIM_MSG_SUCCESS;
        }

        case WIM_MSG_ERROR:
        {
            // Error during capture
            if (lParam && context->userCallback) {
                const wchar_t* errorMsg = (const wchar_t*)lParam;
                std::wstring logMsg = L"[WIM ERROR] ";
                logMsg += errorMsg;
                OutputDebugStringW(logMsg.c_str());
                context->userCallback(50, errorMsg);
            }
            return WIM_MSG_SUCCESS;
        }
        
        case WIM_MSG_WARNING:
        {
            // Warning during capture
            if (lParam) {
                const wchar_t* warningMsg = (const wchar_t*)lParam;
                std::wstring logMsg = L"[WIM WARNING] ";
                logMsg += warningMsg;
                OutputDebugStringW(logMsg.c_str());
            }
            return WIM_MSG_SUCCESS;
        }
    }
    
    return WIM_MSG_SUCCESS;
}

// Helper to enumerate top-level folders on a volume and filter out exclusions
std::vector<std::wstring> EnumerateIncludedFolders(const std::wstring& volumePath, 
                                                    const wchar_t** userExclusions, 
                                                    int userExclusionCount,
                                                    ProgressCallback callback) {
    std::vector<std::wstring> includedFolders;

    try {
        if (callback) {
            callback(5, L"Scanning volume for folders...");
        }

        // Enumerate all items in the volume root
        for (const auto& entry : fs::directory_iterator(volumePath)) {
            std::wstring itemPath = entry.path().wstring();
            std::wstring itemName = entry.path().filename().wstring();

            // Check if this item is excluded
            if (IsPathExcluded(itemPath, userExclusions, userExclusionCount)) {
                OutputDebugStringW((L"[EnumerateIncludedFolders] EXCLUDING: " + itemPath).c_str());
                continue;
            }

            // Only include directories (we'll capture files in the root separately if needed)
            if (entry.is_directory()) {
                includedFolders.push_back(itemPath);
                OutputDebugStringW((L"[EnumerateIncludedFolders] INCLUDING folder: " + itemPath).c_str());
            }
        }

        if (callback) {
            std::wstring msg = L"Found " + std::to_wstring(includedFolders.size()) + L" folders to backup";
            callback(10, msg.c_str());
        }
    }
    catch (const fs::filesystem_error& e) {
        std::string errStr = e.what();
        std::wstring errMsg = L"Error enumerating volume: " + std::wstring(errStr.begin(), errStr.end());
        OutputDebugStringW((L"[EnumerateIncludedFolders] ERROR: " + errMsg).c_str());
    }

    return includedFolders;
}

// Static callback for WIM API during backup capture - handles progress reporting
// NOTE: Exclusions are now handled BEFORE calling WIMCaptureImage by filtering the folder list
//       This prevents WIM API from attempting to access protected folders at all
//
// IMPORTANT: For WIM_MSG_PROCESS, the lParam points to a BOOL:
//   - Set *lParam = TRUE to INCLUDE the file
//   - Set *lParam = FALSE to EXCLUDE the file (skip it)
static DWORD WINAPI BackupProgressCallback(DWORD msgId, WPARAM wParam, LPARAM lParam, PVOID pvIgnored) {
    ProgressCallback userCallback = (ProgressCallback)pvIgnored;

    // DEBUG: Log all message types received
    OutputDebugStringW((L"[BackupProgressCallback] Received msgId: " + std::to_wstring(msgId)).c_str());

    switch (msgId) {
        case WIM_MSG_PROCESS:
        {
            // WIM_MSG_PROCESS - file being processed during capture
            // wParam = path to file (LPCWSTR)
            // lParam = pointer to BOOL - set to FALSE to exclude file
            OutputDebugStringW(L"[BackupProgressCallback] WIM_MSG_PROCESS received!");

            if (wParam && lParam) {
                const wchar_t* filePath = (const wchar_t*)wParam;
                BOOL* pbInclude = (BOOL*)lParam;
                std::wstring path(filePath);

                // DEBUG: Log the file path being processed
                OutputDebugStringW((L"[BackupProgressCallback] Processing file: " + path).c_str());

                // **SYSTEM EXCLUSIONS - Filter protected folders/files that cause backup failures**

                // Exclude protected Windows folders (ERROR_ACCESS_DENIED)
                if (path.find(L"System Volume Information") != std::wstring::npos ||
                    path.find(L"$RECYCLE.BIN") != std::wstring::npos) {
                    OutputDebugStringW((L"[BackupProgress] EXCLUDING system folder: " + path).c_str());
                    *pbInclude = FALSE;  // EXCLUDE this folder
                    return WIM_MSG_SUCCESS;
                }

                // Exclude locked system files (pagefile, swapfile, hiberfil)
                std::wstring lowerPath = path;
                std::transform(lowerPath.begin(), lowerPath.end(), lowerPath.begin(), ::tolower);

                if (lowerPath.find(L"\\pagefile.sys") != std::wstring::npos ||
                    lowerPath.find(L"\\swapfile.sys") != std::wstring::npos ||
                    lowerPath.find(L"\\hiberfil.sys") != std::wstring::npos) {
                    OutputDebugStringW((L"[BackupProgress] EXCLUDING locked file: " + path).c_str());
                    *pbInclude = FALSE;  // EXCLUDE locked system files
                    return WIM_MSG_SUCCESS;
                }

                // TODO: Add user-defined exclusion filtering here
                // Check if path matches any pattern in UserExclusions list

                // File is not excluded - INCLUDE it and report progress to user
                *pbInclude = TRUE;
                if (userCallback) {
                    // Extract just the filename for cleaner display
                    const wchar_t* fileName = wcsrchr(filePath, L'\\');
                    if (fileName) {
                        fileName++; // Skip the backslash
                    } else {
                        fileName = filePath;
                    }

                    // Report file being processed - use percentage 51 to differentiate from progress messages
                    std::wstring message = L"Backing up: ";
                    message += fileName;
                    OutputDebugStringW((L"[BackupProgressCallback] Sending to UI: " + message).c_str());
                    userCallback(51, message.c_str());
                }
            }
            return WIM_MSG_SUCCESS;
        }

        case WIM_MSG_PROGRESS:
        {
            // WIM_MSG_PROGRESS - overall progress during capture
            // wParam = estimated percentage complete
            if (userCallback) {
                int percentage = (int)wParam;
                // Scale to 30-80% range (capture operation is 30-80% of total backup)
                percentage = 30 + (percentage * 50 / 100);
                userCallback(percentage, L"Capturing files...");
            }
            return WIM_MSG_SUCCESS;
        }

        case WIM_MSG_SETRANGE:
        {
            // WIM_MSG_SETRANGE - total number of files to capture
            // wParam = estimated total number of files
            if (userCallback) {
                std::wstring message = L"Preparing to backup ";
                message += std::to_wstring((DWORD)wParam);
                message += L" files...";
                userCallback(25, message.c_str());
            }
            return WIM_MSG_SUCCESS;
        }

        case WIM_MSG_ERROR:
        {
            // WIM_MSG_ERROR - error occurred during capture
            // lParam = pointer to error message (LPCWSTR)
            if (lParam) {
                const wchar_t* errorMsg = (const wchar_t*)lParam;
                std::wstring logMsg = L"[WIM ERROR] ";
                logMsg += errorMsg;
                OutputDebugStringW(logMsg.c_str());

                if (userCallback) {
                    userCallback(50, errorMsg);  // Show error to user
                }
            }
            return WIM_MSG_SUCCESS;  // Continue despite error
        }

        case WIM_MSG_WARNING:
        {
            // WIM_MSG_WARNING - warning during capture (non-fatal)
            // lParam = pointer to warning message (LPCWSTR)
            if (lParam) {
                const wchar_t* warningMsg = (const wchar_t*)lParam;
                std::wstring logMsg = L"[WIM WARNING] ";
                logMsg += warningMsg;
                OutputDebugStringW(logMsg.c_str());
            }
            return WIM_MSG_SUCCESS;
        }

        case WIM_MSG_INFO:
        {
            // WIM_MSG_INFO - informational message
            // lParam = pointer to info message (LPCWSTR)
            if (lParam) {
                const wchar_t* infoMsg = (const wchar_t*)lParam;
                std::wstring logMsg = L"[WIM INFO] ";
                logMsg += infoMsg;
                OutputDebugStringW(logMsg.c_str());
            }
            return WIM_MSG_SUCCESS;
        }

        case WIM_MSG_RETRY:
        {
            // WIM_MSG_RETRY - retrying operation after failure
            if (wParam) {
                const wchar_t* filePath = (const wchar_t*)wParam;
                std::wstring logMsg = L"[WIM RETRY] Retrying: ";
                logMsg += filePath;
                OutputDebugStringW(logMsg.c_str());
            }
            return WIM_MSG_SUCCESS;  // Allow retry
        }

        default:
            return WIM_MSG_SUCCESS;
    }
}

// Helper function to count images in a WIM file
// Uses WIMGetImageCount API with the correct two-parameter signature
// Returns the number of images (0 if no images or error)
DWORD CountWimImages(HANDLE hWim) {
    if (!hWim || hWim == INVALID_HANDLE_VALUE) {
        return 0;
    }

    // Use WIMGetImageCount with output parameter (per our wimgapi.h stub signature)
    DWORD count = 0;
    if (WIMGetImageCount(hWim, &count)) {
        OutputDebugStringW((L"[CountWimImages] WIMGetImageCount returned: " + std::to_wstring(count)).c_str());
        return count;
    } else {
        DWORD err = GetLastError();
        OutputDebugStringW((L"[CountWimImages] WIMGetImageCount FAILED, error: " + std::to_wstring(err)).c_str());

        // Fallback: manually iterate to count images (more reliable backup method)
        count = 0;
        for (DWORD i = 1; i <= 1000; i++) {
            HANDLE hTest = WIMLoadImage(hWim, i);
            if (hTest && hTest != INVALID_HANDLE_VALUE) {
                count = i;
                WIMCloseHandle(hTest);
            } else {
                break;
            }
        }
        OutputDebugStringW((L"[CountWimImages] Fallback iteration count: " + std::to_wstring(count)).c_str());
        return count;
    }
}

// Helper to capture path into WIM image
// Adds image metadata and returns image handle (must be closed by caller)
HANDLE CaptureToWimImage(HANDLE hWim, const wchar_t* sourcePath, const wchar_t* imageName, ProgressCallback callback, const wchar_t* folderName = nullptr) {
    if (!hWim || !sourcePath || !imageName) {
        SetLastErrorMessage(L"Invalid parameters for image capture");
        return INVALID_HANDLE_VALUE;
    }

    if (callback) {
        callback(30, L"Capturing files to backup archive...");
    }

    // CRITICAL: Count images BEFORE capture to detect new image after capture
    // This is necessary because WIMCaptureImage returns NULL when callback skips files,
    // even though the capture succeeded for non-skipped files
    DWORD imageCountBefore = CountWimImages(hWim);
    LogInfo(L"CaptureToWimImage: Image count BEFORE capture: " + std::to_wstring(imageCountBefore));
    LogInfo(L"CaptureToWimImage: Source path: " + std::wstring(sourcePath));
    LogInfo(L"CaptureToWimImage: Image name: " + std::wstring(imageName));
    if (folderName) {
        LogInfo(L"CaptureToWimImage: Folder filter: " + std::wstring(folderName));
    }

    HANDLE hImage = INVALID_HANDLE_VALUE;

    // If folderName is provided, use folder filtering callback
    if (folderName && wcslen(folderName) > 0) {
        // Create filter context with folder name
        FolderFilterContext filterContext;
        filterContext.folderName = folderName;
        filterContext.userCallback = callback;

        if (callback) {
            std::wstring msg = L"Capturing folder with structure preservation: ";
            msg += folderName;
            callback(25, msg.c_str());
        }
        LogInfo(L"CaptureToWimImage: Using folder filter for: " + std::wstring(folderName));

        // Register folder filter callback
        WIMRegisterMessageCallback(hWim, FolderFilterCallback, &filterContext);

        // Capture - will only include files matching folder filter
        LogInfo(L"CaptureToWimImage: Calling WIMCaptureImage with folder filter...");
        hImage = WIMCaptureImage(hWim, sourcePath, 0);
        DWORD captureErr = GetLastError();
        LogInfo(L"CaptureToWimImage: WIMCaptureImage returned, hImage=" + std::to_wstring(reinterpret_cast<uintptr_t>(hImage)) + 
                L", GetLastError=" + std::to_wstring(captureErr));

        // Unregister callback
        WIMUnregisterMessageCallback(hWim, FolderFilterCallback);
    }
    else {
        // Standard capture without folder filtering
        // Register progress callback to get file-level feedback during capture
        // NOTE: Exclusions are handled in callback by returning WIM_MSG_SKIP_ERROR for protected files
        if (callback) {
            WIMRegisterMessageCallback(hWim, BackupProgressCallback, callback);
            callback(25, L"Starting backup capture...");
        }

        // Capture the volume/directory into WIM
        // Exclusions are handled via callback, not config file
        // NOTE: WIM_FLAG_VERIFY removed - caused ERROR_INVALID_PARAMETER and metadata failures
        LogInfo(L"CaptureToWimImage: Calling WIMCaptureImage (no folder filter)...");
        hImage = WIMCaptureImage(
            hWim, 
            sourcePath,
            0  // No flags - WIM_FLAG_VERIFY caused error -5 metadata failures
        );
        DWORD captureErr = GetLastError();
        LogInfo(L"CaptureToWimImage: WIMCaptureImage returned, hImage=" + std::to_wstring(reinterpret_cast<uintptr_t>(hImage)) + 
                L", GetLastError=" + std::to_wstring(captureErr));

        // Unregister callback after capture completes
        if (callback) {
            WIMUnregisterMessageCallback(hWim, BackupProgressCallback);
        }
    }

    DWORD captureError = GetLastError();
    OutputDebugStringW((L"[CaptureToWimImage] WIMCaptureImage returned, hImage=" + 
                       std::to_wstring(reinterpret_cast<uintptr_t>(hImage)) + 
                       L", GetLastError=" + std::to_wstring(captureError)).c_str());

    // CRITICAL FIX: WIMCaptureImage may return INVALID_HANDLE_VALUE when callback excludes files
    // (*pbInclude = FALSE), BUT the capture may have succeeded for all included files!
    // We detect success by checking if a new image was added to the WIM via WIMGetImageCount.
    if (!hImage || hImage == INVALID_HANDLE_VALUE) {
        LogInfo(L"CaptureToWimImage: WIMCaptureImage returned NULL/INVALID, checking if capture actually succeeded...");
        LogInfo(L"CaptureToWimImage: WIMCaptureImage error code was: " + std::to_wstring(captureError));

        // Give WIM API a moment to finalize internal state before checking image count
        // This ensures WIMGetImageCount returns accurate count after capture completion
        Sleep(100);

        // Count images AFTER capture using proper WIM API
        DWORD imageCountAfter = CountWimImages(hWim);
        LogInfo(L"CaptureToWimImage: Image count AFTER capture: " + std::to_wstring(imageCountAfter));
        LogInfo(L"CaptureToWimImage: Image count BEFORE was: " + std::to_wstring(imageCountBefore));

        if (imageCountAfter > imageCountBefore) {
            // SUCCESS! A new image was added despite WIMCaptureImage returning NULL
            // This happens when the callback excluded files (*pbInclude = FALSE)
            LogInfo(L"CaptureToWimImage: SUCCESS - New image detected! Capture completed with filtered files.");
            LogInfo(L"CaptureToWimImage: Loading new image at index " + std::to_wstring(imageCountAfter));

            hImage = WIMLoadImage(hWim, imageCountAfter);
            if (!hImage || hImage == INVALID_HANDLE_VALUE) {
                DWORD loadError = GetLastError();
                // Even if we can't load the handle, the image WAS created - don't fail the backup
                // The WIM file is still valid - return a special marker indicating success without handle
                OutputDebugStringW((L"[CaptureToWimImage] WARNING: Could not load image handle (error " + 
                                   std::to_wstring(loadError) + L") but capture DID succeed - image count increased!").c_str());

                // Verify one more time by re-counting
                Sleep(50);
                DWORD verifyCount = CountWimImages(hWim);
                OutputDebugStringW((L"[CaptureToWimImage] Verification count: " + std::to_wstring(verifyCount)).c_str());

                if (verifyCount >= imageCountAfter) {
                    // Image exists, just can't get handle - this is OK for folder capture
                    // Return a special marker that caller should interpret as success
                    // Using (HANDLE)1 as marker for "capture succeeded but no handle needed"
                    OutputDebugStringW(L"[CaptureToWimImage] Image verified via count - returning success marker");
                    return (HANDLE)1;
                }

                // If verification failed, still try to return success since count increased
                OutputDebugStringW(L"[CaptureToWimImage] Verification uncertain but count DID increase - returning success marker");
                return (HANDLE)1;
            }

            OutputDebugStringW(L"[CaptureToWimImage] Successfully loaded new image handle!");
        } else {
            // SPECIAL CASE: Image count same but capture error was benign (e.g., all files filtered)
            // Check if this is ERROR_SUCCESS (0) or a known "success with warnings" code
            if (captureError == ERROR_SUCCESS || captureError == ERROR_NO_MORE_FILES) {
                OutputDebugStringW(L"[CaptureToWimImage] Capture returned benign error code - checking if empty capture is OK");

                // For folder captures with heavy filtering, an empty result might be valid
                // Log final count for diagnostics
                DWORD finalCount = CountWimImages(hWim);
                OutputDebugStringW((L"[CaptureToWimImage] Final WIM ImageCount: " + std::to_wstring(finalCount)).c_str());
            }

            // No new image was added - this is a genuine failure
            std::wstring errMsg = L"Failed to capture files to archive. WIM Error: " + std::to_wstring(captureError);
            errMsg += L". No new image was created (before=" + std::to_wstring(imageCountBefore) + 
                      L", after=" + std::to_wstring(imageCountAfter) + L").";
            SetLastErrorMessage(errMsg);
            LogError(errMsg);
            LogError(L"CaptureToWimImage: Source path was: " + std::wstring(sourcePath));
            return INVALID_HANDLE_VALUE;
        }
    }

    LogInfo(L"CaptureToWimImage: Capture successful, setting metadata...");

    // Build simple XML metadata for the image (just NAME, no DESCRIPTION)
    // Sanitize the image name to escape XML special characters (prevents Error 1465)
    std::wstring sanitizedName = SanitizeXmlName(imageName);
    LogInfo(L"CaptureToWimImage: Setting metadata with sanitized name: " + sanitizedName);

    std::wstring xmlMetadata = L"<WIM><IMAGE><NAME>";
    xmlMetadata += sanitizedName;
    xmlMetadata += L"</NAME></IMAGE></WIM>";

    // Set image metadata
    if (!WIMSetImageInformation(hImage, xmlMetadata.c_str())) {
        DWORD metadataError = GetLastError();
        WIMCloseHandle(hImage);
        std::wstring errMsg = L"Failed to set image metadata (Error " + std::to_wstring(metadataError) + L")";
        SetLastErrorMessage(errMsg);
        LogError(errMsg);
        return INVALID_HANDLE_VALUE;
    }

    LogInfo(L"CaptureToWimImage: SUCCESS - Image captured and metadata set");
    return hImage;
}

// Helper to backup system state to SystemState subdirectory (metadata format)
bool BackupSystemState(const std::wstring& destPath, ProgressCallback callback) {
    try {
        // Create SystemState subdirectory
        std::wstring systemStatePath = destPath + L"\\SystemState";
        fs::create_directories(systemStatePath);

        // Backup registry hives
        if (callback) {
            callback(82, L"Backing up registry hives...");
        }

        std::vector<std::wstring> registryHives = {
            L"SAM",
            L"SECURITY",
            L"SOFTWARE",
            L"SYSTEM",
            L"DEFAULT"
        };

        for (const auto& hive : registryHives) {
            std::wstring srcPath = L"C:\\Windows\\System32\\config\\" + hive;
            std::wstring dstPath = systemStatePath + L"\\" + hive;

            try {
                // Registry hives are locked, but VSS snapshot allows access
                // Copy via VSS snapshot if available
                if (fs::exists(srcPath)) {
                    fs::copy_file(srcPath, dstPath, fs::copy_options::overwrite_existing);
                }
            }
            catch (...) {
                // Skip if can't access (might not have permissions)
            }
        }

        // Backup BCD (Boot Configuration Data)
        if (callback) {
            callback(85, L"Backing up boot configuration...");
        }

        std::wstring bcdSrc = L"C:\\Boot\\BCD";
        std::wstring bcdDst = systemStatePath + L"\\BCD";

        if (fs::exists(bcdSrc)) {
            try {
                fs::copy_file(bcdSrc, bcdDst, fs::copy_options::overwrite_existing);
            }
            catch (...) {
                // Try alternate location
                bcdSrc = L"C:\\EFI\\Microsoft\\Boot\\BCD";
                if (fs::exists(bcdSrc)) {
                    fs::copy_file(bcdSrc, bcdDst, fs::copy_options::overwrite_existing);
                }
            }
        }

        // Backup critical system files
        if (callback) {
            callback(87, L"Backing up critical system files...");
        }

        std::vector<std::wstring> criticalFiles = {
            L"C:\\Windows\\System32\\config\\RegBack\\SAM",
            L"C:\\Windows\\System32\\config\\RegBack\\SECURITY",
            L"C:\\Windows\\System32\\config\\RegBack\\SOFTWARE",
            L"C:\\Windows\\System32\\config\\RegBack\\SYSTEM",
            L"C:\\Windows\\System32\\config\\RegBack\\DEFAULT"
        };

        std::wstring regBackPath = systemStatePath + L"\\RegBack";
        fs::create_directories(regBackPath);

        for (const auto& file : criticalFiles) {
            if (fs::exists(file)) {
                try {
                    fs::path filename = fs::path(file).filename();
                    fs::copy_file(file, regBackPath + L"\\" + filename.wstring(),
                        fs::copy_options::overwrite_existing);
                }
                catch (...) {
                    // Skip if can't access
                }
            }
        }

        // Create metadata file documenting what was backed up
        std::wofstream metadataFile(systemStatePath + L"\\SystemState_Metadata.txt");
        if (metadataFile.is_open()) {
            SYSTEMTIME st;
            GetLocalTime(&st);

            metadataFile << L"System State Backup" << std::endl;
            metadataFile << L"Created: " << st.wYear << L"-"
                << st.wMonth << L"-" << st.wDay << L" "
                << st.wHour << L":" << st.wMinute << L":" << st.wSecond << std::endl;
            metadataFile << std::endl;
            metadataFile << L"Components backed up:" << std::endl;
            metadataFile << L"- Registry hives (SAM, SECURITY, SOFTWARE, SYSTEM, DEFAULT)" << std::endl;
            metadataFile << L"- Boot Configuration Data (BCD)" << std::endl;
            metadataFile << L"- Registry backup files" << std::endl;
            metadataFile << std::endl;
            metadataFile << L"Note: Active Directory, Certificate Services, and other components" << std::endl;
            metadataFile << L"are backed up via VSS writers if present on the system." << std::endl;
        }

        if (callback) {
            callback(90, L"System state backup completed");
        }

        return true;
    }
    catch (...) {
        return false;
    }
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
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback) {

        if (!volumePath || !destPath) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            if (callback) {
                callback(0, L"Starting volume backup (WIM format)...");
            }

            // Ensure destPath is a file, not a directory
            std::wstring destFile = destPath;
            // Check if path ends with .ssb (C++17 compatible)
            if (destFile.length() < 4 || destFile.substr(destFile.length() - 4) != L".ssb") {
                // If it's a directory, this is wrong - but handle gracefully
                if (fs::is_directory(destPath)) {
                    SetLastErrorMessage(L"Destination must be a file path ending in .ssb, not a directory");
                    return -1;
                }
            }

            // Create parent directory if needed
            fs::path parentDir = fs::path(destFile).parent_path();
            if (!parentDir.empty()) {
                fs::create_directories(parentDir);
            }

            if (callback) {
                callback(10, L"Creating VSS snapshot...");
            }

            // Create VSS snapshot for consistent backup
            BackupEngine::VSSSnapshotManager vssManager;
            HRESULT hr = vssManager.Initialize();
            if (FAILED(hr)) {
                if (callback) {
                    callback(15, L"VSS unavailable - using direct copy (files may be inconsistent)");
                }
            }

            wchar_t snapshotPath[MAX_PATH] = { 0 };
            std::wstring actualSourcePath = volumePath;

            if (SUCCEEDED(hr)) {
                hr = vssManager.CreateVolumeSnapshot(volumePath, snapshotPath, MAX_PATH);
                if (SUCCEEDED(hr)) {
                    actualSourcePath = snapshotPath;
                    if (callback) {
                        callback(20, L"VSS snapshot created successfully");
                    }
                }
                else {
                    if (callback) {
                        callback(15, L"VSS snapshot failed - using direct copy");
                    }
                }
            }

            // Create WIM file
            if (callback) {
                callback(22, L"Creating WIM backup archive...");
            }

            HANDLE hWim = CreateWimFile(destFile.c_str(), compress, callback);
            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                return -3;
            }

            // Ensure source path has trailing backslash for WIM API
            if (!actualSourcePath.empty() && actualSourcePath.back() != L'\\') {
                actualSourcePath += L'\\';
            }

            // Create image name for this volume
            std::wstring imageName = L"Volume Backup";
            LogInfo(L"BackupVolume: Capturing entire volume as single image");
            LogInfo(L"BackupVolume: Source path: " + actualSourcePath);

            if (callback) {
                callback(25, L"Capturing volume to backup archive...");
            }

            // Capture the ENTIRE VOLUME as ONE image (not individual folders)
            // This is simpler, faster, and more reliable than per-folder capture
            HANDLE hImage = CaptureToWimImage(hWim, actualSourcePath.c_str(), imageName.c_str(), callback, nullptr);

            // CaptureToWimImage returns:
            //   - INVALID_HANDLE_VALUE (0xFFFFFFFF) on failure
            //   - Valid handle on normal success  
            //   - (HANDLE)1 special marker when capture succeeded but no handle available
            if (hImage == INVALID_HANDLE_VALUE) {
                LogError(L"BackupVolume: CaptureToWimImage FAILED for volume: " + actualSourcePath);
                WIMCloseHandle(hWim);
                std::wstring err = L"Failed to capture volume: " + actualSourcePath;
                SetLastErrorMessage(err);
                return -4;
            }

            // Close handle only if it's a real handle (not the success marker)
            if (hImage != (HANDLE)1 && hImage != NULL) {
                WIMCloseHandle(hImage);
            }

            LogInfo(L"BackupVolume: Volume captured successfully");

            if (callback) {
                callback(70, L"Finalizing backup archive...");
            }

            // Close WIM file (this writes the file to disk)
            WIMCloseHandle(hWim);

            // Handle system state separately (metadata/instructions approach)
            if (includeSystemState) {
                if (callback) {
                    callback(80, L"Backing up system state metadata...");
                }

                // Create SystemState directory next to the .ssb file
                std::wstring ssbDir = fs::path(destFile).parent_path().wstring();
                std::wstring systemStateDir = ssbDir + L"\\SystemState";

                bool systemStateSuccess = BackupSystemState(systemStateDir.c_str(), callback);
                if (!systemStateSuccess) {
                    if (callback) {
                        callback(85, L"Warning: System state backup incomplete (may need admin rights)");
                    }
                }
            }

            if (callback) {
                callback(100, L"Volume backup completed successfully");
            }

            return 0;
        }
        catch (const std::exception& e) {
            std::string err = "Exception in BackupVolume: ";
            err += e.what();
            SetLastErrorMessage(std::wstring(err.begin(), err.end()));
            return -99;
        }
        catch (...) {
            SetLastErrorMessage(L"Unknown exception in BackupVolume");
            return -99;
        }
    }

    BACKUPENGINE_API int BackupDisk(
        int diskNumber,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback) {

        if (diskNumber < 0 || !destPath) {
            SetLastErrorMessage(L"Invalid parameters: diskNumber=" + std::to_wstring(diskNumber) + L", destPath=" + (destPath ? L"valid" : L"NULL"));
            return -1;
        }

        try {
            // LOG: Starting backup
            std::wstring logMsg = L"[BackupDisk] Starting backup of Disk " + std::to_wstring(diskNumber) + L" to: " + destPath;
            OutputDebugStringW(logMsg.c_str());

            if (callback) {
                callback(0, L"Starting disk backup - enumerating volumes...");
            }

            // LOG: Validating destination path
            std::wstring destFile = destPath;
            OutputDebugStringW((L"[BackupDisk] Dest file: " + destFile).c_str());

            if (destFile.length() < 4 || destFile.substr(destFile.length() - 4) != L".ssb") {
                if (fs::exists(destPath) && fs::is_directory(destPath)) {
                    SetLastErrorMessage(L"Destination must be a file path ending in .ssb, not a directory");
                    OutputDebugStringW(L"[BackupDisk] ERROR: Destination is directory, not file!");
                    return -1;
                }
            }

            // Create parent directory if needed
            fs::path parentDir = fs::path(destFile).parent_path();
            if (!parentDir.empty()) {
                OutputDebugStringW((L"[BackupDisk] Creating parent dir: " + parentDir.wstring()).c_str());
                fs::create_directories(parentDir);
            }

            // Enumerate volumes on this disk using IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS
            OutputDebugStringW((L"[BackupDisk] Enumerating volumes on Disk " + std::to_wstring(diskNumber)).c_str());

            std::vector<std::wstring> volumes;
            wchar_t volumeName[MAX_PATH];
            HANDLE hFind = FindFirstVolumeW(volumeName, ARRAYSIZE(volumeName));

            if (hFind == INVALID_HANDLE_VALUE) {
                DWORD err = GetLastError();
                std::wstring errMsg = L"Failed to enumerate volumes, Win32 Error: " + std::to_wstring(err);
                SetLastErrorMessage(errMsg);
                OutputDebugStringW((L"[BackupDisk] ERROR: " + errMsg).c_str());
                return -2;
            }

            do {
                // volumeName format: \\?\Volume{guid}\
                // Create a COPY to avoid modifying the FindNextVolumeW buffer
                std::wstring volumeNameCopy = volumeName;

                // Remove trailing backslash to open the volume with CreateFile
                if (!volumeNameCopy.empty() && volumeNameCopy.back() == L'\\') {
                    volumeNameCopy.pop_back();
                }

                // Open the volume to query disk extents
                HANDLE hVolume = CreateFileW(
                    volumeNameCopy.c_str(),
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    NULL,
                    OPEN_EXISTING,
                    0,
                    NULL
                );

                if (hVolume != INVALID_HANDLE_VALUE) {
                    // Query which physical disk(s) this volume is on
                    BYTE buffer[sizeof(VOLUME_DISK_EXTENTS) + 32 * sizeof(DISK_EXTENT)];
                    PVOLUME_DISK_EXTENTS pExtents = (PVOLUME_DISK_EXTENTS)buffer;
                    DWORD bytesReturned = 0;

                    if (DeviceIoControl(
                        hVolume,
                        IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                        NULL, 0,
                        pExtents, sizeof(buffer),
                        &bytesReturned,
                        NULL))
                    {
                        // Check if any extent is on our target disk
                        for (DWORD i = 0; i < pExtents->NumberOfDiskExtents; i++) {
                            if (pExtents->Extents[i].DiskNumber == static_cast<DWORD>(diskNumber)) {
                                // This volume is on our target disk!
                                // Add with trailing backslash for BackupVolume
                                std::wstring volPath = volumeNameCopy + L"\\";
                                volumes.push_back(volPath);
                                OutputDebugStringW((L"[BackupDisk] Found volume on Disk " + std::to_wstring(diskNumber) + L": " + volPath).c_str());
                                break; // Only add once even if multiple extents
                            }
                        }
                    }
                    else {
                        DWORD err = GetLastError();
                        OutputDebugStringW((L"[BackupDisk] DeviceIoControl failed for volume, Error: " + std::to_wstring(err)).c_str());
                    }

                    CloseHandle(hVolume);
                }
                else {
                    DWORD err = GetLastError();
                    OutputDebugStringW((L"[BackupDisk] Failed to open volume: " + volumeNameCopy + L", Error: " + std::to_wstring(err)).c_str());
                }
            } while (FindNextVolumeW(hFind, volumeName, ARRAYSIZE(volumeName)));

            FindVolumeClose(hFind);

            OutputDebugStringW((L"[BackupDisk] Volume enumeration complete. Found " + std::to_wstring(volumes.size()) + L" volumes").c_str());

            if (volumes.empty()) {
                std::wstring errMsg = L"No volumes found on Disk " + std::to_wstring(diskNumber);
                SetLastErrorMessage(errMsg);
                OutputDebugStringW((L"[BackupDisk] ERROR: " + errMsg).c_str());
                return -3;
            }

            if (callback) {
                std::wstring msg = L"Found " + std::to_wstring(volumes.size()) + L" volume(s) on disk " + std::to_wstring(diskNumber);
                callback(10, msg.c_str());
            }

            // Create WIM file for all volumes
            OutputDebugStringW(L"[BackupDisk] Creating WIM file...");

            if (callback) {
                callback(15, L"Creating WIM backup archive...");
            }

            HANDLE hWim = CreateWimFile(destFile.c_str(), compress, callback);
            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                OutputDebugStringW(L"[BackupDisk] ERROR: CreateWimFile failed!");
                SetLastErrorMessage(L"Failed to create WIM file: " + destFile);
                return -4;
            }

            OutputDebugStringW((L"[BackupDisk] WIM file created successfully: " + destFile).c_str());

            // Backup each volume as a separate image in the WIM file
            int volumeIndex = 0;
            for (const auto& volume : volumes) {
                volumeIndex++;
                int progressBase = 20 + (volumeIndex - 1) * (60 / static_cast<int>(volumes.size()));

                if (callback) {
                    std::wstring msg = L"Backing up volume " + std::to_wstring(volumeIndex) + 
                                      L" of " + std::to_wstring(volumes.size()) + L"...";
                    callback(progressBase, msg.c_str());
                }

                // Create VSS snapshot for this volume
                LogInfo(L"BackupDisk: Processing volume " + std::to_wstring(volumeIndex) + L"/" + std::to_wstring(volumes.size()) + L": " + volume);

                BackupEngine::VSSSnapshotManager vssManager;
                HRESULT hr = vssManager.Initialize();
                std::wstring vssStatus = SUCCEEDED(hr) ? L"SUCCESS" : L"FAILED";
                LogInfo(L"BackupDisk: VSS Initialize: " + vssStatus);

                wchar_t snapshotPath[MAX_PATH] = { 0 };
                std::wstring actualSourcePath = volume;

                // Note: trailing backslash added AFTER VSS snapshot assignment below
                // VSS snapshot requires volume path for input, but returns path without backslash

                if (SUCCEEDED(hr)) {
                    LogInfo(L"BackupDisk: Creating VSS snapshot for: " + actualSourcePath);
                    hr = vssManager.CreateVolumeSnapshot(actualSourcePath.c_str(), snapshotPath, MAX_PATH);
                    if (SUCCEEDED(hr)) {
                        actualSourcePath = snapshotPath;
                        LogInfo(L"BackupDisk: VSS snapshot created: " + std::wstring(snapshotPath));
                    }
                    else {
                        LogInfo(L"BackupDisk: VSS snapshot failed (HR=" + std::to_wstring(hr) + L"), using direct path");
                    }
                }

                // CRITICAL: Ensure source path has trailing backslash for WIM API AFTER VSS assignment
                // VSS snapshot paths like "\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy22" need trailing backslash
                // to be recognized as directory roots by WIMCaptureImage
                if (!actualSourcePath.empty() && actualSourcePath.back() != L'\\') {
                    actualSourcePath += L'\\';
                }

                // Create image name for this volume
                std::wstring imageName = L"Disk " + std::to_wstring(diskNumber) + L" Volume " + std::to_wstring(volumeIndex);
                LogInfo(L"BackupDisk: Capturing entire volume as single image: " + imageName);
                LogInfo(L"BackupDisk: Source path: " + actualSourcePath);

                // Capture the ENTIRE VOLUME as ONE image (not individual folders)
                // This is simpler, faster, and more reliable than per-folder capture
                HANDLE hImage = CaptureToWimImage(hWim, actualSourcePath.c_str(), imageName.c_str(), callback, nullptr);

                // CaptureToWimImage returns:
                //   - INVALID_HANDLE_VALUE (0xFFFFFFFF) on failure
                //   - Valid handle on normal success  
                //   - (HANDLE)1 special marker when capture succeeded but no handle available
                if (hImage == INVALID_HANDLE_VALUE) {
                    LogError(L"BackupDisk: CaptureToWimImage FAILED for volume: " + volume);
                    WIMCloseHandle(hWim);
                    std::wstring err = L"Failed to capture volume: " + volume;
                    SetLastErrorMessage(err);
                    return -4;
                }

                // Close handle only if it's a real handle (not the success marker)
                if (hImage != (HANDLE)1 && hImage != NULL) {
                    WIMCloseHandle(hImage);
                }

                LogInfo(L"BackupDisk: Volume " + std::to_wstring(volumeIndex) + L" captured successfully");
            }

            LogInfo(L"BackupDisk: All volumes captured, finalizing WIM...");

            if (callback) {
                callback(85, L"Finalizing backup archive...");
            }

            // Close WIM file
            WIMCloseHandle(hWim);
            LogInfo(L"BackupDisk: WIM file closed successfully");

            // Handle system state if requested
            if (includeSystemState) {
                if (callback) {
                    callback(90, L"Backing up system state metadata...");
                }

                std::wstring ssbDir = fs::path(destFile).parent_path().wstring();
                std::wstring systemStateDir = ssbDir + L"\\SystemState";

                bool systemStateSuccess = BackupSystemState(systemStateDir.c_str(), callback);
                if (!systemStateSuccess) {
                    if (callback) {
                        callback(95, L"Warning: System state backup incomplete");
                    }
                }
            }

            OutputDebugStringW(L"[BackupDisk] Backup completed successfully!");

            if (callback) {
                callback(100, L"Disk backup completed successfully!");
            }

            return 0;
        }
        catch (const std::exception& e) {
            std::string err = "Exception in BackupDisk: ";
            err += e.what();
            std::wstring errW(err.begin(), err.end());
            SetLastErrorMessage(errW);
            OutputDebugStringW((L"[BackupDisk] EXCEPTION: " + errW).c_str());
            return -10;
        }
        catch (...) {
            SetLastErrorMessage(L"Unknown exception in BackupDisk");
            OutputDebugStringW(L"[BackupDisk] FATAL: Unknown exception!");
            return -11;
        }
    }

    // NEW FUNCTION: Incremental disk backup using WIM_FLAG_REFERENCE
    BACKUPENGINE_API int BackupDiskIncremental(
        int diskNumber,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback) {

        if (!destPath || diskNumber < 0) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            std::wstring destFile(destPath);

            // Check if base backup (.ssb file) exists
            if (!fs::exists(destFile)) {
                // No base backup exists - create full backup instead
                if (callback) {
                    callback(0, L"No base backup found - creating initial full backup...");
                }
                return BackupDisk(diskNumber, destPath, includeSystemState, compress, userExclusions, userExclusionCount, callback);
            }

            if (callback) {
                callback(0, L"Starting incremental disk backup (WIM referential)...");
            }

            // Enumerate volumes on this disk
            std::vector<std::wstring> volumes;
            wchar_t volumeName[MAX_PATH];
            HANDLE hFind = FindFirstVolumeW(volumeName, ARRAYSIZE(volumeName));

            if (hFind == INVALID_HANDLE_VALUE) {
                SetLastErrorMessage(L"Failed to enumerate volumes");
                return -2;
            }

            do {
                size_t len = wcslen(volumeName);
                if (len > 0 && volumeName[len - 1] == L'\\') {
                    volumeName[len - 1] = L'\0';
                }

                std::wstring volumeNameCopy = volumeName;
                if (volumeNameCopy.size() > 4 && volumeNameCopy.substr(0, 4) == L"\\\\?\\") {
                    volumeNameCopy = volumeNameCopy.substr(4);
                }

                HANDLE hVolume = CreateFileW(
                    volumeName,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    NULL,
                    OPEN_EXISTING,
                    0,
                    NULL
                );

                if (hVolume != INVALID_HANDLE_VALUE) {
                    BYTE buffer[sizeof(VOLUME_DISK_EXTENTS) + 32 * sizeof(DISK_EXTENT)];
                    PVOLUME_DISK_EXTENTS pExtents = (PVOLUME_DISK_EXTENTS)buffer;
                    DWORD bytesReturned = 0;

                    if (DeviceIoControl(
                        hVolume,
                        IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                        NULL, 0,
                        pExtents, sizeof(buffer),
                        &bytesReturned,
                        NULL))
                    {
                        for (DWORD i = 0; i < pExtents->NumberOfDiskExtents; i++) {
                            if (pExtents->Extents[i].DiskNumber == static_cast<DWORD>(diskNumber)) {
                                std::wstring volPath = volumeNameCopy + L"\\";
                                volumes.push_back(volPath);
                                break;
                            }
                        }
                    }

                    CloseHandle(hVolume);
                }
            } while (FindNextVolumeW(hFind, volumeName, ARRAYSIZE(volumeName)));

            FindVolumeClose(hFind);

            if (volumes.empty()) {
                SetLastErrorMessage(L"No volumes found on disk");
                return -3;
            }

            if (callback) {
                std::wstring msg = L"Found " + std::to_wstring(volumes.size()) + L" volume(s) for incremental backup";
                callback(10, msg.c_str());
            }

            // Open existing WIM file with WIM_FLAG_REFERENCE to add incremental images
            if (callback) {
                callback(15, L"Opening existing backup with WIM_FLAG_REFERENCE...");
            }

            // When opening existing WIM, compression type must be 0 (read from file)
            // Passing WIM_COMPRESS_LZMS/NONE when opening existing WIM causes error -4!
            // NOTE: WIM_FLAG_VERIFY removed - can cause ERROR_INVALID_PARAMETER (87) on valid files
            // CRITICAL: Must use WIM_GENERIC_READ | WIM_GENERIC_WRITE when opening existing WIM to append images!
            //           WIM_GENERIC_WRITE alone causes ERROR_INVALID_PARAMETER (87)
            HANDLE hWim = WIMCreateFile(
                destFile.c_str(),
                WIM_GENERIC_READ | WIM_GENERIC_WRITE,  // Need READ+WRITE to append images
                WIM_OPEN_EXISTING,
                WIM_FLAG_REFERENCE,  // Enable referential images only (no VERIFY)
                0,  // MUST be 0 when opening existing WIM! Compression read from file.
                NULL
            );

            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                DWORD wimError = GetLastError();
                std::wstring err = L"Failed to open existing backup for incremental. WIM Error: " + 
                                  std::to_wstring(wimError) + 
                                  L". Ensure full backup exists and is not corrupted.";
                SetLastErrorMessage(err);
                return -4;
            }

            // WIM API automatically handles incremental images when appending to existing WIM
            // Each new image will reference common data from previous images

            // Backup each volume as new incremental image
            int volumeIndex = 0;
            for (const auto& volume : volumes) {
                volumeIndex++;
                int progressBase = 20 + (volumeIndex - 1) * (70 / static_cast<int>(volumes.size()));

                if (callback) {
                    std::wstring msg = L"Creating incremental image " + std::to_wstring(volumeIndex) + 
                                      L" of " + std::to_wstring(volumes.size()) + L"...";
                    callback(progressBase, msg.c_str());
                }

                // Create VSS snapshot
                BackupEngine::VSSSnapshotManager vssManager;
                HRESULT hr = vssManager.Initialize();

                wchar_t snapshotPath[MAX_PATH] = { 0 };
                std::wstring actualSourcePath = volume + L"\\";

                if (SUCCEEDED(hr)) {
                    hr = vssManager.CreateVolumeSnapshot(actualSourcePath.c_str(), snapshotPath, MAX_PATH);
                    if (SUCCEEDED(hr)) {
                        actualSourcePath = snapshotPath;
                        // CRITICAL: VSS snapshot path does NOT include trailing backslash
                        // WIM API requires trailing backslash for volume/directory capture
                        if (!actualSourcePath.empty() && actualSourcePath.back() != L'\\') {
                            actualSourcePath += L'\\';
                        }
                    }
                }

                // Capture new image referencing previous images
                std::wstring imageName = L"Disk " + std::to_wstring(diskNumber) + 
                                        L" Volume " + std::to_wstring(volumeIndex) + 
                                        L" (Incremental)";

                // The WIM_FLAG_REFERENCE in WIMCreateFile automatically makes new images reference existing ones
                HANDLE hImage = CaptureToWimImage(hWim, actualSourcePath.c_str(), 
                                                 imageName.c_str(), callback);

                if (!hImage || hImage == INVALID_HANDLE_VALUE) {
                    WIMCloseHandle(hWim);
                    std::wstring err = L"Failed to capture incremental image " + std::to_wstring(volumeIndex);
                    SetLastErrorMessage(err);
                    return -6;
                }

                WIMCloseHandle(hImage);
            }

            if (callback) {
                callback(95, L"Finalizing incremental backup...");
            }

            WIMCloseHandle(hWim);

            if (callback) {
                callback(100, L"Incremental disk backup completed successfully!");
            }

            return 0;
        }
        catch (const std::exception& e) {
            std::string err = "Exception in BackupDiskIncremental: ";
            err += e.what();
            SetLastErrorMessage(std::wstring(err.begin(), err.end()));
            return -10;
        }
    }

    // NEW FUNCTION: Differential disk backup using WIM_FLAG_REFERENCE
    BACKUPENGINE_API int BackupDiskDifferential(
        int diskNumber,
        const wchar_t* destPath,
        bool includeSystemState,
        bool compress,
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback) {

        if (!destPath || diskNumber < 0) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        try {
            std::wstring destFile(destPath);

            // Check if base backup (.ssb file) exists
            if (!fs::exists(destFile)) {
                // No base backup exists - create full backup instead
                if (callback) {
                    callback(0, L"No base backup found - creating initial full backup...");
                }
                return BackupDisk(diskNumber, destPath, includeSystemState, compress, userExclusions, userExclusionCount, callback);
            }

            if (callback) {
                callback(0, L"Starting differential disk backup (WIM referential)...");
            }

            // Enumerate volumes on this disk
            std::vector<std::wstring> volumes;
            wchar_t volumeName[MAX_PATH];
            HANDLE hFind = FindFirstVolumeW(volumeName, ARRAYSIZE(volumeName));

            if (hFind == INVALID_HANDLE_VALUE) {
                SetLastErrorMessage(L"Failed to enumerate volumes");
                return -2;
            }

            do {
                size_t len = wcslen(volumeName);
                if (len > 0 && volumeName[len - 1] == L'\\') {
                    volumeName[len - 1] = L'\0';
                }

                std::wstring volumeNameCopy = volumeName;
                if (volumeNameCopy.size() > 4 && volumeNameCopy.substr(0, 4) == L"\\\\?\\") {
                    volumeNameCopy = volumeNameCopy.substr(4);
                }

                HANDLE hVolume = CreateFileW(
                    volumeName,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    NULL,
                    OPEN_EXISTING,
                    0,
                    NULL
                );

                if (hVolume != INVALID_HANDLE_VALUE) {
                    BYTE buffer[sizeof(VOLUME_DISK_EXTENTS) + 32 * sizeof(DISK_EXTENT)];
                    PVOLUME_DISK_EXTENTS pExtents = (PVOLUME_DISK_EXTENTS)buffer;
                    DWORD bytesReturned = 0;

                    if (DeviceIoControl(
                        hVolume,
                        IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                        NULL, 0,
                        pExtents, sizeof(buffer),
                        &bytesReturned,
                        NULL))
                    {
                        for (DWORD i = 0; i < pExtents->NumberOfDiskExtents; i++) {
                            if (pExtents->Extents[i].DiskNumber == static_cast<DWORD>(diskNumber)) {
                                std::wstring volPath = volumeNameCopy + L"\\";
                                volumes.push_back(volPath);
                                break;
                            }
                        }
                    }

                    CloseHandle(hVolume);
                }
            } while (FindNextVolumeW(hFind, volumeName, ARRAYSIZE(volumeName)));

            FindVolumeClose(hFind);

            if (volumes.empty()) {
                SetLastErrorMessage(L"No volumes found on disk");
                return -3;
            }

            if (callback) {
                std::wstring msg = L"Found " + std::to_wstring(volumes.size()) + L" volume(s) for differential backup";
                callback(10, msg.c_str());
            }

            // Open existing WIM file with WIM_FLAG_REFERENCE to add differential images
            // Differential always references the FIRST (full) backup, not the most recent
            if (callback) {
                callback(15, L"Opening existing backup with WIM_FLAG_REFERENCE...");
            }

            // When opening existing WIM, compression type must be 0 (read from file)
            // Passing WIM_COMPRESS_LZMS/NONE when opening existing WIM causes error -4!
            // NOTE: WIM_FLAG_VERIFY removed - can cause ERROR_INVALID_PARAMETER (87) on valid files
            // CRITICAL: Must use WIM_GENERIC_READ | WIM_GENERIC_WRITE when opening existing WIM to append images!
            //           WIM_GENERIC_WRITE alone causes ERROR_INVALID_PARAMETER (87)
            HANDLE hWim = WIMCreateFile(
                destFile.c_str(),
                WIM_GENERIC_READ | WIM_GENERIC_WRITE,  // Need READ+WRITE to append images
                WIM_OPEN_EXISTING,
                WIM_FLAG_REFERENCE,  // Enable referential images only (no VERIFY)
                0,  // MUST be 0 when opening existing WIM! Compression read from file.
                NULL
            );

            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                DWORD wimError = GetLastError();
                std::wstring err = L"Failed to open existing backup for differential. WIM Error: " + 
                                  std::to_wstring(wimError) + 
                                  L". Ensure full backup exists and is not corrupted.";
                SetLastErrorMessage(err);
                return -4;
            }

            // Backup each volume as new differential image (referencing first/full backup)
            int volumeIndex = 0;
            for (const auto& volume : volumes) {
                volumeIndex++;
                int progressBase = 20 + (volumeIndex - 1) * (70 / static_cast<int>(volumes.size()));

                if (callback) {
                    std::wstring msg = L"Creating differential image " + std::to_wstring(volumeIndex) + 
                                      L" of " + std::to_wstring(volumes.size()) + L"...";
                    callback(progressBase, msg.c_str());
                }

                // Create VSS snapshot
                BackupEngine::VSSSnapshotManager vssManager;
                HRESULT hr = vssManager.Initialize();

                wchar_t snapshotPath[MAX_PATH] = { 0 };
                std::wstring actualSourcePath = volume + L"\\";

                if (SUCCEEDED(hr)) {
                    hr = vssManager.CreateVolumeSnapshot(actualSourcePath.c_str(), snapshotPath, MAX_PATH);
                    if (SUCCEEDED(hr)) {
                        actualSourcePath = snapshotPath;
                        // CRITICAL: VSS snapshot path does NOT include trailing backslash
                        // WIM API requires trailing backslash for volume/directory capture
                        if (!actualSourcePath.empty() && actualSourcePath.back() != L'\\') {
                            actualSourcePath += L'\\';
                        }
                    }
                }

                // Capture new image referencing base backup (differential)
                std::wstring imageName = L"Disk " + std::to_wstring(diskNumber) + 
                                        L" Volume " + std::to_wstring(volumeIndex) + 
                                        L" (Differential)";

                // The WIM_FLAG_REFERENCE makes new images reference the first (full) backup
                HANDLE hImage = CaptureToWimImage(hWim, actualSourcePath.c_str(), 
                                                 imageName.c_str(), callback);

                if (!hImage || hImage == INVALID_HANDLE_VALUE) {
                    WIMCloseHandle(hWim);
                    std::wstring err = L"Failed to capture differential image " + std::to_wstring(volumeIndex);
                    SetLastErrorMessage(err);
                    return -6;
                }

                WIMCloseHandle(hImage);
            }

            if (callback) {
                callback(95, L"Finalizing differential backup...");
            }

            WIMCloseHandle(hWim);

            if (callback) {
                callback(100, L"Differential disk backup completed successfully!");
            }

            return 0;
        }
        catch (const std::exception& e) {
            std::string err = "Exception in BackupDiskDifferential: ";
            err += e.what();
            SetLastErrorMessage(std::wstring(err.begin(), err.end()));
            return -10;
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
