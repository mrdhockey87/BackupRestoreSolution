using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackupService
{
    public class BackupSchedulerService : BackgroundService
    {
        private readonly ILogger<BackupSchedulerService> _logger;
        private readonly JobManager _jobManager;
        private readonly BackupExecutor _backupExecutor;
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BackupRestoreService",
            "service.log");

        public BackupSchedulerService(
            ILogger<BackupSchedulerService> logger,
            JobManager jobManager,
            BackupExecutor backupExecutor)
        {
            _logger = logger;
            _jobManager = jobManager;
            _backupExecutor = backupExecutor;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Backup Scheduler Service started at: {time}", DateTimeOffset.Now);
            LogToFile("Backup Scheduler Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var dueJobs = _jobManager.GetJobsDueForExecution();

                    foreach (var job in dueJobs)
                    {
                        if (stoppingToken.IsCancellationRequested)
                            break;

                        _logger.LogInformation("Executing scheduled job: {jobName}", job.Name);
                        LogToFile($"Executing scheduled job: {job.Name}");

                        bool success = await _backupExecutor.ExecuteBackupJob(job, LogToFile);

                        if (success)
                        {
                            _logger.LogInformation("Job completed successfully: {jobName}", job.Name);
                            LogToFile($"Job completed successfully: {job.Name}");
                        }
                        else
                        {
                            _logger.LogError("Job failed: {jobName}", job.Name);
                            LogToFile($"Job failed: {job.Name}");
                        }

                        _jobManager.UpdateJobAfterExecution(job);
                    }

                    // Check for jobs every minute
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Service is stopping
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in backup scheduler");
                    LogToFile($"Error in backup scheduler: {ex.Message}");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("Backup Scheduler Service stopped at: {time}", DateTimeOffset.Now);
            LogToFile("Backup Scheduler Service stopped");
        }

        private void LogToFile(string message)
        {
            try
            {
                var directory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, logMessage);
            }
            catch
            {
                // Ignore logging errors
            }
        }
    }
}
