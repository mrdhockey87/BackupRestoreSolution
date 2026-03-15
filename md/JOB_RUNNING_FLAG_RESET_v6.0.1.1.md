# Job Running Flag Reset Feature - v6.0.1.1

## Overview
Added reset button for `IsCurrentlyRunning` flag to handle stuck job states, plus complete display of job execution status (Next Scheduled Run and Status).

## Changes Made

### 1. **BackupJobViewModel Enhancement** (MainWindow.xaml.cs)
Added three new properties to display execution status:
- `NextScheduledRun` (string): Formatted display of next scheduled execution time
- `IsCurrentlyRunning` (string): Formatted status display ("✓ Running" or "○ Idle")
- `IsRunning` (bool): Boolean flag for conditional visibility of Reset button

Constructor now formats `NextScheduledRun` from BackupJob.NextScheduledRun:
- If scheduled: Shows "MM/dd/yyyy hh:mm tt" format (e.g., "03/16/2026 02:00 PM")
- If not scheduled: Shows "Not scheduled"

Status display uses Unicode symbols:
- ✓ (checkmark) for running jobs
- ○ (circle) for idle jobs

### 2. **Job Display Grid Enhancement** (MainWindow.xaml)
Added new Row 5 to display execution status:
- Label: "Next Run:"
- Value: Shows NextScheduledRun and Status (IsCurrentlyRunning) side-by-side
- Layout: Horizontal StackPanel with proper spacing

Button panel updated:
- Increased button widths from 100px to 130px for better visibility
- Updated Grid.RowSpan from 5 to 6 to accommodate new row
- Added "Reset Running" button with WarningButton style (orange)
- Reset button only visible when job is running (Visibility binding to IsRunning)

### 3. **Reset Running Flag Button** (MainWindow.xaml)
New button specifications:
- Width: 130px, Height: 35px
- Content: "Reset Running"
- Style: WarningButton (orange color for caution)
- Tooltip: "Reset the IsCurrentlyRunning flag if stuck"
- Visibility: Conditional - only shows when IsRunning = true
- Click handler: ResetRunningFlag_Click

### 4. **Reset Functionality** (MainWindow.xaml.cs)
Implemented ResetRunningFlag_Click handler with:
1. **Confirmation Dialog**: Warns user about potential issues if job is actually running
2. **Flag Reset**: Sets job.IsCurrentlyRunning = false
3. **Persistence**: Calls jobManager.UpdateJob() to save change
4. **Logging**: Logs reset action via BackupLogger.LogInfo
5. **User Feedback**: Shows confirmation MessageBox
6. **UI Refresh**: Calls LoadBackupJobs() to update display

Warning message explains:
- Should only be used when job is stuck
- Could cause issues if job is actually running
- Requires user confirmation with Yes/No dialog

### 5. **XAML Resource Addition** (MainWindow.xaml)
Added BooleanToVisibilityConverter to Window.Resources:
```xaml
<BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter" />
```
Required for Reset button visibility binding.

## User Workflow

### Normal Job Display
Each job entry now shows:
```
Job Name: ServerBackup
Type: Full Backup
Source: Disk: \\.\PHYSICALDRIVE5
Destination: E:\Backups\ServerBackup
Schedule: Daily at 02:00
Next Run: 03/16/2026 02:00 AM    Status: ○ Idle
[Run Now] [Edit] [Delete]
```

### Running Job Display
When job is executing:
```
Next Run: 03/16/2026 02:00 AM    Status: ✓ Running
[Run Now] [Edit] [Reset Running] [Delete]
```

### Reset Running Flag Workflow
1. User notices job stuck in "✓ Running" state when not actually running
2. "Reset Running" button is visible (orange, warning style)
3. User clicks "Reset Running"
4. Confirmation dialog appears with warning message
5. User clicks "Yes" to confirm
6. Flag is reset, action is logged
7. Confirmation message shown
8. Job list refreshes showing "○ Idle" status
9. "Reset Running" button disappears (job no longer running)

## Benefits

### Visibility Enhancement
- Users can now see Next Scheduled Run directly on job list
- Clear visual indication of running vs idle jobs
- No need to check service logs to see job status

### Stuck Job Recovery
- Self-service recovery for stuck IsCurrentlyRunning flags
- No need to edit jobs.json manually
- No need to restart BackupService
- Maintains audit trail via BackupLogger

### Safety Features
- Button only appears when job shows as running
- Confirmation dialog with clear warning
- Logs all reset actions for troubleshooting
- Won't accidentally reset idle jobs

## Technical Details

### Flag Reset Logic
```csharp
job.IsCurrentlyRunning = false;
jobManager.UpdateJob(job);
BackupLogger.LogInfo(job.Name, "IsCurrentlyRunning flag manually reset by user");
LoadBackupJobs(); // Refresh display
```

### Grid Layout Changes
- Added Row 5 for Next Run/Status display
- Increased RowSpan from 5 to 6 for button panel
- Button widths increased from 100px to 130px
- Reset button conditionally visible based on IsRunning

### Data Binding
- NextScheduledRun: Text binding to formatted DateTime string
- IsCurrentlyRunning: Text binding to formatted status string
- IsRunning: Visibility binding via BoolToVisibilityConverter

## Edge Cases Handled

### Job Not Found
If job doesn't exist (deleted between display and click):
- Shows error MessageBox
- No flag reset attempted

### No Next Scheduled Run
If NextScheduledRun is null:
- Displays "Not scheduled" instead of date
- Common for manual-only jobs (no schedule configured)

### Concurrent Execution Prevention
Reset clears the flag that prevents concurrent runs, allowing:
- Scheduled execution to proceed
- Manual "Run Now" to work again
- Service to properly schedule next run

## Testing Scenarios

### Scenario 1: Normal Reset
1. Job stuck with "✓ Running" status
2. Click Reset Running button
3. Confirm reset in dialog
4. Status changes to "○ Idle"
5. Reset button disappears
6. Log entry created

### Scenario 2: Cancel Reset
1. Click Reset Running button
2. Click "No" in confirmation dialog
3. Flag remains unchanged
4. No log entry created

### Scenario 3: Job Actually Running
1. Start job with Run Now
2. Job shows "✓ Running" (correct)
3. Reset Running button appears
4. If clicked, warning dialog explains risk
5. If confirmed, job execution may fail
6. Log shows manual reset during active run

## Integration Points

### BackupCommon.BackupJob
Uses existing properties:
- NextScheduledRun (DateTime?) - added in v6.0.1.0
- IsCurrentlyRunning (bool) - added in v6.0.1.0

### BackupLogger
Uses existing LogInfo method for audit trail:
- Job name
- Message: "IsCurrentlyRunning flag manually reset by user"

### JobManager
Uses existing methods:
- GetJob(Guid) - retrieve job
- UpdateJob(BackupJob) - persist changes

## Production Notes

### When to Use Reset
- Job stuck in Running state after service crash
- Job stuck after system restart
- Concurrent execution prevented by stuck flag
- Scheduled runs not executing due to flag

### When NOT to Use Reset
- Job is actually running (wait for completion)
- Job scheduled for future (flag is correct)
- Service is stopped (restart service instead)

### Monitoring
Activity logs will show:
```
[Info] ServerBackup - IsCurrentlyRunning flag manually reset by user
```

Check for frequent resets - may indicate:
- Service crashes
- Backup hangs
- Improper shutdowns
- Need for exponential backoff fixes

## Future Enhancements

Potential improvements:
1. Show running time if job is executing
2. Add "Force Stop" button to actually terminate running jobs
3. Show progress percentage for running jobs
4. Color-code status (green=idle, orange=running, red=stuck)
5. Auto-detect stuck jobs (running > X hours)
6. Add confirmation before Run Now if job shows as running

## Version Info
- Version: 6.0.1.1
- Date: 2026-03-16
- Component: BackupUI
- Impact: Job display and management
- Breaking Changes: None (backward compatible)
