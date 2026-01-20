# Build and Deployment Guide

## Prerequisites

### Development Environment
- **Visual Studio 2022** (17.0 or later)
  - Workload: ".NET desktop development"
  - Workload: "Desktop development with C++"
  - Component: "Windows 10/11 SDK"
  
- **.NET 8 SDK** (included with Visual Studio 2022)

- **Windows Server 2019/2022/2025** (or Windows 10/11 for development/testing)

### Build Requirements
- Administrator privileges (for testing VSS and Hyper-V features)
- Hyper-V role installed (optional, for Hyper-V backup/restore features)

## Building the Solution

### 1. Open Solution
1. Open `BackupRestoreSolution.sln` in Visual Studio 2022
2. Select **x64** platform (this solution only supports 64-bit)
3. Select **Release** configuration for production builds

### 2. Build Order
The solution should build in this order:
1. **BackupEngine** (C++ DLL) - Core native library
2. **BackupUI** (C# WPF) - User interface
3. **BackupService** (C# Worker Service) - Windows service

### 3. Build Commands

**From Visual Studio:**
- Build ? Build Solution (Ctrl+Shift+B)

**From Command Line:**
```powershell
# Open Developer PowerShell for Visual Studio 2022
cd "C:\Path\To\BackupRestoreSolution"

# Build entire solution
msbuild BackupRestoreSolution.sln /p:Configuration=Release /p:Platform=x64

# Or build individual projects
msbuild BackupEngine\BackupEngine.vcxproj /p:Configuration=Release /p:Platform=x64
msbuild BackupUI\BackupUI.csproj /p:Configuration=Release /p:Platform=x64
msbuild BackupService\BackupService.csproj /p:Configuration=Release /p:Platform=x64
```

### 4. Build Output
After successful build, files will be in:
```
bin\Release\
??? BackupEngine.dll          # Core C++ library
??? BackupUI.exe             # Main UI application
??? BackupUI.dll             # UI library
??? BackupUI.runtimeconfig.json
??? BackupService.exe        # Windows service executable
??? BackupService.dll        # Service library
??? BackupService.runtimeconfig.json
??? [Various .NET runtime files]
```

## C++ Project Files to Add to BackupEngine.vcxproj

The BackupEngine project should include these source files:

### Header Files
- `BackupEngine.h` - Main API header (already exists)

### Source Files
- `BackupEngine.cpp` - Main implementations (update existing)
- `BackupEngine_Exports.cpp` - Export function wrappers (NEW)
- `BackupManager.cpp` - Basic backup logic (exists, may need updates)
- `BackupManager_Advanced.cpp` - Volume/Disk/Incremental/Differential (NEW)
- `RestoreEngine.cpp` - Basic restore logic (exists)
- `RestoreEngine_Advanced.cpp` - Volume/Disk restore (NEW)
- `VSSManager.cpp` - VSS snapshot management (exists)
- `VolumeEnumeration.cpp` - Enumerate volumes/disks (NEW)
- `HyperVManager.cpp` - Hyper-V WMI integration (exists)
- `HyperVBackup.cpp` - Hyper-V backup logic (exists)
- `HyperVRestore.cpp` - Hyper-V restore logic (exists)
- `HyperV_Enumeration.cpp` - Enumerate VMs (NEW)
- `SystemStateRestore.cpp` - System state operations (exists)
- `BackupVerification.cpp` - Backup verification (exists)
- `RecoveryEnvironment.cpp` - Recovery USB creator (NEW)

## Deployment

### Option 1: XCOPY Deployment (Simple)
1. Copy entire `bin\Release\` folder to target server
2. Run `BackupUI.exe` as Administrator

### Option 2: MSI Installer (Professional)
Use WiX Toolset or Visual Studio Installer Projects to create MSI:

**Files to include:**
- All files from `bin\Release\`
- Create shortcuts to BackupUI.exe
- Register Windows Service during installation

**Installer script would:**
1. Copy files to `C:\Program Files\BackupRestore\`
2. Install Windows Service:
   ```
   sc create BackupRestoreService binPath= "C:\Program Files\BackupRestore\BackupService.exe" start= auto
   sc start BackupRestoreService
   ```
3. Create Start Menu shortcuts
4. Set appropriate permissions

### Option 3: Manual Deployment

```powershell
# 1. Create deployment directory
New-Item -Path "C:\Program Files\BackupRestore" -ItemType Directory -Force

# 2. Copy files
Copy-Item -Path "bin\Release\*" -Destination "C:\Program Files\BackupRestore\" -Recurse -Force

# 3. Install Windows Service
sc.exe create BackupRestoreService binPath= "C:\Program Files\BackupRestore\BackupService.exe" start= auto
sc.exe start BackupRestoreService

# 4. Create Start Menu shortcut (optional)
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut("$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Backup & Restore.lnk")
$Shortcut.TargetPath = "C:\Program Files\BackupRestore\BackupUI.exe"
$Shortcut.WorkingDirectory = "C:\Program Files\BackupRestore"
$Shortcut.Description = "Enterprise Backup & Restore Solution"
$Shortcut.Save()
```

## Post-Deployment Configuration

### 1. Verify Installation
```powershell
# Check service status
sc query BackupRestoreService

# Test UI
Start-Process "C:\Program Files\BackupRestore\BackupUI.exe" -Verb RunAs
```

### 2. Create First Backup Job
1. Launch BackupUI.exe as Administrator
2. Go to Backup ? New Backup
3. Configure backup settings
4. Save job or run immediately

### 3. Schedule Backups
1. In BackupUI, go to Schedules ? Manage Schedules
2. Edit existing jobs or create new scheduled jobs
3. Ensure Windows Service is running

## Troubleshooting Build Issues

### Issue: BackupEngine.dll not found
**Solution:** Build BackupEngine project first, ensure it outputs to `bin\Release\`

### Issue: Missing .NET SDK
**Solution:** Install .NET 8 SDK from https://dot.net

### Issue: C++ build errors
**Solution:**
- Ensure Windows SDK is installed
- Verify platform toolset (v143 for VS2022)
- Check all .cpp files are added to project

### Issue: VSS-related link errors
**Solution:** Add `vssapi.lib` to linker dependencies in BackupEngine project

### Issue: Hyper-V WMI errors
**Solution:** Add `wbemuuid.lib` to linker dependencies

## Testing the Build

### Unit Testing Checklist
- [ ] BackupEngine.dll loads without errors
- [ ] BackupUI.exe launches and shows main window
- [ ] Service installs successfully
- [ ] Service starts without errors
- [ ] Can enumerate volumes
- [ ] Can enumerate disks
- [ ] Can enumerate Hyper-V VMs (if Hyper-V installed)

### Integration Testing Checklist
- [ ] Create full backup of test folder
- [ ] Restore backup to different location
- [ ] Create incremental backup
- [ ] Restore incremental backup chain
- [ ] Schedule backup job
- [ ] Verify scheduled job runs automatically
- [ ] Create recovery USB (if USB available)

## Performance Considerations

### Debug vs Release Builds
- **Debug builds**: Include debug symbols, slower, larger files
- **Release builds**: Optimized, smaller, faster - use for production

### Optimization Flags
BackupEngine C++ project uses:
- `/O2` - Maximum optimization (Release)
- `/std:c++17` - C++ 17 standard
- `/MT` or `/MD` - Runtime library linking

## Distribution

### Minimal Distribution Package
Required files for end-user deployment:
```
BackupRestore/
??? BackupUI.exe
??? BackupUI.dll
??? BackupUI.runtimeconfig.json
??? BackupService.exe
??? BackupService.dll
??? BackupService.runtimeconfig.json
??? BackupEngine.dll
??? [.NET 8 runtime files]
```

### Self-Contained Deployment
To include .NET runtime (no .NET installation required):

```powershell
# Publish self-contained
dotnet publish BackupUI\BackupUI.csproj -c Release -r win-x64 --self-contained true
dotnet publish BackupService\BackupService.csproj -c Release -r win-x64 --self-contained true
```

This creates larger package but doesn't require .NET runtime on target machine.

## Version Management

Update version in:
1. **BackupUI**: `BackupUI.csproj` ? `<Version>1.0.0</Version>`
2. **BackupService**: `BackupService.csproj` ? `<Version>1.0.0</Version>`
3. **BackupEngine**: Version resource file (if created)
4. **About dialog**: `MainWindow.xaml.cs` ? Update version string

## Code Signing (Production)

For production deployment, sign executables:

```powershell
# Sign with code signing certificate
signtool sign /f "certificate.pfx" /p "password" /t "http://timestamp.server.com" BackupUI.exe
signtool sign /f "certificate.pfx" /p "password" /t "http://timestamp.server.com" BackupService.exe
signtool sign /f "certificate.pfx" /p "password" /t "http://timestamp.server.com" BackupEngine.dll
```

## Continuous Integration (CI/CD)

### GitHub Actions Example
```yaml
name: Build and Release

on:
  push:
    tags:
      - 'v*'

jobs:
  build:
    runs-on: windows-latest
    
    steps:
    - uses: actions/checkout@v2
    
    - name: Setup MSBuild
      uses: microsoft/setup-msbuild@v1
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
      with:
        dotnet-version: '8.0.x'
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build Solution
      run: msbuild BackupRestoreSolution.sln /p:Configuration=Release /p:Platform=x64
    
    - name: Create Release Package
      run: |
        Compress-Archive -Path bin\Release\* -DestinationPath BackupRestore-Release.zip
    
    - name: Upload Release
      uses: actions/upload-artifact@v2
      with:
        name: BackupRestore-Release
        path: BackupRestore-Release.zip
```

## Support and Maintenance

### Log Files
- **Service logs**: `C:\ProgramData\BackupRestoreService\service.log`
- **Job definitions**: `C:\ProgramData\BackupRestoreService\jobs.json`

### Diagnostic Information
To collect diagnostic info for support:
1. Service log file
2. Windows Event Viewer ? Application logs
3. VSS writer status: `vssadmin list writers`
4. Service status: `sc query BackupRestoreService`

## Security Considerations

### Required Permissions
- **Administrator**: Required for VSS, disk access, service installation
- **Hyper-V Administrator**: Required for Hyper-V operations
- **Backup Operators**: Alternative to full admin for backup operations

### Firewall Rules
No inbound firewall rules needed (all operations are local).

### Antivirus Exclusions
May need to exclude from antivirus:
- `C:\Program Files\BackupRestore\`
- Backup destination folders
- Recovery USB creation operations
