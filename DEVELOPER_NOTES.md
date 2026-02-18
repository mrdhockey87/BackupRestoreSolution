# BackupRestoreSolution - Developer Notes

## Build System Architecture

### Centralized Build Configuration
This solution uses **Directory.Build.props** for centralized build configuration across all projects.

#### Key Configuration Details:

**Version Management:**
- All projects share a single version number defined in `Directory.Build.props`
- Change version ONCE in Directory.Build.props and ALL projects (BackupUI, BackupService, BackupEngine) update automatically
- Current version: **5.13.3.8**

**Build Output Structure:**
```
artifacts/
  ??? bin/
  ?   ??? Debug/          # All compiled binaries go here
  ?       ??? BackupUI.exe
  ?       ??? BackupService.exe
  ?       ??? BackupEngine.dll
  ?       ??? BackupUI.runtimeconfig.json      ? CRITICAL
  ?       ??? BackupService.runtimeconfig.json  ? CRITICAL
  ??? obj/
      ??? BackupUI/       # Intermediate files per project
      ??? BackupService/
      ??? BackupEngine/
```

**CRITICAL: Runtime Config Files**
- Both `BackupUI.runtimeconfig.json` and `BackupService.runtimeconfig.json` MUST exist in `artifacts\bin\Debug\`
- Without these files, you get ".NET Desktop Runtime required" errors
- `GenerateRuntimeConfigurationFiles=true` in Directory.Build.props ensures these are generated
- `ProduceReferenceAssembly=false` ensures generation works with custom output paths

### Project Dependencies
Build order is enforced via solution dependencies:
1. **BackupEngine** (C++ DLL) - builds first
2. **BackupService** (depends on BackupEngine)
3. **BackupUI** (depends on both BackupEngine and BackupService models)

### Key Properties in Directory.Build.props

**DO NOT REMOVE OR MODIFY THESE:**
```xml
<!-- Ensures runtime config files are generated for ALL managed projects -->
<GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
<GenerateDependencyFile>true</GenerateDependencyFile>
<ProduceReferenceAssembly>false</ProduceReferenceAssembly>

<!-- Prevents TFM subfolders - keeps all binaries together -->
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
<AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
```

## Named Pipe Communication

### BackupServiceClient Architecture
- UI communicates with BackupService via Named Pipes: `\\.\pipe\BackupServicePipe`
- Commands: `RunBackup`, `GetProgress`, `AbortBackup`, `GetVersion`

**CRITICAL Implementation Details:**
```csharp
// MUST use leaveOpen: true to prevent premature pipe disposal
using (var writer = new StreamWriter(pipeClient, Encoding.UTF8, leaveOpen: true))
using (var reader = new StreamReader(pipeClient, Encoding.UTF8, leaveOpen: true))
{
    await writer.WriteLineAsync(command);
    await writer.FlushAsync();  // MUST flush before reading response
    return await reader.ReadLineAsync();
}
```

**Why leaveOpen: true?**
- Without it, StreamWriter/Reader dispose the underlying pipe before data transmits
- Causes "Unknown (old version)" errors and communication failures
- Explicit `FlushAsync()` ensures data is sent before reading response

## Version History Notes

### Version Number System
After note add mdail DATE format mm/dd/yyyy
Format: `MAJOR.MINOR.PATCH.BUILD`
- **MAJOR** (5): Breaking changes, major features
- **MINOR** (13): New features, significant updates
- **PATCH** (3): Bug fixes, minor improvements
- **BUILD** (8): Incremental changes within a patch

### Recent Critical Fixes
- **5.13.3.8**: Fixed runtime config generation persistence
- **5.13.3.7**: Fixed named pipe communication with leaveOpen parameter
- **5.13.3.6**: Service communication simplification
- **5.13.3.5**: Initial runtime config generation fix

## Common Development Workflows

### Diagnostic Scripts

The solution includes PowerShell scripts for quick diagnostics:

```powershell
# Check if service is running latest build
.\Check-ServiceVersion.ps1

# Test named pipe communication
.\Test-NamedPipe.ps1

# Reinstall service with latest build (requires Admin)
.\Reinstall-Service.ps1
```

### Running BackupService in Debug Mode
```csharp
#if DEBUG
// Service runs as console app - no installation needed
// Set breakpoints and press F5
#endif
```

### Verifying Runtime Config Files
```powershell
Get-ChildItem "artifacts\bin\Debug" -Filter "*.runtimeconfig.json"
```
Should show both BackupUI and BackupService files.

### After Modifying Directory.Build.props
1. Clean solution
2. Rebuild all projects
3. Verify runtime config files exist
4. Test both BackupUI.exe and BackupService.exe launch without errors

## DO NOT DO THIS

? **Never add these to individual .csproj files:**
- `<Version>` - Use Directory.Build.props instead
- `<OutputPath>` - Centralized in Directory.Build.props
- `<GenerateRuntimeConfigurationFiles>` - Already in Directory.Build.props
- `<AppendTargetFrameworkToOutputPath>` - Would break centralized build

? **Never remove from Directory.Build.props:**
- `GenerateRuntimeConfigurationFiles=true`
- `ProduceReferenceAssembly=false`
- Output path configurations

## Project Structure

```
BackupRestoreSolution/
??? Directory.Build.props              ? CENTRALIZED BUILD CONFIG
??? BackupUI/                          ? WPF Application
?   ??? BackupUI.csproj
?   ??? VersionClass.cs                ? Version history documentation
?   ??? Services/
?       ??? BackupServiceClient.cs     ? Named pipe client
??? BackupService/                     ? Windows Service
?   ??? BackupService.csproj
?   ??? Services/
?       ??? BackupServiceCommunication.cs  ? Named pipe server
??? BackupEngine/                      ? C++ Native DLL
?   ??? BackupEngine.vcxproj
??? artifacts/                         ? Build output
    ??? bin/Debug/                     ? All executables and DLLs
    ??? obj/                           ? Intermediate files
```

## Troubleshooting

See **TROUBLESHOOTING.md** for common issues and solutions.

## Questions?

If you encounter build issues:
1. Check this file first
2. Check TROUBLESHOOTING.md
3. Verify Directory.Build.props hasn't been modified
4. Clean and rebuild solution
5. Check runtime config files exist

---
**Last Updated:** 2/14/2026 (Version 5.13.3.8)
