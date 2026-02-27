using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BackupService
{
    public class BackupExecutor
    {
        private const string DllName = "BackupEngine.dll";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ProgressCallback(int percentage, [MarshalAs(UnmanagedType.LPWStr)] string message);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupFiles(string sourcePath, string destPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupVolume(string volumePath, string destPath, bool includeSystemState, 
            bool compress, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupDisk(int diskNumber, string destPath, bool includeSystemState, 
            bool compress, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupHyperVVM(string vmName, string destPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int CreateIncrementalBackup(string sourcePath, string destPath, 
            string baseBackupPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int CreateDifferentialBackup(string sourcePath, string destPath, 
            string fullBackupPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int VerifyBackup(string backupPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern void GetLastErrorMessage(StringBuilder buffer, int bufferSize);

        public async Task<bool> ExecuteBackupJobWithProgress(
            BackupJob job, 
            Action<int, string>? progressCallback,
            CancellationToken cancellationToken,
            Action<string>? logger = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    logger?.Invoke($"Starting backup job: {job.Name}");
                    progressCallback?.Invoke(0, "Initializing backup...");

                    if (cancellationToken.IsCancellationRequested)
                    {
                        logger?.Invoke("Backup cancelled by user");
                        return false;
                    }

                    // REMOVED: Backup safety renaming - with single-file approach, we simply overwrite
                    // Old file is replaced atomically by new file

                    // Create native progress callback
                    ProgressCallback nativeCallback = (percentage, message) =>
                    {
                        progressCallback?.Invoke(percentage, message ?? $"Progress: {percentage}%");
                    };

                    bool backupSuccess = false;
                    string? newBackupPath = null;

                    // NEW ARCHITECTURE: Create direct .ssb file (no folders, no timestamps)
                    // Determine backup type suffix
                    string backupTypeSuffix;
                    if (job.Type == BackupType.Incremental || job.Type == BackupType.Differential)
                    {
                        // Check if we need to do a full backup as the base
                        string fullBackupFile = Path.Combine(job.DestinationPath, $"{job.Name}_Full.ssb");
                        if (!File.Exists(fullBackupFile))
                        {
                            backupTypeSuffix = "Full"; // Creating full backup as base
                            logger?.Invoke($"No full backup exists. Creating initial full backup: {job.Name}_Full.ssb");
                        }
                        else
                        {
                            backupTypeSuffix = job.Type == BackupType.Incremental ? "Incremental" : "Differential";
                        }
                    }
                    else
                    {
                        backupTypeSuffix = "Full";
                    }

                    // Create destination FILE path (no folders, no timestamp)
                    // Format: JobName_Full.ssb or JobName_Incremental.ssb
                    newBackupPath = Path.Combine(job.DestinationPath, $"{job.Name}_{backupTypeSuffix}.ssb");

                    // Ensure destination directory exists
                    Directory.CreateDirectory(job.DestinationPath);

                    logger?.Invoke($"Creating backup file: {Path.GetFileName(newBackupPath)}");

                    // Execute backup for all source paths
                    // For multiple sources, they'll be added as multiple images in the same WIM file
                    foreach (var sourcePath in job.SourcePaths)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            logger?.Invoke("Backup cancelled by user");
                            return false;
                        }

                        int result = ExecuteBackup(job, sourcePath, newBackupPath, nativeCallback, logger);

                        if (result != 0)
                        {
                            var error = new StringBuilder(1024);
                            GetLastErrorMessage(error, error.Capacity);
                            logger?.Invoke($"Backup failed: {error}");
                            return false;
                        }

                        backupSuccess = true;
                    }

                    if (job.IsHyperVBackup)
                    {
                        foreach (var vm in job.HyperVMachines)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                logger?.Invoke("Backup cancelled by user");
                                return false;
                            }

                            // Determine backup type suffix for Hyper-V backup
                            string vmBackupSuffix;  // Renamed to avoid conflict
                            string fullBackupFile = Path.Combine(job.DestinationPath, $"{vm}_Full.ssb");

                            if (job.Type == BackupType.Incremental || job.Type == BackupType.Differential)
                            {
                                if (!File.Exists(fullBackupFile))
                                {
                                    vmBackupSuffix = "Full"; // Creating full backup as base
                                    logger?.Invoke($"No full backup exists. Creating initial full backup: {vm}_Full.ssb");
                                }
                                else
                                {
                                    vmBackupSuffix = job.Type == BackupType.Incremental ? "Incremental" : "Differential";
                                }
                            }
                            else
                            {
                                vmBackupSuffix = "Full";
                            }

                            // Create destination FILE path (no folders, no timestamp)
                            newBackupPath = Path.Combine(job.DestinationPath, $"{vm}_{vmBackupSuffix}.ssb");
                            logger?.Invoke($"Creating Hyper-V backup file: {Path.GetFileName(newBackupPath)}");

                            progressCallback?.Invoke(0, $"Backing up Hyper-V VM: {vm}...");
                            int result = BackupHyperVVM(vm, newBackupPath, nativeCallback);

                            if (result != 0)
                            {
                                var error = new StringBuilder(1024);
                                GetLastErrorMessage(error, error.Capacity);
                                logger?.Invoke($"Hyper-V backup failed: {error}");
                                return false;
                            }

                            backupSuccess = true;
                        }
                    }

                    // Verify backup if requested
                    if (job.VerifyAfterBackup && !cancellationToken.IsCancellationRequested)
                    {
                        logger?.Invoke("Verifying backup...");
                        progressCallback?.Invoke(90, "Verifying backup...");

                        string verifyPath = newBackupPath ?? job.DestinationPath;
                        int result = VerifyBackup(verifyPath, nativeCallback);

                        if (result != 0)
                        {
                            logger?.Invoke("Backup verification failed!");

                            // Delete the failed backup FILE
                            if (newBackupPath != null && File.Exists(newBackupPath))
                            {
                                try
                                {
                                    File.Delete(newBackupPath);
                                    logger?.Invoke($"Failed backup deleted: {Path.GetFileName(newBackupPath)}");
                                }
                                catch (Exception ex)
                                {
                                    logger?.Invoke($"Warning: Could not delete failed backup: {ex.Message}");
                                }
                            }

                            return false;
                        }

                        logger?.Invoke("Backup verification successful!");
                    }

                    // REMOVED: Retention cleanup - with single-file approach, each backup type 
                    // (Full/Incremental/Differential) has ONE file that gets overwritten
                    // No need for retention policy or cleanup

                    progressCallback?.Invoke(100, "Backup completed successfully!");
                    logger?.Invoke($"Backup job completed successfully: {job.Name}");
                    return true;
                }
                catch (Exception ex)
                {
                    logger?.Invoke($"Backup job failed with exception: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        public async Task<bool> ExecuteBackupJob(BackupJob job, Action<string>? logger = null)
        {
            return await ExecuteBackupJobWithProgress(job, null, CancellationToken.None, logger);
        }

        private int ExecuteBackup(BackupJob job, string sourcePath, string destPath, 
            ProgressCallback? callback, Action<string>? logger)
        {
            int result;

            // DEFENSIVE FIX: Auto-detect if sourcePath is actually a device path but job.Target is wrong
            // This handles cases where jobs were created before the fix or with incorrect settings
            if (sourcePath.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase) ||
                sourcePath.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
            {
                // Device path detected - correct the job target
                if (sourcePath.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
                {
                    logger?.Invoke($"AUTO-CORRECT: Detected device path (PHYSICALDRIVE) - treating as Disk backup instead of {job.Target}");
                    job.Target = BackupTarget.Disk;
                }
                else if (sourcePath.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
                {
                    logger?.Invoke($"AUTO-CORRECT: Detected device path (Volume GUID) - treating as Volume backup instead of {job.Target}");
                    job.Target = BackupTarget.Volume;
                }
            }

            switch (job.Type)
            {
                case BackupType.Full:
                    if (job.Target == BackupTarget.Disk)
                    {
                        // Extract disk number from device path (e.g., \\.\PHYSICALDRIVE5 -> 5)
                        int diskNumber = ExtractDiskNumber(sourcePath);
                        if (diskNumber < 0)
                        {
                            logger?.Invoke($"ERROR: Invalid disk path format: {sourcePath}");
                            return -11;
                        }
                        logger?.Invoke($"Backing up disk: {diskNumber} ({sourcePath})");
                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, callback);
                    }
                    else if (job.Target == BackupTarget.Volume)
                    {
                        logger?.Invoke($"Backing up volume: {sourcePath}");
                        result = BackupVolume(sourcePath, destPath, job.IncludeSystemState, job.CompressData, callback);
                    }
                    else
                    {
                        logger?.Invoke($"Backing up files: {sourcePath}");
                        result = BackupFiles(sourcePath, destPath, callback);
                    }
                    break;

            case BackupType.Incremental:
                // Incremental backups need a full backup as the base
                var fullBackupBase = FindFullBackup(job.DestinationPath, job.Name);
                if (string.IsNullOrEmpty(fullBackupBase))
                {
                    logger?.Invoke($"No full backup found. Creating initial full backup instead of incremental.");
                    // Do a full backup if no previous full backup exists
                    if (job.Target == BackupTarget.Disk)
                    {
                        int diskNumber = ExtractDiskNumber(sourcePath);
                        if (diskNumber < 0)
                        {
                            logger?.Invoke($"ERROR: Invalid disk path format: {sourcePath}");
                            return -11;
                        }
                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, callback);
                    }
                    else if (job.Target == BackupTarget.Volume)
                    {
                        result = BackupVolume(sourcePath, destPath, job.IncludeSystemState, job.CompressData, callback);
                    }
                    else
                    {
                        result = BackupFiles(sourcePath, destPath, callback);
                    }
                }
                else
                {
                    // Find the most recent backup (could be full, incremental, or differential) to base the incremental on
                    var lastBackup = FindLastBackup(job.DestinationPath, job.Name) ?? fullBackupBase;
                    logger?.Invoke($"Creating incremental backup from: {lastBackup}");
                    result = CreateIncrementalBackup(sourcePath, destPath, lastBackup, callback);
                }
                break;

            case BackupType.Differential:
                var fullBackup = FindFullBackup(job.DestinationPath, job.Name);
                if (string.IsNullOrEmpty(fullBackup))
                {
                    logger?.Invoke($"No full backup found. Creating initial full backup instead of differential.");
                    // Do a full backup if no base full backup exists
                    if (job.Target == BackupTarget.Disk)
                    {
                        int diskNumber = ExtractDiskNumber(sourcePath);
                        if (diskNumber < 0)
                        {
                            logger?.Invoke($"ERROR: Invalid disk path format: {sourcePath}");
                            return -11;
                        }
                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, callback);
                    }
                    else if (job.Target == BackupTarget.Volume)
                    {
                        result = BackupVolume(sourcePath, destPath, job.IncludeSystemState, job.CompressData, callback);
                    }
                    else
                    {
                        result = BackupFiles(sourcePath, destPath, callback);
                    }
                }
                else
                {
                    logger?.Invoke($"Creating differential backup from: {fullBackup}");
                    result = CreateDifferentialBackup(sourcePath, destPath, fullBackup, callback);
                }
                break;

                default:
                    result = -1;
                    break;
            }

            return result;
        }

        private string? FindLastBackup(string destPath, string jobName)
        {
            try
            {
                if (!Directory.Exists(destPath))
                    return null;

                // Look for any backup file for this job (Full, Incremental, or Differential)
                var backupFiles = Directory.GetFiles(destPath, $"{jobName}_*.ssb")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .FirstOrDefault();

                return backupFiles;
            }
            catch
            {
                return null;
            }
        }

        private string? FindFullBackup(string destPath, string jobName)
        {
            try
            {
                if (!Directory.Exists(destPath))
                    return null;

                // Look specifically for the Full backup file
                string fullBackupFile = Path.Combine(destPath, $"{jobName}_Full.ssb");

                if (File.Exists(fullBackupFile))
                    return fullBackupFile;

                return null;
            }
            catch
            {
                return null;
            }
        }

        // REMOVED: GetExistingFullBackups - no longer needed with single-file approach
        // REMOVED: RenameBackupAsPending - overwrite existing files directly  
        // REMOVED: RestoreRenamedBackup - no backup renaming needed
        // REMOVED: CleanupOldBackups - single file per backup type, no cleanup needed

        // Retention logic simplified: Each backup type (Full/Incremental/Differential) 
        // overwrites its own file. For multiple versions, implement versioning later.

        /// <summary>
        /// Extracts disk number from physical drive device path
        /// </summary>
        /// <param name="devicePath">Device path like \\.\PHYSICALDRIVE5</param>
        /// <returns>Disk number or -1 if invalid format</returns>
        private int ExtractDiskNumber(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
                return -1;

            // Expected format: \\.\PHYSICALDRIVE5 or \\.\PhysicalDrive5
            const string prefix = "\\\\.\\PHYSICALDRIVE";

            if (devicePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string numberPart = devicePath.Substring(prefix.Length);
                if (int.TryParse(numberPart, out int diskNumber))
                {
                    return diskNumber;
                }
            }

            return -1;
        }

        // REMOVED: CleanupOldBackups - not needed with single-file approach
        // Each backup type overwrites its own file
    }
}
