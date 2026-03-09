# Version 5.13.9.1 - Quick Summary

## What Changed

**Fixed AccessViolationException crash when mounting backups - missing parameter in P/Invoke signature!**

## The Problem

```
System.AccessViolationException: 'Attempted to read or write protected memory.'
at line 92: WimMount_MountWim(...)
```

## Root Cause

**P/Invoke signature mismatch** between C# and C++:

**C++ (CORRECT - 8 parameters):**
```cpp
WimMount_MountWim(wimPath, backupName, backupType, imageIndex, 
                   mountPath, mountPathSize, errorMsg, errorMsgSize)
```

**C# (WRONG - 7 parameters):**
```csharp
WimMount_MountWim(wimPath, backupName, backupType, 
                   mountPath, mountPathSize, errorMsg, errorMsgSize)
// Missing: imageIndex parameter!
```

## The Problem

C# pushed 7 parameters, C++ expected 8:
- **Parameter 4 missing** → Stack misaligned
- C++ read wrong values for every parameter after the gap
- C++ tried to write to address 260 (the size integer!)
- **Crash!**

## The Solution

Added missing `imageIndex` parameter to P/Invoke:

```csharp
[DllImport("BackupEngine.dll", ...)]
private static extern bool WimMount_MountWim(
    string wimPath,
    string backupName,
    string backupType,
    int imageIndex,              // ✅ ADDED
    StringBuilder mountPath,
    int mountPathSize,
    StringBuilder errorMsg,
    int errorMsgSize
);
```

Updated MountBackup method:

```csharp
public static (bool Success, string MountPath, string Error) MountBackup(
    string wimPath,
    string backupName,
    string backupType,
    int imageIndex = 1)  // ✅ ADDED with default value
{
    bool success = WimMount_MountWim(
        wimPath, backupName, backupType, imageIndex, // ✅ Pass it
        mountPath, 260, errorMsg, 512
    );
}
```

## What imageIndex Does

Selects which restore point to mount from multi-image backups:
- **imageIndex = 1** → First image (oldest restore point) - DEFAULT
- **imageIndex = 3** → Third image (e.g., Day 2 incremental)
- **imageIndex = 5** → Fifth image (e.g., Day 3 incremental)

## Benefits

✅ **No more crashes** - Stack alignment correct  
✅ **Signatures match** - C# and C++ exactly aligned  
✅ **Default behavior** - imageIndex=1 mounts first image  
✅ **Future-ready** - Can select specific restore points  

## Testing

- [x] Mount single-image backup → Works
- [x] Mount multi-image backup → Mounts first image
- [x] No AccessViolationException → Fixed!

## Why This Happened

Version 5.13.8.0 added multi-image support:
- C++ function updated with `imageIndex` parameter
- C# P/Invoke declaration **wasn't updated**
- Mismatch remained hidden until version 5.13.9.0 started using this manager

---

**Build Status**: ✅ Successful  
**Type**: Critical Bug Fix  
**Impact**: HIGH - Mounting now works!  

**P/Invoke signatures perfectly aligned!** 🎉
