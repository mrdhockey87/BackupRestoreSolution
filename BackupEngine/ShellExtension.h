#pragma once
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <string>

// {12345678-1234-1234-1234-123456789ABC} - Generate unique GUID
// Use guidgen.exe to create a real one
DEFINE_GUID(CLSID_BackupMountContextMenu,
    0x12345678, 0x1234, 0x1234, 0x12, 0x34, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC);

namespace BackupEngine {

    // COM Shell Extension for right-click context menu
    class BackupMountContextMenu : 
        public IShellExtInit,
        public IContextMenu
    {
    private:
        LONG refCount;
        std::wstring selectedPath;

    public:
        BackupMountContextMenu();
        ~BackupMountContextMenu();

        // IUnknown methods
        STDMETHODIMP QueryInterface(REFIID riid, void** ppv);
        STDMETHODIMP_(ULONG) AddRef();
        STDMETHODIMP_(ULONG) Release();

        // IShellExtInit methods
        STDMETHODIMP Initialize(
            LPCITEMIDLIST pidlFolder,
            IDataObject* pdtobj,
            HKEY hkeyProgID
        );

        // IContextMenu methods
        STDMETHODIMP QueryContextMenu(
            HMENU hmenu,
            UINT indexMenu,
            UINT idCmdFirst,
            UINT idCmdLast,
            UINT uFlags
        );

        STDMETHODIMP InvokeCommand(LPCMINVOKECOMMANDINFO pici);

        STDMETHODIMP GetCommandString(
            UINT_PTR idCmd,
            UINT uType,
            UINT* pReserved,
            LPSTR pszName,
            UINT cchMax
        );

    private:
        bool IsBackupMountPath(const wchar_t* path);
    };

    // Class factory for COM registration
    class BackupMountContextMenuFactory : public IClassFactory {
    private:
        LONG refCount;

    public:
        BackupMountContextMenuFactory();
        ~BackupMountContextMenuFactory();

        // IUnknown
        STDMETHODIMP QueryInterface(REFIID riid, void** ppv);
        STDMETHODIMP_(ULONG) AddRef();
        STDMETHODIMP_(ULONG) Release();

        // IClassFactory
        STDMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv);
        STDMETHODIMP LockServer(BOOL fLock);
    };

    // DLL registration functions
    HRESULT RegisterServer();
    HRESULT UnregisterServer();

} // namespace BackupEngine
