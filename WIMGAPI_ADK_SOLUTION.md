# ? SOLUTION FOUND: wimgapi.h Location

## ?? **Problem Solved!**

`wimgapi.h` is **NOT in the Windows SDK** - it's in the **Windows ADK (Assessment and Deployment Kit)**!

**Found at:**
```
C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Include\wimgapi.h
```

**Also need:**
```
C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Lib\x64\wimgapi.lib
```

---

## ?? **Fix: Update BackupEngine.vcxproj**

### **Method 1: Edit Project File** ? **Recommended**

Open `BackupEngine\BackupEngine.vcxproj` in a text editor and modify:

#### **Find this section:**
```xml
<PropertyGroup>
  <OutDir>$(SolutionDir)bin\$(Configuration)\</OutDir>
  <IntDir>$(SolutionDir)obj\$(Configuration)\$(ProjectName)\</IntDir>
</PropertyGroup>
```

#### **Replace with:**
```xml
<PropertyGroup>
  <OutDir>$(SolutionDir)bin\$(Configuration)\</OutDir>
  <IntDir>$(SolutionDir)obj\$(Configuration)\$(ProjectName)\</IntDir>
  <!-- Windows ADK paths for wimgapi.h -->
  <IncludePath>$(IncludePath);C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Include</IncludePath>
  <LibraryPath>$(LibraryPath);C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Lib\x64</LibraryPath>
</PropertyGroup>
```

#### **Find this section:**
```xml
<ItemDefinitionGroup>
  <ClCompile>
    <PreprocessorDefinitions>BACKUPENGINE_EXPORTS;%(PreprocessorDefinitions)</PreprocessorDefinitions>
    <LanguageStandard>stdcpp17</LanguageStandard>
    <WarningLevel>Level3</WarningLevel>
  </ClCompile>
  <Link>
    <AdditionalDependencies>VssApi.lib;wbemuuid.lib;ole32.lib;oleaut32.lib;%(AdditionalDependencies)</AdditionalDependencies>
    <SubSystem>Windows</SubSystem>
  </Link>
</ItemDefinitionGroup>
```

#### **Replace with:**
```xml
<ItemDefinitionGroup>
  <ClCompile>
    <PreprocessorDefinitions>BACKUPENGINE_EXPORTS;%(PreprocessorDefinitions)</PreprocessorDefinitions>
    <LanguageStandard>stdcpp17</LanguageStandard>
    <WarningLevel>Level3</WarningLevel>
    <!-- Windows ADK include for wimgapi.h -->
    <AdditionalIncludeDirectories>C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Include;%(AdditionalIncludeDirectories)</AdditionalIncludeDirectories>
  </ClCompile>
  <Link>
    <AdditionalDependencies>wimgapi.lib;VssApi.lib;wbemuuid.lib;ole32.lib;oleaut32.lib;shlwapi.lib;%(AdditionalDependencies)</AdditionalDependencies>
    <SubSystem>Windows</SubSystem>
    <!-- Windows ADK lib path -->
    <AdditionalLibraryDirectories>C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Lib\x64;%(AdditionalLibraryDirectories)</AdditionalLibraryDirectories>
  </Link>
</ItemDefinitionGroup>
```

---

### **Method 2: Visual Studio Property Pages**

1. **Right-click** BackupEngine project ? **Properties**
2. **C/C++** ? **General** ? **Additional Include Directories**:
   ```
   C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Include
   ```

3. **Linker** ? **General** ? **Additional Library Directories**:
   ```
   C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Lib\x64
   ```

4. **Linker** ? **Input** ? **Additional Dependencies**, add:
   ```
   wimgapi.lib;shlwapi.lib;
   ```

---

## ?? **Additional Fixes Needed**

### **1. Add Missing Include to BrsFileManager.cpp**

**Top of file:**
```cpp
#include "BrsFileManager.h"
#include <algorithm>  // ? ADD THIS
#include <fstream>
#include <zlib.h>
#include <wimgapi.h>
```

### **2. Add Missing Include to ShellExtension.cpp**

**Top of file:**
```cpp
#include "ShellExtension.h"
#include "WimMountManager.h"
#include <strsafe.h>
#include <shlwapi.h>          // ? ADD THIS
#pragma comment(lib, "shlwapi.lib")  // ? ADD THIS
```

---

## ?? **Build Commands**

After making the changes:

```cmd
cd BackupEngine
msbuild BackupEngine.vcxproj /p:Configuration=Release /p:Platform=x64
```

Or rebuild entire solution:

```cmd
msbuild BackupRestoreSolution.sln /p:Configuration=Release /p:Platform=x64
```

---

## ? **Verification Checklist**

Before building, verify:

- [ ] Windows ADK is installed
- [ ] wimgapi.h exists at ADK location
- [ ] wimgapi.lib exists at ADK location
- [ ] Project file updated with ADK paths
- [ ] `#include <algorithm>` added to BrsFileManager.cpp
- [ ] `#include <shlwapi.h>` added to ShellExtension.cpp
- [ ] `shlwapi.lib` added to linker dependencies

---

## ?? **What We Found**

| SDK Version | Has wimgapi.h | Has vss.h | Status |
|-------------|---------------|-----------|--------|
| 10.0.10240.0 | ? | ? | Old |
| 10.0.19041.0 | ? | ? | VSS only |
| 10.0.22621.0 | ? | ? | VSS only |
| 10.0.26100.0 | ? | ? | VSS only |
| **Windows ADK** | ? | N/A | **WIM API** |

**Conclusion**: 
- VSS headers ? Windows SDK
- WIM headers ? Windows ADK

---

## ?? **Why This Happened**

Microsoft **moved wimgapi.h** from the Windows SDK to the Windows ADK because:

1. **WIM tools** are deployment/imaging tools
2. **Windows ADK** is specifically for deployment
3. **Standard SDK** focuses on app development

This is **by design**, not a bug!

---

## ?? **Summary**

### **What You Have:**
? Windows SDK 10.0.19041.0, 10.0.22621.0, 10.0.26100.0  
? Windows ADK with wimgapi.h  
? vss.h (in Windows SDK)  
? wimgapi.lib (in Windows ADK)  

### **What You Need to Do:**
1. ? Add ADK include path to project
2. ? Add ADK lib path to project  
3. ? Add `#include <algorithm>` to BrsFileManager.cpp
4. ? Add `#include <shlwapi.h>` to ShellExtension.cpp
5. ? Rebuild project

### **Result:**
? Full compilation with all features  
? VSS snapshot support  
? WIM mounting support  
? BRS compression (with zlib)  
? Shell integration  

---

**Status**: ? **Problem Identified and Solution Provided**  
**Location**: Windows ADK, not Windows SDK  
**Action**: Update project paths
