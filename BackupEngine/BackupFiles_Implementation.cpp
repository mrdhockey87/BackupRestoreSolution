// BackupFiles_Implementation.cpp - Core file backup with WIM format for mountability
#include "BackupEngine.h"
#include "BackupEngine_Common.h"
#include <Windows.h>
#include <string>
#include <filesystem>
#include <vector>
#include <queue>
#include <fstream>
#include <wimgapi.h>  // Windows Imaging API for WIM file creation

#pragma comment(lib, "wimgapi.lib")

namespace fs = std::filesystem;
extern void SetLastErrorMessage(const std::wstring& error);

namespace {
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

    DWORD GetUnicodeXmlBufferSize(const std::wstring& xml) {
        return static_cast<DWORD>((xml.length() + 1) * sizeof(wchar_t));
    }

    // Context structure for WIM capture with user exclusions
    struct FileBackupContext {
        const wchar_t** userExclusions;
        int userExclusionCount;
        ProgressCallback callback;
        int filesProcessed;
    };

    // WIM callback for file backup with exclusion filtering
    static DWORD WINAPI FileBackupCallback(DWORD msgId, WPARAM wParam, LPARAM lParam, PVOID pvContext) {
        FileBackupContext* context = (FileBackupContext*)pvContext;

        switch (msgId) {
            case WIM_MSG_PROCESS:
            {
                if (wParam && lParam) {
                    const wchar_t* filePath = (const wchar_t*)wParam;
                    BOOL* pbInclude = (BOOL*)lParam;
                    std::wstring path(filePath);

                    // Use centralized two-tier exclusion checking (system + user exclusions)
                    if (BackupEngine::Common::IsPathExcluded(path, context->userExclusions, context->userExclusionCount)) {
                        *pbInclude = FALSE;
                        return WIM_MSG_SUCCESS;
                    }

                    // File is included - report progress
                    *pbInclude = TRUE;
                    if (context->callback) {
                        context->filesProcessed++;
                        if (context->filesProcessed % 50 == 0) {
                            const wchar_t* fileName = wcsrchr(filePath, L'\\');
                            if (fileName) fileName++;
                            else fileName = filePath;
                            std::wstring message = L"Backing up: ";
                            message += fileName;
                            context->callback(51, message.c_str());
                        }
                    }
                }
                return WIM_MSG_SUCCESS;
            }

            case WIM_MSG_PROGRESS:
            {
                if (context->callback) {
                    int percentage = (int)wParam;
                    percentage = 30 + (percentage * 60 / 100);
                    context->callback(percentage, L"Capturing files...");
                }
                return WIM_MSG_SUCCESS;
            }

            default:
                return WIM_MSG_SUCCESS;
        }
    }
}

extern "C" {

    BACKUPENGINE_API int BackupFiles(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback,
        LogCallback logCallback) {

        if (!sourcePath || !destPath) {
            SetLastErrorMessage(L"Invalid parameters");
            if (logCallback) logCallback(3, L"BackupFiles: Invalid parameters", L"sourcePath or destPath is NULL");
            return -1;
        }

        try {
            if (callback) {
                callback(0, L"Starting file backup...");
            }
            if (logCallback) logCallback(0, L"Starting file backup", std::wstring(sourcePath).c_str());

            std::wstring sourceStr(sourcePath);

            // Check if source is a device path (e.g., \\.\PHYSICALDRIVE5 or \\?\Volume{guid}\)
            // Device paths can't be checked with fs::exists() - they need special handling
            bool isDevicePath = (sourceStr.find(L"\\\\.\\") == 0 || sourceStr.find(L"\\\\?\\") == 0);

            // Verify source exists (skip for device paths - they're handled differently)
            if (!isDevicePath && !fs::exists(sourcePath)) {
                SetLastErrorMessage(L"Source path does not exist");
                return -2;
            }

            // If source is a device path, return error - these should be handled by BackupVolume or BackupDisk
            if (isDevicePath) {
                SetLastErrorMessage(L"Device paths (e.g., \\\\.\\PHYSICALDRIVE or \\\\?\\Volume) must be backed up using BackupVolume or BackupDisk functions, not BackupFiles");
                return -10;
            }

            // Verify source is a directory or file
            bool isDirectory = fs::is_directory(sourcePath);
            bool isFile = fs::is_regular_file(sourcePath);

            if (!isDirectory && !isFile) {
                SetLastErrorMessage(L"Source is not a valid file or directory");
                return -3;
            }

            // Ensure destPath ends with .ssb extension
            std::wstring wimPath = destPath;
            if (wimPath.find(L".ssb") == std::wstring::npos &&
                wimPath.find(L".wim") == std::wstring::npos) {
                // If destPath doesn't have .ssb extension, add it
                wimPath += L".ssb";
            }

            if (callback) {
                callback(5, L"Creating backup archive...");
            }

            // Create WIM file for backup
            DWORD creationResult = 0;
            HANDLE hWim = WIMCreateFile(
                wimPath.c_str(),
                WIM_GENERIC_WRITE,
                WIM_CREATE_NEW,
                WIM_FLAG_VERIFY | WIM_FLAG_SHARE_WRITE,
                WIM_COMPRESS_LZMS,
                &creationResult);

            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                DWORD error = ::GetLastError();
                std::wstring errorMsg = L"Failed to create backup archive: " + wimPath + 
                                       L" (Error: " + std::to_wstring(error) + L")";
                SetLastErrorMessage(errorMsg);
                if (logCallback) logCallback(3, L"Failed to create WIM file", errorMsg.c_str());
                return -4;
            }

            if (callback) {
                callback(10, L"Preparing file capture...");
            }

            // Setup callback context
            FileBackupContext context;
            context.userExclusions = userExclusions;
            context.userExclusionCount = userExclusionCount;
            context.callback = callback;
            context.filesProcessed = 0;

            // Register callback
            WIMRegisterMessageCallback(hWim, reinterpret_cast<FARPROC>(FileBackupCallback), &context);

            if (callback) {
                callback(20, L"Starting file capture...");
            }

            // Capture the source into WIM
            HANDLE hImage = WIMCaptureImage(hWim, sourcePath, 0);

            // Unregister callback
            WIMUnregisterMessageCallback(hWim, reinterpret_cast<FARPROC>(FileBackupCallback));

            if (!hImage || hImage == INVALID_HANDLE_VALUE) {
                DWORD error = ::GetLastError();
                WIMCloseHandle(hWim);

                std::wstring errorMsg = L"Failed to capture files to archive (Error: " + std::to_wstring(error) + L")";
                SetLastErrorMessage(errorMsg);
                if (logCallback) logCallback(3, L"Failed to capture files", errorMsg.c_str());
                return -5;
            }

            if (callback) {
                callback(90, L"Setting backup metadata...");
            }

            // Set image metadata
            std::wstring sourcePathStr = sourcePath;
            size_t lastSlash = sourcePathStr.find_last_of(L"\\/");
            std::wstring imageName = (lastSlash != std::wstring::npos) 
                ? sourcePathStr.substr(lastSlash + 1) 
                : sourcePathStr;

            if (imageName.empty()) {
                imageName = L"File Backup";
            }

            std::wstring sanitizedImageName = SanitizeXmlName(imageName);
            std::wstring imageXml = L"<IMAGE><NAME>" + sanitizedImageName + L"</NAME></IMAGE>";
            DWORD xmlSize = GetUnicodeXmlBufferSize(imageXml);

            if (!WIMSetImageInformation(hImage, const_cast<wchar_t*>(imageXml.c_str()), xmlSize)) {
                DWORD error = ::GetLastError();
                if (hImage != INVALID_HANDLE_VALUE) {
                    WIMCloseHandle(hImage);
                }
                WIMCloseHandle(hWim);

                std::wstring errorMsg = L"Failed to set backup metadata. ";
                errorMsg += L"XML='" + imageXml + L"'. Error: " + std::to_wstring(error);
                SetLastErrorMessage(errorMsg);
                if (logCallback) logCallback(3, L"Failed to set backup metadata", errorMsg.c_str());
                return -5;
            }

            // Close handles
            if (hImage != INVALID_HANDLE_VALUE) {
                WIMCloseHandle(hImage);
            }
            WIMCloseHandle(hWim);

            if (callback) {
                callback(100, L"Backup completed successfully");
            }

            if (logCallback) {
                logCallback(1, L"Backup completed", wimPath.c_str());
            }

            return 0;
        }
        catch (const fs::filesystem_error& e) {
            std::wstring error = L"Filesystem error: ";
            error += std::wstring(e.what(), e.what() + strlen(e.what()));
            SetLastErrorMessage(error);
            return -6;
        }
        catch (const std::exception& e) {
            std::wstring error = L"Exception: ";
            error += std::wstring(e.what(), e.what() + strlen(e.what()));
            SetLastErrorMessage(error);
            return -7;
        }
        catch (...) {
            SetLastErrorMessage(L"Unknown exception in BackupFiles");
            return -99;
        }
    }
}


