#pragma once
//DiskConsolidator.h
#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <virtdisk.h>
#include <wimgapi.h>
#include <string>
#include <vector>
#include <functional>
#include <atomic>

#pragma comment(lib, "virtdisk.lib")
#pragma comment(lib, "wimgapi.lib")

// ── Disk chain entry used internally ─────────────────────────────────────────
struct DiskChainEntry
{
    std::wstring path;
    std::wstring parentPath;
    int          depth;        // 0 = base VHDX, 1+ = AVHDX levels
    uint64_t     sizeBytes;
    bool         isBase;
};

// Forward declare result enum from BackupEngine.h
enum HVE_RESULT : int32_t;
enum HVE_EVENT : int32_t;

using ProgressFn = std::function<void(int32_t, const wchar_t*)>;
using StatusFn = std::function<void(HVE_EVENT, const wchar_t*)>;

class DiskConsolidator
{
public:
    DiskConsolidator(
        std::atomic<bool>* cancelFlag,
        ProgressFn         progressFn,
        StatusFn           statusFn);

    ~DiskConsolidator() = default;

    // ── Primary operations ────────────────────────────────────────────────────

    // Walk the export staging path, find all AVHDX files, merge them
    // back into the base VHDX using MergeVirtualDisk.
    HVE_RESULT ConsolidateChain(const std::wstring& exportStagingPath);

    // Consolidate an AVHDX chain and write the final VHDX to a specific path.
    HVE_RESULT ConsolidateToSingleVHDX(
        const std::wstring& exportStagingPath,
        const std::wstring& outputVhdxPath);

    // Clone a source VHDX (and its AVHDX chain) to a new standalone VHDX.
    HVE_RESULT CloneDisk(
        const std::wstring& sourceVhdxPath,
        const std::wstring& destinationPath,
        bool                consolidateChain,
        bool                compactAfterMerge);

    // ── Query ─────────────────────────────────────────────────────────────────

    // Returns the full parent→leaf chain for any VHDX/AVHDX.
    // Index 0 is always the base VHDX.
    std::vector<DiskChainEntry> GetDiskChain(const std::wstring& leafVhdxPath);

    const std::wstring& GetLastError() const { return _lastError; }

private:
    // ── VirtDisk helpers ──────────────────────────────────────────────────────

    // Open a VHDX/AVHDX with the VirtDisk API.
    // readOnly=true uses VIRTUAL_DISK_ACCESS_NONE + GET_INFO flag.
    HANDLE OpenVhdx(const std::wstring& path, bool readOnly);

    // Walk the parent locator chain starting from leafPath.
    // Returns entries ordered base→leaf (index 0 = base).
    std::vector<std::wstring> WalkParentChain(const std::wstring& leafPath);

    // Read the parent path stored inside a VHDX/AVHDX via GET_VIRTUAL_DISK_INFO.
    std::wstring ReadParentPath(const std::wstring& vhdxPath);

    // Merge a single child disk into its parent using MergeVirtualDisk.
    // After success, childPath is no longer needed.
    HVE_RESULT  MergeChildIntoParent(
        const std::wstring& childPath,
        int                 progressBase,
        int                 progressSlice);

    // Compact a VHDX in-place using COMPACT_VIRTUAL_DISK.
    HVE_RESULT  CompactVhdx(const std::wstring& vhdxPath);

    // Copy a VHDX file to a new path using CopyFileW with progress.
    HVE_RESULT  CopyVhdxFile(
        const std::wstring& srcPath,
        const std::wstring& dstPath);

    // ── WIM helpers (wimgapi) ─────────────────────────────────────────────────

    // Capture a directory tree or mounted VHDX into a WIM image.
    HVE_RESULT  CaptureToWim(
        const std::wstring& sourcePath,
        const std::wstring& wimPath,
        const std::wstring& imageName);

    // Apply a WIM image back to a directory or mounted VHDX.
    HVE_RESULT  ApplyFromWim(
        const std::wstring& wimPath,
        const std::wstring& destPath,
        uint32_t            imageIndex);

    // WIM progress callback trampoline — wimgapi calls this as a FARPROC.
    static DWORD WINAPI WimProgressCallback(
        DWORD  msgId,
        WPARAM wParam,
        LPARAM lParam,
        void* userData);

    // ── Utility ───────────────────────────────────────────────────────────────

    uint64_t    GetFileSize64(const std::wstring& path);
    bool        FileExists(const std::wstring& path);
    void        SetError(const std::wstring& msg);
    void        ReportProgress(int32_t pct, const wchar_t* msg);

    std::atomic<bool>* _cancelFlag;
    ProgressFn         _progressFn;
    StatusFn           _statusFn;
    std::wstring       _lastError;
};