// LinuxRestore/restore_cli.cpp
// Simple command-line interface for Linux restore

#include <iostream>
#include <string>
#include <vector>
#include "restore_engine.cpp"

void printHeader() {
    std::cout << "\n";
    std::cout << "========================================\n";
    std::cout << " Backup & Restore - Linux Recovery CLI\n";
    std::cout << " Version 4.6.0\n";
    std::cout << "========================================\n";
    std::cout << "\n";
}

void printMenu() {
    std::cout << "\nMain Menu:\n";
    std::cout << "1. List available disks/partitions\n";
    std::cout << "2. Mount NTFS partition\n";
    std::cout << "3. Scan for backups\n";
    std::cout << "4. Restore backup\n";
    std::cout << "5. Unmount partition\n";
    std::cout << "6. Exit\n";
    std::cout << "\nSelect option: ";
}

void listDisks(RestoreEngine& engine) {
    std::cout << "\nScanning for disks and partitions...\n\n";
    auto disks = engine.ListDisks();
    
    if (disks.empty()) {
        std::cout << "No disks found!\n";
        return;
    }

    std::cout << "Available disks and partitions:\n";
    std::cout << "================================\n";
    for (const auto& disk : disks) {
        std::cout << disk;
    }
    std::cout << "\nTip: Use 'fdisk -l' or 'lsblk' for more details\n";
}

void mountPartition(RestoreEngine& engine) {
    std::string device, mountPoint;
    
    std::cout << "\nMount NTFS Partition\n";
    std::cout << "====================\n";
    std::cout << "Enter device (e.g., /dev/sda1): ";
    std::getline(std::cin, device);
    
    std::cout << "Enter mount point (default: /mnt/restore): ";
    std::getline(std::cin, mountPoint);
    
    if (mountPoint.empty()) {
        mountPoint = "/mnt/restore";
    }

    std::cout << "\nMounting " << device << " to " << mountPoint << "...\n";
    
    int result = engine.MountNTFSPartition(device, mountPoint);
    
    if (result == 0) {
        std::cout << "? Mounted successfully!\n";
        std::cout << "You can now access files at: " << mountPoint << "\n";
    } else {
        std::cout << "? Mount failed: " << engine.GetLastError() << "\n";
        std::cout << "\nTroubleshooting:\n";
        std::cout << "  - Make sure ntfs-3g is installed: apk add ntfs-3g\n";
        std::cout << "  - Check device name is correct: lsblk\n";
        std::cout << "  - Run as root: sudo " << "\n";
    }
}

void scanBackups(RestoreEngine& engine) {
    std::cout << "\nScanning for backups...\n";
    std::cout << "Searching in: /media, /mnt, /run/media\n\n";
    
    std::vector<std::string> searchPaths = {"/media", "/mnt", "/run/media"};
    std::vector<std::string> allBackups;
    
    for (const auto& path : searchPaths) {
        auto backups = engine.ScanForBackups(path);
        allBackups.insert(allBackups.end(), backups.begin(), backups.end());
    }
    
    if (allBackups.empty()) {
        std::cout << "No backups found.\n";
        std::cout << "\nTips:\n";
        std::cout << "  - Mount your backup media first\n";
        std::cout << "  - Backup files should contain 'backup' or '.bak' in the name\n";
        return;
    }
    
    std::cout << "Found " << allBackups.size() << " backup(s):\n";
    std::cout << "==============================\n";
    for (size_t i = 0; i < allBackups.size(); i++) {
        std::cout << (i + 1) << ". " << allBackups[i] << "\n";
    }
}

void restoreBackup(RestoreEngine& engine) {
    std::string backupPath, destPath;
    char overwrite;
    
    std::cout << "\nRestore Backup\n";
    std::cout << "==============\n";
    std::cout << "Enter backup path: ";
    std::getline(std::cin, backupPath);
    
    std::cout << "Enter destination path: ";
    std::getline(std::cin, destPath);
    
    std::cout << "Overwrite existing files? (y/n): ";
    std::cin >> overwrite;
    std::cin.ignore();
    
    std::cout << "\nRestore Summary:\n";
    std::cout << "  From: " << backupPath << "\n";
    std::cout << "  To:   " << destPath << "\n";
    std::cout << "  Overwrite: " << (overwrite == 'y' ? "Yes" : "No") << "\n";
    std::cout << "\nWARNING: This will modify files on the destination!\n";
    std::cout << "Continue? (yes/no): ";
    
    std::string confirm;
    std::getline(std::cin, confirm);
    
    if (confirm != "yes") {
        std::cout << "Restore cancelled.\n";
        return;
    }
    
    std::cout << "\nStarting restore...\n";
    
    int result = engine.RestoreFiles(backupPath, destPath, overwrite == 'y');
    
    if (result == 0) {
        std::cout << "\n? Restore completed successfully!\n";
    } else {
        std::cout << "\n? Restore failed: " << engine.GetLastError() << "\n";
    }
}

void unmountPartition(RestoreEngine& engine) {
    std::string mountPoint;
    
    std::cout << "\nUnmount Partition\n";
    std::cout << "=================\n";
    std::cout << "Enter mount point to unmount: ";
    std::getline(std::cin, mountPoint);
    
    std::cout << "Unmounting " << mountPoint << "...\n";
    
    int result = engine.UnmountPartition(mountPoint);
    
    if (result == 0) {
        std::cout << "? Unmounted successfully!\n";
    } else {
        std::cout << "? Unmount failed\n";
        std::cout << "Try: sudo umount " << mountPoint << "\n";
    }
}

int main(int argc, char* argv[]) {
    // Check if running as root
    if (geteuid() != 0) {
        std::cerr << "ERROR: This program must be run as root\n";
        std::cerr << "Use: sudo " << argv[0] << "\n";
        return 1;
    }

    printHeader();
    
    RestoreEngine engine;
    
    // Command-line mode
    if (argc > 1) {
        if (std::string(argv[1]) == "--restore" && argc >= 4) {
            std::string backupPath = argv[2];
            std::string destPath = argv[3];
            bool overwrite = (argc > 4 && std::string(argv[4]) == "--overwrite");
            
            std::cout << "Restoring from: " << backupPath << "\n";
            std::cout << "            to: " << destPath << "\n\n";
            
            int result = engine.RestoreFiles(backupPath, destPath, overwrite);
            
            return (result == 0) ? 0 : 1;
        } else if (std::string(argv[1]) == "--help") {
            std::cout << "Usage:\n";
            std::cout << "  Interactive mode: sudo " << argv[0] << "\n";
            std::cout << "  Direct restore:   sudo " << argv[0] << " --restore <backup> <dest> [--overwrite]\n";
            std::cout << "\n";
            std::cout << "Examples:\n";
            std::cout << "  sudo " << argv[0] << " --restore /media/usb/backup /mnt/restore\n";
            std::cout << "  sudo " << argv[0] << " --restore /mnt/backup /mnt/c --overwrite\n";
            return 0;
        }
    }
    
    // Interactive mode
    while (true) {
        printMenu();
        
        int choice;
        std::cin >> choice;
        std::cin.ignore();
        
        switch (choice) {
            case 1:
                listDisks(engine);
                break;
            case 2:
                mountPartition(engine);
                break;
            case 3:
                scanBackups(engine);
                break;
            case 4:
                restoreBackup(engine);
                break;
            case 5:
                unmountPartition(engine);
                break;
            case 6:
                std::cout << "\nGoodbye!\n";
                return 0;
            default:
                std::cout << "Invalid option. Please try again.\n";
        }
        
        std::cout << "\nPress Enter to continue...";
        std::cin.ignore();
    }
    
    return 0;
}
