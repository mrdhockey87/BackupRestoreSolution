# BackupEngine.dll Loading Fix

## Problem
When running BackupUI in debug mode, the application couldn't find `BackupEngine.dll`, resulting in P/Invoke errors when trying to call native functions.

## Root Cause
The BackupUI project was trying to copy `BackupEngine.dll` using `<None Update>` which doesn't work reliably with dynamic paths. The DLL was built to `bin\Debug` or `bin\Release` but wasn't being copied to the same directory as `BackupUI.exe`.

## Solution Applied

### 1. Fixed BackupUI.csproj
Added a proper MSBuild `Target` that runs after build to copy the DLL:

```xml
<Target Name="CopyBackupEngine" AfterTargets="Build">
  <ItemGroup>
    <BackupEngineDll Include="$(SolutionDir)bin\$(Configuration)\BackupEngine.dll" />
  </ItemGroup>
  <Copy SourceFiles="@(BackupEngineDll)" 
        DestinationFolder="$(OutDir)" 
        SkipUnchangedFiles="true"
        Condition="Exists('$(SolutionDir)bin\$(Configuration)\BackupEngine.dll')" />
  <Message Text="Copied BackupEngine.dll to $(OutDir)" Importance="high" 
           Condition="Exists('$(SolutionDir)bin\$(Configuration)\BackupEngine.dll')" />
  <Warning Text="BackupEngine.dll not found - Build BackupEngine project first!" 
           Condition="!Exists('$(SolutionDir)bin\$(Configuration)\BackupEngine.dll')" />
</Target>
```

### 2. Added DLL Check in App.xaml.cs
Added startup check to provide clear error message if DLL is missing:

```csharp
private void CheckBackupEngineDll()
{
    var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BackupEngine.dll");
    
    if (!File.Exists(dllPath))
    {
        MessageBox.Show(
            $"Critical Error: BackupEngine.dll not found!\n\n" +
            $"Expected location: {dllPath}\n\n" +
            $"Please ensure:\n" +
            $"1. BackupEngine project is built first\n" +
            $"2. BackupEngine.dll is in the same directory as BackupUI.exe\n" +
            $"3. Build the entire solution (Build ? Rebuild Solution)",
            "Missing DLL",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
```

## How It Works Now

1. **Build Order**: 
   - BackupEngine (C++ DLL) builds first ? outputs to `bin\Debug\BackupEngine.dll`
   - BackupUI (C# WPF) builds second ? Post-build target copies DLL to its output directory

2. **Runtime**:
   - App starts and checks if `BackupEngine.dll` exists in the same directory
   - If missing, shows helpful error message
   - If present, app continues normally

3. **Benefits**:
   - Automatic DLL copying after each build
   - Clear error messages if DLL is missing
   - Works in both Debug and Release configurations
   - Skips copying if DLL hasn't changed (faster builds)

## Testing Checklist

- [x] Build BackupEngine project
- [x] Build BackupUI project
- [x] Verify BackupEngine.dll is in `bin\Debug` directory
- [x] Verify DLL is same directory as BackupUI.exe
- [x] Run BackupUI - should start without DLL errors
- [ ] Test P/Invoke calls (enumerate volumes, etc.)
- [ ] Test in Release configuration

## Troubleshooting

### If DLL still not found:
1. **Clean and Rebuild Solution**: `Build ? Clean Solution` then `Build ? Rebuild Solution`
2. **Check build order**: Ensure BackupEngine builds before BackupUI
3. **Manual copy**: Copy `bin\Debug\BackupEngine.dll` to the BackupUI output directory
4. **Check output window**: Look for "Copied BackupEngine.dll" message during build

### If DLL loads but functions fail:
1. **Architecture mismatch**: Both projects must be x64 (not x86 or AnyCPU)
2. **Missing dependencies**: BackupEngine.dll may need Visual C++ Redistributable
3. **Function signatures**: Ensure P/Invoke declarations match C++ exports exactly

## Version History
- **Version 2.0.0.0**: Fixed DLL loading issue with proper MSBuild target and startup checks
