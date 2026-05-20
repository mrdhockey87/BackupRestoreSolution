using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using SecureServerBackup.Models;

namespace SecureServerBackup.Windows
{
	public partial class RestorePointSelectionWindow : Window
	{
		private readonly ObservableCollection<RestorePoint> _restorePoints = new();

		public RestorePointSelectionWindow(AvailableBackupInfo backup)
		{
			InitializeComponent();

			ArgumentNullException.ThrowIfNull(backup);

			Backup = backup;
			txtBackupSummary.Text = $"Backup: {backup.BackupName} ({backup.BackupType})\nSource: {backup.BackupPath}";

			foreach (RestorePoint restorePoint in RestoreWindowNew.GetRestorePointsForBackup(backup.BackupPath))
			{
				_restorePoints.Add(restorePoint);
			}

			lstRestorePoints.ItemsSource = _restorePoints;
			lstRestorePoints.SelectedIndex = _restorePoints.Count > 0 ? 0 : -1;
			btnNext.IsEnabled = _restorePoints.Count > 0;

			if (_restorePoints.Count == 0)
			{
				txtRestorePointHelp.Text = "No restore points were found for the selected backup.";
			}
		}

		public AvailableBackupInfo Backup { get; }

		public RestorePoint? SelectedRestorePoint { get; private set; }

		private void Next_Click(object sender, RoutedEventArgs e)
		{
			if (lstRestorePoints.SelectedItem is not RestorePoint selectedRestorePoint)
			{
				CustomDialogService.ShowWarning(this, "Please select a restore point to continue.", "Restore Point Required");
				return;
			}

			SelectedRestorePoint = selectedRestorePoint;
			DialogResult = true;
		}

		private void Cancel_Click(object sender, RoutedEventArgs e)
		{
			DialogResult = false;
			Close();
		}
	}
}