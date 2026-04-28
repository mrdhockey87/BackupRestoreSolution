using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SecureServerBackupCommon;

namespace SecureServerBackupService
{
    public class JobManager
    {
        private const string JobsFileMutexName = @"Local\SecureServerBackup_JobsFileMutex";
        private static readonly Mutex JobsFileMutex = new(false, JobsFileMutexName);
        private static readonly string JobsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BackupRestoreService",
            "jobs.json");

        private List<BackupJob> jobs = new();

        public JobManager()
        {
            LoadJobs();
        }

        public List<BackupJob> GetAllJobs()
        {
            LoadJobs();
            return jobs.ToList();
        }

        public BackupJob? GetJob(Guid id)
        {
            LoadJobs();
            return jobs.FirstOrDefault(j => j.Id == id);
        }

        public List<BackupJob> GetScheduledJobs()
        {
            LoadJobs();
            return jobs.Where(j => j.Schedule != null && j.Schedule.Enabled).ToList();
        }

        public List<BackupJob> GetJobsDueForExecution()
        {
            LoadJobs();

            var now = DateTime.Now;
            var dueJobs = new List<BackupJob>();

            foreach (var job in jobs.Where(j => j.Schedule != null && j.Schedule.Enabled))
            {
                if (job.Schedule == null)
                    continue;

                // Skip if job is currently running (prevents concurrent execution)
                if (job.IsCurrentlyRunning)
                {
                    System.Diagnostics.Debug.WriteLine($"[SCHEDULING] Job '{job.Name}' is already running, skipping");
                    continue;
                }

                // Initialize NextScheduledRun on first load
                if (!job.NextScheduledRun.HasValue)
                {
                    // Calculate future run time (don't trigger immediately on first load)
                    CalculateNextRunTime(job, isInitialCalculation: true);
                    SaveJobs(); // Persist the calculated time
                    System.Diagnostics.Debug.WriteLine($"[SCHEDULING] Job '{job.Name}' NextScheduledRun initialized to {job.NextScheduledRun:yyyy-MM-dd HH:mm:ss}");
                }

                // Check if job is due
                if (job.NextScheduledRun.HasValue && job.NextScheduledRun.Value <= now)
                {
                    dueJobs.Add(job);
                    System.Diagnostics.Debug.WriteLine($"[SCHEDULING] Job '{job.Name}' is due (NextScheduledRun={job.NextScheduledRun:yyyy-MM-dd HH:mm:ss}, Now={now:yyyy-MM-dd HH:mm:ss})");
                }
                else if (job.NextScheduledRun.HasValue)
                {
                    var timeUntilDue = job.NextScheduledRun.Value - now;
                    System.Diagnostics.Debug.WriteLine($"[SCHEDULING] Job '{job.Name}' not due yet. Time until next run: {timeUntilDue.TotalMinutes:F1} minutes");
                }
            }

            return dueJobs;
        }

        public void UpdateJobAfterExecution(BackupJob job, bool success = true)
        {
            LoadJobs();

            var currentJob = jobs.FirstOrDefault(j => j.Id == job.Id);
            if (currentJob == null)
            {
                currentJob = job;
                jobs.Add(currentJob);
            }

            currentJob.LastRunTime = DateTime.Now;
            currentJob.IsCurrentlyRunning = false; // Mark job as no longer running

            if (!success)
            {
                // Increment consecutive failure counter
                currentJob.ConsecutiveFailures++;

                // Exponential backoff retry logic:
                // 1st failure: +15 minutes
                // 2nd failure: +30 minutes
                // 3rd failure: +1 hour
                // After 3 failures: Wait for next scheduled day

                DateTime retryTime;
                string retryMessage;

                if (currentJob.ConsecutiveFailures == 1)
                {
                    // First failure: retry in 15 minutes
                    retryTime = DateTime.Now.AddMinutes(15);
                    retryMessage = $"First failure. Will retry in 15 minutes at {retryTime:HH:mm:ss}.";
                }
                else if (currentJob.ConsecutiveFailures == 2)
                {
                    // Second failure: retry in 30 minutes
                    retryTime = DateTime.Now.AddMinutes(30);
                    retryMessage = $"Second failure. Will retry in 30 minutes at {retryTime:HH:mm:ss}.";
                }
                else if (currentJob.ConsecutiveFailures == 3)
                {
                    // Third failure: retry in 1 hour (last chance before giving up)
                    retryTime = DateTime.Now.AddHours(1);
                    retryMessage = $"Third failure (LAST CHANCE). Will retry in 1 hour at {retryTime:HH:mm:ss}.";
                }
                else
                {
                    // After 3 failures: Give up and wait for next scheduled day
                    CalculateNextRunTime(currentJob, isInitialCalculation: false);
                    retryTime = currentJob.NextScheduledRun ?? DateTime.Now.AddDays(1);

                    BackupLogger.LogError(
                        jobName: currentJob.Name,
                        message: $"⛔ RETRY LIMIT REACHED - Failed {currentJob.ConsecutiveFailures} times. No more automatic retries.",
                        details: $"Next scheduled backup: {retryTime:yyyy-MM-dd HH:mm:ss}. Please investigate the failure cause before next backup attempt."
                    );

                    SaveJobs();
                    return;
                }

                // Check if retry time would be after the next natural schedule
                var nextNaturalSchedule = CalculateNaturalNextRunTime(currentJob);
                if (nextNaturalSchedule.HasValue && retryTime >= nextNaturalSchedule.Value)
                {
                    // Retry time is past natural schedule, just use natural schedule
                    currentJob.NextScheduledRun = nextNaturalSchedule.Value;
                    currentJob.ConsecutiveFailures = 0; // Reset since we're using natural schedule

                    BackupLogger.LogWarning(
                        jobName: currentJob.Name,
                        message: $"Backup failed but retry time is past next scheduled backup. Using natural schedule instead.",
                        details: $"Next backup: {currentJob.NextScheduledRun:yyyy-MM-dd HH:mm:ss}"
                    );
                }
                else
                {
                    // Use retry time
                    currentJob.NextScheduledRun = retryTime;

                    BackupLogger.LogWarning(
                        jobName: currentJob.Name,
                        message: $"Backup attempt {currentJob.ConsecutiveFailures} of 3 failed. {retryMessage}",
                        details: $"Next retry: {currentJob.NextScheduledRun:yyyy-MM-dd HH:mm:ss}"
                    );
                }
            }
            else
            {
                // Backup succeeded
                var hadFailures = currentJob.ConsecutiveFailures > 0;
                currentJob.ConsecutiveFailures = 0; // Reset failure counter

                // Calculate next normal run time
                CalculateNextRunTime(currentJob, isInitialCalculation: false);

                if (hadFailures)
                {
                    BackupLogger.LogSuccess(
                        jobName: currentJob.Name,
                        message: "✓ Backup succeeded after previous failures. Failure counter reset.",
                        details: $"Next scheduled backup: {currentJob.NextScheduledRun:yyyy-MM-dd HH:mm:ss}"
                    );
                }
            }

            SaveJobs();
        }

        private void CalculateNextRunTime(BackupJob job, bool isInitialCalculation = false)
        {
            if (job.Schedule == null)
                return;

            var nextRun = CalculateNaturalNextRunTime(job, isInitialCalculation);
            job.NextScheduledRun = nextRun;

            // For backward compatibility, also update Schedule.NextRunTime
            if (job.Schedule != null)
            {
                job.Schedule.NextRunTime = nextRun;
            }
        }

        private DateTime? CalculateNaturalNextRunTime(BackupJob job, bool isInitialCalculation = false)
        {
            if (job.Schedule == null)
                return null;

            var now = DateTime.Now;
            var scheduledTime = now.Date.Add(job.Schedule.Time);

            switch (job.Schedule.Frequency)
            {
                case ScheduleFrequency.Daily:
                    // If initial calculation and time is in the past today, schedule for tomorrow
                    if (isInitialCalculation && scheduledTime <= now)
                    {
                        return scheduledTime.AddDays(1);
                    }
                    else
                    {
                        return scheduledTime > now ? scheduledTime : scheduledTime.AddDays(1);
                    }

                case ScheduleFrequency.Weekly:
                    var nextRun = scheduledTime > now ? scheduledTime : scheduledTime.AddDays(1);
                    while (!job.Schedule.DaysOfWeek.Contains(nextRun.DayOfWeek))
                    {
                        nextRun = nextRun.AddDays(1);
                    }
                    return nextRun;

                case ScheduleFrequency.Monthly:
                    var nextMonth = new DateTime(now.Year, now.Month, job.Schedule.DayOfMonth,
                        job.Schedule.Time.Hours, job.Schedule.Time.Minutes, 0);
                    if (nextMonth <= now)
                        nextMonth = nextMonth.AddMonths(1);
                    return nextMonth;

                case ScheduleFrequency.Once:
                    job.Schedule.Enabled = false;
                    return null;

                default:
                    return null;
            }
        }

        private void LoadJobs()
        {
            ExecuteWithJobsFileLock(() =>
            {
                try
                {
                    if (File.Exists(JobsFilePath))
                    {
                        var json = File.ReadAllText(JobsFilePath);
                        jobs = JsonSerializer.Deserialize<List<BackupJob>>(json) ?? new List<BackupJob>();
                    }
                    else
                    {
                        jobs = new List<BackupJob>();
                    }
                }
                catch
                {
                    jobs = new List<BackupJob>();
                }

                return 0;
            });
        }

        public void UpdateJob(BackupJob updatedJob)
        {
            var existingJob = jobs.FirstOrDefault(j => j.Id == updatedJob.Id);
            if (existingJob != null)
            {
                // Remove old and add updated
                jobs.Remove(existingJob);
                jobs.Add(updatedJob);
                SaveJobs();
                System.Diagnostics.Debug.WriteLine($"[UPDATE] Job '{updatedJob.Name}' updated successfully");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[UPDATE ERROR] Job '{updatedJob.Name}' not found");
            }
        }

        public void SaveJob(BackupJob job)
        {
            LoadJobs();

            var existingJob = jobs.FirstOrDefault(j => j.Id == job.Id);
            if (existingJob == null)
            {
                jobs.Add(job);
            }
            else
            {
                existingJob.LastRunTime = job.LastRunTime;
                existingJob.NextScheduledRun = job.NextScheduledRun;
                existingJob.IsCurrentlyRunning = job.IsCurrentlyRunning;
                existingJob.ConsecutiveFailures = job.ConsecutiveFailures;
                existingJob.ForceFullBackupOnNextRun = job.ForceFullBackupOnNextRun;

                if (existingJob.Schedule != null && job.Schedule != null)
                {
                    existingJob.Schedule.NextRunTime = job.Schedule.NextRunTime;
                }
            }

            SaveJobs();
        }

        private void SaveJobs()
        {
            ExecuteWithJobsFileLock(() =>
            {
                string? tempFilePath = null;

                try
                {
                    var directory = Path.GetDirectoryName(JobsFilePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(jobs, options);
                    tempFilePath = $"{JobsFilePath}.{Guid.NewGuid():N}.tmp";
                    File.WriteAllText(tempFilePath, json);

                    if (File.Exists(JobsFilePath))
                    {
                        File.Replace(tempFilePath, JobsFilePath, null, true);
                    }
                    else
                    {
                        File.Move(tempFilePath, JobsFilePath);
                    }

                    System.Diagnostics.Debug.WriteLine($"[SAVE SUCCESS] Jobs saved successfully to {JobsFilePath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CRITICAL ERROR] Failed to save jobs: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[CRITICAL ERROR] Stack trace: {ex.StackTrace}");

                    try
                    {
                        var logPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                            "BackupRestoreService",
                            "save_error.log");
                        File.AppendAllText(logPath,
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Failed to save jobs: {ex.Message}\n");
                    }
                    catch
                    {
                    }

                    throw new InvalidOperationException($"Failed to save jobs file: {JobsFilePath}", ex);
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(tempFilePath) && File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }

                return 0;
            });
        }

        private static T ExecuteWithJobsFileLock<T>(Func<T> action)
        {
            bool lockTaken = false;

            try
            {
                try
                {
                    lockTaken = JobsFileMutex.WaitOne(TimeSpan.FromSeconds(15));
                }
                catch (AbandonedMutexException)
                {
                    lockTaken = true;
                }

                if (!lockTaken)
                {
                    throw new TimeoutException("Timed out waiting for jobs file access.");
                }

                return action();
            }
            finally
            {
                if (lockTaken)
                {
                    JobsFileMutex.ReleaseMutex();
                }
            }
        }
    }
}
