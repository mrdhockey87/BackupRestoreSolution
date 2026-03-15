using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BackupCommon;

namespace BackupService
{
    public class JobManager
    {
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
            return jobs.ToList();
        }

        public BackupJob? GetJob(Guid id)
        {
            return jobs.FirstOrDefault(j => j.Id == id);
        }

        public List<BackupJob> GetScheduledJobs()
        {
            return jobs.Where(j => j.Schedule != null && j.Schedule.Enabled).ToList();
        }

        public List<BackupJob> GetJobsDueForExecution()
        {
            var now = DateTime.Now;
            var dueJobs = new List<BackupJob>();

            foreach (var job in GetScheduledJobs())
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
            job.LastRunTime = DateTime.Now;
            job.IsCurrentlyRunning = false; // Mark job as no longer running

            if (!success)
            {
                // Increment consecutive failure counter
                job.ConsecutiveFailures++;

                // Exponential backoff retry logic:
                // 1st failure: +15 minutes
                // 2nd failure: +30 minutes
                // 3rd failure: +1 hour
                // After 3 failures: Wait for next scheduled day

                DateTime retryTime;
                string retryMessage;

                if (job.ConsecutiveFailures == 1)
                {
                    // First failure: retry in 15 minutes
                    retryTime = DateTime.Now.AddMinutes(15);
                    retryMessage = $"First failure. Will retry in 15 minutes at {retryTime:HH:mm:ss}.";
                }
                else if (job.ConsecutiveFailures == 2)
                {
                    // Second failure: retry in 30 minutes
                    retryTime = DateTime.Now.AddMinutes(30);
                    retryMessage = $"Second failure. Will retry in 30 minutes at {retryTime:HH:mm:ss}.";
                }
                else if (job.ConsecutiveFailures == 3)
                {
                    // Third failure: retry in 1 hour (last chance before giving up)
                    retryTime = DateTime.Now.AddHours(1);
                    retryMessage = $"Third failure (LAST CHANCE). Will retry in 1 hour at {retryTime:HH:mm:ss}.";
                }
                else
                {
                    // After 3 failures: Give up and wait for next scheduled day
                    CalculateNextRunTime(job, isInitialCalculation: false);
                    retryTime = job.NextScheduledRun ?? DateTime.Now.AddDays(1);

                    BackupLogger.LogError(
                        jobName: job.Name,
                        message: $"⛔ RETRY LIMIT REACHED - Failed {job.ConsecutiveFailures} times. No more automatic retries.",
                        details: $"Next scheduled backup: {retryTime:yyyy-MM-dd HH:mm:ss}. Please investigate the failure cause before next backup attempt."
                    );

                    SaveJobs();
                    return;
                }

                // Check if retry time would be after the next natural schedule
                var nextNaturalSchedule = CalculateNaturalNextRunTime(job);
                if (nextNaturalSchedule.HasValue && retryTime >= nextNaturalSchedule.Value)
                {
                    // Retry time is past natural schedule, just use natural schedule
                    job.NextScheduledRun = nextNaturalSchedule.Value;
                    job.ConsecutiveFailures = 0; // Reset since we're using natural schedule

                    BackupLogger.LogWarning(
                        jobName: job.Name,
                        message: $"Backup failed but retry time is past next scheduled backup. Using natural schedule instead.",
                        details: $"Next backup: {job.NextScheduledRun:yyyy-MM-dd HH:mm:ss}"
                    );
                }
                else
                {
                    // Use retry time
                    job.NextScheduledRun = retryTime;

                    BackupLogger.LogWarning(
                        jobName: job.Name,
                        message: $"Backup attempt {job.ConsecutiveFailures} of 3 failed. {retryMessage}",
                        details: $"Next retry: {job.NextScheduledRun:yyyy-MM-dd HH:mm:ss}"
                    );
                }
            }
            else
            {
                // Backup succeeded
                var hadFailures = job.ConsecutiveFailures > 0;
                job.ConsecutiveFailures = 0; // Reset failure counter

                // Calculate next normal run time
                CalculateNextRunTime(job, isInitialCalculation: false);

                if (hadFailures)
                {
                    BackupLogger.LogSuccess(
                        jobName: job.Name,
                        message: "✓ Backup succeeded after previous failures. Failure counter reset.",
                        details: $"Next scheduled backup: {job.NextScheduledRun:yyyy-MM-dd HH:mm:ss}"
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
            try
            {
                if (File.Exists(JobsFilePath))
                {
                    var json = File.ReadAllText(JobsFilePath);
                    jobs = JsonSerializer.Deserialize<List<BackupJob>>(json) ?? new List<BackupJob>();
                }
            }
            catch
            {
                jobs = new List<BackupJob>();
            }
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
            // Just save the in-memory list (job is already in list by reference)
            SaveJobs();
        }

        private void SaveJobs()
        {
            try
            {
                var directory = Path.GetDirectoryName(JobsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(jobs, options);
                File.WriteAllText(JobsFilePath, json);
                System.Diagnostics.Debug.WriteLine($"[SAVE SUCCESS] Jobs saved successfully to {JobsFilePath}");
            }
            catch (Exception ex)
            {
                // CRITICAL ERROR: If save fails, ConsecutiveFailures won't persist!
                System.Diagnostics.Debug.WriteLine($"[CRITICAL ERROR] Failed to save jobs: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[CRITICAL ERROR] Stack trace: {ex.StackTrace}");
                // Log to service log file as well
                try
                {
                    var logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "BackupRestoreService",
                        "save_error.log");
                    File.AppendAllText(logPath, 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Failed to save jobs: {ex.Message}\n");
                }
                catch { /* Ignore logging errors */ }
            }
        }
    }
}
