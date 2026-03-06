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
        private static extern int BackupDiskIncremental(int diskNumber, string destPath, bool includeSystemState, 
            bool compress, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupDiskDifferential(int diskNumber, string destPath, bool includeSystemState, 
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

                    string? newBackupPath = null;

                    // SIMPLIFIED ARCHITECTURE: Create direct .ssb file with NO type suffix
                    // Each backup overwrites the previous one - single file per job
                    // Format: JobName.ssb (no Full/Incremental/Differential suffix)

                    // Create destination FILE path (no folders, no timestamp, no type suffix)
                    newBackupPath = Path.Combine(job.DestinationPath, $"{job.Name}.ssb");

                    // For incremental/differential, check if base backup exists
                    if (job.Type == BackupType.Incremental || job.Type == BackupType.Differential)
                    {
                        if (!File.Exists(newBackupPath))
                        {
                            logger?.Invoke($"No base backup exists. Creating initial full backup: {job.Name}.ssb");
                        }
                    }

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

                            // DIAGNOSTIC: Show both result code AND error message
                            string errorMessage = error.ToString();
                            logger?.Invoke($"[ERROR] Backup failed with code {result}");
                            logger?.Invoke($"[ERROR] Error message: {(string.IsNullOrEmpty(errorMessage) ? "(empty - C++ didn't set error message)" : errorMessage)}");
                            logger?.Invoke($"[ERROR] Source path: {sourcePath}");
                            logger?.Invoke($"[ERROR] Destination path: {newBackupPath}");
                            logger?.Invoke($"[ERROR] File exists after failure: {(newBackupPath != null && File.Exists(newBackupPath))}");

                            return false;
                        }
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

                            // SIMPLIFIED: No type suffix for Hyper-V backups either
                            newBackupPath = Path.Combine(job.DestinationPath, $"{vm}.ssb");

                            // Check if base backup exists for incremental/differential
                            if (job.Type == BackupType.Incremental || job.Type == BackupType.Differential)
                            {
                                if (!File.Exists(newBackupPath))
                                {
                                    logger?.Invoke($"No base backup exists. Creating initial full backup: {vm}.ssb");
                                }
                            }

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
                        }
                    }

                    // Verify backup if requested
                    if (job.VerifyAfterBackup && !cancellationToken.IsCancellationRequested)
                    {
                        logger?.Invoke("Verifying backup...");
                        progressCallback?.Invoke(90, "Verifying backup...");

                        // VerifyBackup expects a FOLDER path, not a file path
                        // newBackupPath is the full .ssb file path, so extract the directory
                        string verifyPath = job.DestinationPath; // Use the folder containing the .ssb file
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
            // Only log correction message if we're actually CHANGING the target (not when already correct)
            if (sourcePath.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
            {
                // Physical drive path detected - should be Disk backup
                if (job.Target != BackupTarget.Disk)
                {
                    logger?.Invoke($"AUTO-CORRECT: Detected device path (PHYSICALDRIVE) - changing from {job.Target} to Disk backup");
                    job.Target = BackupTarget.Disk;
                }
            }
            else if (sourcePath.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
            {
                // Volume GUID path detected - should be Volume backup
                if (job.Target != BackupTarget.Volume)
                {
                    logger?.Invoke($"AUTO-CORRECT: Detected device path (Volume GUID) - changing from {job.Target} to Volume backup");
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

                        // DIAGNOSTIC: Log right before calling C++ function
                        logger?.Invoke($"[DIAGNOSTIC] About to call BackupDisk({diskNumber}, {destPath}, {job.IncludeSystemState}, {job.CompressData})");

                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, callback);

                        // DIAGNOSTIC: Log result code immediately
                        logger?.Invoke($"[DIAGNOSTIC] BackupDisk returned: {result}");

                        if (result != 0)
                        {
                            // DIAGNOSTIC: Log that we're getting error message
                            logger?.Invoke($"[DIAGNOSTIC] BackupDisk failed with code {result}, getting error message...");
                        }
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
                // DISK BACKUPS: Now supports true incremental using WIM_FLAG_REFERENCE!
                if (job.Target == BackupTarget.Disk)
                {
                    int diskNumber = ExtractDiskNumber(sourcePath);
                    if (diskNumber < 0)
                    {
                        logger?.Invoke($"ERROR: Invalid disk path format: {sourcePath}");
                        return -11;
                    }

                    // Check if base backup exists
                    if (File.Exists(destPath))
                    {
                        logger?.Invoke($"Creating incremental disk backup (WIM referential): {diskNumber}");
                        result = BackupDiskIncremental(diskNumber, destPath, job.IncludeSystemState, job.CompressData, callback);

                        if (result != 0)
                        {
                            logger?.Invoke($"Disk incremental backup failed with code {result}");
                        }
                    }
                    else
                    {
                        logger?.Invoke($"No base backup found. Creating initial full backup: {diskNumber}");
                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, callback);

                        if (result != 0)
                        {
                            logger?.Invoke($"Disk full backup (fallback) failed with code {result}");
                        }
                        else
                        {
                            logger?.Invoke($"Initial full backup completed successfully (fallback from incremental)");
                        }
                    }
                }
                else
                {
                    // FILE/FOLDER/VOLUME BACKUPS: Support true incremental backups
                    var fullBackupBase = FindFullBackup(job.DestinationPath, job.Name);
                    if (string.IsNullOrEmpty(fullBackupBase))
                    {
                        logger?.Invoke($"No full backup found. Creating initial full backup instead of incremental.");
                        // Do a full backup if no previous full backup exists
                        if (job.Target == BackupTarget.Volume)
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
                }
                break;

            case BackupType.Differential:
                // DISK BACKUPS: Now supports true differential using WIM_FLAG_REFERENCE!
                if (job.Target == BackupTarget.Disk)
                {
                    int diskNumber = ExtractDiskNumber(sourcePath);
                    if (diskNumber < 0)
                    {
                        logger?.Invoke($"ERROR: Invalid disk path format: {sourcePath}");
                        return -11;
                    }

                    // Check if base backup exists
                    if (File.Exists(destPath))
                    {
                        logger?.Invoke($"Creating differential disk backup (WIM referential): {diskNumber}");
                        result = BackupDiskDifferential(diskNumber, destPath, job.IncludeSystemState, job.CompressData, callback);

                        if (result != 0)
                        {
                            logger?.Invoke($"Disk differential backup failed with code {result}");
                        }
                    }
                    else
                    {
                        logger?.Invoke($"No base backup found. Creating initial full backup: {diskNumber}");
                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, callback);

                        if (result != 0)
                        {
                            logger?.Invoke($"Disk full backup (fallback) failed with code {result}");
                        }
                        else
                        {
                            logger?.Invoke($"Initial full backup completed successfully (fallback from differential)");
                        }
                    }
                }
                else
                {
                    // FILE/FOLDER/VOLUME BACKUPS: Support true differential backups
                    var fullBackup = FindFullBackup(job.DestinationPath, job.Name);
                    if (string.IsNullOrEmpty(fullBackup))
                    {
                        logger?.Invoke($"No full backup found. Creating initial full backup instead of differential.");
                        // Do a full backup if no base full backup exists
                        if (job.Target == BackupTarget.Volume)
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

                // SIMPLIFIED: Look for the single backup file (no suffixes)
                // With new architecture, there's only ONE file: JobName.ssb
                string backupFile = Path.Combine(destPath, $"{jobName}.ssb");

                if (File.Exists(backupFile))
                    return backupFile;

                return null;
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

                // SIMPLIFIED: Look for the single backup file (no suffixes)
                // With new architecture, full/incremental/differential all use same file: JobName.ssb
                string backupFile = Path.Combine(destPath, $"{jobName}.ssb");

                if (File.Exists(backupFile))
                    return backupFile;

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
