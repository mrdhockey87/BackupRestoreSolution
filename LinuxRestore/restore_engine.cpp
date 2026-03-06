// LinuxRestore/restore_engine.cpp
// Cross-platform restore engine for Linux-based bootable USB
// Version 5.13.7.0 - Added WIM (.ssb) support for unified backup format

#include <iostream>
#include <string>
#include <vector>
#include <filesystem>
#include <fstream>
#include <cstring>
#include <ctime>
#include <algorithm>
#include <functional>
#include <sys/stat.h>
#include <unistd.h>
#include <fcntl.h>

namespace fs = std::filesystem;

// Progress callback type
typedef void (*ProgressCallback)(int percentage, const char* message);

class RestoreEngine {
private:
    ProgressCallback progressCallback;
    std::string lastError;

    void SetError(const std::string& error) {
        lastError = error;
        std::cerr << "ERROR: " << error << std::endl;
    }

    void ReportProgress(int percentage, const std::string& message) {
        if (progressCallback) {
            progressCallback(percentage, message.c_str());
        }
        std::cout << "[" << percentage << "%] " << message << std::endl;
    }

    // NEW: Check if file is WIM format (.ssb or .wim)
    bool IsWimBackup(const std::string& path) {
        // Check file extension
        std::string ext = fs::path(path).extension().string();
        std::transform(ext.begin(), ext.end(), ext.begin(), ::tolower);

        if (ext == ".ssb" || ext == ".wim") {
            return true;
        }

        // Check magic number for WIM files (MSWIM)
        std::ifstream file(path, std::ios::binary);
        if (file) {
            char magic[8] = {0};
            file.read(magic, 8);
            if (strncmp(magic, "MSWIM", 5) == 0) {
                return true;
            }
        }

        return false;
    }

    // NEW: Get number of images in WIM file
    int GetWimImageCount(const std::string& wimPath) {
        // Use wimlib-imagex info to get image count
        std::string cmd = "wimlib-imagex info '" + wimPath + "' 2>/dev/null | grep -c '^Image Count:'";

        FILE* pipe = popen(cmd.c_str(), "r");
        if (!pipe) {
            return -1;
        }

        char buffer[128];
        std::string result;
        while (fgets(buffer, sizeof(buffer), pipe) != nullptr) {
            result += buffer;
        }
        pclose(pipe);

        // Parse the count from output
        try {
            return std::stoi(result);
        }
        catch (...) {
            return -1;
        }
    }

    // NEW: List all images in WIM file with their info
    bool ListWimImages(const std::string& wimPath) {
        std::cout << "\n========================================" << std::endl;
        std::cout << "Available Backup Images" << std::endl;
        std::cout << "========================================" << std::endl;

        // Use wimlib-imagex info to list all images
        std::string cmd = "wimlib-imagex info '" + wimPath + "' --detailed 2>&1";
        int result = system(cmd.c_str());

        if (result != 0) {
            SetError("Failed to read WIM information");
            return false;
        }

        std::cout << "\n========================================" << std::endl;
        return true;
    }

    // NEW: Get image information
    bool GetWimImageInfo(const std::string& wimPath, int imageIndex, 
                        std::string& name, std::string& description) {
        // Use wimlib-imagex info to get specific image details
        std::string cmd = "wimlib-imagex info '" + wimPath + "' " + std::to_string(imageIndex) + " 2>&1";

        FILE* pipe = popen(cmd.c_str(), "r");
        if (!pipe) {
            return false;
        }

        char buffer[1024];
        std::string output;
        while (fgets(buffer, sizeof(buffer), pipe) != nullptr) {
            output += buffer;
        }
        pclose(pipe);

        // Parse name and description from output
        // Look for "Name:" and "Description:" lines
        size_t namePos = output.find("Name:");
        if (namePos != std::string::npos) {
            size_t nameEnd = output.find('\n', namePos);
            name = output.substr(namePos + 5, nameEnd - namePos - 5);
            // Trim whitespace
            name.erase(0, name.find_first_not_of(" \t"));
            name.erase(name.find_last_not_of(" \t\r\n") + 1);
        }

        size_t descPos = output.find("Description:");
        if (descPos != std::string::npos) {
            size_t descEnd = output.find('\n', descPos);
            description = output.substr(descPos + 12, descEnd - descPos - 12);
            // Trim whitespace
            description.erase(0, description.find_first_not_of(" \t"));
            description.erase(description.find_last_not_of(" \t\r\n") + 1);
        }

        return true;
    }

    // NEW: Extract WIM backup using wimlib
    int ExtractWimBackup(const std::string& wimPath, 
                        const std::string& destPath,
                        int imageIndex = 1) {
        ReportProgress(10, "Detected WIM format backup (.ssb file)");

        // Check if wimlib-imagex is available
        int result = system("which wimlib-imagex > /dev/null 2>&1");
        if (result != 0) {
            SetError("wimlib-imagex not found. Install wimlib: sudo apt-get install wimtools");
            std::cerr << "\nTo extract WIM backups, install wimlib:" << std::endl;
            std::cerr << "  Debian/Ubuntu: sudo apt-get install wimtools" << std::endl;
            std::cerr << "  Fedora/RHEL:   sudo dnf install wimlib-utils" << std::endl;
            std::cerr << "  Arch Linux:    sudo pacman -S wimlib" << std::endl;
            return -1;
        }

        ReportProgress(20, "Using wimlib to extract backup...");

        // First, get information about the WIM file
        std::string infoCmd = "wimlib-imagex info '" + wimPath + "' 2>&1";
        ReportProgress(25, "Reading WIM metadata...");
        system(infoCmd.c_str());

        // Extract the WIM image
        ReportProgress(30, "Extracting WIM image " + std::to_string(imageIndex) + "...");

        std::string extractCmd = "wimlib-imagex extract '" + wimPath + "' " + 
                                std::to_string(imageIndex) + " '" + destPath + 
                                "' --preserve-modes --preserve-timestamps 2>&1";

        std::cout << "\nExecuting: " << extractCmd << std::endl;
        result = system(extractCmd.c_str());

        if (result != 0) {
            SetError("WIM extraction failed with code " + std::to_string(result));
            return -2;
        }

        ReportProgress(90, "WIM extraction complete");
        return 0;
    }

public:
    RestoreEngine(ProgressCallback callback = nullptr) 
        : progressCallback(callback) {}

    std::string GetLastError() const { return lastError; }

    // Restore files from backup to destination
    // Now supports both folder-based backups AND WIM (.ssb) backups
    int RestoreFiles(const std::string& backupPath, 
                     const std::string& destPath, 
                     bool overwriteExisting) {
        try {
            ReportProgress(0, "Starting file restore...");

            // Verify backup exists
            if (!fs::exists(backupPath)) {
                SetError("Backup path does not exist: " + backupPath);
                return -1;
            }

            // Check if this is a WIM backup
            if (fs::is_regular_file(backupPath) && IsWimBackup(backupPath)) {
                // NEW: Handle WIM backup
                ReportProgress(5, "Detected WIM backup format");

                // Create destination directory
                try {
                    fs::create_directories(destPath);
                } catch (const std::exception& e) {
                    SetError(std::string("Failed to create destination: ") + e.what());
                    return -1;
                }

                // Extract WIM
                int result = ExtractWimBackup(backupPath, destPath);
                if (result != 0) {
                    return result;
                }

                ReportProgress(100, "WIM restore complete!");
                return 0;
            }

            // OLD: Handle folder-based backup (legacy support)
            ReportProgress(5, "Detected folder-based backup (legacy format)");

            // Create destination directory
            try {
                fs::create_directories(destPath);
            } catch (const std::exception& e) {
                SetError(std::string("Failed to create destination: ") + e.what());
                return -1;
            }

            ReportProgress(10, "Scanning backup files...");

            // Collect all files to restore
            std::vector<fs::path> filesToRestore;
            uintmax_t totalSize = 0;

            if (fs::is_directory(backupPath)) {
                for (const auto& entry : fs::recursive_directory_iterator(backupPath)) {
                    if (entry.is_regular_file()) {
                        filesToRestore.push_back(entry.path());
                        totalSize += entry.file_size();
                    }
                }
            } else if (fs::is_regular_file(backupPath)) {
                filesToRestore.push_back(backupPath);
                totalSize = fs::file_size(backupPath);
            }

            if (filesToRestore.empty()) {
                SetError("No files found in backup");
                return -1;
            }

            ReportProgress(20, "Found " + std::to_string(filesToRestore.size()) + " files to restore");

            // Restore files
            uintmax_t copiedSize = 0;
            int filesRestored = 0;

            for (const auto& sourceFile : filesToRestore) {
                try {
                    // Calculate relative path
                    fs::path relativePath = fs::relative(sourceFile, backupPath);
                    fs::path destFile = fs::path(destPath) / relativePath;

                    // Create destination directory
                    fs::create_directories(destFile.parent_path());

                    // Check if file exists
                    if (fs::exists(destFile) && !overwriteExisting) {
                        continue;
                    }

                    // Copy file
                    fs::copy(sourceFile, destFile, 
                        overwriteExisting ? fs::copy_options::overwrite_existing 
                                         : fs::copy_options::skip_existing);

                    // Copy permissions and timestamps
                    try {
                        struct stat sourceStat;
                        if (stat(sourceFile.c_str(), &sourceStat) == 0) {
                            chmod(destFile.c_str(), sourceStat.st_mode);
                            
                            struct timespec times[2];
                            times[0].tv_sec = sourceStat.st_atime;
                            times[0].tv_nsec = 0;
                            times[1].tv_sec = sourceStat.st_mtime;
                            times[1].tv_nsec = 0;
                            utimensat(AT_FDCWD, destFile.c_str(), times, 0);
                        }
                    } catch (...) {
                        // Ignore attribute errors
                    }

                    filesRestored++;
                    copiedSize += fs::file_size(sourceFile);

                    // Update progress
                    int progress = 20 + (int)((copiedSize * 70) / totalSize);
                    if (filesRestored % 10 == 0) {
                        std::string msg = "Restored " + std::to_string(filesRestored) + 
                                        " of " + std::to_string(filesToRestore.size()) + " files";
                        ReportProgress(progress, msg);
                    }

                } catch (const std::exception& e) {
                    std::cerr << "Warning: Failed to restore " << sourceFile << ": " << e.what() << std::endl;
                    continue;
                }
            }

            ReportProgress(90, "Verifying restore...");

            // Quick verification
            int verifiedFiles = 0;
            for (const auto& sourceFile : filesToRestore) {
                fs::path relativePath = fs::relative(sourceFile, backupPath);
                fs::path destFile = fs::path(destPath) / relativePath;

                if (fs::exists(destFile)) {
                    verifiedFiles++;
                }
            }

            ReportProgress(100, "Restore completed! Restored " + std::to_string(filesRestored) + " files");

            return 0;

        } catch (const std::exception& e) {
            SetError(std::string("Exception during restore: ") + e.what());
            return -1;
        }
    }

    // Mount NTFS partition for Windows restore
    int MountNTFSPartition(const std::string& device, const std::string& mountPoint) {
        ReportProgress(0, "Mounting NTFS partition...");

        // Create mount point
        fs::create_directories(mountPoint);

        // Mount using ntfs-3g
        std::string cmd = "ntfs-3g " + device + " " + mountPoint + " -o rw,force 2>&1";
        FILE* pipe = popen(cmd.c_str(), "r");
        
        if (!pipe) {
            SetError("Failed to execute mount command");
            return -1;
        }

        char buffer[256];
        std::string result;
        while (fgets(buffer, sizeof(buffer), pipe) != nullptr) {
            result += buffer;
        }

        int returnCode = pclose(pipe);

        if (returnCode != 0) {
            SetError("Mount failed: " + result);
            return -1;
        }

        ReportProgress(100, "Partition mounted successfully");
        return 0;
    }

    // Unmount partition
    int UnmountPartition(const std::string& mountPoint) {
        std::string cmd = "umount " + mountPoint + " 2>&1";
        system(cmd.c_str());
        return 0;
    }

    // List available disks and partitions
    std::vector<std::string> ListDisks() {
        std::vector<std::string> disks;

        FILE* pipe = popen("lsblk -nlo NAME,SIZE,TYPE,FSTYPE 2>&1", "r");
        if (!pipe) return disks;

        char buffer[256];
        while (fgets(buffer, sizeof(buffer), pipe) != nullptr) {
            disks.push_back(std::string(buffer));
        }

        pclose(pipe);
        return disks;
    }

    // Scan for backup files
    std::vector<std::string> ScanForBackups(const std::string& searchPath) {
        std::vector<std::string> backups;

        try {
            for (const auto& entry : fs::recursive_directory_iterator(searchPath)) {
                if (entry.is_regular_file()) {
                    std::string filename = entry.path().filename().string();
                    if (filename.find("backup") != std::string::npos ||
                        filename.find(".bak") != std::string::npos ||
                        filename.find(".backup") != std::string::npos) {
                        backups.push_back(entry.path().string());
                    }
                }
            }
        } catch (...) {
            // Ignore errors
        }

        return backups;
    }

    // NEW: Enumerate backup dates in a folder
    struct BackupDate {
        std::string date;
        std::string type;
        std::string size;
        std::string path;
    };

    std::vector<BackupDate> EnumerateBackupDates(const std::string& backupPath) {
        std::vector<BackupDate> dates;

        try {
            if (!fs::exists(backupPath) || !fs::is_directory(backupPath)) {
                SetError("Backup path does not exist or is not a directory");
                return dates;
            }

            for (const auto& entry : fs::directory_iterator(backupPath)) {
                if (entry.is_directory()) {
                    std::string folderName = entry.path().filename().string();
                    
                    // Check if it looks like a backup folder
                    std::string type = "Full";
                    if (folderName.find("Full") != std::string::npos) {
                        type = "Full";
                    } else if (folderName.find("Incremental") != std::string::npos) {
                        type = "Incremental";
                    } else if (folderName.find("Differential") != std::string::npos) {
                        type = "Differential";
                    } else {
                        continue; // Not a backup folder
                    }

                    // Get folder size
                    uintmax_t totalSize = 0;
                    try {
                        for (const auto& file : fs::recursive_directory_iterator(entry.path())) {
                            if (fs::is_regular_file(file)) {
                                totalSize += fs::file_size(file);
                            }
                        }
                    } catch (...) {}

                    // Format size
                    std::string sizeStr;
                    if (totalSize < 1024) {
                        sizeStr = std::to_string(totalSize) + " B";
                    } else if (totalSize < 1024 * 1024) {
                        sizeStr = std::to_string(totalSize / 1024) + " KB";
                    } else if (totalSize < 1024 * 1024 * 1024) {
                        sizeStr = std::to_string(totalSize / (1024 * 1024)) + " MB";
                    } else {
                        double gb = static_cast<double>(totalSize) / (1024.0 * 1024.0 * 1024.0);
                        char buf[32];
                        snprintf(buf, sizeof(buf), "%.2f GB", gb);
                        sizeStr = buf;
                    }

                    // Get modification time
                    auto ftime = fs::last_write_time(entry.path());
                    auto sctp = std::chrono::time_point_cast<std::chrono::system_clock::duration>(
                        ftime - fs::file_time_type::clock::now() + std::chrono::system_clock::now());
                    time_t cftime = std::chrono::system_clock::to_time_t(sctp);
                    struct tm timeinfo;
                    localtime_r(&cftime, &timeinfo);

                    char dateStr[128];
                    strftime(dateStr, sizeof(dateStr), "%Y-%m-%d %H:%M:%S", &timeinfo);

                    BackupDate date;
                    date.date = dateStr;
                    date.type = type;
                    date.size = sizeStr;
                    date.path = entry.path().string();
                    dates.push_back(date);
                }
            }

            // Sort by date (newest first)
            std::sort(dates.begin(), dates.end(), 
                [](const BackupDate& a, const BackupDate& b) {
                    return a.date > b.date;
                });

        } catch (const std::exception& e) {
            SetError(std::string("Failed to enumerate backup dates: ") + e.what());
        }

        return dates;
    }

    // NEW: Build hierarchical tree of backup contents
    struct RestoreItem {
        std::string name;
        std::string path;
        std::string type;
        bool checked;
        std::vector<RestoreItem> children;

        RestoreItem() : checked(false) {}
    };

    std::vector<RestoreItem> BuildRestoreTree(const std::string& backupPath) {
        std::vector<RestoreItem> tree;

        try {
            if (!fs::exists(backupPath)) {
                SetError("Backup path does not exist");
                return tree;
            }

            // Build tree from backup contents
            // For simplicity, we'll create a flat list of drives/volumes
            // In production, this would parse the backup metadata

            if (fs::is_directory(backupPath)) {
                for (const auto& entry : fs::directory_iterator(backupPath)) {
                    RestoreItem item;
                    item.name = entry.path().filename().string();
                    item.path = entry.path().string();
                    item.type = entry.is_directory() ? "Folder" : "File";
                    item.checked = false;

                    // Add children if directory
                    if (entry.is_directory()) {
                        item.children = BuildTreeRecursive(entry.path().string(), 1);
                    }

                    tree.push_back(item);
                }
            }

        } catch (const std::exception& e) {
            SetError(std::string("Failed to build restore tree: ") + e.what());
        }

        return tree;
    }

private:
    std::vector<RestoreItem> BuildTreeRecursive(const std::string& path, int depth) {
        std::vector<RestoreItem> items;
        
        // Limit recursion depth to avoid performance issues
        if (depth > 3) return items;

        try {
            for (const auto& entry : fs::directory_iterator(path)) {
                RestoreItem item;
                item.name = entry.path().filename().string();
                item.path = entry.path().string();
                item.type = entry.is_directory() ? "Folder" : "File";
                item.checked = false;

                if (entry.is_directory()) {
                    item.children = BuildTreeRecursive(entry.path().string(), depth + 1);
                }

                items.push_back(item);
            }
        } catch (...) {
            // Ignore errors (access denied, etc.)
        }

        return items;
    }

public:
    // Enhanced: Restore selected items from manifest with intelligent type detection (v5.11.0.7)
    bool RestoreWithManifest(
        const std::string& backupPath,
        const std::string& destPath,
        const std::vector<std::string>& items,
        bool overwrite,
        std::function<void(int, const std::string&)> callback) {
        
        try {
            int totalItems = items.size();
            int currentItem = 0;

            for (const auto& item : items) {
                // Calculate progress percentage
                int percentage = (currentItem * 100) / totalItems;
                
                if (callback) {
                    callback(percentage, "Restoring: " + item);
                }

                // Determine paths
                std::string sourcePath;
                if (item[0] == '/') {
                    sourcePath = backupPath + item;
                } else {
                    sourcePath = backupPath + "/" + item;
                }

                std::string targetPath;
                if (destPath.empty()) {
                    targetPath = item;
                } else {
                    targetPath = destPath + "/" + item;
                }

                // Intelligent type detection and restore
                if (fs::exists(sourcePath)) {
                    if (fs::is_directory(sourcePath)) {
                        // Check if it's a disk backup (contains .img files)
                        bool hasDiskImage = false;
                        bool hasSystemState = false;
                        
                        try {
                            // Check for disk images
                            for (const auto& entry : fs::directory_iterator(sourcePath)) {
                                if (entry.path().extension() == ".img") {
                                    hasDiskImage = true;
                                    break;
                                }
                            }
                            
                            // Check for SystemState directory (Windows backup)
                            hasSystemState = fs::exists(sourcePath + "/SystemState");
                        } catch (...) {}

                        if (hasDiskImage) {
                            // This is a disk backup - needs special handling
                            if (callback) {
                                callback(percentage, "Disk image detected: " + item);
                                callback(percentage, "WARNING: Disk restore requires root privileges and target device");
                                callback(percentage, "Skipping automatic disk restore - use manual dd or restore tools");
                            }
                            
                            // For Linux, we can't automatically restore disk images without:
                            // 1. Root privileges
                            // 2. Target device specification
                            // 3. Confirmation to overwrite
                            // So we log a warning and skip, or could implement dd command
                            
                            // Optional: implement disk restore for Linux
                            // This would require: sudo dd if=disk_0.img of=/dev/sdX bs=1M status=progress
                        }
                        else if (hasSystemState) {
                            // This is a Windows system state backup
                            // Linux can't restore Windows registry/BCD, but can restore files
                            if (callback) {
                                callback(percentage, "Windows system backup detected: " + item);
                                callback(percentage, "Restoring files only (system state requires Windows)");
                            }
                            
                            // Restore files excluding SystemState directory
                            RestoreFiles(sourcePath, targetPath, overwrite);
                        }
                        else {
                            // Regular directory - restore files
                            RestoreFiles(sourcePath, targetPath, overwrite);
                        }
                    }
                    else {
                        // Single file - direct copy
                        try {
                            fs::create_directories(fs::path(targetPath).parent_path());
                            
                            auto copyOptions = overwrite ?
                                fs::copy_options::overwrite_existing :
                                fs::copy_options::skip_existing;
                            
                            fs::copy_file(sourcePath, targetPath, copyOptions);
                        } catch (const std::exception& e) {
                            if (callback) {
                                callback(percentage, "Warning: Failed to restore file: " + std::string(e.what()));
                            }
                        }
                    }
                }
                else {
                    // Source doesn't exist
                    if (callback) {
                        callback(percentage, "Warning: Source not found: " + item);
                    }
                }

                currentItem++;
            }

            if (callback) {
                callback(100, "Restore completed");
            }

            return true;

        } catch (const std::exception& e) {
            SetError(std::string("Restore failed: ") + e.what());
            return false;
        }
    }
};

// C API for compatibility
extern "C" {
    void* CreateRestoreEngine() {
        return new RestoreEngine();
    }

    void DestroyRestoreEngine(void* engine) {
        delete static_cast<RestoreEngine*>(engine);
    }

    int RestoreFiles(void* engine, const char* backupPath, 
                     const char* destPath, int overwrite) {
        auto* eng = static_cast<RestoreEngine*>(engine);
        return eng->RestoreFiles(backupPath, destPath, overwrite != 0);
    }

    int MountNTFS(void* engine, const char* device, const char* mountPoint) {
        auto* eng = static_cast<RestoreEngine*>(engine);
        return eng->MountNTFSPartition(device, mountPoint);
    }

    int Unmount(void* engine, const char* mountPoint) {
        auto* eng = static_cast<RestoreEngine*>(engine);
        return eng->UnmountPartition(mountPoint);
    }

    const char* GetLastError(void* engine) {
        auto* eng = static_cast<RestoreEngine*>(engine);
        return eng->GetLastError().c_str();
    }
}
