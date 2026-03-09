# Version 5.13.9.0 - Quick Summary

## What Changed

**Fixed "virtual disk provider for file not found" error when mounting .ssb backup files!**

## The Problem

User clicked "Mount" on backup → Error: "virtual disk provider for file not found"

## Root Cause

Mount code was calling **WRONG manager**:
- **BackupMountManager** (old) → PowerShell/Virtual Disk API → for VHDX files
- **Trying to mount WIM files (.ssb) as VHDX** → ERROR!

## The Solution

Changed to use **CORRECT manager**:
- **NativeBackupMountManager** (new) → C++/WIM API → for WIM/SSB files

## Code Changes

**MainWindow.xaml.cs:**
```csharp
// OLD (WRONG):
var (success, driveLetter, error) = BackupMountManager.MountBackup(...);

// NEW (CORRECT):
var (success, mountPath, error) = NativeBackupMountManager.MountBackup(...);
```

All 4 mount-related methods updated:
1. MountBackup_Click
2. UnmountBackup_Click  
3. LoadMountedBackups
4. UnmountAll_Click

**MainWindow.xaml:**
- Changed "Drive" column → "Mount Path" column
- Changed Tag binding: DriveLetter → MountPath
- Removed BackupDate column (not available)

## Mount Behavior Change

### Before (VHDX - OLD)
- Mounts as drive letter: E:, F:, G:
- Requires admin rights
- Uses PowerShell

### After (WIM - NEW)
- Mounts to folder: C:\BackupMounts\BackupName_20260306_153022\
- NO admin rights needed
- Uses C++ WIM API

## Benefits

✅ **No more errors** - Correct API for correct format  
✅ **No admin required** - WIM mounting doesn't need elevation  
✅ **Works with .ssb files** - Proper WIM format support  
✅ **Clear mount paths** - Full folder path visible  
✅ **Read-only** - Cannot accidentally modify backups  

## Testing

- [x] Mount .ssb backup → Opens folder in Explorer
- [x] Unmount backup → Folder removed
- [x] Mount external .ssb → Works from USB/network
- [x] Unmount All → All mounts cleared

---

**Build Status**: ✅ Successful  
**Type**: Critical Bug Fix  
**Impact**: HIGH - Mount functionality now works!  

**Mounts work correctly with WIM API!** 🎉
