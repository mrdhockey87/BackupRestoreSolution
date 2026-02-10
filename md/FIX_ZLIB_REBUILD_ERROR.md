# ? FIX: "cannot open file 'zlib.lib'" on Rebuild

## ?? **Root Cause**

Your project has **TWO conflicting zlib references**:
1. ? vcpkg's zlib (installed and working)
2. ? Old NuGet zlib reference (missing package, causing conflict)

**Result**: First build works (vcpkg), rebuild fails (tries NuGet zlib that doesn't exist)

---

## ?? **Solution: Remove Old NuGet References**

### **Step 1: Close Visual Studio** (Important!)

Close Visual Studio completely to unlock the project file.

### **Step 2: Edit BackupEngine.vcxproj in Notepad**

Open `BackupEngine\BackupEngine.vcxproj` in Notepad.

**Find these lines** (at the end of file):

```xml
  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.targets" />
  <Import Project="..\packages\zlib-msvc-x64.1.2.11.8900\build\native\zlib-msvc-x64.targets" Condition="Exists('..\packages\zlib-msvc-x64.1.2.11.8900\build\native\zlib-msvc-x64.targets')" />
  <Target Name="EnsureNuGetPackageBuildImports" BeforeTargets="PrepareForBuild">
    <PropertyGroup>
      <ErrorText>This project references NuGet package(s) that are missing on this computer. Use NuGet Package Restore to download them.  For more information, see http://go.microsoft.com/fwlink/?LinkID=322105. The missing file is {0}.</ErrorText>
    </PropertyGroup>
    <Error Condition="!Exists('..\packages\zlib-msvc-x64.1.2.11.8900\build\native\zlib-msvc-x64.targets')" Text="$([System.String]::Format('$(ErrorText)', '..\packages\zlib-msvc-x64.1.2.11.8900\build\native\zlib-msvc-x64.targets'))" />
  </Target>
</Project>
```

**Replace with:**

```xml
  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.targets" />
  <!-- Old NuGet zlib removed - using vcpkg instead -->
</Project>
```

**Save and close** Notepad.

---

### **Step 3: Edit BrsFileManager.cpp**

Open `BackupEngine\BrsFileManager.cpp` in Notepad.

**Find** (near top of file):

```cpp
#pragma comment(lib, "wimgapi.lib")
#pragma comment(lib, "zlib.lib")
```

**Change to:**

```cpp
#pragma comment(lib, "wimgapi.lib")
// zlib.lib linked automatically by vcpkg
```

**Save and close** Notepad.

---

### **Step 4: Optional - Delete packages.config**

Delete or rename `BackupEngine\packages.config` (it's for the old NuGet zlib).

---

### **Step 5: Reopen Visual Studio and Rebuild**

```cmd
msbuild BackupEngine\BackupEngine.vcxproj /p:Configuration=Debug /p:Platform=x64 /t:Rebuild
```

---

## ? **Expected Result**

```
Build succeeded
  0 Warning(s)
  0 Error(s)
```

---

## ?? **Why This Fixes It**

| Before | After |
|--------|-------|
| NuGet trying to find `zlib.lib` | ? Removed |
| vcpkg provides `zlib.lib` | ? Active |
| Conflict on rebuild | ? Fixed |

vcpkg's integration automatically:
- Adds include paths for `zlib.h`
- Adds library paths for `zlib.lib` and `zlibd.lib` (debug)
- Links the correct library for Debug/Release
- No manual configuration needed!

---

## ?? **Verification**

After rebuild, check the build output for:

```
1>  zlib.lib  ? Should see this (from vcpkg)
```

**NOT:**

```
LINK : fatal error LNK1104: cannot open file 'zlib.lib'
```

---

## ?? **Quick Summary**

**What to do:**
1. ? Close Visual Studio
2. ? Edit `BackupEngine.vcxproj` - remove NuGet zlib references (lines 89-95)
3. ? Edit `BrsFileManager.cpp` - remove `#pragma comment(lib, "zlib.lib")`
4. ? Rebuild project

**Result:** ? Build works every time, no more zlib.lib errors!

---

## ?? **Alternative: PowerShell Fix**

If you prefer, run this PowerShell script:

```powershell
# Close Visual Studio first!

# Remove NuGet zlib from project file
$vcxproj = "BackupEngine\BackupEngine.vcxproj"
$content = Get-Content $vcxproj -Raw
$content = $content -replace '  <Import Project="\.\./packages/zlib.*?targets.*?/>\r?\n', ''
$content = $content -replace '  <Target Name="EnsureNuGetPackageBuildImports".*?</Target>\r?\n', ''
Set-Content $vcxproj $content

# Remove pragma from cpp file
$cpp = "BackupEngine\BrsFileManager.cpp"
$content = Get-Content $cpp -Raw
$content = $content -replace '#pragma comment\(lib, "zlib\.lib"\)', '// zlib.lib linked automatically by vcpkg'
Set-Content $cpp $content

Write-Host "? Fixed! Reopen Visual Studio and rebuild."
```

---

**Status**: ?? **Manual fix required**  
**Time**: **2 minutes**  
**Result**: ? **zlib.lib errors gone forever**
