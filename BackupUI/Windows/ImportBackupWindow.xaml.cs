using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SecureServerBackupCommon;
using SecureServerBackup.Models;
using SecureServerBackup.Services;

namespace SecureServerBackup.Windows
{
    public partial class ImportBackupWindow : Window
    {
        private bool isValidBackup = false;
        private bool isBrsFormat = false;
        private bool isCompressed = false;
        private bool isEncrypted = false;
        private string backupName = "";
        private string backupType = "";
        private DateTime backupDate = DateTime.Now;
        private long backupSize = 0;

        public ImportBackupWindow()
        {
            InitializeComponent();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Backup File",
                Filter = "Backup Files (*.brs;*.ssb)|*.brs;*.ssb|BRS Backups (*.brs)|*.brs|SSB Files (*.ssb)|*.ssb|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                txtFilePath.Text = dialog.FileName;
                ValidateBackupFile(dialog.FileName);
            }
        }

        private void ValidateBackupFile(string filePath)
        {
            try
            {
                pnlValidation.Visibility = Visibility.Visible;
                grpBackupInfo.Visibility = Visibility.Collapsed;
                btnImport.IsEnabled = false;
                isValidBackup = false;

                isEncrypted = BackupEncryptionService.IsEncryptedBackupFile(filePath);
                string validationPath = filePath;
                string? temporaryPath = null;

                if (isEncrypted)
                {
                    using var preparedBackup = EncryptedBackupFileService.PrepareForRead(this, filePath, Path.GetFileNameWithoutExtension(filePath));
                    validationPath = preparedBackup.WorkingPath;
                    temporaryPath = preparedBackup.WorkingPath;
                }

                // P/Invoke to native backup validation
                bool isBrs = false;
                bool compressed = false;
                var errorMsg = new StringBuilder(512);

                // Call C++ BrsFileManager::ValidateBackupFile
                bool valid = NativeBrsValidator.ValidateBackupFile(
                    validationPath,
                    out isBrs,
                    out compressed,
                    out backupName,
                    out backupType,
                    out backupDate,
                    out backupSize,
                    errorMsg,
                    512
                );

                if (valid)
                {
                    // Valid backup
                    pnlValidation.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233)); // Light green
                    txtValidationStatus.Text = "? Valid Backup File";
                    txtValidationStatus.Foreground = Brushes.Green;
                    txtValidationDetails.Text = isBrs 
                        ? "This is a Backup Restore System (.brs) backup file."
                        : "This is a standard backup archive file.";

                    // Fill backup info
                    txtFormat.Text = isBrs ? ".brs (Proprietary)" : ".ssb (Standard)";
                    txtBackupName.Text = backupName;
                    txtBackupType.Text = backupType;
                    txtTimestamp.Text = backupDate.ToString("yyyy-MM-dd HH:mm:ss");
                    txtSize.Text = FormatBytes(backupSize);
                    txtCompressed.Text = compressed ? "Yes" : "No";
                    txtEncrypted.Text = isEncrypted ? "Yes" : "No";

                    grpBackupInfo.Visibility = Visibility.Visible;
                    btnImport.IsEnabled = true;
                    isValidBackup = true;
                    isBrsFormat = isBrs;
                    isCompressed = compressed;

                    BackupLogger.LogInfo("ImportBackup", 
                        $"Valid backup file detected: {Path.GetFileName(filePath)}", 
                        filePath);
                }
                else
                {
                    // Invalid backup
                    pnlValidation.Background = new SolidColorBrush(Color.FromRgb(255, 235, 238)); // Light red
                    txtValidationStatus.Text = "? Invalid Backup File";
                    txtValidationStatus.Foreground = Brushes.Red;
                    txtValidationDetails.Text = $"Error: {errorMsg}\n\nOnly .brs and .ssb backup files are supported.";

                    BackupLogger.LogWarning("ImportBackup", 
                        $"Invalid backup file: {Path.GetFileName(filePath)}", 
                        errorMsg.ToString());
                }
            }
            catch (Exception ex)
            {
                pnlValidation.Background = new SolidColorBrush(Color.FromRgb(255, 235, 238));
                txtValidationStatus.Text = "? Validation Error";
                txtValidationStatus.Foreground = Brushes.Red;
                txtValidationDetails.Text = $"Exception: {ex.Message}";

                BackupLogger.LogError("ImportBackup", 
                    "Exception validating backup file", 
                    ex.Message);
            }
        }

        private void RenameJob_Changed(object sender, RoutedEventArgs e)
        {
            pnlRename.Visibility = chkRenameJob.IsChecked == true 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (!isValidBackup)
            {
                MessageBox.Show("Please select a valid backup file first.", 
                              "Invalid Backup", 
                              MessageBoxButton.OK, 
                              MessageBoxImage.Warning);
                return;
            }

            try
            {
                string jobName = chkRenameJob.IsChecked == true && !string.IsNullOrWhiteSpace(txtJobName.Text)
                    ? txtJobName.Text.Trim()
                    : backupName;

                // Create backup job entry
                var job = new BackupJob
                {
                    Id = Guid.NewGuid(),
                    Name = jobName,
                    Type = ConvertBackupType(backupType),
                    Target = BackupTarget.Disk, // Imported backups are disk-based
                    DestinationPath = txtFilePath.Text,
                    SourcePaths = new System.Collections.Generic.List<string>(),
                    IsImported = true, // Flag to indicate imported backup
                    Schedule = null, // No schedule for imported backups
                    EncryptBackup = isEncrypted
                };

                // Save job
                var jobManager = new JobManager();
                jobManager.AddJob(job);

                BackupLogger.LogSuccess("ImportBackup", 
                    $"Backup imported successfully: {jobName}", 
                    txtFilePath.Text);

                MessageBox.Show(
                    $"Backup '{jobName}' imported successfully!\n\n" +
                    $"Format: {(isBrsFormat ? ".brs" : ".ssb")}\n" +
                    $"Size: {FormatBytes(backupSize)}\n" +
                    $"Compressed: {(isCompressed ? "Yes" : "No")}\n\n" +
                    "The backup is now available in the main window.",
                    "Import Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                BackupLogger.LogError("ImportBackup", 
                    "Failed to import backup", 
                    ex.Message);

                CustomDialogService.ShowError(
                    $"Failed to import backup:\n{ex.Message}",
                    "Import Error");
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private BackupType ConvertBackupType(string typeString)
        {
            return typeString.ToLower() switch
            {
                "full" => BackupType.Full,
                "incremental" => BackupType.Incremental,
                "differential" => BackupType.Differential,
                _ => BackupType.Full
            };
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    // P/Invoke wrapper for BRS validation
    internal static class NativeBrsValidator
    {
        private const string NativeDllName = "SecureServerBackupEngine.dll";

        [DllImport(NativeDllName, CharSet = CharSet.Unicode)]
        private static extern bool Brs_ValidateBackupFile(
            [MarshalAs(UnmanagedType.LPWStr)] string filePath,
            out bool isBrsFormat,
            out bool isCompressed,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder backupName,
            int backupNameSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder backupType,
            int backupTypeSize,
            out long timestamp,
            out ulong originalSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder errorMsg,
            int errorMsgSize
        );

        public static bool ValidateBackupFile(
            string filePath,
            out bool isBrsFormat,
            out bool isCompressed,
            out string backupName,
            out string backupType,
            out DateTime timestamp,
            out long size,
            StringBuilder errorMsg,
            int errorMsgSize
        ) {
            var nameBuilder = new StringBuilder(256);
            var typeBuilder = new StringBuilder(64);
            long timestampTicks;
            ulong sizeULong;

            bool result = Brs_ValidateBackupFile(
                filePath,
                out isBrsFormat,
                out isCompressed,
                nameBuilder,
                256,
                typeBuilder,
                64,
                out timestampTicks,
                out sizeULong,
                errorMsg,
                errorMsgSize
            );

            backupName = nameBuilder.ToString();
            backupType = typeBuilder.ToString();
            timestamp = DateTime.FromFileTime(timestampTicks);
            size = (long)sizeULong;

            return result;
        }
    }
}
