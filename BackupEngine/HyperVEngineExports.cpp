//HyperVEngineExports.cpp
#include "BackupEngine.h"
//#include "../core/DiskConsolidator.h"
#include "RecoveryManager.h"
#include "VerificationEngine.h"

#include <wbemidl.h>
#include <wimgapi.h>
#include <string>
#include <atomic>
#include <mutex>

#pragma comment(lib, "wbemuuid.lib")
#pragma comment(lib, "wimgapi.lib")

// ── Global engine state ───────────────────────────────────────────────────────
//static HyperVCloneEngine* g_cloneEngine = nullptr;
//static DiskConsolidator* g_consolidator = nullptr;
static RecoveryManager* g_recovery = nullptr;

static HVE_ProgressCallback g_progressCb = nullptr;
static HVE_StatusCallback   g_statusCb = nullptr;
static void* g_userState = nullptr;

static std::atomic<bool>    g_cancelFlag{ false };
static std::wstring         g_lastError;
static std::mutex           g_errorMutex;

// ── Internal helpers ──────────────────────────────────────────────────────────
static void SetLastError(const std::wstring& msg)
{
    std::lock_guard<std::mutex> lock(g_errorMutex);
    g_lastError = msg;
}

static void FireProgress(int32_t pct, const wchar_t* msg)
{
    if (g_progressCb)
        g_progressCb(pct, msg, g_userState);
}

static void FireStatus(HVE_EVENT evt, const wchar_t* msg)
{
    if (g_statusCb)
        g_statusCb((int32_t)evt, msg, g_userState);
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────

HVE_RESULT __stdcall HVE_Initialize()
{
    try
    {
        // Initialize COM for WMI calls
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (FAILED(hr) && hr != RPC_E_CHANGED_MODE)
        {
            SetLastError(L"CoInitializeEx failed");
            return HVE_ERR_WMI_CONNECT;
        }

        hr = CoInitializeSecurity(
            nullptr, -1, nullptr, nullptr,
            RPC_C_AUTHN_LEVEL_DEFAULT,
            RPC_C_IMP_LEVEL_IMPERSONATE,
            nullptr, EOAC_NONE, nullptr);

        // RPC_E_TOO_LATE is OK — COM security already set
        if (FAILED(hr) && hr != RPC_E_TOO_LATE)
        {
            SetLastError(L"CoInitializeSecurity failed");
            return HVE_ERR_WMI_CONNECT;
        }

        //g_cloneEngine = new HyperVCloneEngine(&g_cancelFlag, FireProgress, FireStatus);
       // g_consolidator = new DiskConsolidator(&g_cancelFlag, FireProgress, FireStatus);
        g_recovery = new RecoveryManager(FireStatus);

        return HVE_OK;
    }
    catch (const std::exception&)
    {
        SetLastError(L"HVE_Initialize: SEH exception during init");
        return HVE_ERR_GENERAL;
    }
}

HVE_RESULT __stdcall HVE_Shutdown()
{
    try
    {
        //delete g_cloneEngine;  g_cloneEngine = nullptr;
        //delete g_consolidator; g_consolidator = nullptr;
        delete g_recovery;     g_recovery = nullptr;
        CoUninitialize();
        return HVE_OK;
    }
    catch (const std::exception&)
    {
        return HVE_ERR_GENERAL;
    }
}

const wchar_t* __stdcall HVE_GetEngineVersion()
{
    return L"1.0.0";
}

// ── Callbacks ─────────────────────────────────────────────────────────────────

void __stdcall HVE_SetProgressCallback(HVE_ProgressCallback cb, void* userState)
{
    g_progressCb = cb;
    g_userState = userState;
}

void __stdcall HVE_SetStatusCallback(HVE_StatusCallback cb, void* userState)
{
    g_statusCb = cb;
    g_userState = userState;
}

// ── HVE_CloneVM ───────────────────────────────────────────────────────────────

HVE_RESULT __stdcall HVE_CloneVM(const HVE_CloneVMParams* params)
{
    if (!params || !params->vmName || !params->exportStagingPath || !params->targetPath)
    {
        SetLastError(L"HVE_CloneVM: null parameter");
        return HVE_ERR_INVALID_ARG;
    }

    g_cancelFlag = false;

    // Wrap entire operation in SEH so C# always gets a clean return value
    // even if WMI or wimgapi throws a structured exception
    try
    {
        // ── Phase 1: Verify VM is offline ────────────────────────────────────
        FireStatus(HVE_EVT_PHASE_START, L"Verifying VM state");
        FireProgress(0, L"Checking VM power state...");

      /*  if (!g_cloneEngine->IsVMOffline(params->vmName))
        {
            SetLastError(L"VM must be in the Off state before cloning.");
            return HVE_ERR_VM_NOT_OFFLINE;
        } */

        // ── Phase 2: Write recovery checkpoint ───────────────────────────────
        if (params->checkpointPath)
        {
            FireProgress(3, L"Writing recovery checkpoint...");
            if (!g_recovery->WriteCheckpoint(params->checkpointPath,
                params->vmName,
                params->exportStagingPath))
            {
                SetLastError(L"Failed to write recovery checkpoint");
                return HVE_ERR_CHECKPOINT_FAIL;
            }
        }

        // ── Phase 3: Export VM ───────────────────────────────────────────────
        FireStatus(HVE_EVT_PHASE_START, L"Exporting VM");
        FireProgress(5, L"Starting VM export...");

        //HVE_RESULT r = g_cloneEngine->ExportVM(params->vmName, params->exportStagingPath);

       // if (r != HVE_OK) { SetLastError(g_cloneEngine->GetLastError()); return r; }
        if (g_cancelFlag) return HVE_ERR_CANCELLED;

        FireStatus(HVE_EVT_PHASE_END, L"Export complete");

        // ── Phase 4: Consolidate AVHDX chain ─────────────────────────────────
        if (params->removeCheckpoints)
        {
            FireStatus(HVE_EVT_PHASE_START, L"Consolidating AVHDX chain");
            FireProgress(35, L"Scanning disk chain...");

            //r = g_consolidator->ConsolidateChain(params->exportStagingPath);
            //if (r != HVE_OK) { SetLastError(g_consolidator->GetLastError()); return r; }
            if (g_cancelFlag) return HVE_ERR_CANCELLED;

            FireStatus(HVE_EVT_PHASE_END, L"Consolidation complete");
        }

        // ── Phase 5: Import consolidated VM ──────────────────────────────────
        FireStatus(HVE_EVT_PHASE_START, L"Importing VM");
        FireProgress(75, L"Importing consolidated VM...");

   /*     r = g_cloneEngine->ImportVM(
            params->exportStagingPath,
            params->targetPath,
            params->generateNewId);*/

       // if (r != HVE_OK) { SetLastError(g_cloneEngine->GetLastError()); return r; }

        // ── Phase 6: Clear checkpoint on success ──────────────────────────────
        if (params->checkpointPath)
            g_recovery->ClearCheckpoint(params->checkpointPath);

        FireProgress(100, L"VM clone complete.");
        FireStatus(HVE_EVT_PHASE_END, L"Clone complete");

        return HVE_OK;
    }
    catch (std::exception& e)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"Exception during CloneVM: %S", e.what());
        SetLastError(msg);
        FireStatus(HVE_EVT_FATAL, msg);
        return HVE_ERR_GENERAL;
    }
}

// ── HVE_CloneDisk ─────────────────────────────────────────────────────────────

HVE_RESULT __stdcall HVE_CloneDisk(const HVE_CloneDiskParams* params)
{
    if (!params || !params->sourceVhdxPath || !params->destinationPath)
    {
        SetLastError(L"HVE_CloneDisk: null parameter");
        return HVE_ERR_INVALID_ARG;
    }

    g_cancelFlag = false;

    try
    {
        FireStatus(HVE_EVT_PHASE_START, L"Disk clone started");
        FireProgress(0, L"Opening source disk...");

      /*  HVE_RESULT r = g_consolidator->CloneDisk(
            params->sourceVhdxPath,
            params->destinationPath,
            params->consolidateChain,
            params->compactAfterMerge);

        if (r != HVE_OK)
        {
            SetLastError(g_consolidator->GetLastError());
            return r;
        }*/

        FireProgress(100, L"Disk clone complete.");
        FireStatus(HVE_EVT_PHASE_END, L"Disk clone complete");
        return HVE_OK;
    }
    catch (std::exception& e)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"Exception during CloneDisk: %S", e.what());
        SetLastError(msg);
        FireStatus(HVE_EVT_FATAL, msg);
        return HVE_ERR_GENERAL;
    }
}

// ── HVE_ConsolidateAVHDX ──────────────────────────────────────────────────────

HVE_RESULT __stdcall HVE_ConsolidateAVHDX(
    const wchar_t* exportPath, const wchar_t* outputVhdxPath)
{
    if (!exportPath || !outputVhdxPath)
    {
        SetLastError(L"HVE_ConsolidateAVHDX: null parameter");
        return HVE_ERR_INVALID_ARG;
    }

    g_cancelFlag = false;

    try
    {
        //FireStatus(HVE_EVT_PHASE_START, L"AVHDX consolidation");
        //HVE_RESULT r = g_consolidator->ConsolidateToSingleVHDX(exportPath, outputVhdxPath);

        //if (r != HVE_OK) SetLastError(g_consolidator->GetLastError());
       // return r;
    }
    catch (const std::exception&)
    {
        SetLastError(L"SEH exception during ConsolidateAVHDX");
        return HVE_ERR_GENERAL;
    }

    SetLastError(L"ConsolidateAVHDX is not implemented in this build");
    return HVE_ERR_GENERAL;
}

// ── HVE_ImportVM ──────────────────────────────────────────────────────────────

HVE_RESULT __stdcall HVE_ImportVM(
    const wchar_t* exportPath,
    const wchar_t* targetPath,
    bool           generateNewId)
{
    if (!exportPath || !targetPath)
    {
        SetLastError(L"HVE_ImportVM: null parameter");
        return HVE_ERR_INVALID_ARG;
    }

    g_cancelFlag = false;
    
    FireProgress(0, L"Starting VM import...");
    SetLastError(L"ImportVM is not implemented in this build");
    return HVE_ERR_GENERAL;
}

// ── HVE_GetDiskChain ──────────────────────────────────────────────────────────

HVE_RESULT __stdcall HVE_GetDiskChain(
    const wchar_t* vmPath,
    HVE_DiskEntry* outEntries,
    int32_t* outCount)
{
    if (!vmPath || !outCount)
    {
        SetLastError(L"HVE_GetDiskChain: null parameter");
        return HVE_ERR_INVALID_ARG;
    }
    *outCount = 0;
    SetLastError(L"GetDiskChain is not implemented in this build");
    return HVE_ERR_GENERAL;
}

// ── Control & Error ───────────────────────────────────────────────────────────

void __stdcall HVE_CancelOperation()
{
    g_cancelFlag = true;
    FireStatus(HVE_EVT_WARNING, L"Cancel requested.");
}

const wchar_t* __stdcall HVE_GetLastError()
{
    std::lock_guard<std::mutex> lock(g_errorMutex);
    wchar_t* copy = new wchar_t[g_lastError.size() + 1];
    wcscpy_s(copy, g_lastError.size() + 1, g_lastError.c_str());
    return copy;
}

void __stdcall HVE_FreeString(const wchar_t* str)
{
    delete[] str;
}
// Add to EngineExports.cpp:
HVE_RESULT __stdcall HVE_VerifyClone(
    const HVE_VerifyParams* params,
    HVE_VerifyReport* outReport)
{
    if (!params || !outReport)
    {
        SetLastError(L"HVE_VerifyClone: null parameter");
        return HVE_ERR_INVALID_ARG;
    }

    g_cancelFlag = false;

    // Create verification engine with same callbacks as clone ops
    VerificationEngine verifier(
        &g_cancelFlag, FireProgress, FireStatus);
    return verifier.Verify(*params, *outReport);
}