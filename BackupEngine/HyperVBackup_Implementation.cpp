// HyperVBackup_Implementation.cpp
// Complete implementation of Hyper-V VM backup and cloning

#include "BackupEngine.h"
#include <Windows.h>
#include <comdef.h>
#include <Wbemidl.h>
#include <atlbase.h>
#include <string>
#include <vector>
#include <filesystem>
#include <fstream>
#include <shlwapi.h>
#include <cwchar>

#pragma comment(lib, "wbemuuid.lib")
#pragma comment(lib, "shlwapi.lib")

namespace fs = std::filesystem;

extern void SetLastErrorMessage(const std::wstring& error);

namespace {
    struct HyperVBackupPointInfo {
        std::wstring Type;
        std::wstring PointId;
        std::wstring ParentPath;
        std::wstring VmName;
        std::wstring ExportPath;
        bool IsValid = false;
    };

    constexpr wchar_t HyperVMetadataFileName[] = L"hyperv_backup_info.txt";

    std::wstring TrimWhitespace(const std::wstring& value) {
        size_t start = value.find_first_not_of(L" \t\r\n");
        if (start == std::wstring::npos) {
            return L"";
        }

        size_t end = value.find_last_not_of(L" \t\r\n");
        return value.substr(start, end - start + 1);
    }

    std::wstring GetTimestampId() {
        SYSTEMTIME localTime = {};
        GetLocalTime(&localTime);

        wchar_t buffer[32] = {};
        swprintf_s(
            buffer,
            L"%04u%02u%02u_%02u%02u%02u",
            localTime.wYear,
            localTime.wMonth,
            localTime.wDay,
            localTime.wHour,
            localTime.wMinute,
            localTime.wSecond);

        return buffer;
    }

    std::wstring CombinePath(const std::wstring& root, const std::wstring& child) {
        return (fs::path(root) / child).wstring();
    }

    std::wstring GetMetadataPath(const std::wstring& backupPointPath) {
        return CombinePath(backupPointPath, HyperVMetadataFileName);
    }

    std::wstring GetExportRootPath(const std::wstring& backupPointPath) {
        return CombinePath(backupPointPath, L"Export");
    }

    bool IsHyperVBackupPointDirectory(const fs::path& path) {
        if (!fs::is_directory(path)) {
            return false;
        }

        std::error_code ec;
        return fs::exists(path / HyperVMetadataFileName, ec);
    }

    bool TryReadHyperVBackupPointInfo(const std::wstring& backupPointPath, HyperVBackupPointInfo& info) {
        std::wifstream stream(GetMetadataPath(backupPointPath));
        if (!stream.is_open()) {
            return false;
        }

        info = {};
        info.ExportPath = GetExportRootPath(backupPointPath);

        std::wstring line;
        while (std::getline(stream, line)) {
            size_t separatorIndex = line.find(L'=');
            if (separatorIndex == std::wstring::npos) {
                continue;
            }

            std::wstring key = TrimWhitespace(line.substr(0, separatorIndex));
            std::wstring value = TrimWhitespace(line.substr(separatorIndex + 1));

            if (_wcsicmp(key.c_str(), L"Type") == 0) {
                info.Type = value;
            }
            else if (_wcsicmp(key.c_str(), L"PointId") == 0) {
                info.PointId = value;
            }
            else if (_wcsicmp(key.c_str(), L"ParentPath") == 0) {
                info.ParentPath = value;
            }
            else if (_wcsicmp(key.c_str(), L"VmName") == 0) {
                info.VmName = value;
            }
        }

        info.IsValid = !info.Type.empty() && !info.PointId.empty();
        return info.IsValid;
    }

    bool WriteHyperVBackupPointInfo(
        const std::wstring& backupPointPath,
        const std::wstring& backupType,
        const std::wstring& pointId,
        const std::wstring& vmName,
        const std::wstring& parentPath) {

        std::wofstream stream(GetMetadataPath(backupPointPath), std::ios::trunc);
        if (!stream.is_open()) {
            return false;
        }

        stream << L"Type=" << backupType << L'\n';
        stream << L"PointId=" << pointId << L'\n';
        stream << L"VmName=" << vmName << L'\n';
        stream << L"ExportPath=" << GetExportRootPath(backupPointPath) << L'\n';
        if (!parentPath.empty()) {
            stream << L"ParentPath=" << parentPath << L'\n';
        }

        return stream.good();
    }

    std::wstring FindNewestHyperVBackupPoint(const std::wstring& backupRootPath) {
        if (!fs::exists(backupRootPath) || !fs::is_directory(backupRootPath)) {
            return L"";
        }

        std::vector<fs::directory_entry> entries;
        for (const auto& entry : fs::directory_iterator(backupRootPath)) {
            if (IsHyperVBackupPointDirectory(entry.path())) {
                entries.push_back(entry);
            }
        }

        if (entries.empty()) {
            return L"";
        }

        std::sort(
            entries.begin(),
            entries.end(),
            [](const fs::directory_entry& left, const fs::directory_entry& right) {
                return fs::last_write_time(left) > fs::last_write_time(right);
            });

        return entries.front().path().wstring();
    }

    std::wstring FindLatestHyperVBackupPointByType(const std::wstring& backupRootPath, const std::wstring& desiredType) {
        if (!fs::exists(backupRootPath) || !fs::is_directory(backupRootPath)) {
            return L"";
        }

        std::vector<fs::directory_entry> entries;
        for (const auto& entry : fs::directory_iterator(backupRootPath)) {
            if (!IsHyperVBackupPointDirectory(entry.path())) {
                continue;
            }

            HyperVBackupPointInfo info;
            if (TryReadHyperVBackupPointInfo(entry.path().wstring(), info) && _wcsicmp(info.Type.c_str(), desiredType.c_str()) == 0) {
                entries.push_back(entry);
            }
        }

        if (entries.empty()) {
            return L"";
        }

        std::sort(
            entries.begin(),
            entries.end(),
            [](const fs::directory_entry& left, const fs::directory_entry& right) {
                return fs::last_write_time(left) > fs::last_write_time(right);
            });

        return entries.front().path().wstring();
    }

    int ExecuteHyperVBackupPoint(
        const wchar_t* vmName,
        const wchar_t* backupRootPath,
        const std::wstring& backupType,
        const std::wstring& parentPath,
        ProgressCallback callback);
}

// Helper to execute WMI method
HRESULT ExecuteWMIMethod(IWbemServices* pSvc, const std::wstring& objectPath,
    const std::wstring& methodName, IWbemClassObject* pInParams,
    IWbemClassObject** ppOutParams) {
    
    return pSvc->ExecMethod(
        CComBSTR(objectPath.c_str()),
        CComBSTR(methodName.c_str()),
        0,
        NULL,
        pInParams,
        ppOutParams,
        NULL);
}

std::wstring GetWmiErrorMessage(HRESULT hr) {
    _com_error error(hr);
    const wchar_t* description = error.ErrorMessage();
    if (description == nullptr || *description == L'\0') {
        return L"Unknown WMI error";
    }

    return description;
}

bool BuildHyperVExportSettingData(
    IWbemServices* pSvc,
    IWbemClassObject* pClass,
    const std::wstring& backupType,
    std::wstring& exportSettingData,
    std::wstring& errorMessage) {

    CComPtr<IWbemClassObject> pSettingClass;
    HRESULT hr = pSvc->GetObject(
        CComBSTR(L"Msvm_VirtualSystemExportSettingData"),
        0,
        NULL,
        &pSettingClass,
        NULL);

    if (FAILED(hr)) {
        errorMessage = L"Failed to get export setting data class: " + GetWmiErrorMessage(hr);
        return false;
    }

    CComPtr<IWbemClassObject> pSettingInstance;
    hr = pSettingClass->SpawnInstance(0, &pSettingInstance);
    if (FAILED(hr)) {
        errorMessage = L"Failed to create export setting data instance: " + GetWmiErrorMessage(hr);
        return false;
    }

    CComVariant varCopyVmStorage(VARIANT_TRUE);
    hr = pSettingInstance->Put(L"CopyVmStorage", 0, &varCopyVmStorage, 0);
    if (FAILED(hr)) {
        errorMessage = L"Failed to set CopyVmStorage export setting: " + GetWmiErrorMessage(hr);
        return false;
    }

    CComVariant varCopyRuntime(VARIANT_FALSE);
    hr = pSettingInstance->Put(L"CopyVmRuntimeInformation", 0, &varCopyRuntime, 0);
    if (FAILED(hr)) {
        errorMessage = L"Failed to set CopyVmRuntimeInformation export setting: " + GetWmiErrorMessage(hr);
        return false;
    }

    CComVariant varCaptureLiveState(static_cast<unsigned char>(0));
    hr = pSettingInstance->Put(L"CaptureLiveState", 0, &varCaptureLiveState, 0);
    if (FAILED(hr)) {
        errorMessage = L"Failed to set CaptureLiveState export setting: " + GetWmiErrorMessage(hr);
        return false;
    }

    CComVariant varCopySnapshotConfiguration(static_cast<unsigned char>(1));
    hr = pSettingInstance->Put(L"CopySnapshotConfiguration", 0, &varCopySnapshotConfiguration, 0);
    if (FAILED(hr)) {
        errorMessage = L"Failed to set CopySnapshotConfiguration export setting: " + GetWmiErrorMessage(hr);
        return false;
    }

    CComVariant varCreateSubdirectory(VARIANT_TRUE);
    hr = pSettingInstance->Put(L"CreateVmExportSubdirectory", 0, &varCreateSubdirectory, 0);
    if (FAILED(hr)) {
        errorMessage = L"Failed to set CreateVmExportSubdirectory export setting: " + GetWmiErrorMessage(hr);
        return false;
    }

    if (_wcsicmp(backupType.c_str(), L"Differential") == 0) {
        CComVariant varBackupIntent(static_cast<unsigned char>(0));
        hr = pSettingInstance->Put(L"BackupIntent", 0, &varBackupIntent, 0);
        if (FAILED(hr)) {
            errorMessage = L"Failed to set BackupIntent export setting: " + GetWmiErrorMessage(hr);
            return false;
        }
    }

    BSTR bstrObjectText = NULL;
    hr = pSettingInstance->GetObjectText(0, &bstrObjectText);
    if (FAILED(hr) || bstrObjectText == NULL) {
        if (bstrObjectText != NULL) {
            SysFreeString(bstrObjectText);
        }

        errorMessage = L"Failed to serialize export setting data: " + GetWmiErrorMessage(hr);
        return false;
    }

    exportSettingData.assign(bstrObjectText, SysStringLen(bstrObjectText));
    SysFreeString(bstrObjectText);
    return true;
}

// Get Hyper-V management service
HRESULT GetManagementService(IWbemServices* pSvc, IWbemClassObject** ppManagementService, std::wstring& servicePath) {
    CComPtr<IEnumWbemClassObject> pEnumerator;
    
    HRESULT hr = pSvc->ExecQuery(
        CComBSTR(L"WQL"),
        CComBSTR(L"SELECT * FROM Msvm_VirtualSystemManagementService"),
        WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
        NULL,
        &pEnumerator);

    if (FAILED(hr)) return hr;

    CComPtr<IWbemClassObject> pclsObj;
    ULONG uReturn = 0;
    
    hr = pEnumerator->Next(WBEM_INFINITE, 1, &pclsObj, &uReturn);
    if (uReturn == 0) return E_FAIL;

    // Get __PATH
    CComVariant varPath;
    hr = pclsObj->Get(L"__PATH", 0, &varPath, 0, 0);
    if (SUCCEEDED(hr)) {
        servicePath = varPath.bstrVal;
        *ppManagementService = pclsObj.Detach();
    }

    return hr;
}

// Get VM by name
HRESULT GetVMByName(IWbemServices* pSvc, const wchar_t* vmName, IWbemClassObject** ppVM, std::wstring& vmPath) {
    wchar_t query[512];
    swprintf_s(query, L"SELECT * FROM Msvm_ComputerSystem WHERE ElementName='%s' AND Caption='Virtual Machine'", vmName);

    CComPtr<IEnumWbemClassObject> pEnumerator;
    HRESULT hr = pSvc->ExecQuery(
        CComBSTR(L"WQL"),
        CComBSTR(query),
        WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
        NULL,
        &pEnumerator);

    if (FAILED(hr)) return hr;

    CComPtr<IWbemClassObject> pclsObj;
    ULONG uReturn = 0;
    
    hr = pEnumerator->Next(WBEM_INFINITE, 1, &pclsObj, &uReturn);
    if (uReturn == 0) {
        SetLastErrorMessage(std::wstring(L"Virtual machine '") + vmName + L"' not found");
        return E_FAIL;
    }

    // Get __PATH
    CComVariant varPath;
    hr = pclsObj->Get(L"__PATH", 0, &varPath, 0, 0);
    if (SUCCEEDED(hr)) {
        vmPath = varPath.bstrVal;
        *ppVM = pclsObj.Detach();
    }

    return hr;
}

// Export VM (Hyper-V native export)
extern "C" BACKUPENGINE_API int BackupHyperVVM(
    const wchar_t* vmName,
    const wchar_t* destPath,
    ProgressCallback callback) {

    return ExecuteHyperVBackupPoint(vmName, destPath, L"Full", L"", callback);
}

extern "C" BACKUPENGINE_API int BackupHyperVVMIncremental(
    const wchar_t* vmName,
    const wchar_t* destPath,
    ProgressCallback callback) {

    if (!vmName || !destPath) {
        SetLastErrorMessage(L"Invalid parameters");
        return -1;
    }

    std::wstring latestPoint = FindNewestHyperVBackupPoint(destPath);
    return ExecuteHyperVBackupPoint(vmName, destPath, latestPoint.empty() ? L"Full" : L"Incremental", latestPoint, callback);
}

extern "C" BACKUPENGINE_API int BackupHyperVVMDifferential(
    const wchar_t* vmName,
    const wchar_t* destPath,
    ProgressCallback callback) {

    if (!vmName || !destPath) {
        SetLastErrorMessage(L"Invalid parameters");
        return -1;
    }

    std::wstring latestFullPoint = FindLatestHyperVBackupPointByType(destPath, L"Full");
    return ExecuteHyperVBackupPoint(vmName, destPath, latestFullPoint.empty() ? L"Full" : L"Differential", latestFullPoint, callback);
}

namespace {
    int ExecuteHyperVBackupPoint(
        const wchar_t* vmName,
        const wchar_t* backupRootPath,
        const std::wstring& backupType,
        const std::wstring& parentPath,
        ProgressCallback callback) {

        if (!vmName || !backupRootPath) {
            SetLastErrorMessage(L"Invalid parameters");
            return -1;
        }

        std::wstring backupRoot = backupRootPath;
        std::wstring pointId = GetTimestampId();
        std::wstring backupPointName = backupType + L"_" + pointId + L".ssb";
        std::wstring backupPointPath = CombinePath(backupRoot, backupPointName);
        std::wstring exportPath = GetExportRootPath(backupPointPath);

        HRESULT hr = CoInitializeEx(0, COINIT_MULTITHREADED);
        bool coinitCalled = SUCCEEDED(hr);

        CComPtr<IWbemLocator> pLoc;
        CComPtr<IWbemServices> pSvc;

    try {
        // Create WMI locator
        hr = CoCreateInstance(
            CLSID_WbemLocator,
            0,
            CLSCTX_INPROC_SERVER,
            IID_IWbemLocator,
            (LPVOID*)&pLoc);

        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to create WMI locator");
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        // Connect to Hyper-V namespace
        hr = pLoc->ConnectServer(
            CComBSTR(L"ROOT\\virtualization\\v2"),
            NULL,
            NULL,
            0,
            NULL,
            0,
            0,
            &pSvc);

        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to connect to Hyper-V WMI namespace. Is Hyper-V installed?");
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        // Set security levels
        hr = CoSetProxyBlanket(
            pSvc,
            RPC_C_AUTHN_WINNT,
            RPC_C_AUTHZ_NONE,
            NULL,
            RPC_C_AUTHN_LEVEL_CALL,
            RPC_C_IMP_LEVEL_IMPERSONATE,
            NULL,
            EOAC_NONE);

        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to set WMI security");
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        if (callback) callback(10, L"Connecting to Hyper-V...");

        // Get VM
        CComPtr<IWbemClassObject> pVM;
        std::wstring vmPath;
        hr = GetVMByName(pSvc, vmName, &pVM, vmPath);
        
        if (FAILED(hr)) {
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        if (callback) callback(20, L"Found virtual machine");

        // Get management service
        CComPtr<IWbemClassObject> pMgmtService;
        std::wstring mgmtPath;
        hr = GetManagementService(pSvc, &pMgmtService, mgmtPath);
        
        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to get Hyper-V management service");
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        if (callback) {
            std::wstring status = L"Preparing " + backupType + L" Hyper-V backup point...";
            callback(30, status.c_str());
        }

        // Create destination directory
        try {
            fs::create_directories(exportPath);
        }
        catch (const std::exception& e) {
            std::wstring error = L"Failed to create destination directory: ";
            error += std::wstring(e.what(), e.what() + strlen(e.what()));
            SetLastErrorMessage(error);
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        // Get ExportSystemDefinition method
        CComPtr<IWbemClassObject> pClass;
        hr = pSvc->GetObject(
            CComBSTR(L"Msvm_VirtualSystemManagementService"),
            0,
            NULL,
            &pClass,
            NULL);

        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to get management service class");
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        CComPtr<IWbemClassObject> pInParamsDefinition;
        CComPtr<IWbemClassObject> pInParams;

        hr = pClass->GetMethod(CComBSTR(L"ExportSystemDefinition"), 0, &pInParamsDefinition, NULL);
        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to get ExportSystemDefinition method");
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        hr = pInParamsDefinition->SpawnInstance(0, &pInParams);
        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to spawn parameters instance");
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        // Set ComputerSystem parameter
        CComVariant varVM(vmPath.c_str());
        hr = pInParams->Put(L"ComputerSystem", 0, &varVM, 0);
        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to set ComputerSystem parameter");
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        // Set ExportDirectory parameter
        CComVariant varPath(exportPath.c_str());
        hr = pInParams->Put(L"ExportDirectory", 0, &varPath, 0);
        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to set ExportDirectory parameter");
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        std::wstring exportSettingData;
        std::wstring exportSettingError;
        if (!BuildHyperVExportSettingData(pSvc, pClass, backupType, exportSettingData, exportSettingError)) {
            SetLastErrorMessage(exportSettingError);
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        CComVariant varExportSettingData(exportSettingData.c_str());
        hr = pInParams->Put(L"ExportSettingData", 0, &varExportSettingData, 0);
        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to set ExportSettingData parameter: " + GetWmiErrorMessage(hr));
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        if (callback) {
            std::wstring status = L"Starting " + backupType + L" export...";
            callback(40, status.c_str());
        }

        // Execute export
        CComPtr<IWbemClassObject> pOutParams;
        hr = pSvc->ExecMethod(
            CComBSTR(mgmtPath.c_str()),
            CComBSTR(L"ExportSystemDefinition"),
            0,
            NULL,
            pInParams,
            &pOutParams,
            NULL);

        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to execute export method: " + GetWmiErrorMessage(hr));
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        // Get return value
        CComVariant varReturnValue;
        hr = pOutParams->Get(L"ReturnValue", 0, &varReturnValue, NULL, 0);
        
        if (SUCCEEDED(hr)) {
            UINT32 returnValue = varReturnValue.uintVal;
            
            if (returnValue == 0) {
                // Success
                if (callback) callback(95, L"Finalizing Hyper-V backup metadata...");
            }
            else if (returnValue == 4096) {
                // Job started - need to wait for completion
                CComVariant varJob;
                hr = pOutParams->Get(L"Job", 0, &varJob, NULL, 0);
                
                if (SUCCEEDED(hr) && varJob.vt == VT_UNKNOWN) {
                    CComPtr<IWbemClassObject> pJob;
                    hr = varJob.punkVal->QueryInterface(&pJob);
                    
                    if (SUCCEEDED(hr)) {
                        // Poll job status
                        bool jobComplete = false;
                        int progress = 40;
                        
                        while (!jobComplete) {
                            Sleep(1000);
                            
                            CComVariant varJobState;
                            hr = pJob->Get(L"JobState", 0, &varJobState, NULL, 0);
                            
                            if (SUCCEEDED(hr)) {
                                UINT32 jobState = varJobState.uintVal;
                                
                                // 7 = Completed, 10 = Failed, 32768 = CompletedWithWarnings
                                if (jobState == 7 || jobState == 32768) {
                                    jobComplete = true;
                                    if (callback) callback(95, L"Export completed. Finalizing metadata...");
                                }
                                else if (jobState == 10) {
                                    SetLastErrorMessage(L"Export job failed");
                                    if (coinitCalled) CoUninitialize();
                                    return -1;
                                }
                                else {
                                    // Still running
                                    progress = min(95, progress + 10);
                                    if (callback) callback(progress, L"Exporting VM...");
                                }
                            }
                            
                            // Refresh job object
                            pJob->Get(L"__PATH", 0, &varJobState, NULL, 0);
                            CComPtr<IWbemClassObject> pJobRefresh;
                            pSvc->GetObject(CComBSTR(varJobState.bstrVal), 0, NULL, &pJobRefresh, NULL);
                            pJob = pJobRefresh;
                        }
                    }
                }
                else {
                    SetLastErrorMessage(L"Failed to get export job");
                    if (coinitCalled) CoUninitialize();
                    return -1;
                }
            }
            else {
                wchar_t error[256];
                swprintf_s(error, L"Export failed with code: %u", returnValue);
                SetLastErrorMessage(error);
                if (coinitCalled) CoUninitialize();
                return -1;
            }
        }

        if (!WriteHyperVBackupPointInfo(backupPointPath, backupType, pointId, vmName, parentPath)) {
            SetLastErrorMessage(L"Failed to write Hyper-V backup metadata");
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        if (coinitCalled) CoUninitialize();
        if (callback) {
            std::wstring status = backupType + L" Hyper-V backup completed successfully";
            callback(100, status.c_str());
        }
        return 0;
    }
    catch (const std::exception& e) {
        std::wstring error = L"Exception during Hyper-V export: ";
        error += std::wstring(e.what(), e.what() + strlen(e.what()));
        SetLastErrorMessage(error);
        if (coinitCalled) CoUninitialize();
        return -1;
    }
    catch (...) {
        SetLastErrorMessage(L"Unknown exception during Hyper-V export");
        if (coinitCalled) CoUninitialize();
        return -1;
    }
    }
}
