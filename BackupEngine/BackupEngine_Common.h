// BackupEngine_Common.h - Shared utilities for backup operations
#pragma once

#include <windows.h>
#include <string>
#include <vector>
#include <algorithm>

namespace BackupEngine {
namespace Common {

    // ============================================================================
    // WILDCARD PATTERN MATCHING
    // Supports patterns like *.tmp, *.log, D:\Build\*.dll
    // ============================================================================
    bool MatchWildcard(const std::wstring& path, const std::wstring& pattern);

    // ============================================================================
    // PATH EXCLUSION CHECKING
    // Two-tier exclusion system: System (hardcoded) + User (configurable)
    // ============================================================================
    
    // Check if a path should be excluded from backup
    // Returns true if path matches any system or user exclusion rule
    bool IsPathExcluded(
        const std::wstring& path,
        const wchar_t** userExclusions,
        int userExclusionCount
    );

    // ============================================================================
    // SYSTEM EXCLUSIONS (always applied)
    // These protected folders/files cause backup failures if included
    // ============================================================================
    const std::vector<std::wstring> SYSTEM_EXCLUSIONS = {
        L"system volume information",  // VSS metadata, inaccessible
        L"$recycle.bin",                // Recycle bin, not needed for restore
        L"pagefile.sys",                // Virtual memory file, locked by OS
        L"swapfile.sys",                // Swap file for Windows apps, locked by OS
        L"hiberfil.sys"                 // Hibernation file, locked by OS
    };

    // ============================================================================
    // WIM CALLBACK HELPERS
    // Common utilities for WIM API callback functions
    // ============================================================================

    // Progress callback function pointer type
    typedef void(__cdecl* ProgressCallback)(int percentage, const wchar_t* message);

    // Base context structure for WIM callbacks with progress reporting
    struct WimCallbackContext {
        ProgressCallback userCallback;
        int filesProcessed;
        DWORD64 totalSize;
        DWORD64 processedSize;
        int currentPercentage;

        WimCallbackContext(ProgressCallback callback = nullptr)
            : userCallback(callback), filesProcessed(0), totalSize(0), processedSize(0), currentPercentage(0) {}
    };

    // Report progress through callback (scales percentage to range)
    void ReportProgress(
        ProgressCallback callback,
        int percentage,
        const std::wstring& message
    );

    // Initialize WIM callback context
    void InitializeWimContext(WimCallbackContext& context, ProgressCallback callback);

    // Update WIM progress tracking
    void UpdateWimProgress(WimCallbackContext& context, DWORD processed, DWORD total);

    // Report file being processed
    void ReportFileProgress(
        ProgressCallback callback,
        const wchar_t* filePath,
        int& filesProcessed,
        int reportInterval = 50
    );

} // namespace Common
} // namespace BackupEngine
