using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BackupUI.Models;
using BackupUI.Services;

namespace BackupUI.Windows
{
    public partial class ScheduleManagementWindow : Window
    {
        private readonly JobManager jobManager = new();

        public ScheduleManagementWindow()
        {
            InitializeComponent();
            LoadJobs();
        }

        private void LoadJobs()
        {
            var jobs = jobManager.GetScheduledJobs();
            dgJobs.ItemsSource = jobs;
        }

        private void EditJob_Click(object sender, RoutedEventArgs e)
        {
            if (dgJobs.SelectedItem is BackupJob job)
            {
                var window = new BackupWindowNew(job);
                if (window.ShowDialog() == true)
                {
                    LoadJobs();
                }
            }
            else
            {
                MessageBox.Show("Please select a job to edit.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteJob_Click(object sender, RoutedEventArgs e)
        {
            if (dgJobs.SelectedItem is BackupJob job)
            {
                var result = MessageBox.Show($"Are you sure you want to delete the job '{job.Name}'?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    jobManager.DeleteJob(job.Id);
                    LoadJobs();
                    MessageBox.Show("Job deleted successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("Please select a job to delete.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void RunNow_Click(object sender, RoutedEventArgs e)
        {
            if (dgJobs.SelectedItem is BackupJob job)
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
                    BackupLogger.LogInfo(job.Name, $"User initiated manual backup from Schedule Management (Run Now clicked)");
                    
                    // Send job to service and show progress window
                    var serviceClient = new BackupServiceClient();
                    var success = await serviceClient.RunBackupNowAsync(job.Id);

                    if (success)
                    {
                        BackupLogger.LogInfo(job.Name, "Service accepted backup request - backup is starting");
                        
                        // Show non-modal progress window
                        var progressWindow = new BackupProgressWindow(job.Id, job.Name);
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
            else
            {
                MessageBox.Show("Please select a job to run.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
