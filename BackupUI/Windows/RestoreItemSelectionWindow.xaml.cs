using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using SecureServerBackup.Models;

namespace SecureServerBackup.Windows
{
	public partial class RestoreItemSelectionWindow : Window
	{
		public RestoreItemSelectionWindow(AvailableBackupInfo backup, RestorePoint restorePoint, IReadOnlyList<string> items)
		{
			InitializeComponent();

			ArgumentNullException.ThrowIfNull(backup);
			ArgumentNullException.ThrowIfNull(restorePoint);
			ArgumentNullException.ThrowIfNull(items);

			Backup = backup;
			RestorePoint = restorePoint;
			txtSummary.Text = $"Restore point: {restorePoint.DisplayName}\nSource: {restorePoint.FilePath}";

			foreach (string item in items.Where(item => !string.IsNullOrWhiteSpace(item)))
			{
				lstBackupItems.Items.Add(item);
			}

			btnNext.IsEnabled = lstBackupItems.Items.Count > 0;
		}

		public AvailableBackupInfo Backup { get; }

		public RestorePoint RestorePoint { get; }

		public IReadOnlyList<string> SelectedItems { get; private set; } = Array.Empty<string>();

		private void Next_Click(object sender, RoutedEventArgs e)
		{
			List<string> selectedItems = lstBackupItems.SelectedItems.Cast<object>()
				.Select(item => item?.ToString() ?? string.Empty)
				.Where(item => !string.IsNullOrWhiteSpace(item))
				.ToList();

			if (selectedItems.Count == 0)
			{
				CustomDialogService.ShowWarning(this, "Please select at least one file or folder to continue.", "Restore Items Required");
				return;
			}

			SelectedItems = selectedItems;
			DialogResult = true;
		}

		private void Cancel_Click(object sender, RoutedEventArgs e)
		{
			DialogResult = false;
			Close();
		}
	}
}