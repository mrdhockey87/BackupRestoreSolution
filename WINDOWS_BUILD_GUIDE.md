# Windows Build Guide

## ? **Building Linux Restore USB on Windows - Complete Guide**

The bash scripts (`.sh`) don't work in PowerShell or WSL directly from Windows. I've created **PowerShell versions** that work perfectly on Windows!

---

## Quick Start (3 Steps)

### **Requirements:**
- Windows 10/11
- WSL (Windows Subsystem for Linux)
- 2GB+ free space

### **Installation:**

```powershell
# Step 0: Install WSL (if not already installed)
# Open PowerShell as Administrator:
wsl --install
# Restart computer, then continue

# Step 1: Build applications (uses WSL)
.\LinuxRestore\build.ps1

# Step 2: Download GUI dependencies
.\LinuxRestore\download-gui-deps.ps1

# Step 3: Create bootable ISO
.\LinuxRestore\create-bootable-iso.ps1

# Step 4: Write ISO to USB using Rufus
# Download Rufus from https://rufus.ie
# Select ISO, select USB, click START
```

**Done!** You have a bootable USB in 4 steps!

---

## Files Created

### **PowerShell Scripts (Work on Windows):**

| File | Purpose | Works In |
|------|---------|----------|
| `build.ps1` | Build C++ apps using WSL | ? PowerShell |
| `download-gui-deps.ps1` | Download GTK+ packages | ? PowerShell |
| `create-bootable-iso.ps1` | Create ISO file | ? PowerShell |

### **Bash Scripts (For Linux only):**

| File | Purpose | Works In |
|------|---------|----------|
| `build.sh` | Build apps natively | ? PowerShell, ? Linux |
| `download_gui_dependencies.sh` | Download packages | ? PowerShell, ? Linux |
| `create_bootable_usb_gui.sh` | Create USB directly | ? PowerShell, ? Linux |

**Use `.ps1` files on Windows, `.sh` files on Linux!**

---

## Detailed Instructions

### **Install WSL**

```powershell
# Open PowerShell as Administrator
wsl --install

# This installs Ubuntu by default
# Restart your computer when prompted

# After restart, open Ubuntu from Start Menu
# Set up username and password

# Verify installation
wsl --version
```

### **Build Applications**

```powershell
# Open PowerShell (regular, not admin)
cd E:\VisualStudioProjects\BackupRestoreSolution\BackupRestoreSolution

# Run build script
.\LinuxRestore\build.ps1
```

**Output:**
```
=========================================
Building Backup & Restore for Linux
Version 4.6.0
=========================================

? WSL detected
Installing build dependencies in WSL...
? Dependencies installed

Building in WSL...
? Build successful

Copying binaries...
? Binaries copied to dist/

Built applications:
  - restore_gui (2.1 MB)
  - restore_tui (1.2 MB)
  - restore_cli (0.5 MB)

=========================================
Build Complete!
=========================================
```

### **Download GUI Dependencies**

```powershell
.\LinuxRestore\download-gui-deps.ps1
```

**Output:**
```
=========================================
Downloading GTK+ GUI Dependencies
=========================================

Step 1: Downloading package indices...
? Package indices downloaded

Step 2: Downloading GTK+ packages...
This will download ~150-200 MB...

  ? xorg-server
  ? gtk+3.0
  ? jwm
  ... (15 packages)

Downloaded 15 packages

Step 3: Creating bundle archive...
? Bundle created using Windows tar

=========================================
GUI Dependencies Downloaded!
=========================================

Bundle: LinuxRestore\gtk_gui_bundle.tar.gz
Size: 185.5 MB
```

### **Create Bootable ISO**

```powershell
.\LinuxRestore\create-bootable-iso.ps1
```

**Output:**
```
=========================================
Creating Bootable ISO
=========================================

Step 1: Checking for WSL...
? WSL available

Step 2: Downloading Alpine Linux...
Downloading ~800 MB, please wait...
? Alpine Linux downloaded

Step 3: Creating custom ISO...
Creating ISO image...
ISO created successfully!

=========================================
ISO Created Successfully!
=========================================

ISO File: LinuxRestore\BackupRestore_Recovery.iso
Size: 1.2 GB

You can now:
1. Burn to DVD
2. Write to USB with Rufus:
   - Download Rufus: https://rufus.ie
   - Select the ISO
   - Select USB drive
   - Click Start
3. Boot in VM (VirtualBox, VMware, Hyper-V)
```

### **Write ISO to USB (Rufus)**

1. **Download Rufus:** https://rufus.ie
2. **Insert USB drive** (8GB+)
3. **Open Rufus**
4. **Device:** Select your USB drive
5. **Boot selection:** Click SELECT, choose `BackupRestore_Recovery.iso`
6. **Partition scheme:** GPT or MBR (GPT for UEFI, MBR for BIOS)
7. **Click START**
8. **Wait ~5 minutes**
9. **Done!** USB is bootable

---

## Troubleshooting

### **"wsl: command not found"**

WSL is not installed.

```powershell
# Install WSL
wsl --install

# Restart computer

# Verify
wsl --version
```

### **"Build failed"**

Try manual build:

```powershell
# Open WSL
wsl

# Navigate to project
cd /mnt/e/VisualStudioProjects/BackupRestoreSolution/BackupRestoreSolution/LinuxRestore

# Install dependencies
sudo apt-get update
sudo apt-get install build-essential cmake libgtk-3-dev libncurses5-dev

# Build manually
mkdir build && cd build
cmake ..
make -j$(nproc)

# Copy files
cp restore_* ../dist/
```

### **"tar: command not found"**

Windows 10+ has tar. If missing:

```powershell
# Check if tar exists
tar --version

# If not, use WSL
wsl tar --version
```

### **ISO too large**

The ISO is ~1.2 GB because it includes:
- Alpine Linux (~800 MB)
- GTK+ GUI (~200 MB)
- Restore apps (~3 MB)

This is normal. Use 2GB+ USB drive.

### **Rufus says "ISO is not bootable"**

Make sure you're using the ISO created by `create-bootable-iso.ps1`, not the original Alpine ISO.

---

## Alternative: Without WSL

If you can't use WSL, you can:

1. **Use a Linux VM**
   - Install VirtualBox
   - Create Ubuntu VM
   - Copy files to VM
   - Run `.sh` scripts in VM

2. **Use Native Linux**
   - Boot Linux from USB
   - Run `.sh` scripts directly

3. **Ask someone with Linux**
   - Send them the files
   - They run the scripts
   - Send you back the ISO

---

## File Structure

After running all scripts:

```
LinuxRestore/
??? build.ps1                          ? PowerShell build script
??? download-gui-deps.ps1              ? PowerShell download script
??? create-bootable-iso.ps1            ? PowerShell ISO creator
?
??? build.sh                           (Linux only)
??? download_gui_dependencies.sh       (Linux only)
??? create_bootable_usb_gui.sh         (Linux only)
?
??? dist/
?   ??? restore_gui                    ? Built binary (2 MB)
?   ??? restore_tui                    ? Built binary (1 MB)
?   ??? restore_cli                    ? Built binary (500 KB)
?
??? gtk_gui_bundle.tar.gz              ? Downloaded (200 MB)
??? alpine-extended-3.19.0-x86_64.iso  ? Downloaded (800 MB)
??? BackupRestore_Recovery.iso         ? Final ISO (1.2 GB)
```

---

## Summary

**For Windows Users:**
- ? Use `.ps1` PowerShell scripts
- ? Requires WSL (easy to install)
- ? Creates ISO file
- ? Use Rufus to write to USB

**For Linux Users:**
- ? Use `.sh` bash scripts
- ? Creates USB directly
- ? No intermediate ISO needed

**Both methods produce the same bootable media!**

---

## Next Steps

After creating bootable USB:

1. **Test in VM first**
2. **Boot from USB**
3. **Select "Graphical Interface"**
4. **Test restore**
5. **Deploy to users**

---

**You can now build everything on Windows!** ??
