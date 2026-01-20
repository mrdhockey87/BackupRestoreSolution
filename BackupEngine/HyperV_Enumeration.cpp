// HyperV_Enumeration.cpp - Enumerate Hyper-V virtual machines
#include "BackupEngine.h"
#include <Windows.h>
#include <comdef.h>
#include <Wbemidl.h>
#include <string>
#include <sstream>

#pragma comment(lib, "wbemuuid.lib")

extern void SetLastErrorMessage(const std::wstring& error);

extern "C" {

    BACKUPENGINE_API int EnumerateHyperVMachines(wchar_t* buffer, int bufferSize) {
        if (!buffer || bufferSize <= 0) {
            SetLastErrorMessage(L"Invalid buffer");
            return -1;
        }

        HRESULT hr = CoInitializeEx(0, COINIT_MULTITHREADED);
        bool comInitialized = SUCCEEDED(hr);

        IWbemLocator* pLoc = nullptr;
        IWbemServices* pSvc = nullptr;
        IEnumWbemClassObject* pEnumerator = nullptr;

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
                if (comInitialized) CoUninitialize();
                return -2;
            }

            // Connect to Hyper-V WMI namespace
            // Try v2 first (Windows Server 2012+), then v1 (Windows Server 2008 R2)
            hr = pLoc->ConnectServer(
                _bstr_t(L"ROOT\\virtualization\\v2"),
                nullptr, nullptr, 0, 0, 0, 0, &pSvc);

            if (FAILED(hr)) {
                // Try v1 namespace
                hr = pLoc->ConnectServer(
                    _bstr_t(L"ROOT\\virtualization"),
                    nullptr, nullptr, 0, 0, 0, 0, &pSvc);

                if (FAILED(hr)) {
                    SetLastErrorMessage(L"Failed to connect to Hyper-V - ensure Hyper-V role is installed");
                    pLoc->Release();
                    if (comInitialized) CoUninitialize();
                    return -3;
                }
            }

            // Set security levels
            hr = CoSetProxyBlanket(
                pSvc,
                RPC_C_AUTHN_WINNT,
                RPC_C_AUTHZ_NONE,
                nullptr,
                RPC_C_AUTHN_LEVEL_CALL,
                RPC_C_IMP_LEVEL_IMPERSONATE,
                nullptr,
                EOAC_NONE);

            if (FAILED(hr)) {
                SetLastErrorMessage(L"Failed to set proxy blanket");
                pSvc->Release();
                pLoc->Release();
                if (comInitialized) CoUninitialize();
                return -4;
            }

            // Query for virtual machines
            hr = pSvc->ExecQuery(
                _bstr_t(L"WQL"),
                _bstr_t(L"SELECT * FROM Msvm_ComputerSystem WHERE Caption='Virtual Machine'"),
                WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
                nullptr,
                &pEnumerator);

            if (FAILED(hr)) {
                SetLastErrorMessage(L"Failed to query virtual machines");
                pSvc->Release();
                pLoc->Release();
                if (comInitialized) CoUninitialize();
                return -5;
            }

            // Enumerate VMs
            std::wostringstream result;
            IWbemClassObject* pclsObj = nullptr;
            ULONG uReturn = 0;

            while (pEnumerator) {
                hr = pEnumerator->Next(WBEM_INFINITE, 1, &pclsObj, &uReturn);

                if (uReturn == 0) break;

                VARIANT vtProp;
                VariantInit(&vtProp);

                // Get VM name (ElementName property)
                hr = pclsObj->Get(L"ElementName", 0, &vtProp, 0, 0);
                if (SUCCEEDED(hr) && vtProp.vt == VT_BSTR) {
                    result << vtProp.bstrVal;

                    // Get VM state
                    VARIANT vtState;
                    VariantInit(&vtState);
                    hr = pclsObj->Get(L"EnabledState", 0, &vtState, 0, 0);
                    if (SUCCEEDED(hr) && vtState.vt == VT_I4) {
                        // 2 = Running, 3 = Off, 32768 = Paused, 32769 = Saved
                        switch (vtState.intVal) {
                        case 2:
                            result << L" (Running)";
                            break;
                        case 3:
                            result << L" (Off)";
                            break;
                        case 32768:
                            result << L" (Paused)";
                            break;
                        case 32769:
                            result << L" (Saved)";
                            break;
                        default:
                            result << L" (Unknown State)";
                            break;
                        }
                    }
                    VariantClear(&vtState);

                    result << L"\n";
                }

                VariantClear(&vtProp);
                pclsObj->Release();
            }

            // Cleanup
            if (pEnumerator) pEnumerator->Release();
            if (pSvc) pSvc->Release();
            if (pLoc) pLoc->Release();
            if (comInitialized) CoUninitialize();

            // Copy result to buffer
            std::wstring resultStr = result.str();
            if (resultStr.empty()) {
                SetLastErrorMessage(L"No virtual machines found");
                return 0; // Not an error, just no VMs
            }

            if (resultStr.length() >= (size_t)bufferSize) {
                SetLastErrorMessage(L"Buffer too small");
                return -6;
            }

            wcscpy_s(buffer, bufferSize, resultStr.c_str());
            return 0;
        }
        catch (...) {
            if (pEnumerator) pEnumerator->Release();
            if (pSvc) pSvc->Release();
            if (pLoc) pLoc->Release();
            if (comInitialized) CoUninitialize();

            SetLastErrorMessage(L"Exception in EnumerateHyperVMachines");
            return -99;
        }
    }
}
