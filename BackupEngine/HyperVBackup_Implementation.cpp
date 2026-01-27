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

#pragma comment(lib, "wbemuuid.lib")
#pragma comment(lib, "shlwapi.lib")

namespace fs = std::filesystem;

extern void SetLastErrorMessage(const std::wstring& error);

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

    if (!vmName || !destPath) {
        SetLastErrorMessage(L"Invalid parameters");
        return -1;
    }

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

        if (callback) callback(30, L"Preparing export...");

        // Create destination directory
        try {
            fs::create_directories(destPath);
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
        CComVariant varPath(destPath);
        hr = pInParams->Put(L"ExportDirectory", 0, &varPath, 0);
        if (FAILED(hr)) {
            SetLastErrorMessage(L"Failed to set ExportDirectory parameter");
            if (coinitCalled) CoUninitialize();
            return -1;
        }

        // Set CopyVmStorage (copy VHD files)
        CComVariant varCopyVmStorage(true);
        pInParams->Put(L"CopyVmStorage", 0, &varCopyVmStorage, 0);

        // Set CopyVmRuntimeInformation (copy snapshots)
        CComVariant varCopyRuntime(true);
        pInParams->Put(L"CopyVmRuntimeInformation", 0, &varCopyRuntime, 0);

        // Set CreateVmExportSubdirectory
        CComVariant varSubdir(true);
        pInParams->Put(L"CreateVmExportSubdirectory", 0, &varSubdir, 0);

        if (callback) callback(40, L"Starting export...");

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
            SetLastErrorMessage(L"Failed to execute export method");
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
                if (callback) callback(100, L"Export completed successfully");
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
                                    if (callback) callback(100, L"Export completed");
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

        if (coinitCalled) CoUninitialize();
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
