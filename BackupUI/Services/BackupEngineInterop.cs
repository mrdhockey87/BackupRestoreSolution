using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BackupUI.Services
{
    public class BackupEngineInterop
    {
        private const string DllName = "BackupEngine.dll";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ProgressCallback(int percentage, [MarshalAs(UnmanagedType.LPWStr)] string message);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LogCallback(
            int level,
            [MarshalAs(UnmanagedType.LPWStr)] string message,
            [MarshalAs(UnmanagedType.LPWStr)] string details);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int CreateVolumeSnapshot(
            string volume,
            StringBuilder snapshotPath,
            int pathSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int BackupFiles(
            string sourcePath,
            string destPath,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? userExclusions,
            int userExclusionCount,
            ProgressCallback? callback,
            LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int BackupHyperVVM(
            string vmName,
            string destPath,
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int DeleteSnapshot(string snapshotId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int RestoreFiles(
            string sourcePath,
            string destPath,
            bool overwriteExisting,
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int RestoreHyperVVM(
            string backupPath,
            string vmName,
            string vmStoragePath,
            bool startAfterRestore,
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int RestoreSystemState(
            string backupPath,
            string targetVolume,
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int ListBackupContents(
            string backupPath,
            StringBuilder buffer,
            int bufferSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int VerifyBackup(
            string backupPath,
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern void GetLastErrorMessage(
            StringBuilder buffer,
            int bufferSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int BackupVolume(
            string volumePath,
            string destPath,
            bool includeSystemState,
            bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? userExclusions,
            int userExclusionCount,
            ProgressCallback? callback,
            LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int BackupDisk(
            int diskNumber,
            string destPath,
            bool includeSystemState,
            bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? userExclusions,
            int userExclusionCount,
            ProgressCallback? callback,
            LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int CreateIncrementalBackup(
            string sourcePath,
            string destPath,
            string baseBackupPath,
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int CreateDifferentialBackup(
            string sourcePath,
            string destPath,
            string fullBackupPath,
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int EnumerateVolumes(
            StringBuilder buffer,
            int bufferSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int EnumerateDisks(
            StringBuilder buffer,
            int bufferSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int EnumerateHyperVMachines(
            StringBuilder buffer,
            int bufferSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int IsBootVolume(
            string volumePath,
            out bool isBootVolume);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int CreateRecoveryEnvironment(
            string usbDriveLetter,
            string programPath,
            ProgressCallback? callback);

        // New methods for enhanced restore functionality
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int EnumerateBackupDates(
            string backupPath,
            StringBuilder buffer,
            int bufferSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int RestoreWithManifest(
            string backupPath,
            string destPath,
            string manifest,
            bool overwriteExisting,
            bool restoreSystemState,
            bool preservePermissions,
            ProgressCallback? callback);

        // Job context functions - tells C++ engine which job is running for logging
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern void SetCurrentJobName(string jobName);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ClearCurrentJobName();
    }
}
