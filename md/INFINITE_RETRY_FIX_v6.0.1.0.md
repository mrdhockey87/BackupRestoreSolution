# Version 6.0.1.0 - Fix Infinite Retry Loop & Enhanced Scheduling

## Date
March 15, 2026 - mdail

## Critical Issues Fixed

### 1. **INFINITE RETRY LOOP** ❌→✅
**Problem**: Service was retrying failed backups every minute indefinitely, causing 500+ failures in 45 minutes.

**Root Cause**: 
- No tracking of whether a job was currently running
- Retry counter wasn't being properly checked
- No exponential backoff on retries
- File gets deleted and re-created in endless loop

**Fix Applied**:
- Added `IsCurrentlyRunning` property to `BackupJob` class
- Added `NextScheduledRun` property to centralize scheduling
- Implemented **exponential backoff**: 1st failure: +15 min → 2nd: +30 min → 3rd: +1 hour → 4th+: Next scheduled day
- Service checks `IsCurrentlyRunning` before starting job (prevents concurrent execution)
- `UpdateJobAfterExecution` now clears `IsCurrentlyRunning` flag

### 2. **LOG LIMIT TOO LOW** ❌→✅
**Problem**: Only 500 log entries per job, causing loss of historical data.

**Fix**: Increased `MaxLogEntriesPerFile` from 500 to **2000** entries per job.

### 3. **SMART RETRY BACKOFF** 🆕
**Problem**: All retries were 15 minutes apart, no intelligence about natural schedule.

**Fix**: 
```
1st failure: Retry in 15 minutes
2nd failure: Retry in 30 minutes  
3rd failure: Retry in 1 hour (LAST CHANCE)
4th+ failure: ⛔ RETRY LIMIT REACHED - Wait for next scheduled day
```

**Smart Override**: If retry time >= next natural schedule time, use natural schedule instead and reset failure counter.

---

## New Properties in BackupJob

### `NextScheduledRun` (DateTime?)
- **Purpose**: When this job should next execute
- **Replaces**: `Schedule.NextRunTime` (for centralized scheduling)
- **Display Name**: "Next Scheduled Run"
- **UI Location**: Job details page

### `IsCurrentlyRunning` (bool)
- **Purpose**: Prevents concurrent execution of same job
- **Set To**: `true` when backup starts, `false` when backup completes (success or failure)
- **Display Name**: "Is Currently Running"
- **UI Location**: Job details page (show as ✓ Running / ○ Idle)

---

## How The New System Works

### Job Scheduling Flow
```
Service checks every 1 minute:
  ├─ For each scheduled job:
  │   ├─ Is job currently running? → Skip
  │   ├─ Is NextScheduledRun <= Now? → Execute
  │   └─ Otherwise → Wait
```

### Failure Retry Flow
```
Backup fails:
  ├─ ConsecutiveFailures++
  ├─ If failures == 1: NextScheduledRun = Now + 15min
  ├─ If failures == 2: NextScheduledRun = Now + 30min
  ├─ If failures == 3: NextScheduledRun = Now + 1hour
  ├─ If failures >= 4: NextScheduledRun = NextNaturalSchedule, Log error
  └─ Check if retry time >= natural schedule:
      ├─ Yes? Use natural schedule, reset ConsecutiveFailures to 0
      └─ No? Use retry time
```

### Success Recovery Flow
```
Backup succeeds:
  ├─ ConsecutiveFailures = 0 (reset)
  ├─ Calculate next natural schedule
  ├─ NextScheduledRun = calculated time
  └─ If had previous failures: Log success recovery message
```

---

## Files Changed

### BackupCommon/BackupJob.cs
- ✅ Added `NextScheduledRun` property
- ✅ Added `IsCurrentlyRunning` property

### BackupCommon/BackupLogger.cs
- ✅ Increased `MaxLogEntriesPerFile` from 500 to 2000

### BackupService/JobManager.cs
- ✅ Updated `GetJobsDueForExecution()` to use `NextScheduledRun` and check `IsCurrentlyRunning`
- ✅ Rewrote `UpdateJobAfterExecution()` with exponential backoff logic
- ✅ Added `CalculateNaturalNextRunTime()` helper method
- ✅ Updated `CalculateNextRunTime()` to set both `NextScheduledRun` and `Schedule.NextRunTime`
- ✅ Added `SaveJob()` method for single-job saves

### BackupService/BackupSchedulerService.cs
- ✅ Updated `ExecuteBackupJobAsync()` to set `IsCurrentlyRunning = true` at start
- ✅ `UpdateJobAfterExecution()` now clears `IsCurrentlyRunning = false` at end
- ✅ Removed old diagnostic logging (replaced with proper retry messages)

---

## User-Visible Improvements

### Activity Log Messages
**On First Failure**:
```
[Warning] Backup attempt 1 of 3 failed. First failure. Will retry in 15 minutes at 11:46:07.
Next retry: 2026-03-15 11:46:07
```

**On Second Failure**:
```
[Warning] Backup attempt 2 of 3 failed. Second failure. Will retry in 30 minutes at 12:16:07.
Next retry: 2026-03-15 12:16:07
```

**On Third Failure**:
```
[Warning] Backup attempt 3 of 3 failed. Third failure (LAST CHANCE). Will retry in 1 hour at 12:46:07.
Next retry: 2026-03-15 12:46:07
```

**On Retry Limit Reached**:
```
[Error] ⛔ RETRY LIMIT REACHED - Failed 4 times. No more automatic retries.
Next scheduled backup: 2026-03-16 02:00:00. Please investigate the failure cause before next backup attempt.
```

**On Success After Failures**:
```
[Success] ✓ Backup succeeded after previous failures. Failure counter reset.
Next scheduled backup: 2026-03-16 02:00:00
```

### Job Details Page Display
```
Job Name: WDrive
Next Scheduled Run: 2026-03-15 14:30:00
Is Currently Running: ✓ Running  (or ○ Idle)
Last Run Time: 2026-03-15 11:46:07
Consecutive Failures: 2
```

---

## Testing Recommendations

### 1. Test Retry Logic
1. Start service with a backup job that will fail (e.g., disk disconnected)
2. Verify first failure triggers 15-minute retry
3. Let it fail again, verify 30-minute retry
4. Let it fail again, verify 1-hour retry
5. Let it fail again, verify it stops retrying and waits for next scheduled day
6. Reconnect disk, manually run backup, verify success resets failure counter

### 2. Test Concurrent Execution Prevention
1. Manually trigger a long-running backup
2. While running, try to trigger same backup again
3. Verify second trigger is ignored with "Job is already running" message

### 3. Test Smart Schedule Override
1. Set job to run daily at 2:00 AM
2. Let job fail at 1:45 AM (15 min before natural schedule)
3. Verify retry time is set to 2:00 AM (natural schedule) instead of 2:00 AM (1:45 + 15min)
4. Verify failure counter is reset to 0

---

## Breaking Changes
**None** - All changes are backward compatible. Old job definitions without `NextScheduledRun` or `IsCurrentlyRunning` will be initialized automatically.

---

## Migration Notes
- Existing jobs will automatically initialize `NextScheduledRun` on first service start
- `IsCurrentlyRunning` defaults to `false` for all jobs
- No manual intervention required

---

## Future Improvements
1. Add UI button to reset `ConsecutiveFailures` counter manually
2. Add configurable retry limits (currently hardcoded to 3)
3. Add email/notification alerts when retry limit is reached
4. Add UI indicator showing "Next retry in X minutes" countdown

---

## Version Update
- Previous Version: **6.0.0.0**
- New Version: **6.0.1.0**
- Update both `VersionClass.cs` and `Directory.Build.props`
