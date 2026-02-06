#include "BrsFileManager.h"
#include <algorithm> 
#include <fstream>
#include <zlib.h>
#include <wimgapi.h>

#pragma comment(lib, "wimgapi.lib")
//#pragma comment(lib, "zlib.lib")

namespace BackupEngine {

    bool BrsFileManager::CreateBrsFromWim(
        const wchar_t* wimPath,
        const wchar_t* brsPath,
        const wchar_t* backupName,
        const wchar_t* backupType,
        bool compress,
        wchar_t* errorMsg,
        int errorMsgSize,
        void (*progressCallback)(int percent)
    ) {
        try {
            // Validate WIM file first
            if (!IsValidWim(wimPath)) {
                swprintf_s(errorMsg, errorMsgSize, L"Invalid WIM file");
                return false;
            }

            // Get WIM size
            WIN32_FILE_ATTRIBUTE_DATA fileInfo;
            if (!GetFileAttributesExW(wimPath, GetFileExInfoStandard, &fileInfo)) {
                swprintf_s(errorMsg, errorMsgSize, L"Cannot get WIM file info: %d", GetLastError());
                return false;
            }

            ULARGE_INTEGER fileSize;
            fileSize.LowPart = fileInfo.nFileSizeLow;
            fileSize.HighPart = fileInfo.nFileSizeHigh;

            // Create BRS header
            BrsHeader header = {};
            memcpy(header.magic, "BRS1", 4);
            header.version = 1;
            header.compressionType = compress ? 1 : 0;
            header.originalSize = fileSize.QuadPart;
            wcstombs_s(nullptr, header.backupName, 256, backupName, _TRUNCATE);
            wcstombs_s(nullptr, header.backupType, 64, backupType, _TRUNCATE);
            GetSystemTime(&header.timestamp);

            // If compression requested, compress WIM to temp file
            std::wstring dataFile = wimPath;
            if (compress) {
                std::wstring tempPath = std::wstring(brsPath) + L".tmp";
                
                if (progressCallback) progressCallback(10);
                
                if (!CompressFile(wimPath, tempPath.c_str(), progressCallback)) {
                    swprintf_s(errorMsg, errorMsgSize, L"Compression failed");
                    return false;
                }
                
                dataFile = tempPath;

                // Get compressed size
                if (!GetFileAttributesExW(dataFile.c_str(), GetFileExInfoStandard, &fileInfo)) {
                    DeleteFileW(dataFile.c_str());
                    swprintf_s(errorMsg, errorMsgSize, L"Cannot get compressed file info");
                    return false;
                }

                fileSize.LowPart = fileInfo.nFileSizeLow;
                fileSize.HighPart = fileInfo.nFileSizeHigh;
                header.compressedSize = fileSize.QuadPart;
            }
            else {
                header.compressedSize = header.originalSize;
            }

            // Calculate checksums
            header.dataChecksum = CalculateFileCRC64(dataFile.c_str());
            header.headerChecksum = CalculateCRC64(&header, offsetof(BrsHeader, headerChecksum));

            // Write BRS file
            std::ofstream brsFile(brsPath, std::ios::binary);
            if (!brsFile) {
                if (compress) DeleteFileW(dataFile.c_str());
                swprintf_s(errorMsg, errorMsgSize, L"Cannot create BRS file");
                return false;
            }

            // Write header
            brsFile.write(reinterpret_cast<const char*>(&header), sizeof(BrsHeader));

            // Copy data (compressed or original WIM)
            std::ifstream dataStream(dataFile, std::ios::binary);
            if (!dataStream) {
                if (compress) DeleteFileW(dataFile.c_str());
                swprintf_s(errorMsg, errorMsgSize, L"Cannot read data file");
                return false;
            }

            const size_t bufferSize = 1024 * 1024; // 1MB buffer
            std::vector<char> buffer(bufferSize);
            uint64_t totalWritten = 0;

            while (dataStream.read(buffer.data(), bufferSize) || dataStream.gcount() > 0) {
                brsFile.write(buffer.data(), dataStream.gcount());
                totalWritten += dataStream.gcount();

                if (progressCallback) {
                    int percent = static_cast<int>((totalWritten * 100) / header.compressedSize);
                    progressCallback(percent);
                }
            }

            brsFile.close();
            dataStream.close();

            // Cleanup temp file if compressed
            if (compress) {
                DeleteFileW(dataFile.c_str());
            }

            if (progressCallback) progressCallback(100);

            return true;
        }
        catch (...) {
            swprintf_s(errorMsg, errorMsgSize, L"Exception creating BRS file");
            return false;
        }
    }

    bool BrsFileManager::ExtractWimFromBrs(
        const wchar_t* brsPath,
        const wchar_t* wimPath,
        wchar_t* errorMsg,
        int errorMsgSize,
        void (*progressCallback)(int percent)
    ) {
        try {
            // Read header
            BrsHeader header;
            if (!ReadBrsHeader(brsPath, &header)) {
                swprintf_s(errorMsg, errorMsgSize, L"Invalid BRS file header");
                return false;
            }

            // Open BRS file
            std::ifstream brsFile(brsPath, std::ios::binary);
            if (!brsFile) {
                swprintf_s(errorMsg, errorMsgSize, L"Cannot open BRS file");
                return false;
            }

            // Skip header
            brsFile.seekg(sizeof(BrsHeader));

            // Extract data
            if (header.compressionType == 0) {
                // No compression - direct copy
                std::ofstream wimFile(wimPath, std::ios::binary);
                if (!wimFile) {
                    swprintf_s(errorMsg, errorMsgSize, L"Cannot create WIM file");
                    return false;
                }

                const size_t bufferSize = 1024 * 1024;
                std::vector<char> buffer(bufferSize);
                uint64_t totalRead = 0;

                while (brsFile.read(buffer.data(), bufferSize) || brsFile.gcount() > 0) {
                    wimFile.write(buffer.data(), brsFile.gcount());
                    totalRead += brsFile.gcount();

                    if (progressCallback) {
                        int percent = static_cast<int>((totalRead * 100) / header.compressedSize);
                        progressCallback(percent);
                    }
                }

                wimFile.close();
            }
            else if (header.compressionType == 1) {
                // LZMA/ZIP compressed - extract to temp then decompress
                std::wstring tempPath = std::wstring(wimPath) + L".tmp";

                std::ofstream tempFile(tempPath, std::ios::binary);
                if (!tempFile) {
                    swprintf_s(errorMsg, errorMsgSize, L"Cannot create temp file");
                    return false;
                }

                const size_t bufferSize = 1024 * 1024;
                std::vector<char> buffer(bufferSize);

                while (brsFile.read(buffer.data(), bufferSize) || brsFile.gcount() > 0) {
                    tempFile.write(buffer.data(), brsFile.gcount());
                }

                tempFile.close();
                brsFile.close();

                // Decompress
                if (!DecompressFile(tempPath.c_str(), wimPath, progressCallback)) {
                    DeleteFileW(tempPath.c_str());
                    swprintf_s(errorMsg, errorMsgSize, L"Decompression failed");
                    return false;
                }

                DeleteFileW(tempPath.c_str());
            }
            else {
                swprintf_s(errorMsg, errorMsgSize, L"Unsupported compression type");
                return false;
            }

            if (progressCallback) progressCallback(100);

            return true;
        }
        catch (...) {
            swprintf_s(errorMsg, errorMsgSize, L"Exception extracting WIM from BRS");
            return false;
        }
    }

    bool BrsFileManager::ValidateBackupFile(
        const wchar_t* filePath,
        bool* isBrsFormat,
        bool* isCompressed,
        BrsHeader* header,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        *isBrsFormat = false;
        *isCompressed = false;

        // Check file extension
        std::wstring path(filePath);
        std::wstring ext = path.substr(path.find_last_of(L".") + 1);
        std::transform(ext.begin(), ext.end(), ext.begin(), ::tolower);

        if (ext == L"brs") {
            // Check if valid BRS
            if (IsValidBrs(filePath)) {
                *isBrsFormat = true;
                
                if (ReadBrsHeader(filePath, header)) {
                    *isCompressed = (header->compressionType != 0);
                    return true;
                }
            }
            swprintf_s(errorMsg, errorMsgSize, L"Invalid .brs file format");
            return false;
        }
        else if (ext == L"wim") {
            // Check if valid WIM
            if (IsValidWim(filePath)) {
                *isBrsFormat = false;
                *isCompressed = false;
                
                // Fill header with basic info
                if (header) {
                    memset(header, 0, sizeof(BrsHeader));
                    strcpy_s(header->backupName, "Windows Server Backup");
                    strcpy_s(header->backupType, "Full");
                    GetSystemTime(&header->timestamp);
                }
                
                return true;
            }
            swprintf_s(errorMsg, errorMsgSize, L"Invalid .wim file format");
            return false;
        }

        swprintf_s(errorMsg, errorMsgSize, L"Unsupported file format (must be .brs or .wim)");
        return false;
    }

    bool BrsFileManager::IsValidWim(const wchar_t* wimPath) {
        DWORD creationResult = 0;
        HANDLE wimHandle = WIMCreateFile(
            wimPath,
            WIM_GENERIC_READ,
            WIM_OPEN_EXISTING,
            0,                  // dwFlagsAndAttributes
            0,                  // dwCompressionType
            &creationResult     // result
        );

        if (wimHandle && wimHandle != INVALID_HANDLE_VALUE) {
            WIMCloseHandle(wimHandle);
            return true;
        }


        return false;
    }

    bool BrsFileManager::IsValidBrs(const wchar_t* brsPath) {
        std::ifstream file(brsPath, std::ios::binary);
        if (!file) return false;

        BrsHeader header;
        file.read(reinterpret_cast<char*>(&header), sizeof(BrsHeader));

        if (file.gcount() != sizeof(BrsHeader)) {
            return false;
        }

        // Check magic
        if (memcmp(header.magic, "BRS1", 4) != 0) {
            return false;
        }

        // Verify checksum
        uint64_t calculatedChecksum = CalculateCRC64(&header, offsetof(BrsHeader, headerChecksum));
        if (calculatedChecksum != header.headerChecksum) {
            return false;
        }

        return true;
    }

    bool BrsFileManager::ReadBrsHeader(const wchar_t* brsPath, BrsHeader* header) {
        std::ifstream file(brsPath, std::ios::binary);
        if (!file) return false;

        file.read(reinterpret_cast<char*>(header), sizeof(BrsHeader));
        return file.gcount() == sizeof(BrsHeader) && memcmp(header->magic, "BRS1", 4) == 0;
    }

    bool BrsFileManager::WriteBrsHeader(const wchar_t* brsPath, const BrsHeader* header) {
        std::ofstream file(brsPath, std::ios::binary | std::ios::in | std::ios::out);
        if (!file) return false;

        file.write(reinterpret_cast<const char*>(header), sizeof(BrsHeader));
        return file.good();
    }

    bool BrsFileManager::CompressFile(
        const wchar_t* inputPath,
        const wchar_t* outputPath,
        void (*progressCallback)(int percent)
    ) {
        // Use zlib for compression
        gzFile outFile = gzopen_w(outputPath, "wb9"); // Max compression
        if (!outFile) return false;

        std::ifstream inFile(inputPath, std::ios::binary);
        if (!inFile) {
            gzclose(outFile);
            return false;
        }

        // Get file size for progress
        inFile.seekg(0, std::ios::end);
        uint64_t fileSize = inFile.tellg();
        inFile.seekg(0, std::ios::beg);

        const size_t bufferSize = 1024 * 1024; // 1MB
        std::vector<char> buffer(bufferSize);
        uint64_t totalRead = 0;

        while (inFile.read(buffer.data(), bufferSize) || inFile.gcount() > 0) {
            gzwrite(outFile, buffer.data(), static_cast<unsigned>(inFile.gcount()));
            totalRead += inFile.gcount();

            if (progressCallback) {
                int percent = static_cast<int>((totalRead * 100) / fileSize);
                progressCallback(percent);
            }
        }

        gzclose(outFile);
        inFile.close();

        return true;
    }

    bool BrsFileManager::DecompressFile(
        const wchar_t* inputPath,
        const wchar_t* outputPath,
        void (*progressCallback)(int percent)
    ) {
        gzFile inFile = gzopen_w(inputPath, "rb");
        if (!inFile) return false;

        std::ofstream outFile(outputPath, std::ios::binary);
        if (!outFile) {
            gzclose(inFile);
            return false;
        }

        const size_t bufferSize = 1024 * 1024;
        std::vector<char> buffer(bufferSize);

        int bytesRead;
        while ((bytesRead = gzread(inFile, buffer.data(), bufferSize)) > 0) {
            outFile.write(buffer.data(), bytesRead);
            
            if (progressCallback) {
                // Can't calculate exact progress without knowing uncompressed size
                // Just pulse the progress
                static int pulse = 0;
                progressCallback(50 + (pulse++ % 40));
            }
        }

        gzclose(inFile);
        outFile.close();

        return true;
    }

    uint64_t BrsFileManager::CalculateCRC64(const void* data, size_t length) {
        // Simple CRC64 implementation
        static const uint64_t poly = 0x42F0E1EBA9EA3693ULL;
        uint64_t crc = 0xFFFFFFFFFFFFFFFFULL;

        const uint8_t* bytes = static_cast<const uint8_t*>(data);
        for (size_t i = 0; i < length; i++) {
            crc ^= static_cast<uint64_t>(bytes[i]);
            for (int j = 0; j < 8; j++) {
                if (crc & 1) {
                    crc = (crc >> 1) ^ poly;
                }
                else {
                    crc >>= 1;
                }
            }
        }

        return crc ^ 0xFFFFFFFFFFFFFFFFULL;
    }

    uint64_t BrsFileManager::CalculateFileCRC64(const wchar_t* filePath) {
        std::ifstream file(filePath, std::ios::binary);
        if (!file) return 0;

        uint64_t crc = 0xFFFFFFFFFFFFFFFFULL;
        const size_t bufferSize = 1024 * 1024;
        std::vector<char> buffer(bufferSize);

        while (file.read(buffer.data(), bufferSize) || file.gcount() > 0) {
            for (std::streamsize i = 0; i < file.gcount(); i++) {
                crc ^= static_cast<uint64_t>(static_cast<uint8_t>(buffer[i]));
                for (int j = 0; j < 8; j++) {
                    if (crc & 1) {
                        crc = (crc >> 1) ^ 0x42F0E1EBA9EA3693ULL;
                    }
                    else {
                        crc >>= 1;
                    }
                }
            }
        }

        return crc ^ 0xFFFFFFFFFFFFFFFFULL;
    }

    bool BrsFileManager::GetBackupInfo(
        const wchar_t* filePath,
        wchar_t* backupName,
        int backupNameSize,
        wchar_t* backupType,
        int backupTypeSize,
        SYSTEMTIME* timestamp,
        uint64_t* originalSize,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        BrsHeader header;
        bool isBrs, isCompressed;

        if (!ValidateBackupFile(filePath, &isBrs, &isCompressed, &header, errorMsg, errorMsgSize)) {
            return false;
        }

        mbstowcs_s(nullptr, backupName, backupNameSize, header.backupName, _TRUNCATE);
        mbstowcs_s(nullptr, backupType, backupTypeSize, header.backupType, _TRUNCATE);
        
        if (timestamp) {
            *timestamp = header.timestamp;
        }

        if (originalSize) {
            *originalSize = header.originalSize;
        }

        return true;
    }

} // namespace BackupEngine
