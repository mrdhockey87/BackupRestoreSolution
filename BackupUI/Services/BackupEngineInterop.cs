using System.Runtime.InteropServices;
using System.Text;
using SecureServerBackupCommon;

namespace SecureServerBackup.Services
{
    /// <summary>
    /// Type forwarder to the common BackupEngineInterop in SecureServerBackupCommon.
    /// This allows existing UI code to continue using SecureServerBackup.Services.BackupEngineInterop.
    /// </summary>
    public class BackupEngineInterop : SecureServerBackupCommon.BackupEngineInterop
    {
        // All methods and delegates are inherited from SecureServerBackupCommon.BackupEngineInterop

        // All methods and delegates are inherited from SecureServerBackupCommon.BackupEngineInterop
    }
}
