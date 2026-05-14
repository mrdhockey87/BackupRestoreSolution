using System;
using System.Threading.Tasks;
using System.Windows;
using SecureServerBackup.Services;

namespace SecureServerBackup.Windows
{
	/// <summary>
	/// Non-modal-style restore progress window that owns restore completion UI.
	/// </summary>
	public partial class RestoreProgressWindow : Window
	{
		private readonly Func<BackupEngineInterop.ProgressCallback, Task> _restoreOperation;
		private readonly bool _keepWindowOpen;
		private bool _isCompleted;

		public bool RestoreSucceeded { get; private set; }

		public RestoreProgressWindow(
			string restoreName,
			bool keepWindowOpen,
			Func<BackupEngineInterop.ProgressCallback, Task> restoreOperation)
		{
			ArgumentNullException.ThrowIfNull(restoreOperation);
			InitializeComponent();

			_restoreOperation = restoreOperation;
			_keepWindowOpen = keepWindowOpen;

			Title = $"Restore Progress: {restoreName}";
			Loaded += RestoreProgressWindow_Loaded;
		}

		private async void RestoreProgressWindow_Loaded(object sender, RoutedEventArgs e)
		{
			Loaded -= RestoreProgressWindow_Loaded;
			await RunRestoreAsync();
		}

		private async Task RunRestoreAsync()
		{
			try
			{
				await _restoreOperation(UpdateProgress);

				RestoreSucceeded = true;
				_isCompleted = true;
				progressBar.Value = 100;
				txtPercentage.Text = "100%";
				txtProgress.Text = "Restore completed successfully!";
				btnHideClose.Content = "Close";

				var completionDialog = new BackupCompletionDialog { Owner = this };
				completionDialog.ConfigureRestoreSuccess(!_keepWindowOpen);
				completionDialog.ShowDialog();

				if (!_keepWindowOpen && completionDialog.WasAutoClose)
				{
					Close();
				}
			}
			catch (Exception ex)
			{
				_isCompleted = true;
				txtProgress.Text = $"Restore failed: {ex.Message}";
				btnHideClose.Content = "Close";
				MessageBox.Show(this, $"Restore failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void UpdateProgress(int percentage, string? message)
		{
			Dispatcher.Invoke(() =>
			{
				if (percentage >= 0)
				{
					progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, percentage));
					txtPercentage.Text = $"{percentage}%";
				}

				if (!string.IsNullOrWhiteSpace(message))
				{
					txtProgress.Text = message;
					txtCurrentFile.Text = message;
				}
			});
		}

		private void Close_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
		{
			if (!_isCompleted)
			{
				var result = MessageBox.Show(
					this,
					"Restore is still running in the background.\n\nClosing this window will not stop the restore.",
					"Restore Still Running",
					MessageBoxButton.OKCancel,
					MessageBoxImage.Information);

				if (result == MessageBoxResult.Cancel)
				{
					e.Cancel = true;
					return;
				}
			}

			base.OnClosing(e);
		}
	}
}
