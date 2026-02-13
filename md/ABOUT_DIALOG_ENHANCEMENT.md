# About Dialog with Component Versions (Version 5.13.3.1)

## Overview

The new About dialog (Help ? About) displays comprehensive version information for all 3 components of the Backup & Restore Solution, with real-time service status checking and version mismatch detection.

## Features

### 1. Professional About Dialog

**Access**: Help ? About (from main menu)

**Displays**:
- Application title and main version
- All 3 component versions (UI, Service, Engine)
- Service status with warnings
- Full feature list
- Copyright information

### 2. Component Versions Table

| Component | Description | Source |
|-----------|-------------|--------|
| **User Interface (BackupUI)** | WPF application | Assembly version |
| **Backup Service** | Windows Service | Named Pipe (if running) |
| **Backup Engine (C++)** | Native DLL | Shared version (Directory.Build.props) |

### 3. Service Status Indicators

The dialog shows real-time service status with appropriate warnings:

#### Service Running (Match)
```
Backup Service: 5.13.3.1
```

#### Service Running (Mismatch)
```
Backup Service: 5.13.2.9 ?? Version Mismatch!
```

#### Service Stopped
```
Backup Service: N/A (Stopped) ?? Stopped
```

#### Service Not Installed
```
Backup Service: Not Installed ?? Not Installed
```

#### Service Not Responding
```
Backup Service: Unknown (not responding) ?? Not Responding
```

## UI Layout

```
????????????????????????????????????????????????????
?  Backup & Restore Solution                       ?
?  Version 5.13.3.1                               ?
????????????????????????????????????????????????????
?                                                  ?
?  Component Versions                              ?
?  ?????????????????????????????????????????????? ?
?  ? User Interface (BackupUI): 5.13.3.1       ? ?
?  ? Backup Service:           5.13.3.1        ? ?
?  ? Backup Engine (C++):      5.13.3.1        ? ?
?  ?????????????????????????????????????????????? ?
?                                                  ?
?  Description                                     ?
?  ?????????????????????????????????????????????? ?
?  ? Enterprise-grade backup solution with:    ? ?
?  ? • Full, Incremental, Differential backups ? ?
?  ? • Hyper-V Virtual Machine backups         ? ?
?  ? • VSS integration                         ? ?
?  ? • System State backup/restore             ? ?
?  ? • Disk and Volume cloning                 ? ?
?  ? • Network path support                    ? ?
?  ? • Scheduled automated backups             ? ?
?  ? • Windows Service execution               ? ?
?  ? • Mount backups as drives                 ? ?
?  ? • Cross-platform restore (Linux)          ? ?
?  ?????????????????????????????????????????????? ?
?                                                  ?
?  Copyright                                       ?
?  ?????????????????????????????????????????????? ?
?  ? © 2026 Backup & Restore Solution          ? ?
?  ? Developed with GitHub Copilot assistance  ? ?
?  ?????????????????????????????????????????????? ?
?                                                  ?
????????????????????????????????????????????????????
?                                          [  OK  ]?
????????????????????????????????????????????????????
```

## Implementation Details

### Version Retrieval

**UI Version**:
```csharp
var uiVersion = VersionClass.GetAssemblyVersion();
// Returns: "5.13.3.1"
```

**Service Version**:
```csharp
var serviceClient = new BackupServiceClient();
var serviceVersion = await serviceClient.GetServiceVersionAsync();
// Returns: "5.13.3.1" or null
```

**Engine Version**:
```csharp
// Same as UI - they share version from Directory.Build.props
var engineVersion = VersionClass.GetAssemblyVersion();
```

### Service Status Check

```csharp
using var service = new ServiceController("BackupRestoreService");

if (service.Status == ServiceControllerStatus.Running)
{
    // Query via Named Pipe
    var version = await serviceClient.GetServiceVersionAsync();
}
else
{
    // Show status (Stopped, Paused, etc.)
    txtServiceVersion.Text = $"N/A ({service.Status})";
}
```

### Version Mismatch Detection

```csharp
if (serviceVersion != uiVersion)
{
    txtServiceWarning.Text = " ?? Version Mismatch!";
    txtServiceWarning.Foreground = Brushes.Red;
    txtServiceWarning.Visibility = Visibility.Visible;
}
```

## Files Created/Modified

- ? `BackupUI\Windows\AboutWindow.xaml` - New About dialog XAML
- ? `BackupUI\Windows\AboutWindow.xaml.cs` - New About dialog code-behind
- ? `BackupUI\MainWindow.xaml.cs` - Updated About_Click to open new dialog
- ? `BackupUI\VersionClass.cs` - Updated to 5.13.3.1
- ? `Directory.Build.props` - Updated to 5.13.3.1
- ? Build successful!

## Use Cases

### 1. Quick Version Check
User: "What version am I running?"
- Help ? About
- See main version at top: "Version 5.13.3.1"

### 2. Verify All Components Match
User: "Are all my components the same version?"
- Help ? About
- Check Component Versions section
- All should show same version
- No warnings visible

### 3. Diagnose Service Issues
User: "Why isn't my service working?"
- Help ? About
- Check Backup Service line:
  - ?? Not Installed ? Run Install-BackupService.ps1
  - ?? Stopped ? Start service
  - ?? Version Mismatch ? Reinstall service
  - ?? Not Responding ? Restart service

### 4. Troubleshoot After Update
User: "I updated the code but backups still failing"
- Help ? About
- Check for "?? Version Mismatch!"
- If present ? Service wasn't reinstalled
- Solution: Reinstall service with new version

### 5. Support Requests
User reporting issue:
- Help ? About
- Take screenshot
- Include in support request
- Shows exact versions of all components

## Comparison

### Old Way (Before 5.13.3.1)
```
[MessageBox]
Backup & Restore Solution
Version: 5.13.3.0

Enterprise backup with scheduling 
and disaster recovery

[ OK ]
```

**Problems**:
- Only shows UI version
- No service version
- No engine version
- No service status
- No version mismatch detection
- No feature list

### New Way (Version 5.13.3.1+)
```
[Professional Dialog]
- Header with main version
- 3 component versions with real-time status
- Version mismatch warnings
- Service status indicators
- Complete feature list
- Copyright info
- Scrollable content
```

**Benefits**:
- ? All component versions visible
- ? Real-time service status
- ? Automatic version mismatch detection
- ? Professional appearance
- ? Complete feature documentation
- ? Troubleshooting support

## Warning States

### ?? Version Mismatch
**Meaning**: Service version differs from UI version
**Cause**: Service wasn't reinstalled after update
**Solution**: 
```powershell
.\Uninstall-BackupService.ps1
.\Install-BackupService.ps1
```

### ?? Not Running
**Meaning**: Service is installed but stopped
**Cause**: Service was stopped or failed to start
**Solution**: Start service via Service Management or services.msc

### ?? Not Installed
**Meaning**: Service doesn't exist on system
**Cause**: Never installed or was uninstalled
**Solution**:
```powershell
.\Install-BackupService.ps1
```

### ?? Not Responding
**Meaning**: Service is running but Named Pipe isn't working
**Cause**: Service hung or Named Pipe listener failed
**Solution**: Restart service

## Testing

### Test 1: All Components Match (Ideal)
1. Build solution
2. Install service
3. Help ? About
4. **Expected**:
   - UI: 5.13.3.1
   - Service: 5.13.3.1
   - Engine: 5.13.3.1
   - No warnings

### Test 2: Version Mismatch
1. Build solution (version 5.13.3.1)
2. Install service
3. Change Directory.Build.props to 5.13.3.2
4. Build UI only
5. Help ? About
6. **Expected**:
   - UI: 5.13.3.2
   - Service: 5.13.3.1 ?? Version Mismatch!
   - Engine: 5.13.3.2

### Test 3: Service Stopped
1. Stop BackupRestoreService
2. Help ? About
3. **Expected**:
   - Service: N/A (Stopped) ?? Stopped

### Test 4: Service Not Installed
1. Uninstall service
2. Help ? About
3. **Expected**:
   - Service: Not Installed ?? Not Installed

### Test 5: Service Hung
1. Kill service process without stopping service
2. Help ? About
3. **Expected**:
   - Service: Unknown (not responding) ?? Not Responding

## Future Enhancements

Possible improvements:
1. Click warning to auto-fix (restart service, etc.)
2. Copy version info to clipboard button
3. Check for updates button
4. Show last backup date/time
5. Show disk space at backup destination
6. Export system info button
7. Links to documentation
8. Release notes button

## Status

? **COMPLETE** in version 5.13.3.1

Professional About dialog with full component version display and service health monitoring!

---

**Version**: 5.13.3.1  
**Date**: 2/13/2026  
**Feature**: About dialog with all component versions and service status
