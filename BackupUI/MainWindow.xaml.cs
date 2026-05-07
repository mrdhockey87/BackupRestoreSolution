using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SecureServerBackupCommon;
using SecureServerBackup.Models;
using SecureServerBackup.Services;
using SecureServerBackup.Windows;

namespace SecureServerBackup
{
    public partial class MainWindow : Window
    {
        private readonly JobManager jobManager = new();
        private readonly BackupServiceClient backupServiceClient = new();
        private ObservableCollection<BackupJobViewModel> backupJobs = new();
        private JobLogSummary? selectedJobLog = null;  // Track selected job in Activity tab
        private readonly Dictionary<Guid, BackupProgressWindow> activeBackupProgressWindows = new();
        private readonly HashSet<Guid> suppressedAutoOpenProgressJobs = new();
        private readonly DispatcherTimer runningBackupMonitorTimer;
        private bool isCheckingForRunningBackups;

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

            runningBackupMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            runningBackupMonitorTimer.Tick += async (s, e) => await CheckForRunningBackupsAsync();
            runningBackupMonitorTimer.Start();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore saved window position
            WindowPositionManager.RestoreMainWindowPosition(this);

            await CheckForRunningBackupsAsync();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            runningBackupMonitorTimer.Stop();

            // Save window position for next time
            WindowPositionManager.SaveMainWindowPosition(this);
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

        private async Task CheckForRunningBackupsAsync()
        {
            if (isCheckingForRunningBackups)
            {
                return;
            }

            isCheckingForRunningBackups = true;

            try
            {
                var jobs = jobManager.GetAllJobs();
                var runningJobs = jobs.Where(job => job.IsCurrentlyRunning).ToList();
                var runningJobIds = runningJobs.Select(job => job.Id).ToHashSet();

                suppressedAutoOpenProgressJobs.RemoveWhere(jobId => !runningJobIds.Contains(jobId));

                foreach (var job in runningJobs)
                {
                    if (activeBackupProgressWindows.ContainsKey(job.Id) || suppressedAutoOpenProgressJobs.Contains(job.Id))
                    {
                        continue;
                    }

                    var progress = await backupServiceClient.GetProgressAsync(job.Id);
                    if (progress?.IsRunning == true)
                    {
                        ShowBackupProgressWindow(job.Id, job.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for running backups: {ex.Message}");
            }
            finally
            {
                isCheckingForRunningBackups = false;
            }
        }

        private void ShowBackupProgressWindow(Guid jobId, string jobName)
        {
            var existingOpenWindow = Application.Current.Windows
                .OfType<BackupProgressWindow>()
                .FirstOrDefault(window => window.JobId == jobId);

            if (existingOpenWindow != null)
            {
                if (!activeBackupProgressWindows.ContainsKey(jobId))
                {
                    TrackBackupProgressWindow(existingOpenWindow);
                }

                existingOpenWindow.Activate();
                return;
            }

            if (activeBackupProgressWindows.TryGetValue(jobId, out var existingWindow))
            {
                if (!existingWindow.IsVisible)
                {
                    existingWindow.Show();
                }

                existingWindow.Activate();
                return;
            }

            var progressWindow = new BackupProgressWindow(jobId, jobName);
            WindowPositionManager.SetChildWindowPosition(progressWindow, this);
            TrackBackupProgressWindow(progressWindow);

            progressWindow.Show();
        }

        private void TrackBackupProgressWindow(BackupProgressWindow progressWindow)
        {
            activeBackupProgressWindows[progressWindow.JobId] = progressWindow;
            progressWindow.Closed += BackupProgressWindow_Closed;
        }

        private void BackupProgressWindow_Closed(object? sender, EventArgs e)
        {
            if (sender is not BackupProgressWindow progressWindow)
            {
                return;
            }

            activeBackupProgressWindows.Remove(progressWindow.JobId);

            if (progressWindow.WasClosedWhileBackupRunning)
            {
                suppressedAutoOpenProgressJobs.Add(progressWindow.JobId);
            }

            progressWindow.Closed -= BackupProgressWindow_Closed;
        }

        private void ResetRunningFlag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is System.Guid jobId)
            {
                var result = CustomDialogService.ShowQuestion(this,
                    "Are you sure you want to reset this job to its scheduled state?\n\n" +
                    "This will:\n" +
                    "  • Clear the 'IsCurrentlyRunning' flag\n" +
                    "  • Reset consecutive failures to 0\n" +
                    "  • Recalculate next run time based on schedule\n\n" +
                    "This should only be done if the job is stuck in a 'Running' state when it's not actually running.\n\n" +
                    "If a backup is currently running, resetting could cause issues.",
                    "Reset Job State");

                if (result == CustomDialogResult.Yes)
                {
                    var job = jobManager.GetJob(jobId);
                    if (job != null)
                    {
                        // Reset all execution state flags
                        job.IsCurrentlyRunning = false;
                        job.ConsecutiveFailures = 0;
                        
                        // Recalculate next run time based on schedule
                        if (job.Schedule != null)
                        {
                            // Calculate the natural next run time from now
                            var now = DateTime.Now;
                            var scheduledTime = now.Date.Add(job.Schedule.Time);
                            
                            DateTime? nextRun = null;
                            switch (job.Schedule.Frequency)
                            {
                                case SecureServerBackupCommon.ScheduleFrequency.Daily:
                                    nextRun = scheduledTime > now ? scheduledTime : scheduledTime.AddDays(1);
                                    break;
                                    
                                case SecureServerBackupCommon.ScheduleFrequency.Weekly:
                                    var nextWeeklyRun = scheduledTime > now ? scheduledTime : scheduledTime.AddDays(1);
                                    while (!job.Schedule.DaysOfWeek.Contains(nextWeeklyRun.DayOfWeek))
                                    {
                                        nextWeeklyRun = nextWeeklyRun.AddDays(1);
                                    }
                                    nextRun = nextWeeklyRun;
                                    break;
                                    
                                case SecureServerBackupCommon.ScheduleFrequency.Monthly:
                                    var nextMonthlyRun = new DateTime(now.Year, now.Month, job.Schedule.DayOfMonth,
                                        job.Schedule.Time.Hours, job.Schedule.Time.Minutes, 0);
                                    if (nextMonthlyRun <= now)
                                        nextMonthlyRun = nextMonthlyRun.AddMonths(1);
                                    nextRun = nextMonthlyRun;
                                    break;
                            }
                            
                            job.NextScheduledRun = nextRun;
                            if (job.Schedule != null)
                            {
                                job.Schedule.NextRunTime = nextRun;
                            }
                        }
                        else
                        {
                            // No schedule, clear next run time
                            job.NextScheduledRun = null;
                        }
                        
                        jobManager.UpdateJob(job);

                        // Log the reset action
                        BackupLogger.LogInfo(job.Name, "Job state manually reset by user - IsCurrentlyRunning=false, ConsecutiveFailures=0, NextRunTime recalculated");

                        string nextRunMsg = job.NextScheduledRun.HasValue 
                            ? $"\nNext scheduled run: {job.NextScheduledRun.Value:yyyy-MM-dd HH:mm:ss}"
                            : "\nNo schedule configured.";

                        CustomDialogService.ShowInfo(this,
                            $"Job '{job.Name}' has been reset to scheduled state.\n\n" +
                            "State cleared:\n" +
                            "  • IsCurrentlyRunning = false\n" +
                            "  • ConsecutiveFailures = 0\n" +
                            "  • Next run time recalculated" +
                            nextRunMsg,
                            "Job State Reset");

                        // Refresh the job list to show updated status
                        LoadBackupJobs();
                    }
                    else
                    {
                        CustomDialogService.ShowError(this,
                            "Could not find the specified job.",
                            "Error");
                    }
                }
            }
        }

        private async void RunJobNow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is System.Guid jobId)
            {
                var job = jobManager.GetJob(jobId);
                if (job != null)
                {
                    // Check if BackupRestoreService is installed and running (async, non-blocking)
                    bool serviceOk = await CheckBackupServiceAsync();
                    if (!serviceOk)
                    {
                        return; // CheckBackupServiceAsync already showed error message
                    }

                    // Log the manual backup attempt immediately
                    BackupLogger.LogInfo(job.Name, $"User initiated manual backup (Run Now clicked)");

                    // Send job to service and show progress window (no confirmation needed)
                    var success = await backupServiceClient.RunBackupNowAsync(jobId);

                    if (success)
                    {
                        BackupLogger.LogInfo(job.Name, "Service accepted backup request - backup is starting");

                        ShowBackupProgressWindow(jobId, job.Name);
                    }
                    else
                    {
                        // Log the failure to Activity tab
                        BackupLogger.LogError(job.Name, "Failed to communicate with Secure Server Backup Service - backup was not started");

                        CustomDialogService.ShowError(this,
                            "Failed to start backup. The service may be busy or not responding.\n\n" +
                            "Try again in a few moments, or restart the Secure Server Backup Service from Windows Services.",
                            "Service Error");
                    }
                }
            }
        }

        private async Task<bool> CheckBackupServiceAsync()
        {
            try
            {
                // Check if service is installed
                if (!Services.ServiceInstaller.IsServiceInstalled())
                {
                    BackupLogger.LogServiceInfo("Secure Server Backup Service not installed - installing automatically...");

                    // Install and start service automatically without confirmation
                    var (success, message) = await Services.ServiceInstaller.InstallAndStartServiceAsync();

                    if (success)
                    {
                        BackupLogger.LogServiceInfo("Secure Server Backup Service installed and started successfully");
                        return true;
                    }
                    else
                    {
                        BackupLogger.LogServiceError($"Failed to install service: {message}");

                        // Show error on UI thread
                        _ = this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            CustomDialogService.ShowError(this,
                                $"Failed to install service:\n\n{message}\n\n" +
                                "Please ensure the application has Administrator privileges.",
                                "Installation Failed");
                        }), System.Windows.Threading.DispatcherPriority.Background);

                        return false;
                    }
                }

                // Service is installed - check if running
                var status = Services.ServiceInstaller.GetServiceStatus();
                if (status != System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    BackupLogger.LogServiceInfo($"Secure Server Backup Service not running (Status: {status}) - starting automatically...");

                    // Start service automatically without confirmation
                    var (success, message) = await Services.ServiceInstaller.StartServiceAsync();

                    if (success)
                    {
                        BackupLogger.LogServiceInfo("Secure Server Backup Service started successfully");
                        return true;
                    }
                    else
                    {
                        BackupLogger.LogServiceError($"Failed to start service: {message}");

                        // Show error on UI thread
                        _ = this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            CustomDialogService.ShowError(this,
                                $"Failed to start service:\n\n{message}",
                                "Start Failed");
                        }), System.Windows.Threading.DispatcherPriority.Background);

                        return false;
                    }
                }

                // Service is running
                return true;
            }
            catch (Exception ex)
            {
                BackupLogger.LogServiceError($"Error checking service status: {ex.Message}");

                // Show error on UI thread
                _ = this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    CustomDialogService.ShowError(this,
                        $"Error checking service status:\n\n{ex.Message}",
                        "Service Check Error");
                }), System.Windows.Threading.DispatcherPriority.Background);

                return false;
            }
        }

        [Obsolete("Use CheckBackupServiceAsync() instead to avoid UI blocking")]
        private bool CheckBackupService()
        {
            // This synchronous wrapper is deprecated - use CheckBackupServiceAsync() instead
            // Kept for backward compatibility but should not be used in new code
            return CheckBackupServiceAsync().GetAwaiter().GetResult();
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
                        var result = CustomDialogService.ShowQuestion(this,
                            $"Delete backup job '{job.Name}'?\n\n" +
                            $"Note: No backup files found at destination.",
                            "Delete Backup Job");

                        if (result == CustomDialogResult.Yes)
                        {
                            jobManager.DeleteJob(jobId);
                            BackupLogger.LogInfo(job.Name, "Backup job deleted (no backup files existed)");
                            CustomDialogService.ShowInfo(this,
                                $"Backup job '{job.Name}' has been deleted.",
                                "Job Deleted");
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

                        {
                            // Delete job only
                            jobManager.DeleteJob(jobId);
                            BackupLogger.LogInfo(job.Name, "Backup job deleted (files preserved)");
                            CustomDialogService.ShowInfo(this,
                                $"Backup job '{job.Name}' has been deleted.\nBackup files have been preserved.",
                                "Job Deleted");
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
                    CustomDialogService.ShowInfo(this,
                        $"Backup job '{job.Name}' has been deleted.\n\n" +
                        $"Backup files moved to Recycle Bin:\n{job.DestinationPath}\n\n" +
                        $"You can restore them from the Recycle Bin if needed.",
                        "Job and Backups Deleted");
                }
                else
                {
                    CustomDialogService.ShowInfo(this,
                        $"Backup job '{job.Name}' has been deleted.\n\n" +
                        $"No backup files were found to delete.",
                        "Job Deleted");
                }
            }
            catch (Exception ex)
            {
                BackupLogger.LogError(job.Name, "Failed to delete backup files", ex.Message);
                CustomDialogService.ShowError(this,
                    $"Error deleting backup files:\n{ex.Message}\n\nThe job has been removed, but backup files may still exist.",
                    "Error");
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
            WindowPositionManager.SetChildWindowPosition(window, this);
            if (window.ShowDialog() == true)
            {
                LoadBackupJobs();
            }
        }

        private void ImportBackup_Click(object sender, RoutedEventArgs e)
        {
            var window = new ImportBackupWindow();
            WindowPositionManager.SetChildWindowPosition(window, this);
            if (window.ShowDialog() == true)
            {
                LoadBackupJobs();
            }
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (mainTabControl != null)
            {
                mainTabControl.SelectedIndex = 4; // Restore tab
                LoadRestoreBackups();
            }
        }

        private void ManageSchedules_Click(object sender, RoutedEventArgs e)
        {
            var window = new ScheduleManagementWindow();
            WindowPositionManager.SetChildWindowPosition(window, this);
            window.ShowDialog();
        }

        private void ActivityManagement_Click(object sender, RoutedEventArgs e)
        {
            var window = new ActivityManagementWindow();
            WindowPositionManager.SetChildWindowPosition(window, this);
            window.ShowDialog();
        }

        private void OpenActivityManagement_Click(object sender, RoutedEventArgs e)
        {
            var window = new ActivityManagementWindow();
            WindowPositionManager.SetChildWindowPosition(window, this);
            window.ShowDialog();
        }

        private void ServiceManagement_Click(object sender, RoutedEventArgs e)
        {
            var window = new ServiceManagementWindow();
            WindowPositionManager.SetChildWindowPosition(window, this);
            window.ShowDialog();
        }

        private void RecoveryEnvironmentCreator_Click(object sender, RoutedEventArgs e)
        {
            var window = new RecoveryEnvironmentWindow();
            WindowPositionManager.SetChildWindowPosition(window, this);
            window.ShowDialog();
        }
        
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
            // CRITICAL: Only handle events from the TabControl itself, not from child controls!
            if (e.Source != sender)
            {
                System.Diagnostics.Debug.WriteLine("TabControl_SelectionChanged: Event from child control, ignoring");
                return;
            }

            if (sender is TabControl tabControl)
            {
                if (tabControl.SelectedIndex == 1) // Activity tab
                {
                    System.Diagnostics.Debug.WriteLine("TabControl_SelectionChanged: Activity tab selected, loading logs");
                    LoadJobLogsTab();
                    // Mark all errors as read when user views Activity tab
                    BackupLogger.MarkAllErrorsAsRead();
                    UpdateActivityTabWarning();
                }
                else if (tabControl.SelectedIndex == 2) // Mount Backups tab
                {
                    System.Diagnostics.Debug.WriteLine("TabControl_SelectionChanged: Mount Backups tab selected, refreshing");
                    LoadAvailableBackups();
                    LoadMountedBackups();
                }
                else if (tabControl.SelectedIndex == 3) // Verify tab
                {
                    System.Diagnostics.Debug.WriteLine("TabControl_SelectionChanged: Verify tab selected, refreshing");
                    LoadVerifyBackups();
                }
                else if (tabControl.SelectedIndex == 4) // Restore tab
                {
                    System.Diagnostics.Debug.WriteLine("TabControl_SelectionChanged: Restore tab selected, refreshing");
                    LoadRestoreBackups();
                }
            }
        }

        private void LoadRestoreBackups()
        {
            if (dgRestoreBackups == null)
                return;

            var backups = new List<AvailableBackupInfo>();

            try
            {
                var jobs = jobManager.GetAllJobs();

                foreach (var job in jobs)
                {
                    string destPath = job.DestinationPath;

                    if (!Directory.Exists(destPath))
                    {
                        continue;
                    }

                    var backupEntries = Directory.EnumerateFileSystemEntries(destPath, "*.ssb", SearchOption.AllDirectories);
                    foreach (var ssb in backupEntries)
                    {
                        DateTime backupDate = File.Exists(ssb)
                            ? new FileInfo(ssb).LastWriteTime
                            : Directory.GetLastWriteTime(ssb);

                        bool isEncrypted = File.Exists(ssb) && BackupEncryptionService.IsEncryptedBackupFile(ssb);

                        backups.Add(new AvailableBackupInfo
                        {
                            BackupName = job.Name,
                            BackupType = job.Type.ToString(),
                            BackupDate = backupDate,
                            BackupPath = ssb,
                            IsEncrypted = isEncrypted,
                            ProtectedEncryptionPassword = job.ProtectedEncryptionPassword
                        });
                    }
                }

                dgRestoreBackups.ItemsSource = backups;

                if (txtNoRestoreBackups != null)
                {
                    txtNoRestoreBackups.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }

                if (txtRestoreTabStatus != null)
                {
                    txtRestoreTabStatus.Text = backups.Count == 0
                        ? "No backups were found. Create a backup first, then return here to restore it."
                        : "Select a backup to open the restore workflow. Restoring the boot/system drive requires the Linux recovery environment.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading backups for restore: {ex.Message}");
                if (txtRestoreTabStatus != null)
                {
                    txtRestoreTabStatus.Text = $"Error loading restore backups: {ex.Message}";
                }
            }
        }

        private void RefreshRestoreBackups_Click(object sender, RoutedEventArgs e)
        {
            LoadRestoreBackups();
        }

        private void RestoreBackupFromTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AvailableBackupInfo backup)
            {
                CustomDialogService.ShowWarning(this, "Please select a valid backup to restore.", "Restore Error");
                return;
            }

            try
            {
                bool isBootRelatedBackup = IsBootRelatedBackup(backup);
                bool requireAlternateDestination = false;

                if (isBootRelatedBackup)
                {
                    var result = CustomDialogService.ShowQuestion(
                        this,
                        "This backup includes the currently booted disk/volume.\n\nDo you want to restore it to a non-boot disk/volume from Windows?",
                        "Boot Drive Restore Detected");

                    if (result == CustomDialogResult.No)
                    {
                        CustomDialogService.ShowWarning(
                            this,
                            "To restore the currently booted disk/volume in place, boot from the recovery disk and perform the restore from there.",
                            "Recovery Disk Required");
                        return;
                    }

                    if (result != CustomDialogResult.Yes)
                    {
                        return;
                    }

                    requireAlternateDestination = true;
                }

                var window = new RestoreWindowNew(backup, requireAlternateDestination);
                WindowPositionManager.SetChildWindowPosition(window, this);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError(this, $"Error opening restore workflow: {ex.Message}", "Restore Error");
            }
        }

        private bool IsBootRelatedBackup(AvailableBackupInfo backup)
        {
            var jobs = jobManager.GetAllJobs();
            var matchingJob = jobs.FirstOrDefault(j =>
                string.Equals(j.Name, backup.BackupName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(j.DestinationPath) &&
                backup.BackupPath.StartsWith(j.DestinationPath, StringComparison.OrdinalIgnoreCase));

            if (matchingJob == null)
            {
                return false;
            }

            if (matchingJob.Target == BackupTarget.Disk)
            {
                // Only flag as boot-related if the source disk is the currently booted disk.
                // A non-booted disk (e.g. secondary or dual-boot disk) is fully restorable from Windows.
                int bootDiskNumber = GetBootDiskNumber();
                if (bootDiskNumber < 0)
                {
                    // Could not determine boot disk - fail safe: allow restore without warning.
                    return false;
                }

                string bootDevicePath = $@"\\.\PHYSICALDRIVE{bootDiskNumber}";

                foreach (var sourcePath in matchingJob.SourcePaths)
                {
                    if (string.IsNullOrWhiteSpace(sourcePath))
                        continue;

                    string normalized = sourcePath.TrimEnd('\\');
                    if (string.Equals(normalized, bootDevicePath, StringComparison.OrdinalIgnoreCase))
                        return true;

                    // Support numeric disk index stored as plain integer string.
                    if (int.TryParse(normalized, out int diskIndex) && diskIndex == bootDiskNumber)
                        return true;
                }

                return false;
            }

            foreach (var sourcePath in matchingJob.SourcePaths)
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    continue;
                }

                bool isBootVolume = false;

                try
                {
                    if (sourcePath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ||
                        sourcePath.EndsWith(":", StringComparison.OrdinalIgnoreCase) ||
                        sourcePath.EndsWith(@":\", StringComparison.OrdinalIgnoreCase))
                    {
                        int result = BackupEngineInterop.IsBootVolume(sourcePath, out isBootVolume);
                        if (result == 0 && isBootVolume)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? string.Empty;
                        string sourceRoot = Path.GetPathRoot(sourcePath)?.TrimEnd('\\') ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(systemRoot) &&
                            string.Equals(systemRoot, sourceRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"IsBootRelatedBackup warning for '{sourcePath}': {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the disk index (e.g. 3 for PHYSICALDRIVE3) of the disk that contains the
        /// currently active boot partition, or -1 if it cannot be determined.
        /// </summary>
        private static int GetBootDiskNumber()
        {
            try
            {
                // Win32_DiskPartition.BootPartition flags the partition that holds the active OS.
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT DiskIndex FROM Win32_DiskPartition WHERE BootPartition = TRUE");

                foreach (System.Management.ManagementObject partition in searcher.Get())
                {
                    if (int.TryParse(partition["DiskIndex"]?.ToString(), out int diskIndex))
                    {
                        System.Diagnostics.Debug.WriteLine($"GetBootDiskNumber: boot disk is PHYSICALDRIVE{diskIndex}");
                        return diskIndex;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetBootDiskNumber warning: {ex.Message}");
            }

            return -1;
        }

        // NEW: Load job logs for Activity tab
        private void LoadJobLogsTab()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("LoadJobLogsTab: Starting...");
                
                var allLogs = BackupLogger.GetRecentLogs(10000);
                System.Diagnostics.Debug.WriteLine($"LoadJobLogsTab: Loaded {allLogs.Count} total log entries");
                
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

                System.Diagnostics.Debug.WriteLine($"LoadJobLogsTab: Created {jobGroups.Count} job summaries");

                if (dgJobLogs != null)
                {
                    dgJobLogs.ItemsSource = jobGroups;
                    System.Diagnostics.Debug.WriteLine($"LoadJobLogsTab: Set ItemsSource. IsEnabled={dgJobLogs.IsEnabled}, IsVisible={dgJobLogs.IsVisible}, IsHitTestVisible={dgJobLogs.IsHitTestVisible}");
                    System.Diagnostics.Debug.WriteLine($"LoadJobLogsTab: DataGrid has {dgJobLogs.Items.Count} items");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("LoadJobLogsTab: ERROR - dgJobLogs is NULL!");
                }
                
                if (txtJobLogsStatus != null)
                {
                    txtJobLogsStatus.Text = $"Found {jobGroups.Count} backup jobs with activity logs. Double-click to view details.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadJobLogsTab: ERROR - {ex.Message}");
                CustomDialogService.ShowError(this, $"Error loading job logs: {ex.Message}",
                    "Error");
            }
        }

        // NEW: Activity tab event handlers
        private void RefreshJobLogs_Click(object sender, RoutedEventArgs e)
        {
            LoadJobLogsTab();
        }

        private void dgJobLogs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("dgJobLogs_SelectionChanged: Event fired!");
            
            // CRITICAL: Stop event from bubbling up to TabControl which would reload the entire grid!
            e.Handled = true;
            
            if (dgJobLogs != null && dgJobLogs.SelectedItem is JobLogSummary summary)
            {
                selectedJobLog = summary;
                System.Diagnostics.Debug.WriteLine($"Job selected: {summary.JobName}");
            }
            else
            {
                selectedJobLog = null;
                System.Diagnostics.Debug.WriteLine("dgJobLogs_SelectionChanged: No item selected or not JobLogSummary");
            }
        }

        private void dgJobLogs_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            // CRITICAL: Cancel ALL edit attempts - we want selection only, not editing!
            System.Diagnostics.Debug.WriteLine("dgJobLogs_BeginningEdit: Canceling edit attempt");
            e.Cancel = true;
        }

        private void dgJobLogs_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // This event fires before any other mouse events - used for diagnostics
            System.Diagnostics.Debug.WriteLine($"PreviewMouseDown: Button={e.ChangedButton}, ClickCount={e.ClickCount}");
        }

        private void dgJobLogs_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Normal mouse down event - not needed since we have SelectionChanged
            System.Diagnostics.Debug.WriteLine($"MouseDown: Button={e.ChangedButton}");
        }

        private void dgJobLogs_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Left button specific event - not needed
            System.Diagnostics.Debug.WriteLine("PreviewMouseLeftButtonDown fired");
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
                CustomDialogService.ShowError(this, $"Error opening activity details: {ex.Message}",
                    "Error");
            }
        }

        private void ViewJobDetailsFromTab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is string jobName && !string.IsNullOrEmpty(jobName))
                {
                    System.Diagnostics.Debug.WriteLine($"ViewJobDetailsFromTab_Click: Opening detail window for job '{jobName}'");
                    var detailWindow = new ActivityDetailWindow(jobName);
                    detailWindow.ShowDialog();
                }
                else
                {
                    CustomDialogService.ShowWarning(this, "Unable to identify the job. Please try again.",
                        "Error");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in ViewJobDetailsFromTab_Click: {ex.Message}");
                CustomDialogService.ShowError(this, $"Error opening activity details: {ex.Message}",
                    "Error");
            }
        }

        private void JobLog_DoubleClickFromTab(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                // Find the DataGridRow that was actually clicked (not the selected item)
                var clickedElement = e.OriginalSource as DependencyObject;
                
                // Walk up the visual tree to find the DataGridRow
                while (clickedElement != null && !(clickedElement is DataGridRow))
                {
                    clickedElement = VisualTreeHelper.GetParent(clickedElement);
                }
                
                if (clickedElement is DataGridRow row && row.Item is JobLogSummary summary)
                {
                    if (!string.IsNullOrEmpty(summary.JobName))
                    {
                        System.Diagnostics.Debug.WriteLine($"Double-clicked row: Opening detail window for job '{summary.JobName}'");
                        var detailWindow = new ActivityDetailWindow(summary.JobName);
                        detailWindow.ShowDialog();
                    }
                    else
                    {
                        CustomDialogService.ShowError(this, "Job name is empty.", "Error");
                    }
                }
                else
                {
                    // Clicked outside data rows (header or empty space)
                    System.Diagnostics.Debug.WriteLine("Double-click was not on a data row");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in JobLog_DoubleClickFromTab: {ex.Message}");
                CustomDialogService.ShowError(this, $"Error opening activity details: {ex.Message}",
                    "Error");
            }
        }

        private void ExportJobLogFromTab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is string jobName && !string.IsNullOrEmpty(jobName))
                {
                    var allLogs = BackupLogger.GetRecentLogs(10000);
                    var jobLogs = allLogs.Where(l => l.JobName == jobName).ToList();

                    if (jobLogs.Count == 0)
                    {
                        CustomDialogService.ShowInfo(this, "No activities found for this job.", "No Data");
                        return;
                    }

                    var exportDialog = new ExportOptionsDialog { Owner = this };
                    if (exportDialog.ShowDialog() == true)
                    {
                        ExportActivitiesFromTab(jobLogs, exportDialog.ExportFormat, $"{jobName}_activities");
                    }
                }
                else
                {
                    CustomDialogService.ShowWarning(this, "Unable to identify the job. Please try again.", "Error");
                }
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError(this, $"Error exporting activity details: {ex.Message}", "Export Error");
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

            if (dialog.ShowDialog(this) == true)
            {
                if (format == "CSV")
                {
                    ExportActivitiesToCsvFromTab(logs, dialog.FileName);
                }
                else
                {
                    ExportActivitiesToTextFromTab(logs, dialog.FileName);
                }

                CustomDialogService.ShowSuccess(this, $"Successfully exported {logs.Count} activities to:\n{dialog.FileName}", "Export Complete");
            }
        }

        private static void ExportActivitiesToCsvFromTab(List<BackupLogEntry> logs, string filePath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Timestamp,Job Name,Level,Message,Details,Backup Path,Validation Passed");

            foreach (var log in logs.OrderBy(l => l.Timestamp))
            {
                csv.AppendLine($"\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                              $"\"{EscapeCsvForTab(log.JobName)}\"," +
                              $"\"{log.Level}\"," +
                              $"\"{EscapeCsvForTab(log.Message)}\"," +
                              $"\"{EscapeCsvForTab(log.Details)}\"," +
                              $"\"{EscapeCsvForTab(log.BackupPath)}\"," +
                              $"\"{log.ValidationPassed}\"");
            }

            File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        }

        private static void ExportActivitiesToTextFromTab(List<BackupLogEntry> logs, string filePath)
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
                text.AppendLine($"  Validation: {(log.ValidationPassed ? "PASSED" : "FAILED")}");
                text.AppendLine();
            }

            File.WriteAllText(filePath, text.ToString(), Encoding.UTF8);
        }

        private static string EscapeCsvForTab(string? value)
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

            bool hasUnreadErrors = BackupLogger.HasUnreadErrors();
            bool hasUnreadWarnings = BackupLogger.HasUnreadWarnings();

            if (hasUnreadErrors || hasUnreadWarnings)
            {
                // Create a StackPanel to hold text and icon
                var headerPanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal
                };

                // Add "Activity" text
                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = "Activity ",
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Set text color based on severity
                if (hasUnreadErrors)
                {
                    textBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(139, 0, 0)); // Dark Red
                }
                else // hasUnreadWarnings
                {
                    textBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 140, 0)); // Orange
                }

                headerPanel.Children.Add(textBlock);

                // Add appropriate SVG icon
                try
                {
                    var iconViewer = new SharpVectors.Converters.SvgViewbox
                    {
                        Width = 16,
                        Height = 16,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 0, 0)
                    };

                    // Use pack:// URI for embedded resources
                    Uri iconUri = new Uri(
                        hasUnreadErrors ? "pack://application:,,,/Images/error_icon.svg" : 
                                        "pack://application:,,,/Images/warning_icon.svg", 
                        UriKind.Absolute);

                    try
                    {
                        // Try to load the SVG resource
                        iconViewer.Source = iconUri;
                        headerPanel.Children.Add(iconViewer);
                    }
                    catch (Exception svgEx)
                    {
                        // Resource not found - fallback to emoji
                        System.Diagnostics.Debug.WriteLine($"SVG resource not found: {iconUri}, Error: {svgEx.Message}");
                        var fallbackIcon = new System.Windows.Controls.TextBlock
                        {
                            Text = hasUnreadErrors ? "⚠️" : "⚠️",
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(4, 0, 0, 0)
                        };
                        headerPanel.Children.Add(fallbackIcon);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading SVG icon: {ex.Message}");
                    // Fallback to emoji if SVG loading fails
                    var fallbackIcon = new System.Windows.Controls.TextBlock
                    {
                        Text = hasUnreadErrors ? "⚠️" : "⚠️",
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 0, 0)
                    };
                    headerPanel.Children.Add(fallbackIcon);
                }

                tabActivity.Header = headerPanel;
            }
            else
            {
                // No unread errors or warnings - show plain text
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

        private void BrowseBackup_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Backup File to Mount",
                Filter = "Silver State Backup Files (*.ssb)|*.ssb|All Files (*.*)|*.*",
                DefaultExt = ".ssb",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string selectedFile = openFileDialog.FileName;
                var fileInfo = new System.IO.FileInfo(selectedFile);

                // Add to available backups list
                var backups = dgAvailableBackups.ItemsSource as System.Collections.Generic.List<AvailableBackupInfo>;
                if (backups == null)
                {
                    backups = new System.Collections.Generic.List<AvailableBackupInfo>();
                }

                // Check if already in list
                bool exists = false;
                foreach (var b in backups)
                {
                    if (b.BackupPath.Equals(selectedFile, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    backups.Add(new AvailableBackupInfo
                    {
                        BackupName = System.IO.Path.GetFileNameWithoutExtension(selectedFile),
                        BackupType = GetBackupTypeFromFilename(System.IO.Path.GetFileNameWithoutExtension(selectedFile)),
                        BackupDate = fileInfo.LastWriteTime,
                        BackupPath = selectedFile,
                        IsEncrypted = BackupEncryptionService.IsEncryptedBackupFile(selectedFile)
                    });

                    dgAvailableBackups.ItemsSource = null; // Force refresh
                    dgAvailableBackups.ItemsSource = backups;

                    if (txtNoBackups != null)
                        txtNoBackups.Visibility = Visibility.Collapsed;

                    CustomDialogService.ShowSuccess(this, $"Backup file added: {System.IO.Path.GetFileName(selectedFile)}",
                                  "Backup Added");
                }
                else
                {
                    CustomDialogService.ShowInfo(this, "This backup is already in the list.",
                                  "Already Added");
                }
            }
        }

        private async void MountBackup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is AvailableBackupInfo backup)
            {
                try
                {
                    // Get selected backup point if Inc/Diff
                    string wimPath = GetBackupPointPath(backup);

                    if (string.IsNullOrEmpty(wimPath))
                    {
                        CustomDialogService.ShowWarning(this, "Please select a backup point to mount.",
                                      "No Backup Point Selected");
                        return;
                    }

                    // Check if backup has multiple images/restore points
                    System.Diagnostics.Debug.WriteLine($"[Mount] Checking image count for: {wimPath}");
                    var (countSuccess, imageCount, countError) = NativeBackupMountManager.GetImageCount(wimPath);

                    int selectedImageIndex = 1; // Default to first image

                    if (!countSuccess)
                    {
                        CustomDialogService.ShowError(this, $"Failed to check backup images:\n{countError}",
                                      "Error");
                        return;
                    }

                    // If backup has multiple images, show selection dialog
                    if (imageCount > 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mount] Backup has {imageCount} images - showing selection dialog");

                        // Get detailed image information
                        var (infoSuccess, images, infoError) = NativeBackupMountManager.GetImageInfo(wimPath);

                        if (!infoSuccess || images.Count == 0)
                        {
                            CustomDialogService.ShowError(this, $"Failed to get image details:\n{infoError}",
                                          "Error");
                            return;
                        }

                        // Show image selection dialog
                        var imageDialog = new SecureServerBackup.Windows.ImageSelectionDialog(images)
                        {
                            Owner = this
                        };

                        if (imageDialog.ShowDialog() != true)
                        {
                            System.Diagnostics.Debug.WriteLine("[Mount] User cancelled image selection");
                            return;
                        }

                        selectedImageIndex = imageDialog.SelectedImageIndex;
                        System.Diagnostics.Debug.WriteLine($"[Mount] User selected image index: {selectedImageIndex}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[Mount] Backup has {imageCount} image(s) - using first image");
                    }

                    // Show temp path selection dialog
                    var tempPathDialog = new SecureServerBackup.Windows.TempPathSelectionDialog
                    {
                        Owner = this
                    };

                    System.Diagnostics.Debug.WriteLine("[Mount] Showing TempPathSelectionDialog...");

                    if (tempPathDialog.ShowDialog() != true)
                    {
                        System.Diagnostics.Debug.WriteLine("[Mount] User cancelled temp path selection");
                        // User cancelled
                        return;
                    }

                    string selectedTempPath = tempPathDialog.SelectedTempPath;

                    // Diagnostic: Log selected temp path
                    System.Diagnostics.Debug.WriteLine($"[Mount] User selected temp path: '{selectedTempPath}'");
                    System.Diagnostics.Debug.WriteLine($"[Mount] Path length: {selectedTempPath?.Length ?? 0}");
                    System.Diagnostics.Debug.WriteLine($"[Mount] Path is null or empty: {string.IsNullOrEmpty(selectedTempPath)}");
                    System.Diagnostics.Debug.WriteLine($"[Mount] About to create progress window...");

                    // Create and show progress window
                    var progressWindow = new SecureServerBackup.Windows.MountProgressWindow
                    {
                        Owner = this
                    };

                    System.Diagnostics.Debug.WriteLine($"[Mount] Progress window created, setting backup name: {backup.BackupName}");
                    progressWindow.SetBackupName(backup.BackupName);

                    System.Diagnostics.Debug.WriteLine($"[Mount] Showing progress window...");
                    progressWindow.Show();

                    System.Diagnostics.Debug.WriteLine($"[Mount] Progress window shown, about to call MountBackupAsync...");

                    try
                    {
                        // Mount asynchronously with progress updates
                        System.Diagnostics.Debug.WriteLine($"[Mount] Calling NativeBackupMountManager.MountBackupAsync...");
                        System.Diagnostics.Debug.WriteLine($"[Mount] Parameters: wimPath={wimPath}, backupName={backup.BackupName}, backupType={backup.BackupType}, imageIndex={selectedImageIndex}, tempPath={selectedTempPath}");

                        using var preparedBackup = EncryptedBackupFileService.PrepareForRead(
                            this,
                            wimPath,
                            backup.BackupName,
                            backup.ProtectedEncryptionPassword);

                        var (success, mountPath, error) = await NativeBackupMountManager.MountBackupAsync(
                            preparedBackup.WorkingPath,
                            backup.BackupName,
                            backup.BackupType,
                            selectedImageIndex,
                            (percentage, message) =>
                            {
                                progressWindow.SetStatus(message, percentage);
                            },
                            selectedTempPath);

                        // Close progress window
                        progressWindow.CloseProgress();

                        if (success)
                        {
                            CustomDialogService.ShowSuccess(this, $"Backup mounted successfully!\n\n" +
                                          $"Mount Path: {mountPath}\n\n" +
                                          $"You can now browse the backup in Windows Explorer.\n" +
                                          $"Backup is READ-ONLY to prevent modifications.",
                                          "Backup Mounted");

                            LoadMountedBackups();
                            OpenExplorer(mountPath);
                        }
                        else
                        {
                            CustomDialogService.ShowError(this, $"Failed to mount backup:\n{error}",
                                          "Mount Error");
                        }
                    }
                    catch (Exception ex)
                    {
                        progressWindow.CloseProgress();
                        CustomDialogService.ShowError(this, $"Error mounting backup:\n{ex.Message}",
                                      "Error");
                    }
                }
                catch (Exception ex)
                {
                    CustomDialogService.ShowError(this, $"Error initializing mount:\n{ex.Message}",
                                  "Error");
                }
            }
        }

        private async void UnmountBackup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string mountPath)
            {
                var result = CustomDialogService.ShowQuestion(this,
                    $"Unmount backup from {mountPath}?\n\nIMPORTANT: Please close all Windows Explorer windows that are browsing mounted files before proceeding.",
                    "Unmount Backup");

                if (result == CustomDialogResult.Yes)
                {
                    // Create and show progress window
                    var progressWindow = new SecureServerBackup.Windows.MountProgressWindow
                    {
                        Owner = this,
                        Title = "Unmounting Backup"
                    };

                    progressWindow.SetBackupName("Unmounting...");
                    progressWindow.Show();

                    try
                    {
                        // Unmount asynchronously with progress updates
                        var (success, error) = await NativeBackupMountManager.UnmountBackupAsync(
                            mountPath,
                            (percentage, message) =>
                            {
                                progressWindow.SetStatus(message, percentage);
                            });

                        // Close progress window
                        progressWindow.CloseProgress();

                        if (success)
                        {
                            LoadMountedBackups();
                            CustomDialogService.ShowSuccess($"Backup unmounted successfully from {mountPath}",
                                          "Success");
                        }
                        else
                        {
                            CustomDialogService.ShowError($"Failed to unmount:\n{error}",
                                          "Unmount Error");
                        }
                    }
                    catch (Exception ex)
                    {
                        progressWindow.CloseProgress();
                        CustomDialogService.ShowError($"Error unmounting backup:\n{ex.Message}",
                                      "Error");
                    }
                }
            }
        }

        private void UnmountAll_Click(object sender, RoutedEventArgs e)
        {
            var mounted = NativeBackupMountManager.GetMountedBackups();

            if (mounted.Count == 0)
            {
                CustomDialogService.ShowInfo(this, "No mounted backups to unmount.",
                              "No Mounted Backups");
                return;
            }

            var result = CustomDialogService.ShowQuestion(this,
                $"Unmount all {mounted.Count} mounted backup(s)?\n\nIMPORTANT: Please close all Windows Explorer windows that are browsing mounted files before proceeding.",
                "Unmount All");

            if (result == CustomDialogResult.Yes)
            {
                NativeBackupMountManager.UnmountAll();
                LoadMountedBackups();
                CustomDialogService.ShowSuccess(this, "All backups unmounted successfully.",
                              "Success");
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
                // Scan backup directories for .ssb (WIM) files
                var jobs = jobManager.GetAllJobs();

                foreach (var job in jobs)
                {
                    string destPath = job.DestinationPath;

                    if (System.IO.Directory.Exists(destPath))
                    {
                        // Find .ssb (WIM backup) files
                        var ssbFiles = System.IO.Directory.GetFiles(destPath, "*.ssb", System.IO.SearchOption.AllDirectories);

                        foreach (var ssb in ssbFiles)
                        {
                            var fileInfo = new System.IO.FileInfo(ssb);

                            backups.Add(new AvailableBackupInfo
                            {
                                BackupName = job.Name,
                                BackupType = job.Type.ToString(),
                                BackupDate = fileInfo.LastWriteTime,
                                BackupPath = ssb,
                                IsEncrypted = BackupEncryptionService.IsEncryptedBackupFile(ssb),
                                ProtectedEncryptionPassword = job.ProtectedEncryptionPassword
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

            var mounted = NativeBackupMountManager.GetMountedBackups();
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

        private string GetBackupTypeFromFilename(string filenameWithoutExt)
        {
            string lower = filenameWithoutExt.ToLower();

            if (lower.Contains("full"))
                return "Full";
            else if (lower.Contains("incremental") || lower.Contains("incr"))
                return "Incremental";
            else if (lower.Contains("differential") || lower.Contains("diff"))
                return "Differential";
            else
                return "Full"; // Default for job name only (WDrive1.ssb = full backup)
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

        #region Backup Verification Tab Methods

        private void LoadVerifyBackups()
        {
            if (dgVerifyBackups == null)
                return;

            var backups = new System.Collections.Generic.List<AvailableBackupInfo>();

            try
            {
                // Scan backup directories for .ssb (WIM) files
                var jobs = jobManager.GetAllJobs();

                foreach (var job in jobs)
                {
                    string destPath = job.DestinationPath;

                    if (System.IO.Directory.Exists(destPath))
                    {
                        // Find .ssb (WIM backup) files
                        var backupEntries = System.IO.Directory.EnumerateFileSystemEntries(destPath, "*.ssb", System.IO.SearchOption.AllDirectories);

                        foreach (var ssb in backupEntries)
                        {
                            DateTime backupDate = System.IO.File.Exists(ssb)
                                ? new System.IO.FileInfo(ssb).LastWriteTime
                                : System.IO.Directory.GetLastWriteTime(ssb);

                            backups.Add(new AvailableBackupInfo
                            {
                                BackupName = job.Name,
                                BackupType = job.Type.ToString(),
                                BackupDate = backupDate,
                                BackupPath = ssb
                            });
                        }
                    }
                }

                dgVerifyBackups.ItemsSource = backups;

                if (txtNoVerifyBackups != null)
                    txtNoVerifyBackups.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading backups for verification: {ex.Message}");
                if (txtVerificationResults != null)
                {
                    txtVerificationResults.Text = $"Error loading backups: {ex.Message}";
                    txtVerificationResults.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
        }

        private void RefreshVerifyBackups_Click(object sender, RoutedEventArgs e)
        {
            LoadVerifyBackups();
            if (txtVerificationResults != null)
            {
                txtVerificationResults.Text = "Click Verify on a backup to check its integrity...";
                txtVerificationResults.Foreground = new SolidColorBrush(Colors.Black);
            }
        }

        private void BrowseVerifyBackup_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Backup File to Verify",
                Filter = "Silver State Backup Files (*.ssb)|*.ssb|All Files (*.*)|*.*",
                DefaultExt = ".ssb",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string selectedFile = openFileDialog.FileName;
                var fileInfo = new System.IO.FileInfo(selectedFile);

                // Add to verification backups list
                var backups = dgVerifyBackups.ItemsSource as System.Collections.Generic.List<AvailableBackupInfo>;
                if (backups == null)
                {
                    backups = new System.Collections.Generic.List<AvailableBackupInfo>();
                }

                // Check if already in list
                bool exists = false;
                foreach (var b in backups)
                {
                    if (b.BackupPath.Equals(selectedFile, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    backups.Add(new AvailableBackupInfo
                    {
                        BackupName = System.IO.Path.GetFileNameWithoutExtension(selectedFile),
                        BackupType = GetBackupTypeFromFilename(System.IO.Path.GetFileNameWithoutExtension(selectedFile)),
                        BackupDate = fileInfo.LastWriteTime,
                        BackupPath = selectedFile,
                        IsEncrypted = BackupEncryptionService.IsEncryptedBackupFile(selectedFile)
                    });

                    dgVerifyBackups.ItemsSource = null; // Force refresh
                    dgVerifyBackups.ItemsSource = backups;

                    if (txtNoVerifyBackups != null)
                        txtNoVerifyBackups.Visibility = Visibility.Collapsed;

                    CustomDialogService.ShowSuccess($"Backup file added for verification: {System.IO.Path.GetFileName(selectedFile)}",
                              "Backup Added");
                }
                else
                {
                    CustomDialogService.ShowInfo("This backup is already in the list.",
                              "Already Added");
                }
            }
        }

        private async void VerifyBackup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is AvailableBackupInfo backup)
            {
                try
                {
                    if (txtVerificationResults != null)
                    {
                        txtVerificationResults.Text = $"Verifying backup: {backup.BackupName}\nPath: {backup.BackupPath}\n\nStarting verification process...";
                        txtVerificationResults.Foreground = new SolidColorBrush(Colors.Blue);
                    }

                    // Determine which image to verify; for multi-volume backups, ask the user.
                    int selectedImageIndex = 1;
                    var (countSuccess, imageCount, countError) = NativeBackupMountManager.GetImageCount(backup.BackupPath);
                    if (!countSuccess)
                    {
                        CustomDialogService.ShowError(this, $"Failed to read backup images:\n{countError}", "Error");
                        return;
                    }

                    if (imageCount > 1)
                    {
                        var (infoSuccess, images, infoError) = NativeBackupMountManager.GetImageInfo(backup.BackupPath);
                        if (!infoSuccess || images.Count == 0)
                        {
                            CustomDialogService.ShowError(this, $"Failed to get image details:\n{infoError}", "Error");
                            return;
                        }

                        var imageDialog = new SecureServerBackup.Windows.ImageSelectionDialog(
                            images,
                            "Verify Selected",
                            "This backup contains multiple restore points. Select which point to verify:")
                        {
                            Owner = this
                        };

                        if (imageDialog.ShowDialog() != true)
                        {
                            return;
                        }

                        selectedImageIndex = imageDialog.SelectedImageIndex;
                    }

                    var progressWindow = new MountProgressWindow
                    {
                        Owner = this,
                        Title = $"Verify Backup - {backup.BackupName}",
                        ShowInTaskbar = false,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    progressWindow.SetBackupName($"Verifying {backup.BackupName}");
                    progressWindow.SetStatus("Preparing verification...", -1);
                    progressWindow.Show();

                    var verificationResult = new StringBuilder();
                    var verifyJobName = $"{backup.BackupName} [Verify]";
                    verificationResult.AppendLine($"Verification started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    verificationResult.AppendLine($"Backup: {backup.BackupName}");
                    verificationResult.AppendLine($"Type: {backup.BackupType}");
                    verificationResult.AppendLine($"Path: {backup.BackupPath}");
                    if (imageCount > 1)
                    {
                        verificationResult.AppendLine($"Image: {selectedImageIndex} of {imageCount}");
                    }
                    verificationResult.AppendLine(new string('-', 60));

                    BackupLogger.LogInfo(verifyJobName,
                        $"Verification started for {backup.BackupName}",
                        $"Path: {backup.BackupPath}" + (imageCount > 1 ? $" | Image {selectedImageIndex} of {imageCount}" : string.Empty));

                    void UpdateVerificationLog(string message)
                    {
                        verificationResult.AppendLine(message);
                        BackupLogger.LogInfo(verifyJobName, message);

                        if (txtVerificationResults != null)
                        {
                            txtVerificationResults.Text = verificationResult.ToString();
                        }
                    }

                    try
                    {
                        var repairChoice = CustomDialogResult.No;

                        int verificationResultCode = await Task.Run(() =>
                        {
                            using var preparedBackup = EncryptedBackupFileService.PrepareForRead(
                                this,
                                backup.BackupPath,
                                backup.BackupName,
                                backup.ProtectedEncryptionPassword);

                            var healthMessage = new StringBuilder(1024);

                            int checkResult = BackupEngineInterop.CheckBackupImageStatusWithProgress(
                                preparedBackup.WorkingPath,
                                selectedImageIndex,
                                true,
                                healthMessage,
                                healthMessage.Capacity,
                                (percentage, message) =>
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        progressWindow.SetStatus(message, percentage);
                                        verificationResult.AppendLine($"[{percentage}%] {message}");
                                        if (txtVerificationResults != null)
                                        {
                                            txtVerificationResults.Text = verificationResult.ToString();
                                        }
                                    });
                                });

                            Dispatcher.Invoke(() =>
                            {
                                progressWindow.SetStatus(healthMessage.ToString(), checkResult == 0 ? 100 : -1);
                                UpdateVerificationLog($"SSB health check result: {checkResult}");
                                UpdateVerificationLog($"Health details: {healthMessage}");
                            });

                            if (checkResult == 0)
                            {
                                return 0;
                            }

                            Dispatcher.Invoke(() => progressWindow.SetStatus("Verification failed.", -1));
                            return checkResult;
                        });

                        if (verificationResultCode != 0)
                        {
                            repairChoice = CustomDialogService.ShowQuestion(
                                this,
                                "Verification failed. Attempt to repair corrupted backup file?",
                                "Repair Backup File");

                            if (repairChoice == CustomDialogResult.Yes)
                            {
                                progressWindow.SetStatus("Attempting repair of corrupted backup file...", -1);

                                int repairResultCode = await Task.Run(() =>
                                {
                                    using var preparedBackup = EncryptedBackupFileService.PrepareForRead(
                                        this,
                                        backup.BackupPath,
                                        backup.BackupName,
                                        backup.ProtectedEncryptionPassword);

                                    var repairMessage = new StringBuilder(1024);

                                    int repairResult = BackupEngineInterop.RepairBackupImageStatusWithProgress(
                                        preparedBackup.WorkingPath,
                                        selectedImageIndex,
                                        null,
                                        0,
                                        false,
                                        repairMessage,
                                        repairMessage.Capacity,
                                        (percentage, message) =>
                                        {
                                            Dispatcher.Invoke(() =>
                                            {
                                                progressWindow.SetStatus(message, percentage);
                                                verificationResult.AppendLine($"[{percentage}%] {message}");
                                                if (txtVerificationResults != null)
                                                {
                                                    txtVerificationResults.Text = verificationResult.ToString();
                                                }
                                            });
                                        });

                                    Dispatcher.Invoke(() =>
                                    {
                                        UpdateVerificationLog($"SSB repair result: {repairResult}");
                                        UpdateVerificationLog($"Repair details: {repairMessage}");
                                        progressWindow.SetStatus(
                                            repairResult == 0 ? "Repair completed successfully." : "Repair failed.",
                                            repairResult == 0 ? 100 : -1);
                                    });

                                    return repairResult;
                                });

                                verificationResultCode = repairResultCode;
                            }

                            if (repairChoice != CustomDialogResult.Yes)
                            {
                                UpdateVerificationLog("Repair declined by user.");
                            }
                        }

                        verificationResult.AppendLine(new string('-', 60));

                        if (verificationResultCode == 0)
                        {
                            verificationResult.AppendLine($"Verification completed successfully at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                            verificationResult.AppendLine("RESULT: BACKUP IS VALID");

                            if (txtVerificationResults != null)
                            {
                                txtVerificationResults.Text = verificationResult.ToString();
                                txtVerificationResults.Foreground = new SolidColorBrush(Colors.Green);
                            }

                            BackupLogger.LogSuccess(verifyJobName,
                                $"Verification passed for {backup.BackupName}",
                                backup.BackupPath);

                            CustomDialogService.ShowSuccess(this,
                                $"Backup verification completed successfully!\n\nBackup: {backup.BackupName}\n\nThe backup integrity has been verified and is valid.",
                                "Verification Complete");
                        }
                        else
                        {
                            verificationResult.AppendLine($"Verification completed with errors at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                            verificationResult.AppendLine($"RESULT: BACKUP FAILED VERIFICATION (CODE {verificationResultCode})");

                            if (txtVerificationResults != null)
                            {
                                txtVerificationResults.Text = verificationResult.ToString();
                                txtVerificationResults.Foreground = new SolidColorBrush(Colors.Red);
                            }

                            BackupLogger.LogError(verifyJobName,
                                $"Verification failed for {backup.BackupName} (code {verificationResultCode})",
                                backup.BackupPath);

                            CustomDialogService.ShowError(this,
                                $"Backup verification failed!\n\nBackup: {backup.BackupName}\n\nThe backup may be corrupted or inaccessible.",
                                "Verification Failed");
                        }
                    }
                    catch (Exception verifyEx)
                    {
                        verificationResult.AppendLine(new string('-', 60));
                        verificationResult.AppendLine($"ERROR: {verifyEx.Message}");
                        verificationResult.AppendLine($"Verification failed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                        if (txtVerificationResults != null)
                        {
                            txtVerificationResults.Text = verificationResult.ToString();
                            txtVerificationResults.Foreground = new SolidColorBrush(Colors.Red);
                        }

                        BackupLogger.LogError(verifyJobName,
                            $"Verification exception for {backup.BackupName}: {verifyEx.Message}",
                            verifyEx.ToString());

                        CustomDialogService.ShowError(this,
                            $"Backup verification failed:\n{verifyEx.Message}",
                            "Verification Error");
                    }
                    finally
                    {
                        progressWindow.CloseProgress();
                    }
                }
                catch (Exception ex)
                {
                    if (txtVerificationResults != null)
                    {
                        txtVerificationResults.Text = $"Error initializing verification:\n{ex.Message}";
                        txtVerificationResults.Foreground = new SolidColorBrush(Colors.Red);
                    }

                    BackupLogger.LogError($"{backup.BackupName} [Verify]",
                        $"Failed to start verification for {backup.BackupName}: {ex.Message}",
                        ex.ToString());

                    CustomDialogService.ShowError($"Error initializing verification:\n{ex.Message}",
                                  "Error");
                }
            }
        }

        #endregion
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

            // Source description - CHECK DISK/VOLUME FIRST before FilesAndFolders
            // This prevents disk paths from being misidentified as file paths
            if (job.IsHyperVBackup && job.HyperVMachines.Count > 0)
            {
                SourceDescription = $"Hyper-V: {string.Join(", ", job.HyperVMachines)}";
            }
            else if (job.Target == BackupTarget.Disk)
            {
                // Disk backup - show device paths
                SourceDescription = $"Disk: {string.Join(", ", job.SourcePaths)}";
            }
            else if (job.Target == BackupTarget.Volume)
            {
                // Volume backup - show volume paths
                SourceDescription = $"Volume: {string.Join(", ", job.SourcePaths)}";
            }
            else if (job.Target == BackupTarget.FilesAndFolders)
            {
                // Files & Folders backup - extract drive letters and add suffix
                var volumeLetters = job.SourcePaths
                    .Select(p => System.IO.Path.GetPathRoot(p)?.TrimEnd('\\'))
                    .Distinct()
                    .Where(v => !string.IsNullOrEmpty(v));

                SourceDescription = $"{string.Join(", ", volumeLetters)} - Files & Folders";
            }
            else
            {
                // Unknown/fallback - just show paths
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

            // Execution status - show NextScheduledRun and IsCurrentlyRunning
            if (job.NextScheduledRun.HasValue)
            {
                NextScheduledRun = job.NextScheduledRun.Value.ToString("MM/dd/yyyy hh:mm tt");
            }
            else
            {
                NextScheduledRun = "Not scheduled";
            }

            IsCurrentlyRunning = job.IsCurrentlyRunning ? "✓ Running" : "○ Idle";
            IsRunning = job.IsCurrentlyRunning; // Store boolean for button visibility
        }

        public System.Guid Id { get; set; }
        public string Name { get; set; }
        public string TypeDescription { get; set; }
        public string SourceDescription { get; set; }
        public string DestinationPath { get; set; }
        public string ScheduleDescription { get; set; }
        public string NextScheduledRun { get; set; } = string.Empty;
        public string IsCurrentlyRunning { get; set; } = string.Empty;
        public bool IsRunning { get; set; } // Boolean for conditional visibility
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

