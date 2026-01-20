/// SystemStateRestore.cpp
#include "BackupEngine.h"
#include <Windows.h>
#include <string>

class SystemStateRestorer {
private:
    ProgressCallback progressCallback;
    std::wstring lastError;

    bool ExecuteWBAdmin(const std::wstring& command,
        const std::wstring& arguments) {
        STARTUPINFOW si = { sizeof(si) };
        PROCESS_INFORMATION pi;

        std::wstring cmdLine = L"wbadmin.exe " + command + L" " + arguments;

        // Create process with output redirection
        HANDLE hStdOutRead, hStdOutWrite;
        SECURITY_ATTRIBUTES sa = { sizeof(SECURITY_ATTRIBUTES), NULL, TRUE };

        if (!CreatePipe(&hStdOutRead, &hStdOutWrite, &sa, 0)) {
            return false;
        }

        si.cb = sizeof(STARTUPINFO);
        si.dwFlags = STARTF_USESTDHANDLES;
        si.hStdOutput = hStdOutWrite;
        si.hStdError = hStdOutWrite;

        BOOL success = CreateProcessW(
            NULL,
            &cmdLine[0],
            NULL, NULL,
            TRUE,
            CREATE_NO_WINDOW,
            NULL, NULL,
            &si, &pi);

        CloseHandle(hStdOutWrite);

        if (!success) {
            CloseHandle(hStdOutRead);
            lastError = L"Failed to execute wbadmin";
            return false;
        }

        // Read output and monitor progress
        char buffer[4096];
        DWORD bytesRead;

        while (ReadFile(hStdOutRead, buffer, sizeof(buffer) - 1, &bytesRead, NULL) && bytesRead > 0) {
            buffer[bytesRead] = '\0';

            // Parse output for progress indicators
            std::string output(buffer);
            if (output.find("percent") != std::string::npos) {
                // Parse percentage if available
                if (progressCallback) {
                    // Simple progress update
                    progressCallback(50, L"Restore in progress...");
                }
            }
        }

        WaitForSingleObject(pi.hProcess, INFINITE);

        DWORD exitCode;
        GetExitCodeProcess(pi.hProcess, &exitCode);

        CloseHandle(hStdOutRead);
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);

        return exitCode == 0;
    }

public:
    SystemStateRestorer(ProgressCallback callback) : progressCallback(callback) {}

    int RestoreSystemState(const std::wstring& backupPath,
        const std::wstring& targetVolume) {
        try {
            if (progressCallback) {
                progressCallback(0, L"Preparing system state restore...");
            }

            // Build wbadmin command
            std::wstring arguments =
                std::wstring(L"start systemstaterecovery ") +
                L"-version:" + std::wstring(backupPath) + L" " +
                L"-backupTarget:" + std::wstring(targetVolume) + L" " +
                L"-machine:" + std::wstring(backupPath) + L" " +
                L"-quiet";

            if (progressCallback) {
                progressCallback(10, L"Starting system state restore...");
            }

            bool success = ExecuteWBAdmin(L"start systemstaterecovery", arguments);

            if (!success) {
                lastError = L"System state restore failed";
                return -1;
            }

            if (progressCallback) {
                progressCallback(100, L"System state restore completed");
            }

            return 0;
        }
        catch (...) {
            lastError = L"Unexpected error during system state restore";
            return -99;
        }
    }

    const std::wstring& GetLastError() const { return lastError; }
};

extern "C" {
    BACKUPENGINE_API int RestoreSystemState(
        const wchar_t* backupPath,
        const wchar_t* targetVolume,
        ProgressCallback callback) {

        try {
            SystemStateRestorer restorer(callback);
            return restorer.RestoreSystemState(backupPath, targetVolume);
        }
        catch (...) {
            return -99;
        }
    }
}
