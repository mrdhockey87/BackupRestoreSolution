#include "ShellExtension.h"
#include "WimMountManager.h"
#include <strsafe.h>
#include <shlwapi.h>  // ADD THIS LINE
#pragma comment(lib, "shlwapi.lib")  // ADD THIS LINE

namespace BackupEngine {

    // Module instance
    HINSTANCE g_hInst = nullptr;
    LONG g_cDllRef = 0;

    //
    // BackupMountContextMenu Implementation
    //

    BackupMountContextMenu::BackupMountContextMenu() : refCount(1) {
        InterlockedIncrement(&g_cDllRef);
    }

    BackupMountContextMenu::~BackupMountContextMenu() {
        InterlockedDecrement(&g_cDllRef);
    }

    STDMETHODIMP BackupMountContextMenu::QueryInterface(REFIID riid, void** ppv) {
        static const QITAB qit[] = {
            QITABENT(BackupMountContextMenu, IContextMenu),
            QITABENT(BackupMountContextMenu, IShellExtInit),
            { 0 },
        };
        return QISearch(this, qit, riid, ppv);
    }

    STDMETHODIMP_(ULONG) BackupMountContextMenu::AddRef() {
        return InterlockedIncrement(&refCount);
    }

    STDMETHODIMP_(ULONG) BackupMountContextMenu::Release() {
        ULONG cRef = InterlockedDecrement(&refCount);
        if (cRef == 0) {
            delete this;
        }
        return cRef;
    }

    STDMETHODIMP BackupMountContextMenu::Initialize(
        LPCITEMIDLIST pidlFolder,
        IDataObject* pdtobj,
        HKEY hkeyProgID
    ) {
        if (!pdtobj) {
            return E_INVALIDARG;
        }

        FORMATETC fmt = { CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL };
        STGMEDIUM stg = { TYMED_HGLOBAL };

        if (SUCCEEDED(pdtobj->GetData(&fmt, &stg))) {
            HDROP hDrop = static_cast<HDROP>(GlobalLock(stg.hGlobal));
            if (hDrop) {
                UINT nFiles = DragQueryFileW(hDrop, 0xFFFFFFFF, nullptr, 0);
                if (nFiles == 1) {
                    wchar_t szFile[MAX_PATH];
                    if (DragQueryFileW(hDrop, 0, szFile, ARRAYSIZE(szFile))) {
                        selectedPath = szFile;
                    }
                }
                GlobalUnlock(stg.hGlobal);
            }
            ReleaseStgMedium(&stg);
        }

        return S_OK;
    }

    STDMETHODIMP BackupMountContextMenu::QueryContextMenu(
        HMENU hmenu,
        UINT indexMenu,
        UINT idCmdFirst,
        UINT idCmdLast,
        UINT uFlags
    ) {
        // Don't add menu in certain contexts
        if (uFlags & CMF_DEFAULTONLY) {
            return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);
        }

        // Only show for mounted backup directories
        if (!IsBackupMountPath(selectedPath.c_str())) {
            return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);
        }

        // Add "Unmount Backup" menu item
        MENUITEMINFOW mii = { sizeof(mii) };
        mii.fMask = MIIM_STRING | MIIM_ID | MIIM_STATE;
        mii.wID = idCmdFirst;
        mii.fState = MFS_ENABLED;
        mii.dwTypeData = const_cast<LPWSTR>(L"Unmount Backup");

        if (!InsertMenuItemW(hmenu, indexMenu, TRUE, &mii)) {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        // Return number of menu items added
        return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 1);
    }

    STDMETHODIMP BackupMountContextMenu::InvokeCommand(LPCMINVOKECOMMANDINFO pici) {
        // Check if command is from our menu
        if (HIWORD(pici->lpVerb)) {
            // String command - not used
            return E_FAIL;
        }

        // Command index 0 = Unmount
        if (LOWORD(pici->lpVerb) == 0) {
            // Unmount the backup
            wchar_t errorMsg[256] = { 0 };

            if (WimMountManager::UnmountWim(selectedPath.c_str(), errorMsg, 256)) {
                MessageBoxW(pici->hwnd,
                    L"Backup unmounted successfully.",
                    L"Unmount Backup",
                    MB_OK | MB_ICONINFORMATION);
            }
            else {
                MessageBoxW(pici->hwnd,
                    errorMsg,
                    L"Unmount Error",
                    MB_OK | MB_ICONERROR);
            }

            return S_OK;
        }

        return E_FAIL;
    }

    STDMETHODIMP BackupMountContextMenu::GetCommandString(
        UINT_PTR idCmd,
        UINT uType,
        UINT* pReserved,
        LPSTR pszName,
        UINT cchMax
    ) {
        if (idCmd == 0) {
            switch (uType) {
                case GCS_HELPTEXTW:
                    StringCchCopyW(reinterpret_cast<LPWSTR>(pszName), cchMax,
                        L"Unmount this backup virtual folder");
                    return S_OK;

                case GCS_VERBW:
                    StringCchCopyW(reinterpret_cast<LPWSTR>(pszName), cchMax,
                        L"unmount");
                    return S_OK;
            }
        }

        return E_INVALIDARG;
    }

    bool BackupMountContextMenu::IsBackupMountPath(const wchar_t* path) {
        if (!path || !*path) {
            return false;
        }

        // Check if this path is a mounted WIM
        return WimMountManager::IsMountedWim(path);
    }

    //
    // Class Factory Implementation
    //

    BackupMountContextMenuFactory::BackupMountContextMenuFactory() : refCount(1) {
        InterlockedIncrement(&g_cDllRef);
    }

    BackupMountContextMenuFactory::~BackupMountContextMenuFactory() {
        InterlockedDecrement(&g_cDllRef);
    }

    STDMETHODIMP BackupMountContextMenuFactory::QueryInterface(REFIID riid, void** ppv) {
        if (riid == IID_IUnknown || riid == IID_IClassFactory) {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }

        *ppv = nullptr;
        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) BackupMountContextMenuFactory::AddRef() {
        return InterlockedIncrement(&refCount);
    }

    STDMETHODIMP_(ULONG) BackupMountContextMenuFactory::Release() {
        ULONG cRef = InterlockedDecrement(&refCount);
        if (cRef == 0) {
            delete this;
        }
        return cRef;
    }

    STDMETHODIMP BackupMountContextMenuFactory::CreateInstance(
        IUnknown* pUnkOuter,
        REFIID riid,
        void** ppv
    ) {
        if (pUnkOuter != nullptr) {
            return CLASS_E_NOAGGREGATION;
        }

        BackupMountContextMenu* pExt = new (std::nothrow) BackupMountContextMenu();
        if (!pExt) {
            return E_OUTOFMEMORY;
        }

        HRESULT hr = pExt->QueryInterface(riid, ppv);
        pExt->Release();
        return hr;
    }

    STDMETHODIMP BackupMountContextMenuFactory::LockServer(BOOL fLock) {
        if (fLock) {
            InterlockedIncrement(&g_cDllRef);
        }
        else {
            InterlockedDecrement(&g_cDllRef);
        }
        return S_OK;
    }

    //
    // DLL Registration
    //

    HRESULT RegisterServer() {
        wchar_t szModule[MAX_PATH];
        if (!GetModuleFileNameW(g_hInst, szModule, ARRAYSIZE(szModule))) {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        // Register CLSID
        HKEY hKey;
        wchar_t szCLSID[MAX_PATH];
        StringFromGUID2(CLSID_BackupMountContextMenu, szCLSID, ARRAYSIZE(szCLSID));

        wchar_t szSubkey[MAX_PATH];
        StringCchPrintfW(szSubkey, ARRAYSIZE(szSubkey),
            L"CLSID\\%s", szCLSID);

        LONG result = RegCreateKeyExW(HKEY_CLASSES_ROOT, szSubkey, 0, nullptr,
            REG_OPTION_NON_VOLATILE, KEY_WRITE, nullptr, &hKey, nullptr);

        if (result == ERROR_SUCCESS) {
            RegSetValueExW(hKey, nullptr, 0, REG_SZ,
                reinterpret_cast<const BYTE*>(L"Backup Mount Context Menu"),
                static_cast<DWORD>(wcslen(L"Backup Mount Context Menu") + 1) * sizeof(wchar_t));

            RegCloseKey(hKey);
        }

        // Register InprocServer32
        StringCchPrintfW(szSubkey, ARRAYSIZE(szSubkey),
            L"CLSID\\%s\\InprocServer32", szCLSID);

        result = RegCreateKeyExW(HKEY_CLASSES_ROOT, szSubkey, 0, nullptr,
            REG_OPTION_NON_VOLATILE, KEY_WRITE, nullptr, &hKey, nullptr);

        if (result == ERROR_SUCCESS) {
            RegSetValueExW(hKey, nullptr, 0, REG_SZ,
                reinterpret_cast<const BYTE*>(szModule),
                static_cast<DWORD>(wcslen(szModule) + 1) * sizeof(wchar_t));

            RegSetValueExW(hKey, L"ThreadingModel", 0, REG_SZ,
                reinterpret_cast<const BYTE*>(L"Apartment"),
                static_cast<DWORD>(wcslen(L"Apartment") + 1) * sizeof(wchar_t));

            RegCloseKey(hKey);
        }

        // Register for Directory background
        const wchar_t* szApprovedKey = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Approved";
        result = RegCreateKeyExW(HKEY_LOCAL_MACHINE, szApprovedKey, 0, nullptr,
            REG_OPTION_NON_VOLATILE, KEY_WRITE, nullptr, &hKey, nullptr);

        if (result == ERROR_SUCCESS) {
            RegSetValueExW(hKey, szCLSID, 0, REG_SZ,
                reinterpret_cast<const BYTE*>(L"Backup Mount Context Menu"),
                static_cast<DWORD>(wcslen(L"Backup Mount Context Menu") + 1) * sizeof(wchar_t));

            RegCloseKey(hKey);
        }

        // Notify shell of changes
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);

        return S_OK;
    }

    HRESULT UnregisterServer() {
        wchar_t szCLSID[MAX_PATH];
        StringFromGUID2(CLSID_BackupMountContextMenu, szCLSID, ARRAYSIZE(szCLSID));

        wchar_t szSubkey[MAX_PATH];

        // Remove CLSID
        StringCchPrintfW(szSubkey, ARRAYSIZE(szSubkey),
            L"CLSID\\%s", szCLSID);
        RegDeleteTreeW(HKEY_CLASSES_ROOT, szSubkey);

        // Remove from approved extensions
        HKEY hKey;
        const wchar_t* szApprovedKey = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Approved";
        if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, szApprovedKey, 0, KEY_WRITE, &hKey) == ERROR_SUCCESS) {
            RegDeleteValueW(hKey, szCLSID);
            RegCloseKey(hKey);
        }

        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);

        return S_OK;
    }

} // namespace BackupEngine

//
// DLL Entry Points
//

BOOL APIENTRY DllMain(HMODULE hModule, DWORD dwReason, LPVOID lpReserved) {
    switch (dwReason) {
        case DLL_PROCESS_ATTACH:
            BackupEngine::g_hInst = hModule;
            DisableThreadLibraryCalls(hModule);
            BackupEngine::WimMountManager::Initialize();
            break;

        case DLL_PROCESS_DETACH:
            BackupEngine::WimMountManager::Cleanup();
            break;
    }
    return TRUE;
}

STDAPI DllCanUnloadNow() {
    return BackupEngine::g_cDllRef == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv) {
if (rclsid == CLSID_BackupMountContextMenu) {  // CLSID is in global namespace
    BackupEngine::BackupMountContextMenuFactory* pFactory =
        new (std::nothrow) BackupEngine::BackupMountContextMenuFactory();

    if (!pFactory) {
        return E_OUTOFMEMORY;
    }

        HRESULT hr = pFactory->QueryInterface(riid, ppv);
        pFactory->Release();
        return hr;
    }

    return CLASS_E_CLASSNOTAVAILABLE;
}

STDAPI DllRegisterServer() {
    return BackupEngine::RegisterServer();
}

STDAPI DllUnregisterServer() {
    return BackupEngine::UnregisterServer();
}
