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
                var result = MessageBox.Show(
                    $"Run backup job '{job.Name}' now?\n\nThe backup will run in the background service and continue even if you close this window.",
                    "Run Backup",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Send job to service and show progress window
                    var serviceClient = new BackupServiceClient();
                    var success = await serviceClient.RunBackupNowAsync(job.Id);

                    if (success)
                    {
                        BackupLogger.LogInfo(job.Name, "Manual backup started via service");
                        
                        // Show non-modal progress window
                        var progressWindow = new BackupProgressWindow(job.Id, job.Name);
                        progressWindow.Show();
                    }
                    else
                    {
                        MessageBox.Show(
                            "Failed to start backup. Please ensure the BackupRestoreService is running.",
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
