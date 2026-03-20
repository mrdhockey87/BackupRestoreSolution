# Testing Guide for Version 6.0.1.12 - XML Metadata Sanitization

## Pre-Test Setup

### 1. Stop the Service
```powershell
Stop-Service BackupRestoreService
```

### 2. Rebuild Solution
```powershell
# From solution root
dotnet build --configuration Release --no-incremental

# Or in Visual Studio
# Build → Rebuild Solution (Ctrl+Shift+B)
```

### 3. Verify BackupEngine.dll Updated
```powershell
# Check file timestamp
Get-Item "artifacts\bin\Release\BackupEngine.dll" | Select-Object LastWriteTime

# Should be current date/time
```

### 4. Start the Service
```powershell
Start-Service BackupRestoreService

# Verify running
Get-Service BackupRestoreService
```

## Test Cases

### Test 1: Normal Folder Names (Regression Test)
**Purpose:** Ensure fix doesn't break existing functionality

**Test Data:**
- Volume with standard folder names: `Documents`, `Pictures`, `Videos`

**Expected Result:**
- ✅ Backup completes successfully
- ✅ No error 1465
- ✅ All folders captured

### Test 2: Underscores (Original Issue)
**Purpose:** Verify the reported issue is fixed

**Test Data:**
- Volume with folder: `1TB_PCIE_SSD`
- Or create test folder: `Test_Underscore_Name`

**Expected Result:**
- ✅ Backup completes successfully  
- ✅ Metadata set with sanitized name: `Disk 5 Volume 1 - 1TB_PCIE_SSD`
- ✅ No error 1465

**Debug Logs to Check:**
```
[CaptureToWimImage] Setting metadata with sanitized name: Disk 5 Volume 1 - 1TB_PCIE_SSD
[CaptureToWimImage] Folder captured successfully: 1TB_PCIE_SSD
```

### Test 3: Ampersand (&)
**Purpose:** Test XML's most problematic character

**Test Data:**
```powershell
# Create test folder
New-Item -Path "C:\Test" -Name "Data & Backups" -ItemType Directory
New-Item -Path "C:\Test\Data & Backups\test.txt" -ItemType File
```

**Expected Result:**
- ✅ Backup succeeds
- ✅ Metadata contains: `Data &amp; Backups`
- ✅ Folder name preserved in WIM

**Verification:**
```powershell
# Mount the backup and check folder exists
# Should see "Data & Backups" folder (not "Data &amp; Backups")
```

### Test 4: Angle Brackets (< >)
**Purpose:** Test XML tag characters

**Test Data:**
```powershell
# Create test folder (note: Windows allows < > in names if not NTFS)
# Or use existing folder with name containing comparison text
New-Item -Path "C:\Test" -Name "System_Status" -ItemType Directory
# Rename to include <> if filesystem allows
```

**Expected Result:**
- ✅ Backup succeeds
- ✅ If name has `<System>`, metadata contains: `&lt;System&gt;`

### Test 5: Quotes (" ')
**Purpose:** Test quote characters

**Test Data:**
```powershell
# Create test folders with quotes
New-Item -Path "C:\Test" -Name "User's Files" -ItemType Directory
```

**Expected Result:**
- ✅ Backup succeeds
- ✅ Metadata contains: `User&apos;s Files`

### Test 6: Multiple Special Characters
**Purpose:** Test comprehensive sanitization

**Test Data:**
```powershell
# Create folder with multiple special chars
# Windows may restrict some, so test with allowed ones
New-Item -Path "C:\Test" -Name "User's_Data&More" -ItemType Directory
```

**Expected Result:**
- ✅ Backup succeeds
- ✅ Metadata properly escapes all special characters
- ✅ Combined transformations: `User&apos;s_Data&amp;More`

## Verification Steps

### 1. Check Activity Log (UI)
1. Open Backup UI → Activity tab
2. Look for your test backup job
3. Verify status: `[Success]`
4. Check message: "Backup completed successfully"

### 2. Check DebugView (Detailed)
1. Download [DebugView](https://docs.microsoft.com/en-us/sysinternals/downloads/debugview)
2. Run as Administrator
3. Enable: Capture → Capture Global Win32
4. Run backup
5. Look for messages:
   ```
   [CaptureToWimImage] Setting metadata with sanitized name: ...
   [CaptureToWimImage] Folder captured successfully: ...
   ```

### 3. Verify Backup File
```powershell
# Check backup file exists and has size > 0
Get-Item "X:\BackupApplications\YourJob\YourJob.ssb" | Select-Object Length
```

### 4. Mount and Verify (Optional)
1. UI → Mount Backups tab
2. Mount the test backup
3. Browse mounted drive
4. Verify folder names display correctly (original names, not escaped)

## Expected Debug Output

### Successful Backup
```
[BackupDisk] Starting backup of Disk 5 to X:\BackupApplications\Test\Test.ssb
[BackupDisk] Found 3 volumes on Disk 5
[EnumerateIncludedFolders] INCLUDING folder: W:\Data & Backups
[EnumerateIncludedFolders] INCLUDING folder: W:\User's Files
[BackupDisk] Capturing folder 1/2: W:\Data & Backups
[CaptureToWimImage] Capture successful, setting metadata...
[CaptureToWimImage] Setting metadata with sanitized name: Disk 5 Volume 1 - Data &amp; Backups
[CaptureToWimImage] Folder captured successfully: Data & Backups
[BackupDisk] Capturing folder 2/2: W:\User's Files
[CaptureToWimImage] Setting metadata with sanitized name: Disk 5 Volume 1 - User&apos;s Files
[BackupDisk] Backup completed successfully
```

### Failed Backup (If Regression)
```
[CaptureToWimImage] Setting metadata with sanitized name: ...
[CaptureToWimImage] ERROR: Failed to set image metadata (Error 1465) [Name: ...]
[CaptureToWimImage] XML: <WIM><IMAGE><NAME>...</NAME></IMAGE></WIM>
```

## Rollback Plan

If issues occur:

### 1. Stop Service
```powershell
Stop-Service BackupRestoreService
```

### 2. Restore Previous Version
```powershell
# Copy old BackupEngine.dll from backup
Copy-Item "BackupEngine.dll.backup" -Destination "artifacts\bin\Release\BackupEngine.dll"
```

### 3. Restart Service
```powershell
Start-Service BackupRestoreService
```

## Common Issues

### Service Won't Start
**Cause:** BackupEngine.dll file locked or corrupted

**Fix:**
```powershell
# Check service status
Get-Service BackupRestoreService

# Check if DLL is locked
Get-Process | Where-Object {$_.Modules.FileName -like "*BackupEngine.dll*"}

# Force stop if needed
Stop-Service BackupRestoreService -Force
```

### Backup Still Fails with 1465
**Cause:** Service didn't reload DLL

**Fix:**
```powershell
# Restart service to reload DLL
Restart-Service BackupRestoreService

# Verify new DLL loaded
Get-Process BackupRestoreService | Select-Object -ExpandProperty Modules | Where-Object {$_.ModuleName -eq "BackupEngine.dll"}
```

### No Debug Output
**Cause:** DebugView not capturing

**Fix:**
1. Run DebugView as Administrator
2. Capture → Capture Global Win32 (check enabled)
3. Restart backup operation

## Success Criteria

✅ **All test cases pass** (no error 1465)  
✅ **Original issue fixed** (folder "1TB_PCIE_SSD" works)  
✅ **No regressions** (normal names still work)  
✅ **Special characters handled** (all 5 XML characters escaped)  
✅ **Mounted backups readable** (folder names display correctly)  
✅ **Activity log shows success** (all backups complete)

## Test Report Template

```
Test Date: ___________
Tester: ___________
Version: 6.0.1.12

[ ] Test 1: Normal Names - PASS / FAIL
[ ] Test 2: Underscores - PASS / FAIL
[ ] Test 3: Ampersand - PASS / FAIL
[ ] Test 4: Angle Brackets - PASS / FAIL
[ ] Test 5: Quotes - PASS / FAIL
[ ] Test 6: Multiple Special Chars - PASS / FAIL

Notes:
___________________________________
___________________________________

Approved for Production: YES / NO
Signature: ___________
```

---

**Estimated Testing Time:** 30-45 minutes  
**Risk Level:** Low (localized fix, comprehensive testing)  
**Rollback Time:** < 5 minutes
