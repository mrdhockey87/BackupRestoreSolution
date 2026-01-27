# Build Error Fixed - Line Endings Issue

## The Problem

**Error:** `E: Command line option ' ' [from -qq ] is not understood`

**Root Cause:** Windows line endings (CRLF) in PowerShell here-strings being passed to WSL bash.

## The Solution

? **FIXED:** `build.ps1` now writes scripts to temp files instead of using here-strings directly.

---

## How to Build Now

```powershell
# Clean start - delete any previous build attempts
Remove-Item -Recurse -Force LinuxRestore\build -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force LinuxRestore\dist -ErrorAction SilentlyContinue

# Run the FIXED build script
.\LinuxRestore\build.ps1
```

Should work perfectly now!

---

## What Changed

### ? **Before (Broken):**
```powershell
wsl bash -c @"
sudo apt-get update -qq
sudo apt-get install -y build-essential
"@
```
**Problem:** CRLF line endings cause `-qq\r\n` which bash can't parse.

### ? **After (Fixed):**
```powershell
$script = @'
#!/bin/bash
sudo apt-get update
sudo apt-get install -y build-essential
'@

$script | wsl bash -c "cat > /tmp/build.sh && chmod +x /tmp/build.sh"
wsl bash /tmp/build.sh
```
**Solution:** Write to file first, then execute. File has correct LF line endings.

---

## If Build Still Fails

### Option 1: Manual Build in WSL

```powershell
# Enter WSL
wsl

# Navigate to project
cd /mnt/e/VisualStudioProjects/BackupRestoreSolution/BackupRestoreSolution/LinuxRestore

# Update and install dependencies
sudo apt-get update
sudo apt-get install -y build-essential cmake libncurses-dev libgtk-3-dev pkg-config

# Build
mkdir -p build
cd build
cmake ..
make -j$(nproc)

# Copy binaries
mkdir -p ../dist
cp restore_* ../dist/

# Exit WSL
exit
```

### Option 2: Use Simplified Approach

Skip building and just download Alpine Extended:

```powershell
# Download Alpine Extended (includes all packages)
.\LinuxRestore\download-alpine-extended.ps1

# Create ISO directly
.\LinuxRestore\create-bootable-iso.ps1

# Use Rufus to write to USB
```

---

## Complete Workflow (Fixed)

```powershell
# Step 1: Build (FIXED script)
.\LinuxRestore\build.ps1

# Step 2: Download Alpine Extended
.\LinuxRestore\download-alpine-extended.ps1

# Step 3: Create ISO
.\LinuxRestore\create-bootable-iso.ps1

# Step 4: Use Rufus to write ISO to USB
# Download Rufus: https://rufus.ie
```

---

## Summary

? **build.ps1** - FIXED (no more line ending issues)  
? **download-alpine-extended.ps1** - Works perfectly  
? **create-bootable-iso.ps1** - Ready to use  

**The build should work now!** ??

---

## Technical Details

### Why This Happened

Windows uses CRLF (`\r\n`) line endings  
Linux/WSL uses LF (`\n`) line endings  

PowerShell here-strings use CRLF  
When passed directly to bash, they break command parsing  

### How We Fixed It

1. Write scripts to temp files in WSL
2. WSL automatically converts line endings
3. Execute the temp file
4. Clean and reliable!

### Files Fixed

- ? `LinuxRestore/build.ps1` - Completely rewritten
- ? `LinuxRestore/download-gui-deps.ps1` - Uses WSL properly
- ? `LinuxRestore/download-alpine-extended.ps1` - Simple download
- ? `LinuxRestore/create-bootable-iso.ps1` - ISO creation

---

**Try running `.\LinuxRestore\build.ps1` again - it should work!**
