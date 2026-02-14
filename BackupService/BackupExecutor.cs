using System;
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
                try
                {
                    logger?.Invoke($"Starting backup job: {job.Name}");
                    progressCallback?.Invoke(0, "Initializing backup...");

                    if (cancellationToken.IsCancellationRequested)
                    {
                        logger?.Invoke("Backup cancelled by user");
                        return false;
                    }

                    // Create native progress callback
                    ProgressCallback nativeCallback = (percentage, message) =>
                    {
                        progressCallback?.Invoke(percentage, message ?? $"Progress: {percentage}%");
                    };

                    foreach (var sourcePath in job.SourcePaths)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            logger?.Invoke("Backup cancelled by user");
                            return false;
                        }

                        var destPath = Path.Combine(job.DestinationPath,
                            $"{job.Name}_{DateTime.Now:yyyyMMdd_HHmmss}");

                        int result = ExecuteBackup(job, sourcePath, destPath, nativeCallback, logger);

                        if (result != 0)
                        {
                            var error = new StringBuilder(1024);
                            GetLastErrorMessage(error, error.Capacity);
                            logger?.Invoke($"Backup failed: {error}");
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

                            var destPath = Path.Combine(job.DestinationPath,
                                $"{vm}_{DateTime.Now:yyyyMMdd_HHmmss}");
                            
                            progressCallback?.Invoke(0, $"Backing up Hyper-V VM: {vm}...");
                            int result = BackupHyperVVM(vm, destPath, nativeCallback);

                            if (result != 0)
                            {
                                var error = new StringBuilder(1024);
                                GetLastErrorMessage(error, error.Capacity);
                                logger?.Invoke($"Hyper-V backup failed: {error}");
                                return false;
                            }
                        }
                    }

                    if (job.VerifyAfterBackup && !cancellationToken.IsCancellationRequested)
                    {
                        logger?.Invoke("Verifying backup...");
                        progressCallback?.Invoke(90, "Verifying backup...");
                        int result = VerifyBackup(job.DestinationPath, nativeCallback);
                        if (result != 0)
                        {
                            logger?.Invoke("Backup verification failed!");
                            return false;
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
    }
}
