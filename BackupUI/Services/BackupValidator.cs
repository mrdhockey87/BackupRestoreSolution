using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupUI.Services
{
    public class BackupValidator
    {
        public static async Task<(bool Success, string Message)> ValidateBackupAsync(string backupPath, string jobName)
        {
            try
            {
                BackupLogger.LogInfo(jobName, "Starting backup validation", backupPath);

                // Check if backup path exists
                if (!Directory.Exists(backupPath) && !File.Exists(backupPath))
                {
                    var msg = "Backup path does not exist";
                    BackupLogger.LogError(jobName, msg, backupPath);
                    return (false, msg);
                }

                // Validate backup using C++ engine
                var result = await Task.Run(() =>
                {
                    var buffer = new StringBuilder(1024);
                    int validationResult = BackupEngineInterop.VerifyBackup(backupPath, (percent, message) =>
                    {
                        BackupLogger.LogInfo(jobName, $"Validation: {percent}% - {message}");
                    });

                    if (validationResult != 0)
                    {
                        BackupEngineInterop.GetLastErrorMessage(buffer, buffer.Capacity);
                        return (false, buffer.ToString());
                    }

                    return (true, "Backup validation successful");
                });

                // Log result
                BackupLogger.LogValidationResult(jobName, backupPath, result.Item1, result.Item2);

                return result;
            }
            catch (Exception ex)
            {
                var msg = $"Validation exception: {ex.Message}";
                BackupLogger.LogError(jobName, "Validation failed with exception", msg);
                return (false, msg);
            }
        }

        public static async Task HandleFailedValidation(string backupPath, string jobName)
        {
            try
            {
                BackupLogger.LogWarning(jobName, "Starting auto-recovery for failed backup", backupPath);

                // Rename failed backup files by adding V1 before extension
                await RenameFailedBackup(backupPath);

                BackupLogger.LogInfo(jobName, "Failed backup renamed with V1 suffix");

                // Schedule new full backup (will be handled by JobManager)
                BackupLogger.LogInfo(jobName, "New full backup will be created at next scheduled run");
            }
            catch (Exception ex)
            {
                BackupLogger.LogError(jobName, "Auto-recovery failed", ex.Message);
            }
        }

        private static async Task RenameFailedBackup(string backupPath)
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(backupPath))
                {
                    // Rename directory
                    var dirInfo = new DirectoryInfo(backupPath);
                    var newName = $"{dirInfo.Name}_V1";
                    var newPath = Path.Combine(dirInfo.Parent?.FullName ?? "", newName);

                    if (!Directory.Exists(newPath))
                    {
                        dirInfo.MoveTo(newPath);
                    }
                }
                else if (File.Exists(backupPath))
                {
                    // Rename file
                    var fileInfo = new FileInfo(backupPath);
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    var extension = fileInfo.Extension;
                    var newName = $"{nameWithoutExt}_V1{extension}";
                    var newPath = Path.Combine(fileInfo.DirectoryName ?? "", newName);

                    if (!File.Exists(newPath))
                    {
                        fileInfo.MoveTo(newPath);
                    }
                }

                // Rename associated files (split files, metadata, etc.)
                var directory = Path.GetDirectoryName(backupPath) ?? "";
                var baseName = Path.GetFileNameWithoutExtension(backupPath);

                var relatedFiles = Directory.GetFiles(directory, $"{baseName}.*");
                foreach (var file in relatedFiles)
                {
                    if (file.Equals(backupPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fileInfo = new FileInfo(file);
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    var extension = fileInfo.Extension;
                    var newName = $"{nameWithoutExt}_V1{extension}";
                    var newPath = Path.Combine(fileInfo.DirectoryName ?? "", newName);

                    if (!File.Exists(newPath))
                    {
                        fileInfo.MoveTo(newPath);
                    }
                }
            });
        }
    }
}
