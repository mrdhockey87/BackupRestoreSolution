// VSS Headers needed
#include <vss.h>
#include <vswriter.h>
#include <vsbackup.h>

#pragma comment(lib, "VssApi.lib")

class VSSManager {
private:
    IVssBackupComponents* pBackup;
    VSS_ID snapshotSetId;

public:
    HRESULT Initialize() {
        CoInitialize(NULL);
        return CreateVssBackupComponents(&pBackup);
    }

    HRESULT CreateSnapshot(const wchar_t* volumePath,
        wchar_t* snapshotPath,
        DWORD pathSize) {
        // Initialize VSS
        pBackup->InitializeForBackup();
        pBackup->SetBackupState(true, true, VSS_BT_FULL);

        // Start snapshot set
        pBackup->StartSnapshotSet(&snapshotSetId);

        VSS_ID snapshotId;
        pBackup->AddToSnapshotSet((VSS_PWSZ)volumePath,
            GUID_NULL,
            &snapshotId);

        // Execute the snapshot
        HRESULT hr;
        IVssAsync* pAsync = nullptr;
        hr = pBackup->PrepareForBackup(&pAsync);
        if (SUCCEEDED(hr) && pAsync) {
            pAsync->Wait();
            pAsync->Release();
        }

        hr = pBackup->DoSnapshotSet(&pAsync);
        if (SUCCEEDED(hr) && pAsync) {
            pAsync->Wait();
            pAsync->Release();
        }

        // Get snapshot properties
        VSS_SNAPSHOT_PROP prop;
        pBackup->GetSnapshotProperties(snapshotId, &prop);
        wcscpy_s(snapshotPath, pathSize, prop.m_pwszSnapshotDeviceObject);

        VssFreeSnapshotProperties(&prop);
        return S_OK;
    }

    void Cleanup() {
        if (pBackup) {
            IVssAsync* pAsync = nullptr;
            HRESULT hr = pBackup->BackupComplete(&pAsync);
            if (SUCCEEDED(hr) && pAsync) {
                pAsync->Wait();
                pAsync->Release();
            }
            pBackup->Release();
        }
        CoUninitialize();
    }
};

