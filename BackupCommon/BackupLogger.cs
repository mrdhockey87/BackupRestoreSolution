using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BackupCommon
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
        public bool IsRead { get; set; } = false;
    }

    /// <summary>
    /// Enhanced BackupLogger with per-job log files.
    /// Logs are organized as:
    ///  - service.json: Service-only messages (startup, shutdown, etc.)
    ///  - {JobName}.json: Job-specific activity logs
    /// </summary>
    public class BackupLogger
    {
        private static readonly string LogDirectory = 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), 
                        "BackupRestoreService", "Logs");

        // Service log file for service-only messages
        private static readonly string ServiceLogFile = Path.Combine(LogDirectory, "service.json");

        // Legacy log file (pre-5.13.7.2) - kept for backward compatibility
        private static readonly string LegacyLogFile = Path.Combine(LogDirectory, "backup_activity.json");

        private static readonly object lockObject = new object();
        private static readonly int MaxLogEntriesPerFile = 2000; // Keep last 2000 entries per job (increased from 500 to prevent log loss)

        static BackupLogger()
        {
            // Ensure log directory exists
            Directory.CreateDirectory(LogDirectory);
        }

        #region Job-Specific Logging

        public static void LogInfo(string jobName, string message, string details = "")
        {
            LogToJobFile(jobName, new BackupLogEntry
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
            LogToJobFile(jobName, new BackupLogEntry
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
            LogToJobFile(jobName, new BackupLogEntry
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
            LogToJobFile(jobName, new BackupLogEntry
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
            LogToJobFile(jobName, new BackupLogEntry
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

        #endregion

        #region Service-Only Logging

        /// <summary>
        /// Log service-only messages (startup, shutdown, communication errors, etc.)
        /// </summary>
        public static void LogServiceInfo(string message, string details = "")
        {
            LogToServiceFile(new BackupLogEntry
            {
                Timestamp = DateTime.Now,
                JobName = "[SERVICE]",
                Level = BackupLogLevel.Info,
                Message = message,
                Details = details
            });
        }

        public static void LogServiceWarning(string message, string details = "")
        {
            LogToServiceFile(new BackupLogEntry
            {
                Timestamp = DateTime.Now,
                JobName = "[SERVICE]",
                Level = BackupLogLevel.Warning,
                Message = message,
                Details = details
            });
        }

        public static void LogServiceError(string message, string details = "")
        {
            LogToServiceFile(new BackupLogEntry
            {
                Timestamp = DateTime.Now,
                JobName = "[SERVICE]",
                Level = BackupLogLevel.Error,
                Message = message,
                Details = details
            });
        }

        #endregion

        #region File Operations

        private static void LogToJobFile(string jobName, BackupLogEntry entry)
        {
            if (string.IsNullOrWhiteSpace(jobName))
            {
                jobName = "Unknown";
            }

            // Sanitize job name for filename
            var safeJobName = SanitizeFileName(jobName);
            var jobLogFile = Path.Combine(LogDirectory, $"{safeJobName}.json");

            lock (lockObject)
            {
                try
                {
                    var logs = LoadLogsFromFile(jobLogFile);
                    logs.Add(entry);

                    // Keep only last MaxLogEntriesPerFile
                    if (logs.Count > MaxLogEntriesPerFile)
                    {
                        logs = logs.OrderByDescending(l => l.Timestamp)
                                   .Take(MaxLogEntriesPerFile)
                                   .OrderBy(l => l.Timestamp)
                                   .ToList();
                    }

                    SaveLogsToFile(jobLogFile, logs);
                }
                catch (Exception ex)
                {
                    WriteFallbackLog($"ERROR logging to job file: {DateTime.Now}: {entry.Level} - {entry.JobName}: {entry.Message} - Exception: {ex.Message}");
                }
            }
        }

        private static void LogToServiceFile(BackupLogEntry entry)
        {
            lock (lockObject)
            {
                try
                {
                    var logs = LoadLogsFromFile(ServiceLogFile);
                    logs.Add(entry);

                    // Keep only last MaxLogEntriesPerFile
                    if (logs.Count > MaxLogEntriesPerFile)
                    {
                        logs = logs.OrderByDescending(l => l.Timestamp)
                                   .Take(MaxLogEntriesPerFile)
                                   .OrderBy(l => l.Timestamp)
                                   .ToList();
                    }

                    SaveLogsToFile(ServiceLogFile, logs);
                }
                catch (Exception ex)
                {
                    WriteFallbackLog($"ERROR logging to service file: {DateTime.Now}: {entry.Level} - {entry.Message} - Exception: {ex.Message}");
                }
            }
        }

        private static List<BackupLogEntry> LoadLogsFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return new List<BackupLogEntry>();

                var json = File.ReadAllText(filePath);

                if (string.IsNullOrWhiteSpace(json))
                    return new List<BackupLogEntry>();

                var logs = JsonSerializer.Deserialize<List<BackupLogEntry>>(json);
                return logs ?? new List<BackupLogEntry>();
            }
            catch (JsonException)
            {
                // Corrupted JSON file - backup and start fresh
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    var backupFile = Path.Combine(LogDirectory, $"{fileName}_corrupted_{DateTime.Now:yyyyMMddHHmmss}.json");
                    File.Copy(filePath, backupFile, true);
                    System.Diagnostics.Debug.WriteLine($"Corrupted log file backed up to: {backupFile}");
                }
                catch { }

                return new List<BackupLogEntry>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading logs from {filePath}: {ex.Message}");
                return new List<BackupLogEntry>();
            }
        }

        private static void SaveLogsToFile(string filePath, List<BackupLogEntry> logs)
        {
            try
            {
                var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving logs to {filePath}: {ex.Message}");
                throw; // Let caller handle via fallback
            }
        }

        private static void WriteFallbackLog(string message)
        {
            try
            {
                var fallbackFile = Path.Combine(LogDirectory, "backup_errors.txt");
                File.AppendAllText(fallbackFile, message + "\n");
            }
            catch
            {
                // Last resort - just debug output
                System.Diagnostics.Debug.WriteLine($"CRITICAL: Cannot write to any log file: {message}");
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            // Remove invalid filename characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(sanitized) ? "UnknownJob" : sanitized;
        }

        #endregion

        #region Legacy Support / Backward Compatibility

        /// <summary>
        /// Load all logs from all job files plus service log
        /// </summary>
        public static List<BackupLogEntry> LoadLogs()
        {
            lock (lockObject)
            {
                var allLogs = new List<BackupLogEntry>();

                try
                {
                    // Load service logs
                    allLogs.AddRange(LoadLogsFromFile(ServiceLogFile));

                    // Load all job log files
                    var logFiles = Directory.GetFiles(LogDirectory, "*.json")
                        .Where(f => !f.Equals(ServiceLogFile, StringComparison.OrdinalIgnoreCase));

                    foreach (var logFile in logFiles)
                    {
                        allLogs.AddRange(LoadLogsFromFile(logFile));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading all logs: {ex.Message}");
                }

                return allLogs.OrderBy(l => l.Timestamp).ToList();
            }
        }

        #endregion

        #region Query Methods

        public static List<BackupLogEntry> GetRecentLogs(int count = 100)
        {
            return LoadLogs()
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToList();
        }

        public static List<BackupLogEntry> GetLogsByJob(string jobName)
        {
            var safeJobName = SanitizeFileName(jobName);
            var jobLogFile = Path.Combine(LogDirectory, $"{safeJobName}.json");

            lock (lockObject)
            {
                return LoadLogsFromFile(jobLogFile)
                    .OrderByDescending(l => l.Timestamp)
                    .ToList();
            }
        }

        public static List<BackupLogEntry> GetServiceLogs()
        {
            lock (lockObject)
            {
                return LoadLogsFromFile(ServiceLogFile)
                    .OrderByDescending(l => l.Timestamp)
                    .ToList();
            }
        }

        public static List<BackupLogEntry> GetFailedValidations()
        {
            return LoadLogs()
                .Where(l => !l.ValidationPassed && !string.IsNullOrEmpty(l.BackupPath))
                .OrderByDescending(l => l.Timestamp)
                .ToList();
        }

        public static List<string> GetAllJobNames()
        {
            lock (lockObject)
            {
                try
                {
                    var jobNames = new List<string>();
                    var logFiles = Directory.GetFiles(LogDirectory, "*.json")
                        .Where(f => !f.Equals(ServiceLogFile, StringComparison.OrdinalIgnoreCase));

                    foreach (var logFile in logFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(logFile);
                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            jobNames.Add(fileName);
                        }
                    }

                    return jobNames.OrderBy(n => n).ToList();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting job names: {ex.Message}");
                    return new List<string>();
                }
            }
        }

        public static void ClearOldLogs(int daysToKeep = 30)
        {
            lock (lockObject)
            {
                try
                {
                    var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                    var logFiles = Directory.GetFiles(LogDirectory, "*.json");

                    foreach (var logFile in logFiles)
                    {
                        try
                        {
                            var logs = LoadLogsFromFile(logFile);
                            var filteredLogs = logs.Where(l => l.Timestamp >= cutoffDate).ToList();

                            if (filteredLogs.Count != logs.Count)
                            {
                                SaveLogsToFile(logFile, filteredLogs);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error clearing old logs from {logFile}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error clearing old logs: {ex.Message}");
                }
            }
        }

        #endregion

        #region Unread Tracking

        public static int GetUnreadErrorCount()
        {
            return LoadLogs()
                .Count(l => !l.IsRead && (l.Level == BackupLogLevel.Error || l.Level == BackupLogLevel.Warning));
        }

        public static bool HasUnreadErrors()
        {
            return LoadLogs().Any(l => !l.IsRead && l.Level == BackupLogLevel.Error);
        }

        public static bool HasUnreadWarnings()
        {
            return LoadLogs().Any(l => !l.IsRead && l.Level == BackupLogLevel.Warning);
        }

        public static void MarkAllErrorsAsRead()
        {
            lock (lockObject)
            {
                try
                {
                    var logFiles = Directory.GetFiles(LogDirectory, "*.json");

                    foreach (var logFile in logFiles)
                    {
                        try
                        {
                            var logs = LoadLogsFromFile(logFile);
                            bool changed = false;

                            foreach (var log in logs)
                            {
                                if (!log.IsRead && (log.Level == BackupLogLevel.Error || log.Level == BackupLogLevel.Warning))
                                {
                                    log.IsRead = true;
                                    changed = true;
                                }
                            }

                            if (changed)
                            {
                                SaveLogsToFile(logFile, logs);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error marking errors as read in {logFile}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error marking all errors as read: {ex.Message}");
                }
            }
        }

        #endregion

        #region Delete Operations

        public static bool DeleteLogEntry(BackupLogEntry entryToDelete)
        {
            lock (lockObject)
            {
                try
                {
                    // Try to delete from current per-job files first
                    string logFile;
                    if (entryToDelete.JobName == "[SERVICE]")
                    {
                        logFile = ServiceLogFile;
                    }
                    else
                    {
                        var safeJobName = SanitizeFileName(entryToDelete.JobName);
                        logFile = Path.Combine(LogDirectory, $"{safeJobName}.json");
                    }

                    if (File.Exists(logFile))
                    {
                        var logs = LoadLogsFromFile(logFile);

                        var removed = logs.RemoveAll(l => 
                            l.Timestamp == entryToDelete.Timestamp &&
                            l.JobName == entryToDelete.JobName &&
                            l.Message == entryToDelete.Message);

                        if (removed > 0)
                        {
                            SaveLogsToFile(logFile, logs);
                            return true;
                        }
                    }

                    // If not found in per-job file, check legacy file
                    if (File.Exists(LegacyLogFile))
                    {
                        var logs = LoadLogsFromFile(LegacyLogFile);

                        var removed = logs.RemoveAll(l => 
                            l.Timestamp == entryToDelete.Timestamp &&
                            l.JobName == entryToDelete.JobName &&
                            l.Message == entryToDelete.Message);

                        if (removed > 0)
                        {
                            SaveLogsToFile(LegacyLogFile, logs);
                            return true;
                        }
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting log entry: {ex.Message}");
                    return false;
                }
            }
        }

        public static int DeleteLogEntries(List<BackupLogEntry> entriesToDelete)
        {
            lock (lockObject)
            {
                try
                {
                    int totalDeleted = 0;
                    var remainingEntries = new List<BackupLogEntry>(entriesToDelete);

                    // Group entries by job name for efficient deletion from per-job files
                    var entriesByJob = entriesToDelete.GroupBy(e => e.JobName);

                    foreach (var group in entriesByJob)
                    {
                        string logFile;
                        if (group.Key == "[SERVICE]")
                        {
                            logFile = ServiceLogFile;
                        }
                        else
                        {
                            var safeJobName = SanitizeFileName(group.Key);
                            logFile = Path.Combine(LogDirectory, $"{safeJobName}.json");
                        }

                        if (File.Exists(logFile))
                        {
                            var logs = LoadLogsFromFile(logFile);
                            int deletedCount = 0;

                            foreach (var entryToDelete in group)
                            {
                                var removed = logs.RemoveAll(l => 
                                    l.Timestamp == entryToDelete.Timestamp &&
                                    l.JobName == entryToDelete.JobName &&
                                    l.Message == entryToDelete.Message);

                                deletedCount += removed;
                                if (removed > 0)
                                {
                                    remainingEntries.Remove(entryToDelete);
                                }
                            }

                            if (deletedCount > 0)
                            {
                                SaveLogsToFile(logFile, logs);
                                totalDeleted += deletedCount;
                            }
                        }
                    }

                    // Try to delete remaining entries from legacy file
                    if (remainingEntries.Count > 0 && File.Exists(LegacyLogFile))
                    {
                        var legacyLogs = LoadLogsFromFile(LegacyLogFile);
                        int legacyDeletedCount = 0;

                        foreach (var entryToDelete in remainingEntries)
                        {
                            var removed = legacyLogs.RemoveAll(l => 
                                l.Timestamp == entryToDelete.Timestamp &&
                                l.JobName == entryToDelete.JobName &&
                                l.Message == entryToDelete.Message);

                            legacyDeletedCount += removed;
                        }

                        if (legacyDeletedCount > 0)
                        {
                            SaveLogsToFile(LegacyLogFile, legacyLogs);
                            totalDeleted += legacyDeletedCount;
                        }
                    }

                    return totalDeleted;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting log entries: {ex.Message}");
                    return 0;
                }
            }
        }

        #endregion
    }
}
