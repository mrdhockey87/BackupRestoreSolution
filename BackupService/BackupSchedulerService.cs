using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecureServerBackupCommon;

namespace SecureServerBackupService
{
    public class BackupSchedulerService : BackgroundService
    {
        private const string ErrorPrefix = "[ERROR]";
        private const string WarningPrefix = "Warning:";
        private const string DebugPrefix = "[DEBUG]";
        private readonly ILogger<BackupSchedulerService> _logger;
        private readonly JobManager _jobManager;
        private readonly BackupExecutor _backupExecutor;
        private readonly BackupProgressTracker _progressTracker;
        private readonly BackupServiceCommunication _communication;
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SecureServerBackupService",
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
                // Mark job as running (prevents concurrent execution)
                job.IsCurrentlyRunning = true;
                _jobManager.SaveJob(job); // Persist the running state

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
                        LogJobMessage(job, message, job.DestinationPath);
                    });

                // Check if the job was cancelled
                bool wasCancelled = _progressTracker.GetCancellationToken(job.Id).IsCancellationRequested;

                // If backup succeeded and verification is requested, run verification
                if (success && job.VerifyAfterBackup && !_progressTracker.GetCancellationToken(job.Id).IsCancellationRequested)
                {
                    _logger.LogInformation("Starting verification after backup completion for job: {jobName}", job.Name);
                    LogToFile($"Starting verification after backup completion for: {job.Name}");
                    BackupLogger.LogInfo(job.Name, "Starting backup verification after completion");

                    // Transition to verification phase
                    _progressTracker.StartVerification(job.Id);

                    bool verifySuccess = await _backupExecutor.VerifyBackupWithProgress(
                        job,
                        (percentage, message) => _progressTracker.UpdateProgress(job.Id, percentage, message),
                        _progressTracker.GetCancellationToken(job.Id),
                        message => {
                            LogToFile(message);
                            LogJobMessage(job, message);
                        });

                    if (verifySuccess)
                    {
                        _logger.LogInformation("Verification completed successfully for job: {jobName}", job.Name);
                        LogToFile($"Verification completed successfully for: {job.Name}");
                        _progressTracker.UpdateProgress(job.Id, 100, "Backup completed successfully!");
                        BackupLogger.LogSuccess(job.Name, "Backup verification completed successfully");
                    }
                    else
                    {
                        _logger.LogError("Verification failed for job: {jobName}", job.Name);
                        LogToFile($"Verification failed for: {job.Name}");
                        BackupLogger.LogError(job.Name, "Backup verification failed");
                        success = false; // Mark overall job as failed if verification fails
                    }
                }

                _progressTracker.CompleteJob(job.Id, success);

                if (wasCancelled)
                {
                    _logger.LogInformation("Job was cancelled by user: {jobName}", job.Name);
                    LogToFile($"Job cancelled by user: {job.Name}");
                    BackupLogger.LogWarning(job.Name, "Backup cancelled by user");
                }
                else if (success)
                {
                    _logger.LogInformation("Job completed successfully: {jobName}", job.Name);
                    LogToFile($"Job completed successfully: {job.Name}");
                    BackupLogger.LogSuccess(job.Name, "Backup completed successfully", job.DestinationPath);
                }
                else
                {
                    _logger.LogError("Job failed: {jobName}", job.Name);
                    LogToFile($"Job failed: {job.Name}");
                    BackupLogger.LogError(job.Name, "Backup failed");
                }

                // UpdateJobAfterExecution will clear IsCurrentlyRunning flag
                _jobManager.UpdateJobAfterExecution(job, success, wasCancelled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing job: {jobName}", job.Name);
                LogToFile($"Error executing job {job.Name}: {ex.Message}");
                LogToFile($"[EXCEPTION] Stack trace: {ex.StackTrace}");
                BackupLogger.LogError(job.Name, "Error executing backup job", ex.Message);
                _progressTracker.CompleteJob(job.Id, false, ex.Message);

                // CRITICAL: Update job state even on exception to prevent infinite loop
                // UpdateJobAfterExecution will clear IsCurrentlyRunning flag
                _jobManager.UpdateJobAfterExecution(job, success: false, wasCancelled: false);
            }
        }

        internal static void LogJobMessage(BackupJob job, string message, string details = "")
        {
            ArgumentNullException.ThrowIfNull(job);

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (message.StartsWith(ErrorPrefix, StringComparison.OrdinalIgnoreCase))
            {
                BackupLogger.LogError(job.Name, message, details);
                return;
            }

            if (message.StartsWith(WarningPrefix, StringComparison.OrdinalIgnoreCase))
            {
                BackupLogger.LogWarning(job.Name, message, details);
                return;
            }

            if (message.StartsWith(DebugPrefix, StringComparison.OrdinalIgnoreCase))
            {
                BackupLogger.LogInfo(job.Name, message, details);
                return;
            }

            if (message.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("completed", StringComparison.OrdinalIgnoreCase))
            {
                BackupLogger.LogSuccess(job.Name, message, details);
                return;
            }

            BackupLogger.LogInfo(job.Name, message, details);
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
