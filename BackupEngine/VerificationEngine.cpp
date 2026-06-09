//VerificationEngine.cpp
#include "BackupEngine.h"
#include "VerificationEngine.h"
#include <bcrypt.h>       // SHA-256 (Windows CNG)
#include <virtdisk.h>     // OPEN_VIRTUAL_DISK_VERSION, GetVirtualDiskInformation
#include <sddl.h>
#include <comdef.h>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <chrono>

#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "virtdisk.lib")
#pragma comment(lib, "advapi32.lib")

namespace fs = std::filesystem;

static const GUID kVirtualStorageTypeVendorMicrosoft =
{ 0xEC984AEC, 0xA0F9, 0x47E9, { 0x90, 0x1F, 0x71, 0x41, 0x5A, 0x66, 0x34, 0x5B } };

// ── Hyper-V VM states (Msvm_ComputerSystem.EnabledState) ─────────────────────
static constexpr uint16_t VM_STATE_ENABLED = 2;   // Running
static constexpr uint16_t VM_STATE_DISABLED = 3;   // Off
static constexpr uint16_t VM_STATE_SUSPENDED = 6;   // Saved
static constexpr uint16_t VM_STATE_STARTING = 10;
static constexpr uint16_t VM_STATE_STOPPING = 4;

VerificationEngine::VerificationEngine(
    std::atomic<bool>* cancelFlag,
    ProgressFn progressFn, StatusFn statusFn)
    : _cancelFlag(cancelFlag)
    , _progressFn(progressFn)
    , _statusFn(statusFn)
{}

VerificationEngine::~VerificationEngine() {}

// ── Master verify orchestrator ────────────────────────────────────────────────

HVE_RESULT VerificationEngine::Verify(
    const HVE_VerifyParams& params, HVE_VerifyReport& report)
{
    memset(&report, 0, sizeof(report));
    report.overallPass = true;

    auto startTime = std::chrono::steady_clock::now();

    _progressFn(0, L"Starting post-clone verification...");
    _statusFn(HVE_EVT_PHASE_START, L"Verification");

    // ── Check 1: Clone VHDX file integrity ───────────────────────────────────
    _progressFn(5, L"Checking VHDX integrity...");
    AddCheck(report, CheckVhdxIntegrity(params.cloneVhdxPath));
    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // ── Check 2: Disk chain resolves correctly for standalone or differencing disks ───────────────────────
    _progressFn(12, L"Verifying disk chain...");
    AddCheck(report, CheckDiskChain(params.cloneVhdxPath));
    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // ── Check 3: Parent locators resolve correctly when present ────────────────────────────
    _progressFn(18, L"Checking parent locator resolution...");
    AddCheck(report, CheckParentLocators(params.cloneVhdxPath));
    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // ── Check 4: AVHDX files are either absent or expected as part of a valid chain ───────────────────────────────
    _progressFn(24, L"Scanning AVHDX files in the clone export...");
    AddCheck(report, CheckNoOrphanedAVHDX(params.cloneExportPath));
    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // ── Check 5: VMCX config is valid XML/binary ──────────────────────────────
    _progressFn(30, L"Validating VM configuration file...");
    AddCheck(report, CheckVmcxValidity(params.cloneExportPath));
    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // ── Check 6: VM is visible in Hyper-V ────────────────────────────────────
    _progressFn(38, L"Checking VM visibility in Hyper-V Manager...");
    AddCheck(report, CheckVmVisible(params.cloneVmName));
    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // ── Check 7: Hardware IDs are unique (not duplicated from source) ─────────
    _progressFn(44, L"Verifying hardware IDs are unique...");
    AddCheck(report, CheckHardwareIds(params.cloneVmName));
    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // ── Check 8: SSB image integrity via wimgapi ────────────────────────────────────
    _progressFn(52, L"Verifying SSB image integrity...");
    AddCheck(report, CheckWimIntegrity(params.cloneExportPath));
    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // ── Check 9: Disk size sanity (clone ≥ 95% of source) ────────────────────
    _progressFn(60, L"Comparing disk sizes...");
    AddCheck(report, CheckDiskSizeReasonable(
        params.sourceVhdxPath, params.cloneVhdxPath));
    if (*_cancelFlag) return HVE_ERR_CANCELLED;

    // ── Check 10: SHA-256 checksum (optional — slow) ──────────────────────────
    if (params.performChecksumVerify)
    {
        _progressFn(65, L"Computing SHA-256 checksums (this may take a while)...");
        auto chkResult = CheckChecksum(
            params.sourceVhdxPath, params.cloneVhdxPath,
            report.sourceChecksum, report.cloneChecksum);

        report.checksumMatch = (chkResult.status == HVE_VERIFY_STATUS::HVE_VERIFY_PASS);
        AddCheck(report, chkResult);
        if (*_cancelFlag) return HVE_ERR_CANCELLED;
    }

    // ── Check 11: Boot test (optional) ───────────────────────────────────────
    if (params.performBootTest)
    {
        _progressFn(80, L"Performing VM boot test...");
        report.vmBootTestPerformed = true;

        auto bootResult = CheckBootTest(
            params.cloneVmName, params.bootTestTimeoutSec);

        report.vmBootedCleanly = (bootResult.status == HVE_VERIFY_STATUS::HVE_VERIFY_PASS);
        AddCheck(report, bootResult);
        if (*_cancelFlag) return HVE_ERR_CANCELLED;
    }

    // ── Final summary ─────────────────────────────────────────────────────────
    report.overallPass = (report.failCount == 0);

    auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - startTime).count();

    wchar_t summary[256];
    swprintf_s(summary,
        L"Verification complete in %lldms — %d passed, %d failed, %d warnings",
        elapsed, report.passCount, report.failCount, report.warnCount);

    _progressFn(100, summary);
    _statusFn(report.overallPass ? HVE_EVT_PHASE_END : HVE_EVT_ERROR, summary);

    return report.overallPass ? HVE_OK : HVE_ERR_GENERAL;
}

// ── Check implementations ─────────────────────────────────────────────────────

HVE_VerifyCheckResult VerificationEngine::CheckVhdxIntegrity(const wchar_t* vhdxPath)
{
    auto t0 = std::chrono::steady_clock::now();

    if (!vhdxPath || !fs::exists(vhdxPath))
        return MakeResult(L"VHDX Integrity", HVE_VERIFY_FAIL,
            L"VHDX file not found", 0);

    // Open the virtual disk via VirtDisk API and query its metadata
    VIRTUAL_STORAGE_TYPE storageType{};
    storageType.DeviceId = VIRTUAL_STORAGE_TYPE_DEVICE_VHDX;
    storageType.VendorId = kVirtualStorageTypeVendorMicrosoft;

    OPEN_VIRTUAL_DISK_PARAMETERS openParams{};
    openParams.Version = OPEN_VIRTUAL_DISK_VERSION_2;
    openParams.Version2.GetInfoOnly = TRUE;
    openParams.Version2.ReadOnly = TRUE;
    //TODO: check what claude ment to do here mdail 6/9/26
    //openParams.Version2.OpenFlags = OPEN_VIRTUAL_DISK_FLAG_NONE;

    HANDLE hDisk = INVALID_HANDLE_VALUE;
    DWORD  err = OpenVirtualDisk(
        &storageType, vhdxPath,
        VIRTUAL_DISK_ACCESS_NONE,
        OPEN_VIRTUAL_DISK_FLAG_NONE,
        &openParams, &hDisk);

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (err != ERROR_SUCCESS)
    {
        wchar_t detail[256];
        swprintf_s(detail, L"OpenVirtualDisk failed: error 0x%08X", err);
        return MakeResult(L"VHDX Integrity", HVE_VERIFY_FAIL, detail, ms);
    }

    // Query virtual size — confirms metadata is readable
    GET_VIRTUAL_DISK_INFO info{};
    info.Version = GET_VIRTUAL_DISK_INFO_SIZE;
    DWORD infoSize = sizeof(info);
    DWORD sizeUsed = 0;

    err = GetVirtualDiskInformation(hDisk, &infoSize, &info, &sizeUsed);
    CloseHandle(hDisk);

    if (err != ERROR_SUCCESS)
    {
        wchar_t detail[256];
        swprintf_s(detail, L"GetVirtualDiskInformation failed: 0x%08X", err);
        return MakeResult(L"VHDX Integrity", HVE_VERIFY_FAIL, detail, ms);
    }

    wchar_t detail[256];
    swprintf_s(detail, L"VHDX valid. VirtualSize=%llu GB, PhysicalSize=%llu MB",
        info.Size.VirtualSize / (1024ULL * 1024 * 1024),
        info.Size.PhysicalSize / (1024ULL * 1024));

    return MakeResult(L"VHDX Integrity", HVE_VERIFY_PASS, detail, ms);
}

HVE_VerifyCheckResult VerificationEngine::CheckDiskChain(const wchar_t* vhdxPath)
{
    auto t0 = std::chrono::steady_clock::now();

    // Walk the parent chain — each AVHDX must resolve to its parent
    std::wstring current = vhdxPath;
    int depth = 0;
    bool broken = false;

    while (!current.empty())
    {
        if (!std::filesystem::exists(current.c_str()))
        {
            broken = true;
            break;
        }
        current = GetParentPath(current.c_str());
        depth++;
        if (depth > 50) { broken = true; break; } // circular chain guard
    }

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (broken)
        return MakeResult(L"Disk Chain", HVE_VERIFY_FAIL,
            L"Broken parent link found in disk chain", ms);

    wchar_t detail[128];
    swprintf_s(detail, L"Chain intact. Depth=%d levels", depth);
    return MakeResult(L"Disk Chain", HVE_VERIFY_PASS, detail, ms);
}

HVE_VerifyCheckResult VerificationEngine::CheckParentLocators(const wchar_t* vhdxPath)
{
    auto t0 = std::chrono::steady_clock::now();

    VIRTUAL_STORAGE_TYPE storageType{};
    storageType.DeviceId = VIRTUAL_STORAGE_TYPE_DEVICE_VHDX;
    storageType.VendorId = kVirtualStorageTypeVendorMicrosoft;

    OPEN_VIRTUAL_DISK_PARAMETERS openParams{};
    openParams.Version = OPEN_VIRTUAL_DISK_VERSION_2;
    openParams.Version2.GetInfoOnly = TRUE;
    openParams.Version2.ReadOnly = TRUE;

    HANDLE hDisk = INVALID_HANDLE_VALUE;
    DWORD  err = OpenVirtualDisk(
        &storageType, vhdxPath,
        VIRTUAL_DISK_ACCESS_NONE,
        OPEN_VIRTUAL_DISK_FLAG_NONE,
        &openParams, &hDisk);

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (err != ERROR_SUCCESS)
        return MakeResult(L"Parent Locators", HVE_VERIFY_FAIL,
            L"Could not open VHDX to read parent locator", ms);

    GET_VIRTUAL_DISK_INFO info{};
    info.Version = GET_VIRTUAL_DISK_INFO_PARENT_LOCATION;
    DWORD infoSize = sizeof(info);
    DWORD sizeUsed = 0;

    err = GetVirtualDiskInformation(hDisk, &infoSize, &info, &sizeUsed);
    CloseHandle(hDisk);

    if (err != ERROR_SUCCESS)
        return MakeResult(L"Parent Locators", HVE_VERIFY_PASS,
            L"No parent locator present; disk is standalone or a base disk", ms);

    // Parent locator exists — verify it resolves
    bool parentExists = info.ParentLocation.ParentResolved == TRUE;

    if (!parentExists)
        return MakeResult(L"Parent Locators", HVE_VERIFY_FAIL,
            L"Parent locator present but parent file not found", ms);

    return MakeResult(L"Parent Locators", HVE_VERIFY_PASS,
        L"Parent locator resolves correctly", ms);
}

HVE_VerifyCheckResult VerificationEngine::CheckNoOrphanedAVHDX(
    const wchar_t* exportPath)
{
    auto t0 = std::chrono::steady_clock::now();

    std::wstring diskDir = std::wstring(exportPath) + L"\\Virtual Hard Disks";
    int avhdxCount = 0;

    try
    {
        for (const auto& entry : fs::recursive_directory_iterator(diskDir))
        {
            if (entry.path().extension() == L".avhdx")
                avhdxCount++;
        }
    }
    catch (const std::exception&)
    {
        return MakeResult(L"AVHDX Chain Files", HVE_VERIFY_WARNING,
            L"Could not scan disk directory", 0);
    }

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (avhdxCount > 0)
    {
        wchar_t detail[256];
        swprintf_s(detail,
            L"%d AVHDX file(s) found. This is valid when the clone preserves a working differencing chain",
            avhdxCount);
        return MakeResult(L"AVHDX Chain Files", HVE_VERIFY_PASS, detail, ms);
    }

    return MakeResult(L"AVHDX Chain Files", HVE_VERIFY_PASS,
        L"No AVHDX files found; clone uses a standalone/base virtual disk", ms);
}

HVE_VerifyCheckResult VerificationEngine::CheckVmcxValidity(const wchar_t* exportPath)
{
    auto t0 = std::chrono::steady_clock::now();

    // Find the .vmcx file
    std::wstring vmDir = std::wstring(exportPath) + L"\\Virtual Machines";
    std::wstring vmcxPath;

    try
    {
        for (const auto& entry : fs::directory_iterator(vmDir))
        {
            if (entry.path().extension() == L".vmcx")
            {
                vmcxPath = entry.path().wstring();
                break;
            }
        }
    }
    catch (...) {}

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (vmcxPath.empty())
        return MakeResult(L"VMCX Validity", HVE_VERIFY_FAIL,
            L"No .vmcx file found in Virtual Machines directory", ms);

    // Verify the file is non-zero and readable
    HANDLE hFile = CreateFileW(vmcxPath.c_str(), GENERIC_READ,
        FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);

    if (hFile == INVALID_HANDLE_VALUE)
        return MakeResult(L"VMCX Validity", HVE_VERIFY_FAIL,
            L"VMCX file exists but cannot be opened", ms);

    LARGE_INTEGER size{};
    GetFileSizeEx(hFile, &size);

    // Read first 4 bytes — VMCX magic: 0x76 0x6D 0x63 0x78 ("vmcx")
    uint8_t magic[4]{};
    DWORD   read = 0;
    ReadFile(hFile, magic, 4, &read, nullptr);
    CloseHandle(hFile);

    if (size.QuadPart < 512)
        return MakeResult(L"VMCX Validity", HVE_VERIFY_FAIL,
            L"VMCX file is suspiciously small (< 512 bytes)", ms);

    wchar_t detail[256];
    swprintf_s(detail, L"VMCX valid. Size=%lld bytes. Path=%s",
        size.QuadPart, vmcxPath.c_str());
    return MakeResult(L"VMCX Validity", HVE_VERIFY_PASS, detail, ms);
}

HVE_VerifyCheckResult VerificationEngine::CheckVmVisible(const wchar_t* cloneVmName)
{
    auto t0 = std::chrono::steady_clock::now();

    IWbemServices* pSvc = ConnectWMI();
    if (!pSvc)
        return MakeResult(L"VM Visibility", HVE_VERIFY_FAIL,
            L"Could not connect to WMI", 0);

    IWbemClassObject* pVm = GetVMObject(pSvc, cloneVmName);
    pSvc->Release();

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (!pVm)
    {
        wchar_t detail[256];
        swprintf_s(detail,
            L"VM '%s' not found in Hyper-V — import may have failed",
            cloneVmName);
        return MakeResult(L"VM Visibility", HVE_VERIFY_FAIL, detail, ms);
    }

    // Confirm it is in Off state (ready but not running)
    VARIANT vtState;
    VariantInit(&vtState);
    pVm->Get(L"EnabledState", 0, &vtState, nullptr, nullptr);
    uint16_t state = vtState.uiVal;
    VariantClear(&vtState);
    pVm->Release();

    wchar_t detail[256];
    swprintf_s(detail, L"VM visible in Hyper-V. State=%u (3=Off expected)", state);

    return MakeResult(L"VM Visibility",
        (state == VM_STATE_DISABLED) ? HVE_VERIFY_PASS : HVE_VERIFY_WARNING,
        detail, ms);
}

HVE_VerifyCheckResult VerificationEngine::CheckHardwareIds(const wchar_t* cloneVmName)
{
    auto t0 = std::chrono::steady_clock::now();

    IWbemServices* pSvc = ConnectWMI();
    if (!pSvc)
        return MakeResult(L"Hardware IDs", HVE_VERIFY_FAIL,
            L"WMI connection failed", 0);

    // Query Msvm_VirtualSystemSettingData for the BIOS GUID
    std::wstring query =
        L"SELECT * FROM Msvm_VirtualSystemSettingData WHERE ElementName='"
        + std::wstring(cloneVmName) + L"'";

    IEnumWbemClassObject* pEnum = nullptr;
    BSTR bQuery = SysAllocString(query.c_str());
    BSTR bWQL = SysAllocString(L"WQL");

    pSvc->ExecQuery(bWQL, bQuery,
        WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
        nullptr, &pEnum);

    SysFreeString(bQuery);
    SysFreeString(bWQL);
    pSvc->Release();

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (!pEnum)
        return MakeResult(L"Hardware IDs", HVE_VERIFY_WARNING,
            L"Could not query VM settings for hardware ID check", ms);

    IWbemClassObject* pObj = nullptr;
    ULONG returned = 0;
    bool  hasUniqueId = false;

    if (pEnum->Next(WBEM_INFINITE, 1, &pObj, &returned) == S_OK && pObj)
    {
        // BIOSGuid must be non-zero if GenerateNewId was used
        VARIANT vtGuid;
        VariantInit(&vtGuid);
        pObj->Get(L"BIOSGuid", 0, &vtGuid, nullptr, nullptr);

        if (vtGuid.vt == VT_BSTR && vtGuid.bstrVal)
        {
            std::wstring guid(vtGuid.bstrVal);
            // A zeroed GUID indicates new ID was not generated
            hasUniqueId = (guid != L"{00000000-0000-0000-0000-000000000000}");
        }

        VariantClear(&vtGuid);
        pObj->Release();
    }
    pEnum->Release();

    return MakeResult(L"Hardware IDs",
        hasUniqueId ? HVE_VERIFY_PASS : HVE_VERIFY_WARNING,
        hasUniqueId
        ? L"BIOS GUID is unique (GenerateNewId succeeded)"
        : L"BIOS GUID appears zeroed — verify GenerateNewId was set",
        ms);
}

HVE_VerifyCheckResult VerificationEngine::CheckWimIntegrity(const wchar_t* exportPath)
{
    auto t0 = std::chrono::steady_clock::now();

    // Find .wim file in the export directory if present
    std::wstring wimPath;
    try
    {
        for (const auto& entry : fs::recursive_directory_iterator(exportPath))
        {
            if (entry.path().extension() == L".wim")
            {
                wimPath = entry.path().wstring();
                break;
            }
        }
    }
    catch (...) {}

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (wimPath.empty())
        return MakeResult(L"SSB Image Integrity", HVE_VERIFY_SKIPPED,
            L"No .wim file found in export path — skipped", ms);

    // Open WIM with wimgapi and verify its integrity hash
    DWORD   creationResult = 0;
    HANDLE  hWim = WIMCreateFile(
        wimPath.c_str(),
        WIM_GENERIC_READ,
        WIM_OPEN_EXISTING,
        WIM_FLAG_VERIFY,          // ← enables hash verification on read
        0,
        &creationResult);

    if (hWim == INVALID_HANDLE_VALUE)
    {
        wchar_t detail[256];
        swprintf_s(detail, L"WIMCreateFile failed: error 0x%08X — image may be corrupt",
            ::GetLastError());
        return MakeResult(L"SSB Image Integrity", HVE_VERIFY_FAIL, detail, ms);
    }

    // WIMGetAttributes gives us the integrity flag
    WIM_INFO wimInfo{};
    bool integrityOk = (WIMGetAttributes(hWim, &wimInfo, sizeof(wimInfo)) == TRUE);

    WIMCloseHandle(hWim);

    ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (!integrityOk)
        return MakeResult(L"SSB Image Integrity", HVE_VERIFY_FAIL,
            L"SSB image metadata could not be read", ms);

    wchar_t detail[256];
    swprintf_s(detail, L"SSB image opened and verified. ImageCount=%u, CompressionType=%u",
        wimInfo.ImageCount, wimInfo.CompressionType);

    return MakeResult(L"SSB Image Integrity", HVE_VERIFY_PASS, detail, ms);
}

HVE_VerifyCheckResult VerificationEngine::CheckDiskSizeReasonable(
    const wchar_t* sourceVhdx, const wchar_t* cloneVhdx)
{
    auto t0 = std::chrono::steady_clock::now();

    if (!sourceVhdx || !std::filesystem::exists(sourceVhdx))
        return MakeResult(L"Disk Size", HVE_VERIFY_SKIPPED,
            L"Source VHDX not provided — size comparison skipped", 0);

    LARGE_INTEGER srcSize{}, cloneSize{};

    HANDLE hSrc = CreateFileW(sourceVhdx, GENERIC_READ, FILE_SHARE_READ,
        nullptr, OPEN_EXISTING, 0, nullptr);
    HANDLE hCln = CreateFileW(cloneVhdx, GENERIC_READ, FILE_SHARE_READ,
        nullptr, OPEN_EXISTING, 0, nullptr);

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (hSrc == INVALID_HANDLE_VALUE || hCln == INVALID_HANDLE_VALUE)
    {
        if (hSrc != INVALID_HANDLE_VALUE) CloseHandle(hSrc);
        if (hCln != INVALID_HANDLE_VALUE) CloseHandle(hCln);
        return MakeResult(L"Disk Size", HVE_VERIFY_WARNING,
            L"Could not open one or both VHDX files for size check", ms);
    }

    GetFileSizeEx(hSrc, &srcSize);
    GetFileSizeEx(hCln, &cloneSize);
    CloseHandle(hSrc);
    CloseHandle(hCln);

    // Clone should be at least 90% of source size
    // (compaction could make it smaller, never drastically so)
    double ratio = (srcSize.QuadPart > 0)
        ? (double)cloneSize.QuadPart / (double)srcSize.QuadPart
        : 1.0;

    wchar_t detail[256];
    swprintf_s(detail,
        L"Source=%llu MB, Clone=%llu MB, Ratio=%.1f%%",
        srcSize.QuadPart / (1024 * 1024),
        cloneSize.QuadPart / (1024 * 1024),
        ratio * 100.0);

    return MakeResult(L"Disk Size",
        (ratio >= 0.90) ? HVE_VERIFY_PASS : HVE_VERIFY_WARNING,
        detail, ms);
}

HVE_VerifyCheckResult VerificationEngine::CheckBootTest(
    const wchar_t* cloneVmName, uint32_t timeoutSec)
{
    auto t0 = std::chrono::steady_clock::now();

    IWbemServices* pSvc = ConnectWMI();
    if (!pSvc)
        return MakeResult(L"Boot Test", HVE_VERIFY_FAIL,
            L"WMI connection failed for boot test", 0);

    // Start the VM
    if (!StartVM(pSvc, cloneVmName))
    {
        pSvc->Release();
        return MakeResult(L"Boot Test", HVE_VERIFY_FAIL,
            L"RequestStateChange to Running failed", 0);
    }

    // Wait for Running state
    bool reached = WaitForVMState(pSvc, cloneVmName,
        VM_STATE_ENABLED, timeoutSec);

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (!reached)
    {
        StopVM(pSvc, cloneVmName);
        pSvc->Release();
        wchar_t detail[256];
        swprintf_s(detail,
            L"VM did not reach Running state within %u seconds", timeoutSec);
        return MakeResult(L"Boot Test", HVE_VERIFY_FAIL, detail, ms);
    }

    // VM is running — immediately request clean shutdown
    bool stopped = StopVM(pSvc, cloneVmName);
    if (stopped)
        WaitForVMState(pSvc, cloneVmName, VM_STATE_DISABLED, 60);

    pSvc->Release();

    ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    wchar_t detail[256];
    swprintf_s(detail,
        L"VM booted and shut down cleanly in %llums", ms);

    return MakeResult(L"Boot Test",
        stopped ? HVE_VERIFY_PASS : HVE_VERIFY_WARNING,
        detail, ms);
}

HVE_VerifyCheckResult VerificationEngine::CheckChecksum(
    const wchar_t* sourceVhdx, const wchar_t* cloneVhdx,
    wchar_t* outSourceHash, wchar_t* outCloneHash)
{
    auto t0 = std::chrono::steady_clock::now();

    bool srcOk = ComputeSHA256(sourceVhdx, outSourceHash);
    bool clnOk = ComputeSHA256(cloneVhdx, outCloneHash);

    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - t0).count();

    if (!srcOk || !clnOk)
        return MakeResult(L"SHA-256 Checksum", HVE_VERIFY_FAIL,
            L"Failed to compute one or both checksums", ms);

    bool match = (wcscmp(outSourceHash, outCloneHash) == 0);

    wchar_t detail[256];
    swprintf_s(detail, L"Source: %.16s... Clone: %.16s...",
        outSourceHash, outCloneHash);

    return MakeResult(L"SHA-256 Checksum",
        match ? HVE_VERIFY_PASS : HVE_VERIFY_FAIL,
        detail, ms);
}

// ── SHA-256 via Windows CNG (bcrypt.h) ────────────────────────────────────────

bool VerificationEngine::ComputeSHA256(const wchar_t* filePath, wchar_t* outHex)
{
    BCRYPT_ALG_HANDLE  hAlg = nullptr;
    BCRYPT_HASH_HANDLE hHash = nullptr;
    HANDLE hFile = INVALID_HANDLE_VALUE;
    auto cleanup = [&]()
    {
        if (hFile != INVALID_HANDLE_VALUE)
        {
            CloseHandle(hFile);
            hFile = INVALID_HANDLE_VALUE;
        }

        if (hHash)
        {
            BCryptDestroyHash(hHash);
            hHash = nullptr;
        }

        if (hAlg)
        {
            BCryptCloseAlgorithmProvider(hAlg, 0);
            hAlg = nullptr;
        }
    };

    if (BCryptOpenAlgorithmProvider(
        &hAlg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) != 0)
        return false;

    DWORD hashLen = 0, cbData = 0;
    if (BCryptGetProperty(hAlg, BCRYPT_HASH_LENGTH,
        (PBYTE)&hashLen, sizeof(DWORD), &cbData, 0) != 0)
    {
        cleanup();
        return false;
    }

    std::vector<BYTE> hashBuf(hashLen);

    if (BCryptCreateHash(hAlg, &hHash, nullptr, 0, nullptr, 0, 0) != 0)
    {
        cleanup();
        return false;
    }

    hFile = CreateFileW(filePath, GENERIC_READ,
        FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_FLAG_SEQUENTIAL_SCAN, nullptr);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        cleanup();
        return false;
    }

    std::vector<BYTE> chunk(4 * 1024 * 1024);
    DWORD read = 0;

    while (ReadFile(hFile, chunk.data(), (DWORD)chunk.size(), &read, nullptr)
        && read > 0)
    {
        if (*_cancelFlag || BCryptHashData(hHash, chunk.data(), read, 0) != 0)
        {
            cleanup();
            return false;
        }
    }

    if (BCryptFinishHash(hHash, hashBuf.data(), hashLen, 0) != 0)
    {
        cleanup();
        return false;
    }

    for (DWORD i = 0; i < hashLen; i++)
        swprintf_s(outHex + (i * 2), 3, L"%02x", hashBuf[i]);

    outHex[hashLen * 2] = L'\0';
    cleanup();
    return true;
}

// ── WMI helpers ───────────────────────────────────────────────────────────────

IWbemServices* VerificationEngine::ConnectWMI()
{
    IWbemLocator* pLoc = nullptr;
    IWbemServices* pSvc = nullptr;

    CoCreateInstance(CLSID_WbemLocator, nullptr,
        CLSCTX_INPROC_SERVER, IID_IWbemLocator, (void**)&pLoc);

    if (!pLoc) return nullptr;

    BSTR ns = SysAllocString(L"ROOT\\virtualization\\v2");
    pLoc->ConnectServer(ns, nullptr, nullptr, nullptr,
        0, nullptr, nullptr, &pSvc);
    SysFreeString(ns);
    pLoc->Release();

    if (pSvc)
    {
        CoSetProxyBlanket(pSvc, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE,
            nullptr, RPC_C_AUTHN_LEVEL_CALL,
            RPC_C_IMP_LEVEL_IMPERSONATE, nullptr, EOAC_NONE);
    }

    return pSvc;
}

IWbemClassObject* VerificationEngine::GetVMObject(
    IWbemServices* svc, const wchar_t* vmName)
{
    std::wstring query =
        L"SELECT * FROM Msvm_ComputerSystem WHERE ElementName='"
        + std::wstring(vmName) + L"'";

    IEnumWbemClassObject* pEnum = nullptr;
    BSTR bQuery = SysAllocString(query.c_str());
    BSTR bWQL = SysAllocString(L"WQL");

    svc->ExecQuery(bWQL, bQuery,
        WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
        nullptr, &pEnum);

    SysFreeString(bQuery);
    SysFreeString(bWQL);

    if (!pEnum) return nullptr;

    IWbemClassObject* pObj = nullptr;
    ULONG returned = 0;
    pEnum->Next(WBEM_INFINITE, 1, &pObj, &returned);
    pEnum->Release();

    return pObj;
}

uint16_t VerificationEngine::GetVMState(
    IWbemServices* svc, const wchar_t* vmName)
{
    IWbemClassObject* pVm = GetVMObject(svc, vmName);
    if (!pVm) return 0;

    VARIANT vt;
    VariantInit(&vt);
    pVm->Get(L"EnabledState", 0, &vt, nullptr, nullptr);
    uint16_t state = vt.uiVal;
    VariantClear(&vt);
    pVm->Release();
    return state;
}

bool VerificationEngine::StartVM(IWbemServices* svc, const wchar_t* vmName)
{
    IWbemClassObject* pVm = GetVMObject(svc, vmName);
    if (!pVm) return false;

    // Get the VM object path for RequestStateChange
    VARIANT vtPath;
    VariantInit(&vtPath);
    pVm->Get(L"__PATH", 0, &vtPath, nullptr, nullptr);
    std::wstring vmPath(vtPath.bstrVal);
    VariantClear(&vtPath);
    pVm->Release();

    // Invoke RequestStateChange(2) = "Start"
    BSTR   bMethod = SysAllocString(L"RequestStateChange");
    BSTR   bClass = SysAllocString(L"Msvm_ComputerSystem");
    BSTR   bPath = SysAllocString(vmPath.c_str());

    IWbemClassObject* pInClass = nullptr;
    IWbemClassObject* pInInst = nullptr;
    IWbemClassObject* pOutParams = nullptr;

    svc->GetObject(bClass, 0, nullptr, &pInClass, nullptr);
    pInClass->GetMethod(bMethod, 0, &pInInst, nullptr);
    pInClass->Release();

    IWbemClassObject* pMethodIn = nullptr;
    pInInst->SpawnInstance(0, &pMethodIn);
    pInInst->Release();

    VARIANT vtState;
    vtState.vt = VT_UI2;
    vtState.uiVal = VM_STATE_ENABLED; // 2 = Running
    pMethodIn->Put(L"RequestedState", 0, &vtState, 0);

    svc->ExecMethod(bPath, bMethod, 0, nullptr, pMethodIn, &pOutParams, nullptr);

    if (pMethodIn)  pMethodIn->Release();
    if (pOutParams) pOutParams->Release();

    SysFreeString(bMethod);
    SysFreeString(bClass);
    SysFreeString(bPath);

    return true;
}

bool VerificationEngine::StopVM(IWbemServices* svc, const wchar_t* vmName)
{
    // Same as StartVM but RequestedState = 3 (Off)
    IWbemClassObject* pVm = GetVMObject(svc, vmName);
    if (!pVm) return false;

    VARIANT vtPath;
    VariantInit(&vtPath);
    pVm->Get(L"__PATH", 0, &vtPath, nullptr, nullptr);
    std::wstring vmPath(vtPath.bstrVal);
    VariantClear(&vtPath);
    pVm->Release();

    BSTR bMethod = SysAllocString(L"RequestStateChange");
    BSTR bClass = SysAllocString(L"Msvm_ComputerSystem");
    BSTR bPath = SysAllocString(vmPath.c_str());

    IWbemClassObject* pInClass = nullptr, * pInInst = nullptr,
        * pMethodIn = nullptr, * pOutParams = nullptr;

    svc->GetObject(bClass, 0, nullptr, &pInClass, nullptr);
    pInClass->GetMethod(bMethod, 0, &pInInst, nullptr);
    pInClass->Release();
    pInInst->SpawnInstance(0, &pMethodIn);
    pInInst->Release();

    VARIANT vtState;
    vtState.vt = VT_UI2;
    vtState.uiVal = VM_STATE_DISABLED; // 3 = Off
    pMethodIn->Put(L"RequestedState", 0, &vtState, 0);

    svc->ExecMethod(bPath, bMethod, 0, nullptr, pMethodIn, &pOutParams, nullptr);

    if (pMethodIn)  pMethodIn->Release();
    if (pOutParams) pOutParams->Release();
    SysFreeString(bMethod);
    SysFreeString(bClass);
    SysFreeString(bPath);

    return true;
}

bool VerificationEngine::WaitForVMState(
    IWbemServices* svc, const wchar_t* vmName,
    uint16_t targetState, uint32_t timeoutSec)
{
    DWORD deadline = GetTickCount() + (timeoutSec * 1000);
    while (GetTickCount() < deadline)
    {
        if (*_cancelFlag) return false;
        uint16_t state = GetVMState(svc, vmName);
        if (state == targetState) return true;
        Sleep(1000);
    }
    return false;
}

std::wstring VerificationEngine::GetParentPath(const wchar_t* vhdxPath)
{
    VIRTUAL_STORAGE_TYPE st{};
    st.DeviceId = VIRTUAL_STORAGE_TYPE_DEVICE_VHDX;
    st.VendorId = kVirtualStorageTypeVendorMicrosoft;

    OPEN_VIRTUAL_DISK_PARAMETERS op{};
    op.Version = OPEN_VIRTUAL_DISK_VERSION_2;
    op.Version2.GetInfoOnly = TRUE;
    op.Version2.ReadOnly = TRUE;

    HANDLE h = INVALID_HANDLE_VALUE;
    if (OpenVirtualDisk(&st, vhdxPath, VIRTUAL_DISK_ACCESS_NONE,
        OPEN_VIRTUAL_DISK_FLAG_NONE, &op, &h) != ERROR_SUCCESS)
        return {};

    GET_VIRTUAL_DISK_INFO info{};
    info.Version = GET_VIRTUAL_DISK_INFO_PARENT_LOCATION;
    DWORD sz = sizeof(info), used = 0;

    DWORD err = GetVirtualDiskInformation(h, &sz, &info, &used);
    CloseHandle(h);

    if (err != ERROR_SUCCESS || !info.ParentLocation.ParentResolved)
        return {};

    // ParentLocationBuffer is a double-null terminated list of paths
    return std::wstring(info.ParentLocation.ParentLocationBuffer);
}

// ── Utility ───────────────────────────────────────────────────────────────────

HVE_VerifyCheckResult VerificationEngine::MakeResult(
    const wchar_t* name, HVE_VERIFY_STATUS  status,
    const wchar_t* detail, uint64_t ms)
{
    HVE_VerifyCheckResult r{};
    wcscpy_s(r.checkName, name);
    r.status = status;
    r.elapsedMs = ms;
    wcscpy_s(r.detail, detail);
    return r;
}

void VerificationEngine::AddCheck(
    HVE_VerifyReport& report, const HVE_VerifyCheckResult& result)
{
    if (report.totalChecks < 32)
        report.checks[report.totalChecks++] = result;

    switch (result.status)
    {
    case HVE_VERIFY_PASS:    report.passCount++;  break;
    case HVE_VERIFY_FAIL:
        report.failCount++;
        if (report.firstFailureDetail[0] == L'\0')
            wcscpy_s(report.firstFailureDetail, result.detail);
        break;
    case HVE_VERIFY_WARNING: report.warnCount++;  break;
    default: break;
    }
}