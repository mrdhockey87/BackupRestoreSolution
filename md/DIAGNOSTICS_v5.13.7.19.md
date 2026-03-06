# Version 5.13.7.19 - Comprehensive Diagnostics

## What This Version Does

This version adds **extensive logging** to diagnose exactly WHY backups are failing and WHY they're retrying.

## Log Locations

Logs are written to **TWO locations**:

### 1. Service Log (ALL service events)
```
C:\ProgramData\BackupRestoreService\service.log
```

### 2. Per-Job Logs (Job-specific activity)
```
C:\ProgramData\BackupRestoreService\Logs\<JobName>.json
```

**IMPORTANT:** Check BOTH logs! The error details might be in one but not the other.

## New Diagnostic Messages

### Scheduling Diagnostics
```
[SCHEDULING] Checking for due jobs at 2026-02-28 14:30:00
[SCHEDULING] Found 1 job(s) due for execution
[SCHEDULING] Job 'WDrive' is already running, skipping
[SCHEDULING] Job 'WDrive' failed, will retry in 15 minutes at 2026-02-28 14:45:00
```

### Backup Execution Diagnostics
```
[DIAGNOSTIC] About to call BackupDisk(5, W:\Backups\WDrive.ssb, True, True)
[DIAGNOSTIC] BackupDisk returned: -5
[DIAGNOSTIC] BackupDisk failed with code -5, getting error message...
```

### Error Diagnostics (When Backup Fails)
```
[ERROR] Backup failed with code -5
[ERROR] Error message: No volumes found on disk
[ERROR] Source path: \\.\PHYSICALDRIVE5
[ERROR] Destination path: W:\Backups\WDrive.ssb
[ERROR] File exists after failure: False
```

### Exception Diagnostics
```
[EXCEPTION] Stack trace: at BackupService.BackupExecutor...
```

## Understanding the Retry Logic

### When Backup Succeeds:
- NextRunTime = Calculated based on schedule (e.g., tomorrow at 2:00 AM)
- Job will NOT run until scheduled time

### When Backup Fails:
- NextRunTime = Now + 15 minutes
- Job will retry in 15 minutes
- **This creates an infinite loop if backup keeps failing!**

### Why 15 Minutes?
Prevents rapid-fire retries that would:
- Fill up logs
- Waste CPU
- Make debugging difficult

## Common Failure Scenarios

### Scenario 1: "Service runs backup on startup"
**Cause:** Backup failed previously, NextRunTime = past time + 15 minutes  
**Solution:** Fix the root cause of backup failure

### Scenario 2: "No volumes found on disk"
**Cause:** C++ BackupDisk() can't enumerate volumes on disk  
**Check:** Is disk number correct? Does disk have volumes?

### Scenario 3: "Empty error message"
**Cause:** C++ didn't call SetLastError()  
**Check:** Look for exception stack trace instead

## Debugging Steps

1. **Stop the service**
   ```powershell
   Stop-Service BackupRestoreService
   ```

2. **Delete old logs** (optional - fresh start)
   ```powershell
   Remove-Item "C:\ProgramData\BackupRestoreService\service.log" -ErrorAction SilentlyContinue
   Remove-Item "C:\ProgramData\BackupRestoreService\Logs\*.json" -ErrorAction SilentlyContinue
   ```

3. **Start the service**
   ```powershell
   Start-Service BackupRestoreService
   ```

4. **Check service.log immediately**
   ```powershell
   Get-Content "C:\ProgramData\BackupRestoreService\service.log" -Tail 50
   ```

5. **Look for these patterns:**
   - `[SCHEDULING]` - When jobs are checked
   - `[DIAGNOSTIC]` - Before/after C++ calls
   - `[ERROR]` - Failure details
   - `[EXCEPTION]` - Exceptions

## Expected Log Output (Normal Startup)

```
2026-02-28 14:30:00 - Backup Scheduler Service started
2026-02-28 14:30:00 - [SCHEDULING] Checking for due jobs at 2026-02-28 14:30:00
2026-02-28 14:30:00 - [SCHEDULING] Found 0 job(s) due for execution
2026-02-28 14:31:00 - [SCHEDULING] Checking for due jobs at 2026-02-28 14:31:00
2026-02-28 14:31:00 - [SCHEDULING] Found 0 job(s) due for execution
```

**If you see "Found 1 job(s) due" immediately on startup, that's the bug!**

## Expected Log Output (Failed Backup)

```
2026-02-28 14:30:00 - Executing scheduled job: WDrive
2026-02-28 14:30:00 - Starting backup job: WDrive
2026-02-28 14:30:00 - Creating backup file: WDrive.ssb
2026-02-28 14:30:01 - Backing up disk: 5 (\\.\PHYSICALDRIVE5)
2026-02-28 14:30:01 - [DIAGNOSTIC] About to call BackupDisk(5, W:\Backups\WDrive.ssb, True, True)
2026-02-28 14:30:02 - [DIAGNOSTIC] BackupDisk returned: -5
2026-02-28 14:30:02 - [DIAGNOSTIC] BackupDisk failed with code -5, getting error message...
2026-02-28 14:30:02 - [ERROR] Backup failed with code -5
2026-02-28 14:30:02 - [ERROR] Error message: No volumes found on disk
2026-02-28 14:30:02 - [ERROR] Source path: \\.\PHYSICALDRIVE5
2026-02-28 14:30:02 - [ERROR] Destination path: W:\Backups\WDrive.ssb
2026-02-28 14:30:02 - [ERROR] File exists after failure: False
2026-02-28 14:30:02 - Job failed: WDrive
2026-02-28 14:30:02 - [SCHEDULING] Job 'WDrive' failed, will retry in 15 minutes at 2026-02-28 14:45:00
```

**The error code and error message will tell you exactly what's wrong!**

## Next Steps

After reviewing the logs:
- Share the **[ERROR]** messages with us
- Share the **[DIAGNOSTIC]** lines showing result codes
- Share the **[SCHEDULING]** lines showing when jobs are marked "due"

This will tell us exactly:
1. WHY the backup is failing
2. WHEN it's trying to run
3. WHETHER the retry logic is working correctly
