# Version 3.0.0.1 - Major UI Overhaul

## Overview
This version addresses critical issues and implements a completely redesigned backup/restore experience based on user requirements.

## Critical Fixes

### 1. BackupEngine C++ Project Not Loading
**Problem**: Visual Studio couldn't open BackupEngine.vcxproj with error about missing application type.

**Root Cause**: Visual Studio doesn't have "Desktop development with C++" workload installed.

**Solutions**:
- **Option 1 (Recommended)**: Install C++ workload via Visual Studio Installer
- **Option 2**: Use pre-built DLL without modifying C++ code
- **Option 3**: Unload the C++ project and work only on C# projects

See `FIX_CPP_PROJECT.md` for detailed instructions.

### 2. Restore Window Crashes
**Fixed**: RestoreWindow no longer crashes when no backups exist. Now shows file picker to select backup files.

## Major New Features

### 1. TreeView-Based Backup Selection
**New File**: `BackupUI\Windows\BackupWindowNew.xaml[.cs]`

**Features**:
- **Hierarchical Drive/Volume/File Display**:
  - Drives at top level
  - Volumes under drives (2nd level)
  - Files & folders under volumes (3rd level, expandable)
  
- **Smart Checkbox Logic**:
  - Check drive ? all volumes auto-check
  - Uncheck volume ? drive unchecks
  - Indeterminate state for partial selections
  
- **Auto-Detection**:
  - Boot volumes automatically marked
  - Windows Server detection
  - System state auto-included for server boot volumes
  
- **Hyper-V Integration**:
  - Hyper-V systems shown like drives
  - Virtual volumes listed under VMs
  - Complete VM backup for portability

### 2. Clone Backup Support
**New Option** in backup type dropdown:
- Full clone of entire drive
- Clone to physical drive or virtual drive
- Separate from shadow copy backups

### 3. File Splitting for DVD Backups
**Feature**: Automatically split backup into 4.7GB files (DVD-R size)
- Checkbox option in settings
- Enabled by default
- Files numbered sequentially (backup.001, backup.002, etc.)

### 4. Enhanced Restore Experience
**New File**: `BackupUI\Windows\RestoreWindowNew.xaml[.cs]`

**Features**:
- **No-Crash Design**: Gracefully handles missing backups
- **File/Folder Picker**: Select backup from anywhere
- **Backup Set Scanning**:
  - Automatically finds all parts of split backups
  - Detects full/incremental/differential backups
  - Shows available restore points
  
- **Restore Point Selection**:
  - List of all available backup versions
  - Shows timestamp and type
  - Preview contents before restore
  
- **Flexible Restore Options**:
  - Restore to original location
  - Restore to alternate location
  - Selective file restore
  - Overwrite controls

### 5. Network-Aware Destination Picker
**Improvement**: Backup destination picker is network-aware
- Browse network drives
- UNC path support
- Map network locations

## New Models

### DriveTreeItem.cs
**Purpose**: Hierarchical data structure for drive/volume/file tree

**Properties**:
- `ItemType`: Disk, Volume, Folder, File, HyperVSystem, HyperVVolume
- `IsChecked`: Three-state checkbox (checked/unchecked/indeterminate)
- `IsExpanded`: Expand/collapse state
- `Children`: Nested items
- `Parent`: Parent item for hierarchy
- `IsBootVolume`: Detected boot volumes
- `IsWindowsServer`: Windows Server detection

**Features**:
- INotifyPropertyChanged implementation
- Auto-update of parent/child checkboxes
- Display name formatting with size and status

## Updated Files

### MainWindow.xaml.cs
- Now uses `BackupWindowNew` instead of `BackupWindow`
- Now uses `RestoreWindowNew` instead of `RestoreWindow`
- Maintains backward compatibility

### BackupUI.csproj
- Version updated to 3.0.0
- Ready for new window classes

## Backward Compatibility

**Old Windows Still Available**:
- `BackupWindow.xaml[.cs]` - Original backup window
- `RestoreWindow.xaml[.cs]` - Original restore window

**Easy Rollback**: Change MainWindow.xaml.cs to use old windows if needed.

## Usage Instructions

### Creating a Backup

1. **Launch Application** (auto-elevates to Administrator)
2. **File ? New Backup** (or Ctrl+N)
3. **What to Backup Tab**:
   - Tree shows all drives and volumes
   - Check drives or volumes you want to backup
   - Click "Expand" on volumes to see files/folders
   - Boot volumes show [Boot Volume] indicator
   - Windows Server volumes show [Windows Server] indicator
   - System state automatically included for server boot volumes
4. **Settings Tab**:
   - Enter backup name
   - Select type: Full/Incremental/Differential/Clone
   - If Clone: select destination drive
   - Choose backup destination (network-aware)
   - Options: compress, split files, verify
5. **Schedule Tab** (optional):
   - Enable scheduled backup
   - Set frequency and time
6. **Start Backup** or **Save Job**

### Restoring from Backup

1. **File ? Restore**
2. **Click "Browse..."** to select backup file or folder
3. **Click "Scan Backup"**:
   - Application scans for all backup parts
   - Lists all restore points available
4. **Select Restore Point** from list:
   - Shows backup type and date
   - Preview contents in list below
5. **Configure Restore Options**:
   - Restore all or select specific items
   - Choose original or alternate location
   - Set overwrite and verification options
6. **Click "Start Restore"**
7. **Confirm** and wait for completion

## Implementation Notes

### Shadow Copy vs Clone
- **Shadow Copy** (default): VSS snapshot of individual volumes, fast incremental backups
- **Clone Backup**: Bit-by-bit copy of entire drive, bootable restore

### System State Backup
When backing up boot volume on Windows Server:
1. Application detects Windows Server OS
2. Identifies boot volume
3. Automatically includes system state backup
4. Uses `wbadmin` for system state capture

### File Split Logic
For 4.7GB splits:
```
backup-name.001  (4.7 GB)
backup-name.002  (4.7 GB)
backup-name.003  (2.1 GB)
```

### Backup Set Detection
Restore window scans for:
- Full backups (contain "full" in filename)
- Incremental backups (contain "incremental" in filename)  
- Differential backups (contain "differential" in filename)
- Split files (numbered extensions)

## Testing Checklist

- [ ] Tree view displays all drives and volumes
- [ ] Checkbox cascade works (parent?child)
- [ ] Boot volume detection
- [ ] Windows Server detection
- [ ] Hyper-V system enumeration
- [ ] Clone backup option
- [ ] File splitting to 4.7GB
- [ ] Restore file picker
- [ ] Backup set scanning
- [ ] Restore point selection
- [ ] No crash on missing backups
- [ ] Network path support

## Known Limitations

1. **Hyper-V VM Backup**: Basic enumeration only, full live backup requires Hyper-V API integration
2. **File Split Restore**: Must restore sequentially, cannot skip parts
3. **Clone Backup**: Requires same or larger destination drive
4. **System State**: Windows Server only, client OS uses different method

## Future Enhancements

- [ ] Live Hyper-V VM backup (checkpoint-based)
- [ ] Deduplicate scan across backup sets
- [ ] Cloud storage destination (Azure Blob, AWS S3)
- [ ] Email notifications on completion
- [ ] Backup compression ratio display
- [ ] Estimated time remaining
- [ ] Backup catalog/index for faster restore browsing

## Version History

**3.0.0.1** (Current)
- Major UI redesign with tree view
- Clone backup support
- Enhanced restore with file picker
- 4.7GB file splitting
- Auto system state for Windows Server
- Hyper-V integration improvements

**3.0.0.0**
- Fixed DLL copying issue
- Build process improvements

**2.0.0.0**
- Fixed build errors from initial AI generation
- Added NuGet packages
- Renamed GetLastError to GetLastErrorMessage

**1.0.0.0**
- Initial AI-generated version
- Basic backup/restore functionality
- Hyper-V and Windows Server support
