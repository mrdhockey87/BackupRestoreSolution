// BackupEngine_Common.cpp - Shared utilities for backup operations
// Part of BackupRestoreSolution v6.1.3.5
// Purpose: Eliminate code duplication across backup implementations
// Contains: MatchWildcard, IsPathExcluded, progress helpers

#include "BackupEngine_Common.h"
#include <algorithm>
#include <cctype>

namespace BackupEngine {
namespace Common {

// ========================================================================
// WILDCARD PATTERN MATCHING
// ========================================================================

bool MatchWildcard(const std::wstring& path, const std::wstring& pattern) {
    // Handles patterns like:
    // - *.tmp (suffix match)
    // - D:\Build\*.dll (prefix + suffix)
    // - C:\Windows\Logs (exact match)
    
    std::wstring pathLower = path;
    std::wstring patternLower = pattern;
    
    // Convert to lowercase for case-insensitive comparison
    std::transform(pathLower.begin(), pathLower.end(), pathLower.begin(), ::tolower);
    std::transform(patternLower.begin(), patternLower.end(), patternLower.begin(), ::tolower);

    // Check if pattern contains wildcard
    size_t wildcardPos = patternLower.find(L'*');
    
    if (wildcardPos == std::wstring::npos) {
        // No wildcard - exact match (case-insensitive)
        return pathLower == patternLower;
    }

    // Pattern has wildcard - split into prefix and suffix
    std::wstring prefix = patternLower.substr(0, wildcardPos);
    std::wstring suffix = patternLower.substr(wildcardPos + 1);

    // Check if path matches prefix and suffix
    bool prefixMatch = prefix.empty() || pathLower.find(prefix) == 0;
    bool suffixMatch = suffix.empty() || 
                      (pathLower.length() >= suffix.length() && 
                       pathLower.substr(pathLower.length() - suffix.length()) == suffix);

    return prefixMatch && suffixMatch;
}

// ========================================================================
// TWO-TIER EXCLUSION CHECKING
// ========================================================================

bool IsPathExcluded(const std::wstring& path, const wchar_t** userExclusions, int userExclusionCount) {
    // TIER 1: System exclusions (always applied, cannot be disabled)
    std::wstring pathLower = path;
    std::transform(pathLower.begin(), pathLower.end(), pathLower.begin(), ::tolower);

    // Check system exclusions (hardcoded protected paths/files)
    for (const auto& systemExclusion : SYSTEM_EXCLUSIONS) {
        if (pathLower.find(systemExclusion) != std::wstring::npos) {
            // Log exclusion for diagnostics
            OutputDebugStringW((L"[Common] SYSTEM EXCLUSION: " + path + L" (matches: " + systemExclusion + L")\n").c_str());
            return true;
        }
    }

    // TIER 2: User exclusions (configured via UI)
    if (userExclusions != nullptr && userExclusionCount > 0) {
        for (int i = 0; i < userExclusionCount; i++) {
            if (userExclusions[i] != nullptr) {
                std::wstring exclusionPattern(userExclusions[i]);
                
                if (MatchWildcard(path, exclusionPattern)) {
                    // Log user exclusion for diagnostics
                    OutputDebugStringW((L"[Common] USER EXCLUSION: " + path + L" (pattern: " + exclusionPattern + L")\n").c_str());
                    return true;
                }
            }
        }
    }

    return false; // Path not excluded
}

// ========================================================================
// PROGRESS REPORTING HELPERS
// ========================================================================

void ReportProgress(ProgressCallback callback, int percentage, const std::wstring& message) {
    if (callback != nullptr) {
        // Ensure percentage is in valid range
        if (percentage < 0) percentage = 0;
        if (percentage > 100) percentage = 100;
        
        callback(percentage, message.c_str());
    }
}

void ReportFileProgress(ProgressCallback callback, const std::wstring& fileName, int percentage) {
    if (callback != nullptr) {
        std::wstring message = L"Processing: " + fileName;
        
        // Ensure percentage is in valid range
        if (percentage < 0) percentage = 0;
        if (percentage > 100) percentage = 100;
        
        callback(percentage, message.c_str());
    }
}

// ========================================================================
// WIM CALLBACK CONTEXT HELPERS
// ========================================================================

void InitializeWimContext(WimCallbackContext& context, ProgressCallback callback) {
    context.userCallback = callback;
    context.totalSize = 0;
    context.processedSize = 0;
    context.currentPercentage = 0;
}

void UpdateWimProgress(WimCallbackContext& context, DWORD processed, DWORD total) {
    if (total > 0) {
        context.processedSize = processed;
        context.totalSize = total;
        
        // Calculate percentage (avoiding integer overflow)
        int newPercentage = static_cast<int>((static_cast<DWORD64>(processed) * 100) / total);
        
        // Only update if percentage changed (avoid spam)
        if (newPercentage != context.currentPercentage) {
            context.currentPercentage = newPercentage;
            
            // Report progress with current percentage
            std::wstring message = L"Processing... " + std::to_wstring(newPercentage) + L"%";
            ReportProgress(context.userCallback, newPercentage, message);
        }
    }
}

// ========================================================================
// DIAGNOSTIC LOGGING HELPERS
// ========================================================================

void LogExclusionSummary(int systemExclusionCount, int userExclusionCount) {
    std::wstring summary = L"[Common] Exclusion system initialized:\n";
    summary += L"  System exclusions: " + std::to_wstring(systemExclusionCount) + L" (always applied)\n";
    summary += L"  User exclusions: " + std::to_wstring(userExclusionCount) + L" (configured via UI)\n";
    
    OutputDebugStringW(summary.c_str());
}

void LogSystemExclusions() {
    OutputDebugStringW(L"[Common] System exclusions:\n");
    for (const auto& exclusion : SYSTEM_EXCLUSIONS) {
        OutputDebugStringW((L"  - " + exclusion + L"\n").c_str());
    }
}

} // namespace Common
} // namespace BackupEngine
