using System;
using System.Collections.Generic;
using SecureServerBackup.Models;
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

	[Fact]
	public void DriveTreeItem_WhenSelectionDisabled_DoesNotParticipateInCheckState()
	{
		DriveTreeItem item = new()
		{
			Name = "Disk 0",
			ItemType = DriveTreeItemType.Disk,
			IsSelectionEnabled = false
		};

		Assert.False(item.ParticipatesInCheckState);
	}

	[Fact]
	public void DriveTreeItem_WhenDiskSelectionEnabled_ParticipatesInCheckState()
	{
		DriveTreeItem item = new()
		{
			Name = "Disk 0",
			ItemType = DriveTreeItemType.Disk,
			IsSelectionEnabled = true
		};

		Assert.True(item.ParticipatesInCheckState);
	}

	[Theory]
	[InlineData("SERVER01")]
	[InlineData("HV-CLONE-1")]
	[InlineData("ABC123")]
	public void IsValidWindowsComputerName_WhenNameIsValid_ReturnsTrue(string name)
	{
		Assert.True(BackupWindowNew.IsValidWindowsComputerName(name));
	}

	[Theory]
	[InlineData("")]
	[InlineData("TOO-LONG-COMPUTER")]
	[InlineData("BAD NAME")]
	[InlineData("-BADNAME")]
	[InlineData("BADNAME-")]
	[InlineData("BAD.NAME")]
	public void IsValidWindowsComputerName_WhenNameIsInvalid_ReturnsFalse(string name)
	{
		Assert.False(BackupWindowNew.IsValidWindowsComputerName(name));
	}

	[Fact]
	public void DriveTreeItem_WhenDisabledChildExists_DoesNotPromoteParentToChecked()
	{
		DriveTreeItem parent = new()
		{
			Name = "Hyper-V: VmOne",
			ItemType = DriveTreeItemType.HyperVSystem
		};

		DriveTreeItem enabledChild = new()
		{
			Name = "Enabled child",
			ItemType = DriveTreeItemType.HyperVSystem,
			Parent = parent,
			IsSelectionEnabled = true,
			IsChecked = false
		};

		DriveTreeItem disabledChild = new()
		{
			Name = "Disabled disk",
			ItemType = DriveTreeItemType.Disk,
			Parent = parent,
			IsSelectionEnabled = false,
			IsChecked = true
		};

		parent.Children.Add(enabledChild);
		parent.Children.Add(disabledChild);

		enabledChild.IsChecked = true;

		Assert.True(parent.IsChecked);
	}

	[Fact]
	public void GetReplayPathsForJob_WhenCloneHyperVSystemWithSavedVmNames_PrefersHyperVMachines()
	{
		BackupJob job = new()
		{
			Type = BackupType.CloneHyperVSystem,
			Target = BackupTarget.HyperV,
			HyperVMachines = new List<string> { "VmOne" },
			SourcePaths = new List<string> { @"\\.\PHYSICALDRIVE2" }
		};

		IReadOnlyList<string> replayPaths = BackupWindowNew.GetReplayPathsForJob(job);

		Assert.Single(replayPaths);
		Assert.Equal("VmOne", replayPaths[0]);
	}

	[Theory]
	[InlineData("Hyper-V: Win10OEM (Running)", "Win10OEM")]
	[InlineData("Win10OEM (Off)", "Win10OEM")]
	[InlineData("Win10OEM", "Win10OEM")]
	public void GetReplayPathsForJob_WhenCloneHyperVSystemUsesLegacyDisplayNames_NormalizesVmNames(string savedVmName, string expectedVmName)
	{
		BackupJob job = new()
		{
			Type = BackupType.CloneHyperVSystem,
			Target = BackupTarget.HyperV,
			HyperVMachines = new List<string> { savedVmName }
		};

		IReadOnlyList<string> replayPaths = BackupWindowNew.GetReplayPathsForJob(job);

		Assert.Single(replayPaths);
		Assert.Equal(expectedVmName, replayPaths[0]);
	}

	[Fact]
	public void GetReplayPathsForJob_WhenCloneHyperVSystemWithoutSavedVmNames_FallsBackToSourcePaths()
	{
		BackupJob job = new()
		{
			Type = BackupType.CloneHyperVSystem,
			Target = BackupTarget.HyperV,
			SourcePaths = new List<string> { @"\\.\PHYSICALDRIVE2" }
		};

		IReadOnlyList<string> replayPaths = BackupWindowNew.GetReplayPathsForJob(job);

		Assert.Single(replayPaths);
		Assert.Equal(@"\\.\PHYSICALDRIVE2", replayPaths[0]);
	}

	[Fact]
	public void BuildMissingSavedSelectionsWarningMessage_WhenCloneHyperVSystem_ExplainsSelectionCleared()
	{
		string message = BackupWindowNew.BuildMissingSavedSelectionsWarningMessage(
			BackupType.CloneHyperVSystem,
			new List<string> { "Win10OEM" });

		Assert.Contains("current selection list was cleared", message, StringComparison.Ordinal);
		Assert.Contains("Win10OEM", message, StringComparison.Ordinal);
	}
}
