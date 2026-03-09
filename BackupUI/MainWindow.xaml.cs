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
using BackupUI.Models;
using BackupUI.Services;
using BackupUI.Windows;

namespace BackupUI
{
    public partial class MainWindow : Window
    {
        private readonly JobManager jobManager = new();
        private ObservableCollection<BackupJobViewModel> backupJobs = new();
        private JobLogSummary? selectedJobLog = null;  // Track selected job in Activity tab

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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore saved window position
            WindowPositionManager.RestoreMainWindowPosition(this);
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
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
                    var serviceClient = new Services.BackupServiceClient();
                    var success = await serviceClient.RunBackupNowAsync(jobId);

                    if (success)
                    {
                        BackupLogger.LogInfo(job.Name, "Service accepted backup request - backup is starting");

                        // Show non-modal progress window
                        var progressWindow = new Windows.BackupProgressWindow(jobId, job.Name);
                        WindowPositionManager.SetChildWindowPosition(progressWindow, this);
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

        private async Task<bool> CheckBackupServiceAsync()
        {
            try
            {
                // Check if service is installed
                if (!Services.ServiceInstaller.IsServiceInstalled())
                {
                    BackupLogger.LogServiceInfo("BackupRestoreService not installed - installing automatically...");

                    // Install and start service automatically without confirmation
                    var (success, message) = await Services.ServiceInstaller.InstallAndStartServiceAsync();

                    if (success)
                    {
                        BackupLogger.LogServiceInfo("BackupRestoreService installed and started successfully");
                        return true;
                    }
                    else
                    {
                        BackupLogger.LogServiceError($"Failed to install service: {message}");

                        // Show error on UI thread
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show(
                                $"Failed to install service:\n\n{message}\n\n" +
                                "Please ensure the application has Administrator privileges.",
                                "Installation Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }), System.Windows.Threading.DispatcherPriority.Background);

                        return false;
                    }
                }

                // Service is installed - check if running
                var status = Services.ServiceInstaller.GetServiceStatus();
                if (status != System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    BackupLogger.LogServiceInfo($"BackupRestoreService not running (Status: {status}) - starting automatically...");

                    // Start service automatically without confirmation
                    var (success, message) = await Services.ServiceInstaller.StartServiceAsync();

                    if (success)
                    {
                        BackupLogger.LogServiceInfo("BackupRestoreService started successfully");
                        return true;
                    }
                    else
                    {
                        BackupLogger.LogServiceError($"Failed to start service: {message}");

                        // Show error on UI thread
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show(
                                $"Failed to start service:\n\n{message}",
                                "Start Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
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
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    MessageBox.Show(
                        $"Error checking service status:\n\n{ex.Message}",
                        "Service Check Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
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
            var window = new RestoreWindowNew();
            WindowPositionManager.SetChildWindowPosition(window, this);
            window.ShowDialog();
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
            }
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
                MessageBox.Show($"Error loading job logs: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Error opening activity details: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show("Unable to identify the job. Please try again.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in ViewJobDetailsFromTab_Click: {ex.Message}");
                MessageBox.Show($"Error opening activity details: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        MessageBox.Show("Job name is empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show($"Error opening activity details: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Export functionality removed from job summary - users should open ActivityDetailWindow first
        // Export is available in ActivityDetailWindow with full multi-select functionality

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

                    // Get the path to the Images folder
                    string baseDir = System.IO.Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

                    string iconFileName = hasUnreadErrors ? "error_icon.svg" : "warning_icon.svg";
                    string iconPath = System.IO.Path.Combine(baseDir, "Images", iconFileName);

                    if (System.IO.File.Exists(iconPath))
                    {
                        iconViewer.Source = new Uri(iconPath, UriKind.Absolute);
                        headerPanel.Children.Add(iconViewer);
                    }
                    else
                    {
                        // Fallback to emoji if SVG file not found
                        System.Diagnostics.Debug.WriteLine($"SVG icon not found: {iconPath}");
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
                        BackupPath = selectedFile
                    });

                    dgAvailableBackups.ItemsSource = null; // Force refresh
                    dgAvailableBackups.ItemsSource = backups;

                    if (txtNoBackups != null)
                        txtNoBackups.Visibility = Visibility.Collapsed;

                    MessageBox.Show($"Backup file added: {System.IO.Path.GetFileName(selectedFile)}",
                                  "Backup Added",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("This backup is already in the list.",
                                  "Already Added",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
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
                        MessageBox.Show("Please select a backup point to mount.",
                                      "No Backup Point Selected",
                                      MessageBoxButton.OK,
                                      MessageBoxImage.Warning);
                        return;
                    }

                    // Show temp path selection dialog
                    var tempPathDialog = new BackupUI.Windows.TempPathSelectionDialog
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
                    var progressWindow = new BackupUI.Windows.MountProgressWindow
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
                        System.Diagnostics.Debug.WriteLine($"[Mount] Parameters: wimPath={wimPath}, backupName={backup.BackupName}, backupType={backup.BackupType}, imageIndex=1, tempPath={selectedTempPath}");

                        var (success, mountPath, error) = await NativeBackupMountManager.MountBackupAsync(
                            wimPath,
                            backup.BackupName,
                            backup.BackupType,
                            1,  // Image index
                            (percentage, message) =>  // Updated to receive percentage and message
                            {
                                progressWindow.SetStatus(message, percentage);
                            },
                            selectedTempPath);  // Pass user-selected temp path

                        // Close progress window
                        progressWindow.CloseProgress();

                        if (success)
                        {
                            MessageBox.Show($"Backup mounted successfully!\n\n" +
                                          $"Mount Path: {mountPath}\n\n" +
                                          $"You can now browse the backup in Windows Explorer.\n" +
                                          $"Backup is READ-ONLY to prevent modifications.",
                                          "Backup Mounted",
                                          MessageBoxButton.OK,
                                          MessageBoxImage.Information);

                            LoadMountedBackups();
                            OpenExplorer(mountPath);
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
                        progressWindow.CloseProgress();
                        MessageBox.Show($"Error mounting backup:\n{ex.Message}",
                                      "Error",
                                      MessageBoxButton.OK,
                                      MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error initializing mount:\n{ex.Message}",
                                  "Error",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Error);
                }
            }
        }

        private async void UnmountBackup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string mountPath)
            {
                var result = MessageBox.Show(
                    $"Unmount backup from {mountPath}?",
                    "Unmount Backup",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Create and show progress window
                    var progressWindow = new BackupUI.Windows.MountProgressWindow
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
                            MessageBox.Show($"Backup unmounted successfully from {mountPath}",
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
                    catch (Exception ex)
                    {
                        progressWindow.CloseProgress();
                        MessageBox.Show($"Error unmounting backup:\n{ex.Message}",
                                      "Error",
                                      MessageBoxButton.OK,
                                      MessageBoxImage.Error);
                    }
                }
            }
        }

        private void UnmountAll_Click(object sender, RoutedEventArgs e)
        {
            var mounted = NativeBackupMountManager.GetMountedBackups();

            if (mounted.Count == 0)
            {
                MessageBox.Show("No mounted backups to unmount.",
                              "No Mounted Backups",
                              MessageBoxButton.OK,
                              MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Unmount all {mounted.Count} mounted backup(s)?",
                "Unmount All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                NativeBackupMountManager.UnmountAll();
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
                                BackupType = GetBackupTypeFromFilename(System.IO.Path.GetFileNameWithoutExtension(ssb)),
                                BackupDate = fileInfo.LastWriteTime,
                                BackupPath = ssb
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

