using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BackupUI.Models;

namespace BackupUI.Services
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

        public void AddJob(BackupJob job)
        {
            if (job.Id == Guid.Empty)
                job.Id = Guid.NewGuid();

            jobs.Add(job);
            SaveJobs();
        }

        public void UpdateJob(BackupJob job)
        {
            var existingJob = jobs.FirstOrDefault(j => j.Id == job.Id);
            if (existingJob != null)
            {
                jobs.Remove(existingJob);
                jobs.Add(job);
                SaveJobs();
            }
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
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save jobs: {ex.Message}", ex);
            }
        }
    }
}
