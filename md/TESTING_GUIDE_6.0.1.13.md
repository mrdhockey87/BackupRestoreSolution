# Testing Guide - Version 6.0.1.13 Volume Label Fix

**Version:** 6.0.1.13  
**Test Date:** March 19, 2026  
**Test Focus:** Volume label retrieval and metadata generation  
**Critical Bug Fix:** Using volume labels instead of folder names in WIM image metadata

---

## Test Overview

This guide provides comprehensive testing procedures for version 6.0.1.13, which fixes the critical bug where WIM image metadata incorrectly used folder names instead of volume labels. The fix ensures backups correctly identify source volumes and complete without error 1465.

---

## Pre-Test Requirements

### Environment Setup
- ✅ Version 6.0.1.13 deployed (check UI: Help → About)
- ✅ Test volumes prepared with known labels
- ✅ DebugView++ or similar tool running to capture OutputDebugString logs
- ✅ Test exclusion rules configured (if applicable)
- ✅ Sufficient disk space for test backups

### Verification Tools
- **DebugView++:** Capture real-time debug logs from BackupEngine
- **PowerShell:** Verify volume labels: `Get-Volume | Format-Table DriveLetter, FileSystemLabel`
- **WIM tools:** wimlib-imagex or DISM to inspect WIM metadata
- **Disk Management:** Verify disk/volume configuration

---

## Test Case 1: User's Actual Scenario - Bay2_512MG Volume

### Objective
Verify the specific user issue is resolved: backup Disk 5 with volume "Bay2_512MG" containing folders "1TB_PCIE_SSD" and "Note20_SDCard".

### Prerequisites
- Disk 5 with volume labeled "Bay2_512MG"
- Folders in root: "1TB_PCIE_SSD", "Note20_SDCard" (after exclusions)
- Volume assigned drive letter W:

### Test Steps

1. **Verify Volume Label**
   ```powershell
   Get-Volume -DriveLetter W | Format-Table DriveLetter, FileSystemLabel, Size
   ```
   **Expected:** Drive W: shows FileSystemLabel = "Bay2_512MG"

2. **Launch DebugView++**
   - Start DebugView++ with administrator privileges
   - Enable "Capture Global Win32" and "Capture Win32"
   - Clear existing logs

3. **Configure Backup**
   - Open Backup UI
   - Select "Disk Backup" mode
   - Choose Disk 5 (Bay2_512MG)
   - Set destination path
   - Configure exclusion rules (if any)

4. **Execute Backup**
   - Click "Start Backup"
   - Monitor progress in UI
   - Watch DebugView++ for real-time logs

5. **Verify Debug Logs**
   
   Look for these specific entries:
   
   ```
   [BackupDisk] Volume enumeration complete. Found 1 volumes
   [BackupDisk] Volume label retrieved: Bay2_512MG
   [BackupDisk] Processing volume 1/1: \\?\Volume{<guid>}\
   [BackupDisk] VSS Initialize: SUCCESS
   [BackupDisk] VSS snapshot created: \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy<N>\
   [BackupDisk] Capturing folder 1/2: <snapshot_path>\1TB_PCIE_SSD
   [BackupDisk] Image name: Disk 5 Volume 1 - Bay2_512MG [Folder: 1TB_PCIE_SSD]
   [BackupDisk] Capturing folder 2/2: <snapshot_path>\Note20_SDCard
   [BackupDisk] Image name: Disk 5 Volume 1 - Bay2_512MG [Folder: Note20_SDCard]
   ```

6. **Check Backup Completion**
   - Verify backup completes without errors
   - Check return code = 0 (success)
   - No error 1465 should occur

7. **Inspect WIM Metadata**
   
   Using wimlib-imagex:
   ```powershell
   wimlib-imagex info "path\to\backup.wim"
   ```
   
   Or using DISM:
   ```powershell
   Dism /Get-WimInfo /WimFile:"path\to\backup.wim"
   ```
   
   **Expected Output:**
   ```
   Image 1:
     Name: Disk 5 Volume 1 - Bay2_512MG [Folder: 1TB_PCIE_SSD]
     Size: <size>
     
   Image 2:
     Name: Disk 5 Volume 1 - Bay2_512MG [Folder: Note20_SDCard]
     Size: <size>
   ```

### Success Criteria
- ✅ Backup completes with return code 0
- ✅ NO error 1465 "Failed to set image metadata"
- ✅ Debug logs show volume label "Bay2_512MG" retrieved
- ✅ WIM metadata shows "Bay2_512MG" (volume label) NOT "1TB_PCIE_SSD" (folder name)
- ✅ Both folder images have volume label in metadata

### Failure Analysis
If test fails:
- Check debug logs for "Failed to get volume label, Error: [code]"
- Verify volume is mounted and accessible
- Check if volume label was changed
- Review GetLastError codes in logs

---

## Test Case 2: Labeled Volume with Multiple Folders

### Objective
Verify correct metadata generation for volumes with multiple root folders.

### Prerequisites
- Test volume with known label (e.g., "TestData")
- At least 3 root folders
- Drive letter assigned

### Test Steps

1. **Prepare Test Volume**
   ```powershell
   # Create test folders
   New-Item -Path "E:\TestFolder1" -ItemType Directory
   New-Item -Path "E:\TestFolder2" -ItemType Directory
   New-Item -Path "E:\TestFolder3" -ItemType Directory
   
   # Set volume label if needed
   Set-Volume -DriveLetter E -NewFileSystemLabel "TestData"
   
   # Verify label
   Get-Volume -DriveLetter E | Format-Table DriveLetter, FileSystemLabel
   ```

2. **Execute Backup**
   - Select disk containing volume E:
   - Run backup with no exclusions

3. **Verify Debug Logs**
   ```
   [BackupDisk] Volume label retrieved: TestData
   [BackupDisk] Image name: Disk X Volume Y - TestData [Folder: TestFolder1]
   [BackupDisk] Image name: Disk X Volume Y - TestData [Folder: TestFolder2]
   [BackupDisk] Image name: Disk X Volume Y - TestData [Folder: TestFolder3]
   ```

4. **Check WIM Metadata**
   All images should have volume label "TestData" with different folder names:
   ```
   Image 1: Disk X Volume Y - TestData [Folder: TestFolder1]
   Image 2: Disk X Volume Y - TestData [Folder: TestFolder2]
   Image 3: Disk X Volume Y - TestData [Folder: TestFolder3]
   ```

### Success Criteria
- ✅ All folder backups use same volume label "TestData"
- ✅ Each image has unique folder name in brackets
- ✅ Backup completes successfully
- ✅ Metadata clearly distinguishes volume identity vs folder contents

---

## Test Case 3: Unlabeled Volume

### Objective
Verify graceful handling of volumes without labels.

### Prerequisites
- Test volume with NO label (empty label string)
- Drive letter assigned
- Contains test folders

### Test Steps

1. **Prepare Unlabeled Volume**
   ```powershell
   # Remove volume label
   Set-Volume -DriveLetter F -NewFileSystemLabel ""
   
   # Verify no label
   Get-Volume -DriveLetter F | Format-Table DriveLetter, FileSystemLabel
   # FileSystemLabel should be empty
   
   # Create test folder
   New-Item -Path "F:\TestData" -ItemType Directory
   ```

2. **Execute Backup**
   - Select disk containing volume F:
   - Run backup

3. **Verify Debug Logs**
   ```
   [BackupDisk] Volume has no label, using 'Unlabeled'
   [BackupDisk] Image name: Disk X Volume Y - Unlabeled [Folder: TestData]
   ```

4. **Check WIM Metadata**
   ```
   Image 1: Disk X Volume Y - Unlabeled [Folder: TestData]
   ```

### Success Criteria
- ✅ Debug log shows "Volume has no label, using 'Unlabeled'"
- ✅ Metadata contains "Unlabeled" as volume label
- ✅ Backup completes successfully
- ✅ No errors or exceptions thrown

---

## Test Case 4: Multiple Volumes on Same Disk

### Objective
Verify correct metadata when backing up a disk with multiple volumes.

### Prerequisites
- Physical disk with 2+ volumes
- Each volume has unique label
- Drive letters assigned

### Test Steps

1. **Verify Volume Configuration**
   ```powershell
   # List all volumes on target disk
   Get-Partition -DiskNumber 2 | Get-Volume | Format-Table DriveLetter, FileSystemLabel, Size
   ```
   
   Example output:
   ```
   DriveLetter FileSystemLabel Size
   ----------- --------------- ----
   D           Volume1         100GB
   E           Volume2         200GB
   ```

2. **Execute Disk Backup**
   - Select "Disk 2" (entire disk)
   - Backup will process both volumes

3. **Verify Debug Logs**
   ```
   [BackupDisk] Volume enumeration complete. Found 2 volumes
   [BackupDisk] Volume label retrieved: Volume1
   [BackupDisk] Volume label retrieved: Volume2
   [BackupDisk] Processing volume 1/2: \\?\Volume{<guid1>}\
   [BackupDisk] Image name: Disk 2 Volume 1 - Volume1 [Folder: <folder>]
   [BackupDisk] Processing volume 2/2: \\?\Volume{<guid2>}\
   [BackupDisk] Image name: Disk 2 Volume 2 - Volume2 [Folder: <folder>]
   ```

4. **Check WIM Metadata**
   ```
   Image 1: Disk 2 Volume 1 - Volume1 [Folder: ...]
   Image 2: Disk 2 Volume 2 - Volume2 [Folder: ...]
   ```

### Success Criteria
- ✅ Both volumes processed in correct order
- ✅ Each volume's label retrieved correctly
- ✅ Metadata clearly distinguishes between volumes
- ✅ Volume indices (1, 2) match enumeration order

---

## Test Case 5: Volume with Special Characters in Label

### Objective
Verify XML sanitization (version 6.0.1.12) works with volume label fix (version 6.0.1.13).

### Prerequisites
- Test volume with special characters in label
- Characters to test: & < > " '

### Test Steps

1. **Create Volume with Special Characters**
   ```powershell
   Set-Volume -DriveLetter G -NewFileSystemLabel "Data & Backups"
   
   # Verify label
   Get-Volume -DriveLetter G | Format-Table DriveLetter, FileSystemLabel
   ```
   
   Note: Windows may restrict some characters. Test with allowed special chars.

2. **Create Test Folder**
   ```powershell
   New-Item -Path "G:\User's Files" -ItemType Directory
   ```

3. **Execute Backup**
   - Backup volume G:

4. **Verify Debug Logs**
   ```
   [BackupDisk] Volume label retrieved: Data & Backups
   [BackupDisk] Image name: Disk X Volume Y - Data & Backups [Folder: User's Files]
   [CaptureToWimImage] Original image name: Disk X Volume Y - Data & Backups [Folder: User's Files]
   [CaptureToWimImage] Sanitized image name: Disk X Volume Y - Data &amp; Backups [Folder: User&apos;s Files]
   ```

5. **Check WIM Metadata**
   Metadata should show XML-escaped characters:
   ```
   Name: Disk X Volume Y - Data &amp; Backups [Folder: User&apos;s Files]
   ```

### Success Criteria
- ✅ Volume label retrieved correctly (before sanitization)
- ✅ XML sanitization applied to both volume label and folder name
- ✅ & becomes &amp;, ' becomes &apos; in metadata
- ✅ Backup completes without error 1465
- ✅ Both fixes (6.0.1.12 and 6.0.1.13) work together

---

## Test Case 6: Volume Label Retrieval Error Handling

### Objective
Verify graceful handling when GetVolumeInformationW fails.

### Prerequisites
- Scenario to trigger API error (e.g., unmounted volume, access denied)
- This may require controlled test environment

### Simulation Options

**Option A: Unmount volume after enumeration (advanced)**
```powershell
# Dismount volume (requires admin)
Dismount-Volume -DriveLetter H -Confirm:$false
```

**Option B: Review error handling in code**
Check BackupManager_Advanced.cpp lines 920-928 for error handling logic.

### Test Steps

1. **Configure Test Scenario**
   - Prepare volume that will cause GetVolumeInformationW to fail
   - Or use code review to verify error handling logic

2. **Execute Backup**
   - Attempt backup of problematic volume

3. **Verify Debug Logs**
   ```
   [BackupDisk] Failed to get volume label, Error: <error_code>
   [BackupDisk] Image name: Disk X Volume Y - Unknown [Folder: <folder>]
   ```

4. **Check Behavior**
   - Backup should continue (not crash)
   - "Unknown" used as volume label
   - Error logged but not fatal

### Success Criteria
- ✅ GetVolumeInformationW errors are caught
- ✅ Fallback to "Unknown" label
- ✅ Error logged with GetLastError code
- ✅ Backup continues (doesn't abort)
- ✅ No exceptions or crashes

---

## Test Case 7: Volume with Folder Names Similar to Volume Label

### Objective
Verify metadata correctly distinguishes volume label from folder names even when similar.

### Prerequisites
- Volume labeled "MyDrive"
- Create folder named "MyDrive" in root

### Test Steps

1. **Prepare Test Environment**
   ```powershell
   Set-Volume -DriveLetter I -NewFileSystemLabel "MyDrive"
   New-Item -Path "I:\MyDrive" -ItemType Directory
   New-Item -Path "I:\OtherFolder" -ItemType Directory
   ```

2. **Execute Backup**
   - Backup volume I:

3. **Verify Debug Logs**
   ```
   [BackupDisk] Volume label retrieved: MyDrive
   [BackupDisk] Image name: Disk X Volume Y - MyDrive [Folder: MyDrive]
   [BackupDisk] Image name: Disk X Volume Y - MyDrive [Folder: OtherFolder]
   ```

4. **Check WIM Metadata**
   ```
   Image 1: Disk X Volume Y - MyDrive [Folder: MyDrive]
   Image 2: Disk X Volume Y - MyDrive [Folder: OtherFolder]
   ```

### Success Criteria
- ✅ Volume label "MyDrive" used in both image names
- ✅ Folder name "MyDrive" appears in brackets for Image 1
- ✅ Clear distinction between volume identity and folder name
- ✅ No confusion in metadata

---

## Regression Testing

### Verify Previous Functionality Still Works

#### 1. Exclusion Rules
- Test backup with user-defined exclusions
- Verify excluded folders don't appear in backup
- Check EnumerateIncludedFolders still works correctly

#### 2. VSS Snapshots
- Verify VSS snapshots still created
- Check fallback to direct path if VSS fails
- Confirm snapshot paths logged correctly

#### 3. WIM Compression
- Test with different compression levels (none, fast, maximum)
- Verify compression setting doesn't affect metadata

#### 4. Incremental/Differential Backups
- Test incremental backup (requires base backup)
- Test differential backup (requires base backup)
- Verify metadata format consistent across backup types

#### 5. Error Handling
- Test insufficient disk space
- Test invalid destination path
- Test volume access denied
- Verify appropriate error messages

---

## Performance Testing

### Metrics to Monitor

1. **Volume Label Retrieval Time**
   - Should be negligible (<10ms per volume)
   - Check debug logs for timing if available

2. **Memory Usage**
   - volumeLabels vector adds minimal overhead
   - Monitor overall process memory during backup

3. **Backup Speed**
   - Volume label retrieval should NOT slow down backup
   - Compare backup times with version 6.0.1.12

---

## Debug Log Analysis

### Key Log Entries to Verify

```
✅ [BackupDisk] Volume enumeration complete. Found X volumes
✅ [BackupDisk] Volume label retrieved: <label>
✅ [BackupDisk] Processing volume 1/X: \\?\Volume{<guid>}\
✅ [BackupDisk] Capturing folder Y/Z: <path>
✅ [BackupDisk] Image name: Disk A Volume B - <VolumeLabel> [Folder: <FolderName>]
✅ [CaptureToWimImage] Original image name: ...
✅ [CaptureToWimImage] Sanitized image name: ... (if special chars present)
✅ Disk incremental backup completed successfully!
```

### Error Logs to Watch For

```
❌ [BackupDisk] Failed to get volume label, Error: <code>
❌ [BackupDisk] ERROR: <error_message>
❌ Failed to set image metadata (Error 1465)
```

---

## Test Results Template

Use this template to document test results:

```markdown
## Test Execution Results

**Date:** _____________  
**Tester:** _____________  
**Version Tested:** 6.0.1.13  
**Environment:** _____________

### Test Case 1: User's Scenario (Bay2_512MG)
- [ ] Test Executed
- [ ] Volume label "Bay2_512MG" retrieved correctly
- [ ] Metadata shows volume label (not folder name)
- [ ] Backup completed successfully
- [ ] No error 1465
- **Notes:** _____________

### Test Case 2: Multiple Folders
- [ ] Test Executed
- [ ] All folders use same volume label
- [ ] Folder names distinct in metadata
- [ ] Backup completed successfully
- **Notes:** _____________

### Test Case 3: Unlabeled Volume
- [ ] Test Executed
- [ ] "Unlabeled" used as default
- [ ] Backup completed successfully
- **Notes:** _____________

### Test Case 4: Multiple Volumes
- [ ] Test Executed
- [ ] Each volume label retrieved
- [ ] Metadata distinguishes volumes
- [ ] Backup completed successfully
- **Notes:** _____________

### Test Case 5: Special Characters
- [ ] Test Executed
- [ ] XML sanitization applied
- [ ] Both fixes work together
- [ ] Backup completed successfully
- **Notes:** _____________

### Test Case 6: Error Handling
- [ ] Test Executed
- [ ] "Unknown" fallback used
- [ ] Error logged correctly
- [ ] Backup continued safely
- **Notes:** _____________

### Test Case 7: Similar Names
- [ ] Test Executed
- [ ] Volume and folder names distinguished
- [ ] Metadata clear and unambiguous
- [ ] Backup completed successfully
- **Notes:** _____________

### Regression Tests
- [ ] Exclusions work correctly
- [ ] VSS snapshots functional
- [ ] Compression settings apply
- [ ] Incremental/differential work
- [ ] Error handling appropriate
- **Notes:** _____________

### Overall Assessment
- **PASS** / **FAIL** / **NEEDS INVESTIGATION**
- **Critical Issues:** _____________
- **Minor Issues:** _____________
- **Recommendations:** _____________
```

---

## Troubleshooting Guide

### Issue: "Failed to get volume label, Error: 5"

**Meaning:** Access Denied (Error 5)

**Solutions:**
- Run BackupService with administrator privileges
- Check volume permissions
- Verify volume is mounted and accessible

### Issue: Volume Label Shows "Unknown"

**Causes:**
- GetVolumeInformationW API error
- Volume not mounted
- Access denied
- Volume GUID path incorrect

**Diagnosis:**
- Check debug logs for specific error code
- Use Disk Management to verify volume status
- Test GetVolumeInformationW with PowerShell:
  ```powershell
  $vol = Get-Volume -DriveLetter X
  $vol.FileSystemLabel
  ```

### Issue: Backup Still Shows Folder Names

**Causes:**
- Running old version (not 6.0.1.13)
- DLL not updated (BackupEngine.dll cached)
- Build artifacts from old version

**Solutions:**
- Verify version in UI: Help → About
- Check file version of BackupEngine.dll
- Rebuild solution in Release mode
- Restart BackupService

### Issue: Error 1465 Still Occurs

**Causes:**
- Volume label contains invalid XML characters (& < > " ')
- XML sanitization not applied
- WIM API issue

**Diagnosis:**
- Check if version 6.0.1.12 XML sanitization is active
- Review debug logs for "Sanitized image name"
- Test with simple volume label (e.g., "TestVol")

---

## Sign-Off Checklist

Before deploying to production:

- [ ] All test cases executed and passed
- [ ] No error 1465 in any test scenario
- [ ] Debug logs show volume labels retrieved correctly
- [ ] WIM metadata verified with inspection tools
- [ ] Regression tests passed
- [ ] Performance acceptable (no slowdown)
- [ ] Error handling verified
- [ ] Documentation reviewed and accurate
- [ ] Version numbers consistent (6.0.1.13)
- [ ] Build artifacts ready for deployment

---

## Summary

Version 6.0.1.13 introduces volume label retrieval to fix the critical metadata bug. Testing should verify:
1. Volume labels retrieved correctly using GetVolumeInformationW
2. Metadata uses volume labels (not folder names)
3. Graceful handling of unlabeled volumes and errors
4. XML sanitization still works (version 6.0.1.12)
5. No regression in existing functionality

**Critical Success Metric:** User's backup of Disk 5 (Bay2_512MG) completes successfully with correct metadata! 🎯
