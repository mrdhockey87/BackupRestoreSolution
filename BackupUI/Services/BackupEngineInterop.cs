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

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int CreateVolumeSnapshot(
            string volume,
            StringBuilder snapshotPath,
            int pathSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int BackupFiles(
            string sourcePath,
            string destPath,
            ProgressCallback? callback);

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
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int BackupDisk(
            int diskNumber,
            string destPath,
            bool includeSystemState,
            bool compress,
            ProgressCallback? callback);

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
    }
}
