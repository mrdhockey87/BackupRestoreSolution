using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BackupCommon;
using BackupUI.Services;
using Microsoft.Win32;
using System.IO;
using System.Text;

namespace BackupUI.Windows
{
    public partial class ActivityDetailWindow : Window
    {
        private readonly string? _filterJobName;
        private List<BackupLogEntry> _currentLogs = new();

        public ActivityDetailWindow(string? jobName)
        {
            InitializeComponent();
            _filterJobName = jobName;
            
            if (string.IsNullOrEmpty(jobName))
            {
                txtTitle.Text = "All Activities";
                Title = "All Activity Details";
            }
            else
            {
                txtTitle.Text = $"Activities for: {jobName}";
                Title = $"Activity Details - {jobName}";
            }

            LoadActivities();
        }

        private void LoadActivities()
        {
            try
            {
                var allLogs = BackupLogger.GetRecentLogs(10000);

                if (!string.IsNullOrEmpty(_filterJobName))
                {
                    _currentLogs = allLogs.Where(l => l.JobName == _filterJobName).ToList();
                }
                else
                {
                    _currentLogs = allLogs;
                }

                if (dgActivities != null)
                {
                    dgActivities.ItemsSource = _currentLogs;
                }
                UpdateStatusText();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading activities: {ex.Message}\n\nDetails: {ex.StackTrace}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadActivities();
        }

        private void FilterLevel_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Add null checks to prevent issues during initialization
            if (cmbFilterLevel == null || dgActivities == null)
                return;
                
            if (cmbFilterLevel.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                var filter = item.Content.ToString();
                if (string.IsNullOrEmpty(filter))
                    return;
                    
                var allLogs = BackupLogger.GetRecentLogs(10000);

                if (!string.IsNullOrEmpty(_filterJobName))
                {
                    allLogs = allLogs.Where(l => l.JobName == _filterJobName).ToList();
                }

                switch (filter)
                {
                    case "Info":
                        _currentLogs = allLogs.Where(l => l.Level == BackupLogLevel.Info).ToList();
                        break;
                    case "Success":
                        _currentLogs = allLogs.Where(l => l.Level == BackupLogLevel.Success).ToList();
                        break;
                    case "Warning":
                        _currentLogs = allLogs.Where(l => l.Level == BackupLogLevel.Warning).ToList();
                        break;
                    case "Error":
                        _currentLogs = allLogs.Where(l => l.Level == BackupLogLevel.Error).ToList();
                        break;
                    default:
                        _currentLogs = allLogs;
                        break;
                }

                dgActivities.ItemsSource = _currentLogs;
                UpdateStatusText();
            }
        }

        private void Activities_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgActivities == null || txtSelectionCount == null)
                return;
                
            int selectedCount = dgActivities.SelectedItems.Count;
            txtSelectionCount.Text = selectedCount == 1 
                ? "1 activity selected" 
                : $"{selectedCount} activities selected";
        }

        private void ExportCSV_Click(object sender, RoutedEventArgs e)
        {
            ExportSelected("CSV");
        }

        private void ExportText_Click(object sender, RoutedEventArgs e)
        {
            ExportSelected("Text");
        }

        private void ContextExportCSV_Click(object sender, RoutedEventArgs e)
        {
            ExportSelected("CSV");
        }

        private void ContextExportText_Click(object sender, RoutedEventArgs e)
        {
            ExportSelected("Text");
        }

        private void ExportSelected(string format)
        {
            var selectedLogs = dgActivities.SelectedItems.Cast<BackupLogEntry>().ToList();

            if (selectedLogs.Count == 0)
            {
                MessageBox.Show("Please select activities to export.", 
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var suggestedName = string.IsNullOrEmpty(_filterJobName)
                ? "activities_export"
                : $"{_filterJobName}_activities_export";

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
                        ExportToCSV(selectedLogs, dialog.FileName);
                    }
                    else
                    {
                        ExportToText(selectedLogs, dialog.FileName);
                    }

                    MessageBox.Show($"Successfully exported {selectedLogs.Count} activities to:\n{dialog.FileName}",
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting activities: {ex.Message}",
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedActivities();
        }

        private void ContextDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedActivities();
        }

        private void DeleteSelectedActivities()
        {
            var selectedLogs = dgActivities.SelectedItems.Cast<BackupLogEntry>().ToList();

            if (selectedLogs.Count == 0)
            {
                MessageBox.Show("Please select activities to delete.", 
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete {selectedLogs.Count} selected activity log(s)?\n\n" +
                "This action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Delete selected logs from BackupLogger
                    int deletedCount = 0;
                    foreach (var log in selectedLogs)
                    {
                        if (BackupLogger.DeleteLogEntry(log))
                        {
                            deletedCount++;
                        }
                    }

                    MessageBox.Show($"Successfully deleted {deletedCount} activity log(s).",
                        "Delete Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Refresh the display
                    LoadActivities();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting activities: {ex.Message}",
                        "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ContextSelectAll_Click(object sender, RoutedEventArgs e)
        {
            dgActivities.SelectAll();
        }

        private void ContextClearSelection_Click(object sender, RoutedEventArgs e)
        {
            dgActivities.UnselectAll();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            dgActivities.SelectAll();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            dgActivities.UnselectAll();
        }

        private void CopySelected_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedToClipboard();
        }

        private void ContextCopySelected_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedToClipboard();
        }

        private void CopySelectedToClipboard()
        {
            var selectedLogs = dgActivities.SelectedItems.Cast<BackupLogEntry>().ToList();

            if (selectedLogs.Count == 0)
            {
                MessageBox.Show("Please select activities to copy.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var text = new StringBuilder();

                foreach (var log in selectedLogs.OrderBy(l => l.Timestamp))
                {
                    text.AppendLine($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] [{log.Level}] {log.JobName}");
                    text.AppendLine($"  Message: {log.Message}");
                    if (!string.IsNullOrEmpty(log.Details))
                        text.AppendLine($"  Details: {log.Details}");
                    if (!string.IsNullOrEmpty(log.BackupPath))
                        text.AppendLine($"  Backup Path: {log.BackupPath}");
                    text.AppendLine($"  Validation: {(log.ValidationPassed ? "PASSED" : "FAILED")}");
                    text.AppendLine();
                }

                Clipboard.SetText(text.ToString());

                MessageBox.Show($"Copied {selectedLogs.Count} activity log(s) to clipboard.",
                    "Copy Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying to clipboard: {ex.Message}",
                    "Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (!string.IsNullOrEmpty(_filterJobName))
                text.AppendLine($"Job: {_filterJobName}");
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

        private void UpdateStatusText()
        {
            if (txtStatus == null)
                return;
                
            if (string.IsNullOrEmpty(_filterJobName))
            {
                txtStatus.Text = $"Showing {_currentLogs.Count} activities from all jobs. " +
                                "Use Shift+Click or Ctrl+Click to select multiple. Right-click for options.";
            }
            else
            {
                txtStatus.Text = $"Showing {_currentLogs.Count} activities for {_filterJobName}. " +
                                "Use Shift+Click or Ctrl+Click to select multiple. Right-click for options.";
            }
        }
    }
}
