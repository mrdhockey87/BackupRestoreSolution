using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BackupUI.Models;
using BackupUI.Services;

namespace BackupUI.Windows
{
    /// <summary>
    /// Non-modal progress window that shows backup progress from the service
    /// Can reconnect if closed and reopened
    /// </summary>
    public partial class BackupProgressWindow : Window
    {
        private readonly Guid _jobId;
        private readonly string _jobName;
        private readonly BackupServiceClient _serviceClient;
        private readonly DispatcherTimer _progressTimer;
        private bool _isCompleted;
        private bool _abortRequested;

        public Guid JobId => _jobId;
        public bool WasClosedWhileBackupRunning => !_isCompleted && !_abortRequested;

        public BackupProgressWindow(Guid jobId, string jobName)
        {
            InitializeComponent();

            _jobId = jobId;
            _jobName = jobName;
            _serviceClient = new BackupServiceClient();
            _progressTimer = new DispatcherTimer();

            Title = $"Backup Progress: {_jobName}";
            
            _progressTimer.Interval = TimeSpan.FromSeconds(1);
            _progressTimer.Tick += ProgressTimer_Tick;
            _progressTimer.Start();

            Loaded += async (s, e) => await UpdateProgressAsync();
        }

        private async void ProgressTimer_Tick(object? sender, EventArgs e)
        {
            await UpdateProgressAsync();
        }

        private async Task UpdateProgressAsync()
        {
            try
            {
                var progress = await _serviceClient.GetProgressAsync(_jobId);

                if (progress != null)
                {
                    progressBar.Value = progress.Percentage;
                    txtProgress.Text = progress.Message;
                    txtPercentage.Text = $"{progress.Percentage}%";

                    // Display current file if available (v6.0.1.19)
                    if (!string.IsNullOrEmpty(progress.CurrentFile))
                    {
                        txtCurrentFile.Text = progress.CurrentFile;
                    }
                    else
                    {
                        txtCurrentFile.Text = "";
                    }

                    // Update window title based on phase
                    if (progress.IsVerifying)
                    {
                        Title = $"Verification Progress: {_jobName}";
                    }
                    else if (progress.IsRunning)
                    {
                        Title = $"Backup Progress: {_jobName}";
                    }

                    if (!progress.IsRunning)
                    {
                        _progressTimer.Stop();
                        _isCompleted = true;
                        btnAbort.IsEnabled = false;

                        if (progress.Success)
                        {
                            txtProgress.Text = "Backup completed successfully!";
                            MessageBox.Show(
                                $"Backup job '{_jobName}' completed successfully!",
                                "Backup Complete",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        else
                        {
                            txtProgress.Text = $"Backup failed: {progress.ErrorMessage ?? "Unknown error"}";
                            MessageBox.Show(
                                $"Backup job '{_jobName}' failed!\n\nError: {progress.ErrorMessage ?? "Unknown error"}\n\nCheck Activity log for details.",
                                "Backup Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }

                        Close();
                    }
                }
                else
                {
                    // No progress found - backup might not have started yet
                    // Give it a grace period of 5 seconds before showing "waiting" message
                    if (_progressTimer.Tag == null)
                    {
                        _progressTimer.Tag = DateTime.Now;
                    }

                    var elapsed = DateTime.Now - (DateTime)_progressTimer.Tag;
                    if (elapsed.TotalSeconds < 5)
                    {
                        txtProgress.Text = "Initializing backup...";
                    }
                    else
                    {
                        txtProgress.Text = "Waiting for backup to start...";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating progress: {ex.Message}");
            }
        }

        private async void AbortBackup_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to abort the backup '{_jobName}'?\n\nThis cannot be undone.",
                "Abort Backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                btnAbort.IsEnabled = false;
                txtProgress.Text = "Aborting backup...";
                _abortRequested = true;

                var success = await _serviceClient.AbortBackupAsync(_jobId);

                if (success)
                {
                    // Mark as completed since we requested abort
                    _isCompleted = true;
                    _progressTimer.Stop();

                    MessageBox.Show(
                        "Backup abort has been requested.\n\n" +
                        "IMPORTANT: The backup process may continue running in the background " +
                        "for a short time while it safely stops the current operation.\n\n" +
                        "The backup file may be incomplete and should be deleted.",
                        "Backup Abort Requested",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    // Close the progress window
                    Close();
                }
                else
                {
                    MessageBox.Show(
                        "Failed to send abort request. Please try again or check if the service is running.",
                        "Abort Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    btnAbort.IsEnabled = true;
                    _abortRequested = false;
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Only show warning if backup is NOT completed AND NOT aborted
            if (!_isCompleted && !_abortRequested)
            {
                var result = MessageBox.Show(
                    "Backup is still running in the background.\n\nClosing this window will not stop the backup.\n\nYou can reopen this window from the main window to view progress again.",
                    "Backup Still Running",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _progressTimer.Stop();
            base.OnClosing(e);
        }
    }
}
