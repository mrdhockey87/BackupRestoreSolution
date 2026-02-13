# Centralized Version Management (Version 5.13.3.0)

## Overview

All projects in the solution (BackupUI, BackupService, BackupEngine) now share a **single version number** defined in `Directory.Build.props`. The Service Management window displays both UI and Service versions with automatic mismatch detection.

## Key Features

### 1. Single Source of Truth
**File**: `Directory.Build.props`

```xml
<PropertyGroup>
  <Version>5.13.3.0</Version>
  <AssemblyVersion>5.13.3.0</AssemblyVersion>
  <FileVersion>5.13.3.0</FileVersion>
  <InformationalVersion>5.13.3.0</InformationalVersion>
</PropertyGroup>
```

- ? One place to change version
- ? All 3 projects automatically sync
- ? No more version conflicts

### 2. Service Version Retrieval
**New Command**: `GetVersion` via Named Pipe

The service now responds to version queries:
```csharp
case "GetVersion":
    var version = GetServiceVersion();
    return CreateResponse(true, version);
```

### 3. Service Management Window Enhancements

#### UI Changes
- **UI Version**: Always displayed (from assembly)
- **Service Version**: Retrieved via Named Pipe when service is running
- **Version Mismatch Warning**: Red "? VERSION MISMATCH!" appears when versions don't match

#### States
| Service State | UI Version | Service Version | Warning |
|--------------|------------|-----------------|---------|
| Not Installed | Shows | "N/A (not installed)" | Hidden |
| Stopped | Shows | "N/A (service not running)" | Hidden |
| Running (Match) | Shows | Shows (same) | Hidden |
| Running (Mismatch) | Shows | Shows (different) | **? Visible** |

## How It Works

### Version Flow
```
1. Developer changes version in Directory.Build.props
   ?
2. MSBuild propagates to all projects
   ?
3. BackupUI assembly gets version 5.13.3.0
   ?
4. BackupService assembly gets version 5.13.3.0
   ?
5. BackupEngine assembly gets version 5.13.3.0
   ?
6. Service Management window compares:
   - UI: VersionClass.GetAssemblyVersion()
   - Service: BackupServiceClient.GetServiceVersionAsync()
   ?
7. If different ? Show warning!
```

### Named Pipe Communication
```
UI ? Named Pipe ? Service
{
  "CommandType": "GetVersion",
  "Data": null
}
                ?
        Service Responds
{
  "Success": true,
  "Message": "5.13.3.0"
}
```

## Updating the Version

### Single Step Process
1. Open `Directory.Build.props`
2. Change version numbers:
   ```xml
   <Version>5.14.0.0</Version>
   <AssemblyVersion>5.14.0.0</AssemblyVersion>
   <FileVersion>5.14.0.0</FileVersion>
   <InformationalVersion>5.14.0.0</InformationalVersion>
   ```
3. Update fallback in `BackupUI\VersionClass.cs`:
   ```csharp
   return "5.14.0.0";
   ```
4. Add version note to `BackupUI\VersionClass.cs` comment block
5. **Done!** All projects now use 5.14.0.0

### Old Way (Before 5.13.3.0) ?
1. Update BackupUI.csproj
2. Update BackupService.csproj  
3. Update BackupEngine.vcxproj
4. Update VersionClass fallback
5. Hope you didn't miss one
6. Discover version mismatch after deploy

### New Way (Version 5.13.3.0+) ?
1. Update Directory.Build.props
2. Update VersionClass fallback
3. Add version note
4. Build
5. Service Management window validates versions automatically

## Testing Version Mismatch

### Scenario 1: Intentional Mismatch (for testing)
1. Build solution (all at 5.13.3.0)
2. Install service: `.\Install-BackupService.ps1`
3. Change Directory.Build.props to 5.14.0.0
4. Build BackupUI only
5. Open Service Management window
6. **Result**: 
   - UI Version: 5.14.0.0
   - Service Version: 5.13.3.0
   - Warning: ? VERSION MISMATCH!

### Scenario 2: Proper Upgrade
1. Change Directory.Build.props to 5.14.0.0
2. Build entire solution
3. Reinstall service:
   ```powershell
   .\Uninstall-BackupService.ps1
   .\Install-BackupService.ps1
   ```
4. Open Service Management window
5. **Result**:
   - UI Version: 5.14.0.0
   - Service Version: 5.14.0.0
   - Warning: Hidden

## Service Management Window UI

```
?? Service Status ?????????????????????????
? Status:          Running                ?
? Installed:       Yes                    ?
? ?????????????????????????????????????? ?
? UI Version:      5.13.3.0               ?
? Service Version: 5.13.3.0               ?
?                                         ?
? [Refresh Status]                        ?
???????????????????????????????????????????
```

With mismatch:
```
?? Service Status ?????????????????????????
? Status:          Running                ?
? Installed:       Yes                    ?
? ?????????????????????????????????????? ?
? UI Version:      5.14.0.0               ?
? Service Version: 5.13.3.0 ? VERSION MISMATCH! ?
?                                         ?
? [Refresh Status]                        ?
???????????????????????????????????????????
```

## Files Modified

- ? `Directory.Build.props` - Centralized version definition
- ? `BackupUI\BackupUI.csproj` - Removed local version (uses shared)
- ? `BackupUI\VersionClass.cs` - Made GetAssemblyVersion() public
- ? `BackupUI\Services\BackupServiceClient.cs` - Added GetServiceVersionAsync()
- ? `BackupService\BackupServiceCommunication.cs` - Added GetVersion handler
- ? `BackupUI\Windows\ServiceManagementWindow.xaml` - Added version display and warning
- ? `BackupUI\Windows\ServiceManagementWindow.xaml.cs` - Version comparison logic

## Benefits

### 1. Consistency
- All projects always have the same version
- Impossible to have mismatched assemblies after build
- Clear indication when service needs reinstallation

### 2. Maintainability
- Change version once
- No hunting through project files
- Version history in one location

### 3. Visibility
- Always see both UI and Service versions
- Immediate warning when out of sync
- No more guessing if service is up to date

### 4. Safety
- Prevents running old service with new UI
- Prevents running new service with old UI
- Version conflicts caught before problems occur

## When to Reinstall Service

### ? VERSION MISMATCH! appears:
1. Stop the BackupRestoreService
2. Rebuild entire solution
3. Run uninstall script:
   ```powershell
   .\Uninstall-BackupService.ps1
   ```
4. Run install script:
   ```powershell
   .\Install-BackupService.ps1
   ```
5. Verify in Service Management window

### Why Reinstall?
The service runs from a copy of `BackupService.exe` in the service manager. Simply building doesn't update the running service. You must:
1. Stop the old service
2. Replace the executable
3. Start the new service

The install scripts handle this automatically.

## Troubleshooting

### "Unknown (service not responding)"
- Service is running but Named Pipe isn't working
- Check: Named Pipe listener started? (see BACKUP_NOT_STARTING_FIX.md)
- Solution: Restart service

### "N/A (service not running)"
- Service is installed but stopped
- Check: Windows Services (services.msc)
- Solution: Start service or use Service Management window

### "N/A (not installed)"
- Service isn't installed
- Check: `Get-Service BackupRestoreService`
- Solution: Run `.\Install-BackupService.ps1`

### Warning shows but versions look the same
- Could be trailing zeros difference (5.13.3 vs 5.13.3.0)
- Check exact string comparison
- Rebuild and reinstall to sync

## Implementation Details

### Version Retrieval Methods

**UI Version**:
```csharp
VersionClass.GetAssemblyVersion()
// Returns: "5.13.3.0"
```

**Service Version**:
```csharp
var client = new BackupServiceClient();
var version = await client.GetServiceVersionAsync();
// Returns: "5.13.3.0" or null
```

### Version Comparison
```csharp
var uiVersion = VersionClass.GetAssemblyVersion();
var serviceVersion = await serviceClient.GetServiceVersionAsync();

if (serviceVersion != null && serviceVersion != uiVersion)
{
    txtVersionWarning.Visibility = Visibility.Visible;
}
else
{
    txtVersionWarning.Visibility = Visibility.Collapsed;
}
```

## Future Enhancements

Possible improvements:
1. Auto-reinstall service button when mismatch detected
2. Change log display showing what changed between versions
3. Version history in database
4. Automatic service restart after build (dev mode)
5. Semantic version comparison (5.13.3 == 5.13.3.0)

## Status

? **COMPLETE** in version 5.13.3.0

All projects synchronized, version display working, mismatch detection functional!

---

**Version**: 5.13.3.0  
**Date**: 2/13/2026  
**Feature**: Centralized version management with mismatch detection
