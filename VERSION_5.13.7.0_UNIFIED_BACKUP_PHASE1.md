# Version 5.13.7.0 - Unified WIM Backup System (Phase 1)

## MAJOR ARCHITECTURAL CHANGE - Direct File Backups with .SSB Extension

This version implements Phase 1 of the unified WIM backup system. All backups now create single files with `.ssb` extension instead of folders with timestamps.

## Changes Made

### 1. File Naming Convention (NEW)

**Old System:**
```
E:\Backups\WDrive_Full_20260227_134514\
    ├── disk_5.img
    └── metadata.json

E:\Backups\WDrive_Incremental_20260227_140000\
    ├── various files
    └── metadata.json
```

**New System:**
```
E:\Backups\
    ├── WDrive_Full.ssb          (Full backup)
    ├── WDrive_Incremental.ssb   (Incremental backup)
    └── WDrive_Differential.ssb  (Differential backup)
```

### 2. BackupService Changes (BackupExecutor.cs)

#### File Path Generation
- **REMOVED**: Timestamp from filenames (`_20260227_134514`)
- **REMOVED**: Folder creation per backup
- **NEW**: Direct file creation: `{DestinationPath}\{JobName}_{Type}.ssb`

#### Backup Type Detection
```csharp
// Check if full backup exists
string fullBackupFile = Path.Combine(job.DestinationPath, $"{job.Name}_Full.ssb");
if (!File.Exists(fullBackupFile))
{
    backupTypeSuffix = "Full"; // Create base backup first
}
else
{
    backupTypeSuffix = job.Type == BackupType.Incremental ? "Incremental" : "Differential";
}
```

#### Retention Logic Simplified
- **REMOVED**: `GetExistingFullBackups()` - no longer needed
- **REMOVED**: `RenameBackupAsPending()` - no file renaming
- **REMOVED**: `RestoreRenamedBackup()` - no rollback needed  
- **REMOVED**: `CleanupOldBackups()` - single file per type

**Why**: Each backup type (Full/Incremental/Differential) has ONE file that gets overwritten. Retention policy not needed with this approach.

#### Verification Changes
- Failed backups: Delete the `.ssb` FILE (not folder)
- Use `File.Exists()` instead of `Directory.Exists()`
- Use `File.Delete()` instead of `Directory.Delete()`

### 3. Backup Discovery Changes

#### FindFullBackup()
```csharp
// OLD: Searched for folders with "Full_" in name
Directory.GetDirectories(destPath, $"{jobName}_*")

// NEW: Checks for specific file
string fullBackupFile = Path.Combine(destPath, $"{jobName}_Full.ssb");
return File.Exists(fullBackupFile) ? fullBackupFile : null;
```

#### FindLastBackup()
```csharp
// OLD: Found most recent folder
Directory.GetDirectories(destPath, $"{jobName}_*").OrderByDescending(d => d)

// NEW: Finds most recent .ssb file
Directory.GetFiles(destPath, $"{jobName}_*.ssb").OrderByDescending(f => File.GetCreationTime(f))
```

### 4. Multiple Source Paths
When a job has multiple source paths (e.g., multiple drives), they're now added as multiple images in the SAME .ssb WIM file:

```csharp
// Execute backup for all source paths
// Multiple sources = multiple images in one WIM file
foreach (var sourcePath in job.SourcePaths)
{
    int result = ExecuteBackup(job, sourcePath, newBackupPath, nativeCallback, logger);
}
```

## Benefits

✅ **Simpler File Management**: One file per backup type, no folder clutter  
✅ **Clear Backup Chain**: `JobName_Full.ssb` → `JobName_Incremental.ssb`  
✅ **No Timestamp Confusion**: File names don't change with each backup  
✅ **Easier Incremental**: Base backup always has predictable name  
✅ **Less Disk Space**: Overwrites old file instead of accumulating  
✅ **Cleaner UI**: Destination folder stays organized  

## Backward Compatibility

⚠️ **BREAKING CHANGE**: This version is NOT backward compatible with old backup format.

**Old backups** (folders with timestamps) will NOT be recognized by new system.

**Migration Path**:
1. Complete any pending backups with old version
2. Upgrade to 5.13.7.0
3. First run will create new Full backup (no base found)
4. Subsequent runs will properly chain incremental/differential

## What Still Needs Implementation

### Phase 2: C++ Backend WIM Support
The C# service now generates correct file names and paths, but the C++ backend still needs updates:

1. **BackupDisk** - Create single WIM with multiple images (one per volume)
2. **BackupVolume** - Create single .ssb WIM file
3. **BackupFiles** - Create single .ssb WIM file  
4. **CreateIncrementalBackup** - Reference base .ssb file with WIM_FLAG_REFERENCE

### Phase 3: Restore Support
- Extract from `.ssb` WIM files instead of copying folders
- Support selective file restoration from WIM
- Handle multiple images in disk backups

## Testing Needed

### Test Case 1: Full Backup
1. Create job "TestBackup"
2. Run backup
3. Verify file created: `E:\Backups\TestBackup_Full.ssb`
4. Verify NO folder created
5. Verify NO timestamp in filename

### Test Case 2: Incremental Backup
1. Configure job as Incremental
2. Run first time (should create Full)
3. Verify: `TestBackup_Full.ssb` created
4. Run second time
5. Verify: `TestBackup_Incremental.ssb` created
6. Verify: Full backup still exists

### Test Case 3: File Overwrite
1. Run Full backup (creates TestBackup_Full.ssb)
2. Note file size/timestamp
3. Run Full backup again
4. Verify: Same filename, updated size/timestamp
5. Verify: Only ONE file exists (no _1, _2, etc.)

### Test Case 4: Multiple Disks
1. Select multiple disks/volumes
2. Run backup
3. Verify: Single .ssb file created
4. Verify: All disks included (TODO: will be multiple WIM images)

## Known Issues

1. **Retention Not Implemented**: Can't keep multiple versions of Full backups
   - Workaround: Copy files manually before running new backup
   - Future: Implement `JobName_Full_1.ssb`, `JobName_Full_2.ssb` versioning

2. **C++ Backend Not Updated**: Still creates .img files or folders
   - This update is C# service-side only
   - Phase 2 will update C++ to create proper WIM files

3. **Restore Will Fail**: Restore expects old folder structure
   - Don't use restore until Phase 3 completed
   - Old backups still restorable with old version

## Migration Steps for Users

If you have existing backups:

**Option 1: Keep Old Backups (Recommended)**
1. Move old backup folders to archive location
2. Upgrade to 5.13.7.0
3. Run new Full backup (creates .ssb file)
4. Keep old backups for restore if needed

**Option 2: Fresh Start**
1. Complete full backup with old version
2. Verify backup is good
3. Upgrade to 5.13.7.0
4. Delete old backup folders
5. Run new Full backup

**Option 3: Wait for Phase 3**
1. Don't upgrade yet
2. Wait for restore support in Phase 3
3. Upgrade when complete cycle implemented

## Code Changes Summary

**Files Modified:**
- `BackupService\BackupExecutor.cs` - Complete restructure of file naming and retention

**Methods Removed:**
- `GetExistingFullBackups()` 
- `RenameBackupAsPending()`
- `RestoreRenamedBackup()`
- `CleanupOldBackups()`

**Methods Modified:**
- `ExecuteBackupJobWithProgress()` - New file naming logic
- `FindFullBackup()` - Check for file instead of folder
- `FindLastBackup()` - Search files instead of folders

**New Behavior:**
- Direct file creation (no folders)
- Fixed naming (no timestamps)
- File overwrite (no retention)
- `.ssb` extension (not .img, not folders)

## Next Steps

1. **Immediate**: Test with simple Full backup
2. **Next**: Update C++ BackupDisk to create WIM
3. **Then**: Update BackupVolume to create WIM
4. **Then**: Update BackupFiles to create WIM
5. **Finally**: Implement restore from WIM

## Version History

- **5.13.6.36**: SVG icons, device path auto-detection
- **5.13.7.0**: **MAJOR**: Unified file-based backup system with .ssb extension

---

**Status**: Phase 1 Complete (C# Service Side)  
**Build**: ✅ Compiles successfully  
**Testing**: ⏳ Needs user testing  
**Production Ready**: ⚠️ Not yet (awaits Phase 2 C++ implementation)
