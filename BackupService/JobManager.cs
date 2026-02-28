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
                }

                if (job.Schedule.NextRunTime <= now)
                {
                    dueJobs.Add(job);
                }
            }

            return dueJobs;
        }

        public void UpdateJobAfterExecution(BackupJob job)
        {
            job.LastRunTime = DateTime.Now;
            CalculateNextRunTime(job, isInitialCalculation: false);
            SaveJobs();
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
            }
            catch
            {
                // Log error
            }
        }
    }
}
