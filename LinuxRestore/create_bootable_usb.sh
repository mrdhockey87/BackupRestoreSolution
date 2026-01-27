#!/bin/bash
# create_bootable_usb.sh - Create bootable Linux recovery USB

if [ "$EUID" -ne 0 ]; then
    echo "This script must be run as root (use sudo)"
    exit 1
fi

if [ -z "$1" ]; then
    echo "Usage: sudo $0 <device>"
    echo "Example: sudo $0 /dev/sdb"
    echo ""
    echo "Available devices:"
    lsblk -d -o NAME,SIZE,TYPE,TRAN | grep usb
    exit 1
fi

DEVICE=$1
USB_LABEL="RESTORE_USB"
MOUNT_POINT="/tmp/restore_usb_$$"

echo "====================================="
echo "Creating Bootable Recovery USB"
echo "====================================="
echo "Target device: $DEVICE"
echo "WARNING: All data on $DEVICE will be ERASED!"
echo ""
read -p "Continue? (yes/no): " confirm

if [ "$confirm" != "yes" ]; then
    echo "Cancelled"
    exit 0
fi

echo ""
echo "Step 1: Unmounting device..."
umount ${DEVICE}* 2>/dev/null || true

echo "Step 2: Creating partition..."
parted -s $DEVICE mklabel msdos
parted -s $DEVICE mkpart primary fat32 1MiB 100%
parted -s $DEVICE set 1 boot on

# Determine partition name
if [[ $DEVICE == *"nvme"* ]] || [[ $DEVICE == *"mmcblk"* ]]; then
    PARTITION="${DEVICE}p1"
else
    PARTITION="${DEVICE}1"
fi

echo "Step 3: Formatting partition..."
mkfs.vfat -F 32 -n $USB_LABEL $PARTITION

echo "Step 4: Downloading Alpine Linux (minimal)..."
ALPINE_VERSION="3.19"
ALPINE_ISO="alpine-standard-${ALPINE_VERSION}.0-x86_64.iso"
ALPINE_URL="https://dl-cdn.alpinelinux.org/alpine/v${ALPINE_VERSION}/releases/x86_64/${ALPINE_ISO}"

if [ ! -f "/tmp/${ALPINE_ISO}" ]; then
    wget -O "/tmp/${ALPINE_ISO}" "$ALPINE_URL"
fi

echo "Step 5: Mounting USB..."
mkdir -p $MOUNT_POINT
mount $PARTITION $MOUNT_POINT

echo "Step 6: Extracting Alpine Linux..."
cd /tmp
mkdir -p alpine_extract
mount -o loop $ALPINE_ISO alpine_extract
cp -r alpine_extract/* $MOUNT_POINT/
umount alpine_extract
rmdir alpine_extract

echo "Step 7: Installing SYSLINUX bootloader..."
syslinux --install $PARTITION
dd if=/usr/lib/syslinux/mbr/mbr.bin of=$DEVICE bs=440 count=1 conv=notrunc

echo "Step 8: Copying restore application..."
mkdir -p $MOUNT_POINT/restore
cp dist/restore_tui $MOUNT_POINT/restore/
cp dist/restore_cli $MOUNT_POINT/restore/
chmod +x $MOUNT_POINT/restore/*

echo "Step 9: Creating autostart script..."
cat > $MOUNT_POINT/restore/autostart.sh << 'EOF'
#!/bin/sh
# Autostart script for restore application

echo "======================================"
echo " Backup & Restore - Recovery Mode"
echo " Version 4.6.0"
echo "======================================"
echo ""
echo "Preparing system..."

# Load NTFS driver
modprobe fuse
apk add ntfs-3g ntfs-3g-progs --no-cache

# Start restore UI
cd /media/usb/restore
./restore_tui

echo ""
echo "Press any key to shutdown..."
read -n 1
poweroff
EOF

chmod +x $MOUNT_POINT/restore/autostart.sh

echo "Step 10: Configuring boot menu..."
cat > $MOUNT_POINT/boot/syslinux/syslinux.cfg << 'EOF'
DEFAULT restore
PROMPT 0
TIMEOUT 50

LABEL restore
  MENU LABEL Backup & Restore Recovery Mode
  KERNEL /boot/vmlinuz-lts
  INITRD /boot/initramfs-lts
  APPEND root=/dev/sda1 modules=loop,squashfs,sd-mod,usb-storage quiet nomodeset

LABEL shell
  MENU LABEL Boot to Shell
  KERNEL /boot/vmlinuz-lts
  INITRD /boot/initramfs-lts
  APPEND root=/dev/sda1 modules=loop,squashfs,sd-mod,usb-storage
EOF

echo "Step 11: Creating local startup script..."
mkdir -p $MOUNT_POINT/etc/local.d
cat > $MOUNT_POINT/etc/local.d/restore.start << 'EOF'
#!/bin/sh
# Auto-launch restore application

sleep 3
/media/usb/restore/autostart.sh
EOF

chmod +x $MOUNT_POINT/etc/local.d/restore.start

echo "Step 12: Syncing and unmounting..."
sync
umount $MOUNT_POINT
rmdir $MOUNT_POINT

echo ""
echo "====================================="
echo "Bootable USB created successfully!"
echo "====================================="
echo ""
echo "The USB drive is ready to use!"
echo ""
echo "To boot from this USB:"
echo "1. Insert the USB drive into the target computer"
echo "2. Boot from USB (change boot order in BIOS/UEFI)"
echo "3. The restore application will start automatically"
echo "4. Follow on-screen instructions to restore your backup"
echo ""
echo "Features:"
echo "  ? Boots directly to restore interface"
echo "  ? Supports NTFS (Windows) partitions"
echo "  ? No Windows ADK required"
echo "  ? Fully self-contained"
echo "  ? ~500 MB total size"
echo ""
