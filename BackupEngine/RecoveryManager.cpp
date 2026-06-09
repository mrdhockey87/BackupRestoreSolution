//RecoveryManager.cpp
#include "RecoveryManager.h"
#include <fstream>
#include <sstream>

RecoveryManager::RecoveryManager(StatusFn statusFn)
    : _statusFn(statusFn) {}

std::wstring RecoveryManager::CheckpointFilePath(const wchar_t* dir)
{
    return std::wstring(dir) + L"\\hve_checkpoint.json";
}

bool RecoveryManager::WriteCheckpoint(
    const wchar_t* checkpointDir,
    const wchar_t* vmName,
    const wchar_t* stagingPath)
{
    std::wofstream f(CheckpointFilePath(checkpointDir));
    if (!f) return false;

    // Simple hand-rolled JSON — no external dependency needed here
    f << L"{\n"
        << L"  \"vmName\": \"" << vmName << L"\",\n"
        << L"  \"stagingPath\": \"" << stagingPath << L"\",\n"
        << L"  \"phase\": \"export\",\n"
        << L"  \"timestamp\": " << GetTickCount64() << L"\n"
        << L"}\n";

    return f.good();
}

bool RecoveryManager::HasCheckpoint(const wchar_t* checkpointDir)
{
    return GetFileAttributesW(
        CheckpointFilePath(checkpointDir).c_str()) != INVALID_FILE_ATTRIBUTES;
}

void RecoveryManager::UpdateCheckpointPhase(
    const wchar_t* checkpointDir, const wchar_t* phase)
{
    // Read existing, rewrite with updated phase
    std::wstring vmName, stagingPath, oldPhase;
    if (!ReadCheckpoint(checkpointDir, vmName, stagingPath, oldPhase)) return;

    std::wofstream f(CheckpointFilePath(checkpointDir));
    if (!f) return;

    f << L"{\n"
        << L"  \"vmName\": \"" << vmName << L"\",\n"
        << L"  \"stagingPath\": \"" << stagingPath << L"\",\n"
        << L"  \"phase\": \"" << phase << L"\",\n"
        << L"  \"timestamp\": " << GetTickCount64() << L"\n"
        << L"}\n";
}

bool RecoveryManager::ReadCheckpoint(
    const wchar_t* checkpointDir,
    std::wstring& outVmName,
    std::wstring& outStagingPath,
    std::wstring& outPhase)
{
    std::wifstream f(CheckpointFilePath(checkpointDir));
    if (!f) return false;

    // Minimal parser — reads our own known format
    std::wstring line;
    auto extract = [](const std::wstring& l) -> std::wstring {
        auto s = l.find(L'"', l.find(L':') + 1) + 1;
        auto e = l.rfind(L'"');
        return (s < e) ? l.substr(s, e - s) : L"";
        };

    while (std::getline(f, line))
    {
        if (line.find(L"vmName") != std::wstring::npos) outVmName = extract(line);
        if (line.find(L"stagingPath") != std::wstring::npos) outStagingPath = extract(line);
        if (line.find(L"phase") != std::wstring::npos) outPhase = extract(line);
    }

    return !outVmName.empty();
}

void RecoveryManager::ClearCheckpoint(const wchar_t* checkpointDir)
{
    DeleteFileW(CheckpointFilePath(checkpointDir).c_str());
}

LONG RecoveryManager::HandleSEH(DWORD exceptionCode, EXCEPTION_POINTERS* ep)
{
    wchar_t msg[256];
    swprintf_s(msg, L"SEH exception 0x%08X — engine recovering", exceptionCode);
    _statusFn(HVE_EVT_RECOVERING, msg);
    return EXCEPTION_EXECUTE_HANDLER;
}