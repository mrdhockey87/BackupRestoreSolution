//RecoveryManager.h
#pragma once
#include <windows.h>
#include <string>
#include <functional>
#include "BackupEngine.h"

class RecoveryManager
{
public:
    using StatusFn = std::function<void(HVE_EVENT, const wchar_t*)>;

    explicit RecoveryManager(StatusFn statusFn);

    // Write checkpoint so we can resume after a crash
    bool WriteCheckpoint(const wchar_t* checkpointDir,
        const wchar_t* vmName,
        const wchar_t* stagingPath);

    // Check for an existing checkpoint on startup
    bool HasCheckpoint(const wchar_t* checkpointDir);
    bool ReadCheckpoint(const wchar_t* checkpointDir,
        std::wstring& outVmName,
        std::wstring& outStagingPath,
        std::wstring& outPhase);

    void UpdateCheckpointPhase(const wchar_t* checkpointDir,
        const wchar_t* phase);
    void ClearCheckpoint(const wchar_t* checkpointDir);

    // SEH filter — call from __except() expression
    LONG HandleSEH(DWORD exceptionCode, EXCEPTION_POINTERS* ep);

private:
    StatusFn     _statusFn;
    std::wstring CheckpointFilePath(const wchar_t* dir);
};