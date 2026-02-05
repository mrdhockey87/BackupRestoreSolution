#include "VSSSnapshotManager.h"
#include <comdef.h>
#include <sstream>

namespace BackupEngine {

    VSSSnapshotManager::VSSSnapshotManager()
        : pBackupComponents(nullptr)
        , snapshotSetId(GUID_NULL)
        , isInitialized(false)
        , snapshotCreated(false)
    {
    }

    VSSSnapshotManager::~VSSSnapshotManager() {
        Cleanup();
    }

    HRESULT VSSSnapshotManager::Initialize() {
        if (isInitialized) {
            return S_OK;
        }

        // Initialize COM
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) {
            lastError = L"Failed to initialize COM";
            return hr;
        }

        // Initialize security for VSS
        hr = CoInitializeSecurity(
            nullptr,
            -1,
            nullptr,
            nullptr,
            RPC_C_AUTHN_LEVEL_PKT_PRIVACY,
            RPC_C_IMP_LEVEL_IDENTIFY,
            nullptr,
            EOAC_NONE,
            nullptr
        );

        if (FAILED(hr) && hr != RPC_E_TOO_LATE) {
            lastError = L"Failed to initialize COM security";
            CoUninitialize();
            return hr;
        }

        // Create VSS backup components
        hr = CreateVssBackupComponents(&pBackupComponents);
        if (FAILED(hr)) {
            lastError = L"Failed to create VSS backup components";
            CoUninitialize();
            return hr;
        }

        // Initialize for backup
        hr = pBackupComponents->InitializeForBackup();
        if (FAILED(hr)) {
            lastError = L"Failed to initialize VSS for backup";
            pBackupComponents->Release();
            pBackupComponents = nullptr;
            CoUninitialize();
            return hr;
        }

        // Set context (standard backup)
        hr = pBackupComponents->SetContext(VSS_CTX_BACKUP);
        if (FAILED(hr)) {
            lastError = L"Failed to set VSS context";
            pBackupComponents->Release();
            pBackupComponents = nullptr;
            CoUninitialize();
            return hr;
        }

        // Set backup state
        hr = pBackupComponents->SetBackupState(
            TRUE,           // Select components
            TRUE,           // Bootable system state
            VSS_BT_FULL,    // Full backup
            FALSE           // Not partial file support
        );

        if (FAILED(hr)) {
            lastError = L"Failed to set backup state";
            pBackupComponents->Release();
            pBackupComponents = nullptr;
            CoUninitialize();
            return hr;
        }

        isInitialized = true;
        return S_OK;
    }

    HRESULT VSSSnapshotManager::CreateVolumeSnapshot(
        const wchar_t* volumePath,
        wchar_t* snapshotDevicePath,
        DWORD pathSize
    ) {
        if (!isInitialized) {
            HRESULT hr = Initialize();
            if (FAILED(hr)) {
                return hr;
            }
        }

        // Start snapshot set if not already started
        if (snapshotSetId == GUID_NULL) {
            HRESULT hr = pBackupComponents->StartSnapshotSet(&snapshotSetId);
            if (FAILED(hr)) {
                lastError = L"Failed to start snapshot set";
                return hr;
            }
        }

        // Add volume to snapshot set
        VSS_ID snapshotId;
        HRESULT hr = pBackupComponents->AddToSnapshotSet(
            const_cast<wchar_t*>(volumePath),
            GUID_NULL,
            &snapshotId
        );

        if (FAILED(hr)) {
            std::wstringstream ss;
            ss << L"Failed to add volume to snapshot set: " << volumePath;
            lastError = ss.str();
            return hr;
        }

        snapshotIds.push_back(snapshotId);

        // Prepare for backup (gather writer metadata)
        IVssAsync* pAsync = nullptr;
        hr = pBackupComponents->PrepareForBackup(&pAsync);
        if (FAILED(hr)) {
            lastError = L"Failed to prepare for backup";
            return hr;
        }

        hr = WaitForAsync(pAsync);
        if (FAILED(hr)) {
            lastError = L"Prepare for backup failed";
            return hr;
        }

        // Create the shadow copies
        pAsync = nullptr;
        hr = pBackupComponents->DoSnapshotSet(&pAsync);
        if (FAILED(hr)) {
            lastError = L"Failed to create shadow copy";
            return hr;
        }

        hr = WaitForAsync(pAsync);
        if (FAILED(hr)) {
            lastError = L"Shadow copy creation failed";
            return hr;
        }

        snapshotCreated = true;

        // Get snapshot properties
        VSS_SNAPSHOT_PROP prop;
        hr = pBackupComponents->GetSnapshotProperties(snapshotId, &prop);
        if (FAILED(hr)) {
            lastError = L"Failed to get snapshot properties";
            return hr;
        }

        // Copy snapshot device path
        wcscpy_s(snapshotDevicePath, pathSize, prop.m_pwszSnapshotDeviceObject);

        // Free snapshot properties
        VssFreeSnapshotProperties(&prop);

        return S_OK;
    }

    HRESULT VSSSnapshotManager::CreateMultiVolumeSnapshot(
        const std::vector<std::wstring>& volumePaths,
        std::vector<std::wstring>& snapshotPaths
    ) {
        if (!isInitialized) {
            HRESULT hr = Initialize();
            if (FAILED(hr)) {
                return hr;
            }
        }

        snapshotPaths.clear();

        // Start snapshot set
        HRESULT hr = pBackupComponents->StartSnapshotSet(&snapshotSetId);
        if (FAILED(hr)) {
            lastError = L"Failed to start snapshot set";
            return hr;
        }

        // Add all volumes to snapshot set
        for (const auto& volumePath : volumePaths) {
            VSS_ID snapshotId;
            hr = pBackupComponents->AddToSnapshotSet(
                const_cast<wchar_t*>(volumePath.c_str()),
                GUID_NULL,
                &snapshotId
            );

            if (FAILED(hr)) {
                std::wstringstream ss;
                ss << L"Failed to add volume to snapshot set: " << volumePath;
                lastError = ss.str();
                return hr;
            }

            snapshotIds.push_back(snapshotId);
        }

        // Prepare for backup
        IVssAsync* pAsync = nullptr;
        hr = pBackupComponents->PrepareForBackup(&pAsync);
        if (FAILED(hr)) {
            lastError = L"Failed to prepare for backup";
            return hr;
        }

        hr = WaitForAsync(pAsync);
        if (FAILED(hr)) {
            lastError = L"Prepare for backup failed";
            return hr;
        }

        // Create all shadow copies atomically
        pAsync = nullptr;
        hr = pBackupComponents->DoSnapshotSet(&pAsync);
        if (FAILED(hr)) {
            lastError = L"Failed to create shadow copies";
            return hr;
        }

        hr = WaitForAsync(pAsync);
        if (FAILED(hr)) {
            lastError = L"Shadow copy creation failed";
            return hr;
        }

        snapshotCreated = true;

        // Get all snapshot paths
        for (const auto& snapshotId : snapshotIds) {
            VSS_SNAPSHOT_PROP prop;
            hr = pBackupComponents->GetSnapshotProperties(snapshotId, &prop);
            if (FAILED(hr)) {
                lastError = L"Failed to get snapshot properties";
                return hr;
            }

            snapshotPaths.push_back(prop.m_pwszSnapshotDeviceObject);
            VssFreeSnapshotProperties(&prop);
        }

        return S_OK;
    }

    bool VSSSnapshotManager::GetSnapshotPath(
        const wchar_t* originalPath,
        std::wstring& snapshotPath
    ) {
        if (!snapshotCreated || snapshotIds.empty()) {
            return false;
        }

        // Extract volume from original path
        wchar_t volumeRoot[MAX_PATH];
        if (!GetVolumePathNameW(originalPath, volumeRoot, MAX_PATH)) {
            return false;
        }

        // Find matching snapshot
        for (const auto& snapshotId : snapshotIds) {
            VSS_SNAPSHOT_PROP prop;
            HRESULT hr = pBackupComponents->GetSnapshotProperties(snapshotId, &prop);
            
            if (SUCCEEDED(hr)) {
                // Check if this snapshot is for our volume
                if (_wcsicmp(prop.m_pwszOriginalVolumeName, volumeRoot) == 0) {
                    // Build snapshot path
                    std::wstring devicePath = prop.m_pwszSnapshotDeviceObject;
                    std::wstring relativePath = originalPath + wcslen(volumeRoot);
                    
                    snapshotPath = devicePath;
                    if (!relativePath.empty() && relativePath[0] != L'\\') {
                        snapshotPath += L"\\";
                    }
                    snapshotPath += relativePath;

                    VssFreeSnapshotProperties(&prop);
                    return true;
                }

                VssFreeSnapshotProperties(&prop);
            }
        }

        return false;
    }

    HRESULT VSSSnapshotManager::Complete() {
        if (!pBackupComponents) {
            return S_OK;
        }

        // Signal backup complete to writers
        IVssAsync* pAsync = nullptr;
        HRESULT hr = pBackupComponents->BackupComplete(&pAsync);
        
        if (SUCCEEDED(hr) && pAsync) {
            WaitForAsync(pAsync);
        }

        return hr;
    }

    void VSSSnapshotManager::Cleanup() {
        if (pBackupComponents) {
            // Signal backup complete
            Complete();

            // Delete snapshot set
            if (snapshotSetId != GUID_NULL) {
                LONG deletedSnapshots = 0;
                VSS_ID undeletedSnapshotId;
                pBackupComponents->DeleteSnapshots(
                    snapshotSetId,
                    VSS_OBJECT_SNAPSHOT_SET,
                    TRUE,
                    &deletedSnapshots,
                    &undeletedSnapshotId
                );
            }

            pBackupComponents->Release();
            pBackupComponents = nullptr;
        }

        snapshotIds.clear();
        snapshotSetId = GUID_NULL;
        isInitialized = false;
        snapshotCreated = false;

        CoUninitialize();
    }

    std::wstring VSSSnapshotManager::GetLastError() const {
        return lastError;
    }

    HRESULT VSSSnapshotManager::WaitForAsync(IVssAsync* pAsync) {
        if (!pAsync) {
            return E_POINTER;
        }

        HRESULT hr = S_OK;
        
        // Wait for the async operation
        hr = pAsync->Wait();
        
        if (SUCCEEDED(hr)) {
            // Check the final status
            HRESULT hrStatus;
            hr = pAsync->QueryStatus(&hrStatus, nullptr);
            
            if (SUCCEEDED(hr)) {
                hr = hrStatus;
            }
        }

        pAsync->Release();
        return hr;
    }

    bool VSSSnapshotManager::IsVolumeSnapshotNeeded(const wchar_t* volumePath) {
        // Check if volume is NTFS (VSS works best with NTFS)
        wchar_t volumeRoot[MAX_PATH];
        wchar_t fileSystem[MAX_PATH];
        
        if (!GetVolumePathNameW(volumePath, volumeRoot, MAX_PATH)) {
            return false;
        }

        if (!GetVolumeInformationW(
            volumeRoot,
            nullptr, 0,
            nullptr,
            nullptr,
            nullptr,
            fileSystem,
            MAX_PATH
        )) {
            return false;
        }

        // VSS works with NTFS, ReFS
        return (_wcsicmp(fileSystem, L"NTFS") == 0 || 
                _wcsicmp(fileSystem, L"ReFS") == 0);
    }

} // namespace BackupEngine
