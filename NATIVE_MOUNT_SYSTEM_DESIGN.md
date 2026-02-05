# Native Backup Mount System - No PowerShell, No Admin, C++ Shell Extension

## ? **Improvements Over PowerShell Approach**

### **What Changed**

| Feature | PowerShell Version | Native C++ Version |
|---------|-------------------|-------------------|
| **Admin Required** | ? Yes | ? No (read-only) |
| **PowerShell Needed** | ? Yes | ? No |
| **File Format** | VHDX only | WIM (Windows native) |
| **Mount Method** | External process | Native WIMGAPI.DLL |
| **Context Menu** | Command-line handler | COM Shell Extension |
| **Drive Letter** | Yes (G:, H:) | No (Folder path) |
| **Performance** | Slower | Faster |
| **Dependencies** | PowerShell 5+ | None (Windows built-in) |

---

## ?? **Architecture**

### **1. WIM File Format**
```
Backups saved as WIM (Windows Imaging Format):
- Native Windows format
- Excellent compression
- Read-only mounting without admin
- WIMGAPI.DLL included in Windows
- No third-party dependencies
```

### **2. C++ Backend (BackupEngine.dll)**
```cpp
WimMountManager.cpp
??? MountWim() - Mount WIM to folder
??? UnmountWim() - Unmount WIM
??? UnmountAll() - Cleanup all mounts
??? GetMountedWims() - List active mounts

ShellExtension.cpp
??? IShellExtInit - Initialize context menu
??? IContextMenu - Add "Unmount Backup" item
??? InvokeCommand() - Calls WimMountManager::UnmountWim()
```

### **3. C# Frontend (BackupUI)**
```csharp
NativeBackupMountManager.cs
??? P/Invoke to BackupEngine.dll
??? MountBackup() - No PowerShell!
??? UnmountBackup() - Direct C++ call
??? GetMountedBackups() - Query mounted list
```

---

## ?? **How It Works**

### **Mounting Process**

```
User clicks "Mount" ?
C# calls NativeBackupMountManager.MountBackup() ?
P/Invoke to BackupEngine.dll WimMount_MountWim() ?
C++ uses WIMGAPI.DLL:
  1. WIMCreateFile() - Open WIM
  2. WIMLoadImage() - Load image #1
  3. WIMMountImage() - Mount to temp folder
  4. Returns folder path
C# receives mount path ?
Opens Explorer to folder ?
User browses files
```

**No PowerShell process spawned!**  
**No admin elevation required!**

### **Unmounting Process**

```
User right-clicks folder in Explorer ?
Windows loads BackupEngine.dll shell extension ?
Extension checks: IsMountedWim(path) ?
If true: Add "Unmount Backup" menu item ?
User clicks menu item ?
InvokeCommand() calls WimMount_UnmountWim() ?
C++ unmounts WIM ?
Removes temp folder ?
Done
```

**No command-line handler needed!**  
**Direct C++ callback from Explorer!**

---

## ?? **Implementation Steps**

### **Step 1: Add C++ Export Functions**

Add to `BackupEngine.cpp`:

```cpp
extern "C" {
    __declspec(dllexport) bool WimMount_MountWim(
        const wchar_t* wimPath,
        const wchar_t* backupName,
        const wchar_t* backupType,
        wchar_t* mountPath,
        int mountPathSize,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        return BackupEngine::WimMountManager::MountWim(
            wimPath, backupName, backupType,
            mountPath, mountPathSize,
            errorMsg, errorMsgSize
        );
    }

    __declspec(dllexport) bool WimMount_UnmountWim(
        const wchar_t* mountPath,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        return BackupEngine::WimMountManager::UnmountWim(
            mountPath, errorMsg, errorMsgSize
        );
    }

    __declspec(dllexport) void WimMount_UnmountAll() {
        BackupEngine::WimMountManager::UnmountAll();
    }

    __declspec(dllexport) int WimMount_GetMountedCount() {
        auto mounts = BackupEngine::WimMountManager::GetMountedWims();
        return static_cast<int>(mounts.size());
    }

    __declspec(dllexport) bool WimMount_GetMountedInfo(
        int index,
        wchar_t* wimPath, int wimPathSize,
        wchar_t* mountPath, int mountPathSize,
        wchar_t* backupName, int backupNameSize,
        wchar_t* backupType, int backupTypeSize
    ) {
        auto mounts = BackupEngine::WimMountManager::GetMountedWims();
        
        if (index < 0 || index >= mounts.size()) {
            return false;
        }

        const auto& info = mounts[index];
        wcscpy_s(wimPath, wimPathSize, info.wimPath.c_str());
        wcscpy_s(mountPath, mountPathSize, info.mountPath.c_str());
        wcscpy_s(backupName, backupNameSize, info.backupName.c_str());
        wcscpy_s(backupType, backupTypeSize, info.backupType.c_str());

        return true;
    }
}
```

### **Step 2: Register Shell Extension (One-time, requires admin)**

Create installer or registration tool:

```cpp
// regsvr32 BackupEngine.dll
STDAPI DllRegisterServer() {
    return BackupEngine::RegisterServer();
}

STDAPI DllUnregisterServer() {
    return BackupEngine::UnregisterServer();
}
```

Run once as admin:
```cmd
regsvr32 /s BackupEngine.dll
```

After registration:
- **Runtime requires NO admin**
- Context menu appears automatically
- Shell extension loads on-demand

### **Step 3: Update C# to Use Native Manager**

Replace `BackupMountManager` calls with `NativeBackupMountManager`:

```csharp
// OLD (PowerShell):
var (success, driveLetter, error) = BackupMountManager.MountBackup(...);
OpenExplorer(driveLetter); // Opens G:

// NEW (Native):
var (success, mountPath, error) = NativeBackupMountManager.MountBackup(...);
OpenExplorer(mountPath); // Opens C:\Temp\BackupMounts\ServerBackup_...
```

### **Step 4: Update UI for Folder Paths**

Change DataGrid binding:

```xml
<!-- OLD -->
<DataGridTextColumn Header="Drive" Binding="{Binding DriveLetter}" Width="60"/>

<!-- NEW -->
<DataGridTextColumn Header="Mount Path" Binding="{Binding MountPath}" Width="*"/>
```

---

## ?? **User Experience**

### **Mounting**

```
User: Clicks "Mount" on backup

App:
  ? Calls NativeBackupMountManager.MountBackup()
  ? WIM mounted to: C:\Temp\BackupMounts\ServerBackup_20260202_143000
  ? Explorer opens to folder
  ? User sees backup files immediately

No UAC prompt!
No PowerShell window flash!
Just works!
```

### **Browsing**

```
User opens mounted folder:
  ServerBackup_20260202_143000\
  ??? C\
  ?   ??? Program Files\
  ?   ??? Users\
  ?   ??? Windows\
  ??? D\
  ?   ??? Data\
  ??? [All backup contents]

User can:
  ? Browse all files
  ? Copy files out
  ? Search for files
  ? Cannot modify (read-only)
  ? Cannot delete
```

### **Unmounting**

```
User: Right-clicks folder in Explorer

Windows:
  ? Loads BackupEngine.dll shell extension
  ? Checks if path is mounted WIM
  ? Shows context menu: "Unmount Backup"

User: Clicks "Unmount Backup"

Shell Extension:
  ? Calls WimMountManager::UnmountWim()
  ? Unmounts WIM
  ? Removes folder
  ? Shows success message

Done!
```

---

## ?? **Security & Permissions**

### **Read-Only Protection**

```cpp
// In WimMountManager.cpp
WIMMountImage(
    mountPoint.c_str(),
    wimPath,
    1,    // Image index
    nullptr  // Mount flags (read-only by default)
);
```

**Result:**
- Files appear in Explorer
- User can read/copy
- **Cannot** modify or delete
- **Cannot** execute from mount
- Backup integrity protected

### **No Admin Required**

```
WIM read-only mounting permissions:
? Any user can mount WIM files
? No UAC elevation needed
? No registry writes (for mounting)
? Admin only for DLL registration (one-time)
```

---

## ?? **Comparison: Drive vs. Folder**

| Aspect | Drive Letter (VHDX) | Folder (WIM) |
|--------|---------------------|--------------|
| **Path** | G:\ | C:\Temp\BackupMounts\... |
| **Limit** | 26 drives max | Unlimited |
| **Admin** | Required | Not required |
| **Icon** | Drive icon | Folder icon |
| **Speed** | Fast | Faster (no block device) |
| **Cleanup** | Manual | Automatic on exit |

---

## ??? **Build Configuration**

### **BackupEngine.vcxproj**

Add to project:

```xml
<ItemGroup>
  <ClCompile Include="WimMountManager.cpp" />
  <ClCompile Include="ShellExtension.cpp" />
</ItemGroup>
<ItemGroup>
  <ClInclude Include="WimMountManager.h" />
  <ClInclude Include="ShellExtension.h" />
</ItemGroup>
<ItemGroup>
  <None Include="ShellExtension.def" />
</ItemGroup>
```

Link with:
```xml
<Link>
  <AdditionalDependencies>wimgapi.lib;shlwapi.lib;ole32.lib;%(AdditionalDependencies)</AdditionalDependencies>
</Link>
```

### **ShellExtension.def**

```
LIBRARY BackupEngine
EXPORTS
    DllCanUnloadNow PRIVATE
    DllGetClassObject PRIVATE
    DllRegisterServer PRIVATE
    DllUnregisterServer PRIVATE
    WimMount_MountWim
    WimMount_UnmountWim
    WimMount_UnmountAll
    WimMount_GetMountedCount
    WimMount_GetMountedInfo
```

---

## ?? **Advantages Summary**

### **? Benefits**

1. **No PowerShell** - Direct Win32 API calls
2. **No Admin** - Read-only mounting doesn't require elevation
3. **Faster** - No process spawning
4. **More Reliable** - No dependency on PowerShell version
5. **Better Integration** - Native shell extension
6. **Unlimited Mounts** - Not limited by drive letters
7. **Cleaner** - Temp folders auto-cleanup
8. **Native Format** - WIM is Windows standard

### **? Tradeoffs**

1. **One-time admin** - DLL registration requires admin once
2. **No drive letter** - Folder path instead (arguably better)
3. **C++ complexity** - More code than PowerShell script
4. **COM registration** - Shell extension needs registration

---

## ?? **Migration Path**

### **Phase 1: Dual Support**
- Keep PowerShell version for VHDX
- Add native version for WIM
- Let user choose format

### **Phase 2: Transition**
- Default to WIM format
- Migrate existing VHDX to WIM
- PowerShell becomes fallback

### **Phase 3: Native Only**
- Remove PowerShell code
- WIM-only backups
- Full native stack

---

## ?? **Version Update**

```csharp
Version 4.11.0.0 MAJOR UPGRADE: Native backup mounting system - replaced PowerShell 
with native C++ WIMGAPI implementation, no admin privileges required for read-only 
mounting, COM shell extension for right-click unmount (no command-line handler), 
WIM file format for native Windows compatibility, unlimited concurrent mounts 
(not limited by drive letters), faster performance with direct Win32 APIs, automatic 
temp folder cleanup, full Explorer integration. Professional-grade solution! 
mdail 2/2/2026
```

---

**Document Version**: 1.0  
**Created**: February 2, 2026  
**Status**: ? **Ready for Implementation**  
**Complexity**: ???? (Advanced C++ COM programming)
