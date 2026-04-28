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
    allPassed &= Expect(plan.isBootVolume, "Metadata parsing should capture boot volume flag.");
    allPassed &= Expect(plan.isSystemVolume, "Metadata parsing should capture system volume flag.");

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

    fs::remove_all(tempDirectory);
    return allPassed ? 0 : 1;
}
