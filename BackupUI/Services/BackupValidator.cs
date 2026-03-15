using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BackupCommon;

namespace BackupUI.Services
{
    public class BackupValidator
    {
        public static async Task<(bool Success, string Message)> ValidateBackupAsync(string backupPath, string jobName, bool backupSucceeded)
        {
            // CRITICAL: Don't validate if backup failed
            if (!backupSucceeded)
            {
                var skipMsg = "Validation skipped - backup failed";
                BackupLogger.LogWarning(jobName, skipMsg, backupPath);
                return (false, skipMsg);
            }

            try
            {
                BackupLogger.LogInfo(jobName, "Starting backup validation", backupPath);

                // Check if backup path exists
                if (!Directory.Exists(backupPath) && !File.Exists(backupPath))
                {
                    var msg = "Validation failed: Backup path does not exist";
                    BackupLogger.LogError(jobName, msg, backupPath);
                    BackupLogger.LogValidationResult(jobName, backupPath, false, msg);
                    return (false, msg);
                }

                // Validate backup using C++ engine
                var result = await Task.Run(() =>
                {
                    try
                    {
                        var buffer = new StringBuilder(1024);
                        int validationResult = BackupEngineInterop.VerifyBackup(backupPath, (percent, message) =>
                        {
                            try
                            {
                                BackupLogger.LogInfo(jobName, $"Validation: {percent}% - {message}");
                            }
                            catch (Exception logEx)
                            {
                                // Even logging errors shouldn't crash validation
                                System.Diagnostics.Debug.WriteLine($"Validation progress logging error: {logEx.Message}");
                            }
                        });

                        if (validationResult != 0)
                        {
                            BackupEngineInterop.GetLastErrorMessage(buffer, buffer.Capacity);
                            var errorMsg = buffer.ToString();
                            return (false, $"Validation failed: {errorMsg}");
                        }

                        return (true, "Backup validation successful");
                    }
                    catch (Exception innerEx)
                    {
                        return (false, $"Validation exception: {innerEx.Message}");
                    }
                });

                // Log result
                BackupLogger.LogValidationResult(jobName, backupPath, result.Item1, result.Item2);

                // Send notification if validation failed
                if (!result.Item1)
                {
                    try
                    {
                        NotificationService.ShowValidationFailureNotification(jobName, backupPath);
                    }
                    catch (Exception notifyEx)
                    {
                        // Notification failure shouldn't stop the process
                        BackupLogger.LogWarning(jobName, "Failed to send validation failure notification", notifyEx.Message);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                var msg = $"Validation exception: {ex.GetType().Name} - {ex.Message}";
                BackupLogger.LogError(jobName, "Validation failed with exception", msg);
                BackupLogger.LogValidationResult(jobName, backupPath, false, msg);
                return (false, msg);
            }
        }

        public static async Task HandleFailedValidation(string backupPath, string jobName)
        {
            try
            {
                BackupLogger.LogWarning(jobName, "Starting auto-recovery for failed backup", backupPath);

                // Rename failed backup files by adding _V suffix
                await RenameFailedBackup(backupPath);

                BackupLogger.LogInfo(jobName, "Failed backup renamed with version suffix");

                // Schedule new full backup (will be handled by JobManager)
                BackupLogger.LogInfo(jobName, "New full backup will be created at next scheduled run");
            }
            catch (Exception ex)
            {
                // Even auto-recovery failure shouldn't crash
                BackupLogger.LogError(jobName, "Auto-recovery failed", $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static async Task RenameFailedBackup(string backupPath)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(backupPath))
                    {
                        // Rename directory with incremented version
                        var dirInfo = new DirectoryInfo(backupPath);
                        var baseName = dirInfo.Name;
                        var parentPath = dirInfo.Parent?.FullName ?? "";

                        // Find next available version number
                        int version = GetNextVersionNumber(parentPath, baseName);
                        var newName = $"{baseName}_V{version}";
                        var newPath = Path.Combine(parentPath, newName);

                        if (!Directory.Exists(newPath))
                        {
                            dirInfo.MoveTo(newPath);
                            BackupLogger.LogInfo("Auto-Recovery", $"Renamed failed backup: {baseName} ? {newName}");
                        }
                    }
                    else if (File.Exists(backupPath))
                    {
                        // Rename file with incremented version
                        var fileInfo = new FileInfo(backupPath);
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileInfo.Name);
                        var extension = fileInfo.Extension;
                        var directory = fileInfo.DirectoryName ?? "";

                        // Find next available version number
                        int version = GetNextVersionNumber(directory, nameWithoutExt, extension);
                        var newName = $"{nameWithoutExt}_V{version}{extension}";
                        var newPath = Path.Combine(directory, newName);

                        if (!File.Exists(newPath))
                        {
                            fileInfo.MoveTo(newPath);
                            BackupLogger.LogInfo("Auto-Recovery", $"Renamed failed backup: {fileInfo.Name} ? {newName}");
                        }

                        // Rename associated files (split files, metadata, etc.)
                        RenameRelatedFiles(directory, nameWithoutExt, version);
                    }
                }
                catch (UnauthorizedAccessException accessEx)
                {
                    BackupLogger.LogError("Auto-Recovery", "Access denied renaming backup", 
                        $"Path: {backupPath}\nError: {accessEx.Message}");
                }
                catch (IOException ioEx)
                {
                    BackupLogger.LogError("Auto-Recovery", "I/O error renaming backup", 
                        $"Path: {backupPath}\nError: {ioEx.Message}");
                }
                catch (Exception ex)
                {
                    BackupLogger.LogError("Auto-Recovery", "Unexpected error renaming backup", 
                        $"Path: {backupPath}\nError: {ex.GetType().Name} - {ex.Message}");
                }
            });
        }

        private static int GetNextVersionNumber(string directory, string baseName, string extension = "")
        {
            int maxVersion = 0;

            try
            {
                if (!Directory.Exists(directory))
                    return 1;

                // Pattern to match: basename_V1, basename_V2, etc.
                string searchPattern = string.IsNullOrEmpty(extension) 
                    ? $"{baseName}_V*" 
                    : $"{baseName}_V*{extension}";

                var existingFiles = Directory.GetFileSystemEntries(directory, searchPattern);

                foreach (var file in existingFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        
                        // Extract version number from filename
                        // Pattern: basename_V123
                        var vIndex = fileName.LastIndexOf("_V", StringComparison.OrdinalIgnoreCase);
                        if (vIndex >= 0)
                        {
                            var versionStr = fileName.Substring(vIndex + 2);
                            if (int.TryParse(versionStr, out int version))
                            {
                                maxVersion = Math.Max(maxVersion, version);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Skip problematic files
                        System.Diagnostics.Debug.WriteLine($"Error parsing version number: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                BackupLogger.LogWarning("Auto-Recovery", "Error finding version number", ex.Message);
            }

            return maxVersion + 1;
        }

        private static void RenameRelatedFiles(string directory, string baseName, int version)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return;

                // Find all files that start with the base name
                var relatedFiles = Directory.GetFiles(directory, $"{baseName}.*");

                foreach (var file in relatedFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileInfo.Name);
                        var extension = fileInfo.Extension;

                        // Skip if this is the main file we already renamed
                        if (nameWithoutExt.EndsWith($"_V{version}", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Create new name with version
                        var newName = $"{nameWithoutExt}_V{version}{extension}";
                        var newPath = Path.Combine(directory, newName);

                        if (!File.Exists(newPath))
                        {
                            fileInfo.MoveTo(newPath);
                            BackupLogger.LogInfo("Auto-Recovery", $"Renamed related file: {fileInfo.Name} ? {newName}");
                        }
                    }
                    catch (Exception fileEx)
                    {
                        BackupLogger.LogWarning("Auto-Recovery", $"Failed to rename related file: {file}", fileEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                BackupLogger.LogWarning("Auto-Recovery", "Error renaming related files", ex.Message);
            }
        }
    }
}
