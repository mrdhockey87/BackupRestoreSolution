// LinuxRestore/restore_cli.cpp
// Command-line interface for Linux restore - Version 6.2.5.31
// Enhanced with backup date selection, item selection, destination mapping, metadata-driven disk restore mapping,
// encrypted SSB backup prompts, and restore-target disk tree (matching Windows restore page)

#include <iostream>
#include <string>
#include <vector>
#include <sstream>
#include "restore_engine.cpp"

void printHeader() {
    std::cout << "\n";
    std::cout << "========================================\n";
    std::cout << " Backup & Restore - Linux Recovery CLI\n";
    std::cout << " Version 6.2.5.31\n";
    std::cout << "========================================\n";
    std::cout << "\n";
}

void ensureBackupPassword(RestoreEngine& engine, const std::string& backupPath) {
    std::ifstream file(backupPath, std::ios::binary);
    if (!file) {
        return;
    }

    char header[7] = { 0 };
    file.read(header, sizeof(header));
    if (file.gcount() == sizeof(header) && std::strncmp(header, "SSBAES1", sizeof(header)) == 0) {
        std::string password;
        std::cout << "Encrypted backup detected. Enter password: ";
        std::getline(std::cin, password);
        engine.SetBackupPassword(password);
    }
}

void printUsage() {
    std::cout << "Usage:\n";
    std::cout << "  restore_cli [options]\n\n";
    std::cout << "Options:\n";
    std::cout << "  --list-dates <path>           List available backup dates\n";
    std::cout << "  --show-contents <backup>      Show contents of specific backup\n";
    std::cout << "  --list-volumes <backup>       List volumes in a disk/volume backup\n";
    std::cout << "    --show-hidden               Include hidden partitions (EFI, MSR, Recovery)\n";
    std::cout << "  --restore <backup>            Start restore from backup file or backup folder\n";
    std::cout << "    --restore-point <index>     Select restore point index when --restore targets a folder\n";
    std::cout << "    --all                       Restore all items from the selected restore point\n";
    std::cout << "    --items <paths>             Comma-separated list of items\n";
    std::cout << "    --dest <path>               Destination (omit for original)\n";
    std::cout << "    --log-file <path>           Save restore activity to a plain-text log file\n";
    std::cout << "    --overwrite                 Overwrite existing files\n";
    std::cout << "  --interactive                 Interactive mode with menus\n";
    std::cout << "  --list-disks                  List available disks\n";
    std::cout << "  --list-target-disks           List disks available as restore targets (boot disk flagged)\n";
    std::cout << "  --mount <device> <path>       Mount NTFS partition\n";
    std::cout << "  --restore-disk <backup> <device>  Metadata-driven disk reconstruction restore\n";
    std::cout << "    --show-hidden               Include hidden partitions in the restore\n";
    std::cout << "  --unmount <path>              Unmount partition\n";
    std::cout << "  --help                        Show this help message\n";
    std::cout << "\nExamples:\n";
    std::cout << "  restore_cli --list-dates /media/backup\n";
    std::cout << "  restore_cli --list-volumes /media/backup/Full.ssb\n";
    std::cout << "  restore_cli --list-volumes /media/backup/Full.ssb --show-hidden\n";
    std::cout << "  restore_cli --show-contents /media/backup/Full_20260130\n";
    std::cout << "  restore_cli --restore /media/backup --restore-point 1 --all --dest /mnt/restore\n";
    std::cout << "  restore_cli --restore /media/backup/Full_20260130 --items \"FolderOne/File.txt,/home\" --dest /mnt/restore\n";
    std::cout << "  restore_cli --restore /media/backup/EncryptedBackup.ssb --all --dest /mnt/restore\n";
    std::cout << "  restore_cli --restore-disk /media/backup/Full.ssb /dev/sdb\n";
    std::cout << "  restore_cli --restore-disk /media/backup/Full.ssb /dev/sdb --show-hidden\n";
    std::cout << "  restore_cli --interactive\n\n";
}

void collectAllRestoreItems(const std::vector<RestoreEngine::RestoreItem>& items, std::vector<std::string>& selectedItems) {
    for (const auto& item : items) {
        selectedItems.push_back(item.path);
    }
}

bool resolveRestorePointPath(RestoreEngine& engine, const std::string& inputPath, int restorePointIndex, std::string& resolvedPath) {
    resolvedPath = inputPath;

    if (!std::filesystem::exists(inputPath) || !std::filesystem::is_directory(inputPath)) {
        return true;
    }

    auto restorePoints = engine.EnumerateBackupDates(inputPath);
    if (restorePoints.empty()) {
        std::cerr << "Error: No restore points were found in '" << inputPath << "'.\n";
        return false;
    }

    if (restorePointIndex <= 0 || restorePointIndex > static_cast<int>(restorePoints.size())) {
        std::cerr << "Error: When --restore points to a folder, specify --restore-point using one of these indexes:\n";
        for (size_t i = 0; i < restorePoints.size(); ++i) {
            std::cout << "  " << (i + 1) << ". " << restorePoints[i].date << " - "
                      << restorePoints[i].type << " (" << restorePoints[i].size << ")\n";
        }
        return false;
    }

    resolvedPath = restorePoints[restorePointIndex - 1].path;
    return true;
}

void performDiskRestore(RestoreEngine& engine, const std::string& backupPath, const std::string& targetDisk, bool showHidden, const std::string& logFilePath) {
    std::cout << "\nStarting metadata-driven disk restore...\n";
    std::cout << "Backup:      " << backupPath << "\n";
    std::cout << "Target disk: " << targetDisk << "\n";
    if (showHidden) {
        std::cout << "(including hidden partitions: EFI, MSR, Recovery)\n";
    } else {
        std::cout << "(hidden partitions excluded; use --show-hidden to include EFI/MSR/Recovery)\n";
    }
    std::cout << "\n";

    engine.SetLogOperationName("Linux Restore CLI");
    engine.SetLogFilePath(logFilePath);

    auto callback = [](int percent, const std::string& msg) {
        printf("\r[%3d%%] %-70s", percent, msg.c_str());
        fflush(stdout);
    };

    int result = engine.RestoreDisk(backupPath, targetDisk, callback, showHidden);

    std::cout << "\n\n";
    if (result == 0) {
        std::cout << "Disk restore completed successfully!\n";
    } else {
        std::cerr << "Disk restore failed: " << engine.GetLastError() << "\n";
    }
}

void printTree(const std::vector<RestoreEngine::RestoreItem>& items, int indent) {
    for (const auto& item : items) {
        std::string prefix(indent * 2, ' ');
        std::cout << prefix << "- " << item.name << " (" << item.type << ")\n";
        if (!item.children.empty()) {
            printTree(item.children, indent + 1);
        }
    }
}

void listBackupDates(RestoreEngine& engine, const std::string& backupPath) {
    std::cout << "\nScanning for backup dates in: " << backupPath << "\n";
    
    auto dates = engine.EnumerateBackupDates(backupPath);
    
    if (dates.empty()) {
        std::cout << "No backups found.\n";
        return;
    }

    std::cout << "\nDate                  Type          Size\n";
    std::cout << "----------------------------------------------------\n";
    for (const auto& date : dates) {
        printf("%-20s %-12s %s\n", date.date.c_str(), 
               date.type.c_str(), date.size.c_str());
    }
    std::cout << "\nTotal: " << dates.size() << " backup(s) found\n";
}

void showBackupContents(RestoreEngine& engine, const std::string& backupPath) {
    std::cout << "\nLoading backup contents from: " << backupPath << "\n\n";
    
    auto tree = engine.BuildRestoreTree(backupPath);
    
    if (tree.empty()) {
        std::cout << "No contents found or backup is empty.\n";
        return;
    }

    std::cout << "Backup Contents:\n";
    std::cout << "================\n";
    printTree(tree, 0);
}

std::vector<std::string> split(const std::string& str, char delimiter) {
    std::vector<std::string> tokens;
    std::stringstream ss(str);
    std::string token;
    
    while (std::getline(ss, token, delimiter)) {
        tokens.push_back(token);
    }
    
    return tokens;
}

void performRestore(RestoreEngine& engine, const std::string& backupPath, 
                    const std::vector<std::string>& items,
                    const std::string& dest, bool overwrite, const std::string& logFilePath) {
    
    std::cout << "\nStarting restore...\n";
    std::cout << "Backup:      " << backupPath << "\n";
    std::cout << "Destination: " << (dest.empty() ? "Original locations" : dest) << "\n";
    std::cout << "Items:       " << items.size() << " item(s)\n";
    std::cout << "Overwrite:   " << (overwrite ? "Yes" : "No") << "\n\n";

    engine.SetLogOperationName("Linux Restore CLI");
    engine.SetLogFilePath(logFilePath);

    auto callback = [](int percent, const std::string& msg) {
        printf("\r[%3d%%] %-60s", percent, msg.c_str());
        fflush(stdout);
    };
    
    bool success = engine.RestoreWithManifest(backupPath, dest, items, overwrite, callback);
    
    std::cout << "\n\n";
    
    if (success) {
        std::cout << "Restore completed successfully!\n";
    } else {
        std::cerr << "Restore failed: " << engine.GetLastError() << "\n";
    }
}

void listVolumesInBackup(RestoreEngine& engine, const std::string& backupPath, bool showHidden) {
    std::cout << "\nVolumes in backup: " << backupPath << "\n";
    if (showHidden) {
        std::cout << "(including hidden partitions)\n";
    }
    std::cout << "\n";

    auto volumes = engine.ListVolumesInBackup(backupPath, showHidden);

    if (volumes.empty()) {
        std::cout << "No volume metadata found in backup (or no visible volumes).\n";
        if (!showHidden) {
            std::cout << "Tip: use --show-hidden to include EFI, MSR, and Recovery partitions.\n";
        }
        return;
    }

    printf("%-4s  %-6s  %-10s  %-26s  %-8s  %s\n",
           "Idx", "Part#", "FileSystem", "Mount/Label", "Flags", "PartitionType");
    std::cout << std::string(80, '-') << "\n";

    for (const auto& v : volumes) {
        std::string flags;
        if (v.isBootVolume)       flags += "Boot ";
        if (v.isSystemVolume)     flags += "Sys ";
        if (v.isHiddenPartition)  flags += "Hidden";
        if (flags.empty())        flags = "-";

        std::string label = v.volumeLabel.empty() ? v.mountPath : v.volumeLabel;
        if (label.empty()) label = "(no label)";

        printf("%-4d  %-6lu  %-10s  %-26s  %-8s  %s\n",
               v.imageIndex,
               v.partitionNumber,
               v.fileSystem.empty() ? "?" : v.fileSystem.c_str(),
               label.c_str(),
               flags.c_str(),
               v.partitionType.empty() ? "-" : v.partitionType.c_str());
    }
    std::cout << "\n";
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
    std::cout << "\nTip: Use 'lsblk -f' for more details\n";
}

// -----------------------------------------------------------------------
//  Restore-target disk tree (matching Windows restore page)
// -----------------------------------------------------------------------

void printTargetDiskTree(const std::vector<RestoreEngine::DiskInfo>& disks) {
    if (disks.empty()) {
        std::cout << "No disks found.\n";
        return;
    }

    for (const auto& disk : disks) {
        if (disk.isBootDisk) {
            printf("  [BOOT - cannot restore]  /dev/%-8s  %s\n",
                   disk.device.c_str(), disk.size.c_str());
        } else {
            printf("  ( )  /dev/%-8s  %s\n",
                   disk.device.c_str(), disk.size.c_str());
        }

        for (const auto& part : disk.partitions) {
            std::string extra;
            if (!part.fsType.empty())   extra += "  fs:" + part.fsType;
            if (!part.mountPoint.empty()) extra += "  mount:" + part.mountPoint;

            if (disk.isBootDisk) {
                printf("         [boot]  /dev/%-10s  %s%s\n",
                       part.device.c_str(), part.size.c_str(), extra.c_str());
            } else {
                printf("         ( )     /dev/%-10s  %s%s\n",
                       part.device.c_str(), part.size.c_str(), extra.c_str());
            }
        }
    }
}

void listTargetDisks(RestoreEngine& engine) {
    std::cout << "\nAvailable restore target disks:\n";
    std::cout << "(Boot disk is shown but cannot be used as a restore target)\n";
    std::cout << "================================================================\n";
    auto disks = engine.ListTargetDisks();
    printTargetDiskTree(disks);
    std::cout << "\n";
}

void mountPartition(RestoreEngine& engine, const std::string& device, 
                    const std::string& mountPoint) {
    std::cout << "\nMounting " << device << " to " << mountPoint << "...\n";
    
    int result = engine.MountNTFSPartition(device, mountPoint);
    
    if (result == 0) {
        std::cout << "Mounted successfully!\n";
        std::cout << "You can now access files at: " << mountPoint << "\n";
    } else {
        std::cout << "Mount failed: " << engine.GetLastError() << "\n";
    }
}

void unmountPartition(RestoreEngine& engine, const std::string& mountPoint) {
    std::cout << "\nUnmounting " << mountPoint << "...\n";
    
    int result = engine.UnmountPartition(mountPoint);
    
    if (result == 0) {
        std::cout << "Unmounted successfully!\n";
    } else {
        std::cout << "Unmount failed: " << engine.GetLastError() << "\n";
    }
}

// Interactive mode - 3-step wizard
void runInteractive(RestoreEngine& engine) {
    printHeader();
    
    std::string backupFolder;
    std::string selectedBackupPath;
    std::vector<std::string> selectedItems;
    std::string destination;
    bool overwrite = true;

    // Step 1: Select backup and date
    std::cout << "Step 1: Select Backup and Date\n";
    std::cout << "===============================\n";
    std::cout << "Enter backup folder path: ";
    std::getline(std::cin, backupFolder);
    
    auto dates = engine.EnumerateBackupDates(backupFolder);
    
    if (dates.empty()) {
        std::cout << "No backups found. Exiting.\n";
        return;
    }

    std::cout << "\nAvailable backup dates:\n";
    for (size_t i = 0; i < dates.size(); i++) {
        std::cout << (i + 1) << ". " << dates[i].date << " - " 
                  << dates[i].type << " (" << dates[i].size << ")\n";
    }

    int selection = 0;
    std::cout << "\nSelect backup (1-" << dates.size() << "): ";
    std::cin >> selection;
    std::cin.ignore();

    if (selection < 1 || selection > dates.size()) {
        std::cout << "Invalid selection. Exiting.\n";
        return;
    }

    selectedBackupPath = dates[selection - 1].path;
    ensureBackupPassword(engine, selectedBackupPath);
    std::cout << "Selected: " << selectedBackupPath << "\n\n";

    // Step 2: Choose all items vs selected items
    std::cout << "Step 2: Choose Restore Scope\n";
    std::cout << "=============================\n";
    
    auto tree = engine.BuildRestoreTree(selectedBackupPath);
    if (tree.empty()) {
        std::cout << "No restore items were found for the selected restore point.\n";
        return;
    }

    char restoreAllChoice = 'y';
    std::cout << "Restore all files/volumes from this restore point? (y/n): ";
    std::cin >> restoreAllChoice;
    std::cin.ignore();

    bool restoreAll = (restoreAllChoice == 'y' || restoreAllChoice == 'Y');

    if (restoreAll) {
        collectAllRestoreItems(tree, selectedItems);
        std::cout << "Selected restore scope: all items from the restore point.\n\n";
    } else {
        std::cout << "Step 2A: Select Items to Restore\n";
        std::cout << "================================\n";
    
        std::cout << "Backup contents:\n";
        printTree(tree, 0);
    
        std::cout << "\nEnter items to restore (comma-separated paths):\n";
        std::string itemsInput;
        std::getline(std::cin, itemsInput);
    
        selectedItems = split(itemsInput, ',');
        // Trim whitespace
        for (auto& item : selectedItems) {
            item.erase(0, item.find_first_not_of(" \t"));
            item.erase(item.find_last_not_of(" \t") + 1);
        }

        selectedItems.erase(
            std::remove_if(selectedItems.begin(), selectedItems.end(), [](const std::string& item) {
                return item.empty();
            }),
            selectedItems.end());

        if (selectedItems.empty()) {
            std::cout << "No restore items were selected. Exiting.\n";
            return;
        }
    }

    std::cout << "Selected " << selectedItems.size() << " item(s) to restore.\n\n";

    // Step 3: Select destination — always show the target disk tree first,
    // matching the Windows restore page behavior.
    std::cout << "\nStep 3: Select Restore Target Disk or Partition\n";
    std::cout << "================================================\n";
    std::cout << "Boot/system disk is shown greyed and cannot be selected as a restore target.\n\n";

    auto targetDisks = engine.ListTargetDisks();
    printTargetDiskTree(targetDisks);
    std::cout << "\n";

    // Collect only non-boot devices as selectable options.
    std::vector<std::string> selectableDevices;
    for (const auto& d : targetDisks) {
        if (!d.isBootDisk) {
            selectableDevices.push_back("/dev/" + d.device);
            for (const auto& p : d.partitions) {
                selectableDevices.push_back("/dev/" + p.device);
            }
        }
    }

    std::cout << "Restore destination options:\n";
    std::cout << "  1. Restore to original location\n";
    std::cout << "  2. Restore to selected target disk or partition (from tree above)\n";
    std::cout << "  3. Restore to alternate folder path\n";
    std::cout << "  4. Metadata-driven disk reconstruction restore\n";
    std::cout << "Select option (1-4): ";

    int destChoice = 0;
    std::cin >> destChoice;
    std::cin.ignore();

    if (destChoice == 2) {
        if (selectableDevices.empty()) {
            std::cout << "No non-boot disks available as restore targets. Aborting.\n";
            return;
        }

        std::cout << "Enter target disk or partition device (e.g. /dev/sdb or /dev/sdb1): ";
        std::getline(std::cin, destination);

        bool valid = false;
        for (const auto& dev : selectableDevices) {
            if (dev == destination) { valid = true; break; }
        }
        if (!valid) {
            std::cout << "Warning: '" << destination << "' is not in the listed non-boot devices. Proceeding anyway.\n";
        }
    } else if (destChoice == 3) {
        std::cout << "Enter destination folder path: ";
        std::getline(std::cin, destination);
    } else if (destChoice == 4) {
        if (selectableDevices.empty()) {
            std::cout << "No non-boot disks available as restore targets. Aborting.\n";
            return;
        }

        std::cout << "Enter target disk device for reconstruction (e.g. /dev/sdb): ";
        std::getline(std::cin, destination);

        bool valid = false;
        for (const auto& dev : selectableDevices) {
            if (dev == destination) { valid = true; break; }
        }
        if (!valid) {
            std::cout << "Warning: '" << destination << "' is not a listed non-boot disk. Proceeding anyway.\n";
        }
    }

    std::cout << "\nReady to restore. Continue? (y/n): ";
    char confirm;
    std::cin >> confirm;

    if (confirm == 'y' || confirm == 'Y') {
        performRestore(engine, selectedBackupPath, selectedItems, destination, overwrite, std::string());
    } else {
        std::cout << "Restore cancelled.\n";
    }
}

int main(int argc, char* argv[]) {
    RestoreEngine engine;

    // No arguments - show usage
    if (argc == 1) {
        printHeader();
        printUsage();
        return 0;
    }

    std::string command = argv[1];

    // Help
    if (command == "--help" || command == "-h") {
        printHeader();
        printUsage();
        return 0;
    }

    // Interactive mode
    if (command == "--interactive" || command == "-i") {
        runInteractive(engine);
        return 0;
    }

    // List backup dates
    if (command == "--list-dates" && argc >= 3) {
        printHeader();
        listBackupDates(engine, argv[2]);
        return 0;
    }

    // Show backup contents
    if (command == "--show-contents" && argc >= 3) {
        printHeader();
        showBackupContents(engine, argv[2]);
        return 0;
    }

    // List disks
    if (command == "--list-disks") {
        printHeader();
        listDisks(engine);
        return 0;
    }

    // List restore target disks (boot disk flagged)
    if (command == "--list-target-disks") {
        printHeader();
        listTargetDisks(engine);
        return 0;
    }

    // Mount partition
    if (command == "--mount" && argc >= 4) {
        printHeader();
        mountPartition(engine, argv[2], argv[3]);
        return 0;
    }

    // Unmount partition
    if (command == "--unmount" && argc >= 3) {
        printHeader();
        unmountPartition(engine, argv[2]);
        return 0;
    }

    // Restore
    if (command == "--restore" && argc >= 3) {
        printHeader();
        
        std::string backupPath = argv[2];
        std::string resolvedBackupPath;
        std::vector<std::string> items;
        std::string dest;
        std::string logFilePath;
        int restorePointIndex = 0;
        bool restoreAll = false;
        bool overwrite = false;

        // Parse additional arguments
        for (int i = 3; i < argc; i++) {
            std::string arg = argv[i];
            
            if (arg == "--restore-point" && i + 1 < argc) {
                restorePointIndex = std::stoi(argv[++i]);
            } else if (arg == "--all") {
                restoreAll = true;
            } else if (arg == "--items" && i + 1 < argc) {
                items = split(argv[++i], ',');
            } else if (arg == "--dest" && i + 1 < argc) {
                dest = argv[++i];
            } else if (arg == "--log-file" && i + 1 < argc) {
                logFilePath = argv[++i];
            } else if (arg == "--overwrite") {
                overwrite = true;
            }
        }

        if (!resolveRestorePointPath(engine, backupPath, restorePointIndex, resolvedBackupPath)) {
            return 1;
        }

        ensureBackupPassword(engine, resolvedBackupPath);

        if (restoreAll) {
            auto tree = engine.BuildRestoreTree(resolvedBackupPath);
            if (tree.empty()) {
                std::cerr << "Error: No restore items were found for the selected restore point.\n";
                return 1;
            }

            collectAllRestoreItems(tree, items);
        }

        if (items.empty()) {
            std::cerr << "Error: No items specified. Use --all or --items <paths>\n";
            return 1;
        }

        performRestore(engine, resolvedBackupPath, items, dest, overwrite, logFilePath);
        return 0;
    }

    // List volumes in a disk/volume backup
    if (command == "--list-volumes" && argc >= 3) {
        printHeader();
        std::string backupPath = argv[2];
        bool showHidden = false;
        for (int i = 3; i < argc; i++) {
            if (std::string(argv[i]) == "--show-hidden") {
                showHidden = true;
            }
        }
        ensureBackupPassword(engine, backupPath);
        listVolumesInBackup(engine, backupPath, showHidden);
        return 0;
    }

    // Restore disk using metadata-driven layout reconstruction
    if (command == "--restore-disk" && argc >= 4) {
        printHeader();
        std::string backupPath = argv[2];
        std::string targetDisk = argv[3];
        bool showHidden = false;
        std::string logFilePath;
        for (int i = 4; i < argc; i++) {
            if (std::string(argv[i]) == "--show-hidden") {
                showHidden = true;
            } else if (std::string(argv[i]) == "--log-file" && i + 1 < argc) {
                logFilePath = argv[++i];
            }
        }
        ensureBackupPassword(engine, backupPath);
        performDiskRestore(engine, backupPath, targetDisk, showHidden, logFilePath);
        return 0;
    }

    // Unknown command
    std::cerr << "Unknown command: " << command << "\n";
    std::cout << "Use --help for usage information\n";
    return 1;
}
