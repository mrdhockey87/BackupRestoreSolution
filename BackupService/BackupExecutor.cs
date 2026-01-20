using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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

        public async Task<bool> ExecuteBackupJob(BackupJob job, Action<string>? logger = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    logger?.Invoke($"Starting backup job: {job.Name}");

                    foreach (var sourcePath in job.SourcePaths)
                    {
                        var destPath = Path.Combine(job.DestinationPath,
                            $"{job.Name}_{DateTime.Now:yyyyMMdd_HHmmss}");

                        int result = ExecuteBackup(job, sourcePath, destPath, logger);

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
                            var destPath = Path.Combine(job.DestinationPath,
                                $"{vm}_{DateTime.Now:yyyyMMdd_HHmmss}");
                            
                            int result = BackupHyperVVM(vm, destPath, null);

                            if (result != 0)
                            {
                                var error = new StringBuilder(1024);
                                GetLastErrorMessage(error, error.Capacity);
                                logger?.Invoke($"Hyper-V backup failed: {error}");
                                return false;
                            }
                        }
                    }

                    if (job.VerifyAfterBackup)
                    {
                        logger?.Invoke("Verifying backup...");
                        int result = VerifyBackup(job.DestinationPath, null);
                        if (result != 0)
                        {
                            logger?.Invoke("Backup verification failed!");
                            return false;
                        }
                    }

                    logger?.Invoke($"Backup job completed successfully: {job.Name}");
                    return true;
                }
                catch (Exception ex)
                {
                    logger?.Invoke($"Backup job failed with exception: {ex.Message}");
                    return false;
                }
            });
        }

        private int ExecuteBackup(BackupJob job, string sourcePath, string destPath, Action<string>? logger)
        {
            int result;

            switch (job.Type)
            {
                case BackupType.Full:
                    if (job.Target == BackupTarget.Volume)
                    {
                        logger?.Invoke($"Backing up volume: {sourcePath}");
                        result = BackupVolume(sourcePath, destPath, job.IncludeSystemState, job.CompressData, null);
                    }
                    else
                    {
                        logger?.Invoke($"Backing up files: {sourcePath}");
                        result = BackupFiles(sourcePath, destPath, null);
                    }
                    break;

                case BackupType.Incremental:
                    var lastBackup = FindLastBackup(job.DestinationPath, job.Name);
                    logger?.Invoke($"Creating incremental backup from: {lastBackup ?? "none"}");
                    result = CreateIncrementalBackup(sourcePath, destPath, lastBackup ?? "", null);
                    break;

                case BackupType.Differential:
                    var fullBackup = FindFullBackup(job.DestinationPath, job.Name);
                    logger?.Invoke($"Creating differential backup from: {fullBackup ?? "none"}");
                    result = CreateDifferentialBackup(sourcePath, destPath, fullBackup ?? "", null);
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
            return FindLastBackup(destPath, jobName);
        }
    }
}
