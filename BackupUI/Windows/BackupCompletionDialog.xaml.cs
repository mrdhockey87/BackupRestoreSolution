using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SecureServerBackup.Windows
{
    /// <summary>
    /// Completion alert for backup jobs. Auto-closes after <see cref="AutoCloseMinutes"/> minutes
    /// if the user has not dismissed it. When the user clicks OK the dialog closes but the caller
    /// (BackupProgressWindow) remains open so the user can close it at their own pace.
    /// </summary>
    public partial class BackupCompletionDialog : Window
    {
        private const int AutoCloseMinutes = 15;

        private readonly DispatcherTimer _countdownTimer;
        private TimeSpan _remaining;

        /// <summary>
        /// True when the dialog was closed automatically by the timer (not by the user).
        /// The caller uses this to also close the progress window.
        /// </summary>
        public bool WasAutoClose { get; private set; }

        public BackupCompletionDialog()
        {
            InitializeComponent();

            Topmost = true;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _remaining = TimeSpan.FromMinutes(AutoCloseMinutes);

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;

            Loaded += (_, _) => _countdownTimer.Start();
            Closed += (_, _) => _countdownTimer.Stop();
        }

        /// <summary>
        /// Configure the dialog for a successful backup completion.
        /// </summary>
        public void ConfigureSuccess(string jobName)
        {
            txtTitle.Text = "Backup Complete";
            txtIcon.Text = "✅";
            txtIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 100, 0));
            txtMessage.Text = $"Backup job '{jobName}' completed successfully!";
        }

        /// <summary>
        /// Configure the dialog for a failed backup.
        /// </summary>
        public void ConfigureFailure(string jobName, string? errorMessage)
        {
            txtTitle.Text = "Backup Failed";
            txtIcon.Text = "❌";
            txtIcon.Foreground = new SolidColorBrush(Color.FromRgb(139, 0, 0));
            txtMessage.Text = $"Backup job '{jobName}' failed!\n\nError: {errorMessage ?? "Unknown error"}\n\nCheck Activity log for details.";
            btnOk.Background = new SolidColorBrush(Color.FromRgb(139, 0, 0));
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            _remaining = _remaining.Subtract(TimeSpan.FromSeconds(1));

            if (_remaining <= TimeSpan.Zero)
            {
                WasAutoClose = true;
                Close();
                return;
            }

            txtCountdown.Text = $"This dialog will close automatically in {_remaining:m\\:ss}";
        }

        private void BtnOk_Click(object? sender, RoutedEventArgs e)
        {
            WasAutoClose = false;
            Close();
        }
    }
}
