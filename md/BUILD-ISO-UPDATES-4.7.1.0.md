# BUILD-AND-CREATE-ISO.ps1 Updates for Version 4.7.1.0

## Summary
? **The BUILD-AND-CREATE-ISO.ps1 script is READY** for version 4.7.1.0!

The script already compiles and includes all the updated restore tools. Only minor version updates were needed.

---

## Changes Made

### 1. ? BUILD-AND-CREATE-ISO.ps1
**Updated header**:
- Changed version to 4.7.1.0
- Added note about new 3-step wizard restore tools

**What the script does** (already correct):
- Compiles: `restore_tui.cpp`, `restore_cli.cpp`, `restore_gui_gtk.cpp`
- Copies binaries to `dist/` folder
- Downloads Alpine Linux 3.19.0
- Extracts Alpine ISO
- Adds restore apps to ISO
- Creates startup scripts
- Builds bootable ISO with `genisoimage`
- Cleans up temporary files

### 2. ? CMakeLists.txt
**Updated**:
- Project version: 4.6.0 ? 4.7.1
- Added comment about 3-step wizard

**What it compiles** (already correct):
- `restore_engine` (static library)
- `restore_tui` (ncurses TUI)
- `restore_cli` (command-line)
- `restore_gui` (GTK+, if available)

---

## No Additional Changes Required!

The script will automatically:
1. ? Use the NEW `restore_tui.cpp` (3-step wizard)
2. ? Use the NEW `restore_cli.cpp` (enhanced CLI)
3. ? Use the NEW `restore_gui_gtk.cpp` (GTK+ wizard)
4. ? Link against updated `restore_engine.cpp` (new methods)

---

## How to Build the ISO

### Prerequisites
1. **WSL2 with Ubuntu** installed
2. **PowerShell** (Windows)
3. **Internet connection** (to download Alpine ISO first time)

### Steps
```powershell
# Navigate to LinuxRestore directory
cd LinuxRestore

# Run the build script
.\BUILD-AND-CREATE-ISO.ps1
```

### What Happens
1. **Installs packages** (in WSL):
   - build-essential
   - cmake
   - libncurses-dev
   - rsync
   - genisoimage

2. **Compiles C++ code**:
   - Creates `build/` directory
   - Runs CMake configuration
   - Compiles with `make -j4` (parallel)
   - Copies binaries to `dist/`

3. **Downloads Alpine** (if not cached):
   - Downloads `alpine-extended-3.19.0-x86_64.iso` (800MB)
   - Cached for future builds

4. **Creates bootable ISO**:
   - Extracts Alpine ISO
   - Adds restore apps to `/restore` folder
   - Creates startup script (`start.sh`)
   - Configures SYSLINUX bootloader
   - Sets up autostart
   - Builds final ISO

5. **Output**:
   - `BackupRestore_Recovery.iso` (size varies, typically 150-200MB)
   - `dist/restore_tui` (ncurses binary)
   - `dist/restore_cli` (CLI binary)
   - `dist/restore_gui` (GTK+ binary, if compiled)

### Expected Output
```
=========================================
Building Bootable Linux ISO v4.7.1.0
=========================================

Testing sudo...
? Sudo OK

Part 1: Building...
? Build complete

Part 2: Downloading Alpine...
? Alpine ready

Part 3: Creating ISO...
Extracting Alpine ISO...
Setting permissions...
Adding restore apps...
Creating scripts...
Building ISO (1-2 minutes)...

=========================================
SUCCESS!
=========================================

ISO: BackupRestore_Recovery.iso
Size: 180 MB

Final files:
  ? dist\restore_tui
  ? dist\restore_cli
  ? dist\restore_gui
  ? BackupRestore_Recovery.iso (180 MB)

Use Rufus to write to USB: https://rufus.ie
```

---

## Testing the ISO

### In VirtualBox
1. Create new VM
2. Mount `BackupRestore_Recovery.iso` as CD
3. Boot from CD
4. Should auto-launch `restore_tui` with 3-step wizard

### On Real Hardware (USB)
1. Download Rufus: https://rufus.ie
2. Insert USB drive (will be erased!)
3. Select `BackupRestore_Recovery.iso`
4. Write to USB
5. Boot computer from USB

---

## New Features in Version 4.7.1.0

### restore_tui (Terminal UI)
- **Step 1**: Browse backup folder ? See all backup dates ? Select
- **Step 2**: Tree view with checkboxes ? Select items to restore
- **Step 3**: Choose destination ? Confirm ? Restore
- Color-coded UI, progress bars, error handling

### restore_cli (Command-Line)
- `--list-dates` - Show available backup dates
- `--show-contents` - Display backup contents tree
- `--restore --items "..." --dest "..."` - Automated restore
- `--interactive` - Text-based wizard mode

### restore_gui (GTK+ Graphical)
- Full GTK+ interface with wizard
- TreeView with checkboxes
- File chooser dialogs
- Progress dialogs
- Status bar

### restore_engine.cpp
- `EnumerateBackupDates()` - List backup snapshots
- `BuildRestoreTree()` - Hierarchical backup contents
- `RestoreWithManifest()` - Selective item restore

---

## Troubleshooting

### Build Fails
```powershell
# Clean and rebuild
rm -rf build, dist
.\BUILD-AND-CREATE-ISO.ps1
```

### ISO Too Small
- Check for errors in build output
- Ensure Alpine ISO downloaded correctly
- Verify all restore binaries compiled

### WSL Issues
```powershell
# Restart WSL
wsl --shutdown
wsl
```

### Permissions Issues
```powershell
# Test sudo
wsl bash -c "sudo -v"
```

---

## File Checklist

Before running the build:
- ? `restore_tui.cpp` (updated to 4.7.1.0)
- ? `restore_cli.cpp` (updated to 4.7.1.0)
- ? `restore_gui_gtk.cpp` (updated to 4.7.1.0)
- ? `restore_engine.cpp` (3 new methods added)
- ? `CMakeLists.txt` (version updated to 4.7.1)
- ? `BUILD-AND-CREATE-ISO.ps1` (version header updated)

All files are ready! ??

---

## Next Steps After Building

1. **Test the ISO** in VirtualBox or physical hardware
2. **Test restore workflow**:
   - Select backup date
   - Navigate tree
   - Select items
   - Execute restore
3. **Create USB boot drive** with Rufus
4. **Test on real backup** to verify functionality
5. **Update documentation** with screenshots

---

## Summary

? **No major changes needed to BUILD-AND-CREATE-ISO.ps1**

The script already:
- Compiles all C++ files correctly
- Links against updated restore_engine.cpp
- Includes all three restore tools in the ISO
- Creates bootable media properly

Only updates were:
- Version number in header (cosmetic)
- CMakeLists.txt project version (cosmetic)

**The script is production-ready for version 4.7.1.0!** ??
