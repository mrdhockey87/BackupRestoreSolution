using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SecureServerBackupCommon;

namespace SecureServerBackupService
{
    public class BackupExecutor
    {
        private const string DllName = "SecureServerBackupEngine.dll";
        private static readonly SemaphoreSlim NativeExecutionLock = new(1, 1);

        public static string GetHyperVBackupMode(BackupType backupType, bool hasExistingFullBackup, bool hasAnyExistingBackup)
        {
            return backupType switch
            {
                BackupType.Incremental when hasAnyExistingBackup => "Incremental",
                BackupType.Differential when hasExistingFullBackup => "Differential",
                _ => "Full"
            };
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ProgressCallback(int percentage, [MarshalAs(UnmanagedType.LPWStr)] string message);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void LogCallback(
            int level,
            [MarshalAs(UnmanagedType.LPWStr)] string message,
            [MarshalAs(UnmanagedType.LPWStr)] string details);

        private static void EncryptBackupFileIfNeeded(BackupJob job, string backupPath, Action<int, string>? progressCallback, Action<string>? logger)
        {
            if (!job.EncryptBackup)
            {
                return;
            }

            progressCallback?.Invoke(88, "Encrypting backup archive with AES-128...");
            logger?.Invoke("Encrypting backup archive with AES-128...");
            string password = BackupEncryptionService.UnprotectPassword(job.ProtectedEncryptionPassword);
            BackupEncryptionService.EncryptFile(backupPath, backupPath, password);
            logger?.Invoke("Backup archive encrypted successfully.");
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupFiles(string sourcePath, string destPath,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount, ProgressCallback? callback, LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupVolume(string volumePath, string destPath,
            [MarshalAs(UnmanagedType.I1)] bool includeSystemState,
            [MarshalAs(UnmanagedType.I1)] bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount,
            ProgressCallback? callback, LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupDisk(int diskNumber, string destPath,
            [MarshalAs(UnmanagedType.I1)] bool includeSystemState,
            [MarshalAs(UnmanagedType.I1)] bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount,
            ProgressCallback? callback, LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupDiskIncremental(int diskNumber, string destPath,
            [MarshalAs(UnmanagedType.I1)] bool includeSystemState,
            [MarshalAs(UnmanagedType.I1)] bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount,
            ProgressCallback? callback, LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupDiskDifferential(int diskNumber, string destPath,
            [MarshalAs(UnmanagedType.I1)] bool includeSystemState,
            [MarshalAs(UnmanagedType.I1)] bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount,
            ProgressCallback? callback, LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupHyperVVM(string vmName, string destPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupHyperVVMIncremental(string vmName, string destPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupHyperVVMDifferential(string vmName, string destPath, ProgressCallback? callback);

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

        public enum DismImageHealthState
        {
            Healthy = 0,
            Repairable = 1,
            NonRepairable = 2
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int CheckBackupImageHealth(
            string backupPath,
            int imageIndex,
            [MarshalAs(UnmanagedType.I1)] bool scanImage,
            StringBuilder healthMessage,
            int healthMessageSize,
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int RestoreBackupImageHealth(
            string backupPath,
            int imageIndex,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? sourcePaths,
            int sourcePathCount,
            [MarshalAs(UnmanagedType.I1)] bool limitAccess,
            StringBuilder healthMessage,
            int healthMessageSize,
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
            var originalPriority = ProcessPriorityClass.Normal;

            await NativeExecutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
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

                        BackupType originalType = job.Type;
                        if (job.ForceFullBackupOnNextRun && (job.Type == BackupType.Incremental || job.Type == BackupType.Differential))
                        {
                            logger?.Invoke($"AUTO-RECOVERY MODE: Previous {originalType} backup failed verification");
                            logger?.Invoke("Forcing FULL backup to rebuild backup chain");
                            job.Type = BackupType.Full;
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

                        ProgressCallback? nativeCallback = null;
                        if (progressCallback != null)
                        {
                            nativeCallback = (percentage, message) =>
                            {
                                progressCallback(percentage, message ?? $"Progress: {percentage}%");
                            };
                        }

                        LogCallback nativeLogCallback = (level, message, details) =>
                        {
                            LogFromEngine(job.Name, level, message, details);
                        };

                        string? newBackupPath = Path.Combine(job.DestinationPath, $"{job.Name}.ssb");
                        if (job.Type == BackupType.Incremental || job.Type == BackupType.Differential)
                        {
                            if (!File.Exists(newBackupPath))
                            {
                                logger?.Invoke($"No base backup exists. Automatically switching from {job.Type} to Full backup: {job.Name}.ssb");
                                job.Type = BackupType.Full;
                            }
                        }

                        Directory.CreateDirectory(job.DestinationPath);
                        logger?.Invoke($"Creating backup file: {Path.GetFileName(newBackupPath)}");

                        foreach (var sourcePath in job.SourcePaths)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                logger?.Invoke("Backup cancelled by user");
                                return false;
                            }

                            int result = ExecuteBackup(job, sourcePath, newBackupPath, nativeCallback, nativeLogCallback, logger);
                            if (result != 0)
                            {
                                var error = new StringBuilder(1024);
                                GetLastErrorMessage(error, error.Capacity);

                                string errorMessage = error.ToString();
                                logger?.Invoke($"[ERROR] Backup failed with code {result}");
                                logger?.Invoke($"[ERROR] Error message: {(string.IsNullOrEmpty(errorMessage) ? "(empty - C++ didn't set error message)" : errorMessage)}");
                                logger?.Invoke($"[ERROR] Source path: {sourcePath}");
                                logger?.Invoke($"[ERROR] Destination path: {newBackupPath}");
                                logger?.Invoke($"[DEBUG] Failed backup file preserved for analysis: {Path.GetFileName(newBackupPath)}");
                                return false;
                            }
                        }

                        if (job.IsHyperVBackup || job.Target == BackupTarget.HyperV)
                        {
                            foreach (var vm in job.HyperVMachines)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    logger?.Invoke("Backup cancelled by user");
                                    return false;
                                }

                                newBackupPath = job.DestinationPath;

                                logger?.Invoke($"Creating Hyper-V backup point in: {newBackupPath}");
                                progressCallback?.Invoke(0, $"Backing up Hyper-V VM: {vm}...");

                                bool hasAnyExistingHyperVPoint = Directory.Exists(newBackupPath) && Directory.EnumerateDirectories(newBackupPath, "*.ssb", SearchOption.TopDirectoryOnly).Any();
                                bool hasExistingFullHyperVPoint = Directory.Exists(newBackupPath) && Directory.EnumerateDirectories(newBackupPath, "Full_*.ssb", SearchOption.TopDirectoryOnly).Any();
                                string hyperVBackupMode = GetHyperVBackupMode(job.Type, hasExistingFullHyperVPoint, hasAnyExistingHyperVPoint);

                                int result = hyperVBackupMode switch
                                {
                                    "Incremental" => BackupHyperVVMIncremental(vm, newBackupPath, nativeCallback),
                                    "Differential" => BackupHyperVVMDifferential(vm, newBackupPath, nativeCallback),
                                    _ => BackupHyperVVM(vm, newBackupPath, nativeCallback)
                                };
                                if (result != 0)
                                {
                                    var error = new StringBuilder(1024);
                                    GetLastErrorMessage(error, error.Capacity);
                                    logger?.Invoke($"Hyper-V backup failed: {error}");
                                    return false;
                                }
                            }
                        }

                        if (newBackupPath != null && job.Target != BackupTarget.HyperV)
                        {
                            EncryptBackupFileIfNeeded(job, newBackupPath, progressCallback, logger);
                        }

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
                        try
                        {
                            ClearCurrentJobName();
                        }
                        catch
                        {
                        }

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
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                NativeExecutionLock.Release();
            }
        }

        public async Task<bool> ExecuteBackupJob(BackupJob job, Action<string>? logger = null)
        {
            return await ExecuteBackupJobWithProgress(job, null, CancellationToken.None, logger);
        }

        /// <summary>
        /// Executes verification on a completed backup with progress tracking
        /// </summary>
        public async Task<bool> VerifyBackupWithProgress(
            BackupJob job,
            Action<int, string>? progressCallback,
            CancellationToken cancellationToken,
            Action<string>? logger = null)
        {
            await NativeExecutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        logger?.Invoke($"Starting backup verification for: {job.Name}");
                        progressCallback?.Invoke(0, "Initializing verification...");

                        if (cancellationToken.IsCancellationRequested)
                        {
                            logger?.Invoke("Verification cancelled by user");
                            return false;
                        }

                        string backupPath = Path.Combine(job.DestinationPath, $"{job.Name}.ssb");
                        if (!File.Exists(backupPath))
                        {
                            logger?.Invoke($"[ERROR] Backup file not found: {backupPath}");
                            return false;
                        }

                        logger?.Invoke($"Verifying backup file: {Path.GetFileName(backupPath)}");
                        progressCallback?.Invoke(10, "Verifying SSB archive integrity...");

                        ProgressCallback? nativeCallback = null;
                        if (progressCallback != null)
                        {
                            nativeCallback = (percentage, message) =>
                            {
                                int mappedPercentage = 10 + (int)(percentage * 0.8);
                                progressCallback(mappedPercentage, message ?? $"Verifying: {percentage}%");
                            };
                        }

                        int expectedImageCount = -1;
                        if (job.Target == BackupTarget.Disk && job.SourcePaths?.Count > 0)
                        {
                            int diskNumber = ExtractDiskNumber(job.SourcePaths[0]);
                            if (diskNumber >= 0)
                            {
                                expectedImageCount = -1;
                            }
                        }
                        else if (job.Target == BackupTarget.HyperV)
                        {
                            expectedImageCount = -1;
                        }

                        var errorMsg = new StringBuilder(1024);
                        int verifyResult = VerifyWimArchive(
                            backupPath,
                            expectedImageCount,
                            errorMsg,
                            errorMsg.Capacity,
                            nativeCallback);

                        if (verifyResult != 0)
                        {
                            logger?.Invoke($"[VERIFICATION FAILED] Result code: {verifyResult}");
                            logger?.Invoke($"[VERIFICATION FAILED] Error: {errorMsg}");
                            return false;
                        }

                        logger?.Invoke($"Archive verification PASSED: {errorMsg}");
                        progressCallback?.Invoke(90, "Checking image health...");

                        var healthMsg = new StringBuilder(1024);
                        int healthState = CheckBackupImageHealth(
                            backupPath,
                            1,
                            true,
                            healthMsg,
                            healthMsg.Capacity,
                            nativeCallback);

                        if (healthState < 0)
                        {
                            logger?.Invoke($"[DISM VERIFY FAILED] Result code: {healthState}");
                            logger?.Invoke($"[DISM VERIFY FAILED] {healthMsg}");
                            return false;
                        }

                        if (healthState == (int)DismImageHealthState.Repairable)
                        {
                            logger?.Invoke($"[DISM] Image is repairable. Attempting RestoreHealth: {healthMsg}");
                            progressCallback?.Invoke(95, "Repairing image...");

                            var repairMsg = new StringBuilder(1024);
                            int repairResult = RestoreBackupImageHealth(
                                backupPath,
                                1,
                                null,
                                0,
                                false,
                                repairMsg,
                                repairMsg.Capacity,
                                nativeCallback);

                            if (repairResult != 0)
                            {
                                logger?.Invoke($"[DISM REPAIR FAILED] Result code: {repairResult}");
                                logger?.Invoke($"[DISM REPAIR FAILED] {repairMsg}");
                                return false;
                            }

                            logger?.Invoke($"[DISM] Repair completed: {repairMsg}");
                        }
                        else if (healthState == (int)DismImageHealthState.NonRepairable)
                        {
                            logger?.Invoke($"[DISM VERIFY FAILED] Image is non-repairable: {healthMsg}");
                            return false;
                        }
                        else
                        {
                            logger?.Invoke($"[DISM VERIFY PASSED] {healthMsg}");
                        }

                        progressCallback?.Invoke(100, "Verification completed successfully!");
                        logger?.Invoke($"Backup verification completed successfully: {job.Name}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke($"Verification failed with exception: {ex.Message}");
                        return false;
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                NativeExecutionLock.Release();
            }
        }

        private static void LogFromEngine(string jobName, int level, string message, string details)
        {
            switch (level)
            {
                case 0: // Info
                    BackupLogger.LogInfo(jobName, message, details);
                    break;
                case 1: // Success
                    BackupLogger.LogSuccess(jobName, message, details);
                    break;
                case 2: // Warning
                    BackupLogger.LogWarning(jobName, message, details);
                    break;
                case 3: // Error
                    BackupLogger.LogError(jobName, message, details);
                    break;
                default:
                    BackupLogger.LogInfo(jobName, message, details);
                    break;
            }
        }

        private int ExecuteBackup(BackupJob job, string sourcePath, string destPath,
            ProgressCallback? progressCallback, LogCallback logCallback, Action<string>? logger)
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

            if (job.Target == BackupTarget.HyperV)
            {
                logger?.Invoke($"Backing up Hyper-V virtual machine: {sourcePath}");
                return BackupHyperVVM(sourcePath, destPath, progressCallback);
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
                                          exclusionsArray, exclusionCount, progressCallback, logCallback);

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
                                            exclusionsArray, exclusionCount, progressCallback, logCallback);
                    }
                    else
                    {
                        logger?.Invoke($"Backing up files: {sourcePath}");
                        result = BackupFiles(sourcePath, destPath, exclusionsArray, exclusionCount, progressCallback, logCallback);
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
                        result = BackupDiskIncremental(diskNumber, destPath, job.IncludeSystemState, job.CompressData,
                            exclusionsArray, exclusionCount, progressCallback, logCallback);

                        if (result != 0)
                        {
                            logger?.Invoke($"Disk incremental backup failed with code {result}");
                        }
                    }
                    else
                    {
                        logger?.Invoke($"No base backup found. Creating initial full backup: {diskNumber}");
                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, 
                                          exclusionsArray, exclusionCount, progressCallback, logCallback);

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
                                                exclusionsArray, exclusionCount, progressCallback, logCallback);
                        }
                        else
                        {
                            result = BackupFiles(sourcePath, destPath, exclusionsArray, exclusionCount, progressCallback, logCallback);
                        }
                    }
                    else
                    {
                        // Find the most recent backup (could be full, incremental, or differential) to base the incremental on
                        var lastBackup = FindLastBackup(job.DestinationPath, job.Name) ?? fullBackupBase;
                        logger?.Invoke($"Creating incremental backup from: {lastBackup}");
                        result = CreateIncrementalBackup(sourcePath, destPath, lastBackup, progressCallback);
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
                        result = BackupDiskDifferential(diskNumber, destPath, job.IncludeSystemState, job.CompressData,
                            exclusionsArray, exclusionCount, progressCallback, logCallback);

                        if (result != 0)
                        {
                            logger?.Invoke($"Disk differential backup failed with code {result}");
                        }
                    }
                    else
                    {
                        logger?.Invoke($"No base backup found. Creating initial full backup: {diskNumber}");
                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, 
                                          exclusionsArray, exclusionCount, progressCallback, logCallback);

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
                                                exclusionsArray, exclusionCount, progressCallback, logCallback);
                        }
                        else
                        {
                            result = BackupFiles(sourcePath, destPath, exclusionsArray, exclusionCount, progressCallback, logCallback);
                        }
                    }
                    else
                    {
                        logger?.Invoke($"Creating differential backup from: {fullBackup}");
                        result = CreateDifferentialBackup(sourcePath, destPath, fullBackup, progressCallback);
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

