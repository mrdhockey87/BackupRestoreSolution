using System;
using System.Windows;
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
                // Open BackupWindow with job loaded for editing
                MessageBox.Show($"Edit job: {job.Name}\n(Edit functionality to be implemented)",
                    "Edit Job", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void RunNow_Click(object sender, RoutedEventArgs e)
        {
            if (dgJobs.SelectedItem is BackupJob job)
            {
                MessageBox.Show($"Running job: {job.Name}\n(Run now functionality to be implemented)",
                    "Run Job", MessageBoxButton.OK, MessageBoxImage.Information);
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
