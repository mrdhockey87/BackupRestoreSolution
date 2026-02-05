#pragma once
#include <windows.h>
#include <string>
#include <vector>

namespace BackupEngine {

    // BRS File Format Header (to identify our backups)
    #pragma pack(push, 1)
    struct BrsHeader {
        char magic[4];              // "BRS1" - Backup Restore System v1
        uint32_t version;           // Format version (1)
        uint32_t compressionType;   // 0=None, 1=LZMA, 2=ZIP
        uint64_t originalSize;      // Original WIM size
        uint64_t compressedSize;    // Compressed size (or same if uncompressed)
        uint64_t headerChecksum;    // CRC64 of header
        uint64_t dataChecksum;      // CRC64 of compressed data
        char backupName[256];       // Backup job name
        char backupType[64];        // Full/Incremental/Differential
        SYSTEMTIME timestamp;       // Creation time
        char reserved[128];         // Future use
    };
    #pragma pack(pop)

    class BrsFileManager {
    public:
        // Create a .brs file from WIM
        static bool CreateBrsFromWim(
            const wchar_t* wimPath,
            const wchar_t* brsPath,
            const wchar_t* backupName,
            const wchar_t* backupType,
            bool compress,
            wchar_t* errorMsg,
            int errorMsgSize,
            void (*progressCallback)(int percent) = nullptr
        );

        // Extract WIM from .brs file (for mounting/restoring)
        static bool ExtractWimFromBrs(
            const wchar_t* brsPath,
            const wchar_t* wimPath,
            wchar_t* errorMsg,
            int errorMsgSize,
            void (*progressCallback)(int percent) = nullptr
        );

        // Validate a backup file (.brs or .wim)
        static bool ValidateBackupFile(
            const wchar_t* filePath,
            bool* isBrsFormat,      // OUT: true if .brs, false if .wim
            bool* isCompressed,     // OUT: true if compressed
            BrsHeader* header,      // OUT: header info (if .brs)
            wchar_t* errorMsg,
            int errorMsgSize
        );

        // Get backup info without extracting
        static bool GetBackupInfo(
            const wchar_t* filePath,
            wchar_t* backupName,
            int backupNameSize,
            wchar_t* backupType,
            int backupTypeSize,
            SYSTEMTIME* timestamp,
            uint64_t* originalSize,
            wchar_t* errorMsg,
            int errorMsgSize
        );

        // Check if file is a valid WIM
        static bool IsValidWim(const wchar_t* wimPath);

        // Check if file is a valid .brs
        static bool IsValidBrs(const wchar_t* brsPath);

    private:
        // Compression helpers
        static bool CompressFile(
            const wchar_t* inputPath,
            const wchar_t* outputPath,
            void (*progressCallback)(int percent)
        );

        static bool DecompressFile(
            const wchar_t* inputPath,
            const wchar_t* outputPath,
            void (*progressCallback)(int percent)
        );

        // CRC calculation
        static uint64_t CalculateCRC64(const void* data, size_t length);
        static uint64_t CalculateFileCRC64(const wchar_t* filePath);

        // Read/Write BRS header
        static bool ReadBrsHeader(const wchar_t* brsPath, BrsHeader* header);
        static bool WriteBrsHeader(const wchar_t* brsPath, const BrsHeader* header);
    };

} // namespace BackupEngine
