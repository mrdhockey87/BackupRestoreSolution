#pragma once
#include "BackupEngine.h"
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wimgapi.h>
#include <wbemidl.h>
#include <string>
#include <vector>
#include <functional>
#include <atomic>

using ProgressFn = std::function<void(int32_t, const wchar_t*)>;
using StatusFn = std::function<void(HVE_EVENT, const wchar_t*)>;

class VerificationEngine
{
public:
    VerificationEngine(std::atomic<bool>* cancelFlag,
        ProgressFn         progressFn,
        StatusFn           statusFn);
    ~VerificationEngine();

    HVE_RESULT Verify(const HVE_VerifyParams& params, HVE_VerifyReport& outReport);

    const std::wstring& GetLastError() const { return _lastError; }

private:
    // ── Individual check methods ──────────────────────────────────────────────
    HVE_VerifyCheckResult CheckVhdxIntegrity(const wchar_t* vhdxPath);
    HVE_VerifyCheckResult CheckDiskChain(const wchar_t* vhdxPath);
    HVE_VerifyCheckResult CheckParentLocators(const wchar_t* vhdxPath);
    HVE_VerifyCheckResult CheckVmcxValidity(const wchar_t* exportPath);
    HVE_VerifyCheckResult CheckHardwareIds(const wchar_t* cloneVmName);
    HVE_VerifyCheckResult CheckVmVisible(const wchar_t* cloneVmName);
    HVE_VerifyCheckResult CheckNoOrphanedAVHDX(const wchar_t* exportPath);
    HVE_VerifyCheckResult CheckDiskSizeReasonable(const wchar_t* sourceVhdx,
        const wchar_t* cloneVhdx);
    HVE_VerifyCheckResult CheckWimIntegrity(const wchar_t* exportPath);
    HVE_VerifyCheckResult CheckBootTest(const wchar_t* cloneVmName,
        uint32_t       timeoutSec);
    HVE_VerifyCheckResult CheckChecksum(const wchar_t* sourceVhdx,
        const wchar_t* cloneVhdx,
        wchar_t* outSourceHash,
        wchar_t* outCloneHash);

    // ── WMI helpers ───────────────────────────────────────────────────────────
    IWbemServices* ConnectWMI();
    IWbemClassObject* GetVMObject(IWbemServices* svc, const wchar_t* vmName);
    uint16_t          GetVMState(IWbemServices* svc, const wchar_t* vmName);
    bool              StartVM(IWbemServices* svc, const wchar_t* vmName);
    bool              StopVM(IWbemServices* svc, const wchar_t* vmName);
    bool              WaitForVMState(IWbemServices* svc, const wchar_t* vmName,
        uint16_t targetState, uint32_t timeoutSec);

    // ── Disk helpers ──────────────────────────────────────────────────────────
    bool              VhdxMetadataValid(const wchar_t* path);
    bool              ResolveParentChain(const wchar_t* leafPath);
    std::wstring      GetParentPath(const wchar_t* vhdxPath);
    bool              ComputeSHA256(const wchar_t* filePath, wchar_t* outHex);

    // ── WIM helpers ───────────────────────────────────────────────────────────
    bool              VerifyWimFile(const wchar_t* wimPath);

    // ── Utility ───────────────────────────────────────────────────────────────
    HVE_VerifyCheckResult MakeResult(const wchar_t* name, HVE_VERIFY_STATUS status,
        const wchar_t* detail, uint64_t ms);
    void              AddCheck(HVE_VerifyReport& report,
        const HVE_VerifyCheckResult& result);

    std::atomic<bool>* _cancelFlag;
    ProgressFn         _progressFn;
    StatusFn           _statusFn;
    std::wstring       _lastError;
};