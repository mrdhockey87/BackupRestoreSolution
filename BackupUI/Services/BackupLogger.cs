using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BackupUI.Services
{
    public enum BackupLogLevel
    {
        Info,
        Warning,
        Error,
        Success
    }

    public class BackupLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string JobName { get; set; } = "";
        public BackupLogLevel Level { get; set; }
        public string Message { get; set; } = "";
        public string Details { get; set; } = "";
        public bool ValidationPassed { get; set; } = true;
        public string BackupPath { get; set; } = "";
    }

    public class BackupLogger
    {
        private static readonly string LogDirectory = 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), 
                        "BackupRestoreService", "Logs");
        
        private static readonly string LogFile = Path.Combine(LogDirectory, "backup_activity.json");
        private static readonly object lockObject = new object();
        private static readonly int MaxLogEntries = 1000; // Keep last 1000 entries

        static BackupLogger()
        {
            // Ensure log directory exists
            Directory.CreateDirectory(LogDirectory);
        }

        public static void LogInfo(string jobName, string message, string details = "")
        {
            Log(new BackupLogEntry
            {
                Timestamp = DateTime.Now,
                JobName = jobName,
                Level = BackupLogLevel.Info,
                Message = message,
                Details = details
            });
        }

        public static void LogSuccess(string jobName, string message, string backupPath = "", string details = "")
        {
            Log(new BackupLogEntry
            {
                Timestamp = DateTime.Now,
                JobName = jobName,
                Level = BackupLogLevel.Success,
                Message = message,
                BackupPath = backupPath,
                Details = details
            });
        }

        public static void LogWarning(string jobName, string message, string details = "")
        {
            Log(new BackupLogEntry
            {
                Timestamp = DateTime.Now,
                JobName = jobName,
                Level = BackupLogLevel.Warning,
                Message = message,
                Details = details
            });
        }

        public static void LogError(string jobName, string message, string details = "")
        {
            Log(new BackupLogEntry
            {
                Timestamp = DateTime.Now,
                JobName = jobName,
                Level = BackupLogLevel.Error,
                Message = message,
                Details = details
            });
        }

        public static void LogValidationResult(string jobName, string backupPath, bool passed, string details = "")
        {
            Log(new BackupLogEntry
            {
                Timestamp = DateTime.Now,
                JobName = jobName,
                Level = passed ? BackupLogLevel.Success : BackupLogLevel.Error,
                Message = passed ? "Backup validation passed" : "Backup validation FAILED",
                BackupPath = backupPath,
                ValidationPassed = passed,
                Details = details
            });
        }

        private static void Log(BackupLogEntry entry)
        {
            lock (lockObject)
            {
                try
                {
                    var logs = LoadLogs();
                    logs.Add(entry);

                    // Keep only last MaxLogEntries
                    if (logs.Count > MaxLogEntries)
                    {
                        logs = logs.OrderByDescending(l => l.Timestamp)
                                   .Take(MaxLogEntries)
                                   .OrderBy(l => l.Timestamp)
                                   .ToList();
                    }

                    SaveLogs(logs);
                }
                catch (Exception ex)
                {
                    // Fallback to text file if JSON fails
                    File.AppendAllText(Path.Combine(LogDirectory, "backup_errors.txt"),
                        $"{DateTime.Now}: Failed to write log - {ex.Message}\n");
                }
            }
        }

        public static List<BackupLogEntry> LoadLogs()
        {
            lock (lockObject)
            {
                try
                {
                    if (!File.Exists(LogFile))
                        return new List<BackupLogEntry>();

                    var json = File.ReadAllText(LogFile);
                    return JsonSerializer.Deserialize<List<BackupLogEntry>>(json) ?? new List<BackupLogEntry>();
                }
                catch
                {
                    return new List<BackupLogEntry>();
                }
            }
        }

        private static void SaveLogs(List<BackupLogEntry> logs)
        {
            var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(LogFile, json);
        }

        public static List<BackupLogEntry> GetRecentLogs(int count = 100)
        {
            return LoadLogs()
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToList();
        }

        public static List<BackupLogEntry> GetLogsByJob(string jobName)
        {
            return LoadLogs()
                .Where(l => l.JobName.Equals(jobName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(l => l.Timestamp)
                .ToList();
        }

        public static List<BackupLogEntry> GetFailedValidations()
        {
            return LoadLogs()
                .Where(l => !l.ValidationPassed && !string.IsNullOrEmpty(l.BackupPath))
                .OrderByDescending(l => l.Timestamp)
                .ToList();
        }

        public static void ClearOldLogs(int daysToKeep = 30)
        {
            lock (lockObject)
            {
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var logs = LoadLogs()
                    .Where(l => l.Timestamp >= cutoffDate)
                    .ToList();
                SaveLogs(logs);
            }
        }
    }
}
