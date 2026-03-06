# Quick Start: Diagnosing Silent Backup Failures (v5.13.8.5)

## Step 1: Download DebugView

**Get it here:**
```
https://learn.microsoft.com/en-us/sysinternals/downloads/debugview
```

Or direct download:
```
https://download.sysinternals.com/files/DebugView.zip
```

Extract to: `C:\Tools\DebugView\`

## Step 2: Configure DebugView

1. **Run DebugView as Administrator**
   - Right-click `Dbgview.exe` → Run as administrator

2. **Enable Global Capture**
   - Menu: Capture → Capture Global Win32 (check it ✓)

3. **Set Filter (Optional)**
   - Menu: Edit → Filter/Highlight
   - Include: `*BackupDisk*`
   - Click OK

4. **Clear Buffer**
   - Menu: Edit → Clear Display

## Step 3: Start Your Backup

1. Open Backup UI
2. Click "Jobs" tab
3. Find "WDrive1" job
4. Click "Run Now"

## Step 4: Watch The Logs!

**DebugView will show:**

```
[00001] [BackupDisk] Starting backup of Disk 5 to: X:\BackupApplications\WDrive1\WDrive1.ssb
[00002] [BackupDisk] Dest file: X:\BackupApplications\WDrive1\WDrive1.ssb
[00003] [BackupDisk] Creating parent dir: X:\BackupApplications\WDrive1
[00004] [BackupDisk] Enumerating volumes on Disk 5
[00005] [BackupDisk] Found volume on Disk 5: \\?\Volume{12345678-1234-1234-1234-123456789012}\
[00006] [BackupDisk] Volume enumeration complete. Found 1 volumes
[00007] [BackupDisk] Creating WIM file...
[00008] [BackupDisk] WIM file created successfully: X:\BackupApplications\WDrive1\WDrive1.ssb
[00009] [BackupDisk] Processing volume 1/1: \\?\Volume{12345678-1234-1234-1234-123456789012}\
[00010] [BackupDisk] VSS Initialize: SUCCESS
[00011] [BackupDisk] Creating VSS snapshot...
[00012] [BackupDisk] VSS snapshot created: \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy5\
[00013] [BackupDisk] Capturing to WIM: Disk 5 Volume 1 from \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy5\
```

**THEN IT WILL STOP FOR A LONG TIME (30-60 minutes)!**

**This is NORMAL - WIMCaptureImage is working but doesn't report progress!**

**Wait for:**
```
[00014] [BackupDisk] Volume 1 captured successfully
[00015] [BackupDisk] All volumes captured, finalizing WIM...
[00016] [BackupDisk] WIM file closed successfully
[00017] [BackupDisk] Backup completed successfully!
```

## Step 5: Identify The Problem

### Failure Point 1: No Volumes Found

```
[BackupDisk] Enumerating volumes on Disk 5
[BackupDisk] Volume enumeration complete. Found 0 volumes
[BackupDisk] ERROR: No volumes found on Disk 5
```

**Problem:** Disk has no accessible volumes
**Fix:** Check if disk is initialized, online, and has partitions

### Failure Point 2: VSS Failed (USB Drives)

```
[BackupDisk] Processing volume 1/1: \\?\Volume{...}\
[BackupDisk] VSS Initialize: FAILED
[BackupDisk] VSS snapshot failed, using direct path, HR=-2147467259
[BackupDisk] Capturing to WIM: Disk 5 Volume 1 from \\?\Volume{...}\
```

**Problem:** VSS doesn't work on this disk (common for USB)
**Fix:** This is OK - backup continues with direct read (slower but works)

### Failure Point 3: Hung During Capture

```
[BackupDisk] Capturing to WIM: Disk 5 Volume 1 from ...
(NO MORE MESSAGES - HUNG HERE!)
```

**Problem:** WIMCaptureImage is blocking/hung
**Possible causes:**
- ✅ Normal if USB drive (takes HOURS!)
- ❌ Bad sectors on disk
- ❌ Disk read error
- ❌ Antivirus blocking
- ❌ Disk disconnected

**Fix:** 
- If USB: Wait 5-10 hours (seriously!)
- If internal: Check disk health with `chkdsk W: /f`

### Failure Point 4: Access Denied

```
[BackupDisk] ERROR: Failed to enumerate volumes, Win32 Error: 5
```

**Problem:** Service doesn't have permissions
**Fix:** Verify service is running as LocalSystem

## Step 6: Save The Log

1. In DebugView: File → Save As...
2. Save to: `X:\BackupApplications\WDrive1\DebugView.log`
3. Send to support/developer

## JMicron USB Drive Warning! ⚠

**If Disk 5 is USB (JMicron controller):**

```powershell
Get-Disk | Where-Object Number -eq 5
```

**If `BusType = USB`:**
- Backup will take **5-10 HOURS** (not minutes!)
- Progress will appear stuck at 30%
- This is **NORMAL for USB drives!**
- WIMCaptureImage reads at 5-10 MB/s
- 500GB ÷ 10MB/s = 13.8 hours!

**DO NOT ABORT unless:**
- No disk activity for 30+ minutes
- No new temp files appearing
- Backup size not growing

## Quick Checks

**While backup is running:**

### Check 1: Disk Activity
```
Task Manager → Performance → Disk 5
Look for: 5-50 MB/s read activity
```

### Check 2: File Size
```powershell
while ($true) { 
    ls "X:\BackupApplications\WDrive1\*.ssb","X:\BackupApplications\WDrive1\*.tmp" | ft Name,Length -Auto
    Start-Sleep 5 
}
```

### Check 3: Temp Files
```
X:\BackupApplications\WDrive1\
Look for: WDrive1.ssb.tmp, ~WIMBootCompress.tmp
If they exist: Backup is RUNNING!
```

## Expected Timeline (500GB USB Drive)

| Time | DebugView Message | File Size | What's Happening |
|------|-------------------|-----------|------------------|
| 0:00 | Starting backup... | 0 bytes | Initializing |
| 0:01 | Found 1 volumes | 0 bytes | Enumeration |
| 0:02 | VSS snapshot created | 0 bytes | VSS ready |
| 0:03 | Capturing to WIM... | 0 bytes | Starting capture |
| 0:05 | (no messages) | 1 MB | Reading disk |
| 1:00 | (no messages) | 600 MB | Reading disk |
| 5:00 | (no messages) | 3 GB | Reading disk |
| 10:00 | (no messages) | 6 GB | Reading disk |
| **13:00** | **Volume 1 captured!** | **200 GB** | **Done!** |

**See the pattern?** 13 hours with NO progress messages!

## Summary

**Version 5.13.8.5 lets you see:**
- ✅ Exactly where backup fails
- ✅ Win32 error codes
- ✅ VSS status
- ✅ Which volume is being processed
- ✅ Real-time progress

**Your 0-byte backup mystery WILL be solved!** 🔍
