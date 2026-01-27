// LinuxRestore/restore_engine.cpp
// Cross-platform restore engine for Linux-based bootable USB

#include <iostream>
#include <string>
#include <vector>
#include <filesystem>
#include <fstream>
#include <cstring>
#include <ctime>
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

public:
    RestoreEngine(ProgressCallback callback = nullptr) 
        : progressCallback(callback) {}

    std::string GetLastError() const { return lastError; }

    // Restore files from backup to destination
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
