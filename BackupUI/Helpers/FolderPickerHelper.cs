// FolderPickerHelper.cs - Folder and file selection helpers for backup/restore operations
using Microsoft.Win32;
using System;
using System.IO;

namespace BackupUI.Helpers
{
    public static class FolderPickerHelper
    {
        /// <summary>
        /// Opens a folder browser dialog to select a folder
        /// </summary>
        /// <param name="title">Dialog title</param>
        /// <param name="initialDirectory">Starting directory (optional)</param>
        /// <returns>Selected folder path or null if cancelled</returns>
        public static string? PickFolder(string title = "Select Folder", string? initialDirectory = null)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog 
            { 
                Description = title,
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };

            // Set initial directory if provided and valid
            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.SelectedPath = initialDirectory;
            }

            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        /// <summary>
        /// Opens a file picker dialog to select a file
        /// </summary>
        /// <param name="title">Dialog title</param>
        /// <param name="filter">File filter (e.g., "Backup Files|*.brs;*.ssb|All Files|*.*")</param>
        /// <param name="initialDirectory">Starting directory (optional)</param>
        /// <returns>Selected file path or null if cancelled</returns>
        public static string? PickFile(string title, string filter, string? initialDirectory = null)
        {
            var dialog = new OpenFileDialog 
            { 
                Title = title, 
                Filter = filter,
                CheckFileExists = true,
                CheckPathExists = true
            };

            // Set initial directory if provided and valid
            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        /// <summary>
        /// Opens a folder picker specifically for selecting backup destination
        /// </summary>
        /// <param name="suggestedName">Suggested folder name for the backup</param>
        /// <returns>Selected backup location or null if cancelled</returns>
        public static string? PickBackupLocation(string suggestedName)
        {
            // Try to determine a good initial directory
            string? initialDir = null;

            // Check common backup locations in order of preference
            string[] commonBackupPaths = {
                @"D:\Backups",
                @"E:\Backups",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Backups"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            foreach (var path in commonBackupPaths)
            {
                if (Directory.Exists(path))
                {
                    initialDir = path;
                    break;
                }
            }

            var selectedPath = PickFolder($"Select Backup Location for '{suggestedName}'", initialDir);

            // If user selected a path and suggestedName is provided, suggest creating a subfolder
            if (!string.IsNullOrWhiteSpace(selectedPath) && !string.IsNullOrWhiteSpace(suggestedName))
            {
                // Return the path with suggested name as subfolder
                return Path.Combine(selectedPath, suggestedName);
            }

            return selectedPath;
        }

        /// <summary>
        /// Opens a folder picker for selecting a backup to restore
        /// </summary>
        /// <returns>Selected backup path or null if cancelled</returns>
        public static string? PickBackupToRestore()
        {
            // Try to find common backup locations
            string? initialDir = null;

            string[] commonBackupPaths = {
                @"D:\Backups",
                @"E:\Backups",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Backups")
            };

            foreach (var path in commonBackupPaths)
            {
                if (Directory.Exists(path))
                {
                    initialDir = path;
                    break;
                }
            }

            return PickFolder("Select Backup to Restore", initialDir);
        }
    }
}
