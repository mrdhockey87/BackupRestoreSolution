# Testing Guide - Incremental Backup Fix v5.13.8.6

## Overview
This guide helps you verify the WIM_FLAG_REFERENCE fix for incremental/differential disk backups.

## Prerequisites
- BackupEngine.dll rebuilt with fix (version 5.13.8.6)
- BackupRestoreService stopped and updated
- Clean test environment (delete previous failed backups)

## Test Scenario 1: Incremental Backup Chain

### Step 1: Clean Slate
```
1. Delete X:\BackupApplications\WDrive1\WDrive1.ssb (if exists)
2. Delete any temporary files (*.tmp, ~WIM*.tmp)
3. Stop BackupRestoreService if running
4. Copy new BackupEngine.dll to service directory
5. Start BackupRestoreService
```

### Step 2: Full Backup (Day 1)
```
1. Open BackupUI
2. Select WDrive1 job (or create new disk backup for Disk 5)
3. Set backup type: Full Backup
4. Click "Run Now"
5. Expected: Creates WDrive1.ssb with 4 images
6. Verify: File size should be ~2.1TB (actual data size)
```

### Step 3: First Incremental (Day 2)
```
1. Wait 5 minutes or modify some files on Disk 5
2. Change backup type to: Incremental
3. Click "Run Now"
4. Expected: Opens WDrive1.ssb with WIM_FLAG_REFERENCE
5. Expected: Adds 4 new referential images
6. Expected: SUCCESS (not error -4!)
7. Verify: File size increases by changed data only (~50GB)
```

### Step 4: Second Incremental (Day 3)
```
1. Modify more files
2. Run incremental backup again
3. Expected: Adds 4 more referential images
4. Expected: File now has 12 images total (4+4+4)
5. Verify: File size increases again by changed data only
```

## Test Scenario 2: Differential Backup Chain

### Step 1: Full Backup (Base)
```
Same as Incremental test Step 1-2
```

### Step 2: First Differential
```
1. Change backup type to: Differential
2. Click "Run Now"
3. Expected: Opens WDrive1.ssb with WIM_FLAG_REFERENCE
4. Expected: Adds 4 images referencing FIRST (full) backup
5. Expected: SUCCESS
```

### Step 3: Second Differential
```
1. Modify different files
2. Run differential backup again
3. Expected: Adds 4 more images still referencing FIRST backup
4. Expected: Each differential is independent of others
```

## Verification Checklist

### During Backup
- [ ] No error -4 "Failed to open existing backup"
- [ ] Progress window shows 0-100% progress
- [ ] No exceptions in Activity log
- [ ] DebugView shows "[BackupDisk] Opening existing backup..."

### After Backup
- [ ] WDrive1.ssb file exists
- [ ] File size increases appropriately
- [ ] No .tmp files left behind
- [ ] Activity log shows "Backup completed successfully"

### Image Count Verification
Use WimMount_GetImageCount to verify:
```csharp
// After full backup: 4 images
// After 1st incremental: 8 images (4+4)
// After 2nd incremental: 12 images (4+4+4)
```

## Common Issues

### Issue: Still getting error -4
**Cause:** Old BackupEngine.dll not replaced  
**Fix:** 
1. Stop service
2. Copy new BackupEngine.dll to bin folder
3. Restart service

### Issue: File not growing after incremental
**Cause:** No data changed on disk  
**Fix:** Modify some files before running incremental

### Issue: "File exists after failure: True"
**Cause:** Full backup succeeded but incremental failed  
**Fix:** This was the original bug - should be fixed now!

## DebugView Monitoring

### What to Watch For
```
[BackupDisk] Starting backup of Disk 5 to path...
[BackupDisk] Opening existing backup with WIM_FLAG_REFERENCE...
[BackupDisk] Found 4 volume(s) for incremental backup
[BackupDisk] Creating incremental image 1 of 4...
[BackupDisk] VSS snapshot created: \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1
[BackupDisk] Capturing to WIM: Disk 5 Volume 1
[BackupDisk] Volume 1 captured successfully
[BackupDisk] Creating incremental image 2 of 4...
... (repeat for volumes 2-4) ...
[BackupDisk] All volumes captured, finalizing WIM...
[BackupDisk] WIM file closed successfully
[BackupDisk] Backup completed successfully!
```

### Red Flags
```
[BackupDisk] ERROR: Failed to open existing backup (THIS SHOULD NOT HAPPEN!)
[BackupDisk] EXCEPTION: (ANY EXCEPTION IS BAD)
[BackupDisk] No volumes found on Disk 5 (DISK OFFLINE?)
```

## Success Criteria

✅ Full backup creates base .ssb file  
✅ Incremental backup opens file with WIM_FLAG_REFERENCE  
✅ Incremental adds new images without error -4  
✅ Multiple incrementals work (3+ successful runs)  
✅ File size grows proportionally to changed data  
✅ No .tmp files left behind  
✅ Activity log shows all successes  
✅ Can mount and browse any image in the .ssb  

## Rollback Plan

If test fails:
1. Stop BackupRestoreService
2. Replace BackupEngine.dll with previous version
3. Delete failed WDrive1.ssb
4. Restart service
5. Report issue with DebugView logs

## Reporting Results

### Success Report
```
✅ Version 5.13.8.6 tested successfully
✅ Full backup: X GB in Y minutes
✅ 1st incremental: +A GB in B minutes (no error -4!)
✅ 2nd incremental: +C GB in D minutes
✅ Total images: 12 (4 full + 4 inc + 4 inc)
✅ Ready for production
```

### Failure Report
```
❌ Version 5.13.8.6 test failed
❌ Step: [which step failed]
❌ Error: [exact error message]
❌ DebugView: [attach logs]
❌ File state: [.ssb exists? size? images?]
```

---

**Remember:** The fix adds `WIM_FLAG_REFERENCE` to enable referential images. Without this flag, the WIM API cannot create incremental/differential backups properly!
