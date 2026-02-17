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
                string? oldBackupToDelete = null;
                string? renamedOldBackup = null;

                try
                {
                    logger?.Invoke($"Starting backup job: {job.Name}");
                    progressCallback?.Invoke(0, "Initializing backup...");

                    if (cancellationToken.IsCancellationRequested)
                    {
                        logger?.Invoke("Backup cancelled by user");
                        return false;
                    }

                    // Handle full backup retention - rename existing backup before starting new one
                    if (job.Type == BackupType.Full && Directory.Exists(job.DestinationPath))
                    {
                        var existingBackups = GetExistingFullBackups(job.DestinationPath, job.Name);
                        
                        if (existingBackups.Count > 0)
                        {
                            // Rename the most recent backup as a safety measure
                            var mostRecentBackup = existingBackups.OrderByDescending(b => Directory.GetCreationTime(b)).First();
                            renamedOldBackup = RenameBackupAsPending(mostRecentBackup, logger);
                            logger?.Invoke($"Existing backup renamed to: {Path.GetFileName(renamedOldBackup)}");
                        }
                    }

                    // Create native progress callback
                    ProgressCallback nativeCallback = (percentage, message) =>
                    {
                        progressCallback?.Invoke(percentage, message ?? $"Progress: {percentage}%");
                    };

                    bool backupSuccess = false;
                    string? newBackupPath = null;

                    foreach (var sourcePath in job.SourcePaths)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            logger?.Invoke("Backup cancelled by user");
                            RestoreRenamedBackup(renamedOldBackup, logger);
                            return false;
                        }

                        // Create destination path with timestamp if retaining multiple backups
                        if (job.Type == BackupType.Full && job.RetainFullBackupCount > 1)
                        {
                            newBackupPath = Path.Combine(job.DestinationPath,
                                $"{job.Name}_{DateTime.Now:yyyyMMdd_HHmmss}");
                        }
                        else
                        {
                            newBackupPath = Path.Combine(job.DestinationPath,
                                $"{job.Name}_{DateTime.Now:yyyyMMdd_HHmmss}");
                        }

                        int result = ExecuteBackup(job, sourcePath, newBackupPath, nativeCallback, logger);

                        if (result != 0)
                        {
                            var error = new StringBuilder(1024);
                            GetLastErrorMessage(error, error.Capacity);
                            logger?.Invoke($"Backup failed: {error}");
                            RestoreRenamedBackup(renamedOldBackup, logger);
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
                                RestoreRenamedBackup(renamedOldBackup, logger);
                                return false;
                            }

                            if (job.Type == BackupType.Full && job.RetainFullBackupCount > 1)
                            {
                                newBackupPath = Path.Combine(job.DestinationPath,
                                    $"{vm}_{DateTime.Now:yyyyMMdd_HHmmss}");
                            }
                            else
                            {
                                newBackupPath = Path.Combine(job.DestinationPath,
                                    $"{vm}_{DateTime.Now:yyyyMMdd_HHmmss}");
                            }
                            
                            progressCallback?.Invoke(0, $"Backing up Hyper-V VM: {vm}...");
                            int result = BackupHyperVVM(vm, newBackupPath, nativeCallback);

                            if (result != 0)
                            {
                                var error = new StringBuilder(1024);
                                GetLastErrorMessage(error, error.Capacity);
                                logger?.Invoke($"Hyper-V backup failed: {error}");
                                RestoreRenamedBackup(renamedOldBackup, logger);
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
                            logger?.Invoke("Backup verification failed! Restoring previous backup...");
                            RestoreRenamedBackup(renamedOldBackup, logger);
                            
                            // Delete the failed backup
                            if (newBackupPath != null && Directory.Exists(newBackupPath))
                            {
                                try
                                {
                                    Directory.Delete(newBackupPath, true);
                                    logger?.Invoke($"Failed backup deleted: {newBackupPath}");
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

                    // Only clean up old backups after successful verification (or if verification disabled)
                    if (backupSuccess && job.Type == BackupType.Full)
                    {
                        // Delete the renamed backup now that the new one is verified
                        if (renamedOldBackup != null && Directory.Exists(renamedOldBackup))
                        {
                            try
                            {
                                oldBackupToDelete = renamedOldBackup;
                                logger?.Invoke($"Deleting old backup: {Path.GetFileName(renamedOldBackup)}");
                                Directory.Delete(renamedOldBackup, true);
                                logger?.Invoke("Old backup deleted successfully");
                                renamedOldBackup = null; // Prevent restore attempt
                            }
                            catch (Exception ex)
                            {
                                logger?.Invoke($"Warning: Could not delete old backup: {ex.Message}");
                            }
                        }

                        // Clean up excess backups based on retention policy
                        if (job.RetainFullBackupCount > 1)
                        {
                            CleanupOldBackups(job.DestinationPath, job.Name, job.RetainFullBackupCount, logger);
                        }
                    }

                    progressCallback?.Invoke(100, "Backup completed successfully!");
                    logger?.Invoke($"Backup job completed successfully: {job.Name}");
                    return true;
                }
                catch (Exception ex)
                {
                    logger?.Invoke($"Backup job failed with exception: {ex.Message}");
                    RestoreRenamedBackup(renamedOldBackup, logger);
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

            switch (job.Type)
            {
                case BackupType.Full:
                    if (job.Target == BackupTarget.Volume)
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
                break;

            case BackupType.Differential:
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

                return Directory.GetDirectories(destPath, $"{jobName}_*")
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
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

                // Look for folders starting with Full_
                var fullBackups = Directory.GetDirectories(destPath, $"{jobName}_*")
                    .Where(d => Path.GetFileName(d).Contains("Full_") || !Path.GetFileName(d).Contains("Incremental_") && !Path.GetFileName(d).Contains("Differential_"))
                    .OrderByDescending(d => Directory.GetCreationTime(d))
                    .ToList();

                return fullBackups.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private List<string> GetExistingFullBackups(string destPath, string jobName)
        {
            try
            {
                if (!Directory.Exists(destPath))
                    return new List<string>();

                // Find all full backup directories (excluding _PENDING_ and _OLD_)
                return Directory.GetDirectories(destPath, $"{jobName}_*")
                    .Where(d =>
                    {
                        var fileName = Path.GetFileName(d);
                        return !fileName.Contains("_PENDING_") && 
                               !fileName.Contains("_OLD_") &&
                               !fileName.Contains("Incremental_") && 
                               !fileName.Contains("Differential_");
                    })
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private string? RenameBackupAsPending(string backupPath, Action<string>? logger)
        {
            try
            {
                var dirInfo = new DirectoryInfo(backupPath);
                var parentDir = dirInfo.Parent?.FullName;
                
                if (parentDir == null)
                    return null;

                var newName = $"{dirInfo.Name}_PENDING_{DateTime.Now:yyyyMMddHHmmss}";
                var newPath = Path.Combine(parentDir, newName);

                Directory.Move(backupPath, newPath);
                logger?.Invoke($"Renamed existing backup for safety: {dirInfo.Name} -> {newName}");
                
                return newPath;
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Warning: Could not rename existing backup: {ex.Message}");
                return null;
            }
        }

        private void RestoreRenamedBackup(string? renamedBackupPath, Action<string>? logger)
        {
            if (string.IsNullOrEmpty(renamedBackupPath) || !Directory.Exists(renamedBackupPath))
                return;

            try
            {
                var dirInfo = new DirectoryInfo(renamedBackupPath);
                var parentDir = dirInfo.Parent?.FullName;
                
                if (parentDir == null)
                    return;

                // Remove the _PENDING_timestamp suffix to restore original name
                var originalName = dirInfo.Name;
                var pendingIndex = originalName.IndexOf("_PENDING_");
                
                if (pendingIndex > 0)
                {
                    originalName = originalName.Substring(0, pendingIndex);
                    var restoredPath = Path.Combine(parentDir, originalName);

                    // Only restore if original path doesn't exist
                    if (!Directory.Exists(restoredPath))
                    {
                        Directory.Move(renamedBackupPath, restoredPath);
                        logger?.Invoke($"Restored previous backup: {originalName}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Warning: Could not restore renamed backup: {ex.Message}");
            }
        }

        private void CleanupOldBackups(string destPath, string jobName, int retainCount, Action<string>? logger)
        {
            try
            {
                var existingBackups = GetExistingFullBackups(destPath, jobName);
                
                if (existingBackups.Count <= retainCount)
                {
                    logger?.Invoke($"Retention policy: Keeping {existingBackups.Count} backup(s) (limit: {retainCount})");
                    return;
                }

                // Sort by creation time, newest first
                var sortedBackups = existingBackups
                    .OrderByDescending(b => Directory.GetCreationTime(b))
                    .ToList();

                // Keep the most recent 'retainCount' backups, delete the rest
                var backupsToDelete = sortedBackups.Skip(retainCount).ToList();

                logger?.Invoke($"Retention policy: Deleting {backupsToDelete.Count} old backup(s), keeping {retainCount} most recent");

                foreach (var oldBackup in backupsToDelete)
                {
                    try
                    {
                        var backupName = Path.GetFileName(oldBackup);
                        logger?.Invoke($"Deleting old backup: {backupName}");
                        Directory.Delete(oldBackup, true);
                        logger?.Invoke($"Deleted: {backupName}");
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke($"Warning: Could not delete backup {Path.GetFileName(oldBackup)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Warning: Error during backup cleanup: {ex.Message}");
            }
        }
    }
}
