using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SecureServerBackupCommon;

namespace SecureServerBackup.Services
{
    public class JobManager
    {
        private const string JobsFileMutexName = @"Local\SecureServerBackup_JobsFileMutex";
        private static readonly Mutex JobsFileMutex = new(false, JobsFileMutexName);
        private static readonly string JobsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SecureServerBackupService",
            "jobs.json");

        private List<BackupJob> jobs = new();

        public JobManager()
        {
            LoadJobs();
        }

        public void Reload()
        {
            LoadJobs();
            System.Diagnostics.Debug.WriteLine($"JobManager reloaded. {jobs.Count} jobs in memory.");
        }

        public List<BackupJob> GetAllJobs()
        {
            // Always reload from file to get latest changes
            LoadJobs();
            return jobs.ToList();
        }

        public BackupJob? GetJob(Guid id)
        {
            // Always reload from file to ensure we have the latest job data
            LoadJobs();
            return jobs.FirstOrDefault(j => j.Id == id);
        }

        public void AddJob(BackupJob job)
        {
            try
            {
                if (job.Id == Guid.Empty)
                    job.Id = Guid.NewGuid();

                ApplyScheduleState(job);

                jobs.Add(job);
                SaveJobs();
                
                System.Diagnostics.Debug.WriteLine($"Job '{job.Name}' saved successfully to {JobsFilePath}");
            }
            catch (Exception ex)
            {
                jobs.Remove(job); // Roll back
                System.Diagnostics.Debug.WriteLine($"ERROR saving job: {ex.Message}\nStack: {ex.StackTrace}");
                throw new Exception($"Failed to save backup job: {ex.Message}", ex);
            }
        }

        public void UpdateJob(BackupJob job)
        {
            try
            {
                var existingJob = jobs.FirstOrDefault(j => j.Id == job.Id);
                if (existingJob != null)
                {
                    PreserveExecutionState(existingJob, job);
                    ApplyScheduleState(job, existingJob);

                    jobs.Remove(existingJob);
                    jobs.Add(job);
                    SaveJobs();
                    System.Diagnostics.Debug.WriteLine($"Job '{job.Name}' updated successfully");
                }
                else
                {
                    throw new Exception($"Job with ID {job.Id} not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR updating job: {ex.Message}");
                throw new Exception($"Failed to update backup job: {ex.Message}", ex);
            }
        }

        public void RemoveJob(Guid id)
        {
            DeleteJob(id);
        }

        public void DeleteJob(Guid id)
        {
            var job = jobs.FirstOrDefault(j => j.Id == id);
            if (job != null)
            {
                jobs.Remove(job);
                SaveJobs();
            }
        }

        public List<BackupJob> GetScheduledJobs()
        {
            return jobs.Where(j => j.Schedule != null && j.Schedule.Enabled).ToList();
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
                        System.Diagnostics.Debug.WriteLine($"Loaded {jobs.Count} jobs from {JobsFilePath}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Jobs file not found: {JobsFilePath}");
                        jobs = new List<BackupJob>();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR loading jobs: {ex.Message}");
                    jobs = new List<BackupJob>();
                }

                return 0;
            });
        }

        private void SaveJobs()
        {
            ExecuteWithJobsFileLock(() =>
            {
                string? tempFilePath = null;

                try
                {
                    var directory = Path.GetDirectoryName(JobsFilePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        if (!Directory.Exists(directory))
                        {
                            System.Diagnostics.Debug.WriteLine($"Creating directory: {directory}");
                            Directory.CreateDirectory(directory);
                        }
                    }

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(jobs, options);
                    tempFilePath = $"{JobsFilePath}.{Guid.NewGuid():N}.tmp";

                    System.Diagnostics.Debug.WriteLine($"Saving {jobs.Count} jobs to {JobsFilePath}");
                    File.WriteAllText(tempFilePath, json);

                    if (File.Exists(JobsFilePath))
                    {
                        File.Replace(tempFilePath, JobsFilePath, null, true);
                    }
                    else
                    {
                        File.Move(tempFilePath, JobsFilePath);
                    }

                    System.Diagnostics.Debug.WriteLine($"Jobs saved successfully. File size: {new FileInfo(JobsFilePath).Length} bytes");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR in SaveJobs: {ex.Message}\nStack: {ex.StackTrace}");
                    throw new Exception($"Failed to save jobs file: {ex.Message}\nPath: {JobsFilePath}", ex);
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

        private static void PreserveExecutionState(BackupJob existingJob, BackupJob updatedJob)
        {
            updatedJob.LastRunTime = existingJob.LastRunTime;
            updatedJob.ConsecutiveFailures = existingJob.ConsecutiveFailures;
            updatedJob.IsCurrentlyRunning = existingJob.IsCurrentlyRunning;
            updatedJob.ForceFullBackupOnNextRun = existingJob.ForceFullBackupOnNextRun;
        }

        private static void ApplyScheduleState(BackupJob job, BackupJob? existingJob = null)
        {
            if (job.Schedule == null || !job.Schedule.Enabled)
            {
                job.NextScheduledRun = null;
                if (job.Schedule != null)
                {
                    job.Schedule.NextRunTime = null;
                }

                return;
            }

            bool scheduleChanged = existingJob == null || HasScheduleChanged(existingJob.Schedule, job.Schedule);

            if (!scheduleChanged && existingJob != null)
            {
                job.NextScheduledRun = existingJob.NextScheduledRun;
                job.Schedule.NextRunTime = existingJob.Schedule?.NextRunTime ?? existingJob.NextScheduledRun;
                return;
            }

            var nextRun = CalculateNextRunTime(job.Schedule, isInitialCalculation: true);
            job.NextScheduledRun = nextRun;
            job.Schedule.NextRunTime = nextRun;
        }

        private static bool HasScheduleChanged(BackupSchedule? existingSchedule, BackupSchedule? updatedSchedule)
        {
            if (existingSchedule == null || updatedSchedule == null)
            {
                return existingSchedule != updatedSchedule;
            }

            if (existingSchedule.Enabled != updatedSchedule.Enabled ||
                existingSchedule.Frequency != updatedSchedule.Frequency ||
                existingSchedule.Time != updatedSchedule.Time ||
                existingSchedule.DayOfMonth != updatedSchedule.DayOfMonth)
            {
                return true;
            }

            var existingDays = existingSchedule.DaysOfWeek.OrderBy(day => day).ToList();
            var updatedDays = updatedSchedule.DaysOfWeek.OrderBy(day => day).ToList();

            return !existingDays.SequenceEqual(updatedDays);
        }

        private static DateTime? CalculateNextRunTime(BackupSchedule schedule, bool isInitialCalculation)
        {
            var now = DateTime.Now;
            var scheduledTime = now.Date.Add(schedule.Time);

            switch (schedule.Frequency)
            {
                case ScheduleFrequency.Daily:
                    if (isInitialCalculation && scheduledTime <= now)
                    {
                        return scheduledTime.AddDays(1);
                    }

                    return scheduledTime > now ? scheduledTime : scheduledTime.AddDays(1);

                case ScheduleFrequency.Weekly:
                    var nextWeeklyRun = scheduledTime > now ? scheduledTime : scheduledTime.AddDays(1);
                    while (!schedule.DaysOfWeek.Contains(nextWeeklyRun.DayOfWeek))
                    {
                        nextWeeklyRun = nextWeeklyRun.AddDays(1);
                    }

                    return nextWeeklyRun;

                case ScheduleFrequency.Monthly:
                    int day = Math.Max(1, Math.Min(schedule.DayOfMonth, DateTime.DaysInMonth(now.Year, now.Month)));
                    var nextMonthlyRun = new DateTime(now.Year, now.Month, day, schedule.Time.Hours, schedule.Time.Minutes, 0);
                    if (nextMonthlyRun <= now)
                    {
                        var nextMonth = now.AddMonths(1);
                        day = Math.Max(1, Math.Min(schedule.DayOfMonth, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)));
                        nextMonthlyRun = new DateTime(nextMonth.Year, nextMonth.Month, day, schedule.Time.Hours, schedule.Time.Minutes, 0);
                    }

                    return nextMonthlyRun;

                case ScheduleFrequency.Once:
                    schedule.Enabled = false;
                    return null;

                default:
                    return null;
            }
        }
    }
}

