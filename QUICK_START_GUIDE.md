# Quick Start Guide - Backup & Restore Solution v3.0.0.1

## Installation & Setup

### Prerequisites
1. **Windows Server 2016+** or **Windows 10/11 Pro/Enterprise**
2. **Visual Studio 2022** with:
   - .NET desktop development workload
   - Desktop development with C++ workload (for modifying C++ code)
3. **Administrator privileges**

### First Time Setup
1. **Clone or open the solution**
2. **Restore NuGet packages**: Right-click solution ? Restore NuGet Packages
3. **Rebuild solution**: Build ? Rebuild Solution (Ctrl+Shift+B)
4. **Run as Administrator**: Visual Studio should be running as Administrator

### If BackupEngine Project Won't Load
See `FIX_CPP_PROJECT.md` for detailed instructions. Quick fix:
- Open Visual Studio Installer
- Modify VS 2022
- Check "Desktop development with C++"
- Install and restart

## Running the Application

### Debug Mode
1. Press F5 or click Debug ? Start Debugging
2. Application will auto-elevate to Administrator if needed
3. Click "Yes" on UAC prompt

### Production Mode
1. Build in Release mode
2. Find executable in `bin\Release\BackupUI.exe`
3. Run as Administrator

## Creating Your First Backup

### Simple File Backup
1. **File ? New Backup**
2. **What to Backup Tab**:
   - Expand tree to see drives and volumes
   - Check the volume containing your files (e.g., "C:\")
   - Click "Expand" to browse specific folders (optional)
3. **Settings Tab**:
   - Backup Name: "MyDocuments Backup"
   - Backup Type: "Full Backup"
   - Destination: Browse to external drive or network share
   - Check "Split into 4.7GB files" for DVD burning
4. **Click "Start Backup"**
5. Wait for completion

### Full System Backup (Windows Server)
1. **File ? New Backup**
2. **What to Backup Tab**:
   - Check your C: drive (boot volume)
   - System state will auto-include (look for [Boot Volume] indicator)
3. **Settings Tab**:
   - Backup Name: "Full System 2026-01-20"
   - Backup Type: "Full Backup"
   - Destination: Network share (\\\\SERVER\\Backups\\)
   - Check all options
4. **Click "Start Backup"**

### Hyper-V VM Backup
1. **File ? New Backup**
2. **What to Backup Tab**:
   - Scroll to "Hyper-V: VM_NAME" entries
   - Check the VMs you want to backup
3. **Settings Tab**:
   - Backup Name: "VM_Backup_Weekly"
   - Backup Type: "Full Backup"
   - Destination: Large storage location
4. **Schedule Tab** (optional):
   - Enable Scheduled Backup
   - Frequency: Weekly
   - Days: Saturday, Sunday
   - Time: 02:00
5. **Click "Save Job"** (schedules it) or **"Start Backup"** (runs now)

## Restoring from Backup

### Restore Files
1. **File ? Restore**
2. **Click "Browse..."**
   - Navigate to backup location
   - Select backup file or folder
3. **Click "Scan Backup"**
   - Application finds all backup parts
   - Lists available restore points
4. **Select Restore Point**
   - Choose the date/version you want
   - Preview shows files in backup
5. **Restore Options**:
   - "Original location" OR "Alternate location"
   - Check "Overwrite existing files" if needed
6. **Click "Start Restore"**
7. **Confirm and wait**

### Restore Entire System
1. Boot from Recovery Environment USB (if available)
2. Or run from Windows:
   - File ? Restore
   - Browse to system state backup
   - Scan backup
   - Select latest full backup point
   - Choose "Original location"
   - **WARNING**: This will overwrite system files
3. Click "Start Restore"
4. Reboot after completion

## Scheduling Backups

### Create Scheduled Backup
1. **File ? New Backup**
2. Configure what to backup
3. **Schedule Tab**:
   - Check "Enable Scheduled Backup"
   - **Daily**: Runs every day at specified time
   - **Weekly**: Select days of week
   - **Monthly**: Select day of month (1-31)
   - **Once**: Runs one time only
4. **Click "Save Job"**
5. Backup service will run it automatically

### View/Edit Schedules
1. **Schedules ? Manage Schedules**
2. See list of all scheduled backups
3. Edit, Delete, or Run Now

## Service Management

### Check Service Status
1. **Service ? Service Management**
2. View if service is installed and running
3. Use Start/Stop/Restart buttons

### Windows Services (Alternative)
1. **Press Win+R**, type `services.msc`
2. Find "Backup Scheduler Service"
3. Right-click ? Properties
4. Set Startup Type to "Automatic"

## Troubleshooting

### DLL Not Found Error
**Symptoms**: "BackupEngine.dll not found" error on startup

**Fix**:
1. Build ? Rebuild Solution
2. Check `bin\Debug` for BackupEngine.dll
3. If missing:
   - Install C++ workload in Visual Studio
   - Build BackupEngine project separately
   - Manually copy DLL to BackupUI output directory

### UAC Prompt Every Time
**Symptoms**: UAC asks for elevation every debug run

**Fix**:
- Run Visual Studio as Administrator permanently:
  - Right-click VS shortcut ? Properties
  - Compatibility tab ? Check "Run as administrator"

### Backup Fails with Access Denied
**Symptoms**: Some files can't be backed up

**Causes**:
- Files in use by another process
- System files requiring special permissions
- Corrupted files

**Fix**:
- Close applications using the files
- Run backup during off-hours
- Use "Volume Shadow Copy" (default) to backup open files

### Restore Crashes
**Symptoms**: App crashes when clicking Restore

**This is fixed in v3.0.0.1!** Use the new restore window:
- Doesn't crash on missing backups
- File picker to select any backup
- Graceful error handling

### Hyper-V VMs Not Showing
**Symptoms**: No Hyper-V systems in tree view

**Causes**:
- Hyper-V not installed
- Not running as Administrator
- WMI service not running

**Fix**:
1. Verify Hyper-V is installed: `Get-WindowsFeature -Name Hyper-V`
2. Check Hyper-V Manager opens: `virtmgmt.msc`
3. Run as Administrator
4. Start WMI service: `net start winmgmt`

## Best Practices

### Backup Strategy
1. **3-2-1 Rule**:
   - 3 copies of data
   - 2 different media types
   - 1 offsite copy

2. **Full + Incremental**:
   - Weekly full backup (Saturday 2 AM)
   - Daily incremental (every night)
   - Reduces backup time and storage

3. **Test Restores**:
   - Monthly: Restore a test file to verify backups work
   - Quarterly: Full restore test to alternate location

### Storage Recommendations
- **Local Backup**: Fast external USB 3.0 drive
- **Network Backup**: NAS with RAID 5/6
- **Offsite Backup**: Cloud storage or rotating external drives
- **Split Files**: Enable for DVD archiving (4.7GB parts)

### Schedule Timing
- **Daily backups**: 2:00 AM - 4:00 AM (low activity)
- **Weekly full**: Sunday 1:00 AM
- **Monthly system state**: 1st of month, 3:00 AM

## Advanced Features

### Clone Backup
Creates bootable drive copy:
1. Select "Clone Backup" type
2. Choose source drive (check entire disk)
3. Select destination drive (must be same size or larger)
4. Use for disaster recovery or migration

### System State Backup
Auto-enabled when you select Windows Server boot volume:
- Active Directory
- Registry
- COM+ Class Registration
- Boot files
- System files

### Recovery Environment
Create bootable USB for bare-metal restore:
1. **Service ? Recovery Environment Creator**
2. Select USB drive
3. Click "Create"
4. Boot from USB to restore without OS

## Keyboard Shortcuts
- **Ctrl+N**: New Backup
- **Ctrl+R**: Restore
- **Ctrl+S**: Manage Schedules
- **F5**: Refresh drive list
- **Ctrl+Q**: Exit

## Getting Help
- Check `VERSION_3.0.0.1_SUMMARY.md` for feature details
- See `DLL_LOADING_FIX.md` for DLL issues
- Review `FIX_CPP_PROJECT.md` for C++ project problems

## Version Information
Current Version: **3.0.0.1**

View version: Check bottom-left of main window or Help ? About
