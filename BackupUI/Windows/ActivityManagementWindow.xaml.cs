using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BackupCommon;
using BackupUI.Services;
using Microsoft.Win32;
using System.IO;
using System.Text;

namespace BackupUI.Windows
{
    public partial class ActivityManagementWindow : Window
    {
        public ActivityManagementWindow()
        {
            InitializeComponent();
            LoadJobLogs();
        }

        private void LoadJobLogs()
        {
            try
            {
                var allLogs = BackupLogger.GetRecentLogs(10000); // Get more logs for aggregation
                
                // Group by job name - filter out null or empty job names
                var jobGroups = allLogs
                    .Where(log => !string.IsNullOrEmpty(log.JobName))  // Filter out null/empty job names
                    .GroupBy(log => log.JobName)
                    .Select(group => new JobLogSummary
                    {
                        JobName = group.Key ?? "Unknown",  // Provide fallback for null
                        TotalActivities = group.Count(),
                        LastActivity = group.Max(l => l.Timestamp),
                        SuccessCount = group.Count(l => l.Level == BackupLogLevel.Success),
                        WarningCount = group.Count(l => l.Level == BackupLogLevel.Warning),
                        ErrorCount = group.Count(l => l.Level == BackupLogLevel.Error),
                        InfoCount = group.Count(l => l.Level == BackupLogLevel.Info)
                    })
                    .OrderByDescending(s => s.LastActivity)
                    .ToList();

                dgJobLogs.ItemsSource = jobGroups;
                txtStatus.Text = $"Found {jobGroups.Count} backup jobs with activity logs";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading job logs: {ex.Message}\n\nDetails: {ex.StackTrace}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadJobLogs();
        }

        private void ViewAllActivities_Click(object sender, RoutedEventArgs e)
        {
            var detailWindow = new ActivityDetailWindow(null);
            detailWindow.ShowDialog();
        }

        private void ViewJobDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string jobName && !string.IsNullOrEmpty(jobName))
            {
                try
                {
                    var detailWindow = new ActivityDetailWindow(jobName);
                    detailWindow.ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening activity details: {ex.Message}\n\nDetails: {ex.StackTrace}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void JobLog_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dgJobLogs.SelectedItem is JobLogSummary summary && !string.IsNullOrEmpty(summary.JobName))
            {
                try
                {
                    var detailWindow = new ActivityDetailWindow(summary.JobName);
                    detailWindow.ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening activity details: {ex.Message}\n\nDetails: {ex.StackTrace}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportJobLog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string jobName)
            {
                var allLogs = BackupLogger.GetRecentLogs(10000);
                var jobLogs = allLogs.Where(l => l.JobName == jobName).ToList();

                if (jobLogs.Count == 0)
                {
                    MessageBox.Show("No activities found for this job.", 
                        "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Show export options
                var exportDialog = new ExportOptionsDialog();
                if (exportDialog.ShowDialog() == true)
                {
                    ExportActivities(jobLogs, exportDialog.ExportFormat, $"{jobName}_activities");
                }
            }
        }

        private void ExportActivities(List<BackupLogEntry> logs, string format, string suggestedName)
        {
            var dialog = new SaveFileDialog
            {
                FileName = suggestedName,
                Filter = format == "CSV" 
                    ? "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
                    : "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = format == "CSV" ? ".csv" : ".txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (format == "CSV")
                    {
                        ExportToCSV(logs, dialog.FileName);
                    }
                    else
                    {
                        ExportToText(logs, dialog.FileName);
                    }

                    MessageBox.Show($"Successfully exported {logs.Count} activities to:\n{dialog.FileName}",
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting activities: {ex.Message}",
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportToCSV(List<BackupLogEntry> logs, string filePath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Timestamp,Job Name,Level,Message,Details,Backup Path,Validation Passed");

            foreach (var log in logs.OrderBy(l => l.Timestamp))
            {
                csv.AppendLine($"\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                              $"\"{EscapeCSV(log.JobName)}\"," +
                              $"\"{log.Level}\"," +
                              $"\"{EscapeCSV(log.Message)}\"," +
                              $"\"{EscapeCSV(log.Details)}\"," +
                              $"\"{EscapeCSV(log.BackupPath)}\"," +
                              $"\"{log.ValidationPassed}\"");
            }

            File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        }

        private void ExportToText(List<BackupLogEntry> logs, string filePath)
        {
            var text = new StringBuilder();
            text.AppendLine("===== BACKUP ACTIVITY LOG =====");
            text.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            text.AppendLine($"Total Entries: {logs.Count}");
            text.AppendLine("================================");
            text.AppendLine();

            foreach (var log in logs.OrderBy(l => l.Timestamp))
            {
                text.AppendLine($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] [{log.Level}] {log.JobName}");
                text.AppendLine($"  Message: {log.Message}");
                if (!string.IsNullOrEmpty(log.Details))
                    text.AppendLine($"  Details: {log.Details}");
                if (!string.IsNullOrEmpty(log.BackupPath))
                    text.AppendLine($"  Backup Path: {log.BackupPath}");
                if (!string.IsNullOrEmpty(log.BackupPath))
                    text.AppendLine($"  Validation: {(log.ValidationPassed ? "PASSED" : "FAILED")}");
                text.AppendLine();
            }

            File.WriteAllText(filePath, text.ToString(), Encoding.UTF8);
        }

        private string EscapeCSV(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "");
        }
    }

    // Data model for job log summary
    public class JobLogSummary
    {
        public string JobName { get; set; } = string.Empty;
        public int TotalActivities { get; set; }
        public DateTime LastActivity { get; set; }
        public int SuccessCount { get; set; }
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }
        public int InfoCount { get; set; }
    }
}
