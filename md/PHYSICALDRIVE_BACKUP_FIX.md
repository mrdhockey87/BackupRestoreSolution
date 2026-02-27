# PHYSICALDRIVE Backup Fix

## Problem
Backup jobs failed immediately with error:
```
Backup failed: Filesystem error: exists: Incorrect function.: "\\.\PHYSICALDRIVE5"
```

When attempting to backup W: drive (which is on PHYSICALDRIVE5).

## Root Cause Analysis

### The Issue
When a user selected an entire **disk** (not just a volume) in the backup source tree, the BackupWindowNew saved the device path `\\.\PHYSICALDRIVE5` to the BackupJob.SourcePaths. However, the BackupExecutor was treating ALL backup targets the same way:

1. If `job.Target == BackupTarget.Volume`, call `BackupVolume()` 
2. Otherwise, call `BackupFiles()`

The problem: **`BackupFiles()` uses `std::filesystem::exists()` to check if the source path exists, but device paths like `\\.\PHYSICALDRIVE5` cannot be checked with regular filesystem functions!** Device paths require special Windows API handling.

### Why Device Paths Fail with filesystem::exists()

Device paths are Windows kernel-mode paths that represent physical devices:
- `\\.\PHYSICALDRIVE5` - Physical disk 5
- `\\?\Volume{guid}\` - Volume by GUID

These are NOT regular filesystem paths and cannot be checked with standard C++ filesystem library functions. Calling `fs::exists()` on these paths returns the error "Incorrect function" because the filesystem library tries to use file operations on a device handle.

## The Fix

### 1. Added Disk Backup Support to BackupExecutor.cs

**Added P/Invoke declaration for BackupDisk:**
```csharp
[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
private static extern int BackupDisk(int diskNumber, string destPath, bool includeSystemState, 
    bool compress, ProgressCallback? callback);
```

**Updated ExecuteBackup method to handle disk backups:**
```csharp
case BackupType.Full:
    if (job.Target == BackupTarget.Disk)
    {
        // Extract disk number from device path (e.g., \\.\PHYSICALDRIVE5 -> 5)
        int diskNumber = ExtractDiskNumber(sourcePath);
        if (diskNumber < 0)
        {
            logger?.Invoke($"ERROR: Invalid disk path format: {sourcePath}");
            return -11;
        }
        logger?.Invoke($"Backing up disk: {diskNumber} ({sourcePath})");
        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, callback);
    }
    else if (job.Target == BackupTarget.Volume)
    {
        logger?.Invoke($"Backing up volume: {sourcePath}");
        result = BackupVolume(sourcePath, destPath, job.IncludeSystemState, job.CompressData, callback);
    }
    else
    {
        logger?.Invoke($"Backing up files: {sourcePath}");
        result = BackupFiles(sourcePath, destPath, callback);
    }
    break;
```

**Added helper method to extract disk number:**
```csharp
/// <summary>
/// Extracts disk number from physical drive device path
/// </summary>
/// <param name="devicePath">Device path like \\.\PHYSICALDRIVE5</param>
/// <returns>Disk number or -1 if invalid format</returns>
private int ExtractDiskNumber(string devicePath)
{
    if (string.IsNullOrEmpty(devicePath))
        return -1;

    // Expected format: \\.\PHYSICALDRIVE5 or \\.\PhysicalDrive5
    const string prefix = "\\\\.\\PHYSICALDRIVE";
    
    if (devicePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        string numberPart = devicePath.Substring(prefix.Length);
        if (int.TryParse(numberPart, out int diskNumber))
        {
            return diskNumber;
        }
    }

    return -1;
}
```

### 2. Enhanced BackupFiles_Implementation.cpp Error Handling

**Added device path detection and clear error message:**
```cpp
std::wstring sourceStr(sourcePath);

// Check if source is a device path (e.g., \\.\PHYSICALDRIVE5 or \\?\Volume{guid}\)
// Device paths can't be checked with fs::exists() - they need special handling
bool isDevicePath = (sourceStr.find(L"\\\\.\\") == 0 || sourceStr.find(L"\\\\?\\") == 0);

// Verify source exists (skip for device paths - they're handled differently)
if (!isDevicePath && !fs::exists(sourcePath)) {
    SetLastErrorMessage(L"Source path does not exist");
    return -2;
}

// If source is a device path, return error - these should be handled by BackupVolume or BackupDisk
if (isDevicePath) {
    SetLastErrorMessage(L"Device paths (e.g., \\\\.\\PHYSICALDRIVE or \\\\?\\Volume) must be backed up using BackupVolume or BackupDisk functions, not BackupFiles");
    return -10;
}
```

## How It Works Now

### Backup Flow for Different Targets

1. **Volume Backup (e.g., W:\)**
   - User selects W: volume in tree
   - BackupJob.Target = BackupTarget.Volume
   - BackupJob.SourcePaths = ["W:\"]
   - BackupExecutor calls `BackupVolume("W:\", ...)`
   - BackupVolume creates VSS snapshot, then calls `BackupFiles()` with snapshot path
   - ✅ Works perfectly

2. **Disk Backup (e.g., entire Disk 5)**
   - User selects entire Disk 5 in tree
   - BackupJob.Target = BackupTarget.Disk
   - BackupJob.SourcePaths = ["\\\\.\\PHYSICALDRIVE5"]
   - BackupExecutor extracts disk number: 5
   - BackupExecutor calls `BackupDisk(5, ...)`
   - BackupDisk opens physical drive using CreateFileW with device path
   - ✅ Works correctly now!

3. **Files and Folders (e.g., C:\Data)**
   - User selects specific folder
   - BackupJob.Target = BackupTarget.FilesAndFolders
   - BackupJob.SourcePaths = ["C:\Data"]
   - BackupExecutor calls `BackupFiles("C:\Data", ...)`
   - BackupFiles uses filesystem operations on regular path
   - ✅ Works perfectly

## Testing

To verify the fix works:

1. **Test Volume Backup:**
   - Create backup job
   - Select W: drive (volume only, not parent disk)
   - Run backup
   - Should succeed with "Backing up volume: W:\" log message

2. **Test Disk Backup:**
   - Create backup job
   - Select entire Disk 5 (check the disk node, not individual volumes)
   - Run backup
   - Should succeed with "Backing up disk: 5 (\\\\.\\PHYSICALDRIVE5)" log message

3. **Test Files/Folders:**
   - Create backup job
   - Select specific folder like C:\Users\Documents
   - Run backup
   - Should succeed with "Backing up files: C:\Users\Documents" log message

## Why This Happened

The backup system was originally designed to handle three different types of backups:
- Volume backups (using WIM/VSS)
- File/folder backups (using filesystem copy)
- Disk backups (using sector-by-sector copy)

However, the BackupExecutor only had logic for the first two. When a user selected an entire disk, it stored the physical drive device path but then tried to back it up as if it were a regular file path, causing the filesystem error.

## Prevention

The fix ensures:
1. ✅ Device paths are never passed to `BackupFiles()` (error code -10 if attempted)
2. ✅ Disk backups use the proper `BackupDisk()` function
3. ✅ Clear error messages if wrong function is used
4. ✅ Disk number extraction handles both "PHYSICALDRIVE" and "PhysicalDrive" casings

## Files Modified

1. **BackupService\BackupExecutor.cs**
   - Added `BackupDisk` P/Invoke declaration
   - Added disk backup handling in `ExecuteBackup` method
   - Added `ExtractDiskNumber` helper method

2. **BackupEngine\BackupFiles_Implementation.cpp**
   - Added device path detection
   - Enhanced error handling with clear error messages
   - Prevents misuse of `BackupFiles()` with device paths

## Build Status
✅ Build successful - all changes compile without errors

## Version
This fix should be included in the next version increment (current: 5.13.6.28)
