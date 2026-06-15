//DiskConsolidator.cpp
#include "DiskConsolidator.h"
#include "BackupEngine.h"
#include <filesystem>
#include <algorithm>
#include <chrono>
#include <sstream>
#include <initguid.h> // Must be included before windows.h / virtdisk.h
#include <windows.h>
#include <virtdisk.h>

namespace fs = std::filesystem;

static const GUID kVirtualStorageTypeVendorMicrosoft =
{ 0xEC984AEC, 0xA0F9, 0x47E9, { 0x90, 0x1F, 0x71, 0x41, 0x5A, 0x66, 0x34, 0x5B } };

// ── Constructor ───────────────────────────────────────────────────────────────

DiskConsolidator::DiskConsolidator(
    std::atomic<bool>* cancelFlag,
    ProgressFn progressFn, StatusFn statusFn)
    : _cancelFlag(cancelFlag)
    , _progressFn(progressFn)
    , _statusFn(statusFn)
{}

// ── ConsolidateChain ──────────────────────────────────────────────────────────

HVE_RESULT DiskConsolidator::ConsolidateChain(
    const std::wstring& exportStagingPath)
{
    // Find the Virtual Hard Disks subfolder
    std::wstring diskDir = exportStagingPath + L"\\Virtual Hard Disks";

    if (!fs::exists(diskDir))
    {
        SetError(L"Virtual Hard Disks directory not found in export path: "
            + exportStagingPath);
        return HVE_ERR_INVALID_ARG;
    }

    // Collect all AVHDX files — these are the differencing disks to merge
    std::vector<std::wstring> avhdxFiles;
    for (const auto& entry : fs::directory_iterator(diskDir))
    {
        if (entry.path().extension() == L".avhdx")
            avhdxFiles.push_back(entry.path().wstring());
    }

    if (avhdxFiles.empty())
    {
        ReportProgress(100, L"No AVHDX files found — disk already consolidated.");
        return HVE_OK;
    }

    // Find the leaf (most recently modified AVHDX) and walk its chain
    // to determine merge order
    std::wstring leafPath = *std::max_element(
        avhdxFiles.begin(), avhdxFiles.end(),
        [](const std::wstring& a, const std::wstring& b)
        {
            WIN32_FILE_ATTRIBUTE_DATA fa{}, fb{};
            GetFileAttributesExW(a.c_str(), GetFileExInfoStandard, &fa);
            GetFileAttributesExW(b.c_str(), GetFileExInfoStandard, &fb);
            return CompareFileTime(&fa.ftLastWriteTime,
                &fb.ftLastWriteTime) < 0;
        });

    auto chain = WalkParentChain(leafPath);

    if (chain.size() < 2)
    {
        ReportProgress(100, L"Chain has only one disk — nothing to merge.");
        return HVE_OK;
    }

    // Merge leaf→parent, leaf-1→parent, ... until only base remains
    // chain[0]=base, chain[n-1]=leaf
    int totalMerges = (int)chain.size() - 1;

    for (int i = totalMerges; i >= 1; i--)
    {
        if (*_cancelFlag) return HVE_ERR_CANCELLED;

        int progressBase = (int)(((float)(totalMerges - i) / totalMerges) * 90.0f);
        int progressSlice = (int)((1.0f / totalMerges) * 90.0f);

        wchar_t msg[256];
        swprintf_s(msg, L"Merging disk %d of %d: %s",
            totalMerges - i + 1, totalMerges,
            fs::path(chain[i]).filename().wstring().c_str());
        ReportProgress(progressBase + 5, msg);

        HVE_RESULT r = MergeChildIntoParent(chain[i], progressBase, progressSlice);
        if (r != HVE_OK) return r;

        // Delete the now-merged AVHDX
        if (!DeleteFileW(chain[i].c_str()))
        {
            wchar_t delMsg[256];
            swprintf_s(delMsg, L"Warning: could not delete merged AVHDX: %s",
                chain[i].c_str());
            _statusFn(HVE_EVT_WARNING, delMsg);
        }
    }

    ReportProgress(100, L"AVHDX chain fully consolidated.");
    return HVE_OK;
}

// ── ConsolidateToSingleVHDX ───────────────────────────────────────────────────

HVE_RESULT DiskConsolidator::ConsolidateToSingleVHDX(
    const std::wstring& exportStagingPath,
    const std::wstring& outputVhdxPath)
{
    // First consolidate in place
    HVE_RESULT r = ConsolidateChain(exportStagingPath);
    if (r != HVE_OK) return r;
    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // Then copy the resulting base VHDX to the requested output path
    std::wstring diskDir = exportStagingPath + L"\\Virtual Hard Disks";
    std::wstring baseVhdx;

    for (const auto& entry : fs::directory_iterator(diskDir))
    {
        if (entry.path().extension() == L".vhdx")
        {
            baseVhdx = entry.path().wstring();
            break;
        }
    }

    if (baseVhdx.empty())
    {
        SetError(L"No base VHDX found after consolidation in: " + diskDir);
        return HVE_ERR_MERGE_FAILED;
    }

    ReportProgress(92, L"Copying consolidated VHDX to output path...");
    return CopyVhdxFile(baseVhdx, outputVhdxPath);
}

// ── CloneDisk ─────────────────────────────────────────────────────────────────

HVE_RESULT DiskConsolidator::CloneDisk(
    const std::wstring& sourceVhdxPath,
    const std::wstring& destinationPath,
    bool                consolidateChain,
    bool                compactAfterMerge)
{
    if (!FileExists(sourceVhdxPath))
    {
        SetError(L"Source VHDX not found: " + sourceVhdxPath);
        return HVE_ERR_INVALID_ARG;
    }

    ReportProgress(0, L"Opening source disk chain...");

    // Walk the source chain to find the base
    auto chain = WalkParentChain(sourceVhdxPath);
    std::wstring baseSource = chain.empty() ? sourceVhdxPath : chain[0];

    ReportProgress(5, L"Copying base VHDX...");

    // Copy base VHDX to destination first
    HVE_RESULT r = CopyVhdxFile(baseSource, destinationPath);
    if (r != HVE_OK) return r;
    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // If the source had a chain and consolidation is requested,
    // copy each AVHDX alongside the base and then merge them
    if (consolidateChain && chain.size() > 1)
    {
        std::wstring destDir = fs::path(destinationPath).parent_path().wstring();
        std::vector<std::wstring> copiedChain;
        copiedChain.push_back(destinationPath);

        for (size_t i = 1; i < chain.size(); i++)
        {
            if (*_cancelFlag) return HVE_ERR_CANCELLED;

            std::wstring avhdxDest = destDir + L"\\"
                + fs::path(chain[i]).filename().wstring();

            int pct = 5 + (int)((float)i / chain.size() * 40.0f);
            wchar_t msg[256];
            swprintf_s(msg, L"Copying differencing disk %zu of %zu...",
                i, chain.size() - 1);
            ReportProgress(pct, msg);

            r = CopyVhdxFile(chain[i], avhdxDest);
            if (r != HVE_OK) return r;

            copiedChain.push_back(avhdxDest);
        }

        // Now merge the copied chain into the copied base
        for (int i = (int)copiedChain.size() - 1; i >= 1; i--)
        {
            if (*_cancelFlag) return HVE_ERR_CANCELLED;

            int pct = 50 + (int)(((float)(copiedChain.size() - i)
                / copiedChain.size()) * 40.0f);
            wchar_t msg[256];
            swprintf_s(msg, L"Merging differencing disk %d of %d...",
                (int)copiedChain.size() - i,
                (int)copiedChain.size() - 1);
            ReportProgress(pct, msg);

            r = MergeChildIntoParent(copiedChain[i], pct, 5);
            if (r != HVE_OK) return r;

            DeleteFileW(copiedChain[i].c_str());
        }
    }

    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // Optional compact pass to reclaim freed space from merged blocks
    if (compactAfterMerge)
    {
        ReportProgress(92, L"Compacting consolidated disk...");
        r = CompactVhdx(destinationPath);
        if (r != HVE_OK)
        {
            // Compaction failure is non-fatal — warn and continue
            _statusFn(HVE_EVT_WARNING,
                L"Compaction failed but disk clone succeeded.");
        }
    }

    ReportProgress(100, L"Disk clone complete.");
    return HVE_OK;
}

// ── GetDiskChain ──────────────────────────────────────────────────────────────

std::vector<DiskChainEntry> DiskConsolidator::GetDiskChain(
    const std::wstring& leafVhdxPath)
{
    auto paths = WalkParentChain(leafVhdxPath);
    std::vector<DiskChainEntry> entries;

    for (int i = 0; i < (int)paths.size(); i++)
    {
        DiskChainEntry e{};
        e.path = paths[i];
        e.parentPath = (i > 0) ? paths[i - 1] : L"";
        e.depth = i;
        e.isBase = (i == 0);
        e.sizeBytes = GetFileSize64(paths[i]);
        entries.push_back(e);
    }

    return entries;
}

// ── MergeChildIntoParent ──────────────────────────────────────────────────────

HVE_RESULT DiskConsolidator::MergeChildIntoParent(
    const std::wstring& childPath,
    int                 progressBase,
    int                 progressSlice)
{
    VIRTUAL_STORAGE_TYPE storageType{};
    storageType.DeviceId = VIRTUAL_STORAGE_TYPE_DEVICE_VHDX;
    storageType.VendorId = kVirtualStorageTypeVendorMicrosoft;

    // Open the child AVHDX with read/write access for merging
    OPEN_VIRTUAL_DISK_PARAMETERS openParams{};
    openParams.Version = OPEN_VIRTUAL_DISK_VERSION_2;
    openParams.Version2.GetInfoOnly = FALSE;
    openParams.Version2.ReadOnly = FALSE;
    //openParams.Version2.OpenFlags = OPEN_VIRTUAL_DISK_FLAG_NONE;

    HANDLE hDisk = INVALID_HANDLE_VALUE;
    DWORD  err = OpenVirtualDisk(
        &storageType,
        childPath.c_str(),
        VIRTUAL_DISK_ACCESS_NONE,
        OPEN_VIRTUAL_DISK_FLAG_NONE,
        &openParams,
        &hDisk);

    if (err != ERROR_SUCCESS)
    {
        wchar_t msg[256];
        swprintf_s(msg,
            L"OpenVirtualDisk failed for merge source: 0x%08X path: %s",
            err, childPath.c_str());
        SetError(msg);
        return HVE_ERR_DISK_LOCKED;
    }

    // MERGE_VIRTUAL_DISK_VERSION_2 merges the child all the way to the
    // ultimate root parent (MergeDepth = 0xFFFFFFFF means "all the way up")
    MERGE_VIRTUAL_DISK_PARAMETERS mergeParams{};
    mergeParams.Version = MERGE_VIRTUAL_DISK_VERSION_2;
    mergeParams.Version2.MergeSourceDepth = 1;
    mergeParams.Version2.MergeTargetDepth = 0; // 0 = ultimate parent

    // Use overlapped I/O so we can poll progress without blocking
    OVERLAPPED ov{};
    ov.hEvent = CreateEvent(nullptr, TRUE, FALSE, nullptr);

    err = MergeVirtualDisk(
        hDisk,
        MERGE_VIRTUAL_DISK_FLAG_NONE,
        &mergeParams,
        &ov);

    // ERROR_IO_PENDING is the expected return for async VirtDisk ops
    if (err != ERROR_SUCCESS && err != ERROR_IO_PENDING)
    {
        CloseHandle(ov.hEvent);
        CloseHandle(hDisk);
        wchar_t msg[256];
        swprintf_s(msg, L"MergeVirtualDisk failed: 0x%08X", err);
        SetError(msg);
        return HVE_ERR_MERGE_FAILED;
    }

    // Poll until merge completes, reporting progress every second
    VIRTUAL_DISK_PROGRESS progress{};
    while (true)
    {
        if (*_cancelFlag)
        {
            CancelIoEx(hDisk, &ov);
            CloseHandle(ov.hEvent);
            CloseHandle(hDisk);
            return HVE_ERR_CANCELLED;
        }

        DWORD waitResult = WaitForSingleObject(ov.hEvent, 1000);

        // Get current progress regardless of wait result
        GetVirtualDiskOperationProgress(hDisk, &ov, &progress);

        if (progress.OperationStatus == ERROR_SUCCESS)
        {
            ReportProgress(progressBase + progressSlice, L"Merge complete.");
            break;
        }

        if (progress.OperationStatus != ERROR_IO_PENDING)
        {
            CloseHandle(ov.hEvent);
            CloseHandle(hDisk);
            wchar_t msg[256];
            swprintf_s(msg, L"Merge operation failed mid-flight: 0x%08X",
                progress.OperationStatus);
            SetError(msg);
            return HVE_ERR_MERGE_FAILED;
        }

        // Report intermediate progress
        if (progress.CompletionValue > 0 && progress.CurrentValue > 0)
        {
            float pct = (float)progress.CurrentValue
                / (float)progress.CompletionValue;
            int   reportPct = progressBase
                + (int)(pct * progressSlice);
            wchar_t msg[128];
            swprintf_s(msg, L"Merging... %.1f%%", pct * 100.0f);
            ReportProgress(reportPct, msg);
        }

        if (waitResult == WAIT_OBJECT_0) break;
    }

    CloseHandle(ov.hEvent);
    CloseHandle(hDisk);
    return HVE_OK;
}

// ── CompactVhdx ───────────────────────────────────────────────────────────────

HVE_RESULT DiskConsolidator::CompactVhdx(const std::wstring& vhdxPath)
{
    VIRTUAL_STORAGE_TYPE storageType{};
    storageType.DeviceId = VIRTUAL_STORAGE_TYPE_DEVICE_VHDX;
    storageType.VendorId = kVirtualStorageTypeVendorMicrosoft;

    OPEN_VIRTUAL_DISK_PARAMETERS openParams{};
    openParams.Version = OPEN_VIRTUAL_DISK_VERSION_2;
    openParams.Version2.GetInfoOnly = FALSE;
    openParams.Version2.ReadOnly = FALSE;

    HANDLE hDisk = INVALID_HANDLE_VALUE;
    DWORD  err = OpenVirtualDisk(
        &storageType, vhdxPath.c_str(),
        VIRTUAL_DISK_ACCESS_NONE,
        OPEN_VIRTUAL_DISK_FLAG_NONE,
        &openParams, &hDisk);

    if (err != ERROR_SUCCESS)
    {
        SetError(L"CompactVhdx: OpenVirtualDisk failed");
        return HVE_ERR_GENERAL;
    }

    COMPACT_VIRTUAL_DISK_PARAMETERS compactParams{};
    compactParams.Version = COMPACT_VIRTUAL_DISK_VERSION_1;

    OVERLAPPED ov{};
    ov.hEvent = CreateEvent(nullptr, TRUE, FALSE, nullptr);

    err = CompactVirtualDisk(
        hDisk,
        COMPACT_VIRTUAL_DISK_FLAG_NONE,
        &compactParams,
        &ov);

    if (err != ERROR_SUCCESS && err != ERROR_IO_PENDING)
    {
        CloseHandle(ov.hEvent);
        CloseHandle(hDisk);
        SetError(L"CompactVirtualDisk call failed");
        return HVE_ERR_GENERAL;
    }

    // Poll with progress
    VIRTUAL_DISK_PROGRESS progress{};
    while (true)
    {
        if (*_cancelFlag)
        {
            CancelIoEx(hDisk, &ov);
            break;
        }

        WaitForSingleObject(ov.hEvent, 1000);
        GetVirtualDiskOperationProgress(hDisk, &ov, &progress);

        if (progress.OperationStatus == ERROR_SUCCESS) break;
        if (progress.OperationStatus != ERROR_IO_PENDING) break;

        if (progress.CompletionValue > 0)
        {
            float pct = (float)progress.CurrentValue
                / (float)progress.CompletionValue;
            wchar_t msg[128];
            swprintf_s(msg, L"Compacting... %.1f%%", pct * 100.0f);
            ReportProgress(92 + (int)(pct * 7.0f), msg);
        }
    }

    CloseHandle(ov.hEvent);
    CloseHandle(hDisk);
    return HVE_OK;
}

// ── WalkParentChain ───────────────────────────────────────────────────────────

std::vector<std::wstring> DiskConsolidator::WalkParentChain(
    const std::wstring& leafPath)
{
    std::vector<std::wstring> chain;
    std::wstring current = leafPath;

    // Walk from leaf up to root — insert at front so [0] = base
    while (!current.empty() && FileExists(current))
    {
        chain.insert(chain.begin(), current);
        current = ReadParentPath(current);

        // Guard against malformed circular chains
        if (chain.size() > 64) break;
    }

    return chain;
}

// ── ReadParentPath ────────────────────────────────────────────────────────────

std::wstring DiskConsolidator::ReadParentPath(const std::wstring& vhdxPath)
{
    VIRTUAL_STORAGE_TYPE storageType{};
    storageType.DeviceId = VIRTUAL_STORAGE_TYPE_DEVICE_VHDX;
    storageType.VendorId = kVirtualStorageTypeVendorMicrosoft;

    OPEN_VIRTUAL_DISK_PARAMETERS openParams{};
    openParams.Version = OPEN_VIRTUAL_DISK_VERSION_2;
    openParams.Version2.GetInfoOnly = TRUE;
    openParams.Version2.ReadOnly = TRUE;

    HANDLE hDisk = INVALID_HANDLE_VALUE;
    DWORD  err = OpenVirtualDisk(
        &storageType, vhdxPath.c_str(),
        VIRTUAL_DISK_ACCESS_NONE,
        OPEN_VIRTUAL_DISK_FLAG_NONE,
        &openParams, &hDisk);

    if (err != ERROR_SUCCESS || hDisk == INVALID_HANDLE_VALUE)
        return {};

    // GET_VIRTUAL_DISK_INFO_PARENT_LOCATION returns the parent locator
    // ParentLocationBuffer is a double-null terminated list of paths
    GET_VIRTUAL_DISK_INFO info{};
    info.Version = GET_VIRTUAL_DISK_INFO_PARENT_LOCATION;
    DWORD sz = sizeof(info);
    DWORD used = 0;

    err = GetVirtualDiskInformation(hDisk, &sz, &info, &used);
    CloseHandle(hDisk);

    if (err != ERROR_SUCCESS || !info.ParentLocation.ParentResolved)
        return {};

    // First path in the double-null list is the primary locator
    return std::wstring(info.ParentLocation.ParentLocationBuffer);
}

// ── CopyVhdxFile ──────────────────────────────────────────────────────────────

HVE_RESULT DiskConsolidator::CopyVhdxFile(
    const std::wstring& srcPath,
    const std::wstring& dstPath)
{
    // Ensure destination directory exists
    fs::path dstDir = fs::path(dstPath).parent_path();
    if (!dstDir.empty())
        fs::create_directories(dstDir);

    // CopyFileExW with a progress callback so we can report to C#
    struct CopyState
    {
        DiskConsolidator* self;
        int               progressBase;
    } state{ this, 0 };

    BOOL cancelled = FALSE;
    BOOL ok = CopyFileExW(
        srcPath.c_str(),
        dstPath.c_str(),
        // LPPROGRESS_ROUTINE lambda via trampoline
        [](LARGE_INTEGER totalSize, LARGE_INTEGER transferred,
            LARGE_INTEGER, LARGE_INTEGER,
            DWORD, DWORD, HANDLE, HANDLE, LPVOID data) -> DWORD
        {
            auto* s = reinterpret_cast<CopyState*>(data);
            if (*s->self->_cancelFlag)
                return PROGRESS_CANCEL;

            if (totalSize.QuadPart > 0)
            {
                float pct = (float)transferred.QuadPart
                    / (float)totalSize.QuadPart;
                wchar_t msg[128];
                swprintf_s(msg, L"Copying VHDX... %.1f%%", pct * 100.0f);
                s->self->ReportProgress(
                    s->progressBase + (int)(pct * 30.0f), msg);
            }
            return PROGRESS_CONTINUE;
        },
        &state,
        &cancelled,
        0);

    if (!ok)
    {
        wchar_t msg[512];
        swprintf_s(msg, L"CopyFileExW failed 0x%s: %s → %s",
            GetLastError().c_str(), srcPath.c_str(), dstPath.c_str());
        SetError(msg);
        return HVE_ERR_GENERAL;
    }

    if (cancelled) return HVE_ERR_CANCELLED;
    return HVE_OK;
}

// ── WIM helpers ───────────────────────────────────────────────────────────────
HVE_RESULT DiskConsolidator::CaptureToWim(
    const std::wstring& sourcePath,
    const std::wstring& wimPath,
    const std::wstring& imageName)
{
    DWORD creationResult = 0;
    HANDLE hWim = WIMCreateFile(
        const_cast<PWSTR>(wimPath.c_str()),
        WIM_GENERIC_WRITE,          // dwDesiredAccess
        WIM_CREATE_ALWAYS,          // dwCreationDisposition
        WIM_FLAG_VERIFY,            // dwFlagsAndAttributes — WIM_FLAG_* only
        WIM_COMPRESS_LZX,           // dwCompressionType   — WIM_COMPRESS_* only
        &creationResult);

    if (hWim == INVALID_HANDLE_VALUE)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"WIMCreateFile failed: 0x%s", GetLastError().c_str());
        SetError(msg);
        return HVE_ERR_WIM_OPEN;
    }

    WIMRegisterMessageCallback(hWim, (FARPROC)WimProgressCallback, this);

    HANDLE hImage = WIMCaptureImage(
        hWim,
        const_cast<PWSTR>(sourcePath.c_str()),
        WIM_FLAG_VERIFY);

    WIMUnregisterMessageCallback(hWim, (FARPROC)WimProgressCallback);
    WIMCloseHandle(hWim);

    if (hImage == INVALID_HANDLE_VALUE)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"WIMCaptureImage failed: 0x%s", GetLastError().c_str());
        SetError(msg);
        return HVE_ERR_WIM_APPLY;
    }

    WIMCloseHandle(hImage);
    return HVE_OK;
}
/* Fixed code above Original code - WIM_FLAG_COMPRESS_LZX was not correct mdail 6/15/2026
HVE_RESULT DiskConsolidator::CaptureToWim(
    const std::wstring& sourcePath,
    const std::wstring& wimPath,
    const std::wstring& imageName)
{
    DWORD creationResult = 0;
    HANDLE hWim = WIMCreateFile(
        wimPath.c_str(),
        WIM_GENERIC_WRITE,
        WIM_CREATE_ALWAYS,
        WIM_FLAG_COMPRESS_LZX,   // LZX compression — good size/speed balance
        WIM_COMPRESS_LZX,
        &creationResult);

    if (hWim == INVALID_HANDLE_VALUE)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"WIMCreateFile failed: 0x%08X", GetLastError());
        SetError(msg);
        return HVE_ERR_WIM_OPEN;
    }

    // Register our progress trampoline
    WIMRegisterMessageCallback(hWim, (FARPROC)WimProgressCallback, this);

    HANDLE hImage = WIMCaptureImage(hWim, sourcePath.c_str(), WIM_FLAG_VERIFY);

    WIMUnregisterMessageCallback(hWim, (FARPROC)WimProgressCallback);
    WIMCloseHandle(hWim);

    if (hImage == INVALID_HANDLE_VALUE)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"WIMCaptureImage failed: 0x%08X", GetLastError());
        SetError(msg);
        return HVE_ERR_WIM_APPLY;
    }

    WIMCloseHandle(hImage);
    return HVE_OK;
}
*/
HVE_RESULT DiskConsolidator::ApplyFromWim(
    const std::wstring& wimPath,
    const std::wstring& destPath,
    uint32_t            imageIndex)
{
    DWORD creationResult = 0;
    HANDLE hWim = WIMCreateFile(
        wimPath.c_str(),
        WIM_GENERIC_READ,
        WIM_OPEN_EXISTING,
        WIM_FLAG_VERIFY,
        0,
        &creationResult);

    if (hWim == INVALID_HANDLE_VALUE)
    {
        SetError(L"ApplyFromWim: WIMCreateFile failed");
        return HVE_ERR_WIM_OPEN;
    }

    WIMSetTemporaryPath(hWim, fs::temp_directory_path().wstring().c_str());
    WIMRegisterMessageCallback(hWim, (FARPROC)WimProgressCallback, this);

    HANDLE hImage = WIMLoadImage(hWim, imageIndex);
    if (hImage == INVALID_HANDLE_VALUE)
    {
        WIMCloseHandle(hWim);
        SetError(L"ApplyFromWim: WIMLoadImage failed");
        return HVE_ERR_WIM_OPEN;
    }

    BOOL ok = WIMApplyImage(hImage, destPath.c_str(), WIM_FLAG_VERIFY);

    WIMUnregisterMessageCallback(hWim, (FARPROC)WimProgressCallback);
    WIMCloseHandle(hImage);
    WIMCloseHandle(hWim);

    if (!ok)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"WIMApplyImage failed: 0x%s", GetLastError().c_str());
        SetError(msg);
        return HVE_ERR_WIM_APPLY;
    }

    return HVE_OK;
}

// wimgapi message callback — fires for WIM_MSG_PROGRESS, WIM_MSG_PROCESS, etc.
DWORD WINAPI DiskConsolidator::WimProgressCallback(
    DWORD  msgId,
    WPARAM wParam,
    LPARAM lParam,
    void* userData)
{
    auto* self = reinterpret_cast<DiskConsolidator*>(userData);

    if (self->_cancelFlag && *self->_cancelFlag)
        return WIM_MSG_ABORT_IMAGE;

    if (msgId == WIM_MSG_PROGRESS)
    {
        // wParam = percentage complete
        wchar_t msg[64];
        swprintf_s(msg, L"WIM operation: %u%%", (UINT)wParam);
        self->ReportProgress((int)wParam, msg);
    }

    return WIM_MSG_SUCCESS;
}

// ── Utility ───────────────────────────────────────────────────────────────────

uint64_t DiskConsolidator::GetFileSize64(const std::wstring& path)
{
    WIN32_FILE_ATTRIBUTE_DATA attr{};
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &attr))
        return 0;
    return ((uint64_t)attr.nFileSizeHigh << 32) | attr.nFileSizeLow;
}

bool DiskConsolidator::FileExists(const std::wstring& path)
{
    return GetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES;
}

void DiskConsolidator::SetError(const std::wstring& msg)
{
    _lastError = msg;
    _statusFn(HVE_EVT_ERROR, msg.c_str());
}

void DiskConsolidator::ReportProgress(int32_t pct, const wchar_t* msg)
{
    _progressFn(pct, msg);
}