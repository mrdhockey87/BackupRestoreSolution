using System;
using System.Collections.Generic;
using SecureServerBackup.Windows;

namespace SecureServerBackup.Models
{
	public sealed class RestoreSelectionContext
	{
		public AvailableBackupInfo Backup { get; init; } = new();

		public SecureServerBackup.Windows.RestorePoint RestorePoint { get; init; } = new();

		public bool RequireAlternateDestination { get; init; }

		public RestoreScopeKind ScopeKind { get; init; } = RestoreScopeKind.All;

		public IReadOnlyList<string> SelectedItems { get; init; } = Array.Empty<string>();

		public IReadOnlyList<VolumeInfo> SelectedVolumes { get; init; } = Array.Empty<VolumeInfo>();
	}
}