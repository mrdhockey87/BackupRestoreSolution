using System;
using System.Collections.Generic;
using System.Linq;
using SecureServerBackupCommon;

namespace SecureServerBackupCommon
{
	/// <summary>
	/// Helper methods for Hyper-V backup tree operations and system disk detection.
	/// </summary>
	public static class HyperVBackupTreeHelper
	{
		/// <summary>
		/// Determines whether SetupCl should be scheduled for a Hyper-V clone operation.
		/// </summary>
		public static bool ShouldScheduleSetupCl(
			bool renameHyperVSystem,
			string? renameHyperVSystemName,
			BackupTarget target,
			IEnumerable<string>? sourcePaths,
			IEnumerable<int>? protectedDiskIndexes = null)
		{
			bool renamedClone = renameHyperVSystem && !string.IsNullOrWhiteSpace(renameHyperVSystemName);
			HashSet<int> protectedDisks = protectedDiskIndexes?
				.Where(index => index >= 0)
				.ToHashSet() ?? new HashSet<int>();

			bool clonedFromSystemDisk = target == BackupTarget.Disk && (sourcePaths?.Any(path =>
				TryGetPhysicalDriveNumber(path, out int diskNumber) && protectedDisks.Contains(diskNumber)) ?? false);

			return renamedClone || clonedFromSystemDisk;
		}

		private static bool TryGetPhysicalDriveNumber(string? path, out int diskNumber)
		{
			diskNumber = -1;
			if (string.IsNullOrWhiteSpace(path))
			{
				return false;
			}

			const string physicalDrivePrefix = "PHYSICALDRIVE";
			int prefixIndex = path.LastIndexOf(physicalDrivePrefix, StringComparison.OrdinalIgnoreCase);
			if (prefixIndex < 0)
			{
				return false;
			}

			string suffix = path[(prefixIndex + physicalDrivePrefix.Length)..].Trim();
			return int.TryParse(suffix, out diskNumber);
		}
	}
}
