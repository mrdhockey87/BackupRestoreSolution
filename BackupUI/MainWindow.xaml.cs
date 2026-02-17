using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BackupUI.Models;
using BackupUI.Services;
using BackupUI.Windows;

namespace BackupUI
{
    public partial class MainWindow : Window
    {
        private readonly JobManager jobManager = new();
        private ObservableCollection<BackupJobViewModel> backupJobs = new();

        public MainWindow()
        {
            InitializeComponent();
            LoadVersion();
            LoadBackupJobs();
            NotificationService.Initialize();
            UpdateActivityTabWarning();
            
            // Check for unread errors periodically
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(30);
            timer.Tick += (s, e) => UpdateActivityTabWarning();
            timer.Start();
        }

        private void LoadVersion()
        {
            txtVersion.Text = VersionClass.GetVersion();
        }

        private void LoadBackupJobs()
        {
            backupJobs.Clear();
            
            // Reload from file to get latest changes
            System.Diagnostics.Debug.WriteLine("MainWindow: Loading backup jobs...");
            var jobs = jobManager.GetAllJobs();
            System.Diagnostics.Debug.WriteLine($"MainWindow: Loaded {jobs.Count} jobs");

            foreach (var job in jobs)
            {
                backupJobs.Add(new BackupJobViewModel(job));
            }

            lstBackupJobs.ItemsSource = backupJobs;

            // Show/hide "no jobs" message
            if (backupJobs.Count == 0)
            {
                txtNoJobs.Visibility = Visibility.Visible;
                lstBackupJobs.Visibility = Visibility.Collapsed;
            }
            else
            {
                txtNoJobs.Visibility = Visibility.Collapsed;
                lstBackupJobs.Visibility = Visibility.Visible;
            }
        }

        private void RefreshJobs_Click(object sender, RoutedEventArgs e)
        {
            LoadBackupJobs();
        }

        private async void RunJobNow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is System.Guid jobId)
            {
                var job = jobManager.GetJob(jobId);
                if (job != null)
                {
                    // Check if BackupRestoreService is installed and running
                    bool serviceOk = CheckBackupService();
                    if (!serviceOk)
                    {
                        return; // CheckBackupService already showed error message
                    }

                    var result = MessageBox.Show(
                        $"Run backup job '{job.Name}' now?\n\nThe backup will run in the background service and continue even if you close this window.",
                        "Run Backup",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Log the manual backup attempt immediately
                        BackupLogger.LogInfo(job.Name, $"User initiated manual backup (Run Now clicked)");
                        
                        // Send job to service and show progress window
                        var serviceClient = new Services.BackupServiceClient();
                        var success = await serviceClient.RunBackupNowAsync(jobId);

                        if (success)
                        {
                            BackupLogger.LogInfo(job.Name, "Service accepted backup request - backup is starting");
                            
                            // Show non-modal progress window
                            var progressWindow = new Windows.BackupProgressWindow(jobId, job.Name);
                            progressWindow.Show();
                        }
                        else
                        {
                            // Log the failure to Activity tab
                            BackupLogger.LogError(job.Name, "Failed to communicate with BackupRestoreService - backup was not started");
                            
                            MessageBox.Show(
                                "Failed to start backup. The service may be busy or not responding.\n\n" +
                                "Try again in a few moments, or restart the BackupRestoreService from Windows Services.",
                                "Service Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        private bool CheckBackupService()
        {
            try
            {
                using var service = new System.ServiceProcess.ServiceController("BackupRestoreService");
                
                if (service.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    // Log service status issue to Activity tab
                    BackupLogger.LogWarning("System", $"BackupRestoreService is not running (Status: {service.Status})");
                    
                    var result = MessageBox.Show(
                        $"The BackupRestoreService is not running (Status: {service.Status}).\n\n" +
                        "Would you like to start it now?\n\n" +
                        "Note: You may need to run this application as Administrator to start the service.",
                        "Service Not Running",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            BackupLogger.LogInfo("System", "Attempting to start BackupRestoreService...");
                            service.Start();
                            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                            BackupLogger.LogInfo("System", "BackupRestoreService started successfully");
                            MessageBox.Show(
                                "BackupRestoreService started successfully.",
                                "Service Started",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            BackupLogger.LogError("System", $"Failed to start BackupRestoreService: {ex.Message}");
                            MessageBox.Show(
                                $"Failed to start service: {ex.Message}\n\n" +
                                "Please start the service manually from Windows Services (services.msc) or run this application as Administrator.",
                                "Service Start Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            return false;
                        }
                    }
                    return false;
                }
                return true;
            }
            catch (System.InvalidOperationException)
            {
                // Service doesn't exist - log this critical issue
                BackupLogger.LogError("System", "BackupRestoreService is not installed on this system");
                
                var result = MessageBox.Show(
                    "The BackupRestoreService is not installed on this system.\n\n" +
                    "The service must be installed before backups can run.\n\n" +
                    "To install the service:\n" +
                    "1. Open PowerShell as Administrator\n" +
                    "2. Navigate to the solution folder\n" +
                    "3. Run: .\\Install-BackupService.ps1\n\n" +
                    "Would you like to open the solution folder now?",
                    "Service Not Installed",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        string solutionDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                        // Navigate up to solution root (from artifacts\bin\Debug)
                        solutionDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(solutionDir, "..", "..", ".."));
                        System.Diagnostics.Process.Start("explorer.exe", solutionDir);
                    }
                    catch { }
                }
                return false;
            }
            catch (Exception ex)
            {
                BackupLogger.LogError("System", $"Error checking BackupRestoreService status: {ex.Message}");
                MessageBox.Show(
                    $"Error checking service status: {ex.Message}",
                    "Service Check Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        private void EditJob_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is System.Guid jobId)
            {
                var job = jobManager.GetJob(jobId);
                if (job != null)
                {
                    var window = new BackupWindowNew(job);
                    if (window.ShowDialog() == true)
                    {
                        LoadBackupJobs();
                    }
                }
            }
        }

        private void DeleteJob_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is System.Guid jobId)
            {
                var job = jobManager.GetJob(jobId);
                if (job != null)
                {
                    // Check if backup files exist
                    bool backupsExist = CheckBackupsExist(job.DestinationPath);

                    if (!backupsExist)
                    {
                        // Simple confirmation - no backups exist
                        var result = MessageBox.Show(
                            $"Delete backup job '{job.Name}'?\n\n" +
                            $"Note: No backup files found at destination.",
                            "Delete Backup Job",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            jobManager.DeleteJob(jobId);
                            BackupLogger.LogInfo(job.Name, "Backup job deleted (no backup files existed)");
                            MessageBox.Show(
                                $"Backup job '{job.Name}' has been deleted.",
                                "Job Deleted",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            LoadBackupJobs();
                        }
                        return;
                    }

                    // Backups exist - show two-option dialog
                    var deleteDialog = new Window
                    {
                        Title = "Delete Backup Job",
                        Width = 450,
                        Height = 220,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Owner = this,
                        ResizeMode = ResizeMode.NoResize
                    };

                    var stackPanel = new StackPanel { Margin = new Thickness(20) };
                    
                    var icon = new System.Windows.Controls.TextBlock
                    {
                        Text = "??",
                        FontSize = 32,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    
                    var message = new System.Windows.Controls.TextBlock
                    {
                        Text = $"What would you like to delete for backup job:\n'{job.Name}'?",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                        Margin = new Thickness(0, 0, 0, 15),
                        TextAlignment = TextAlignment.Center
                    };

                    var btnJobOnly = new System.Windows.Controls.Button
                    {
                        Content = "Delete Job Only (Keep Backup Files)",
                        Height = 35,
                        Margin = new Thickness(0, 5, 0, 0),
                        Tag = "jobOnly"
                    };

                    var btnJobAndBackup = new System.Windows.Controls.Button
                    {
                        Content = "Delete Job AND Backup Files (Move to Recycle Bin)",
                        Height = 35,
                        Margin = new Thickness(0, 5, 0, 0),
                        Tag = "jobAndBackup",
                        Background = System.Windows.Media.Brushes.LightCoral
                    };

                    var btnCancel = new System.Windows.Controls.Button
                    {
                        Content = "Cancel",
                        Height = 35,
                        Margin = new Thickness(0, 10, 0, 0)
                    };

                    btnJobOnly.Click += (s, args) =>
                    {
                        deleteDialog.Tag = "jobOnly";
                        deleteDialog.DialogResult = true;
                    };

                    btnJobAndBackup.Click += (s, args) =>
                    {
                        deleteDialog.Tag = "jobAndBackup";
                        deleteDialog.DialogResult = true;
                    };

                    btnCancel.Click += (s, args) =>
                    {
                        deleteDialog.DialogResult = false;
                    };

                    stackPanel.Children.Add(icon);
                    stackPanel.Children.Add(message);
                    stackPanel.Children.Add(btnJobOnly);
                    stackPanel.Children.Add(btnJobAndBackup);
                    stackPanel.Children.Add(btnCancel);

                    deleteDialog.Content = stackPanel;

                    if (deleteDialog.ShowDialog() == true)
                    {
                        var choice = deleteDialog.Tag?.ToString();

                        if (choice == "jobAndBackup")
                        {
                            // Delete job and move backup files to recycle bin
                            DeleteJobAndBackupFiles(job);
                        }
                        else if (choice == "jobOnly")
                        {
                            // Delete job only
                            jobManager.DeleteJob(jobId);
                            BackupLogger.LogInfo(job.Name, "Backup job deleted (files preserved)");
                            MessageBox.Show(
                                $"Backup job '{job.Name}' has been deleted.\nBackup files have been preserved.",
                                "Job Deleted",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }

                        LoadBackupJobs();
                    }
                }
            }
        }

        private bool CheckBackupsExist(string destinationPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(destinationPath))
                    return false;

                // Check if directory exists and has files
                if (System.IO.Directory.Exists(destinationPath))
                {
                    var files = System.IO.Directory.GetFiles(destinationPath, "*.*", System.IO.SearchOption.AllDirectories);
                    return files.Length > 0;
                }

                // Check if it's a file path
                if (System.IO.File.Exists(destinationPath))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                // If we can't check, assume no backups exist
                System.Diagnostics.Debug.WriteLine($"Error checking backups: {ex.Message}");
                return false;
            }
        }

        private void DeleteJobAndBackupFiles(BackupJob job)
        {
            try
            {
                BackupLogger.LogWarning(job.Name, "Deleting job and moving backup files to recycle bin");

                // Find all backup files in the destination path
                var backupFiles = new System.Collections.Generic.List<string>();
                bool filesDeleted = false;
                
                if (System.IO.Directory.Exists(job.DestinationPath))
                {
                    // Get all files and subdirectories
                    backupFiles.AddRange(System.IO.Directory.GetFiles(job.DestinationPath, "*.*", System.IO.SearchOption.AllDirectories));
                    
                    if (backupFiles.Count > 0)
                    {
                        // Move directory to recycle bin
                        MoveToRecycleBin(job.DestinationPath);
                        BackupLogger.LogInfo(job.Name, $"Moved {backupFiles.Count} backup file(s) to recycle bin", job.DestinationPath);
                        filesDeleted = true;
                    }
                    else
                    {
                        // Directory exists but is empty - just delete it
                        try
                        {
                            System.IO.Directory.Delete(job.DestinationPath, false);
                            BackupLogger.LogInfo(job.Name, "Removed empty backup directory", job.DestinationPath);
                        }
                        catch
                        {
                            // Ignore errors deleting empty directory
                        }
                    }
                }
                else if (System.IO.File.Exists(job.DestinationPath))
                {
                    // Single file backup
                    MoveToRecycleBin(job.DestinationPath);
                    BackupLogger.LogInfo(job.Name, "Moved backup file to recycle bin", job.DestinationPath);
                    filesDeleted = true;
                }
                else
                {
                    BackupLogger.LogWarning(job.Name, "Backup destination path not found", job.DestinationPath);
                }

                // Delete the job from job manager
                jobManager.DeleteJob(job.Id);

                // Show appropriate message
                if (filesDeleted)
                {
                    MessageBox.Show(
                        $"Backup job '{job.Name}' has been deleted.\n\n" +
                        $"Backup files moved to Recycle Bin:\n{job.DestinationPath}\n\n" +
                        $"You can restore them from the Recycle Bin if needed.",
                        "Job and Backups Deleted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"Backup job '{job.Name}' has been deleted.\n\n" +
                        $"No backup files were found to delete.",
                        "Job Deleted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                BackupLogger.LogError(job.Name, "Failed to delete backup files", ex.Message);
                MessageBox.Show(
                    $"Error deleting backup files:\n{ex.Message}\n\nThe job has been removed, but backup files may still exist.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void MoveToRecycleBin(string path)
        {
            try
            {
                if (System.IO.Directory.Exists(path))
                {
                    // Use Microsoft.VisualBasic FileIO for recycle bin support
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else if (System.IO.File.Exists(path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to move to recycle bin: {ex.Message}", ex);
            }
        }
        
        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        
        private void NewBackup_Click(object sender, RoutedEventArgs e)
        {
            var window = new BackupWindowNew();
            if (window.ShowDialog() == true)
            {
                LoadBackupJobs();
            }
        }
        
        private void ImportBackup_Click(object sender, RoutedEventArgs e)
        {
            var window = new ImportBackupWindow();
            if (window.ShowDialog() == true)
            {
                LoadBackupJobs();
            }
        }
        
        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            var window = new RestoreWindowNew();
            window.ShowDialog();
        }
        
        private void ManageSchedules_Click(object sender, RoutedEventArgs e) => new ScheduleManagementWindow().ShowDialog();
        private void ActivityManagement_Click(object sender, RoutedEventArgs e) => new ActivityManagementWindow().ShowDialog();
        private void OpenActivityManagement_Click(object sender, RoutedEventArgs e) => new ActivityManagementWindow().ShowDialog();
        private void ServiceManagement_Click(object sender, RoutedEventArgs e) => new ServiceManagementWindow().ShowDialog();
        private void RecoveryEnvironmentCreator_Click(object sender, RoutedEventArgs e) => new RecoveryEnvironmentWindow().ShowDialog();
        
        private void About_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow { Owner = this };
            aboutWindow.ShowDialog();
        }

        // OLD Activity Tab Methods - REMOVED (now using job summary tab)
        // These methods were for the old dgActivityLog DataGrid which no longer exists
        // New Activity tab uses dgJobLogs and shows job summaries instead

        // Tab selection handler - load activity when tab is selected
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is TabControl tabControl && tabControl.SelectedIndex == 1) // Activity tab is index 1
            {
                LoadJobLogsTab();
                // Mark all errors as read when user views Activity tab
                BackupLogger.MarkAllErrorsAsRead();
                UpdateActivityTabWarning();
            }
        }

        // NEW: Load job logs for Activity tab
        private void LoadJobLogsTab()
        {
            try
            {
                var allLogs = BackupLogger.GetRecentLogs(10000);
                
                // Group by job name - filter out null or empty job names
                var jobGroups = allLogs
                    .Where(log => !string.IsNullOrEmpty(log.JobName))
                    .GroupBy(log => log.JobName)
                    .Select(group => new JobLogSummary
                    {
                        JobName = group.Key ?? "Unknown",
                        TotalActivities = group.Count(),
                        LastActivity = group.Max(l => l.Timestamp),
                        SuccessCount = group.Count(l => l.Level == BackupLogLevel.Success),
                        WarningCount = group.Count(l => l.Level == BackupLogLevel.Warning),
                        ErrorCount = group.Count(l => l.Level == BackupLogLevel.Error),
                        InfoCount = group.Count(l => l.Level == BackupLogLevel.Info)
                    })
                    .OrderByDescending(s => s.LastActivity)
                    .ToList();

                if (dgJobLogs != null)  // Will be null until XAML is updated
                {
                    dgJobLogs.ItemsSource = jobGroups;
                }
                
                if (txtJobLogsStatus != null)  // Will be null until XAML is updated
                {
                    txtJobLogsStatus.Text = $"Found {jobGroups.Count} backup jobs with activity logs. Double-click to view details.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading job logs: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // NEW: Activity tab event handlers
        private void RefreshJobLogs_Click(object sender, RoutedEventArgs e)
        {
            LoadJobLogsTab();
        }

        private void ViewAllActivitiesFromTab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var detailWindow = new ActivityDetailWindow(null);
                detailWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening activity details: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ViewJobDetailsFromTab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ViewJobDetailsFromTab_Click called");
                
                if (sender is Button btn)
                {
                    System.Diagnostics.Debug.WriteLine($"Button sender confirmed. Tag type: {btn.Tag?.GetType().Name}");
                    System.Diagnostics.Debug.WriteLine($"Tag value: {btn.Tag}");
                    
                    if (btn.Tag is string jobName)
                    {
                        System.Diagnostics.Debug.WriteLine($"JobName extracted: '{jobName}'");
                        
                        if (!string.IsNullOrEmpty(jobName))
                        {
                            System.Diagnostics.Debug.WriteLine($"Opening ActivityDetailWindow for job: {jobName}");
                            var detailWindow = new ActivityDetailWindow(jobName);
                            detailWindow.ShowDialog();
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("JobName is null or empty!");
                            MessageBox.Show("Job name is empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Tag is not a string! Tag type: {btn.Tag?.GetType().Name}");
                        MessageBox.Show("Invalid job information.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Sender is not a Button! Sender type: {sender?.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in ViewJobDetailsFromTab_Click: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error opening activity details: {ex.Message}\n\n{ex.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void JobLog_DoubleClickFromTab(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("JobLog_DoubleClickFromTab called");
                
                if (dgJobLogs != null)
                {
                    System.Diagnostics.Debug.WriteLine($"dgJobLogs is not null. SelectedItem type: {dgJobLogs.SelectedItem?.GetType().Name}");
                    
                    if (dgJobLogs.SelectedItem is JobLogSummary summary)
                    {
                        System.Diagnostics.Debug.WriteLine($"JobLogSummary extracted. JobName: '{summary.JobName}'");
                        
                        if (!string.IsNullOrEmpty(summary.JobName))
                        {
                            System.Diagnostics.Debug.WriteLine($"Opening ActivityDetailWindow for job: {summary.JobName}");
                            var detailWindow = new ActivityDetailWindow(summary.JobName);
                            detailWindow.ShowDialog();
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("JobName is null or empty!");
                            MessageBox.Show("Job name is empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"SelectedItem is not JobLogSummary! Type: {dgJobLogs.SelectedItem?.GetType().Name}");
                        MessageBox.Show("Please select a job first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("dgJobLogs is null!");
                    MessageBox.Show("Job logs grid not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in JobLog_DoubleClickFromTab: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error opening activity details: {ex.Message}\n\n{ex.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportJobLogFromTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string jobName && !string.IsNullOrEmpty(jobName))
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
                    ExportActivitiesFromTab(jobLogs, exportDialog.ExportFormat, $"{jobName}_activities");
                }
            }
        }

        private void ExportActivitiesFromTab(List<BackupLogEntry> logs, string format, string suggestedName)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
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
                        ExportToCSVFromTab(logs, dialog.FileName);
                    }
                    else
                    {
                        ExportToTextFromTab(logs, dialog.FileName);
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

        private void ExportToCSVFromTab(List<BackupLogEntry> logs, string filePath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Timestamp,Job Name,Level,Message,Details,Backup Path,Validation Passed");

            foreach (var log in logs.OrderBy(l => l.Timestamp))
            {
                csv.AppendLine($"\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                              $"\"{EscapeCSVFromTab(log.JobName)}\"," +
                              $"\"{log.Level}\"," +
                              $"\"{EscapeCSVFromTab(log.Message)}\"," +
                              $"\"{EscapeCSVFromTab(log.Details)}\"," +
                              $"\"{EscapeCSVFromTab(log.BackupPath)}\"," +
                              $"\"{log.ValidationPassed}\"");
            }

            File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        }

        private void ExportToTextFromTab(List<BackupLogEntry> logs, string filePath)
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

        private string EscapeCSVFromTab(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "");
        }

        // Update Activity tab header with warning icon if there are unread errors
        private void UpdateActivityTabWarning()
        {
            if (tabActivity == null)
                return;

            bool hasUnread = BackupLogger.HasUnreadErrors();
            
            if (hasUnread)
            {
                tabActivity.Header = "Activity ??";
                tabActivity.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 140, 0)); // Orange/Yellow
            }
            else
            {
                tabActivity.Header = "Activity";
                tabActivity.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        // Public method to show Activity tab (called from notifications)
        public void ShowActivityTab()
        {
            var tabControl = this.FindName("mainTabControl") as TabControl;
            if (tabControl != null)
            {
                tabControl.SelectedIndex = 1; // Switch to Activity tab
            }
        }

        // Mount Backups Tab Methods
        private void RefreshMounts_Click(object sender, RoutedEventArgs e)
        {
            LoadAvailableBackups();
            LoadMountedBackups();
        }

        private void MountBackup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is AvailableBackupInfo backup)
            {
                try
                {
                    // Get selected backup point if Inc/Diff
                    string vhdxPath = GetBackupPointPath(backup);

                    if (string.IsNullOrEmpty(vhdxPath))
                    {
                        MessageBox.Show("Please select a backup point to mount.",
                                      "No Backup Point Selected",
                                      MessageBoxButton.OK,
                                      MessageBoxImage.Warning);
                        return;
                    }

                    var (success, driveLetter, error) = BackupMountManager.MountBackup(
                        vhdxPath,
                        backup.BackupName,
                        backup.BackupType,
                        backup.BackupDate);

                    if (success)
                    {
                        MessageBox.Show($"Backup mounted as {driveLetter}:\n\n" +
                                      $"You can now browse the backup in Windows Explorer.\n" +
                                      $"Drive is READ-ONLY to prevent modifications.",
                                      "Backup Mounted",
                                      MessageBoxButton.OK,
                                      MessageBoxImage.Information);

                        LoadMountedBackups();
                        OpenExplorer(driveLetter);
                    }
                    else
                    {
                        MessageBox.Show($"Failed to mount backup:\n{error}",
                                      "Mount Error",
                                      MessageBoxButton.OK,
                                      MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error mounting backup:\n{ex.Message}",
                                  "Error",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Error);
                }
            }
        }

        private void UnmountBackup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string driveLetter)
            {
                var result = MessageBox.Show(
                    $"Unmount backup drive {driveLetter}?",
                    "Unmount Backup",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var (success, error) = BackupMountManager.UnmountBackup(driveLetter);

                    if (success)
                    {
                        LoadMountedBackups();
                        MessageBox.Show($"Drive {driveLetter} unmounted successfully.",
                                      "Success",
                                      MessageBoxButton.OK,
                                      MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"Failed to unmount:\n{error}",
                                      "Unmount Error",
                                      MessageBoxButton.OK,
                                      MessageBoxImage.Error);
                    }
                }
            }
        }

        private void UnmountAll_Click(object sender, RoutedEventArgs e)
        {
            var mounted = BackupMountManager.GetMountedBackups();

            if (mounted.Count == 0)
            {
                MessageBox.Show("No mounted backups to unmount.",
                              "No Mounted Backups",
                              MessageBoxButton.OK,
                              MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Unmount all {mounted.Count} mounted backup drive(s)?",
                "Unmount All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                BackupMountManager.UnmountAll();
                LoadMountedBackups();
                MessageBox.Show("All backups unmounted successfully.",
                              "Success",
                              MessageBoxButton.OK,
                              MessageBoxImage.Information);
            }
        }

        private void AvailableBackups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgAvailableBackups == null || pnlBackupPoints == null)
                return;

            if (dgAvailableBackups.SelectedItem is AvailableBackupInfo backup)
            {
                if (backup.BackupType == "Incremental" || backup.BackupType == "Differential")
                {
                    LoadBackupPoints(backup);
                    pnlBackupPoints.Visibility = Visibility.Visible;
                }
                else
                {
                    pnlBackupPoints.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void LoadAvailableBackups()
        {
            if (dgAvailableBackups == null)
                return;

            var backups = new System.Collections.Generic.List<AvailableBackupInfo>();

            try
            {
                // Scan backup directories for VHDX files
                var jobs = jobManager.GetAllJobs();

                foreach (var job in jobs)
                {
                    string destPath = job.DestinationPath;

                    if (System.IO.Directory.Exists(destPath))
                    {
                        // Find VHDX files
                        var vhdxFiles = System.IO.Directory.GetFiles(destPath, "*.vhdx", System.IO.SearchOption.AllDirectories);

                        foreach (var vhdx in vhdxFiles)
                        {
                            var fileInfo = new System.IO.FileInfo(vhdx);

                            backups.Add(new AvailableBackupInfo
                            {
                                BackupName = job.Name,
                                BackupType = GetBackupTypeFromPath(vhdx),
                                BackupDate = fileInfo.LastWriteTime,
                                BackupPath = vhdx
                            });
                        }
                    }
                }

                dgAvailableBackups.ItemsSource = backups;

                if (txtNoBackups != null)
                    txtNoBackups.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading available backups: {ex.Message}");
            }
        }

        private void LoadMountedBackups()
        {
            if (dgMountedBackups == null)
                return;

            var mounted = BackupMountManager.GetMountedBackups();
            dgMountedBackups.ItemsSource = mounted;
        }

        private void LoadBackupPoints(AvailableBackupInfo backup)
        {
            if (cmbBackupPoints == null)
                return;

            // For now, just show the main backup
            // In a full implementation, scan for incremental/differential points
            var points = new System.Collections.Generic.List<BackupPoint>
            {
                new BackupPoint
                {
                    PointDate = backup.BackupDate,
                    PointType = backup.BackupType,
                    VhdxPath = backup.BackupPath
                }
            };

            cmbBackupPoints.ItemsSource = points;
            cmbBackupPoints.DisplayMemberPath = "DisplayName";
            if (points.Count > 0)
                cmbBackupPoints.SelectedIndex = 0;
        }

        private string GetBackupPointPath(AvailableBackupInfo backup)
        {
            if (backup.BackupType == "Incremental" || backup.BackupType == "Differential")
            {
                if (cmbBackupPoints?.SelectedItem is BackupPoint point)
                {
                    return point.VhdxPath;
                }
                return "";
            }
            else
            {
                return backup.BackupPath;
            }
        }

        private string GetBackupTypeFromPath(string path)
        {
            string filename = System.IO.Path.GetFileName(path).ToLower();

            if (filename.Contains("full"))
                return "Full";
            else if (filename.Contains("incr"))
                return "Incremental";
            else if (filename.Contains("diff"))
                return "Differential";
            else
                return "Full"; // Default
        }

        private void OpenExplorer(string driveLetter)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", driveLetter);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open Explorer: {ex.Message}");
            }
        }
    }

    // ViewModel for displaying backup jobs
    public class BackupJobViewModel
    {
        public BackupJobViewModel(BackupJob job)
        {
            Id = job.Id;
            Name = job.Name;
            DestinationPath = job.DestinationPath;

            // Type description
            TypeDescription = job.Type switch
            {
                BackupType.Full => "Full Backup",
                BackupType.Incremental => "Full then Incremental",
                BackupType.Differential => "Full then Differential",
                BackupType.CloneToDisk => "Clone to Physical Disk",
                BackupType.CloneToVirtualDisk => "Clone to Virtual Disk (Hyper-V)",
                _ => job.Type.ToString()
            };

            // Source description
            if (job.IsHyperVBackup && job.HyperVMachines.Count > 0)
            {
                SourceDescription = $"Hyper-V: {string.Join(", ", job.HyperVMachines)}";
            }
            else if (job.Target == BackupTarget.FilesAndFolders)
            {
                var volumeLetters = job.SourcePaths
                    .Select(p => System.IO.Path.GetPathRoot(p)?.TrimEnd('\\'))
                    .Distinct()
                    .Where(v => !string.IsNullOrEmpty(v));

                SourceDescription = $"{string.Join(", ", volumeLetters)} - Files & Folders";
            }
            else if (job.Target == BackupTarget.Disk)
            {
                SourceDescription = $"Disk: {string.Join(", ", job.SourcePaths)}";
            }
            else if (job.Target == BackupTarget.Volume)
            {
                SourceDescription = $"Volume: {string.Join(", ", job.SourcePaths)}";
            }
            else
            {
                SourceDescription = string.Join(", ", job.SourcePaths);
            }

            // Schedule description
            if (job.Schedule == null || !job.Schedule.Enabled)
            {
                ScheduleDescription = "No schedule (manual only)";
            }
            else
            {
                var freq = job.Schedule.Frequency switch
                {
                    ScheduleFrequency.Daily => "Daily",
                    ScheduleFrequency.Weekly => $"Weekly on {string.Join(", ", job.Schedule.DaysOfWeek)}",
                    ScheduleFrequency.Monthly => $"Monthly on day {job.Schedule.DayOfMonth}",
                    _ => job.Schedule.Frequency.ToString()
                };
                ScheduleDescription = $"{freq} at {job.Schedule.Time:hh\\:mm}";
            }
        }

        public System.Guid Id { get; set; }
        public string Name { get; set; }
        public string TypeDescription { get; set; }
        public string SourceDescription { get; set; }
        public string DestinationPath { get; set; }
        public string ScheduleDescription { get; set; }
    }

    // Job log summary for Activity tab
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

