// HyperVRestore.cpp
#include "BackupEngine.h"
#include <comdef.h>
#include <Wbemidl.h>
#include <string>

#pragma comment(lib, "wbemuuid.lib")

class HyperVRestorer {
private:
    IWbemServices* pSvc;
    IWbemLocator* pLoc;
    ProgressCallback progressCallback;
    std::wstring lastError;

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
            if (progressCallback) {
                progressCallback(0, L"Initializing Hyper-V connection...");
            }

            HRESULT hr = Initialize();
            if (FAILED(hr)) {
                lastError = L"Failed to initialize Hyper-V connection";
                return -1;
            }

            if (progressCallback) {
                progressCallback(10, L"Getting management service...");
            }

            // Get management service
            IWbemClassObject* pMgmtService = NULL;
            hr = GetManagementService(&pMgmtService);
            if (FAILED(hr)) {
                lastError = L"Failed to get management service";
                Cleanup();
                return -2;
            }

            // Get the __PATH of the management service
            VARIANT varPath;
            hr = pMgmtService->Get(L"__PATH", 0, &varPath, 0, 0);
            if (FAILED(hr)) {
                pMgmtService->Release();
                Cleanup();
                return -3;
            }

            if (progressCallback) {
                progressCallback(20, L"Preparing import operation...");
            }

            // Get ImportSystemDefinition method
            IWbemClassObject* pClass = NULL;
            hr = pSvc->GetObject(
                bstr_t(L"Msvm_VirtualSystemManagementService"),
                0, NULL, &pClass, NULL);

            if (FAILED(hr)) {
                VariantClear(&varPath);
                pMgmtService->Release();
                Cleanup();
                return -4;
            }

            IWbemClassObject* pInParamsDefinition = NULL;
            hr = pClass->GetMethod(
                bstr_t(L"ImportSystemDefinition"),
                0, &pInParamsDefinition, NULL);

            if (FAILED(hr)) {
                pClass->Release();
                VariantClear(&varPath);
                pMgmtService->Release();
                Cleanup();
                return -5;
            }

            IWbemClassObject* pInParams = NULL;
            pInParamsDefinition->SpawnInstance(0, &pInParams);

            if (progressCallback) {
                progressCallback(40, L"Importing VM configuration...");
            }

            // Set import parameters
            VARIANT varBackupPath;
            varBackupPath.vt = VT_BSTR;
            varBackupPath.bstrVal = SysAllocString(backupPath);
            pInParams->Put(L"SourcePath", 0, &varBackupPath, 0);

            VARIANT varStoragePath;
            varStoragePath.vt = VT_BSTR;
            varStoragePath.bstrVal = SysAllocString(vmStoragePath);
            pInParams->Put(L"DestinationPath", 0, &varStoragePath, 0);

            VARIANT varGenerateNewId;
            varGenerateNewId.vt = VT_BOOL;
            varGenerateNewId.boolVal = VARIANT_FALSE; // Keep original ID
            pInParams->Put(L"GenerateNewSystemIdentifier", 0, &varGenerateNewId, 0);

            if (progressCallback) {
                progressCallback(60, L"Executing import...");
            }

            // Execute import
            IWbemClassObject* pOutParams = NULL;
            hr = pSvc->ExecMethod(
                varPath.bstrVal,
                bstr_t(L"ImportSystemDefinition"),
                0, NULL, pInParams, &pOutParams, NULL);

            if (SUCCEEDED(hr) && pOutParams) {
                VARIANT varReturnValue;
                hr = pOutParams->Get(L"ReturnValue", 0, &varReturnValue, NULL, 0);

                if (SUCCEEDED(hr)) {
                    DWORD returnValue = varReturnValue.uintVal;

                    if (returnValue == 0) {
                        // Success
                        if (progressCallback) {
                            progressCallback(90, L"Import successful, finalizing...");
                        }
                    }
                    else if (returnValue == 4096) {
                        // Job started - need to wait for completion
                        VARIANT varJob;
                        hr = pOutParams->Get(L"Job", 0, &varJob, NULL, 0);
                        if (SUCCEEDED(hr)) {
                            // Monitor job progress (simplified here)
                            if (progressCallback) {
                                progressCallback(80, L"Import job in progress...");
                            }
                            VariantClear(&varJob);
                        }
                    }
                    else {
                        lastError = L"Import failed with return code: " +
                            std::to_wstring(returnValue);
                        hr = E_FAIL;
                    }
                    VariantClear(&varReturnValue);
                }
                pOutParams->Release();
            }

            // Cleanup
            VariantClear(&varBackupPath);
            VariantClear(&varStoragePath);
            VariantClear(&varGenerateNewId);
            pInParams->Release();
            pInParamsDefinition->Release();
            pClass->Release();
            VariantClear(&varPath);
            pMgmtService->Release();
            Cleanup();

            if (FAILED(hr)) {
                return -6;
            }

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
};

extern "C" {
    BACKUPENGINE_API int RestoreHyperVVM(
        const wchar_t* backupPath,
        const wchar_t* vmName,
        const wchar_t* vmStoragePath,
        bool startAfterRestore,
        ProgressCallback callback) {

        try {
            HyperVRestorer restorer(callback);

            int result = restorer.ImportVM(backupPath, vmName, vmStoragePath);
            if (result != 0) {
                return result;
            }

            if (startAfterRestore) {
                if (callback) {
                    callback(95, L"Starting VM...");
                }
                result = restorer.StartVM(vmName);
            }

            return result;
        }
        catch (...) {
            return -99;
        }
    }
}
