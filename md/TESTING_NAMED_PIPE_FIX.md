# Testing the Named Pipe Fix (Version 5.13.2.8)

## Quick Test Instructions

### 1. Stop and Reinstall the Service

Open PowerShell **as Administrator**:

```powershell
# Stop the current service
Stop-Service -Name "BackupRestoreService" -Force

# Reinstall with the new version
.\Uninstall-BackupService.ps1
.\Install-BackupService.ps1
```

### 2. Verify Service is Running

```powershell
Get-Service -Name "BackupRestoreService"
```

Should show **Status: Running**

### 3. Check Named Pipe is Active (Optional)

```powershell
[System.IO.Directory]::GetFiles("\\.\pipe\") | Select-String "BackupRestoreServicePipe"
```

If the pipe exists, the listener is running!

### 4. Test Backup Execution

1. Launch **BackupUI.exe**
2. Click **"Run Now"** on any backup job
3. **Expected Results**:
   - Progress window appears **immediately**
   - Real-time percentage updates
   - Status messages display
   - BackupService.exe shows CPU activity in Task Manager
   - Backup completes successfully

### 5. Debug Logging (If Issues Persist)

Download [DebugView](https://learn.microsoft.com/en-us/sysinternals/downloads/debugview) to see debug output:

1. Run DebugView as Administrator
2. Enable: Capture > Capture Global Win32
3. Restart BackupService
4. Look for messages like:
   ```
   BackupServiceCommunication: Starting named pipe listener...
   BackupServiceCommunication: Started listening for connections
   BackupServiceCommunication: Waiting for client connection...
   ```

When you click "Run Now" in UI:
```
BackupServiceCommunication: Client connected!
BackupServiceCommunication: Received message: {"CommandType":"RunBackup","Data":"..."}
BackupServiceCommunication: Processing command: RunBackup
BackupServiceCommunication: Raising RunBackup event for job: ...
```

## What Changed

### Before (Broken)
```
Service Start
  ?
BackupServiceCommunication created (Singleton)
  ?
Nobody calls Start() method ?
  ?
Named Pipe never listens
  ?
UI tries to connect ? TIMEOUT
  ?
Nothing happens
```

### After (Fixed)
```
Service Start
  ?
IHostedService.StartAsync() called automatically ?
  ?
Named Pipe starts listening
  ?
UI connects successfully
  ?
Backup executes!
```

## Troubleshooting

### Progress Window Doesn't Appear

**Check**: Is the service actually running?
```powershell
Get-Service BackupRestoreService
```

**Check**: Can you see the named pipe?
```powershell
[System.IO.Directory]::GetFiles("\\.\pipe\") | Select-String "BackupRestoreServicePipe"
```

**Check**: Service event log for errors
```powershell
Get-EventLog -LogName Application -Source BackupRestoreService -Newest 10
```

### Service Won't Start

**Check**: Is BackupService.exe accessible?
```powershell
Test-Path "artifacts\bin\Debug\BackupService.exe"
```

**Check**: Permissions issue?
```powershell
# Run as Administrator
.\Install-BackupService.ps1
```

### Still Not Working?

1. **Uninstall Service**: `.\Uninstall-BackupService.ps1`
2. **Clean Build**: In Visual Studio ? Build ? Clean Solution, then Rebuild
3. **Reinstall Service**: `.\Install-BackupService.ps1`
4. **Check Debug Output**: Use DebugView to see what's happening

## Success Criteria

? Service starts without errors  
? Named Pipe appears in `\\.\pipe\`  
? Clicking "Run Now" shows progress window immediately  
? Backup executes with real-time updates  
? BackupService.exe shows CPU activity during backup  
? Backup completes successfully  

## Next Steps

After confirming the fix works:
1. Test with different backup types (Full, Incremental, Differential)
2. Test multiple simultaneous backups
3. Test abort functionality
4. Test scheduled backups (wait for scheduled time or adjust clock)

---

**Version**: 5.13.2.8  
**Date**: 2/13/2026  
**Fixed**: Named Pipe listener now starts automatically via IHostedService
