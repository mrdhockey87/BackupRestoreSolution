using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BackupService
{
    public class BackupJob
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public BackupType Type { get; set; }
        public BackupTarget Target { get; set; }
        public List<string> SourcePaths { get; set; } = new();
        public string DestinationPath { get; set; } = string.Empty;
        public bool IncludeSystemState { get; set; }
        public bool CompressData { get; set; }
        public bool VerifyAfterBackup { get; set; }
        public DateTime? LastRunTime { get; set; }
        public BackupSchedule? Schedule { get; set; }
        public bool IsHyperVBackup { get; set; }
        public List<string> HyperVMachines { get; set; } = new();
        public int RetainFullBackupCount { get; set; } = 1; // Default: keep only 1 full backup
        public int ConsecutiveFailures { get; set; } = 0; // Track consecutive backup failures for retry limit

        // Auto-recovery: Force full backup on next run if incremental/differential verification fails
        public bool ForceFullBackupOnNextRun { get; set; } = false;
    }

    public class BackupSchedule
    {
        public Guid JobId { get; set; }
        public bool Enabled { get; set; }
        public ScheduleFrequency Frequency { get; set; }
        public TimeSpan Time { get; set; }
        public List<DayOfWeek> DaysOfWeek { get; set; } = new();
        public int DayOfMonth { get; set; }
        public DateTime? NextRunTime { get; set; }
    }

    public enum BackupType
    {
        Full,
        Incremental,
        Differential,
        CloneToDisk,
        CloneToVirtualDisk,
        CloneHyperVSystem
    }

    public enum BackupTarget
    {
        Disk,
        Volume,
        FilesAndFolders
    }

    public enum ScheduleFrequency
    {
        Daily,
        Weekly,
        Monthly,
        Once
    }

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

                if (job.Schedule.NextRunTime == null)
                {
                    // IMPORTANT: When NextRunTime is null (first time or not saved),
                    // calculate it for the FUTURE - don't trigger immediately!
                    // Set a flag to indicate this is initial calculation
                    CalculateNextRunTime(job, isInitialCalculation: true);
                    SaveJobs(); // Save the calculated NextRunTime so it persists

                    // LOG: Show what we calculated
                    if (job.Schedule.NextRunTime.HasValue)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SCHEDULING] Job '{job.Name}' NextRunTime set to {job.Schedule.NextRunTime.Value:yyyy-MM-dd HH:mm:ss} (initial calculation)");
                    }
                }

                // LOG: Show comparison
                if (job.Schedule.NextRunTime.HasValue)
                {
                    var timeUntilDue = job.Schedule.NextRunTime.Value - now;
                    System.Diagnostics.Debug.WriteLine($"[SCHEDULING] Job '{job.Name}': NextRun={job.Schedule.NextRunTime.Value:yyyy-MM-dd HH:mm:ss}, Now={now:yyyy-MM-dd HH:mm:ss}, TimeUntilDue={timeUntilDue.TotalMinutes:F1} minutes, IsDue={job.Schedule.NextRunTime <= now}");
                }

                if (job.Schedule.NextRunTime <= now)
                {
                    dueJobs.Add(job);
                    System.Diagnostics.Debug.WriteLine($"[SCHEDULING] Job '{job.Name}' added to due jobs list");
                }
            }

            return dueJobs;
        }

        public void UpdateJobAfterExecution(BackupJob job, bool success = true)
        {
            job.LastRunTime = DateTime.Now;

            if (!success)
            {
                // Increment consecutive failure counter
                job.ConsecutiveFailures++;

                // IMPORTANT: Maximum 3 retry attempts, then wait for next scheduled time
                // This prevents infinite retry loops on persistent failures
                if (job.ConsecutiveFailures < 3)  // FIXED: < 3 instead of <= 3 (allows attempts 1 and 2, stops at 3)
                {
                    // Schedule retry for 15 minutes from now (attempts 1-2)
                    job.Schedule!.NextRunTime = DateTime.Now.AddMinutes(15);
                    System.Diagnostics.Debug.WriteLine($"[RETRY] Job '{job.Name}' failed (attempt {job.ConsecutiveFailures}/3), will retry in 15 minutes at {job.Schedule.NextRunTime:yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    // After 2 failed attempts (ConsecutiveFailures = 3 means 3rd failure), wait for next scheduled time
                    CalculateNextRunTime(job, isInitialCalculation: false);
                    System.Diagnostics.Debug.WriteLine($"[RETRY LIMIT] Job '{job.Name}' failed {job.ConsecutiveFailures} times (max 3 attempts reached), waiting for next scheduled time: {job.Schedule!.NextRunTime:yyyy-MM-dd HH:mm:ss}");
                    // Don't reset ConsecutiveFailures here - let successful backup reset it
                }
            }
            else
            {
                // Backup succeeded - reset failure counter and calculate normal next run time
                job.ConsecutiveFailures = 0;
                CalculateNextRunTime(job, isInitialCalculation: false);
            }

            SaveJobs();

            // DIAGNOSTIC: Verify save was successful by reading back
            try
            {
                var savedJob = GetJob(job.Id);
                if (savedJob != null && savedJob.ConsecutiveFailures != job.ConsecutiveFailures)
                {
                    System.Diagnostics.Debug.WriteLine($"[CRITICAL ERROR] ConsecutiveFailures not persisted! In-memory: {job.ConsecutiveFailures}, On-disk: {savedJob.ConsecutiveFailures}");
                }
                else if (savedJob == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[CRITICAL ERROR] Job not found after save: {job.Name}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[SAVE VERIFIED] Job '{job.Name}' ConsecutiveFailures={savedJob.ConsecutiveFailures} persisted successfully");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SAVE VERIFICATION ERROR] Failed to verify save: {ex.Message}");
            }
        }

        private void CalculateNextRunTime(BackupJob job, bool isInitialCalculation = false)
        {
            if (job.Schedule == null)
                return;

            var now = DateTime.Now;
            var scheduledTime = now.Date.Add(job.Schedule.Time);

            switch (job.Schedule.Frequency)
            {
                case ScheduleFrequency.Daily:
                    // If this is initial calculation and scheduledTime is in the past,
                    // ALWAYS schedule for tomorrow to avoid immediate execution
                    if (isInitialCalculation && scheduledTime <= now)
                    {
                        job.Schedule.NextRunTime = scheduledTime.AddDays(1);
                    }
                    else
                    {
                        job.Schedule.NextRunTime = scheduledTime > now
                            ? scheduledTime
                            : scheduledTime.AddDays(1);
                    }
                    break;

                case ScheduleFrequency.Weekly:
                    var nextRun = scheduledTime > now ? scheduledTime : scheduledTime.AddDays(1);
                    while (!job.Schedule.DaysOfWeek.Contains(nextRun.DayOfWeek))
                    {
                        nextRun = nextRun.AddDays(1);
                    }
                    job.Schedule.NextRunTime = nextRun;
                    break;

                case ScheduleFrequency.Monthly:
                    var nextMonth = new DateTime(now.Year, now.Month, job.Schedule.DayOfMonth, 
                        job.Schedule.Time.Hours, job.Schedule.Time.Minutes, 0);
                    if (nextMonth <= now)
                        nextMonth = nextMonth.AddMonths(1);
                    job.Schedule.NextRunTime = nextMonth;
                    break;

                case ScheduleFrequency.Once:
                    job.Schedule.NextRunTime = null;
                    job.Schedule.Enabled = false;
                    break;
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
