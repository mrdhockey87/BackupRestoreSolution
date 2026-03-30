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
            // Retry logic to handle race condition with C++ engine atomic writes
            // C++ writes to temp file then renames - we might catch it mid-operation
            const int maxRetries = 3;
            const int retryDelayMs = 50;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    if (!File.Exists(filePath))
                        return new List<BackupLogEntry>();

                    // Read with auto-detect encoding to handle both UTF-8 and UTF-16 files
                    // (C++ engine may have written UTF-16 previously, now writes UTF-8)
                    string json;
                    var bytes = File.ReadAllBytes(filePath);
                    if (bytes.Length == 0)
                        return new List<BackupLogEntry>();

                    // Check for BOM to detect encoding
                    if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                    {
                        // UTF-16 LE BOM
                        json = System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
                    }
                    else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                    {
                        // UTF-16 BE BOM
                        json = System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
                    }
                    else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                    {
                        // UTF-8 BOM
                        json = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                    }
                    else
                    {
                        // No BOM - try UTF-8 (default for new files)
                        json = System.Text.Encoding.UTF8.GetString(bytes);
                    }

                    if (string.IsNullOrWhiteSpace(json))
                        return new List<BackupLogEntry>();

                    var logs = JsonSerializer.Deserialize<List<BackupLogEntry>>(json);
                    return logs ?? new List<BackupLogEntry>();
                }
                catch (IOException) when (attempt < maxRetries - 1)
                {
                    // File might be locked by C++ engine - retry after short delay
                    System.Threading.Thread.Sleep(retryDelayMs);
                    continue;
                }
                catch (JsonException)
                {
                    // JSON parsing failed - try to recover individual entries
                    var fileName = Path.GetFileNameWithoutExtension(filePath);

                    // Skip recovery for already-corrupted backup files
                    if (fileName.Contains("_corrupted_"))
                        return new List<BackupLogEntry>();

                    try
                    {
                        var recoveredLogs = TryRecoverLogsFromCorruptedFile(filePath);

                        // Backup original corrupted file
                        var backupFile = Path.Combine(LogDirectory, $"{fileName}_corrupted_{DateTime.Now:yyyyMMddHHmmss}.json");
                        File.Copy(filePath, backupFile, true);
                        System.Diagnostics.Debug.WriteLine($"Corrupted log file backed up to: {backupFile}");

                        // If we recovered entries, save them back (fixes the file)
                        if (recoveredLogs.Count > 0)
                        {
                            SaveLogsToFile(filePath, recoveredLogs);
                            System.Diagnostics.Debug.WriteLine($"Recovered {recoveredLogs.Count} entries from corrupted file: {filePath}");
                            return recoveredLogs;
                        }
                        else
                        {
                            // No entries recovered - reset to empty
                            File.WriteAllText(filePath, "[]");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error during log recovery: {ex.Message}");
                    }

                    return new List<BackupLogEntry>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading logs from {filePath}: {ex.Message}");
                    return new List<BackupLogEntry>();
                }
            }

            return new List<BackupLogEntry>();
        }

        /// <summary>
        /// Attempts to recover log entries from a corrupted JSON file by parsing each entry individually.
        /// This handles cases where some entries are truncated or malformed.
        /// </summary>
        private static List<BackupLogEntry> TryRecoverLogsFromCorruptedFile(string filePath)
        {
            var recoveredLogs = new List<BackupLogEntry>();

            try
            {
                var content = File.ReadAllText(filePath);

                // Find all JSON object patterns that look like log entries
                // Pattern: {"Timestamp":... through ...,"IsRead":...}
                int searchStart = 0;
                while (searchStart < content.Length)
                {
                    int objStart = content.IndexOf("{\"Timestamp\":", searchStart);
                    if (objStart < 0) break;

                    // Find the end of this object - look for }," or }] or just }
                    int objEnd = -1;
                    int braceCount = 0;
                    bool inString = false;
                    bool escaped = false;

                    for (int i = objStart; i < content.Length; i++)
                    {
                        char c = content[i];

                        if (escaped)
                        {
                            escaped = false;
                            continue;
                        }

                        if (c == '\\' && inString)
                        {
                            escaped = true;
                            continue;
                        }

                        if (c == '"')
                        {
                            inString = !inString;
                            continue;
                        }

                        if (!inString)
                        {
                            if (c == '{') braceCount++;
                            else if (c == '}')
                            {
                                braceCount--;
                                if (braceCount == 0)
                                {
                                    objEnd = i;
                                    break;
                                }
                            }
                        }
                    }

                    if (objEnd > objStart)
                    {
                        var jsonObj = content.Substring(objStart, objEnd - objStart + 1);

                        try
                        {
                            var entry = JsonSerializer.Deserialize<BackupLogEntry>(jsonObj);
                            if (entry != null && entry.Timestamp != default)
                            {
                                recoveredLogs.Add(entry);
                            }
                        }
                        catch
                        {
                            // This individual entry couldn't be parsed - try to fix common issues
                            var fixedJson = TryFixMalformedEntry(jsonObj);
                            if (fixedJson != null)
                            {
                                try
                                {
                                    var entry = JsonSerializer.Deserialize<BackupLogEntry>(fixedJson);
                                    if (entry != null && entry.Timestamp != default)
                                    {
                                        recoveredLogs.Add(entry);
                                    }
                                }
                                catch { }
                            }
                        }

                        searchStart = objEnd + 1;
                    }
                    else
                    {
                        // Couldn't find closing brace - try to construct a minimal valid entry from what we have
                        var partialJson = content.Substring(objStart, Math.Min(500, content.Length - objStart));
                        var fixedJson = TryFixMalformedEntry(partialJson);
                        if (fixedJson != null)
                        {
                            try
                            {
                                var entry = JsonSerializer.Deserialize<BackupLogEntry>(fixedJson);
                                if (entry != null && entry.Timestamp != default)
                                {
                                    recoveredLogs.Add(entry);
                                }
                            }
                            catch { }
                        }
                        searchStart = objStart + 1;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error recovering logs: {ex.Message}");
            }

            return recoveredLogs;
        }

        /// <summary>
        /// Tries to fix common JSON malformations in log entries.
        /// </summary>
        private static string? TryFixMalformedEntry(string json)
        {
            try
            {
                // If JSON doesn't end with }, it's truncated
                json = json.TrimEnd();

                if (!json.EndsWith("}"))
                {
                    // Find the last complete property and close the object
                    // Look for common property endings like :false}, :true}, :""}, etc.

                    // Check if we're in the middle of a string value
                    int lastQuote = json.LastIndexOf('"');
                    int lastColon = json.LastIndexOf(':');

                    if (lastColon > lastQuote)
                    {
                        // We're after a colon but haven't finished the value
                        // Truncate to before this property and close
                        int lastComma = json.LastIndexOf(',');
                        if (lastComma > 0)
                        {
                            json = json.Substring(0, lastComma) + "}";
                        }
                        else
                        {
                            return null; // Can't fix
                        }
                    }
                    else
                    {
                        // We're in the middle of a string - close the string and object
                        json += "\"}";

                        // Now check if we need to add missing default properties
                        if (!json.Contains("\"Details\""))
                            json = json.TrimEnd('}') + ",\"Details\":\"\",\"ValidationPassed\":true,\"BackupPath\":\"\",\"IsRead\":false}";
                        else if (!json.Contains("\"ValidationPassed\""))
                            json = json.TrimEnd('}') + ",\"ValidationPassed\":true,\"BackupPath\":\"\",\"IsRead\":false}";
                        else if (!json.Contains("\"BackupPath\""))
                            json = json.TrimEnd('}') + ",\"BackupPath\":\"\",\"IsRead\":false}";
                        else if (!json.Contains("\"IsRead\""))
                            json = json.TrimEnd('}') + ",\"IsRead\":false}";
                    }
                }

                // Validate it can be parsed
                JsonSerializer.Deserialize<BackupLogEntry>(json);
                return json;
            }
            catch
            {
                return null;
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
        /// Load all logs from all job files plus service log.
        /// Also includes any orphaned entries from corrupted backup files.
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

                    // Load all job log files (exclude corrupted backups and legacy file)
                    var logFiles = Directory.GetFiles(LogDirectory, "*.json")
                        .Where(f => !f.Equals(ServiceLogFile, StringComparison.OrdinalIgnoreCase)
                                 && !Path.GetFileName(f).Contains("_corrupted_"));

                    foreach (var logFile in logFiles)
                    {
                        allLogs.AddRange(LoadLogsFromFile(logFile));
                    }

                    // Also load entries from corrupted backup files so no messages are lost
                    // These are backup copies made when log corruption is detected
                    var corruptedFiles = Directory.GetFiles(LogDirectory, "*_corrupted_*.json");
                    foreach (var corruptedFile in corruptedFiles)
                    {
                        try
                        {
                            var recoveredEntries = TryRecoverLogsFromCorruptedFile(corruptedFile);
                            if (recoveredEntries.Count > 0)
                            {
                                // Add any entries not already in allLogs (by timestamp + message to dedupe)
                                foreach (var entry in recoveredEntries)
                                {
                                    bool isDupe = allLogs.Any(l => 
                                        l.Timestamp == entry.Timestamp && 
                                        l.Message == entry.Message &&
                                        l.JobName == entry.JobName);

                                    if (!isDupe)
                                    {
                                        allLogs.Add(entry);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading corrupted log file {corruptedFile}: {ex.Message}");
                        }
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
                var logs = LoadLogsFromFile(jobLogFile);

                // Also include entries from any corrupted backup files for this job
                // These are backup copies made when log corruption is detected
                try
                {
                    var corruptedFiles = Directory.GetFiles(LogDirectory, $"{safeJobName}_corrupted_*.json");
                    foreach (var corruptedFile in corruptedFiles)
                    {
                        try
                        {
                            var recoveredEntries = TryRecoverLogsFromCorruptedFile(corruptedFile);
                            foreach (var entry in recoveredEntries)
                            {
                                // Add if not a duplicate (by timestamp + message)
                                bool isDupe = logs.Any(l => 
                                    l.Timestamp == entry.Timestamp && 
                                    l.Message == entry.Message);

                                if (!isDupe)
                                {
                                    logs.Add(entry);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading corrupted job log {corruptedFile}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error searching for corrupted job logs: {ex.Message}");
                }

                return logs.OrderByDescending(l => l.Timestamp).ToList();
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
                        .Where(f => !f.Equals(ServiceLogFile, StringComparison.OrdinalIgnoreCase)
                                 && !Path.GetFileName(f).Contains("_corrupted_"));

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
                    var logFiles = Directory.GetFiles(LogDirectory, "*.json")
                        .Where(f => !Path.GetFileName(f).Contains("_corrupted_"));

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
                    var logFiles = Directory.GetFiles(LogDirectory, "*.json")
                        .Where(f => !Path.GetFileName(f).Contains("_corrupted_"));

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

        #region Corrupted Log Consolidation

        /// <summary>
        /// Consolidates entries from corrupted backup log files back into the main job log files.
        /// This merges any orphaned log entries and then deletes the corrupted backup files.
        /// Call this method to recover messages that ended up in corrupted log files.
        /// </summary>
        /// <returns>Number of entries recovered and merged</returns>
        public static int ConsolidateCorruptedLogs()
        {
            lock (lockObject)
            {
                int totalRecovered = 0;

                try
                {
                    var corruptedFiles = Directory.GetFiles(LogDirectory, "*_corrupted_*.json");

                    foreach (var corruptedFile in corruptedFiles)
                    {
                        try
                        {
                            // Extract original job name from corrupted filename
                            // Format: {JobName}_corrupted_{timestamp}.json
                            var fileName = Path.GetFileNameWithoutExtension(corruptedFile);
                            var corruptedIndex = fileName.IndexOf("_corrupted_");
                            if (corruptedIndex <= 0) continue;

                            var jobName = fileName.Substring(0, corruptedIndex);
                            var mainLogFile = Path.Combine(LogDirectory, $"{jobName}.json");

                            // Recover entries from corrupted file
                            var recoveredEntries = TryRecoverLogsFromCorruptedFile(corruptedFile);
                            if (recoveredEntries.Count == 0)
                            {
                                // No entries to recover - delete the corrupted file
                                File.Delete(corruptedFile);
                                continue;
                            }

                            // Load existing main log file
                            var mainLogs = LoadLogsFromFile(mainLogFile);

                            // Merge recovered entries (skip duplicates by timestamp + message)
                            int mergedCount = 0;
                            foreach (var entry in recoveredEntries)
                            {
                                bool isDupe = mainLogs.Any(l => 
                                    l.Timestamp == entry.Timestamp && 
                                    l.Message == entry.Message);

                                if (!isDupe)
                                {
                                    mainLogs.Add(entry);
                                    mergedCount++;
                                }
                            }

                            if (mergedCount > 0)
                            {
                                // Sort by timestamp and save
                                mainLogs = mainLogs.OrderBy(l => l.Timestamp).ToList();

                                // Keep only last MaxLogEntriesPerFile
                                if (mainLogs.Count > MaxLogEntriesPerFile)
                                {
                                    mainLogs = mainLogs
                                        .OrderByDescending(l => l.Timestamp)
                                        .Take(MaxLogEntriesPerFile)
                                        .OrderBy(l => l.Timestamp)
                                        .ToList();
                                }

                                SaveLogsToFile(mainLogFile, mainLogs);
                                totalRecovered += mergedCount;
                                System.Diagnostics.Debug.WriteLine($"Merged {mergedCount} entries from {Path.GetFileName(corruptedFile)} into {jobName}.json");
                            }

                            // Delete the corrupted backup file after successful merge
                            File.Delete(corruptedFile);
                            System.Diagnostics.Debug.WriteLine($"Deleted corrupted backup file: {Path.GetFileName(corruptedFile)}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error consolidating {corruptedFile}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error consolidating corrupted logs: {ex.Message}");
                }

                return totalRecovered;
            }
        }

        /// <summary>
        /// Gets list of corrupted log backup files that exist in the log directory.
        /// </summary>
        public static List<string> GetCorruptedLogFiles()
        {
            try
            {
                return Directory.GetFiles(LogDirectory, "*_corrupted_*.json")
                    .Select(f => Path.GetFileName(f))
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting corrupted log files: {ex.Message}");
                return new List<string>();
            }
        }

        #endregion
    }
}
