# Backup & Restore Solution

Enterprise-grade backup solution with:
- C++ backup/restore engine (protected IP)
- C# WPF UI
- Automated scheduling
- Disaster recovery (bootable USB + WinRE)

## Build

```
msbuild BackupRestoreSolution.sln /p:Configuration=Release /p:Platform=x64
```

## Run

```
bin\Release\BackupUI.exe (as Administrator)
```

## TODO

Copy complete implementations from conversation into:
- BackupEngine/*.cpp files
- BackupUI/Windows/*.xaml files
- BackupUI/Interop/*.cs files
- BackupService/*/*.cs files
- RecoveryUI/*.cs and *.xaml files
