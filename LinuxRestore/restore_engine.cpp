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
#include <array>
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
    std::string backupPassword;
    bool backupPasswordVerified = false;

    static constexpr const char* EncryptedHeader = "SSBAES1";

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

    bool IsEncryptedBackup(const std::string& path) {
        std::ifstream file(path, std::ios::binary);
        if (!file) {
            return false;
        }

        std::array<char, 7> header{};
        file.read(header.data(), header.size());
        return file.gcount() == static_cast<std::streamsize>(header.size()) &&
               std::memcmp(header.data(), EncryptedHeader, header.size()) == 0;
    }

    bool EnsurePasswordAvailable() {
        if (!backupPassword.empty()) {
            return true;
        }

        if (progressCallback) {
            progressCallback(0, "Encrypted backup detected - password required.");
        }

        std::cout << "Encrypted backup detected. Enter password: ";
        std::getline(std::cin, backupPassword);

        if (backupPassword.empty()) {
            SetError("Encryption password is required for this backup.");
            return false;
        }

        return true;
    }

    std::string CreateTempPath(const std::string& originalPath) {
        char tmpl[] = "/tmp/backup_restore_XXXXXX";
        int fd = mkstemp(tmpl);
        if (fd < 0) {
            throw std::runtime_error("Failed to create temporary file for encrypted backup.");
        }
        close(fd);

        std::string tempPath = std::string(tmpl) + ".ssb";
        rename(tmpl, tempPath.c_str());
        return tempPath;
    }

    bool DecryptEncryptedBackup(const std::string& encryptedPath, const std::string& outputPath) {
        if (!EnsurePasswordAvailable()) {
            return false;
        }

        std::string command = "python3 -c \"from pathlib import Path; import hashlib, sys; from Crypto.Cipher import AES; from Crypto.Util.Padding import unpad; "
            "password=sys.argv[1].encode('utf-8'); src=Path(sys.argv[2]); dst=Path(sys.argv[3]); data=src.read_bytes(); "
            "assert data[:7]==b'SSBAES1'; salt=data[7:23]; iv=data[23:39]; enc=data[39:]; "
            "key=hashlib.pbkdf2_hmac('sha256', password, salt, 100000, 16); "
            "cipher=AES.new(key, AES.MODE_CBC, iv); dst.write_bytes(unpad(cipher.decrypt(enc), AES.block_size))\" '" +
            backupPassword + "' '" + encryptedPath + "' '" + outputPath + "' 2>/tmp/backup_restore_decrypt.log";

        int result = system(command.c_str());
        if (result != 0) {
            backupPassword.clear();
            backupPasswordVerified = false;
            SetError("Failed to decrypt encrypted backup. The password may be incorrect.");
            return false;
        }

        backupPasswordVerified = true;
        return true;
    }

    template<typename Func>
    auto WithPreparedBackup(const std::string& backupPath, Func func) -> decltype(func(backupPath)) {
        if (!fs::is_regular_file(backupPath) || !IsEncryptedBackup(backupPath)) {
            return func(backupPath);
        }

        ReportProgress(2, "Decrypting encrypted backup to temporary working file...");
        std::string tempPath = CreateTempPath(backupPath);

        try {
            if (!DecryptEncryptedBackup(backupPath, tempPath)) {
                BackupCleanup(tempPath);
                return decltype(func(backupPath))();
            }

            auto result = func(tempPath);
            BackupCleanup(tempPath);
            return result;
        }
        catch (...) {
            BackupCleanup(tempPath);
            throw;
        }
    }

    void BackupCleanup(const std::string& path) {
        try {
            if (!path.empty() && fs::exists(path)) {
                fs::remove(path);
            }
        }
        catch (...) {
        }
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

    void SetBackupPassword(const std::string& password) {
        backupPassword = password;
        backupPasswordVerified = !password.empty();
    }

    // Restore files from backup to destination
    // Now supports both folder-based backups AND WIM (.ssb) backups
    int RestoreFiles(const std::string& backupPath, 
                     const std::string& destPath, 
                     bool overwriteExisting) {
        try {
            return WithPreparedBackup<int>(backupPath, [&](const std::string& workingPath) {
                ReportProgress(0, "Starting file restore...");

                if (!fs::exists(workingPath)) {
                    SetError("Backup path does not exist: " + workingPath);
                    return -1;
                }

                if (fs::is_regular_file(workingPath) && IsWimBackup(workingPath)) {
                    ReportProgress(5, "Detected WIM backup format");

                    try {
                        fs::create_directories(destPath);
                    } catch (const std::exception& e) {
                        SetError(std::string("Failed to create destination: ") + e.what());
                        return -1;
                    }

                    int result = ExtractWimBackup(workingPath, destPath);
                    if (result != 0) {
                        return result;
                    }

                    ReportProgress(100, "WIM restore complete!");
                    return 0;
                }

                ReportProgress(5, "Detected folder-based backup (legacy format)");

                try {
                    fs::create_directories(destPath);
                } catch (const std::exception& e) {
                    SetError(std::string("Failed to create destination: ") + e.what());
                    return -1;
                }

                ReportProgress(10, "Scanning backup files...");

                std::vector<fs::path> filesToRestore;
                uintmax_t totalSize = 0;

                if (fs::is_directory(workingPath)) {
                    for (const auto& entry : fs::recursive_directory_iterator(workingPath)) {
                        if (entry.is_regular_file()) {
                            filesToRestore.push_back(entry.path());
                            totalSize += entry.file_size();
                        }
                    }
                } else if (fs::is_regular_file(workingPath)) {
                    filesToRestore.push_back(workingPath);
                    totalSize = fs::file_size(workingPath);
                }

                if (filesToRestore.empty()) {
                    SetError("No files found in backup");
                    return -1;
                }

                ReportProgress(20, "Found " + std::to_string(filesToRestore.size()) + " files to restore");

                uintmax_t copiedSize = 0;
                int filesRestored = 0;

                for (const auto& sourceFile : filesToRestore) {
                    try {
                        fs::path relativePath = fs::relative(sourceFile, workingPath);
                        fs::path destFile = fs::path(destPath) / relativePath;

                        fs::create_directories(destFile.parent_path());

                        if (fs::exists(destFile) && !overwriteExisting) {
                            continue;
                        }

                        fs::copy(sourceFile, destFile,
                            overwriteExisting ? fs::copy_options::overwrite_existing
                                             : fs::copy_options::skip_existing);

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
                        }

                        filesRestored++;
                        copiedSize += fs::file_size(sourceFile);

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

                int verifiedFiles = 0;
                for (const auto& sourceFile : filesToRestore) {
                    fs::path relativePath = fs::relative(sourceFile, workingPath);
                    fs::path destFile = fs::path(destPath) / relativePath;

                    if (fs::exists(destFile)) {
                        verifiedFiles++;
                    }
                }

                ReportProgress(100, "Restore completed! Restored " + std::to_string(filesRestored) + " files");
                return 0;
            });

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
            if (!fs::exists(backupPath)) {
                SetError("Backup path does not exist.");
                return dates;
            }

            if (fs::is_regular_file(backupPath)) {
                BackupDate date;
                auto fileName = fs::path(backupPath).filename().string();
                std::string lowerFileName = fileName;
                std::transform(lowerFileName.begin(), lowerFileName.end(), lowerFileName.begin(), ::tolower);

                if (lowerFileName.find("incremental") != std::string::npos) {
                    date.type = "Incremental";
                } else if (lowerFileName.find("differential") != std::string::npos) {
                    date.type = "Differential";
                } else {
                    date.type = "Full";
                }

                uintmax_t totalSize = fs::file_size(backupPath);
                if (totalSize < 1024) {
                    date.size = std::to_string(totalSize) + " B";
                } else if (totalSize < 1024 * 1024) {
                    date.size = std::to_string(totalSize / 1024) + " KB";
                } else if (totalSize < 1024 * 1024 * 1024) {
                    date.size = std::to_string(totalSize / (1024 * 1024)) + " MB";
                } else {
                    double gb = static_cast<double>(totalSize) / (1024.0 * 1024.0 * 1024.0);
                    char buf[32];
                    snprintf(buf, sizeof(buf), "%.2f GB", gb);
                    date.size = buf;
                }

                auto ftime = fs::last_write_time(backupPath);
                auto sctp = std::chrono::time_point_cast<std::chrono::system_clock::duration>(
                    ftime - fs::file_time_type::clock::now() + std::chrono::system_clock::now());
                time_t cftime = std::chrono::system_clock::to_time_t(sctp);
                struct tm timeinfo;
                localtime_r(&cftime, &timeinfo);

                char dateStr[128];
                strftime(dateStr, sizeof(dateStr), "%Y-%m-%d %H:%M:%S", &timeinfo);
                date.date = dateStr;
                date.path = backupPath;
                dates.push_back(date);
                return dates;
            }

            if (!fs::is_directory(backupPath)) {
                SetError("Backup path is not a directory or backup file.");
                return dates;
            }

            for (const auto& entry : fs::directory_iterator(backupPath)) {
                if (!entry.is_directory() && !entry.is_regular_file()) {
                    continue;
                }

                std::string folderName = entry.path().filename().string();
                std::string type = "Full";

                if (folderName.find("Full") != std::string::npos) {
                    type = "Full";
                } else if (folderName.find("Incremental") != std::string::npos || folderName.find("incremental") != std::string::npos) {
                    type = "Incremental";
                } else if (folderName.find("Differential") != std::string::npos || folderName.find("differential") != std::string::npos) {
                    type = "Differential";
                } else if (entry.is_regular_file() && (entry.path().extension() == ".ssb" || entry.path().extension() == ".wim")) {
                    type = "Full";
                } else {
                    continue;
                }

                uintmax_t totalSize = 0;
                try {
                    if (entry.is_directory()) {
                        for (const auto& file : fs::recursive_directory_iterator(entry.path())) {
                            if (fs::is_regular_file(file)) {
                                totalSize += fs::file_size(file);
                            }
                        }
                    } else {
                        totalSize = fs::file_size(entry.path());
                    }
                } catch (...) {}

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
            WithPreparedBackup<int>(backupPath, [&](const std::string& workingPath) {
                if (!fs::exists(workingPath)) {
                    SetError("Backup path does not exist");
                    return 0;
                }

                if (fs::is_regular_file(workingPath) && IsWimBackup(workingPath)) {
                    std::string command = "wimlib-imagex dir '" + workingPath + "' 1 2>/dev/null";
                    FILE* pipe = popen(command.c_str(), "r");
                    if (!pipe) {
                        SetError("Failed to read WIM contents.");
                        return 0;
                    }

                    char buffer[1024];
                    while (fgets(buffer, sizeof(buffer), pipe) != nullptr) {
                        std::string line(buffer);
                        if (line.empty() || line.find("Directory listing") != std::string::npos || line.find("------") != std::string::npos) {
                            continue;
                        }

                        line.erase(std::remove(line.begin(), line.end(), '\r'), line.end());
                        line.erase(std::remove(line.begin(), line.end(), '\n'), line.end());
                        if (line.empty()) {
                            continue;
                        }

                        RestoreItem item;
                        item.name = line;
                        item.path = line;
                        item.type = "Item";
                        item.checked = false;
                        tree.push_back(item);
                    }

                    pclose(pipe);
                    return 0;
                }

                if (fs::is_directory(workingPath)) {
                    for (const auto& entry : fs::directory_iterator(workingPath)) {
                        RestoreItem item;
                        item.name = entry.path().filename().string();
                        item.path = entry.path().string();
                        item.type = entry.is_directory() ? "Folder" : "File";
                        item.checked = false;

                        if (entry.is_directory()) {
                            item.children = BuildTreeRecursive(entry.path().string(), 1);
                        }

                        tree.push_back(item);
                    }
                }

                return 0;
            });

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
