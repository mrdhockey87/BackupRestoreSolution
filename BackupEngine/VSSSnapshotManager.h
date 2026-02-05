#pragma once
#include <windows.h>
#include <vss.h>
#include <vswriter.h>
#include <vsbackup.h>
#include <string>
#include <vector>

#pragma comment(lib, "VssApi.lib")
#pragma comment(lib, "ole32.lib")

namespace BackupEngine {

    // VSS Snapshot Manager - Production Ready
    class VSSSnapshotManager {
    private:
        IVssBackupComponents* pBackupComponents;
        VSS_ID snapshotSetId;
        std::vector<VSS_ID> snapshotIds;
        bool isInitialized;
        bool snapshotCreated;

    public:
        VSSSnapshotManager();
        ~VSSSnapshotManager();

        // Initialize VSS subsystem
        HRESULT Initialize();

        // Create snapshot of a volume
        HRESULT CreateVolumeSnapshot(
            const wchar_t* volumePath,
            wchar_t* snapshotDevicePath,
            DWORD pathSize
        );

        // Create snapshots of multiple volumes
        HRESULT CreateMultiVolumeSnapshot(
            const std::vector<std::wstring>& volumePaths,
            std::vector<std::wstring>& snapshotPaths
        );

        // Get snapshot path for a volume
        bool GetSnapshotPath(const wchar_t* originalPath, std::wstring& snapshotPath);

        // Complete and cleanup
        HRESULT Complete();
        void Cleanup();

        // Get last error message
        std::wstring GetLastError() const;

    private:
        std::wstring lastError;

        // Helper: Wait for async operation
        HRESULT WaitForAsync(IVssAsync* pAsync);

        // Helper: Check if volume needs snapshot
        bool IsVolumeSnapshotNeeded(const wchar_t* volumePath);
    };

} // namespace BackupEngine
