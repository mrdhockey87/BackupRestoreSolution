#include <comdef.h>
#include <Wbemidl.h>

#pragma comment(lib, "wbemuuid.lib")

class HyperVManager {
private:
    IWbemServices* pSvc;
    IWbemLocator* pLoc;

public:
    HRESULT Initialize() {
        CoInitializeEx(0, COINIT_MULTITHREADED);

        HRESULT hr = CoCreateInstance(
            CLSID_WbemLocator, 0,
            CLSCTX_INPROC_SERVER,
            IID_IWbemLocator,
            (LPVOID*)&pLoc);

        if (FAILED(hr)) return hr;

        // Connect to Hyper-V namespace
        hr = pLoc->ConnectServer(
            _bstr_t(L"ROOT\\virtualization\\v2"),
            NULL, NULL, 0, NULL, 0, 0, &pSvc);

        return hr;
    }

    HRESULT ExportVM(const wchar_t* vmName,
        const wchar_t* exportPath) {
        // Get VM object
        IEnumWbemClassObject* pEnumerator = NULL;
        wchar_t query[512];
        swprintf_s(query,
            L"SELECT * FROM Msvm_ComputerSystem WHERE ElementName='%s'",
            vmName);

        HRESULT hr = pSvc->ExecQuery(
            bstr_t("WQL"),
            bstr_t(query),
            WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
            NULL,
            &pEnumerator);

        if (FAILED(hr)) return hr;

        IWbemClassObject* pclsObj = NULL;
        ULONG uReturn = 0;

        hr = pEnumerator->Next(WBEM_INFINITE, 1, &pclsObj, &uReturn);

        if (uReturn == 0) return E_FAIL;

        // Get export service
        IWbemClassObject* pClass = NULL;
        hr = pSvc->GetObject(
            bstr_t(L"Msvm_VirtualSystemManagementService"),
            0, NULL, &pClass, NULL);

        if (FAILED(hr)) return hr;

        IWbemClassObject* pInParamsDefinition = NULL;
        IWbemClassObject* pInParams = NULL;

        pClass->GetMethod(
            bstr_t(L"ExportSystemDefinition"),
            0, &pInParamsDefinition, NULL);
        pInParamsDefinition->SpawnInstance(0, &pInParams);

        // Set parameters
        VARIANT varSystem;
        pclsObj->Get(L"__PATH", 0, &varSystem, 0, 0);
        pInParams->Put(L"ComputerSystem", 0, &varSystem, 0);

        VARIANT varPath;
        varPath.vt = VT_BSTR;
        varPath.bstrVal = SysAllocString(exportPath);
        pInParams->Put(L"ExportDirectory", 0, &varPath, 0);

        // Execute export
        IWbemClassObject* pOutParams = NULL;
        hr = pSvc->ExecMethod(
            varSystem.bstrVal,
            bstr_t(L"ExportSystemDefinition"),
            0, NULL, pInParams, &pOutParams, NULL);

        // Cleanup
        VariantClear(&varSystem);
        VariantClear(&varPath);
        pInParams->Release();
        pInParamsDefinition->Release();
        pClass->Release();
        pclsObj->Release();
        pEnumerator->Release();

        return hr;
    }

    void Cleanup() {
        if (pSvc) pSvc->Release();
        if (pLoc) pLoc->Release();
        CoUninitialize();
    }
};