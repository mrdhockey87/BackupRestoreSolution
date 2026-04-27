# BUILD-AND-CREATE-ISO.ps1 - Version 4.7.1.0
# Builds updated Linux restore applications and creates bootable recovery ISO
# Updated: Includes new 3-step wizard restore tools

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Building Bootable Linux ISO v4.7.1.0" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

$restoreTuiBinary = "SecureServerBackupLinuxRestore"
$restoreCliBinary = "SecureServerBackupLinuxRestoreCli"
$restoreGuiBinary = "SecureServerBackupLinuxRestoreGui"
$recoveryIsoName = "SecureServerBackupRestore_Recovery.iso"

if (-not (Test-Path "CMakeLists.txt")) {
    Write-Host "ERROR: Run from LinuxRestore directory!" -ForegroundColor Red
    exit 1
}

# Test sudo once
Write-Host "Testing sudo..." -ForegroundColor Yellow
wsl bash -c "sudo -v"
Write-Host "? Sudo OK" -ForegroundColor Green
Write-Host ""

# Get WSL path
$currentLoc = (Get-Location).Path
$driveLetter = $currentLoc.Substring(0, 1).ToLower()
$restOfPath = $currentLoc.Substring(2).Replace('\', '/')
$wslPath = "/mnt/$driveLetter$restOfPath"

# PART 1: BUILD
Write-Host "Part 1: Building..." -ForegroundColor Cyan
wsl bash -c "cd '$wslPath' && sudo apt-get install -y build-essential cmake libncurses-dev rsync genisoimage 2>&1 | grep -v Reading"
wsl bash -c "cd '$wslPath' && rm -rf build && mkdir build && cd build && cmake .. && make -j4"

# Check if build succeeded
if ($LASTEXITCODE -ne 0) {
    Write-Host "" -ForegroundColor Red
    Write-Host "? BUILD FAILED!" -ForegroundColor Red
    Write-Host "Check compilation errors above." -ForegroundColor Yellow
    Write-Host "" -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Path "dist" -Force | Out-Null
wsl bash -c "cd '$wslPath/build' && cp SecureServerBackupLinuxRestore* ../dist/ 2>/dev/null"

if (-not (Test-Path (Join-Path "dist" $restoreTuiBinary))) {
    Write-Host "" -ForegroundColor Red
    Write-Host "? Build failed - $restoreTuiBinary not found!" -ForegroundColor Red
    Write-Host "Compilation succeeded but binaries not created." -ForegroundColor Yellow
    Write-Host "" -ForegroundColor Red
    exit 1
}
Write-Host "? Build complete" -ForegroundColor Green

# PART 2: DOWNLOAD ALPINE
Write-Host ""
Write-Host "Part 2: Downloading Alpine..." -ForegroundColor Cyan
$ALPINE_ISO = "alpine-extended-3.19.0-x86_64.iso"
if (-not (Test-Path $ALPINE_ISO)) {
    Invoke-WebRequest -Uri "https://dl-cdn.alpinelinux.org/alpine/v3.19/releases/x86_64/$ALPINE_ISO" -OutFile $ALPINE_ISO -UseBasicParsing
}
Write-Host "? Alpine ready" -ForegroundColor Green

# PART 3: CREATE ISO
Write-Host ""
Write-Host "Part 3: Creating ISO..." -ForegroundColor Cyan

# Clean
wsl bash -c "cd '$wslPath' && rm -rf iso_build && rm -f '$recoveryIsoName'"

# Extract Alpine
Write-Host "Extracting Alpine ISO..." -ForegroundColor Gray
wsl bash -c "cd '$wslPath' && mkdir -p iso_build/mnt && sudo mount -o loop '$ALPINE_ISO' iso_build/mnt && rsync -a iso_build/mnt/ iso_build/ && sudo umount iso_build/mnt && rmdir iso_build/mnt"

# FIX: Make all files writable (they're read-only from the ISO)
Write-Host "Setting permissions..." -ForegroundColor Gray
wsl bash -c "cd '$wslPath/iso_build' && chmod -R u+w ."

# Add restore apps
Write-Host "Adding restore apps..." -ForegroundColor Gray
wsl bash -c "cd '$wslPath/iso_build' && mkdir restore && cp ../dist/SecureServerBackupLinuxRestore* restore/ && chmod +x restore/*"

# Create startup script
Write-Host "Creating scripts..." -ForegroundColor Gray
wsl bash -c "cd '$wslPath/iso_build' && echo '#!/bin/sh' > restore/start.sh && echo 'apk add ntfs-3g --no-cache' >> restore/start.sh && echo 'cd /media/cdrom/restore' >> restore/start.sh && echo './$restoreTuiBinary || ./$restoreCliBinary' >> restore/start.sh && echo 'poweroff' >> restore/start.sh && chmod +x restore/start.sh"

# Configure boot
wsl bash -c "cd '$wslPath/iso_build' && echo 'DEFAULT restore' > boot/syslinux/syslinux.cfg && echo 'TIMEOUT 50' >> boot/syslinux/syslinux.cfg && echo 'LABEL restore' >> boot/syslinux/syslinux.cfg && echo '  KERNEL /boot/vmlinuz-lts' >> boot/syslinux/syslinux.cfg && echo '  INITRD /boot/initramfs-lts' >> boot/syslinux/syslinux.cfg && echo '  APPEND root=/dev/sr0 quiet' >> boot/syslinux/syslinux.cfg"

# Autostart
wsl bash -c "cd '$wslPath/iso_build' && mkdir -p etc/local.d && echo '#!/bin/sh' > etc/local.d/restore.start && echo 'sleep 2' >> etc/local.d/restore.start && echo '/media/cdrom/restore/start.sh' >> etc/local.d/restore.start && chmod +x etc/local.d/restore.start"

# Build ISO
Write-Host "Building ISO (1-2 minutes)..." -ForegroundColor Yellow
wsl bash -c "cd '$wslPath/iso_build' && genisoimage -o ../$recoveryIsoName -b boot/syslinux/isolinux.bin -c boot/syslinux/boot.cat -no-emul-boot -boot-load-size 4 -boot-info-table -J -R -V RESTORE . 2>&1 | tail -5"

# Cleanup
wsl bash -c "cd '$wslPath' && rm -rf iso_build"

# Verify
if (Test-Path $recoveryIsoName) {
    $size = [math]::Round((Get-Item $recoveryIsoName).Length / 1MB, 0)
    
    if ($size -gt 100) {
        Write-Host ""
        Write-Host "=========================================" -ForegroundColor Cyan
        Write-Host "SUCCESS!" -ForegroundColor Green
        Write-Host "=========================================" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "ISO: $recoveryIsoName" -ForegroundColor White
        Write-Host "Size: $size MB" -ForegroundColor White
        Write-Host ""
        
        # Cleanup temporary files
        Write-Host "Cleaning up temporary files..." -ForegroundColor Yellow
        
        # Delete build directory
        if (Test-Path "build") {
            Remove-Item -Recurse -Force "build"
            Write-Host "  ? Removed build/" -ForegroundColor Gray
        }
        
        # Ask about Alpine ISO (it's large - 800MB)
        if (Test-Path $ALPINE_ISO) {
            $keepAlpine = Read-Host "Keep Alpine ISO ($ALPINE_ISO - 800MB)? (y/n)"
            if ($keepAlpine -eq "n") {
                Remove-Item $ALPINE_ISO
                Write-Host "  ? Removed $ALPINE_ISO" -ForegroundColor Gray
            } else {
                Write-Host "  ? Kept $ALPINE_ISO" -ForegroundColor Gray
            }
        }
        
        Write-Host ""
        Write-Host "Final files:" -ForegroundColor Cyan
        Write-Host "  ? dist\$restoreTuiBinary" -ForegroundColor Green
        Write-Host "  ? dist\$restoreCliBinary" -ForegroundColor Green
        Write-Host "  ? dist\$restoreGuiBinary" -ForegroundColor Green
        Write-Host "  ? $recoveryIsoName ($size MB)" -ForegroundColor Green
        Write-Host ""
        Write-Host "Use Rufus to write to USB: https://rufus.ie" -ForegroundColor Yellow
        Write-Host ""
    } else {
        Write-Host ""
        Write-Host "? ISO created but is too small ($size MB)" -ForegroundColor Red
        Write-Host "This indicates the ISO creation failed" -ForegroundColor Yellow
    }
} else {
    Write-Host ""
    Write-Host "? ISO creation failed - file not found" -ForegroundColor Red
}
