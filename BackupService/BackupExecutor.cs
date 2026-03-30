using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BackupCommon;

namespace BackupService
{
    public class BackupExecutor
    {
        private const string DllName = "BackupEngine.dll";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ProgressCallback(int percentage, [MarshalAs(UnmanagedType.LPWStr)] string message);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupFiles(string sourcePath, string destPath,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupVolume(string volumePath, string destPath, bool includeSystemState, 
            bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount,
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupDisk(int diskNumber, string destPath, bool includeSystemState, 
            bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount,
            ProgressCallback? callback);

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
        private static extern int VerifyWimArchive(
            string archivePath, 
            int expectedImageCount, 
            StringBuilder errorMsg, 
            int errorMsgSize, 
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern void GetLastErrorMessage(StringBuilder buffer, int bufferSize);

        // Job context functions - tells C++ engine which job is running for logging
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern void SetCurrentJobName(string jobName);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ClearCurrentJobName();

        public async Task<bool> ExecuteBackupJobWithProgress(
            BackupJob job, 
            Action<int, string>? progressCallback,
            CancellationToken cancellationToken,
            Action<string>? logger = null)
        {
            // Store original priority to restore after backup completes
            var originalPriority = ProcessPriorityClass.Normal;
            
            return await Task.Run(() =>
            {
                try
                {
                    // Set process to BelowNormal priority for backup operations only
                    // This implements "Efficiency mode" for backups to reduce CPU impact
                    // Mount/Unmount and UI operations remain at Normal priority
                    try
                    {
                        originalPriority = Process.GetCurrentProcess().PriorityClass;
                        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
                        logger?.Invoke($"Process priority set to BelowNormal for backup operation (was {originalPriority})");
                    }
                    catch (Exception prioEx)
                    {
                        logger?.Invoke($"Warning: Could not set process priority: {prioEx.Message}");
                    }

                    // Set job name for C++ engine logging context
                    // This ensures C++ logs go to {JobName}.json instead of engine.json
                    try
                    {
                        SetCurrentJobName(job.Name);
                        logger?.Invoke($"C++ engine logging context set to: {job.Name}");
                    }
                    catch (Exception jobNameEx)
                    {
                        logger?.Invoke($"Warning: Could not set C++ job name context: {jobNameEx.Message}");
                    }

                    logger?.Invoke($"Starting backup job: {job.Name}");
                    progressCallback?.Invoke(0, "Initializing backup...");

                    if (cancellationToken.IsCancellationRequested)
                    {
                        logger?.Invoke("Backup cancelled by user");
                        return false;
                    }

                    // AUTO-RECOVERY: Check if we need to force a full backup due to previous verification failure
                    BackupType originalType = job.Type;
                    if (job.ForceFullBackupOnNextRun && (job.Type == BackupType.Incremental || job.Type == BackupType.Differential))
                    {
                        logger?.Invoke($"AUTO-RECOVERY MODE: Previous {originalType} backup failed verification");
                        logger?.Invoke($"Forcing FULL backup to rebuild backup chain");
                        job.Type = BackupType.Full;

                        // Clear the flag and save
                        job.ForceFullBackupOnNextRun = false;
                        try
                        {
                            var jobManager = new JobManager();
                            jobManager.UpdateJob(job);
                            logger?.Invoke("ForceFullBackupOnNextRun flag cleared");
                        }
                        catch (Exception saveEx)
                        {
                            logger?.Invoke($"Warning: Failed to clear ForceFullBackupOnNextRun flag: {saveEx.Message}");
                        }
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
                    // If no base exists, automatically switch to Full backup
                    if (job.Type == BackupType.Incremental || job.Type == BackupType.Differential)
                    {
                        if (!File.Exists(newBackupPath))
                        {
                            logger?.Invoke($"No base backup exists. Automatically switching from {job.Type} to Full backup: {job.Name}.ssb");
                            job.Type = BackupType.Full;
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

                            // CRITICAL: Delete failed backup file for incremental/differential
                            // This ensures next attempt will start fresh instead of trying to open corrupt file
                            if ((job.Type == BackupType.Incremental || job.Type == BackupType.Differential) && 
                                newBackupPath != null && File.Exists(newBackupPath))
                            {
                                try
                                {
                                    logger?.Invoke($"[CLEANUP] Deleting failed backup file: {Path.GetFileName(newBackupPath)}");
                                    File.Delete(newBackupPath);
                                    logger?.Invoke($"[CLEANUP] Failed backup file deleted successfully");
                                }
                                catch (Exception ex)
                                {
                                    logger?.Invoke($"[WARNING] Could not delete failed backup file: {ex.Message}");
                                }
                            }

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
                        logger?.Invoke("Verifying backup archive...");
                        progressCallback?.Invoke(90, "Verifying SSB archive integrity...");

                        // For disk backups, count expected images (one per volume)
                        // For other backups, pass -1 to skip image count validation
                        int expectedImageCount = -1;

                        if (job.Target == BackupTarget.Disk && job.SourcePaths != null && job.SourcePaths.Count > 0)
                        {
                            // Extract disk number to count volumes
                            int diskNumber = ExtractDiskNumber(job.SourcePaths[0]);
                            if (diskNumber >= 0)
                            {
                                // For now, we don't have volume count - pass -1 to skip validation
                                // Future enhancement: query WMI for actual volume count
                                expectedImageCount = -1;
                            }
                        }

                        // Use enhanced WIM archive verification
                        var errorMsg = new StringBuilder(1024);
                        int verifyResult = VerifyWimArchive(
                            newBackupPath,  // Direct .ssb file path
                            expectedImageCount,  // Expected image count (or -1 to skip)
                            errorMsg,
                            errorMsg.Capacity,
                            nativeCallback
                        );

                        if (verifyResult != 0)
                        {
                            logger?.Invoke($"[VERIFICATION FAILED] Result code: {verifyResult}");
                            logger?.Invoke($"[VERIFICATION FAILED] Error: {errorMsg}");
                            logger?.Invoke($"[VERIFICATION FAILED] The backup file will be DELETED because verification failed");
                            logger?.Invoke($"[VERIFICATION FAILED] This is why no backup file exists in target directory");

                            // Auto-recovery: If incremental/differential failed verification,
                            // force FULL backup on next run to rebuild the backup chain
                            if (job.Type == BackupType.Incremental || job.Type == BackupType.Differential)
                            {
                                job.ForceFullBackupOnNextRun = true;
                                logger?.Invoke($"AUTO-RECOVERY: Next backup will be FULL to rebuild backup chain");

                                // Save job with updated flag
                                try
                                {
                                    var jobManager = new JobManager();
                                    jobManager.UpdateJob(job);
                                    logger?.Invoke("Job updated with ForceFullBackupOnNextRun flag");
                                }
                                catch (Exception saveEx)
                                {
                                    logger?.Invoke($"Warning: Failed to save ForceFullBackupOnNextRun flag: {saveEx.Message}");
                                }
                            }

                            // Delete the failed backup FILE
                            if (newBackupPath != null && File.Exists(newBackupPath))
                            {
                                try
                                {
                                    var fileInfo = new FileInfo(newBackupPath);
                                    var fileSize = fileInfo.Length;
                                    logger?.Invoke($"[CLEANUP] Deleting failed backup: {Path.GetFileName(newBackupPath)} ({fileSize:N0} bytes)");
                                    File.Delete(newBackupPath);
                                    logger?.Invoke($"[CLEANUP] Failed backup deleted successfully");
                                }
                                catch (Exception ex)
                                {
                                    logger?.Invoke($"[WARNING] Could not delete failed backup: {ex.Message}");
                                }
                            }

                            return false;
                        }

                        // Verification succeeded!
                        logger?.Invoke($"Backup verification PASSED: {errorMsg}");
                    }

                    // If we forced a full backup for auto-recovery, restore original type for future backups
                    if (originalType != job.Type)
                    {
                        job.Type = originalType;
                        try
                        {
                            var jobManager = new JobManager();
                            jobManager.UpdateJob(job);
                            logger?.Invoke($"AUTO-RECOVERY COMPLETE: Job type restored to {originalType} for next run");
                        }
                        catch (Exception saveEx)
                        {
                            logger?.Invoke($"Warning: Failed to restore job type: {saveEx.Message}");
                        }
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
                finally
                {
                    // Clear C++ engine logging context
                    try
                    {
                        ClearCurrentJobName();
                    }
                    catch { }

                    // Restore original process priority after backup completion
                    try
                    {
                        Process.GetCurrentProcess().PriorityClass = originalPriority;
                        logger?.Invoke($"Process priority restored to {originalPriority}");
                    }
                    catch (Exception prioEx)
                    {
                        logger?.Invoke($"Warning: Could not restore process priority: {prioEx.Message}");
                    }
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

            // Convert user exclusions to array for P/Invoke (empty array if null)
            string[] exclusionsArray = job.UserExclusions?.ToArray() ?? Array.Empty<string>();
            int exclusionCount = exclusionsArray.Length;

            if (exclusionCount > 0)
            {
                logger?.Invoke($"Applying {exclusionCount} user-defined exclusion(s) to backup");
            }

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
                        logger?.Invoke($"[DIAGNOSTIC] About to call BackupDisk({diskNumber}, {destPath}, {job.IncludeSystemState}, {job.CompressData}, exclusions: {exclusionCount})");

                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, 
                                          exclusionsArray, exclusionCount, callback);

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
                        result = BackupVolume(sourcePath, destPath, job.IncludeSystemState, job.CompressData,
                                            exclusionsArray, exclusionCount, callback);
                    }
                    else
                    {
                        logger?.Invoke($"Backing up files: {sourcePath}");
                        result = BackupFiles(sourcePath, destPath, exclusionsArray, exclusionCount, callback);
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
                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, 
                                          exclusionsArray, exclusionCount, callback);

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
                            result = BackupVolume(sourcePath, destPath, job.IncludeSystemState, job.CompressData,
                                                exclusionsArray, exclusionCount, callback);
                        }
                        else
                        {
                            result = BackupFiles(sourcePath, destPath, exclusionsArray, exclusionCount, callback);
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
                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, 
                                          exclusionsArray, exclusionCount, callback);

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
                            result = BackupVolume(sourcePath, destPath, job.IncludeSystemState, job.CompressData,
                                                exclusionsArray, exclusionCount, callback);
                        }
                        else
                        {
                            result = BackupFiles(sourcePath, destPath, exclusionsArray, exclusionCount, callback);
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
