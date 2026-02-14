# Named Pipe Communication Diagnostic Guide

## Problem
The UI cannot retrieve the service version via named pipe communication.

## Quick Diagnosis

Run these scripts **in order**:

### 1. Check Service Version
```powershell
.\Check-ServiceVersion.ps1
```

**What it checks:**
- Which version of BackupService.exe is installed as a Windows Service
- Whether latest build is newer than installed service
- File timestamps and version numbers

**Expected output:**
- Should show matching versions between installed service and latest build
- If build is newer, you need to reinstall

### 2. Test Named Pipe
```powershell
.\Test-NamedPipe.ps1
```

**What it tests:**
- Service is running
- Named pipe exists
- Can connect to pipe
- Can send/receive messages
- GetVersion command works

**Expected output:**
```
Service Status: Running
Found pipe: \\.\pipe\BackupRestoreServicePipe
Connected successfully!
Success: True
Message: 5.13.3.8
```

## Solution

If either test fails:

### Reinstall Service (Requires Administrator)
```powershell
.\Reinstall-Service.ps1
```

**What it does:**
1. Stops existing service
2. Deletes old service registration
3. Builds latest code
4. Installs new service
5. Starts service
6. Verifies named pipe exists

Then run the tests again to verify.

## Common Issues

### Issue: "Access Denied" when running Reinstall-Service.ps1
**Solution:** Run PowerShell as Administrator

### Issue: Named pipe not found after service starts
**Possible causes:**
1. BackupServiceCommunication.StartAsync() not being called
2. Exception during pipe creation
3. Service crashed during startup

**Check logs:**
```powershell
Get-Content "C:\ProgramData\BackupRestoreService\startup.log"
Get-Content "C:\ProgramData\BackupRestoreService\startup_error.log"
```

### Issue: Service won't start
**Check Windows Event Log:**
```powershell
Get-EventLog -LogName Application -Source BackupRestoreService -Newest 10
```

### Issue: Build failed
**Solution:**
```powershell
# Clean and rebuild
dotnet clean
dotnet build
```

## Architecture Reference

### Named Pipe Communication Flow

**Server (BackupService):**
1. BackupServiceCommunication implements IHostedService
2. StartAsync() is called when service starts
3. ListenForConnectionsAsync() creates NamedPipeServerStream
4. WaitForConnectionAsync() waits for client
5. HandleClientAsync() processes messages
6. ProcessMessage() handles GetVersion, RunBackup, etc.

**Client (BackupUI):**
1. BackupServiceClient.GetServiceVersionAsync()
2. Creates NamedPipeClientStream
3. Connects with 5-second timeout
4. Sends JSON command: `{"CommandType":"GetVersion","Data":null}`
5. Reads JSON response: `{"Success":true,"Message":"5.13.3.8"}`

**Pipe Name:** `BackupRestoreServicePipe`

### Critical Code Locations

**Server:**
- `BackupService/Services/BackupServiceCommunication.cs`
- `BackupService/Program.cs` - IHostedService registration

**Client:**
- `BackupUI/Services/BackupServiceClient.cs`

**Common Data:**
- `BackupUI/Models/` - Shared between UI and Service

## See Also

- **TROUBLESHOOTING.md** - Detailed troubleshooting guide
- **DEVELOPER_NOTES.md** - Architecture and build system documentation
