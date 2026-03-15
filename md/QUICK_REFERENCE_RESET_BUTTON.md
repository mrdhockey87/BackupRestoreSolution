# Quick Reference: Job Running Flag Reset

## What Changed
✓ Added "Next Run" and "Status" display to each job  
✓ Added "Reset Running" button (only shows when job is running)  
✓ Button allows manual reset of stuck IsCurrentlyRunning flags  

## New Display Fields

### Next Run
Shows next scheduled execution time:
- Format: "03/16/2026 02:00 PM"
- "Not scheduled" for manual-only jobs

### Status
Shows current execution state:
- "✓ Running" - job is executing
- "○ Idle" - job is not running

## Reset Running Button

### When It Appears
- Only visible when Status shows "✓ Running"
- Orange color (warning style)
- Appears between Edit and Delete buttons

### What It Does
1. Shows confirmation dialog with warning
2. Resets IsCurrentlyRunning flag to false
3. Logs reset action
4. Refreshes job list
5. Status changes to "○ Idle"
6. Button disappears

### When to Use
✓ Job stuck in Running state after crash  
✓ Job won't run because flag is stuck  
✓ Scheduled execution prevented by stuck flag  
✗ Job is actually running (wait for completion)  
✗ Job scheduled for future (flag is correct)  

## Example Display

### Idle Job
```
ServerBackup
Type: Full Backup
Source: Disk: \\.\PHYSICALDRIVE5
Destination: E:\Backups\ServerBackup
Schedule: Daily at 02:00
Next Run: 03/16/2026 02:00 AM    Status: ○ Idle

[Run Now] [Edit] [Delete]
```

### Running Job
```
Next Run: 03/16/2026 02:00 AM    Status: ✓ Running

[Run Now] [Edit] [Reset Running] [Delete]
            ↑
         Orange button - only shows when running
```

## Safety Features
- Confirmation dialog before reset
- Warning about risks if job is actually running
- Audit log entry for all resets
- Button only visible when needed

## Activity Log Entry
```
[Info] ServerBackup - IsCurrentlyRunning flag manually reset by user
```

## Technical Changes
- BackupJobViewModel: Added NextScheduledRun, IsCurrentlyRunning, IsRunning properties
- MainWindow.xaml: Added Row 5 for status display, added Reset Running button
- MainWindow.xaml.cs: Added ResetRunningFlag_Click handler
- Button width: 100px → 130px for better visibility
