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
        public bool IsRead { get; set; } = false;  // NEW: Track if user has seen this error
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
                catch (UnauthorizedAccessException accessEx)
                {
                    // Access denied - write to fallback file
                    WriteFallbackLog($"ACCESS DENIED: {DateTime.Now}: {entry.Level} - {entry.JobName}: {entry.Message}");
                    System.Diagnostics.Debug.WriteLine($"Log access denied: {accessEx.Message}");
                }
                catch (IOException ioEx)
                {
                    // I/O error - write to fallback file
                    WriteFallbackLog($"IO ERROR: {DateTime.Now}: {entry.Level} - {entry.JobName}: {entry.Message}");
                    System.Diagnostics.Debug.WriteLine($"Log I/O error: {ioEx.Message}");
                }
                catch (Exception ex)
                {
                    // Generic error - write to fallback file
                    WriteFallbackLog($"ERROR ({ex.GetType().Name}): {DateTime.Now}: {entry.Level} - {entry.JobName}: {entry.Message}");
                    System.Diagnostics.Debug.WriteLine($"Log error: {ex.Message}");
                }
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

        public static List<BackupLogEntry> LoadLogs()
        {
            lock (lockObject)
            {
                try
                {
                    if (!File.Exists(LogFile))
                        return new List<BackupLogEntry>();

                    var json = File.ReadAllText(LogFile);
                    
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
                        var backupFile = Path.Combine(LogDirectory, $"backup_activity_corrupted_{DateTime.Now:yyyyMMddHHmmss}.json");
                        File.Copy(LogFile, backupFile, true);
                        System.Diagnostics.Debug.WriteLine($"Corrupted log file backed up to: {backupFile}");
                    }
                    catch { }

                    return new List<BackupLogEntry>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading logs: {ex.Message}");
                    return new List<BackupLogEntry>();
                }
            }
        }

        private static void SaveLogs(List<BackupLogEntry> logs)
        {
            try
            {
                var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(LogFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving logs: {ex.Message}");
                throw; // Let caller handle via fallback
            }
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

        // NEW: Get count of unread errors and warnings
        public static int GetUnreadErrorCount()
        {
            return LoadLogs()
                .Count(l => !l.IsRead && (l.Level == BackupLogLevel.Error || l.Level == BackupLogLevel.Warning));
        }

        // NEW: Check if there are unread errors (not warnings)
        public static bool HasUnreadErrors()
        {
            return LoadLogs().Any(l => !l.IsRead && l.Level == BackupLogLevel.Error);
        }

        // NEW: Check if there are unread warnings (not errors)
        public static bool HasUnreadWarnings()
        {
            return LoadLogs().Any(l => !l.IsRead && l.Level == BackupLogLevel.Warning);
        }

        // NEW: Mark all errors as read
        public static void MarkAllErrorsAsRead()
        {
            lock (lockObject)
            {
                var logs = LoadLogs();
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
                    SaveLogs(logs);
                }
            }
        }

        // NEW: Delete a specific log entry
        public static bool DeleteLogEntry(BackupLogEntry entryToDelete)
        {
            lock (lockObject)
            {
                try
                {
                    var logs = LoadLogs();
                    
                    // Find and remove the matching entry
                    var removed = logs.RemoveAll(l => 
                        l.Timestamp == entryToDelete.Timestamp &&
                        l.JobName == entryToDelete.JobName &&
                        l.Message == entryToDelete.Message);

                    if (removed > 0)
                    {
                        SaveLogs(logs);
                        return true;
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

        // NEW: Delete multiple log entries
        public static int DeleteLogEntries(List<BackupLogEntry> entriesToDelete)
        {
            lock (lockObject)
            {
                try
                {
                    var logs = LoadLogs();
                    int deletedCount = 0;

                    foreach (var entryToDelete in entriesToDelete)
                    {
                        var removed = logs.RemoveAll(l => 
                            l.Timestamp == entryToDelete.Timestamp &&
                            l.JobName == entryToDelete.JobName &&
                            l.Message == entryToDelete.Message);
                        
                        deletedCount += removed;
                    }

                    if (deletedCount > 0)
                    {
                        SaveLogs(logs);
                    }

                    return deletedCount;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting log entries: {ex.Message}");
                    return 0;
                }
            }
        }
    }
}
