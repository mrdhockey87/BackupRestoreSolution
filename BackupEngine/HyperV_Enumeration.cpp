// HyperV_Enumeration.cpp - Enumerate Hyper-V virtual machines
#include "BackupEngine.h"
#include <Windows.h>
#include <comdef.h>
#include <Wbemidl.h>
#include <string>
#include <sstream>
#include <vector>

#pragma comment(lib, "wbemuuid.lib")

extern void SetLastErrorMessage(const std::wstring& error);

namespace {
    HRESULT ConnectToHyperVNamespace(IWbemLocator* pLoc, IWbemServices** pSvc)
    {
        HRESULT hr = pLoc->ConnectServer(
            _bstr_t(L"ROOT\\virtualization\\v2"),
            nullptr, nullptr, 0, 0, 0, 0, pSvc);

        if (FAILED(hr))
        {
            hr = pLoc->ConnectServer(
                _bstr_t(L"ROOT\\virtualization"),
                nullptr, nullptr, 0, 0, 0, 0, pSvc);
        }

        return hr;
    }

    HRESULT ApplyWmiSecurity(IWbemServices* pSvc)
    {
        return CoSetProxyBlanket(
            pSvc,
            RPC_C_AUTHN_WINNT,
            RPC_C_AUTHZ_NONE,
            nullptr,
            RPC_C_AUTHN_LEVEL_CALL,
            RPC_C_IMP_LEVEL_IMPERSONATE,
            nullptr,
            EOAC_NONE);
    }

    bool AppendResultToBuffer(const std::wstring& value, wchar_t* buffer, int bufferSize)
    {
        if (value.empty())
        {
            if (bufferSize > 0)
            {
                buffer[0] = L'\0';
            }

            return true;
        }

        if (value.length() >= static_cast<size_t>(bufferSize))
        {
            SetLastErrorMessage(L"Buffer too small");
            return false;
        }

        wcscpy_s(buffer, bufferSize, value.c_str());
        return true;
    }

    std::wstring GetVariantString(IWbemClassObject* instance, const wchar_t* propertyName)
    {
        VARIANT value;
        VariantInit(&value);

        std::wstring result;
        HRESULT hr = instance->Get(propertyName, 0, &value, 0, 0);
        if (SUCCEEDED(hr) && value.vt == VT_BSTR && value.bstrVal != nullptr)
        {
            result = value.bstrVal;
        }

        VariantClear(&value);
        return result;
    }

    void AppendVmStateSuffix(IWbemClassObject* instance, std::wostringstream& result)
    {
        VARIANT vtState;
        VariantInit(&vtState);

        HRESULT hr = instance->Get(L"EnabledState", 0, &vtState, 0, 0);
        if (SUCCEEDED(hr) && vtState.vt == VT_I4)
        {
            switch (vtState.intVal)
            {
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
    }

    std::vector<std::wstring> GetHostResourceValues(IWbemClassObject* instance)
    {
        std::vector<std::wstring> hostResources;

        VARIANT value;
        VariantInit(&value);
        HRESULT hr = instance->Get(L"HostResource", 0, &value, 0, 0);
        if (SUCCEEDED(hr) && (value.vt & VT_ARRAY) && value.parray != nullptr)
        {
            LONG lowerBound = 0;
            LONG upperBound = -1;
            if (SUCCEEDED(SafeArrayGetLBound(value.parray, 1, &lowerBound)) &&
                SUCCEEDED(SafeArrayGetUBound(value.parray, 1, &upperBound)))
            {
                for (LONG index = lowerBound; index <= upperBound; ++index)
                {
                    BSTR entry = nullptr;
                    if (SUCCEEDED(SafeArrayGetElement(value.parray, &index, &entry)) && entry != nullptr)
                    {
                        hostResources.emplace_back(entry, SysStringLen(entry));
                        SysFreeString(entry);
                    }
                }
            }
        }

        VariantClear(&value);
        return hostResources;
    }

    bool IsVirtualDiskResource(IWbemClassObject* instance)
    {
        std::wstring resourceSubType = GetVariantString(instance, L"ResourceSubType");
        return resourceSubType.find(L"Virtual Hard Disk") != std::wstring::npos ||
               resourceSubType.find(L"Microsoft:Hyper-V:Virtual Hard Disk") != std::wstring::npos;
    }
}

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

            hr = ConnectToHyperVNamespace(pLoc, &pSvc);
            if (FAILED(hr)) {
                SetLastErrorMessage(L"Failed to connect to Hyper-V - ensure Hyper-V role is installed");
                pLoc->Release();
                if (comInitialized) CoUninitialize();
                return -3;
            }

            hr = ApplyWmiSecurity(pSvc);
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

                    AppendVmStateSuffix(pclsObj, result);

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

            if (!AppendResultToBuffer(resultStr, buffer, bufferSize)) {
                return -6;
            }

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

    BACKUPENGINE_API int EnumerateHyperVVirtualMachineDisks(wchar_t* buffer, int bufferSize) {
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

            hr = ConnectToHyperVNamespace(pLoc, &pSvc);
            if (FAILED(hr)) {
                SetLastErrorMessage(L"Failed to connect to Hyper-V - ensure Hyper-V role is installed");
                pLoc->Release();
                if (comInitialized) CoUninitialize();
                return -3;
            }

            hr = ApplyWmiSecurity(pSvc);
            if (FAILED(hr)) {
                SetLastErrorMessage(L"Failed to set proxy blanket");
                pSvc->Release();
                pLoc->Release();
                if (comInitialized) CoUninitialize();
                return -4;
            }

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

            std::wostringstream result;
            IWbemClassObject* pclsObj = nullptr;
            ULONG uReturn = 0;

            while (pEnumerator) {
                hr = pEnumerator->Next(WBEM_INFINITE, 1, &pclsObj, &uReturn);
                if (uReturn == 0) break;

                std::wstring vmName = GetVariantString(pclsObj, L"ElementName");
                std::wstring vmPath = GetVariantString(pclsObj, L"__PATH");
                if (!vmName.empty() && !vmPath.empty()) {
                    std::wostringstream vmDisplayNameBuilder;
                    vmDisplayNameBuilder << vmName;
                    AppendVmStateSuffix(pclsObj, vmDisplayNameBuilder);
                    std::wstring vmDisplayName = vmDisplayNameBuilder.str();

                    std::wstring query = L"ASSOCIATORS OF {" + vmPath + L"} WHERE AssocClass=Msvm_SystemDevice ResultClass=Msvm_StorageAllocationSettingData";
                    IEnumWbemClassObject* pStorageEnumerator = nullptr;
                    hr = pSvc->ExecQuery(
                        _bstr_t(L"WQL"),
                        _bstr_t(query.c_str()),
                        WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
                        nullptr,
                        &pStorageEnumerator);

                    if (SUCCEEDED(hr) && pStorageEnumerator != nullptr) {
                        IWbemClassObject* pStorageObj = nullptr;
                        ULONG storageReturn = 0;
                        while (pStorageEnumerator->Next(WBEM_INFINITE, 1, &pStorageObj, &storageReturn) == WBEM_S_NO_ERROR && storageReturn != 0) {
                            if (IsVirtualDiskResource(pStorageObj)) {
                                std::vector<std::wstring> hostResources = GetHostResourceValues(pStorageObj);
                                for (const std::wstring& hostResource : hostResources) {
                                    if (!hostResource.empty()) {
                                        result << vmName << L'\t' << vmDisplayName << L'\t' << hostResource << L"\n";
                                    }
                                }
                            }

                            pStorageObj->Release();
                            pStorageObj = nullptr;
                        }

                        pStorageEnumerator->Release();
                    }
                }

                pclsObj->Release();
                pclsObj = nullptr;
            }

            if (pEnumerator) pEnumerator->Release();
            if (pSvc) pSvc->Release();
            if (pLoc) pLoc->Release();
            if (comInitialized) CoUninitialize();

            std::wstring resultStr = result.str();
            if (resultStr.empty()) {
                SetLastErrorMessage(L"No Hyper-V virtual disks found");
                if (bufferSize > 0) {
                    buffer[0] = L'\0';
                }

                return 0;
            }

            if (!AppendResultToBuffer(resultStr, buffer, bufferSize)) {
                return -6;
            }

            return 0;
        }
        catch (...) {
            if (pEnumerator) pEnumerator->Release();
            if (pSvc) pSvc->Release();
            if (pLoc) pLoc->Release();
            if (comInitialized) CoUninitialize();

            SetLastErrorMessage(L"Exception in EnumerateHyperVVirtualMachineDisks");
            return -99;
        }
    }
}
