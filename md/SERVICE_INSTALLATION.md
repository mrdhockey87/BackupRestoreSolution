# BackupService Installation and Troubleshooting

## Issue: Backups Not Starting

If you click "Run Now" on a backup job and nothing happens (no progress window, no CPU activity), the **BackupRestoreService is not installed or not running**.

## Quick Solution

### 1. Install the Service (First Time Setup)

Open PowerShell **as Administrator** and run:

```powershell
cd "E:\VisualStudioProjects\BackupRestoreSolution\BackupRestoreSolution"
.\Install-BackupService.ps1
```

This will:
- Install the BackupRestoreService Windows Service
- Configure it to start automatically
- Start the service immediately
- Show the service status

### 2. Verify Service is Running

Check in Windows Services (services.msc) or PowerShell:

```powershell
Get-Service -Name "BackupRestoreService"
```

Status should be **Running**.

### 3. Rebuild After Service Installation

If you need to rebuild the solution after the service is installed:

**STOP THE SERVICE FIRST:**

```powershell
Stop-Service -Name "BackupRestoreService" -Force
```

Then rebuild in Visual Studio. The service locks the BackupService.exe file while running.

**START THE SERVICE AGAIN:**

```powershell
Start-Service -Name "BackupRestoreService"
```

## What the Service Does

The BackupRestoreService is a Windows Service that:
- Runs backups in the background (continues even if UI is closed)
- Executes scheduled backup jobs
- Handles manual "Run Now" backup requests from the UI
- Communicates with the UI via Named Pipes
- Logs backup progress and results

## Uninstalling the Service

If you need to remove the service:

```powershell
.\Uninstall-BackupService.ps1
```

## Service Architecture

```
BackupUI.exe (User Interface)
    ? Named Pipe Communication
BackupService.exe (Windows Service)
    ? P/Invoke
BackupEngine.dll (C++ Backup Engine)
```

When you click "Run Now":
1. UI checks if service is running
2. If not, shows helpful error with installation instructions
3. If running, sends job ID via Named Pipe
4. Service receives request and executes backup
5. UI shows progress window polling service for updates
6. Backup continues even if UI is closed

## Development Workflow

1. **Initial Setup:** Run `Install-BackupService.ps1` once
2. **Daily Development:** 
   - Stop service before building (`Stop-Service BackupRestoreService`)
   - Build solution
   - Start service after building (`Start-Service BackupRestoreService`)
3. **Or:** Uninstall service during development, reinstall when testing backups

## Error Messages

### "Service Not Installed"
The UI will show this if the service doesn't exist. Follow installation instructions above.

### "Service Not Running"
The service exists but is stopped. The UI will offer to start it for you (requires Admin rights).

### "Build Error: File Locked"
The service is running and locking BackupService.exe. Stop the service before building.

## Build Configuration

The service executable location:
- **Debug:** `artifacts\bin\Debug\BackupService.exe`
- **Release:** `artifacts\bin\Release\BackupService.exe`

The install script uses Debug configuration by default. For Release:

```powershell
.\Install-BackupService.ps1 -Configuration Release
```
