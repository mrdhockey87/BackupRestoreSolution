using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BackupUI.Models;
using BackupUI.Services;
using Microsoft.Win32;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace BackupUI.Windows
{
    public partial class BackupWindow : Window
    {
        private readonly JobManager jobManager = new();
        private BackupJob currentJob = new();

        public BackupWindow()
        {
            InitializeComponent();
            InitializeControls();
        }

        private void InitializeControls()
        {
            cmbBackupType.ItemsSource = Enum.GetValues(typeof(BackupType));
            cmbBackupType.SelectedIndex = 0;

            cmbBackupTarget.ItemsSource = Enum.GetValues(typeof(BackupTarget));
            cmbBackupTarget.SelectedIndex = 0;

            cmbFrequency.ItemsSource = Enum.GetValues(typeof(ScheduleFrequency));
            cmbFrequency.SelectedIndex = 0;

            for (int i = 0; i < 24; i++)
                cmbHour.Items.Add(i.ToString("D2"));
            cmbHour.SelectedIndex = 0;

            for (int i = 0; i < 60; i += 15)
                cmbMinute.Items.Add(i.ToString("D2"));
            cmbMinute.SelectedIndex = 0;

            for (int i = 1; i <= 31; i++)
                cmbDayOfMonth.Items.Add(i);
            cmbDayOfMonth.SelectedIndex = 0;

            LoadVolumes();
            LoadDisks();
        }

        private void LoadVolumes()
        {
            Task.Run(() =>
            {
                try
                {
                    var buffer = new StringBuilder(4096);
                    int result = BackupEngineInterop.EnumerateVolumes(buffer, buffer.Capacity);

                    if (result == 0)
                    {
                        var volumes = buffer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        Dispatcher.Invoke(() =>
                        {
                            lstVolumes.Items.Clear();
                            foreach (var volume in volumes)
                                lstVolumes.Items.Add(volume);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                        MessageBox.Show($"Failed to load volumes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }

        private void LoadDisks()
        {
            Task.Run(() =>
            {
                try
                {
                    var buffer = new StringBuilder(4096);
                    int result = BackupEngineInterop.EnumerateDisks(buffer, buffer.Capacity);

                    if (result == 0)
                    {
                        var disks = buffer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        Dispatcher.Invoke(() =>
                        {
                            cmbDisks.Items.Clear();
                            foreach (var disk in disks)
                                cmbDisks.Items.Add(disk);
                            if (cmbDisks.Items.Count > 0)
                                cmbDisks.SelectedIndex = 0;
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                        MessageBox.Show($"Failed to load disks: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }

        private void BackupType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Update UI based on backup type selection
        }

        private void BackupTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            pnlDiskSelection.Visibility = Visibility.Collapsed;
            pnlVolumeSelection.Visibility = Visibility.Collapsed;
            pnlFileSelection.Visibility = Visibility.Collapsed;

            if (cmbBackupTarget.SelectedItem is BackupTarget target)
            {
                switch (target)
                {
                    case BackupTarget.Disk:
                        pnlDiskSelection.Visibility = Visibility.Visible;
                        break;
                    case BackupTarget.Volume:
                        pnlVolumeSelection.Visibility = Visibility.Visible;
                        break;
                    case BackupTarget.FilesAndFolders:
                        pnlFileSelection.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        private void HyperV_CheckedChanged(object sender, RoutedEventArgs e)
        {
            pnlHyperV.Visibility = chkHyperV.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (chkHyperV.IsChecked == true)
                RefreshHyperV_Click(sender, e);
        }

        private void RefreshHyperV_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                try
                {
                    var buffer = new StringBuilder(8192);
                    int result = BackupEngineInterop.EnumerateHyperVMachines(buffer, buffer.Capacity);

                    if (result == 0)
                    {
                        var vms = buffer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        Dispatcher.Invoke(() =>
                        {
                            lstHyperVMachines.Items.Clear();
                            foreach (var vm in vms)
                                lstHyperVMachines.Items.Add(vm);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                        MessageBox.Show($"Failed to load Hyper-V machines: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Select Files to Backup"
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                    lstFiles.Items.Add(file);
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select Folder to Backup",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                lstFiles.Items.Add(dialog.SelectedPath);
            }
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (lstFiles.SelectedItem != null)
                lstFiles.Items.Remove(lstFiles.SelectedItem);
        }

        private void BrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select Backup Destination",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtDestination.Text = dialog.SelectedPath;
            }
        }

        private void Schedule_CheckedChanged(object sender, RoutedEventArgs e)
        {
            pnlSchedule.IsEnabled = chkEnableSchedule.IsChecked == true;
        }

        private void Frequency_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            pnlWeekly.Visibility = Visibility.Collapsed;
            pnlMonthly.Visibility = Visibility.Collapsed;

            if (cmbFrequency.SelectedItem is ScheduleFrequency freq)
            {
                switch (freq)
                {
                    case ScheduleFrequency.Weekly:
                        pnlWeekly.Visibility = Visibility.Visible;
                        break;
                    case ScheduleFrequency.Monthly:
                        pnlMonthly.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        private async void StartBackup_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            var job = CreateJobFromInput();
            await ExecuteBackup(job);
        }

        private void SaveJob_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
                return;

            var job = CreateJobFromInput();
            jobManager.AddJob(job);
            MessageBox.Show("Backup job saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(txtBackupName.Text))
            {
                MessageBox.Show("Please enter a backup name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtDestination.Text))
            {
                MessageBox.Show("Please select a backup destination.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var target = (BackupTarget)cmbBackupTarget.SelectedItem;
            if (target == BackupTarget.FilesAndFolders && lstFiles.Items.Count == 0)
            {
                MessageBox.Show("Please select files or folders to backup.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private BackupJob CreateJobFromInput()
        {
            var job = new BackupJob
            {
                Id = Guid.NewGuid(),
                Name = txtBackupName.Text,
                Type = (BackupType)cmbBackupType.SelectedItem,
                Target = (BackupTarget)cmbBackupTarget.SelectedItem,
                DestinationPath = txtDestination.Text,
                IncludeSystemState = chkIncludeSystemState.IsChecked == true,
                CompressData = chkCompress.IsChecked == true,
                VerifyAfterBackup = chkVerify.IsChecked == true,
                IsHyperVBackup = chkHyperV.IsChecked == true
            };

            switch (job.Target)
            {
                case BackupTarget.Disk:
                    if (cmbDisks.SelectedItem != null)
                        job.SourcePaths.Add(cmbDisks.SelectedItem.ToString()!);
                    break;
                case BackupTarget.Volume:
                    foreach (var item in lstVolumes.SelectedItems)
                        job.SourcePaths.Add(item.ToString()!);
                    break;
                case BackupTarget.FilesAndFolders:
                    foreach (var item in lstFiles.Items)
                        job.SourcePaths.Add(item.ToString()!);
                    break;
            }

            if (job.IsHyperVBackup)
            {
                foreach (var item in lstHyperVMachines.SelectedItems)
                    job.HyperVMachines.Add(item.ToString()!);
            }

            if (chkEnableSchedule.IsChecked == true)
            {
                job.Schedule = new BackupSchedule
                {
                    JobId = job.Id,
                    Enabled = true,
                    Frequency = (ScheduleFrequency)cmbFrequency.SelectedItem,
                    Time = new TimeSpan(int.Parse(cmbHour.SelectedItem.ToString()!),
                                       int.Parse(cmbMinute.SelectedItem.ToString()!), 0)
                };

                if (job.Schedule.Frequency == ScheduleFrequency.Weekly)
                {
                    if (chkMonday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Monday);
                    if (chkTuesday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Tuesday);
                    if (chkWednesday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Wednesday);
                    if (chkThursday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Thursday);
                    if (chkFriday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Friday);
                    if (chkSaturday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Saturday);
                    if (chkSunday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Sunday);
                }
                else if (job.Schedule.Frequency == ScheduleFrequency.Monthly)
                {
                    job.Schedule.DayOfMonth = (int)cmbDayOfMonth.SelectedItem;
                }
            }

            return job;
        }

        private async Task ExecuteBackup(BackupJob job)
        {
            progressBar.Visibility = Visibility.Visible;
            txtProgress.Visibility = Visibility.Visible;

            try
            {
                BackupEngineInterop.ProgressCallback callback = (percentage, message) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = percentage;
                        txtProgress.Text = message;
                    });
                };

                await Task.Run(() =>
                {
                    int result = -1;

                    foreach (var sourcePath in job.SourcePaths)
                    {
                        var destPath = Path.Combine(job.DestinationPath,
                            $"{job.Name}_{DateTime.Now:yyyyMMdd_HHmmss}");

                        switch (job.Type)
                        {
                            case BackupType.Full:
                                if (job.Target == BackupTarget.Volume)
                                {
                                    result = BackupEngineInterop.BackupVolume(
                                        sourcePath, destPath, job.IncludeSystemState,
                                        job.CompressData, callback);
                                }
                                else
                                {
                                    result = BackupEngineInterop.BackupFiles(
                                        sourcePath, destPath, callback);
                                }
                                break;

                            case BackupType.Incremental:
                                var lastBackup = FindLastBackup(job.DestinationPath, job.Name);
                                result = BackupEngineInterop.CreateIncrementalBackup(
                                    sourcePath, destPath, lastBackup ?? "", callback);
                                break;

                            case BackupType.Differential:
                                var fullBackup = FindFullBackup(job.DestinationPath, job.Name);
                                result = BackupEngineInterop.CreateDifferentialBackup(
                                    sourcePath, destPath, fullBackup ?? "", callback);
                                break;
                        }

                        if (result != 0)
                        {
                            var error = new StringBuilder(1024);
                            BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
                            throw new Exception($"Backup failed: {error}");
                        }
                    }

                    if (job.IsHyperVBackup)
                    {
                        foreach (var vm in job.HyperVMachines)
                        {
                            var destPath = Path.Combine(job.DestinationPath,
                                $"{vm}_{DateTime.Now:yyyyMMdd_HHmmss}");
                            result = BackupEngineInterop.BackupHyperVVM(vm, destPath, callback);

                            if (result != 0)
                            {
                                var error = new StringBuilder(1024);
                                BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
                                throw new Exception($"Hyper-V backup failed: {error}");
                            }
                        }
                    }

                    if (job.VerifyAfterBackup)
                    {
                        result = BackupEngineInterop.VerifyBackup(job.DestinationPath, callback);
                        if (result != 0)
                        {
                            throw new Exception("Backup verification failed!");
                        }
                    }
                });

                MessageBox.Show("Backup completed successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Backup failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                txtProgress.Visibility = Visibility.Collapsed;
            }
        }

        private string? FindLastBackup(string destPath, string jobName)
        {
            try
            {
                if (!Directory.Exists(destPath))
                    return null;

                var backups = Directory.GetDirectories(destPath, $"{jobName}_*")
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                return backups;
            }
            catch
            {
                return null;
            }
        }

        private string? FindFullBackup(string destPath, string jobName)
        {
            return FindLastBackup(destPath, jobName);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
