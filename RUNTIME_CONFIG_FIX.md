# Runtime Config File Issue - RESOLVED

## Problem
After restarting Visual Studio or running "Clean Solution", the `BackupUI.exe` would fail with error:
```
This application requires the .NET Desktop Runtime
```

## Root Cause
When using **custom output paths** (`AppendTargetFrameworkToOutputPath=false`), .NET SDK generates `.runtimeconfig.json` files in the **intermediate directory** but doesn't automatically copy them to the final output directory.

When you run "Clean Solution", the output directory is cleared but the intermediate files remain. On next build, SDK thinks the runtimeconfig already exists (in intermediate) and doesn't regenerate it to the output folder.

## Solution (Version 5.13.3.9)

### 1. Added Custom Build Target
Created `EnsureRuntimeConfigInOutput` target in `Directory.Build.targets` that:
- Runs **after every build** (`AfterTargets="Build"`)
- Copies `.runtimeconfig.json` from intermediate to output
- Copies `.deps.json` from intermediate to output  
- Sets `SkipUnchangedFiles=false` to **force copy** even if files exist
- Warns if runtime config is missing from intermediate directory

### 2. What This Fixes
? Clean Solution ? Build works correctly  
? Restart Visual Studio ? Build works correctly  
? Files persist across all build operations  
? "Install .NET runtime" error permanently resolved

### 3. File Locations
```
Intermediate: artifacts\obj\BackupUI\Debug\BackupUI.runtimeconfig.json
Output:       artifacts\bin\Debug\BackupUI.runtimeconfig.json
              ? This is what Windows needs to launch the .exe
```

## Verification

Run this script to verify runtime configs are present:
```powershell
.\Verify-RuntimeConfigs.ps1
```

Should show:
```
Runtime Config Files:
  ? BackupUI.runtimeconfig.json
  ? BackupService.runtimeconfig.json
```

## How It Works

### Directory.Build.props
- Sets `GenerateRuntimeConfigurationFiles=true` (generates in intermediate)
- Sets `ProduceReferenceAssembly=false` (required with custom paths)

### Directory.Build.targets  
- Runs `EnsureRuntimeConfigInOutput` after every build
- Copies files from intermediate ? output
- Ensures files always present

## Build Workflow
```
1. Clean Solution
   ?? Deletes artifacts\bin\Debug\*.* (output)
   ?? Keeps artifacts\obj\BackupUI\Debug\*.* (intermediate)

2. Build
   ?? SDK generates .runtimeconfig.json in intermediate
   ?? Builds .exe to output
   ?? EnsureRuntimeConfigInOutput copies runtime config to output

3. Launch BackupUI.exe
   ?? Windows finds BackupUI.runtimeconfig.json ?
```

## Version History
- **5.13.3.5** - Initial attempt with centralized GenerateRuntimeConfigurationFiles
- **5.13.3.8** - Removed conditional, added ProduceReferenceAssembly=false
- **5.13.3.9** - **FINAL FIX** - Added build target to copy files after every build

## Related Files
- `Directory.Build.props` - Enables runtime config generation
- `Directory.Build.targets` - Copies files to output
- `Verify-RuntimeConfigs.ps1` - Verification script

## Testing
1. Clean Solution
2. Build Solution
3. Check artifacts\bin\Debug\ - should contain .runtimeconfig.json files
4. Launch BackupUI.exe - should start without errors
5. Repeat steps 1-4 - should always work

---
**Status:** ? RESOLVED (Version 5.13.3.9)  
**Date:** 2/14/2026
