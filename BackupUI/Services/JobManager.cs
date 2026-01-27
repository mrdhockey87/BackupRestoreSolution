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
            return jobs.FirstOrDefault(j => j.Id == id);
        }

        public void AddJob(BackupJob job)
        {
            try
            {
                if (job.Id == Guid.Empty)
                    job.Id = Guid.NewGuid();

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
        }

        private void SaveJobs()
        {
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
                
                System.Diagnostics.Debug.WriteLine($"Saving {jobs.Count} jobs to {JobsFilePath}");
                File.WriteAllText(JobsFilePath, json);
                
                System.Diagnostics.Debug.WriteLine($"Jobs saved successfully. File size: {new FileInfo(JobsFilePath).Length} bytes");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR in SaveJobs: {ex.Message}\nStack: {ex.StackTrace}");
                throw new Exception($"Failed to save jobs file: {ex.Message}\nPath: {JobsFilePath}", ex);
            }
        }
    }
}

