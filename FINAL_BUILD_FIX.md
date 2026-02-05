# ? FINAL FIX - Build Errors Resolved

## ?? **Current Status**

? wimgapi.h found (Windows ADK)  
? Code syntax fixed  
? **wimgapi.lib not found** (linker error)

---

## ?? **Solution: Add wimgapi.lib Path**

The linker can't find `wimgapi.lib`. You need to add the library path to your project.

### **Manual Fix - Edit BackupEngine.vcxproj**

Open `BackupEngine\BackupEngine.vcxproj` in Notepad and find this section:

```xml
<Link>
  <AdditionalDependencies>wimgapi.lib;VssApi.lib;wbemuuid.lib;ole32.lib;oleaut32.lib;shlwapi.lib;%(AdditionalDependencies)</AdditionalDependencies>
  <SubSystem>Windows</SubSystem>
</Link>
```

**Add the AdditionalLibraryDirectories line:**

```xml
<Link>
  <AdditionalDependencies>wimgapi.lib;VssApi.lib;wbemuuid.lib;ole32.lib;oleaut32.lib;shlwapi.lib;%(AdditionalDependencies)</AdditionalDependencies>
  <AdditionalLibraryDirectories>C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Lib\x64;%(AdditionalLibraryDirectories)</AdditionalLibraryDirectories>
  <SubSystem>Windows</SubSystem>
</Link>
```

---

## ?? **Complete BackupEngine.vcxproj Configuration**

Here's the complete `<ItemDefinitionGroup>` section you should have:

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
    <AdditionalLibraryDirectories>C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Lib\x64;%(AdditionalLibraryDirectories)</AdditionalLibraryDirectories>
    <SubSystem>Windows</SubSystem>
  </Link>
</ItemDefinitionGroup>
```

---

## ? **Verification Steps**

After editing, verify the paths exist:

```powershell
# Check include path
Test-Path "C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Include\wimgapi.h"

# Check lib path  
Test-Path "C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Lib\x64\wimgapi.lib"
```

Both should return `True`.

---

## ?? **Build Again**

After adding the library path:

```cmd
msbuild BackupEngine\BackupEngine.vcxproj /p:Configuration=Release /p:Platform=x64
```

---

## ?? **Fixed Issues Summary**

| Issue | Status | Fix |
|-------|--------|-----|
| wimgapi.h not found | ? FIXED | Added ADK include path |
| CLSID namespace error | ? FIXED | Removed `BackupEngine::` prefix |
| WIMCreateFile API | ? FIXED | Corrected parameter types |
| WIMGetImageCount API | ? FIXED | Returns value directly |
| wimgapi.lib not found | ?? **FIX THIS** | Add ADK lib path |

---

## ?? **Quick Reference**

**Windows ADK Paths:**
```
Include: C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Include
Lib x64: C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\SDKs\Wimgapi\Lib\x64
```

**Required Libraries:**
- `wimgapi.lib` - WIM API
- `VssApi.lib` - VSS API
- `shlwapi.lib` - Shell API
- `ole32.lib` - COM
- `oleaut32.lib` - OLE Automation
- `wbemuuid.lib` - WMI

---

## ?? **After This Fix**

Your project should build successfully with:
- ? Full VSS support
- ? WIM file support  
- ? BRS compression
- ? Shell integration

---

**Next Command:**
```cmd
msbuild BackupRestoreSolution.sln /p:Configuration=Release /p:Platform=x64
```

**Expected Result:** ? **Build Succeeded**
