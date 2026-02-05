# SOLUTION: Missing wimgapi.h Fix

## ?? **Problem Identified**

- ? Windows SDK **IS** installed
- ? `wimgapi.lib` exists
- ? `wimgapi.h` is **MISSING**
- ? `vss.h` exists in SDK 10.0.19041.0

**Your SDKs:**
- Current: `10.0.26100.0` (missing wimgapi.h)
- Found: `10.0.19041.0` (has vss.h, might have wimgapi.h)

## ? **Immediate Fix Applied**

I've created a **stub `wimgapi.h`** in `BackupEngine/wimgapi.h` that will allow your project to compile.

**Status**: ?? **This is a temporary workaround**

## ?? **Permanent Solutions**

### **Option 1: Use Older SDK** ? **Best Quick Fix**

Your system has SDK `10.0.19041.0` which likely has all headers.

**Update BackupEngine.vcxproj:**

```xml
<PropertyGroup>
  <!-- Force use of SDK 10.0.19041.0 which has wimgapi.h -->
  <WindowsTargetPlatformVersion>10.0.19041.0</WindowsTargetPlatformVersion>
</PropertyGroup>
```

**Then:**
```cmd
cd BackupEngine
msbuild BackupEngine.vcxproj /p:Configuration=Release /p:Platform=x64
```

### **Option 2: Repair Visual Studio Installation**

```
1. Open Visual Studio Installer
2. Click "More" ? "Repair"
3. Wait 10-15 minutes
4. Rebuild project
```

### **Option 3: Install Specific SDK Components**

```
Visual Studio Installer ? Modify
? Individual Components
? Check:
  ? Windows 10 SDK (10.0.19041.0)
  ? Windows 10 SDK (10.0.22000.0)
? Modify
```

### **Option 4: Standalone SDK Download**

Download from:
https://developer.microsoft.com/en-us/windows/downloads/sdk-archive/

**Select**: Windows 10 SDK version 2004 (10.0.19041.0)

## ?? **Check Which SDKs You Have**

```powershell
Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\Include" | Select-Object Name
```

**Expected output:**
```
Name
----
10.0.19041.0  ? Has wimgapi.h (probably)
10.0.22000.0  ? Windows 11 SDK
10.0.26100.0  ? Your current (missing wimgapi.h)
```

## ?? **Verify wimgapi.h in Older SDK**

```powershell
Test-Path "C:\Program Files (x86)\Windows Kits\10\Include\10.0.19041.0\um\wimgapi.h"
```

If this returns `True`, you can use that SDK version!

## ?? **Recommended Action Plan**

### **Step 1: Check Older SDK** (2 minutes)

```powershell
# Check if wimgapi.h exists in older SDK
Test-Path "C:\Program Files (x86)\Windows Kits\10\Include\10.0.19041.0\um\wimgapi.h"
```

### **Step 2: If TRUE - Use That SDK**

Edit `BackupEngine/BackupEngine.vcxproj`:

```xml
<PropertyGroup Label="Globals">
  <VCProjectVersion>17.0</VCProjectVersion>
  <Keyword>Win32Proj</Keyword>
  <ProjectGuid>{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}</ProjectGuid>
  <WindowsTargetPlatformVersion>10.0.19041.0</WindowsTargetPlatformVersion>  ? ADD THIS
</PropertyGroup>
```

### **Step 3: If FALSE - Use Stub**

The stub wimgapi.h I created will work temporarily.

### **Step 4: Build**

```cmd
cd BackupEngine
msbuild BackupEngine.vcxproj /p:Configuration=Release /p:Platform=x64
```

## ?? **Why This Happened**

SDK `10.0.26100.0` is a **very new** pre-release SDK that might:
- Have incomplete headers
- Be missing legacy APIs
- Not be fully tested

**Recommendation**: Use SDK `10.0.19041.0` or `10.0.22000.0` for production.

## ?? **Next Steps**

### **For Immediate Build:**

1. ? Use the stub `wimgapi.h` I created
2. ? Add `#include <algorithm>` to BrsFileManager.cpp
3. ? Build project

### **For Production:**

1. ?? Switch to SDK 10.0.19041.0
2. ?? Install zlib (optional for BRS)
3. ?? Test full functionality

## ?? **Quick Test**

Run this to check SDK versions:

```powershell
Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\Include" | ForEach-Object {
    $sdkVer = $_.Name
    $hasWim = Test-Path "$($_.FullName)\um\wimgapi.h"
    $hasVss = Test-Path "$($_.FullName)\um\vss.h"
    [PSCustomObject]@{
        SDK = $sdkVer
        'Has wimgapi.h' = $hasWim
        'Has vss.h' = $hasVss
    }
} | Format-Table -AutoSize
```

This will show you which SDK has what headers!

---

**Status**: ? **Workaround Applied** (stub wimgapi.h created)  
**Recommended**: ?? **Switch to SDK 10.0.19041.0**  
**Created**: February 3, 2026
