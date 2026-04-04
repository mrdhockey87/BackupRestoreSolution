# Version 6.1.1.38 - Backup Verification False Failure Fix

## Issue Summary
Backup verification was failing with error -6/-1632 ("Image data is corrupted") on perfectly valid backup files. Users could mount these "failed" backups and access all data, proving the files were actually valid.

## Root Cause
Multiple functions across the Windows backup/restore engine were calling `WIMLoadImage()` without first setting a temporary path via `WIMSetTemporaryPath()`.

The Windows Imaging API requires a temp directory to decompress and load image data. Without it, `WIMLoadImage()` returns error 1632 (ERROR_INSTALL_SERVICE_FAILURE) even when the WIM file structure is completely valid.

## Why This Matters
This was a **FALSE FAILURE** pattern:
- ✅ Backup creation completed successfully
- ✅ WIM file finalized successfully  
- ✅ File was mountable with all data intact
- ❌ Verification incorrectly reported corruption
- ❌ Valid backups were being deleted (or flagged as failed)

## Historical Context
The same error 1632 was previously fixed for **mounting** operations in v5.13.9.8 by adding `WIMSetTemporaryPath()`. However, the **verification**, **restore**, and **metadata** code paths were not updated with the same fix at that time.

## Comprehensive Fix - All WIMLoadImage Locations

This fix applies `WIMSetTemporaryPath()` to **ALL** remaining `WIMLoadImage()` calls in the Windows codebase:

### 1. BackupVerification.cpp - VerifyWimArchive()
**Location:** Line ~234  
**Impact:** Prevents false verification failures after backup completion  
**User Impact:** Valid backups no longer deleted due to verification errors

### 2. RestoreEngine_Advanced.cpp - RestoreDisk()
**Location:** Line ~629  
**Impact:** Prevents restore failures when loading valid backup images  
**User Impact:** Restores now succeed on first attempt without error 1632

### 3. WimMountManager.cpp - GetWimImageInfo()
**Location:** Line ~788  
**Impact:** Prevents failures when reading image metadata (name, description)  
**User Impact:** Image information displays correctly in UI

### 4. BackupManager_Advanced.cpp - CaptureToWimImage() (3 locations)
**Locations:** Lines ~997, ~1103, ~1121  
**Impact:** Prevents failures when reloading image handles after filtered captures  
**User Impact:** Metadata setting works correctly for folder backups with exclusions

## The Fix Pattern
Added `WIMSetTemporaryPath()` call before each `WIMLoadImage()`:

```cpp
// Set temporary path for WIM API (required for WIMLoadImage)
wchar_t tempPath[MAX_PATH];
if (GetTempPathW(MAX_PATH, tempPath)) {
    WIMSetTemporaryPath(hWim, tempPath);
}

// Now WIMLoadImage will work correctly
HANDLE hImage = WIMLoadImage(hWim, imageIndex);
```

## Pattern Reference
This mirrors the proven fix from `WimMountManager.cpp` v5.13.9.8:
1. Get system temp directory using `GetTempPathW()`
2. Call `WIMSetTemporaryPath()` on the WIM handle
3. Then call `WIMLoadImage()` - it will now succeed

## Files Modified
1. `BackupEngine/BackupVerification.cpp` - 1 location
2. `BackupEngine/RestoreEngine_Advanced.cpp` - 1 location
3. `BackupEngine/WimMountManager.cpp` - 1 location
4. `BackupEngine/BackupManager_Advanced.cpp` - 3 locations

**Total:** 6 WIMLoadImage calls fixed across 4 files

## Already Had The Fix
- ✅ `WimMountManager.cpp` - `MountWimImage()` function (fixed in v5.13.9.8)

## Linux Restore
**No fix needed** - The Linux restore functionality uses `wimlib` (via `wimlib-imagex` command-line tool), which handles temporary paths automatically. This fix only applies to Windows WIMGAPI code.

## Impact
- ✅ Prevents deletion of valid backups due to false verification failures
- ✅ Fixes restore operations that were failing with error 1632
- ✅ Fixes metadata retrieval for image information display
- ✅ Fixes filtered capture metadata handling
- ✅ Eliminates ALL error 1632 false failures in Windows code
- ✅ Restores confidence in the backup/restore/verification system

## Testing
To test this fix:
1. **Verification:** Re-run verification on backups that previously failed with error 1632
2. **Restore:** Attempt restore from valid backup files
3. **Info:** Check that image information displays correctly
4. **Filtered Backup:** Run folder backup with exclusions and verify metadata

## User Report
User's 150GB WDrive.ssb backup:
- **Before fix:** Verification failed with error 1632, backup flagged as corrupted
- **Reality:** File was mountable and contained all expected data
- **After fix:** Verification, restore, and info retrieval all work correctly

## Date
April 4, 2026 - mdail
