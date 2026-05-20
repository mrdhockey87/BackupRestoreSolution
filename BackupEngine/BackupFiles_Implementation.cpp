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
    struct FileBackupSelectionEntry {
        std::wstring normalizedPath;
        bool includeDescendants;
    };

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

    HANDLE OpenWimArchiveForFileBackup(const std::wstring& wimPath, DWORD* creationResult) {
        if (fs::exists(wimPath)) {
            return WIMCreateFile(
                wimPath.c_str(),
                WIM_GENERIC_READ | WIM_GENERIC_WRITE,
                WIM_OPEN_EXISTING,
                0,
                0,
                nullptr);
        }

        DWORD localCreationResult = 0;
        HANDLE hWim = WIMCreateFile(
            wimPath.c_str(),
            WIM_GENERIC_WRITE,
            WIM_CREATE_NEW,
            WIM_FLAG_VERIFY | WIM_FLAG_SHARE_WRITE,
            WIM_COMPRESS_LZMS,
            &localCreationResult);

        if (creationResult != nullptr) {
            *creationResult = localCreationResult;
        }

        return hWim;
    }

    std::wstring UpsertImageXmlElement(const std::wstring& xml, const std::wstring& elementName, const std::wstring& elementValue) {
        const std::wstring openTag = L"<" + elementName + L">";
        const std::wstring closeTag = L"</" + elementName + L">";

        size_t elementStart = xml.find(openTag);
        if (elementStart != std::wstring::npos) {
            size_t valueStart = elementStart + openTag.length();
            size_t elementEnd = xml.find(closeTag, valueStart);
            if (elementEnd != std::wstring::npos) {
                return xml.substr(0, valueStart) + elementValue + xml.substr(elementEnd);
            }
        }

        const std::wstring imageCloseTag = L"</IMAGE>";
        size_t imageClose = xml.rfind(imageCloseTag);
        if (imageClose != std::wstring::npos) {
            return xml.substr(0, imageClose) + openTag + elementValue + closeTag + xml.substr(imageClose);
        }

        return L"<IMAGE>" + openTag + elementValue + closeTag + L"</IMAGE>";
    }

    std::wstring ResolveBackupStartTimestamp() {
        std::wstring backupStartTimestamp = BackupEngine::Common::GetCurrentJobBackupStartTimestamp();
        return backupStartTimestamp.empty()
            ? BackupEngine::Common::GetCurrentLocalTimestamp()
            : backupStartTimestamp;
    }

    std::wstring BuildFileBackupMetadataPayload(const std::wstring& backupStartTime) {
        std::wstring payload;
        payload += L"<SCHEMA_VERSION>1</SCHEMA_VERSION>";
        payload += L"<BACKUP_KIND>FILE_FOLDER_IMAGE</BACKUP_KIND>";
        payload += L"<BACKUP_START_TIME>" + SanitizeXmlName(backupStartTime) + L"</BACKUP_START_TIME>";
        return payload;
    }

    // Context structure for WIM capture with user exclusions
    struct FileBackupContext {
        const wchar_t** userExclusions;
        int userExclusionCount;
        ProgressCallback callback;
        int filesProcessed;
        std::wstring captureRootPath;
        std::vector<FileBackupSelectionEntry> includeSelections;
    };

    std::wstring NormalizePathForComparison(const std::wstring& path) {
        std::wstring normalized = path;
        std::replace(normalized.begin(), normalized.end(), L'/', L'\\');

        while (normalized.length() > 3 && !normalized.empty() && normalized.back() == L'\\') {
            normalized.pop_back();
        }

        return normalized;
    }

    bool PathsEqualInsensitive(const std::wstring& left, const std::wstring& right) {
        return _wcsicmp(left.c_str(), right.c_str()) == 0;
    }

    std::wstring EnsureTrailingSeparator(const std::wstring& path) {
        if (path.empty() || path.back() == L'\\') {
            return path;
        }

        return path + L'\\';
    }

    bool IsSameOrDescendantPath(const std::wstring& path, const std::wstring& ancestor) {
        if (PathsEqualInsensitive(path, ancestor)) {
            return true;
        }

        std::wstring normalizedAncestor = EnsureTrailingSeparator(ancestor);
        return path.length() > normalizedAncestor.length() &&
               _wcsnicmp(path.c_str(), normalizedAncestor.c_str(), normalizedAncestor.length()) == 0;
    }

    std::vector<FileBackupSelectionEntry> BuildIncludeSelections(
        const wchar_t** includePaths,
        int includePathCount,
        const std::wstring& captureRootPath) {
        std::vector<FileBackupSelectionEntry> selections;
        if (includePaths == nullptr || includePathCount <= 0) {
            return selections;
        }

        for (int index = 0; index < includePathCount; index++) {
            const wchar_t* includePath = includePaths[index];
            if (includePath == nullptr || *includePath == L'\0') {
                continue;
            }

            std::wstring normalizedPath = NormalizePathForComparison(includePath);
            if (normalizedPath.empty()) {
                continue;
            }

            bool includeDescendants = false;
            try {
                includeDescendants = fs::exists(includePath) && fs::is_directory(includePath);
            }
            catch (...) {
                includeDescendants = false;
            }

            if (PathsEqualInsensitive(normalizedPath, captureRootPath)) {
                includeDescendants = true;
            }

            auto existingSelection = std::find_if(
                selections.begin(),
                selections.end(),
                [&](const FileBackupSelectionEntry& entry) {
                    return PathsEqualInsensitive(entry.normalizedPath, normalizedPath);
                });

            if (existingSelection != selections.end()) {
                existingSelection->includeDescendants = existingSelection->includeDescendants || includeDescendants;
                continue;
            }

            selections.push_back(FileBackupSelectionEntry
            {
                normalizedPath,
                includeDescendants
            });
        }

        return selections;
    }

    bool ShouldIncludeSelectionPath(const FileBackupContext* context, const std::wstring& path) {
        if (context == nullptr || context->includeSelections.empty()) {
            return true;
        }

        std::wstring normalizedPath = NormalizePathForComparison(path);
        if (normalizedPath.empty()) {
            return false;
        }

        if (!context->captureRootPath.empty() && PathsEqualInsensitive(normalizedPath, context->captureRootPath)) {
            return true;
        }

        for (const FileBackupSelectionEntry& selection : context->includeSelections) {
            if (PathsEqualInsensitive(normalizedPath, selection.normalizedPath)) {
                return true;
            }

            if (IsSameOrDescendantPath(selection.normalizedPath, normalizedPath)) {
                return true;
            }

            if (selection.includeDescendants && IsSameOrDescendantPath(normalizedPath, selection.normalizedPath)) {
                return true;
            }
        }

        return false;
    }

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

                    if (!ShouldIncludeSelectionPath(context, path)) {
                        *pbInclude = FALSE;
                        return WIM_MSG_SUCCESS;
                    }

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

    static int BackupFilesCore(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t** includePaths,
        int includePathCount,
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

            // Create or open WIM file for backup
            DWORD creationResult = 0;
            HANDLE hWim = OpenWimArchiveForFileBackup(wimPath, &creationResult);

            if (!hWim || hWim == INVALID_HANDLE_VALUE) {
                DWORD error = ::GetLastError();
                std::wstring errorMsg = L"Failed to open backup archive: " + wimPath + 
                                       L" (Error: " + std::to_wstring(error) + L")";
                SetLastErrorMessage(errorMsg);
                if (logCallback) logCallback(3, L"Failed to open WIM file", errorMsg.c_str());
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
            context.captureRootPath.clear();
            context.includeSelections.clear();

            std::wstring capturePath = sourceStr;
            std::vector<std::wstring> defaultIncludePaths;
            if (isFile) {
                fs::path sourceFilePath(sourceStr);
                fs::path parentPath = sourceFilePath.parent_path();
                if (parentPath.empty()) {
                    SetLastErrorMessage(L"Could not determine parent directory for selected file backup");
                    WIMCloseHandle(hWim);
                    return -3;
                }

                capturePath = parentPath.wstring();
                defaultIncludePaths.push_back(sourceStr);
            }

            context.captureRootPath = NormalizePathForComparison(capturePath);

            std::vector<const wchar_t*> effectiveIncludePathPointers;
            const wchar_t** effectiveIncludePaths = includePaths;
            int effectiveIncludePathCount = includePathCount;
            if (!defaultIncludePaths.empty()) {
                effectiveIncludePathPointers.reserve(defaultIncludePaths.size());
                for (const std::wstring& includePath : defaultIncludePaths) {
                    effectiveIncludePathPointers.push_back(includePath.c_str());
                }

                effectiveIncludePaths = effectiveIncludePathPointers.data();
                effectiveIncludePathCount = static_cast<int>(effectiveIncludePathPointers.size());
            }

            context.includeSelections = BuildIncludeSelections(
                effectiveIncludePaths,
                effectiveIncludePathCount,
                context.captureRootPath);

            // Register callback
            WIMRegisterMessageCallback(hWim, reinterpret_cast<FARPROC>(FileBackupCallback), &context);

            if (callback) {
                callback(20, L"Starting file capture...");
            }

            // Capture the source into WIM
            HANDLE hImage = WIMCaptureImage(hWim, capturePath.c_str(), 0);

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

            std::wstring backupStartTimestamp = ResolveBackupStartTimestamp();
            std::wstring metadataPayload = BuildFileBackupMetadataPayload(backupStartTimestamp);
            std::wstring imageDescription = BackupEngine::Common::GetCurrentJobName().empty()
                ? imageName
                : BackupEngine::Common::GetCurrentJobName();
            std::wstring sanitizedImageName = SanitizeXmlName(imageName);
            wchar_t tempPath[MAX_PATH] = {};
            if (GetTempPathW(MAX_PATH, tempPath)) {
                WIMSetTemporaryPath(hWim, tempPath);
            }

            int imageIndex = WIMGetImageCount(hWim);
            HANDLE hImageForMetadata = imageIndex > 0 ? WIMLoadImage(hWim, imageIndex) : INVALID_HANDLE_VALUE;
            if (!hImageForMetadata || hImageForMetadata == INVALID_HANDLE_VALUE) {
                DWORD error = ::GetLastError();
                if (hImage != INVALID_HANDLE_VALUE) {
                    WIMCloseHandle(hImage);
                }
                WIMCloseHandle(hWim);

                std::wstring errorMsg = L"Failed to load captured image for metadata. Error: " + std::to_wstring(error);
                SetLastErrorMessage(errorMsg);
                if (logCallback) logCallback(3, L"Failed to load captured image for metadata", errorMsg.c_str());
                return -5;
            }

            std::wstring imageXml;
            wchar_t* existingXmlInfo = nullptr;
            DWORD existingXmlSize = 0;

            if (WIMGetImageInformation(hImageForMetadata, reinterpret_cast<LPVOID*>(&existingXmlInfo), &existingXmlSize) &&
                existingXmlInfo != nullptr && existingXmlSize >= sizeof(wchar_t)) {
                imageXml.assign(existingXmlInfo);
                imageXml = UpsertImageXmlElement(imageXml, L"NAME", sanitizedImageName);
                imageXml = UpsertImageXmlElement(imageXml, L"DESCRIPTION", SanitizeXmlName(imageDescription));
                imageXml = UpsertImageXmlElement(imageXml, L"BACKUPRESTOREMETADATA", metadataPayload);
                LocalFree(existingXmlInfo);
                existingXmlInfo = nullptr;
            }
            else {
                if (existingXmlInfo != nullptr) {
                    LocalFree(existingXmlInfo);
                    existingXmlInfo = nullptr;
                }

                imageXml = L"<IMAGE><NAME>" + sanitizedImageName + L"</NAME><DESCRIPTION>";
                imageXml += SanitizeXmlName(imageDescription);
                imageXml += L"</DESCRIPTION>";
                imageXml += L"<BACKUPRESTOREMETADATA>" + metadataPayload + L"</BACKUPRESTOREMETADATA>";
                imageXml += L"</IMAGE>";
            }

            DWORD xmlSize = GetUnicodeXmlBufferSize(imageXml);

            if (!WIMSetImageInformation(hImageForMetadata, const_cast<wchar_t*>(imageXml.c_str()), xmlSize)) {
                DWORD error = ::GetLastError();
                if (hImageForMetadata != INVALID_HANDLE_VALUE) {
                    WIMCloseHandle(hImageForMetadata);
                }
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

            if (hImageForMetadata != INVALID_HANDLE_VALUE) {
                WIMCloseHandle(hImageForMetadata);
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

    BACKUPENGINE_API int BackupFiles(
        const wchar_t* sourcePath,
        const wchar_t* destPath,
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback,
        LogCallback logCallback) {
        return BackupFilesCore(
            sourcePath,
            destPath,
            nullptr,
            0,
            userExclusions,
            userExclusionCount,
            callback,
            logCallback);
    }

    BACKUPENGINE_API int BackupFilesBySelections(
        const wchar_t* sourceRoot,
        const wchar_t* destPath,
        const wchar_t** includePaths,
        int includePathCount,
        const wchar_t** userExclusions,
        int userExclusionCount,
        ProgressCallback callback,
        LogCallback logCallback) {
        return BackupFilesCore(
            sourceRoot,
            destPath,
            includePaths,
            includePathCount,
            userExclusions,
            userExclusionCount,
            callback,
            logCallback);
    }
}


