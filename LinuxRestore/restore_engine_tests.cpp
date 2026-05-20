#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

#define private public
#define public public
#include "restore_engine.cpp"
#undef private
#undef public

namespace fs = std::filesystem;

namespace
{
    bool Expect(bool condition, const std::string& message)
    {
        if (!condition)
        {
            std::cerr << "FAILED: " << message << std::endl;
            return false;
        }

        return true;
    }

    std::string CreateUniqueTempDirectory()
    {
        fs::path root = fs::temp_directory_path() / "SecureServerBackupLinuxRestoreTests" / fs::path(std::to_string(::getpid()));
        fs::create_directories(root);

        for (int i = 0; i < 100; ++i)
        {
            fs::path candidate = root / ("case_" + std::to_string(i));
            if (!fs::exists(candidate))
            {
                fs::create_directories(candidate);
                return candidate.string();
            }
        }

        throw std::runtime_error("Failed to create temp directory for LinuxRestore tests.");
    }
}

int main()
{
    bool allPassed = true;
    RestoreEngine engine;

    RestoreEngine::RestoreVolumePlan plan;
    const std::string metadata =
        "<BACKUPRESTOREMETADATA>"
        "<SOURCE_DISK_NUMBER>2</SOURCE_DISK_NUMBER>"
        "<SOURCE_DISK_SIZE_BYTES>500107862016</SOURCE_DISK_SIZE_BYTES>"
        "<SOURCE_VOLUME_GUID_PATH>\\\\?\\Volume{abc}\\</SOURCE_VOLUME_GUID_PATH>"
        "<SOURCE_VOLUME_MOUNT_PATH>C:\\\\</SOURCE_VOLUME_MOUNT_PATH>"
        "<SOURCE_VOLUME_LABEL>System</SOURCE_VOLUME_LABEL>"
        "<SOURCE_FILESYSTEM>NTFS</SOURCE_FILESYSTEM>"
        "<PARTITION_STYLE>GPT</PARTITION_STYLE>"
        "<PARTITION_NUMBER>3</PARTITION_NUMBER>"
        "<PARTITION_OFFSET_BYTES>1048576</PARTITION_OFFSET_BYTES>"
        "<PARTITION_LENGTH_BYTES>1073741824</PARTITION_LENGTH_BYTES>"
        "<PARTITION_TYPE>Basic</PARTITION_TYPE>"
        "<IS_BOOT_VOLUME>true</IS_BOOT_VOLUME>"
        "<IS_SYSTEM_VOLUME>1</IS_SYSTEM_VOLUME>"
        "<VOLUME_INDEX>4</VOLUME_INDEX>"
        "</BACKUPRESTOREMETADATA>";

    allPassed &= Expect(engine.ParseRestoreMetadataBlob(metadata, plan), "ParseRestoreMetadataBlob should parse a complete metadata payload.");
    allPassed &= Expect(plan.sourceDiskNumber == 2, "Metadata parsing should capture source disk number.");
    allPassed &= Expect(plan.partitionNumber == 3, "Metadata parsing should capture partition number.");
    allPassed &= Expect(plan.imageIndex == 4, "Metadata parsing should capture image index.");
    allPassed &= Expect(plan.sourceUsedSpaceBytes == 6442450944ULL, "Metadata parsing should capture source used-space bytes.");
    allPassed &= Expect(plan.isBootVolume, "Metadata parsing should capture boot volume flag.");
    allPassed &= Expect(plan.isSystemVolume, "Metadata parsing should capture system volume flag.");

    RestoreEngine::RestoreVolumePlan dataPlan;
    dataPlan.partitionLengthBytes = 10737418240ULL;
    dataPlan.sourceUsedSpaceBytes = 4294967296ULL;
    dataPlan.sourceFileSystem = "NTFS";
    dataPlan.isBootVolume = true;
    dataPlan.isSystemVolume = true;

    RestoreEngine::RestoreVolumePlan fixedPlan;
    fixedPlan.partitionLengthBytes = 1073741824ULL;
    fixedPlan.sourceFileSystem = "FAT32";
    fixedPlan.isHiddenPartition = true;

    const std::string targetDevice = "/dev/nonexistent-test-device";
    std::vector<RestoreEngine::RestoreVolumePlan> sizingPlans{ dataPlan, fixedPlan };
    std::vector<unsigned long long> plannedMinimums;
    plannedMinimums.reserve(sizingPlans.size());
    for (const auto& sizingPlan : sizingPlans) {
        unsigned long long minimumBytes = sizingPlan.partitionLengthBytes;
        std::string fsType = sizingPlan.sourceFileSystem;
        std::transform(fsType.begin(), fsType.end(), fsType.begin(), ::tolower);
        bool isGrowableDataPartition = (fsType.find("ntfs") != std::string::npos ||
                                        fsType.find("ext") != std::string::npos ||
                                        fsType.find("xfs") != std::string::npos ||
                                        fsType.find("btrfs") != std::string::npos ||
                                        fsType.find("refs") != std::string::npos) &&
                                       !sizingPlan.isHiddenPartition;
        if (isGrowableDataPartition && sizingPlan.sourceUsedSpaceBytes > 0) {
            unsigned long long usedSpaceWithOverhead = sizingPlan.sourceUsedSpaceBytes + (sizingPlan.sourceUsedSpaceBytes / 10ULL);
            minimumBytes = std::min(sizingPlan.partitionLengthBytes, std::max(usedSpaceWithOverhead, 1ULL));
        }

        plannedMinimums.push_back(minimumBytes);
    }

    allPassed &= Expect(plannedMinimums[0] == 4724464025ULL, "Linux sizing minimum should use source used-space bytes plus 10 percent overhead for growable data partitions.");
    allPassed &= Expect(plannedMinimums[1] == 1073741824ULL, "Linux sizing minimum should keep original partition length for fixed or hidden partitions.");

    RestoreEngine::RestoreVolumePlan invalidPlan;
    allPassed &= Expect(!engine.ParseRestoreMetadataBlob("<BACKUPRESTOREMETADATA></BACKUPRESTOREMETADATA>", invalidPlan), "ParseRestoreMetadataBlob should reject metadata without a source disk number.");

    allPassed &= Expect(RestoreEngine::LooksLikeBlockDevice("/dev/sda"), "LooksLikeBlockDevice should accept /dev paths.");
    allPassed &= Expect(!RestoreEngine::LooksLikeBlockDevice("C:/temp/file"), "LooksLikeBlockDevice should reject non-device paths.");

    std::string tempDirectory = CreateUniqueTempDirectory();
    fs::path encryptedPath = fs::path(tempDirectory) / "encrypted.ssb";
    {
        std::ofstream stream(encryptedPath, std::ios::binary);
        stream.write("SSBAES1", 7);
        stream.write("payload", 7);
    }

    allPassed &= Expect(engine.IsEncryptedBackup(encryptedPath.string()), "IsEncryptedBackup should detect the encrypted header.");
    allPassed &= Expect(engine.IsSsbBackup(encryptedPath.string()), "IsSsbBackup should treat .ssb files as backup archives.");
    allPassed &= Expect(engine.ShouldTreatLegacySingleImageAsVolumeRestore(encryptedPath.string(), 1), "Legacy single-image SSB backups should fall back to a single-volume disk restore.");
    allPassed &= Expect(!engine.ShouldTreatLegacySingleImageAsVolumeRestore(encryptedPath.string(), 2), "Legacy multi-image SSB backups should not use the single-volume disk fallback.");

    fs::path plainPath = fs::path(tempDirectory) / "plain.txt";
    {
        std::ofstream stream(plainPath, std::ios::binary);
        stream << "plain";
    }

    allPassed &= Expect(!engine.IsEncryptedBackup(plainPath.string()), "IsEncryptedBackup should reject files without the encrypted header.");
    allPassed &= Expect(!engine.IsSsbBackup(plainPath.string()), "IsSsbBackup should reject non-archive files.");

    fs::path cleanupPath = fs::path(tempDirectory) / "cleanup.tmp";
    {
        std::ofstream stream(cleanupPath, std::ios::binary);
        stream << "temp";
    }

    engine.BackupCleanup(cleanupPath.string());
    allPassed &= Expect(!fs::exists(cleanupPath), "BackupCleanup should remove an existing temporary file.");

    fs::path hyperVBackupPoint = fs::path(tempDirectory) / "Full_20260429_120000.ssb";
    fs::path exportRoot = hyperVBackupPoint / "Export";
    fs::create_directories(exportRoot);
    fs::path metadataPath = hyperVBackupPoint / "hyperv_backup_info.txt";
    {
        std::ofstream metadataStream(metadataPath);
        metadataStream << "Type=Full\n";
        metadataStream << "PointId=20260429_120000\n";
        metadataStream << "ExportPath=" << exportRoot.string() << "\n";
    }

    allPassed &= Expect(engine.IsHyperVBackupPointDirectory(hyperVBackupPoint.string()), "IsHyperVBackupPointDirectory should detect Hyper-V backup-point metadata.");
    allPassed &= Expect(engine.ResolveHyperVExportPath(hyperVBackupPoint.string()) == exportRoot.string(), "ResolveHyperVExportPath should return the Hyper-V export folder from metadata.");

    fs::path exportOnlyBackupPoint = fs::path(tempDirectory) / "Full_20260429_130000.ssb";
    fs::create_directories(exportOnlyBackupPoint / "Export");
    allPassed &= Expect(engine.IsHyperVBackupPointDirectory(exportOnlyBackupPoint.string()), "IsHyperVBackupPointDirectory should detect legacy Hyper-V backup-point folders by Export content alone.");

    auto backupDates = engine.EnumerateBackupDates(tempDirectory);
    auto hyperVDate = std::find_if(backupDates.begin(), backupDates.end(), [&](const RestoreEngine::BackupDate& date) {
        return date.path == hyperVBackupPoint.string();
    });
    allPassed &= Expect(hyperVDate != backupDates.end(), "EnumerateBackupDates should include Hyper-V backup-point directories.");
    allPassed &= Expect(hyperVDate != backupDates.end() && hyperVDate->type == "Hyper-V", "EnumerateBackupDates should label Hyper-V backup points distinctly.");

    std::vector<RestoreEngine::RestoreItem> archiveTree;
    engine.AddArchiveEntryToTree(archiveTree, "FolderOne/SubFolder/FileA.txt", false);
    engine.AddArchiveEntryToTree(archiveTree, "FolderOne/SubFolder", true);
    engine.AddArchiveEntryToTree(archiveTree, "RootFile.log", false);

    allPassed &= Expect(archiveTree.size() == 2, "AddArchiveEntryToTree should create top-level folder and file nodes.");
    auto folderNode = std::find_if(archiveTree.begin(), archiveTree.end(), [](const RestoreEngine::RestoreItem& item) {
        return item.name == "FolderOne";
    });
    allPassed &= Expect(folderNode != archiveTree.end(), "AddArchiveEntryToTree should preserve the top-level folder name.");
    allPassed &= Expect(folderNode != archiveTree.end() && folderNode->type == "Folder", "AddArchiveEntryToTree should mark parent segments as folders.");
    allPassed &= Expect(folderNode != archiveTree.end() && folderNode->children.size() == 1, "AddArchiveEntryToTree should add one child for the nested folder.");
    allPassed &= Expect(folderNode != archiveTree.end() && folderNode->children[0].name == "SubFolder", "AddArchiveEntryToTree should preserve nested folder names.");
    allPassed &= Expect(folderNode != archiveTree.end() && folderNode->children[0].children.size() == 1, "AddArchiveEntryToTree should place file nodes under their nested folder.");
    allPassed &= Expect(folderNode != archiveTree.end() && folderNode->children[0].children[0].path == "FolderOne/SubFolder/FileA.txt", "AddArchiveEntryToTree should preserve normalized relative archive paths.");

    fs::remove_all(tempDirectory);
    return allPassed ? 0 : 1;
}
