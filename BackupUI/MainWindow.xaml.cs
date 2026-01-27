using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
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

        private void RunJobNow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is System.Guid jobId)
            {
                var job = jobManager.GetJob(jobId);
                if (job != null)
                {
                    var result = MessageBox.Show(
                        $"Run backup job '{job.Name}' now?",
                        "Run Backup",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // TODO: Execute backup job
                        MessageBox.Show(
                            $"Backup job '{job.Name}' execution started.\n\nThis feature will be fully implemented in the next update.",
                            "Backup Started",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
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
                    var result = MessageBox.Show(
                        $"Are you sure you want to delete backup job '{job.Name}'?",
                        "Delete Backup Job",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        jobManager.DeleteJob(jobId);
                        LoadBackupJobs();
                        MessageBox.Show(
                            $"Backup job '{job.Name}' has been deleted.",
                            "Job Deleted",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
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
        
        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            var window = new RestoreWindowNew();
            window.ShowDialog();
        }
        
        private void ManageSchedules_Click(object sender, RoutedEventArgs e) => new ScheduleManagementWindow().ShowDialog();
        private void ServiceManagement_Click(object sender, RoutedEventArgs e) => new ServiceManagementWindow().ShowDialog();
        private void RecoveryEnvironmentCreator_Click(object sender, RoutedEventArgs e) => new RecoveryEnvironmentWindow().ShowDialog();
        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"Backup & Restore Solution\n{VersionClass.GetVersion()}\n\nEnterprise backup with scheduling and disaster recovery", 
                "About", MessageBoxButton.OK, MessageBoxImage.Information);
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
}

