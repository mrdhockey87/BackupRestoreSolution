using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SecureServerBackupCommon;
using SecureServerBackup.Models;
using SecureServerBackup.Services;

namespace SecureServerBackup.Windows
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

        private void EditNextRun_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not BackupJob job)
            {
                CustomDialogService.ShowWarning(this, "Please select a valid scheduled job.", "No Selection");
                return;
            }

            if (job.Schedule == null || !job.Schedule.Enabled || !job.NextScheduledRun.HasValue)
            {
                CustomDialogService.ShowWarning(this, "This job does not have an editable next run time.", "Edit Next Run");
                return;
            }

            var currentNextRun = job.NextScheduledRun.Value;
            var latestAllowedRun = GetLatestAllowedNextRun(job.Schedule, currentNextRun);

            if (!latestAllowedRun.HasValue || latestAllowedRun.Value < currentNextRun)
            {
                CustomDialogService.ShowWarning(this, "Unable to determine the valid edit range for this schedule.", "Edit Next Run");
                return;
            }

            CustomDialogService.ShowInfo(
                this,
                $"You can edit the next run for '{job.Name}'.\n\nThis is a one time change only. After this edited next run is used, the job will return to its normal schedule.\n\nCurrent next run: {currentNextRun:yyyy-MM-dd hh:mm tt}\nLatest allowed value: {latestAllowedRun.Value:yyyy-MM-dd hh:mm tt}",
                "Edit Next Run");

            var editWindow = new NextRunTimeEditWindow(job, currentNextRun, latestAllowedRun.Value)
            {
                Owner = this
            };

            if (editWindow.ShowDialog() != true)
            {
                return;
            }

            job.NextScheduledRun = editWindow.SelectedNextRun;
            jobManager.UpdateJob(job);

            BackupLogger.LogInfo(job.Name, $"Next scheduled run manually edited to {editWindow.SelectedNextRun:yyyy-MM-dd HH:mm:ss}");
            LoadJobs();

            CustomDialogService.ShowSuccess(
                this,
                $"Next run updated to {editWindow.SelectedNextRun:yyyy-MM-dd hh:mm tt}.",
                "Next Run Updated");
        }

        private static DateTime? GetLatestAllowedNextRun(BackupSchedule schedule, DateTime currentNextRun)
        {
            return schedule.Frequency switch
            {
                ScheduleFrequency.Daily => currentNextRun.Date.Add(schedule.Time).AddDays(1),
                ScheduleFrequency.Weekly => GetNextWeeklyOccurrence(schedule, currentNextRun),
                ScheduleFrequency.Monthly => GetNextMonthlyOccurrence(schedule, currentNextRun),
                ScheduleFrequency.Once => currentNextRun,
                _ => null
            };
        }

        private static DateTime? GetNextWeeklyOccurrence(BackupSchedule schedule, DateTime currentNextRun)
        {
            if (schedule.DaysOfWeek.Count == 0)
            {
                return null;
            }

            DateTime candidate = currentNextRun.Date.AddDays(1).Add(schedule.Time);
            while (!schedule.DaysOfWeek.Contains(candidate.DayOfWeek))
            {
                candidate = candidate.AddDays(1);
            }

            return candidate;
        }

        private static DateTime GetNextMonthlyOccurrence(BackupSchedule schedule, DateTime currentNextRun)
        {
            DateTime nextMonth = new DateTime(currentNextRun.Year, currentNextRun.Month, 1).AddMonths(1);
            int day = Math.Max(1, Math.Min(schedule.DayOfMonth, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)));
            return new DateTime(nextMonth.Year, nextMonth.Month, day, schedule.Time.Hours, schedule.Time.Minutes, 0);
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
                // Check if SecureServerBackupService is installed and running
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
                        BackupLogger.LogError(job.Name, "Failed to communicate with Secure Server Backup Service - backup was not started");
                        
                        MessageBox.Show(
                            "Failed to start backup. The service may be busy or not responding.\n\n" +
                            "Try again in a few moments, or restart the Secure Server Backup Service from Windows Services.",
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
                using var service = new System.ServiceProcess.ServiceController("SecureServerBackupService");
                
                if (service.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    // Log service status issue to Activity tab
                    BackupLogger.LogWarning("System", $"Secure Server Backup Service is not running (Status: {service.Status})");
                    
                    var result = MessageBox.Show(
                        $"The Secure Server Backup Service is not running (Status: {service.Status}).\n\n" +
                        "Would you like to start it now?\n\n" +
                        "Note: You may need to run this application as Administrator to start the service.",
                        "Service Not Running",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            BackupLogger.LogInfo("System", "Attempting to start Secure Server Backup Service...");
                            service.Start();
                            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                            BackupLogger.LogInfo("System", "Secure Server Backup Service started successfully");
                            MessageBox.Show(
                                "Secure Server Backup Service started successfully.",
                                "Service Started",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            BackupLogger.LogError("System", $"Failed to start Secure Server Backup Service: {ex.Message}");
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
                BackupLogger.LogError("System", "Secure Server Backup Service is not installed on this system");
                
                var result = MessageBox.Show(
                    "The Secure Server Backup Service is not installed on this system.\n\n" +
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
                BackupLogger.LogError("System", $"Error checking Secure Server Backup Service status: {ex.Message}");
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
