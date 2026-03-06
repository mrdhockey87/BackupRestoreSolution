# Device Path Auto-Detection Fix

## Issue
User attempted to run backup job "WDrive" which was targeting physical drive `\\.\PHYSICALDRIVE5`, but the backup failed with error:
```
Device paths (e.g., \\.\PHYSICALDRIVE or \\?\Volume) must be backed up using BackupVolume or BackupDisk functions, not BackupFiles
```

## Root Cause
The backup job was saved with `BackupTarget.Files` instead of `BackupTarget.Disk`. When `ExecuteBackup()` method was called, it followed the switch logic and called `BackupFiles()` instead of `BackupDisk()`.

While the code in `BackupWindowNew.xaml.cs` (lines 2233-2237) correctly sets `job.Target = BackupTarget.Disk` when a physical drive is selected, existing jobs that were created before version 5.13.6.29 or with incorrect settings would still have the wrong target type.

## Solution
Added defensive device path detection at the beginning of `ExecuteBackup()` method in `BackupService\BackupExecutor.cs`. The method now:

1. **Auto-detects device paths** before executing backup
2. **Automatically corrects job.Target** if mismatch detected
3. **Logs the auto-correction** for visibility

### Implementation

```csharp
private int ExecuteBackup(BackupJob job, string sourcePath, string destPath, 
    ProgressCallback? callback, Action<string>? logger)
{
    int result;

    // DEFENSIVE FIX: Auto-detect if sourcePath is actually a device path but job.Target is wrong
    if (sourcePath.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase) ||
        sourcePath.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
    {
        if (sourcePath.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
        {
            logger?.Invoke($"AUTO-CORRECT: Detected device path (PHYSICALDRIVE) - treating as Disk backup");
            job.Target = BackupTarget.Disk;
        }
        else if (sourcePath.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
        {
            logger?.Invoke($"AUTO-CORRECT: Detected device path (Volume GUID) - treating as Volume backup");
            job.Target = BackupTarget.Volume;
        }
    }

    switch (job.Type)
    {
        case BackupType.Full:
            if (job.Target == BackupTarget.Disk)
            {
                // Extract disk number and call BackupDisk()
                ...
```

## Detection Logic

The auto-detection checks for two types of device paths:

1. **Physical Drive Paths**: `\\.\PHYSICALDRIVE<N>`
   - Example: `\\.\PHYSICALDRIVE5`
   - Auto-corrects to: `BackupTarget.Disk`

2. **Volume GUID Paths**: `\\?\Volume{<GUID>}`
   - Example: `\\?\Volume{12345678-1234-1234-1234-123456789ABC}`
   - Auto-corrects to: `BackupTarget.Volume`

## Benefits

✅ **Backward Compatibility**: Old jobs with incorrect target types now work  
✅ **User-Friendly**: No need to recreate existing jobs  
✅ **Defensive Programming**: Handles edge cases gracefully  
✅ **Clear Logging**: AUTO-CORRECT message shows exactly what happened  
✅ **Zero Breaking Changes**: Doesn't affect correctly configured jobs  

## Testing

### Test Case 1: Physical Drive with Wrong Target
- **Job Settings**: SourcePath = `\\.\PHYSICALDRIVE5`, Target = Files
- **Expected**: Auto-detects as Disk, calls BackupDisk(5, ...)
- **Log Output**: "AUTO-CORRECT: Detected device path (PHYSICALDRIVE) - treating as Disk backup instead of Files"

### Test Case 2: Volume GUID with Wrong Target
- **Job Settings**: SourcePath = `\\?\Volume{GUID}`, Target = Files
- **Expected**: Auto-detects as Volume, calls BackupVolume(...)
- **Log Output**: "AUTO-CORRECT: Detected device path (Volume GUID) - treating as Volume backup instead of Files"

### Test Case 3: Regular File Path
- **Job Settings**: SourcePath = `C:\Data`, Target = Files
- **Expected**: No auto-correction, executes normally
- **Log Output**: "Backing up files: C:\Data"

### Test Case 4: Correctly Configured Disk
- **Job Settings**: SourcePath = `\\.\PHYSICALDRIVE5`, Target = Disk
- **Expected**: No auto-correction needed, executes normally
- **Log Output**: "Backing up disk: 5 (\\.\PHYSICALDRIVE5)"

## User Action (Optional)

While the auto-correction fix handles the issue automatically, users can optionally:

1. Open Backup & Restore application
2. Click "Edit" on the "WDrive" job
3. Verify the physical drive is checked in the tree view
4. Click "Save" to update the job with correct BackupTarget

This will persist the correct setting in the jobs.json file.

## Related Fixes

This complements version 5.13.6.29 which added:
- BackupDisk P/Invoke declaration
- Enhanced ExecuteBackup method to detect BackupTarget.Disk
- ExtractDiskNumber() helper method
- BackupFiles_Implementation.cpp device path detection

The auto-correction adds a safety net for cases where the BackupTarget wasn't set correctly during job creation.

## Version
This fix is included in version **5.13.6.35**

## Files Modified
- `BackupService\BackupExecutor.cs` - Added device path auto-detection before switch statement

## Build Status
✅ Build successful - all changes compile without errors
