# BackupRestoreSolution - Troubleshooting Guide

## Common Build Issues

### Issue: Service Not Responding to Named Pipe Commands

**Symptoms:**
- "Unknown (old version)" in Service Management
- GetVersion returns null
- Service shows as Running but doesn't respond

**Diagnosis:**
```powershell
# Check if service is running latest build
.\Check-ServiceVersion.ps1

# Test named pipe communication
.\Test-NamedPipe.ps1
```

**Root Causes:**
1. **Old service binary still installed** - Service wasn't reinstalled after rebuild
2. **Named pipe listener not starting** - BackupServiceCommunication.StartAsync() not being called
3. **Permission issues** - Service account doesn't have permission to create named pipe

**Solution:**
```powershell
# Reinstall service with latest build (requires Administrator)
.\Reinstall-Service.ps1

# Verify installation
.\Check-ServiceVersion.ps1
.\Test-NamedPipe.ps1
```

**Manual Reinstall Steps:**
```powershell
# 1. Stop and remove old service
sc stop BackupRestoreService
sc delete BackupRestoreService

# 2. Build latest version
dotnet build BackupService\BackupService.csproj -c Debug

# 3. Install new service (update path as needed)
sc create BackupRestoreService binPath= "E:\VisualStudioProjects\BackupRestoreSolution\artifacts\bin\Debug\BackupService.exe" start= auto

# 4. Start service
sc start BackupRestoreService

# 5. Verify named pipe exists
[System.IO.Directory]::GetFiles("\\.\pipe\") | Select-String "BackupRestore"
```

**Verification:**
- Service version should match Directory.Build.props version
- Named pipe `\\.\pipe\BackupRestoreServicePipe` should exist
- Test-NamedPipe.ps1 should return success with version number

---

### Issue: "This application requires the .NET Desktop Runtime"

**Symptoms:**
- Application won't launch
- Error message: "You must install .NET Desktop Runtime"
- Occurs after Visual Studio restart

**Root Cause:**
- Missing `BackupUI.runtimeconfig.json` or `BackupService.runtimeconfig.json` files

**Solution:**
```powershell
# 1. Verify files exist
Get-ChildItem "artifacts\bin\Debug" -Filter "*.runtimeconfig.json"

# Should show:
# BackupUI.runtimeconfig.json
# BackupService.runtimeconfig.json

# 2. If missing, rebuild
dotnet clean
dotnet build
```

**Prevention:**
- Never remove `GenerateRuntimeConfigurationFiles=true` from Directory.Build.props
- Never remove `ProduceReferenceAssembly=false` from Directory.Build.props
- These settings ensure runtime config files are generated even with custom output paths

---

### Issue: "Unknown (old version)" in Service Management Window

**Symptoms:**
- Service version shows "Unknown (old version)" or "Unknown (check failed)"
- Service communication fails silently

**Root Cause:**
- Named pipe streams closing prematurely
- Missing `leaveOpen: true` parameter in StreamWriter/StreamReader

**Solution:**
Check `BackupServiceClient.cs` has correct implementation:
```csharp
using (var writer = new StreamWriter(pipeClient, Encoding.UTF8, leaveOpen: true))
using (var reader = new StreamReader(pipeClient, Encoding.UTF8, leaveOpen: true))
{
    await writer.WriteLineAsync(command);
    await writer.FlushAsync();  // CRITICAL: Must flush!
    return await reader.ReadLineAsync();
}
```

**Verification:**
- Service Management window should show matching versions for UI and Service
- About dialog (Help ? About) should show all 3 components with same version

---

### Issue: BackupEngine.dll Not Found

**Symptoms:**
- Build succeeds but runtime error: "Could not load BackupEngine.dll"
- P/Invoke calls fail with DllNotFoundException

**Root Cause:**
- BackupEngine.dll not copied to centralized output directory

**Solution:**
1. Build BackupEngine project first (C++ project)
2. Verify DLL exists:
```powershell
Get-ChildItem "artifacts\bin\Debug" -Filter "BackupEngine.dll"
```

3. Check BackupUI.csproj has copy target:
```xml
<Target Name="CopyBackupEngine" AfterTargets="Build">
  <!-- Copies BackupEngine.dll after build -->
</Target>
```

**Build Order:**
1. BackupEngine (C++)
2. BackupService (.NET)
3. BackupUI (.NET)

---

### Issue: Version Mismatch Warning

**Symptoms:**
- "? VERSION MISMATCH!" warning in Service Management
- UI shows version X.X.X.X but Service shows Y.Y.Y.Y

**Root Cause:**
- Service not rebuilt after version change
- Old service still installed/running

**Solution:**
```powershell
# 1. Stop and uninstall old service
sc stop BackupSchedulerService
sc delete BackupSchedulerService

# 2. Clean and rebuild entire solution
dotnet clean
dotnet build

# 3. Reinstall service with new version
# (Use service installation script)
```

**Prevention:**
- Always rebuild ALL projects when version changes
- Version is defined ONCE in Directory.Build.props
- All projects automatically get same version

---

### Issue: Named Pipe Server Not Starting

**Symptoms:**
- "Backup service is not responding" errors
- Service status shows "Running" but UI can't communicate

**Root Cause:**
- BackupServiceCommunication not registered as IHostedService
- Named pipe listener never starts

**Verification:**
Check `BackupService/Program.cs`:
```csharp
// MUST register as BOTH Singleton AND HostedService
builder.Services.AddSingleton<BackupServiceCommunication>();
builder.Services.AddHostedService(sp => 
    sp.GetRequiredService<BackupServiceCommunication>());
```

**Debug in Visual Studio:**
```csharp
#if DEBUG
// Service runs as console app - see real-time logging
// Set breakpoints in BackupServiceCommunication.StartAsync
#endif
```

---

### Issue: Builds Fail After Modifying Directory.Build.props

**Symptoms:**
- "Property X is not valid" errors
- Projects won't load
- Build errors across all projects

**Root Cause:**
- Syntax error in Directory.Build.props
- Invalid property values
- Condition evaluation errors

**Solution:**
1. Validate XML syntax in Directory.Build.props
2. Check for typos in property names
3. Verify conditions use correct syntax: `Condition="'$(Property)' == 'Value'"`

**Rollback:**
```powershell
# If all else fails, restore from Git
git checkout Directory.Build.props
```

---

## Debug Workflows

### Debugging BackupService

**Option 1: Console Mode (Recommended)**
1. Set configuration to Debug
2. Press F5 in Visual Studio
3. Service runs as console app (no installation needed)
4. Set breakpoints, view console output

**Option 2: Attach to Running Service**
1. Install and start service
2. Debug ? Attach to Process
3. Find BackupService.exe
4. Attach debugger

### Verifying Named Pipe Communication

```powershell
# Test from PowerShell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "BackupServicePipe", "InOut")
$pipe.Connect(3000)
$writer = New-Object System.IO.StreamWriter($pipe)
$reader = New-Object System.IO.StreamReader($pipe)

$writer.WriteLine("GetVersion")
$writer.Flush()
$response = $reader.ReadLine()
Write-Host "Version: $response"

$pipe.Close()
```

### Checking Service Logs

```powershell
# Service startup log
Get-Content "C:\ProgramData\BackupRestoreService\Logs\startup.log" -Tail 50

# Activity log (from BackupLogger)
Get-Content "C:\ProgramData\BackupRestoreService\Logs\activity.log" -Tail 50
```

---

## Prevention Checklist

Before making changes to build configuration:

- [ ] Backup current Directory.Build.props
- [ ] Understand impact of changes
- [ ] Test with clean build
- [ ] Verify runtime config files generated
- [ ] Test both BackupUI and BackupService launch
- [ ] Check service version communication
- [ ] Run full backup job to verify functionality

---

## Getting Help

If issues persist:

1. Check **DEVELOPER_NOTES.md** for architecture details
2. Review recent commits in Git history
3. Check VersionClass.cs for recent changes and fixes
4. Clean solution and rebuild from scratch
5. Verify .NET 8 SDK is installed correctly

---

**Common Commands:**

```powershell
# Clean build
dotnet clean
dotnet build

# Verify runtime configs
Get-ChildItem "artifacts\bin\Debug" -Filter "*.runtimeconfig.json"

# Check service status
sc query BackupSchedulerService

# View recent build output
Get-ChildItem "artifacts\bin\Debug" | Sort-Object LastWriteTime -Descending
```

---
**Last Updated:** 2/14/2026 (Version 5.13.3.8)
