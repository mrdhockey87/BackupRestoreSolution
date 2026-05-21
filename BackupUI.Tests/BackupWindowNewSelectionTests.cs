using System;
using System.Collections.Generic;
using SecureServerBackup.Windows;
using SecureServerBackupCommon;
using Xunit;

namespace SecureServerBackup.Tests;

public sealed class BackupWindowNewSelectionTests
{
	[Fact]
	public void BuildMissingSavedSelectionsWarningMessage_WhenSelectedFilesBackup_ExplainsRemainingSelections()
	{
		string message = BackupWindowNew.BuildMissingSavedSelectionsWarningMessage(
			BackupType.SelectedFilesAndFolders,
			new List<string> { @"C:\Data\Missing.txt" });

		Assert.Contains("removed from the current selection list", message, StringComparison.Ordinal);
		Assert.Contains(@"C:\Data\Missing.txt", message, StringComparison.Ordinal);
	}

	[Fact]
	public void BuildMissingSavedSelectionsWarningMessage_WhenNonFileBackup_ExplainsSelectionCleared()
	{
		string message = BackupWindowNew.BuildMissingSavedSelectionsWarningMessage(
			BackupType.Full,
			new List<string> { @"\\.\PHYSICALDRIVE4" });

		Assert.Contains("current selection list was cleared", message, StringComparison.Ordinal);
		Assert.Contains(@"\\.\PHYSICALDRIVE4", message, StringComparison.Ordinal);
	}

	[Fact]
	public void GetSelectionValidationMessage_WhenSelectedFilesBackupHasSelections_ReturnsNull()
	{
		string? message = BackupWindowNew.GetSelectionValidationMessage(
			selectedFilesBackup: true,
			selectedFilesCount: 1,
			selectedHyperVCount: 0,
			selectedNonHyperVCount: 0);

		Assert.Null(message);
	}

	[Fact]
	public void GetSelectionValidationMessage_WhenSelectedFilesBackupHasNoSelections_ReturnsFileMessage()
	{
		string? message = BackupWindowNew.GetSelectionValidationMessage(
			selectedFilesBackup: true,
			selectedFilesCount: 0,
			selectedHyperVCount: 0,
			selectedNonHyperVCount: 0);

		Assert.Equal("Please select at least one file, folder, or network share to back up.", message);
	}

	[Fact]
	public void GetSelectionValidationMessage_WhenNonFileBackupHasNoSelections_ReturnsGenericMessage()
	{
		string? message = BackupWindowNew.GetSelectionValidationMessage(
			selectedFilesBackup: false,
			selectedFilesCount: 0,
			selectedHyperVCount: 0,
			selectedNonHyperVCount: 0);

		Assert.Equal("Please select at least one drive, volume, folder, or Hyper-V system to backup.", message);
	}

	[Fact]
	public void GetSelectionValidationMessage_WhenNonFileBackupHasSelection_ReturnsNull()
	{
		string? message = BackupWindowNew.GetSelectionValidationMessage(
			selectedFilesBackup: false,
			selectedFilesCount: 0,
			selectedHyperVCount: 1,
			selectedNonHyperVCount: 0);

		Assert.Null(message);
	}

	[Fact]
	public void GetSelectionValidationMessage_WhenDiskOrVolumeBackupUsesGenericValidation_DoesNotRequireSelectedFiles()
	{
		string? message = BackupWindowNew.GetSelectionValidationMessage(
			selectedFilesBackup: false,
			selectedFilesCount: 0,
			selectedHyperVCount: 0,
			selectedNonHyperVCount: 1);

		Assert.Null(message);
	}
}
