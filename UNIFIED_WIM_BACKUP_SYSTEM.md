# Unified WIM Backup System with .SSB Extension

## Overview
This document describes the changes needed to unify all backup types (Disk, Volume, Files/Folders) to use WIM format with VSS and `.ssb` file extension.

## Current Problems

1. **Disk backups** create raw `.img` files instead of WIM format
2. **File/Folder backups** copy files directly instead of using WIM
3. **Extension** is `.img` for disks, folders for others - should be `.ssb` for all
4. **Incremental backups** fail because BackupFiles doesn't support incremental format
5. **Inconsistent format** makes restore complex and doesn't leverage WIM benefits

## Proposed Solution

### 1. All Backups Use WIM Format

**Benefits of WIM:**
- Built-in compression
- Incremental/Differential support
- File-level deduplication
- Integrity checking
- Microsoft-supported format
- Works with VSS

### 2. File Extension: `.ssb` (Silver State Backup)

All backup files will use `.ssb` extension:
- `WDrive_Full_20260227.ssb` - Full disk backup
- `WDrive_Incremental_20260227.ssb` - Incremental backup
- `CVolume_Full_20260227.ssb` - Volume backup
- `MyFiles_Full_20260227.ssb` - Files/Folders backup

### 3. Backup Structure

Each backup creates a **folder** containing:
```
WDrive_Full_20260227/
├── WDrive.ssb              (Main WIM file)
├── metadata.json           (Backup metadata)
└── SystemState/            (Optional - if system state included)
    ├── registry/
    └── boot/
```

For disk backups with multiple volumes:
```
WDrive_Full_20260227/
├── disk5_vol1.ssb          (Volume 1 WIM)
├── disk5_vol2.ssb          (Volume 2 WIM)
├── disk5_vol3.ssb          (Volume 3 WIM)
└── metadata.json
```

## Implementation Changes

### A. BackupEngine C++ Changes

#### 1. BackupManager_Advanced.cpp - BackupDisk
**STATUS: ✅ DONE (see updated code above)**

Changed from creating raw `.img` file to:
1. Enumerate volumes on the disk
2. Create VSS snapshot for each volume
3. Capture each volume to separate WIM file (`.ssb` extension)
4. Benefits: compression, incremental support, consistency

#### 2. BackupFiles_Implementation.cpp - BackupFiles
**STATUS: ⏳ NEEDS UPDATE**

Current: Copies files recursively
Needed: Create WIM file capturing specified path

```cpp
BACKUPENGINE_API int BackupFiles(
    const wchar_t* sourcePath,
    const wchar_t* destPath,
    ProgressCallback callback) {
    
    // Create VSS snapshot of volume containing sourcePath
    VSSSnapshotManager vss;
    std::wstring snapshotPath = GetVolumeForPath(sourcePath);
    
    if (!vss.CreateSnapshot(snapshotPath)) {
        // Fall back to direct backup
    }
    
    // Build .ssb file path
    std::wstring ssbFile = std::wstring(destPath) + L"\\backup.ssb";
    
    // Create WIM file
    HANDLE hWim = WIMCreateFile(ssbFile.c_str(), 
        WIM_GENERIC_WRITE,
        WIM_CREATE_ALWAYS,
        WIM_FLAG_COMPRESS_LZMS,
        WIM_COMPRESS_LZMS,
        NULL);
        
    // Capture files to WIM
    HANDLE hImage = WIMCaptureImage(hWim, 
        sourcePath,
        WIM_FLAG_VERIFY);
        
    // Cleanup
    WIMCloseHandle(hImage);
    WIMCloseHandle(hWim);
    vss.DeleteSnapshot();
    
    return 0;
}
```

#### 3. BackupManager_Advanced.cpp - BackupVolume
**STATUS: ⏳ NEEDS UPDATE**

Current: Calls BackupFiles which copies recursively
Needed: Create WIM capturing entire volume

```cpp
BACKUPENGINE_API int BackupVolume(
    const wchar_t* volumePath,
    const wchar_t* destPath,
    bool includeSystemState,
    bool compress,
    ProgressCallback callback) {
    
    // Create VSS snapshot
    VSSSnapshotManager vss;
    vss.CreateSnapshot(volumePath);
    std::wstring snapshotPath = vss.GetSnapshotPath();
    
    // Build .ssb file path  
    std::wstring ssbFile = std::wstring(destPath) + L"\\volume.ssb";
    
    // Create WIM file
    // ... (same as BackupFiles above)
    
    // System state backup (separate files/folders)
    if (includeSystemState) {
        BackupSystemState(destPath, callback);
    }
    
    return 0;
}
```

#### 4. CreateIncrementalBackup / CreateDifferentialBackup
**STATUS: ⏳ NEEDS IMPLEMENTATION**

WIM supports incremental backups natively:

```cpp
BACKUPENGINE_API int CreateIncrementalBackup(
    const wchar_t* sourcePath,
    const wchar_t* destPath,
    const wchar_t* baseBackupPath,
    ProgressCallback callback) {
    
    // Open base WIM file
    std::wstring baseWim = FindWimFile(baseBackupPath);
    HANDLE hBaseWim = WIMLoadImage(baseWim.c_str(), 1);
    
    // Create new WIM file
    std::wstring newWim = std::wstring(destPath) + L"\\backup.ssb";
    HANDLE hWim = WIMCreateFile(newWim.c_str(), ...);
    
    // Capture ONLY changed files (WIM handles this)
    HANDLE hImage = WIMCaptureImage(hWim, 
        sourcePath,
        WIM_FLAG_VERIFY | WIM_FLAG_REFERENCE,
        hBaseWim); // Reference to base
        
    return 0;
}
```

### B. BackupService C# Changes

#### 1. BackupExecutor.cs - ExecuteBackup
**STATUS: ⏳ NEEDS UPDATE**

Currently calls:
- `BackupDisk()` - ✅ Now creates WIM files
- `BackupVolume()` - ⏳ Needs WIM update
- `BackupFiles()` - ⏳ Needs WIM update

#### 2. Fix Incremental Logic
**STATUS: ⏳ NEEDS UPDATE**

The error "Filesystem error in incremental backup" occurs because:
1. Incremental backup calls `CreateIncrementalBackup()`
2. That function tries to use filesystem operations on WIM files
3. WIM files aren't filesystem paths - they're archives

**Solution:**
- Implement proper `CreateIncrementalBackup()` in C++
- Use WIM API's built-in incremental support
- Reference base WIM file when creating incremental

### C. Restore Changes

#### Restore Operations Need Update

Currently: Restore directly from files/IMG
Needed: Extract from WIM files

```cpp
BACKUPENGINE_API int RestoreDisk(...) {
    // Find all .ssb files in backup folder
    // Extract each WIM to corresponding volume
}

BACKUPENGINE_API int RestoreVolume(...) {
    // Find volume.ssb file
    // Extract WIM to target volume
}

BACKUPENGINE_API int RestoreFiles(...) {
    // Find backup.ssb file
    // Extract specific files/folders from WIM
}
```

## Migration Strategy

### Phase 1: Disk Backups (DONE ✅)
- Updated BackupDisk to create WIM files
- Each volume on disk gets separate `.ssb` file
- Uses VSS for consistency

### Phase 2: Volume Backups (TODO)
- Update BackupVolume to create single WIM file
- Use `.ssb` extension
- Keep system state separate

### Phase 3: File/Folder Backups (TODO)
- Update BackupFiles to create WIM file
- Capture specified path to WIM
- Use VSS when possible

### Phase 4: Incremental Support (TODO)
- Implement CreateIncrementalBackup using WIM API
- Fix base backup detection
- Test incremental chain

### Phase 5: Restore Support (TODO)
- Update all restore functions
- Extract from WIM instead of direct copy
- Support selective file restore

### Phase 6: Testing
- Full backup → incremental → restore
- Disk backup with multiple volumes
- Files/Folders backup → selective restore

## Benefits of This Approach

1. **Unified Format**: All backups use same WIM format
2. **Compression**: Built-in LZMS compression saves space
3. **Incremental Support**: Native WIM incremental backups
4. **VSS Integration**: Consistent point-in-time backups
5. **Deduplication**: WIM automatically dedupes files
6. **Integrity**: Built-in verification
7. **Standards-Based**: Microsoft-supported format
8. **Flexibility**: Can mount WIM as read-only drive
9. **Cross-Platform**: WIM tools available on Linux

## Current Status

- ✅ BackupDisk updated to WIM format
- ⏳ BackupVolume needs WIM update
- ⏳ BackupFiles needs WIM update  
- ⏳ CreateIncrementalBackup needs implementation
- ⏳ Restore functions need WIM support
- ⏳ .img/.brs → .ssb extension migration

## Next Steps

1. **Immediate**: Implement BackupVolumeToWIM helper function
2. **Next**: Update BackupFiles to use WIM
3. **Then**: Implement CreateIncrementalBackup with WIM API
4. **Finally**: Update all restore functions
5. **Test**: Full backup → incremental → restore cycle

## Version
Target version: **5.13.7.0**
- Major version bump due to breaking backup format change
- Existing backups remain readable (backward compat)
- New backups use unified WIM format with .ssb extension
