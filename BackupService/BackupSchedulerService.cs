using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BackupUI.Services; // For BackupLogger

namespace BackupService
{
    public class BackupSchedulerService : BackgroundService
    {
        private readonly ILogger<BackupSchedulerService> _logger;
        private readonly JobManager _jobManager;
        private readonly BackupExecutor _backupExecutor;
        private readonly BackupProgressTracker _progressTracker;
        private readonly BackupServiceCommunication _communication;
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BackupRestoreService",
            "service.log");

        public BackupSchedulerService(
            ILogger<BackupSchedulerService> logger,
            JobManager jobManager,
            BackupExecutor backupExecutor,
            BackupProgressTracker progressTracker,
            BackupServiceCommunication communication)
        {
            _logger = logger;
            _jobManager = jobManager;
            _backupExecutor = backupExecutor;
            _progressTracker = progressTracker;
            _communication = communication;

            // Wire up communication events
            _communication.CommandReceived += OnCommandReceived;
            _communication.ProgressQueried += OnProgressQueried;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Backup Scheduler Service started at: {time}", DateTimeOffset.Now);
            LogToFile("Backup Scheduler Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // DIAGNOSTIC: Log scheduling check
                    LogToFile($"[SCHEDULING] Checking for due jobs at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                    var dueJobs = _jobManager.GetJobsDueForExecution();

                    // DIAGNOSTIC: Log how many jobs are due
                    LogToFile($"[SCHEDULING] Found {dueJobs.Count} job(s) due for execution");

                    foreach (var job in dueJobs)
                    {
                        if (stoppingToken.IsCancellationRequested)
                            break;

                        // Don't start if already running
                        if (_progressTracker.IsJobRunning(job.Id))
                        {
                            LogToFile($"[SCHEDULING] Job '{job.Name}' is already running, skipping");
                            continue;
                        }

                        _logger.LogInformation("Executing scheduled job: {jobName}", job.Name);
                        LogToFile($"Executing scheduled job: {job.Name}");

                        // Execute in background
                        _ = Task.Run(() => ExecuteBackupJobAsync(job, stoppingToken), stoppingToken);
                    }

                    // Check for jobs every minute
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
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

        private void OnCommandReceived(object? sender, BackupCommandEventArgs e)
        {
            if (e.IsAbort)
            {
                _logger.LogInformation("Abort requested for job: {jobId}", e.JobId);
                _progressTracker.RequestCancellation(e.JobId);
            }
            else
            {
                var job = _jobManager.GetJob(e.JobId);
                if (job != null)
                {
                    if (_progressTracker.IsJobRunning(job.Id))
                    {
                        _logger.LogWarning("Job {jobId} is already running, ignoring duplicate run request", job.Id);
                        return;
                    }

                    _logger.LogInformation("Manual execution requested for job: {jobName}", job.Name);

                    // Initialize job tracking IMMEDIATELY so UI sees progress right away
                    _progressTracker.StartJob(job.Id);

                    // Execute backup in background (ExecuteBackupJobAsync won't call StartJob again since job is already running)
                    _ = Task.Run(() => ExecuteBackupJobAsync(job, CancellationToken.None));
                }
                else
                {
                    _logger.LogWarning("Job {jobId} not found", e.JobId);
                }
            }
        }

        private void OnProgressQueried(object? sender, ProgressQueryEventArgs e)
        {
            e.Progress = _progressTracker.GetProgress(e.JobId);
        }

        private async Task ExecuteBackupJobAsync(BackupJob job, CancellationToken stoppingToken)
        {
            try
            {
                // Only call StartJob if not already started (manual runs call StartJob in OnCommandReceived)
                if (!_progressTracker.IsJobRunning(job.Id))
                {
                    _progressTracker.StartJob(job.Id);
                }

                BackupLogger.LogInfo(job.Name, "Starting backup job execution (via service)");

                bool success = await _backupExecutor.ExecuteBackupJobWithProgress(
                    job,
                    (percentage, message) => _progressTracker.UpdateProgress(job.Id, percentage, message),
                    _progressTracker.GetCancellationToken(job.Id),
                    message => {
                        LogToFile(message);
                        // Also log to BackupLogger for UI
                        if (message.Contains("fail", StringComparison.OrdinalIgnoreCase) || 
                            message.Contains("error", StringComparison.OrdinalIgnoreCase))
                        {
                            BackupLogger.LogError(job.Name, message);
                        }
                        else if (message.Contains("success", StringComparison.OrdinalIgnoreCase) || 
                                 message.Contains("completed", StringComparison.OrdinalIgnoreCase))
                        {
                            BackupLogger.LogSuccess(job.Name, message, job.DestinationPath);
                        }
                        else
                        {
                            BackupLogger.LogInfo(job.Name, message);
                        }
                    });

                _progressTracker.CompleteJob(job.Id, success);

                if (success)
                {
                    _logger.LogInformation("Job completed successfully: {jobName}", job.Name);
                    LogToFile($"Job completed successfully: {job.Name}");
                    BackupLogger.LogSuccess(job.Name, "Backup completed successfully", job.DestinationPath);

                    // Send success notification
                    try
                    {
                        BackupUI.Services.NotificationService.ShowBackupSuccessNotification(job.Name);
                    }
                    catch { /* Ignore notification errors in service */ }
                }
                else
                {
                    _logger.LogError("Job failed: {jobName}", job.Name);
                    LogToFile($"Job failed: {job.Name}");
                    BackupLogger.LogError(job.Name, "Backup failed");

                    // DIAGNOSTIC: Log retry scheduling
                    LogToFile($"[SCHEDULING] Job '{job.Name}' failed, will retry in 15 minutes at {DateTime.Now.AddMinutes(15):yyyy-MM-dd HH:mm:ss}");

                    // Send failure notification
                    try
                    {
                        BackupUI.Services.NotificationService.ShowBackupFailureNotification(job.Name, "Backup failed");
                    }
                    catch { /* Ignore notification errors in service */ }
                }

                _jobManager.UpdateJobAfterExecution(job, success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing job: {jobName}", job.Name);
                LogToFile($"Error executing job {job.Name}: {ex.Message}");
                LogToFile($"[EXCEPTION] Stack trace: {ex.StackTrace}");
                BackupLogger.LogError(job.Name, "Error executing backup job", ex.Message);
                _progressTracker.CompleteJob(job.Id, false, ex.Message);

                // CRITICAL: Update NextRunTime even on exception to prevent infinite loop!
                // DIAGNOSTIC: Log retry scheduling
                LogToFile($"[SCHEDULING] Job '{job.Name}' exception, will retry in 15 minutes at {DateTime.Now.AddMinutes(15):yyyy-MM-dd HH:mm:ss}");

                _jobManager.UpdateJobAfterExecution(job, success: false);
            }
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

        // NOTE: Don't dispose _communication - it's managed by DI container as IHostedService
    }
}
