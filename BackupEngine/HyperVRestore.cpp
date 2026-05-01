// HyperVRestore.cpp
#include "BackupEngine.h"
#include <Windows.h>
#include <comdef.h>
#include <Wbemidl.h>
#include <string>
#include <filesystem>
#include <fstream>
#include <vector>
#include <algorithm>

#pragma comment(lib, "wbemuuid.lib")

namespace fs = std::filesystem;

namespace {
    std::wstring TrimProcessOutput(const std::wstring& value)
    {
        size_t start = value.find_first_not_of(L" \t\r\n");
        if (start == std::wstring::npos)
        {
            return L"";
        }

        size_t end = value.find_last_not_of(L" \t\r\n");
        return value.substr(start, end - start + 1);
    }

    std::wstring EscapePowerShellSingleQuotedString(const std::wstring& value)
    {
        std::wstring escaped = value;
        size_t position = 0;
        while ((position = escaped.find(L'\'', position)) != std::wstring::npos)
        {
            escaped.insert(position, 1, L'\'');
            position += 2;
        }

        return escaped;
    }

    bool IsVirtualMachinesFolderPath(const fs::path& path)
    {
        std::wstring parent = path.parent_path().filename().wstring();
        if (!parent.empty() && _wcsicmp(parent.c_str(), L"Virtual Machines") == 0)
        {
            return true;
        }

        std::wstring grandParent = path.parent_path().parent_path().filename().wstring();
        return !grandParent.empty() && _wcsicmp(grandParent.c_str(), L"Virtual Machines") == 0;
    }

    int GetDefinitionFilePriority(const fs::path& path)
    {
        std::wstring extension = path.extension().wstring();
        if (_wcsicmp(extension.c_str(), L".vmcx") == 0)
        {
            return 0;
        }

        if (_wcsicmp(extension.c_str(), L".xml") == 0)
        {
            return 1;
        }

        if (_wcsicmp(extension.c_str(), L".exp") == 0)
        {
            return 2;
        }

        return 3;
    }

    std::wstring ResolveHyperVSystemDefinitionFilePath(const std::wstring& importRoot)
    {
        std::error_code ec;
        fs::path root(importRoot);
        if (!fs::exists(root, ec) || !fs::is_directory(root, ec))
        {
            return L"";
        }

        std::vector<fs::path> preferredCandidates;
        std::vector<fs::path> fallbackCandidates;

        for (const auto& entry : fs::recursive_directory_iterator(root, ec))
        {
            if (ec || !entry.is_regular_file(ec))
            {
                continue;
            }

            std::wstring extension = entry.path().extension().wstring();
            if (_wcsicmp(extension.c_str(), L".vmcx") != 0 &&
                _wcsicmp(extension.c_str(), L".xml") != 0 &&
                _wcsicmp(extension.c_str(), L".exp") != 0)
            {
                continue;
            }

            if (IsVirtualMachinesFolderPath(entry.path()))
            {
                preferredCandidates.push_back(entry.path());
            }
            else
            {
                fallbackCandidates.push_back(entry.path());
            }
        }

        auto sortCandidates = [](std::vector<fs::path>& candidates)
        {
            std::sort(candidates.begin(), candidates.end(), [](const fs::path& left, const fs::path& right)
            {
                int leftPriority = GetDefinitionFilePriority(left);
                int rightPriority = GetDefinitionFilePriority(right);
                if (leftPriority != rightPriority)
                {
                    return leftPriority < rightPriority;
                }

                return left.wstring() < right.wstring();
            });
        };

        sortCandidates(preferredCandidates);
        sortCandidates(fallbackCandidates);

        if (!preferredCandidates.empty())
        {
            return preferredCandidates.front().wstring();
        }

        if (!fallbackCandidates.empty())
        {
            return fallbackCandidates.front().wstring();
        }

        return L"";
    }

    bool ExecutePowerShellImportScript(const std::wstring& script, std::wstring& output, std::wstring& errorMessage)
    {
        output.clear();
        errorMessage.clear();

        SECURITY_ATTRIBUTES securityAttributes = { sizeof(SECURITY_ATTRIBUTES), NULL, TRUE };
        HANDLE standardOutputRead = NULL;
        HANDLE standardOutputWrite = NULL;
        if (!CreatePipe(&standardOutputRead, &standardOutputWrite, &securityAttributes, 0))
        {
            errorMessage = L"Failed to create PowerShell output pipe.";
            return false;
        }

        STARTUPINFOW startupInfo = { sizeof(STARTUPINFOW) };
        startupInfo.dwFlags = STARTF_USESTDHANDLES;
        startupInfo.hStdOutput = standardOutputWrite;
        startupInfo.hStdError = standardOutputWrite;

        PROCESS_INFORMATION processInformation = {};
        std::wstring commandLine = L"powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + L"\"";

        BOOL created = CreateProcessW(
            NULL,
            commandLine.data(),
            NULL,
            NULL,
            TRUE,
            CREATE_NO_WINDOW,
            NULL,
            NULL,
            &startupInfo,
            &processInformation);

        CloseHandle(standardOutputWrite);

        if (!created)
        {
            CloseHandle(standardOutputRead);
            errorMessage = L"Failed to start PowerShell Import-VM process.";
            return false;
        }

        std::string outputBytes;
        char buffer[4096];
        DWORD bytesRead = 0;
        while (ReadFile(standardOutputRead, buffer, sizeof(buffer), &bytesRead, NULL) && bytesRead > 0)
        {
            outputBytes.append(buffer, buffer + bytesRead);
        }

        WaitForSingleObject(processInformation.hProcess, INFINITE);

        DWORD exitCode = 0;
        GetExitCodeProcess(processInformation.hProcess, &exitCode);

        CloseHandle(standardOutputRead);
        CloseHandle(processInformation.hProcess);
        CloseHandle(processInformation.hThread);

        if (!outputBytes.empty())
        {
            int requiredSize = MultiByteToWideChar(CP_UTF8, 0, outputBytes.c_str(), static_cast<int>(outputBytes.size()), NULL, 0);
            if (requiredSize > 0)
            {
                output.resize(requiredSize);
                MultiByteToWideChar(CP_UTF8, 0, outputBytes.c_str(), static_cast<int>(outputBytes.size()), output.data(), requiredSize);
                output = TrimProcessOutput(output);
            }
        }

        if (exitCode == 0)
        {
            return true;
        }

        errorMessage = output.empty() ? L"PowerShell Import-VM failed." : output;
        return false;
    }
}

class HyperVRestorer {
private:
    IWbemServices* pSvc;
    IWbemLocator* pLoc;
    ProgressCallback progressCallback;
    std::wstring lastError;
    std::wstring importedVmName;

    HRESULT Initialize() {
        CoInitializeEx(0, COINIT_MULTITHREADED);

        HRESULT hr = CoCreateInstance(
            CLSID_WbemLocator, 0,
            CLSCTX_INPROC_SERVER,
            IID_IWbemLocator,
            (LPVOID*)&pLoc);

        if (FAILED(hr)) return hr;

        hr = pLoc->ConnectServer(
            _bstr_t(L"ROOT\\virtualization\\v2"),
            NULL, NULL, 0, NULL, 0, 0, &pSvc);

        if (SUCCEEDED(hr)) {
            CoSetProxyBlanket(
                pSvc,
                RPC_C_AUTHN_WINNT,
                RPC_C_AUTHZ_NONE,
                NULL,
                RPC_C_AUTHN_LEVEL_CALL,
                RPC_C_IMP_LEVEL_IMPERSONATE,
                NULL,
                EOAC_NONE);
        }

        return hr;
    }

    HRESULT GetManagementService(IWbemClassObject** ppService) {
        IEnumWbemClassObject* pEnumerator = NULL;

        HRESULT hr = pSvc->ExecQuery(
            bstr_t("WQL"),
            bstr_t("SELECT * FROM Msvm_VirtualSystemManagementService"),
            WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
            NULL,
            &pEnumerator);

        if (FAILED(hr)) return hr;

        ULONG uReturn = 0;
        hr = pEnumerator->Next(WBEM_INFINITE, 1, ppService, &uReturn);
        pEnumerator->Release();

        return (uReturn == 0) ? E_FAIL : hr;
    }

public:
    HyperVRestorer(ProgressCallback callback)
        : pSvc(nullptr), pLoc(nullptr), progressCallback(callback) {
    }

    int ImportVM(const wchar_t* backupPath,
        const wchar_t* vmName,
        const wchar_t* vmStoragePath) {
        try {
            if (!backupPath || !vmStoragePath || *backupPath == L'\0' || *vmStoragePath == L'\0') {
                lastError = L"Hyper-V restore requires a backup path and VM storage path.";
                return -1;
            }

            if (progressCallback) {
                progressCallback(0, L"Preparing Hyper-V import...");
            }

            std::wstring systemDefinitionFile = ResolveHyperVSystemDefinitionFilePath(backupPath);
            if (systemDefinitionFile.empty()) {
                lastError = L"Failed to locate an exported Hyper-V configuration file (.vmcx, .xml, or .exp).";
                return -2;
            }

            if (progressCallback) {
                progressCallback(20, L"Preparing VM storage path...");
            }

            std::error_code ec;
            fs::path vmStorage(vmStoragePath);
            fs::create_directories(vmStorage, ec);
            if (ec) {
                lastError = L"Failed to create Hyper-V VM storage path.";
                return -3;
            }

            fs::path snapshotPath = vmStorage / L"Snapshots";
            fs::path smartPagingPath = vmStorage / L"SmartPaging";
            fs::create_directories(snapshotPath, ec);
            if (ec) {
                lastError = L"Failed to create Hyper-V snapshot storage path.";
                return -4;
            }

            fs::create_directories(smartPagingPath, ec);
            if (ec) {
                lastError = L"Failed to create Hyper-V smart paging storage path.";
                return -5;
            }

            if (progressCallback) {
                progressCallback(40, L"Importing VM configuration...");
            }

            std::wstring escapedConfigPath = EscapePowerShellSingleQuotedString(systemDefinitionFile);
            std::wstring escapedVmStoragePath = EscapePowerShellSingleQuotedString(vmStorage.wstring());
            std::wstring escapedSnapshotPath = EscapePowerShellSingleQuotedString(snapshotPath.wstring());
            std::wstring escapedSmartPagingPath = EscapePowerShellSingleQuotedString(smartPagingPath.wstring());
            std::wstring escapedVmName = EscapePowerShellSingleQuotedString(vmName ? vmName : L"");

            std::wstring script =
                L"$ErrorActionPreference='Stop'; "
                L"[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; "
                L"$OutputEncoding=[System.Text.Encoding]::UTF8; "
                L"Import-Module Hyper-V -ErrorAction Stop; "
                L"$configPath='" + escapedConfigPath + L"'; "
                L"$vmPath='" + escapedVmStoragePath + L"'; "
                L"$snapshotPath='" + escapedSnapshotPath + L"'; "
                L"$smartPagingPath='" + escapedSmartPagingPath + L"'; "
                L"$targetName='" + escapedVmName + L"'; "
                L"$importedVm = Import-VM -Path $configPath -Copy -GenerateNewId -VhdDestinationPath $vmPath -VirtualMachinePath $vmPath -SnapshotFilePath $snapshotPath -SmartPagingFilePath $smartPagingPath -ErrorAction Stop; "
                L"if (-not [string]::IsNullOrWhiteSpace($targetName) -and $importedVm.Name -ne $targetName) { "
                L"Rename-VM -VM $importedVm -NewName $targetName -ErrorAction Stop; "
                L"$importedVm = Get-VM -Id $importedVm.VMId -ErrorAction Stop; } "
                L"Write-Output $importedVm.Name;";

            if (progressCallback) {
                progressCallback(60, L"Executing Hyper-V import...");
            }

            std::wstring importOutput;
            std::wstring importError;
            if (!ExecutePowerShellImportScript(script, importOutput, importError)) {
                lastError = importError;
                return -6;
            }

            importedVmName = !importOutput.empty()
                ? importOutput
                : std::wstring(vmName ? vmName : L"");

            if (progressCallback) {
                progressCallback(100, L"VM restore completed successfully");
            }

            return 0;
        }
        catch (...) {
            lastError = L"Unexpected error during VM import";
            return -99;
        }
    }

    int StartVM(const wchar_t* vmName) {
        try {
            HRESULT hr = Initialize();
            if (FAILED(hr)) return -1;

            // Query for the VM
            wchar_t query[512];
            swprintf_s(query,
                L"SELECT * FROM Msvm_ComputerSystem WHERE ElementName='%s'",
                vmName);

            IEnumWbemClassObject* pEnumerator = NULL;
            hr = pSvc->ExecQuery(
                bstr_t("WQL"),
                bstr_t(query),
                WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
                NULL,
                &pEnumerator);

            if (FAILED(hr)) {
                Cleanup();
                return -2;
            }

            IWbemClassObject* pVM = NULL;
            ULONG uReturn = 0;
            hr = pEnumerator->Next(WBEM_INFINITE, 1, &pVM, &uReturn);

            if (uReturn == 0) {
                pEnumerator->Release();
                Cleanup();
                return -3;
            }

            // Get VM path
            VARIANT varPath;
            pVM->Get(L"__PATH", 0, &varPath, 0, 0);

            // Call RequestStateChange method
            IWbemClassObject* pClass = NULL;
            hr = pSvc->GetObject(
                bstr_t(L"Msvm_ComputerSystem"),
                0, NULL, &pClass, NULL);

            if (SUCCEEDED(hr)) {
                IWbemClassObject* pInParamsDefinition = NULL;
                pClass->GetMethod(
                    bstr_t(L"RequestStateChange"),
                    0, &pInParamsDefinition, NULL);

                IWbemClassObject* pInParams = NULL;
                pInParamsDefinition->SpawnInstance(0, &pInParams);

                // State 2 = Running
                VARIANT varState;
                varState.vt = VT_I4;
                varState.lVal = 2;
                pInParams->Put(L"RequestedState", 0, &varState, 0);

                IWbemClassObject* pOutParams = NULL;
                hr = pSvc->ExecMethod(
                    varPath.bstrVal,
                    bstr_t(L"RequestStateChange"),
                    0, NULL, pInParams, &pOutParams, NULL);

                if (pOutParams) pOutParams->Release();
                VariantClear(&varState);
                pInParams->Release();
                pInParamsDefinition->Release();
                pClass->Release();
            }

            VariantClear(&varPath);
            pVM->Release();
            pEnumerator->Release();
            Cleanup();

            return SUCCEEDED(hr) ? 0 : -4;
        }
        catch (...) {
            return -99;
        }
    }

    void Cleanup() {
        if (pSvc) {
            pSvc->Release();
            pSvc = nullptr;
        }
        if (pLoc) {
            pLoc->Release();
            pLoc = nullptr;
        }
        CoUninitialize();
    }

    const std::wstring& GetLastError() const { return lastError; }
    const std::wstring& GetImportedVmName() const { return importedVmName; }
};

namespace {
    std::wstring TrimWhitespace(const std::wstring& value)
    {
        size_t start = value.find_first_not_of(L" \t\r\n");
        if (start == std::wstring::npos)
        {
            return L"";
        }

        size_t end = value.find_last_not_of(L" \t\r\n");
        return value.substr(start, end - start + 1);
    }

    std::wstring ReadMetadataValue(const fs::path& metadataPath, const wchar_t* key)
    {
        std::wifstream stream(metadataPath);
        if (!stream.is_open())
        {
            return L"";
        }

        std::wstring line;
        while (std::getline(stream, line))
        {
            size_t separatorIndex = line.find(L'=');
            if (separatorIndex == std::wstring::npos)
            {
                continue;
            }

            std::wstring currentKey = TrimWhitespace(line.substr(0, separatorIndex));
            if (_wcsicmp(currentKey.c_str(), key) == 0)
            {
                return TrimWhitespace(line.substr(separatorIndex + 1));
            }
        }

        return L"";
    }

    std::wstring ResolveHyperVImportPath(const wchar_t* backupPath)
    {
        fs::path candidate(backupPath);
        std::error_code ec;
        if (!fs::exists(candidate, ec))
        {
            return L"";
        }

        if (fs::is_directory(candidate, ec))
        {
            fs::path exportPath = candidate / L"Export";
            if (fs::exists(exportPath, ec) && fs::is_directory(exportPath, ec))
            {
                return exportPath.wstring();
            }

            fs::path metadataPath = candidate / L"hyperv_backup_info.txt";
            if (fs::exists(metadataPath, ec))
            {
                std::wstring metadataExportPath = ReadMetadataValue(metadataPath, L"ExportPath");
                if (!metadataExportPath.empty())
                {
                    return metadataExportPath;
                }
            }
        }

        return candidate.wstring();
    }
}

extern "C" {
    BACKUPENGINE_API int RestoreHyperVVM(
        const wchar_t* backupPath,
        const wchar_t* vmName,
        const wchar_t* vmStoragePath,
        bool startAfterRestore,
        ProgressCallback callback) {

        try {
            HyperVRestorer restorer(callback);

            std::wstring importPath = ResolveHyperVImportPath(backupPath);
            if (importPath.empty()) {
                return -98;
            }

            int result = restorer.ImportVM(importPath.c_str(), vmName, vmStoragePath);
            if (result != 0) {
                return result;
            }

            if (startAfterRestore) {
                if (callback) {
                    callback(95, L"Starting VM...");
                }
                const std::wstring& importedVmName = restorer.GetImportedVmName();
                result = restorer.StartVM(importedVmName.empty() ? vmName : importedVmName.c_str());
            }

            return result;
        }
        catch (...) {
            return -99;
        }
    }
}
