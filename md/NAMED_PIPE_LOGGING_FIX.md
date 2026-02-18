# Named Pipe Diagnostic Fix - Version 5.13.3.12

## Problem
Named pipe communication hangs when UI tries to get service version. The pipe exists but messages aren't being processed correctly.

## Root Cause
Debug.WriteLine doesn't work in Windows Services, so we had NO visibility into where the hang occurs. Added comprehensive file logging to `BackupServiceCommunication.cs`.

## Changes Made

### Added File Logging
Created `ServiceLog` helper in BackupServiceCommunication that writes to:
```
C:\ProgramData\BackupRestoreService\pipe_debug.log
```

### Logging Added To
1. **StartAsync** - Service startup
2. **ListenForConnectionsAsync** - Pipe creation and client connections
3. **HandleClientAsync** - Message reading/writing
4. **ProcessMessage** - Command processing and GetVersion handling

## How to Use

1. **Stop the service:**
   ```powershell
   Stop-Service BackupRestoreService
   ```

2. **Delete old log** (optional):
   ```powershell
   Remove-Item "C:\ProgramData\BackupRestoreService\pipe_debug.log" -ErrorAction SilentlyContinue
   ```

3. **Rebuild and reinstall:**
   ```powershell
   .\Force-Service-Refresh.ps1
   ```

4. **Try to get version** from UI (it will hang)

5. **Check the log:**
   ```powershell
   Get-Content "C:\ProgramData\BackupRestoreService\pipe_debug.log"
   ```

The log will show EXACTLY where it hangs:
- If it stops after "Waiting for message..." ? Client isn't sending
- If it stops after "Processing message..." ? Server-side processing issue
- If it stops after "Sending response..." ? Response isn't being received

## Expected Log Output

**Normal flow:**
```
[15:32:10.123] StartAsync: Starting named pipe listener...
[15:32:10.125] ListenForConnections: Started on pipe 'BackupRestoreServicePipe'
[15:32:10.126] ListenForConnections: Creating pipe server...
[15:32:10.127] ListenForConnections: Waiting for client...
[15:32:15.234] ListenForConnections: Client connected!
[15:32:15.235] HandleClient: Creating streams...
[15:32:15.236] HandleClient: Entering read loop...
[15:32:15.237] HandleClient: Waiting for message...
[15:32:15.245] HandleClient: Received message: {"CommandType":"GetVersion",...
[15:32:15.246] HandleClient: Processing message...
[15:32:15.247] ProcessMessage: Deserializing...
[15:32:15.248] ProcessMessage: Command type = GetVersion
[15:32:15.249] ProcessMessage: GetVersion case
[15:32:15.250] ProcessMessage: Returning version: 5.13.3.12
[15:32:15.251] HandleClient: Sending response: {"Success":true,"Message":"5...
[15:32:15.252] HandleClient: Response sent
```

This will pinpoint the exact hang location!
