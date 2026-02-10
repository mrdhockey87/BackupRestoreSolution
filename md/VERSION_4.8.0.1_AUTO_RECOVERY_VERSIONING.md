# Version 4.8.0.1 - Enhanced Auto-Recovery Versioning

## ?? Enhancement Summary
Improved the auto-recovery system to use **incremental version suffixes** (_V1, _V2, _V3...) instead of a single _V1, preventing data loss and enabling forensic analysis of multiple failures.

---

## ? What Changed

### Before (Version 4.8.0.0):
- Failed backup renamed to: `Full_20260130_V1.bak`
- Second failure would **overwrite** the first V1 backup
- No history of multiple failures
- Lost previous failed backup data

### After (Version 4.8.0.1):
- Failed backup renamed to: `Full_20260130_V1.bak`
- Second failure renamed to: `Full_20260130_V2.bak`
- Third failure renamed to: `Full_20260130_V3.bak`
- **Preserves all failed backups** for analysis
- Automatic version number detection and increment

---

## ?? How It Works

### Version Detection Algorithm

```csharp
1. System detects failed backup: "Full_20260130.bak"
2. Scans directory for existing versions:
   - Full_20260130_V1.bak (found)
   - Full_20260130_V2.bak (found)
   - Full_20260130_V3.bak (not found)
3. Finds highest version: V2
4. Increments: V2 + 1 = V3
5. Renames to: "Full_20260130_V3.bak"
```

### Example Timeline

```
Timeline of Backups:
====================

Jan 30, 10:00 AM: Full_20260130_100000.bak created
Jan 30, 10:30 AM: Validation FAILS
                  ? Renamed to: Full_20260130_100000_V1.bak
                  
Jan 30, 11:00 AM: Full_20260130_110000.bak created (new full backup)
Jan 30, 11:30 AM: Validation FAILS again
                  ? Renamed to: Full_20260130_110000_V1.bak
                  
Jan 30, 12:00 PM: Inc_20260130_120000.bak created (incremental)
Jan 30, 12:30 PM: Validation FAILS
                  ? Renamed to: Inc_20260130_120000_V1.bak

Result: All 3 failed backups preserved!
```

---

## ?? File Naming Examples

### Single File Backups
```
Original:        ServerBackup_Full.bak
First failure:   ServerBackup_Full_V1.bak
Second failure:  ServerBackup_Full_V2.bak
Third failure:   ServerBackup_Full_V3.bak
```

### Directory Backups
```
Original:        Full_20260130_153000/
First failure:   Full_20260130_153000_V1/
Second failure:  Full_20260130_153000_V2/
Third failure:   Full_20260130_153000_V3/
```

### Split Backups (Multiple Files)
```
Original Files:
  - ServerBackup_Full.001
  - ServerBackup_Full.002
  - ServerBackup_Full.003
  - ServerBackup_Full.manifest

After First Failure:
  - ServerBackup_Full_V1.001
  - ServerBackup_Full_V1.002
  - ServerBackup_Full_V1.003
  - ServerBackup_Full_V1.manifest

After Second Failure:
  - ServerBackup_Full_V2.001
  - ServerBackup_Full_V2.002
  - ServerBackup_Full_V2.003
  - ServerBackup_Full_V2.manifest
```

---

## ?? Code Changes

### Updated Method: `GetNextVersionNumber()`
```csharp
private static int GetNextVersionNumber(string directory, string baseName, string extension = "")
{
    int maxVersion = 0;

    // Pattern to match: basename_V1, basename_V2, etc.
    string searchPattern = string.IsNullOrEmpty(extension) 
        ? $"{baseName}_V*" 
        : $"{baseName}_V*{extension}";

    var existingFiles = Directory.GetFileSystemEntries(directory, searchPattern);

    foreach (var file in existingFiles)
    {
        var fileName = Path.GetFileNameWithoutExtension(file);
        
        // Extract version number from filename
        var vIndex = fileName.LastIndexOf("_V", StringComparison.OrdinalIgnoreCase);
        if (vIndex >= 0)
        {
            var versionStr = fileName.Substring(vIndex + 2);
            if (int.TryParse(versionStr, out int version))
            {
                maxVersion = Math.Max(maxVersion, version);
            }
        }
    }

    return maxVersion + 1; // Next version
}
```

### Updated Method: `RenameFailedBackup()`
Now calls `GetNextVersionNumber()` to determine the correct version:
```csharp
int version = GetNextVersionNumber(parentPath, baseName);
var newName = $"{baseName}_V{version}";
```

### New Method: `RenameRelatedFiles()`
Ensures all associated files (split files, metadata, manifests) get the same version number:
```csharp
private static void RenameRelatedFiles(string directory, string baseName, int version)
{
    var relatedFiles = Directory.GetFiles(directory, $"{baseName}.*");
    foreach (var file in relatedFiles)
    {
        var newName = $"{nameWithoutExt}_V{version}{extension}";
        fileInfo.MoveTo(newPath);
    }
}
```

---

## ?? Benefits

### For IT Administrators:
? **Forensic Analysis** - Review all failed backups to identify patterns
? **Data Safety** - Never lose previous failed backups
? **Troubleshooting** - Compare multiple failures to find root cause
? **Audit Trail** - Complete history of backup attempts

### For System Reliability:
? **No Data Loss** - All failures preserved
? **Smart Versioning** - Automatic detection and increment
? **Clean Naming** - Underscore prefix for clarity (_V1, not V1)
? **Consistent Behavior** - Works for files, directories, and split backups

---

## ?? Use Cases

### Scenario 1: Intermittent Network Issue
```
Problem: Network drops during backup occasionally
Result:
  - Full_20260130_V1.bak (network timeout)
  - Full_20260130_V2.bak (network timeout)
  - Full_20260130_V3.bak (disk full on target)
  - Full_20260130.bak (SUCCESS!)

Analysis: Review V1 and V2 logs ? Find network issue
         Review V3 log ? Find disk space issue
         Fix both ? V4 succeeds
```

### Scenario 2: Disk Space Issues
```
Problem: Backup destination running out of space
Result:
  - Incremental_V1.bak (disk full - 95%)
  - Incremental_V2.bak (disk full - 98%)
  - Incremental_V3.bak (disk full - 99%)

Analysis: Pattern shows progressive disk fill
Action: Add more storage or implement cleanup policy
```

### Scenario 3: Data Corruption
```
Problem: Source data corruption
Result:
  - Full_V1.bak (validation failed - checksum error)
  - Full_V2.bak (validation failed - checksum error)

Analysis: Same files failing ? Source disk issue
Action: Run disk check on source before backup
```

---

## ?? Activity Log Integration

All rename operations are logged in the Activity tab:

```
[INFO] Auto-Recovery: Renamed failed backup: Full_20260130 ? Full_20260130_V3
[INFO] Auto-Recovery: Renamed related file: Full_20260130.manifest ? Full_20260130_V3.manifest
[INFO] Auto-Recovery: Renamed related file: Full_20260130.001 ? Full_20260130_V3.001
[INFO] Auto-Recovery: Renamed related file: Full_20260130.002 ? Full_20260130_V3.002
```

---

## ?? Configuration

### Unlimited Versions
The system has **no limit** on version numbers. It will continue incrementing:
- _V1, _V2, _V3... _V99, _V100, _V1000...

### Cleanup Strategy (Future Enhancement)
Consider implementing:
```csharp
// Keep only last 5 failed versions
public static void CleanupOldFailedBackups(string baseName, int keepVersions = 5)
{
    // Delete _V1 through _V(n-5)
    // Keep _V(n-4) through _V(n)
}
```

---

## ?? Testing Scenarios

### Test 1: Single Failure
1. Create backup: `Test_Full.bak`
2. Manually fail validation
3. Verify renamed to: `Test_Full_V1.bak`

### Test 2: Multiple Failures
1. Create backup: `Test_Full.bak` ? Fail ? `Test_Full_V1.bak`
2. Create backup: `Test_Full.bak` ? Fail ? `Test_Full_V2.bak`
3. Create backup: `Test_Full.bak` ? Fail ? `Test_Full_V3.bak`
4. Verify all 3 exist

### Test 3: Non-Sequential
1. Manually create: `Test_Full_V1.bak` and `Test_Full_V5.bak`
2. Create backup: `Test_Full.bak` ? Fail
3. Verify renamed to: `Test_Full_V6.bak` (found V5, incremented)

### Test 4: Split Files
1. Create split backup:
   - `Test.001`
   - `Test.002`
   - `Test.manifest`
2. Fail validation
3. Verify all renamed:
   - `Test_V1.001`
   - `Test_V1.002`
   - `Test_V1.manifest`

---

## ?? Summary

Version 4.8.0.1 enhances the auto-recovery system with **intelligent version management**:

- ? Incremental version numbers (_V1, _V2, _V3...)
- ? Automatic detection of existing versions
- ? Never overwrites previous failures
- ? Works with files, directories, and split backups
- ? Comprehensive activity logging
- ? Enables forensic analysis of backup failures

This ensures **data preservation** and **troubleshooting capability** for production environments! ??
