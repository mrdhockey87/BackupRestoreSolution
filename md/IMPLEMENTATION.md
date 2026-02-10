# Implementation Guide - Windows Server Backup & Restore Solution

## Project Structure

```
BackupRestoreSolution/
??? BackupUI/                          # C# WPF User Interface (.NET 8)
?   ??? Models/                        # Data models
?   ?   ??? BackupJob.cs              # Job definition
?   ?   ??? BackupSchedule.cs         # Schedule configuration
?   ?   ??? BackupTarget.cs           # Target type enum
?   ?   ??? BackupType.cs             # Backup type enum
?   ?   ??? RestoreJob.cs             # Restore job definition
?   ??? Services/                      # Business logic services
?   ?   ??? BackupEngineInterop.cs    # P/Invoke to C++ engine
?   ?   ??? BackupServiceManager.cs   # Windows service control
?   ?   ??? JobManager.cs             # Job persistence and retrieval
?   ??? Windows/                       # UI Windows
?   ?   ??? BackupWindow.xaml[.cs]    # Create/configure backups
?   ?   ??? RestoreWindow.xaml[.cs]   # Restore interface
?   ?   ??? ScheduleManagementWindow.xaml[.cs]  # Manage scheduled jobs
?   ?   ??? ServiceManagementWindow.xaml[.cs]   # Control Windows service
?   ?   ??? RecoveryEnvironmentWindow.xaml[.cs] # Create bootable USB
?   ??? App.xaml[.cs]                 # Application entry point
?   ??? MainWindow.xaml[.cs]          # Main application window
?   ??? app.manifest                  # Admin elevation manifest
?
??? BackupService/                     # C# Windows Service (.NET 8)
?   ??? BackupSchedulerService.cs     # Background service worker
?   ??? BackupExecutor.cs             # Execute backup jobs
?   ??? JobManager.cs                 # Job management (shared logic)
?   ??? Program.cs                    # Service entry point
?
??? BackupEngine/                      # C++ Native DLL (Performance Critical)
    ??? BackupEngine.h                # Main API header
    ??? BackupEngine.cpp              # Core implementation
    ??? BackupManager.cpp             # Backup orchestration
    ??? RestoreEngine.cpp             # Restore operations
    ??? VSSManager.cpp                # Volume Shadow Copy Service
    ??? HyperVManager.cpp             # Hyper-V integration
    ??? HyperVBackup.cpp              # Hyper-V backup logic
    ??? HyperVRestore.cpp             # Hyper-V restore logic
    ??? SystemStateRestore.cpp        # System state operations
    ??? BackupVerification.cpp        # Backup integrity checks
```

## Key Features Implementation

### 1. Backup Types

**Full Backup**
- Copies all selected files/volumes
- Creates baseline for incremental/differential
- Uses VSS for consistent snapshots

**Incremental Backup**
- Only backs up files changed since last backup (full or incremental)
- Smallest backup size, fastest backup time
- Requires full chain for restore

**Differential Backup**
- Backs up all changes since last full backup
- Moderate size, faster than full
- Only needs full backup + latest differential for restore

### 2. Backup Targets

**Disk Backup**
- Backs up entire physical disk
- Uses raw disk access
- Includes partition table and boot sector

**Volume Backup**
- Backs up specific volume (e.g., C:, D:)
- Uses VSS for consistency
- Auto-detects boot volumes for system state inclusion

**Files and Folders**
- Selective backup of specific paths
- Supports exclusion patterns
- Preserves ACLs and attributes

**Hyper-V VMs**
- Uses Hyper-V VSS writer
- Backs up running VMs without downtime
- Includes VM configuration and virtual disks

### 3. System State Backup

For boot volumes, automatically backs up:
- Windows Registry
- Boot files (bootmgr, BCD)
- System files
- Active Directory (on DCs)
- Certificate Store
- COM+ Registration
- IIS Metabase

### 4. Compression

Uses zlib compression algorithm:
- Configurable compression levels
- Typically achieves 40-60% size reduction
- Slight performance impact acceptable for storage savings

### 5. Windows Service Architecture

**BackupSchedulerService** runs as Windows Service:
- Checks for due jobs every minute
- Executes jobs via BackupExecutor
- Updates next run time after execution
- Logs all activities to file

**Installation**:
```
sc create BackupRestoreService binPath= "C:\Path\To\BackupService.exe" start= auto
sc start BackupRestoreService
```

### 6. VSS Integration

Volume Shadow Copy Service provides:
- Consistent point-in-time snapshots
- No application downtime
- Coordination with VSS writers (SQL, Exchange, Hyper-V, etc.)

Implementation:
1. Initialize VSS
2. Add volumes to snapshot set
3. Create snapshot
4. Backup from snapshot
5. Delete snapshot

### 7. Hyper-V Integration

**Backup**:
- Uses Hyper-V VSS writer
- Exports VM configuration
- Backs up VHDX/VHD files
- Supports running VMs

**Restore**:
- Can restore to original VM
- Can create new VM from backup
- Supports converting physical backup to Hyper-V VM

**Bare-Metal to Hyper-V**:
- Restore physical disk backup as VHDX
- Create VM configuration
- Attach VHDX as boot disk
- Inject Hyper-V drivers if needed

### 8. Recovery Environment

Creates bootable USB with:
- Windows PE (WinPE) environment
- Backup/Restore program
- Network drivers
- Storage drivers
- Simple UI for disaster recovery

Process:
1. Format USB as bootable
2. Install WinPE boot files
3. Copy restore program
4. Copy drivers
5. Create boot configuration

## C++ BackupEngine Implementation Details

### Core Components

**VSSManager**
- Manages Volume Shadow Copy Service
- Creates/deletes snapshots
- Handles VSS writers

**BackupManager**
- Orchestrates backup operations
- Handles file copying with progress
- Implements compression
- Manages metadata

**RestoreEngine**
- Restores files from backup
- Decompresses data
- Preserves file attributes
- Handles system state restore

**HyperVManager**
- WMI integration with Hyper-V
- Enumerates VMs
- Controls VM state
- Exports/imports VMs

### Error Handling

All functions return:
- `0` on success
- Non-zero error code on failure

Last error retrievable via `GetLastError()` function

### Progress Callbacks

Functions accept optional callback:
```cpp
typedef void (*ProgressCallback)(int percentage, const wchar_t* message);
```

Called periodically during long operations with:
- Percentage complete (0-100)
- Status message

## C# UI Implementation Details

### WPF Architecture

**MVVM-Like Pattern**:
- Windows are Views
- Code-behind contains view logic
- Services contain business logic
- Models define data structures

**Key Services**:

**BackupEngineInterop**
- P/Invoke declarations
- Marshaling between C# and C++
- Callback handling

**JobManager**
- Persists jobs to JSON
- Retrieves scheduled jobs
- Calculates next run times

**BackupServiceManager**
- Controls Windows service
- Install/uninstall service
- Start/stop/restart service

### Data Flow

1. **User creates backup in UI**
   ? BackupWindow collects settings
   ? Creates BackupJob object
   ? Saves to JobManager (JSON file)
   ? Optionally executes immediately via BackupEngineInterop

2. **Service executes scheduled backup**
   ? BackupSchedulerService checks for due jobs
   ? JobManager returns due jobs
   ? BackupExecutor calls BackupEngine DLL
   ? Updates job's LastRunTime and NextRunTime
   ? Logs to file

3. **User restores from backup**
   ? RestoreWindow selects backup
   ? Lists contents via ListBackupContents()
   ? Executes restore via RestoreFiles/RestoreVolume()
   ? Shows progress

## Configuration and Storage

### Job Storage
`C:\ProgramData\BackupRestoreService\jobs.json`

JSON structure:
```json
[
  {
    "Id": "guid",
    "Name": "Daily System Backup",
    "Type": 0,  // Full
    "Target": 1,  // Volume
    "SourcePaths": ["C:\\"],
    "DestinationPath": "D:\\Backups",
    "IncludeSystemState": true,
    "CompressData": true,
    "VerifyAfterBackup": true,
    "Schedule": {
      "Enabled": true,
      "Frequency": 0,  // Daily
      "Time": "02:00:00",
      "NextRunTime": "2024-01-15T02:00:00"
    }
  }
]
```

### Service Log
`C:\ProgramData\BackupRestoreService\service.log`

Format:
```
2024-01-14 02:00:00 - Executing scheduled job: Daily System Backup
2024-01-14 02:00:05 - Backing up volume: C:\
2024-01-14 02:15:32 - Job completed successfully: Daily System Backup
```

## Security Considerations

1. **Administrator Rights Required**
   - VSS requires admin
   - Disk/volume access requires admin
   - Service installation requires admin
   - Hyper-V access requires admin

2. **File Permissions**
   - Backup preserves ACLs
   - Restore applies original ACLs
   - Service runs as Local System

3. **Sensitive Data**
   - No encryption implemented (feature for future)
   - Backups should be stored securely
   - System state contains sensitive info

## Performance Optimization

1. **Multi-threading**
   - File copy operations use async I/O
   - Compression can be parallelized
   - Multiple backup jobs can run concurrently

2. **Memory Management**
   - Stream data instead of loading entire files
   - Use memory-mapped files for large files
   - Release VSS snapshots promptly

3. **Disk I/O**
   - Sequential reads preferred
   - Batch small files together
   - Use larger buffer sizes for large files

## Testing Checklist

- [ ] Full backup of volume
- [ ] Incremental backup chain
- [ ] Differential backup
- [ ] File/folder selective backup
- [ ] Hyper-V VM backup (running VM)
- [ ] File restore to original location
- [ ] File restore to custom location
- [ ] Volume restore
- [ ] System state restore
- [ ] Hyper-V VM restore
- [ ] Physical to Hyper-V restore
- [ ] Backup verification
- [ ] Scheduled job execution
- [ ] Service install/uninstall
- [ ] Service start/stop
- [ ] Recovery USB creation
- [ ] Boot from recovery USB
- [ ] Restore from recovery environment

## Known Limitations

1. **File System Support**
   - NTFS fully supported
   - ReFS supported for basic operations
   - FAT32 not recommended for backups

2. **Size Limits**
   - Individual file: Limited by file system
   - Backup set: Limited by destination storage
   - VSS snapshot: Limited by available disk space

3. **Platform Support**
   - Windows Server 2019, 2022, 2025
   - Client SKUs not officially supported
   - Hyper-V features require Hyper-V role

## Future Enhancements

1. **Encryption**: AES-256 encryption for backup data
2. **Cloud Integration**: Azure Blob, AWS S3, etc.
3. **Deduplication**: Block-level deduplication
4. **Email Notifications**: Success/failure alerts
5. **Web Interface**: Browser-based management
6. **Central Management**: Manage multiple servers
7. **Bare-Metal Backup**: Complete system image
8. **Active Directory Integration**: User-based policies
