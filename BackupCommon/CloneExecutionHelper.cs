using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SecureServerBackupCommon
{
	/// <summary>
	/// Shared clone execution helper accessible by both BackupUI and BackupService.
	/// </summary>
	public static class CloneExecutionHelper
	{
		internal static Func<string, CloneHyperVPaths, bool, BackupEngineInterop.ProgressCallback, string>? CreateCloneHyperVVirtualDiskFromVmOverride { get; set; }
		internal static Action<string, string>? CreateCloneHyperVVirtualMachineFromExportOverride { get; set; }
		internal static Action<string>? ScheduleSetupClPendingRequestOverride { get; set; }
		internal static Action<string>? RegenerateHyperVVirtualMachineMacAddressOverride { get; set; }
		internal static Func<BackupEngineInterop.HyperVVerifyParams, (int Result, BackupEngineInterop.HyperVVerifyReport Report)>? VerifyHyperVCloneOverride { get; set; }

		internal static void ResetTestOverrides()
		{
			CreateCloneHyperVVirtualDiskFromVmOverride = null;
			CreateCloneHyperVVirtualMachineFromExportOverride = null;
			ScheduleSetupClPendingRequestOverride = null;
			RegenerateHyperVVirtualMachineMacAddressOverride = null;
			VerifyHyperVCloneOverride = null;
		}

		/// <summary>
		/// Executes a CloneToVirtualDisk job.
		/// </summary>
		public static bool ExecuteCloneToVirtualDiskJob(BackupJob job, BackupEngineInterop.ProgressCallback progressCallback)
		{
			ArgumentNullException.ThrowIfNull(job);
			ArgumentNullException.ThrowIfNull(progressCallback);

			string virtualDiskPath = job.GetVirtualDiskClonePath();
			bool cloneAsDisk = job.ShouldCloneToVirtualDiskAsDisk();

			progressCallback(0, cloneAsDisk
				? $"Cloning selected source into virtual disk {Path.GetFileName(virtualDiskPath)}..."
				: $"Cloning selected volume into virtual disk {Path.GetFileName(virtualDiskPath)}...");

			string temporaryArchiveDirectory = Path.Combine(Path.GetTempPath(), "SecureServerBackup", "VirtualDiskClone", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(temporaryArchiveDirectory);

			string temporaryArchivePath = Path.Combine(temporaryArchiveDirectory, $"{job.Name}.ssb");

			try
			{
				int result;
				if (cloneAsDisk)
				{
					string diskPath = job.SourcePaths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
						?? throw new InvalidOperationException("Clone to Virtual Disk requires a selected source disk.");
					string diskNumStr = diskPath.Replace("\\\\?\\PHYSICALDRIVE", "").Replace("\\\\.\\PHYSICALDRIVE", "");
					if (!int.TryParse(diskNumStr, out int diskNum))
					{
						throw new InvalidOperationException($"Invalid disk path format: {diskPath}");
					}

					result = BackupEngineInterop.BackupDisk(
						diskNum,
						temporaryArchivePath,
						job.IncludeSystemState,
						job.CompressData,
						null,
						0,
						progressCallback,
						null);

					if (result != 0)
					{
						var errorBuffer = new StringBuilder(4096);
						BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
						throw new InvalidOperationException($"Disk capture for virtual disk clone failed: {errorBuffer}");
					}
				}
				else
				{
					string volumePath = job.SourcePaths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
						?? throw new InvalidOperationException("Clone to Virtual Disk requires a selected source volume.");

					result = BackupEngineInterop.BackupVolume(
						volumePath,
						temporaryArchivePath,
						job.IncludeSystemState,
						job.CompressData,
						null,
						0,
						progressCallback,
						null);

					if (result != 0)
					{
						var errorBuffer = new StringBuilder(4096);
						BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
						throw new InvalidOperationException($"Volume capture for virtual disk clone failed: {errorBuffer}");
					}
				}

				progressCallback(70, "Creating virtual disk from archive...");
				result = cloneAsDisk
					? BackupEngineInterop.RestoreArchiveToVhdxAsDisk(temporaryArchivePath, virtualDiskPath, progressCallback)
					: BackupEngineInterop.RestoreArchiveToVhdxAsVolume(temporaryArchivePath, virtualDiskPath, progressCallback);

				if (result != 0)
				{
					var errorBuffer = new StringBuilder(4096);
					BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
					throw new InvalidOperationException($"Virtual disk creation failed: {errorBuffer}");
				}
			}
			finally
			{
				try
				{
					if (Directory.Exists(temporaryArchiveDirectory))
					{
						Directory.Delete(temporaryArchiveDirectory, recursive: true);
					}
				}
				catch (Exception cleanupEx)
				{
					Debug.WriteLine($"Temporary virtual disk clone cleanup warning: {cleanupEx.Message}");
				}
			}

			progressCallback(100, $"Virtual disk clone completed: {Path.GetFileName(virtualDiskPath)}");
			return true;
		}

		/// <summary>
		/// Executes a CloneHyperVSystem job.
		/// </summary>
		public static void ExecuteCloneHyperVSystemJob(BackupJob job, BackupEngineInterop.ProgressCallback progressCallback)
		{
			CloneHyperVPaths clonePaths = CreateCloneHyperVPaths(job);
			string actualVirtualDiskPath;
			string? sourceVmName = null;
			bool cloneFromExportedVm = false;
			bool renameRequested = job.RenameHyperVSystem && !string.IsNullOrWhiteSpace(job.RenameHyperVSystemName);

			progressCallback(1, $"Starting Hyper-V System Clone '{job.Name}'...");

			if (job.HyperVMachines.Count > 0)
			{
				sourceVmName = job.HyperVMachines[0];
				progressCallback(5, $"Exporting Hyper-V VM '{sourceVmName}' for clone...");
				actualVirtualDiskPath = (CreateCloneHyperVVirtualDiskFromVmOverride ?? CreateCloneHyperVVirtualDiskFromVm)(sourceVmName, clonePaths, job.RenameHyperVSystem, progressCallback);
				cloneFromExportedVm = true;
			}
			else if (job.Target == BackupTarget.Disk && job.SourcePaths.Count > 0)
			{
				progressCallback(5, $"Cloning selected disk into {Path.GetFileName(clonePaths.VirtualDiskPath)}...");
				CreateCloneHyperVVirtualDiskFromDisk(job, clonePaths.VirtualDiskPath, progressCallback);
				actualVirtualDiskPath = clonePaths.VirtualDiskPath;
			}
			else
			{
				throw new InvalidOperationException("Clone Hyper-V System requires either a selected Hyper-V VM or a selected disk.");
			}

			bool shouldScheduleSetupCl = renameRequested;

			if (shouldScheduleSetupCl)
			{
				progressCallback(75, $"Scheduling SetupCl for '{clonePaths.VmName}'...");
				(ScheduleSetupClPendingRequestOverride ?? ScheduleSetupClPendingRequest)(actualVirtualDiskPath);
			}

			if (cloneFromExportedVm)
			{
				progressCallback(80, $"Importing Hyper-V VM '{clonePaths.VmName}'...");
				(CreateCloneHyperVVirtualMachineFromExportOverride ?? CreateCloneHyperVVirtualMachineFromExport)(clonePaths.VmName, clonePaths.RootDirectory);
			}
			else
			{
				progressCallback(80, $"Creating Hyper-V VM '{clonePaths.VmName}'...");
				CreateCloneHyperVVirtualMachine(clonePaths.VmName, clonePaths.HyperVSystemDirectory, actualVirtualDiskPath);
			}

			if (renameRequested)
			{
				progressCallback(95, $"Regenerating MAC address for '{clonePaths.VmName}'...");
				(RegenerateHyperVVirtualMachineMacAddressOverride ?? RegenerateHyperVVirtualMachineMacAddress)(clonePaths.VmName);
			}

			if (cloneFromExportedVm)
			{
				progressCallback(renameRequested ? 97 : 95, $"Verifying cloned Hyper-V VM '{clonePaths.VmName}'...");
				VerifyClonedHyperVVirtualMachine(
					sourceVmName,
					clonePaths.VmName,
					actualVirtualDiskPath,
					GetCloneExportRootFromVirtualDiskPath(actualVirtualDiskPath));
			}

			progressCallback(100, $"Clone Hyper-V System completed: {clonePaths.VmName}");
		}

		/// <summary>
		/// Executes an ExportHyperVSystem job.
		/// </summary>
		public static string ExecuteExportHyperVSystemJob(BackupJob job, BackupEngineInterop.ProgressCallback progressCallback)
		{
			ArgumentNullException.ThrowIfNull(job);
			ArgumentNullException.ThrowIfNull(progressCallback);

			if (job.HyperVMachines.Count == 0)
			{
				throw new InvalidOperationException("Export Hyper-V System requires a selected Hyper-V VM.");
			}

			CloneHyperVPaths clonePaths = CreateCloneHyperVPaths(job);
			string sourceVmName = job.HyperVMachines[0];

			progressCallback(1, $"Starting Hyper-V System export '{job.Name}'...");
			progressCallback(5, $"Exporting Hyper-V VM '{sourceVmName}'...");

			string exportedDiskPath = (CreateCloneHyperVVirtualDiskFromVmOverride ?? CreateCloneHyperVVirtualDiskFromVm)(
				sourceVmName,
				clonePaths,
				false,
				progressCallback);

			string exportRootPath = GetCloneExportRootFromVirtualDiskPath(exportedDiskPath);
			progressCallback(100, $"Hyper-V System export completed: {Path.GetFileName(exportRootPath)}");
			return clonePaths.RootDirectory;
		}

		// Helper methods
		private static CloneHyperVPaths CreateCloneHyperVPaths(BackupJob job)
		{
			ArgumentNullException.ThrowIfNull(job);

			if (string.IsNullOrWhiteSpace(job.DestinationPath))
			{
				throw new InvalidOperationException("Clone Hyper-V System requires a destination folder.");
			}

			bool renameRequested = job.RenameHyperVSystem && !string.IsNullOrWhiteSpace(job.RenameHyperVSystemName);
			bool diskClone = job.Target == BackupTarget.Disk && (job.SourcePaths?.Count > 0);
			string vmName = renameRequested
				? job.RenameHyperVSystemName!.Trim()
				: diskClone
					? Environment.MachineName
					: job.Name;
			string rootDirectoryName = renameRequested
				? vmName
				: job.Name;
			string rootDirectory = Path.Combine(job.DestinationPath, rootDirectoryName);

			Directory.CreateDirectory(rootDirectory);

			// VirtualDiskPath will be resolved after export to point to the export subdirectory's Virtual Hard Disks folder
			// For now, return a placeholder that will be updated after we know the export structure
			string virtualDiskPath = Path.Combine(rootDirectory, $"{vmName}.vhdx");
			return new CloneHyperVPaths(rootDirectory, rootDirectory, rootDirectory, virtualDiskPath, vmName);
		}

		private static void CreateCloneHyperVVirtualDiskFromDisk(BackupJob job, string virtualDiskPath, BackupEngineInterop.ProgressCallback progressCallback)
		{
			string temporaryArchiveDirectory = Path.Combine(Path.GetTempPath(), "SecureServerBackup", "CloneHyperVSystem", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(temporaryArchiveDirectory);

			try
			{
				string temporaryArchivePath = Path.Combine(temporaryArchiveDirectory, $"{job.Name}.ssb");
				string diskPath = job.SourcePaths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
					?? throw new InvalidOperationException("Clone Hyper-V System requires a selected source disk.");
				string diskNumStr = diskPath.Replace("\\\\?\\PHYSICALDRIVE", "").Replace("\\\\.\\PHYSICALDRIVE", "");
				if (!int.TryParse(diskNumStr, out int diskNum))
				{
					throw new InvalidOperationException($"Invalid disk path format: {diskPath}");
				}

				int result = BackupEngineInterop.BackupDisk(
					diskNum,
					temporaryArchivePath,
					job.IncludeSystemState,
					job.CompressData,
					null,
					0,
					progressCallback,
					null);

				if (result != 0)
				{
					var errorBuffer = new StringBuilder(4096);
					BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
					throw new InvalidOperationException($"Disk capture for Clone Hyper-V System failed: {errorBuffer}");
				}

				progressCallback(70, "Creating virtual disk from archive...");
				result = BackupEngineInterop.RestoreArchiveToVhdxAsDisk(temporaryArchivePath, virtualDiskPath, progressCallback);

				if (result != 0)
				{
					var errorBuffer = new StringBuilder(4096);
					BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
					throw new InvalidOperationException($"Virtual disk creation failed: {errorBuffer}");
				}
			}
			finally
			{
				try
				{
					if (Directory.Exists(temporaryArchiveDirectory))
					{
						Directory.Delete(temporaryArchiveDirectory, recursive: true);
					}
				}
				catch (Exception cleanupEx)
				{
					Debug.WriteLine($"Clone Hyper-V System temp cleanup warning: {cleanupEx.Message}");
				}
			}
		}

		private static string CreateCloneHyperVVirtualDiskFromVm(string sourceVmName, CloneHyperVPaths clonePaths, bool renameExportedArtifacts, BackupEngineInterop.ProgressCallback progressCallback)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(sourceVmName);
			ArgumentNullException.ThrowIfNull(clonePaths);

			PrepareCloneHyperVExportDirectory(clonePaths.RootDirectory);

			progressCallback(10, $"Exporting VM '{sourceVmName}'... (this may take several minutes)");

			// Start export in background and provide progress updates
			var exportTask = Task.Run(() => ExportHyperVVmWithPowerShell(sourceVmName, clonePaths.RootDirectory));
			int currentProgress = 10;
			while (!exportTask.Wait(TimeSpan.FromSeconds(5)))
			{
				currentProgress = Math.Min(currentProgress + 2, 35);
				progressCallback(currentProgress, $"Still exporting VM '{sourceVmName}'...");
			}

			if (exportTask.Exception != null)
			{
				throw exportTask.Exception.InnerException ?? exportTask.Exception;
			}

			progressCallback(38, "Export complete. Waiting for file system to stabilize...");
			System.Threading.Thread.Sleep(2000); // Give file system time to release locks

			if (renameExportedArtifacts)
			{
				progressCallback(40, "Renaming exported artifacts...");
				RenameCloneHyperVExportArtifacts(clonePaths.RootDirectory, sourceVmName, clonePaths.VmName);
			}

			progressCallback(50, "Finding virtual disk...");
			string sourceDiskPath = FindPrimaryHyperVVirtualDisk(clonePaths.RootDirectory);
			if (string.IsNullOrWhiteSpace(sourceDiskPath))
			{
				throw new InvalidOperationException("The exported Hyper-V VM did not contain a source virtual disk.");
			}

			progressCallback(60, "Keeping exported virtual disk chain unchanged...");
			return sourceDiskPath;
		}

		private static void PrepareCloneHyperVExportDirectory(string exportRootPath)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(exportRootPath);

			Directory.CreateDirectory(exportRootPath);

			foreach (string directory in Directory.EnumerateDirectories(exportRootPath))
			{
				Directory.Delete(directory, recursive: true);
			}

			foreach (string file in Directory.EnumerateFiles(exportRootPath))
			{
				File.Delete(file);
			}
		}

		private static void ExportHyperVVmWithPowerShell(string vmName, string exportRootPath)
		{
			Directory.CreateDirectory(exportRootPath);

			string escapedVmName = vmName.Replace("'", "''");
			string escapedExportPath = exportRootPath.Replace("'", "''");
			string script =
				"$ProgressPreference='SilentlyContinue'; $VerbosePreference='SilentlyContinue'; $WarningPreference='Continue'; " +
				"Import-Module Hyper-V -ErrorAction Stop; " +
				$"$vmName = '{escapedVmName}'; " +
				$"$exportPath = '{escapedExportPath}'; " +
				"try { Export-VM -Name $vmName -Path $exportPath -CaptureLiveState CaptureDataConsistentState -ErrorAction Stop | Out-Null } " +
				"catch { Export-VM -Name $vmName -Path $exportPath -ErrorAction Stop | Out-Null }; " +
				"Write-Output 'EXPORT_COMPLETE'";

			RunPowerShellScript(script);
		}

		private static void VerifyClonedHyperVVirtualMachine(
			string? sourceVmName,
			string cloneVmName,
			string cloneVirtualDiskPath,
			string cloneExportRootPath)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(cloneVmName);
			ArgumentException.ThrowIfNullOrWhiteSpace(cloneVirtualDiskPath);
			ArgumentException.ThrowIfNullOrWhiteSpace(cloneExportRootPath);

			var verifyParams = new BackupEngineInterop.HyperVVerifyParams
			{
				SourceVmName = string.IsNullOrWhiteSpace(sourceVmName) ? null : sourceVmName,
				CloneVmName = cloneVmName,
				SourceVhdxPath = null,
				CloneVhdxPath = cloneVirtualDiskPath,
				CloneExportPath = cloneExportRootPath,
				PerformBootTest = false,
				PerformChecksumVerify = false,
				BootTestTimeoutSec = 120
			};

			(int result, BackupEngineInterop.HyperVVerifyReport report) = VerifyHyperVCloneOverride is null
				? InvokeNativeHyperVCloneVerification(verifyParams)
				: VerifyHyperVCloneOverride(verifyParams);

			if (result == 0 && report.OverallPass)
			{
				return;
			}

			string failureDetail = string.IsNullOrWhiteSpace(report.FirstFailureDetail)
				? "The cloned Hyper-V system did not pass verification."
				: report.FirstFailureDetail.Trim();

			throw new InvalidOperationException($"Clone Hyper-V System verification failed: {failureDetail}");
		}

		private static string GetCloneExportRootFromVirtualDiskPath(string cloneVirtualDiskPath)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(cloneVirtualDiskPath);

			DirectoryInfo? virtualDiskDirectory = Directory.GetParent(cloneVirtualDiskPath);
			if (virtualDiskDirectory is null)
			{
				throw new InvalidOperationException("Failed to resolve the cloned virtual disk directory.");
			}

			DirectoryInfo? exportRootDirectory = virtualDiskDirectory.Parent;
			if (exportRootDirectory is null)
			{
				throw new InvalidOperationException("Failed to resolve the cloned Hyper-V export root directory.");
			}

			return exportRootDirectory.FullName;
		}

		private static (int Result, BackupEngineInterop.HyperVVerifyReport Report) InvokeNativeHyperVCloneVerification(BackupEngineInterop.HyperVVerifyParams verifyParams)
		{
			int result = BackupEngineInterop.VerifyHyperVClone(verifyParams, out BackupEngineInterop.HyperVVerifyReport report);
			return (result, report);
		}

		private static void RenameCloneHyperVExportArtifacts(string exportRootPath, string sourceVmName, string targetVmName)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(exportRootPath);
			ArgumentException.ThrowIfNullOrWhiteSpace(sourceVmName);
			ArgumentException.ThrowIfNullOrWhiteSpace(targetVmName);

			string normalizedSourceVmName = Regex.Replace(sourceVmName, @"[^a-zA-Z0-9_\-]", "");
			if (string.IsNullOrWhiteSpace(normalizedSourceVmName) ||
				string.Equals(normalizedSourceVmName, targetVmName, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			foreach (string filePath in Directory.EnumerateFiles(exportRootPath, "*", SearchOption.AllDirectories)
						 .OrderByDescending(path => path.Length))
			{
				RenameCloneHyperVExportPath(filePath, normalizedSourceVmName, targetVmName, isDirectory: false);
			}

			foreach (string directoryPath in Directory.EnumerateDirectories(exportRootPath, "*", SearchOption.AllDirectories)
						 .OrderByDescending(path => path.Length))
			{
				RenameCloneHyperVExportPath(directoryPath, normalizedSourceVmName, targetVmName, isDirectory: true);
			}
		}

		private static void RenameCloneHyperVExportPath(string path, string sourceVmName, string targetVmName, bool isDirectory)
		{
			string fileSystemEntryName = Path.GetFileName(path);
			if (string.IsNullOrWhiteSpace(fileSystemEntryName))
			{
				return;
			}

			string renamedEntryName = Regex.Replace(
				fileSystemEntryName,
				Regex.Escape(sourceVmName),
				targetVmName.Replace("$", "$$"),
				RegexOptions.IgnoreCase);

			if (string.Equals(fileSystemEntryName, renamedEntryName, StringComparison.Ordinal))
			{
				return;
			}

			string parentDirectory = Path.GetDirectoryName(path)
				?? throw new InvalidOperationException("Failed to resolve the parent directory for the exported Hyper-V artifact.");
			string targetPath = Path.Combine(parentDirectory, renamedEntryName);

			if (File.Exists(targetPath) || Directory.Exists(targetPath))
			{
				throw new InvalidOperationException($"The exported Hyper-V artifact '{targetPath}' already exists.");
			}

			// Retry rename operation with delays to handle locked files
			const int maxRetries = 3;
			for (int retry = 0; retry < maxRetries; retry++)
			{
				try
				{
					if (isDirectory)
					{
						Directory.Move(path, targetPath);
					}
					else
					{
						File.Move(path, targetPath);
					}
					return; // Success
				}
				catch (UnauthorizedAccessException) when (retry < maxRetries - 1)
				{
					// Wait and retry - file may still be locked
					System.Threading.Thread.Sleep(1000 * (retry + 1));
				}
				catch (IOException ex) when (ex.Message.Contains("being used by another process") && retry < maxRetries - 1)
				{
					// File locked by another process - wait and retry
					System.Threading.Thread.Sleep(1000 * (retry + 1));
				}
			}

			// Final attempt - let exception bubble up if it still fails
			if (isDirectory)
			{  
				Directory.Move(path, targetPath);
			}
			else
			{
				File.Move(path, targetPath);
			}
		}

		private static string FindPrimaryHyperVVirtualDisk(string exportRootPath)
		{
			if (!Directory.Exists(exportRootPath))
			{
				return string.Empty;
			}

			return Directory.EnumerateFiles(exportRootPath, "*.vhd*", SearchOption.AllDirectories)
				.Select(path => new FileInfo(path))
				.OrderByDescending(file => file.Length)
				.ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
				.Select(file => file.FullName)
				.FirstOrDefault() ?? string.Empty;
		}

		private static void CopyAndMergeHyperVVirtualDisk(string sourceDiskPath, string targetDiskPath)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(targetDiskPath) ?? throw new InvalidOperationException("Invalid Clone Hyper-V target disk path."));

			string escapedSourcePath = sourceDiskPath.Replace("'", "''");
			string escapedTargetPath = targetDiskPath.Replace("'", "''");

			// Build the merge chain: if source is .avhdx, merge through parent chain to final target
			string script =
				"$ErrorActionPreference='Stop'; " +
				$"$sourcePath = '{escapedSourcePath}'; " +
				$"$targetPath = '{escapedTargetPath}'; " +
				"$currentPath = $sourcePath; " +
				"$mergeChain = @(); " +
				// Build the chain of parent disks if source is differencing
				"while ($currentPath.ToLowerInvariant().EndsWith('.avhdx')) { " +
				"  $mergeChain += $currentPath; " +
				"  $vhd = Get-VHD -Path $currentPath -ErrorAction Stop; " +
				"  if ($vhd.ParentPath) { $currentPath = $vhd.ParentPath; } else { break; } " +
				"} " +
				// Merge from deepest parent to target in one step
				"if ($mergeChain.Count -gt 0) { " +
				"  Merge-VHD -Path $mergeChain[0] -DestinationPath $targetPath -Force -ErrorAction Stop | Out-Null; " +
				"} else { " +
				"  Copy-Item -Path $currentPath -Destination $targetPath -Force -ErrorAction Stop; " +
				"}";

			RunPowerShellScript(script);
		}

		private static void CleanupHyperVExportArtifacts(string exportRootPath, string preservedVhdxPath)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(exportRootPath);
			ArgumentException.ThrowIfNullOrWhiteSpace(preservedVhdxPath);

			if (!Directory.Exists(exportRootPath))
			{
				return;
			}

			string normalizedPreservedPath = Path.GetFullPath(preservedVhdxPath);

			// Delete all AVHDX differencing disks (snapshots)
			foreach (string avhdxPath in Directory.EnumerateFiles(exportRootPath, "*.avhdx", SearchOption.AllDirectories))
			{
				try
				{
					File.Delete(avhdxPath);
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"Warning: Could not delete temporary AVHDX file '{avhdxPath}': {ex.Message}");
				}
			}

			// Delete all VHDX files EXCEPT the final merged one
			foreach (string vhdxPath in Directory.EnumerateFiles(exportRootPath, "*.vhdx", SearchOption.AllDirectories))
			{
				string normalizedVhdxPath = Path.GetFullPath(vhdxPath);
				if (!string.Equals(normalizedVhdxPath, normalizedPreservedPath, StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						File.Delete(vhdxPath);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"Warning: Could not delete temporary VHDX file '{vhdxPath}': {ex.Message}");
					}
				}
			}

				// Delete VHD files (if any legacy format exists)
				foreach (string vhdPath in Directory.EnumerateFiles(exportRootPath, "*.vhd", SearchOption.AllDirectories))
				{
					try
					{
						File.Delete(vhdPath);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"Warning: Could not delete temporary VHD file '{vhdPath}': {ex.Message}");
					}
				}

				// Delete Snapshots directory if it exists
				string snapshotsPath = Path.Combine(exportRootPath, "Snapshots");
				if (Directory.Exists(snapshotsPath))
				{
					try
					{
						Directory.Delete(snapshotsPath, recursive: true);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"Warning: Could not delete Snapshots directory '{snapshotsPath}': {ex.Message}");
					}
				}

				// Delete Virtual Machines\Snapshots directory if it exists (nested structure from export)
				string vmSnapshotsPath = Path.Combine(exportRootPath, "Virtual Machines", "Snapshots");
				if (Directory.Exists(vmSnapshotsPath))
				{
					try
					{
						Directory.Delete(vmSnapshotsPath, recursive: true);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"Warning: Could not delete Virtual Machines\\Snapshots directory '{vmSnapshotsPath}': {ex.Message}");
					}
				}

				// NOTE: We do NOT delete the Virtual Hard Disks directory because it now contains
				// the final merged VHDX that Import-VM needs to reference from the .vmcx configuration
			}

		private static void CreateCloneHyperVVirtualMachine(string vmName, string vmStoragePath, string virtualDiskPath)
		{
			string script = BuildCreateVirtualMachineScript(vmName, vmStoragePath, virtualDiskPath);
			RunPowerShellScript(script);
		}

		private static void CreateCloneHyperVVirtualMachineFromExport(string vmName, string exportRootPath)
		{
			string script = BuildImportVirtualMachineFromExportScript(vmName, exportRootPath);
			RunPowerShellScript(script);
		}

		private static string BuildCreateVirtualMachineScript(string vmName, string vmStoragePath, string virtualDiskPath)
		{
			string escapedVmName = vmName.Replace("'", "''");
			string escapedVmStoragePath = vmStoragePath.Replace("'", "''");
			string escapedVirtualDiskPath = virtualDiskPath.Replace("'", "''");

			return
				"$ErrorActionPreference='Stop'; " +
				"Import-Module Hyper-V -ErrorAction Stop; " +
				$"$vmName = '{escapedVmName}'; " +
				$"$vmPath = '{escapedVmStoragePath}'; " +
				$"$vhdPath = '{escapedVirtualDiskPath}'; " +
				// Find .vmcx in the entire clone directory tree to read source VM settings
				"$vmcxPath = Get-ChildItem -Path $vmPath -Filter '*.vmcx' -Recurse -ErrorAction Stop | Select-Object -First 1 -ExpandProperty FullName; " +
				"if (-not $vmcxPath) { throw 'No .vmcx configuration file found in clone directory' }; " +
				// Import the original VM temporarily to read its configuration
				"$sourceVm = Import-VM -Path $vmcxPath -ErrorAction Stop; " +
				"$generation = $sourceVm.Generation; " +
				"$memoryStartupBytes = $sourceVm.MemoryStartup; " +
				"$processorCount = $sourceVm.ProcessorCount; " +
				// Get the first network adapter's switch name (if any)
				"$switchName = ($sourceVm | Get-VMNetworkAdapter -ErrorAction SilentlyContinue | Select-Object -First 1).SwitchName; " +
				// Remove the temporary imported VM without touching any files
				"$sourceVm | Remove-VM -Force -ErrorAction Stop; " +
				// Create a fresh new VM with the merged VHDX
				"$newVm = New-VM -Name $vmName -MemoryStartupBytes $memoryStartupBytes -Generation $generation -VHDPath $vhdPath -ErrorAction Stop; " +
				"$newVm | Set-VMProcessor -Count $processorCount -ErrorAction Stop; " +
				// Connect to the same virtual switch if one was found
				"if ($switchName) { $newVm | Get-VMNetworkAdapter | Connect-VMNetworkAdapter -SwitchName $switchName -ErrorAction SilentlyContinue }";
		}

		private static string BuildImportVirtualMachineFromExportScript(string vmName, string exportRootPath)
		{
			string escapedVmName = vmName.Replace("'", "''");
			string escapedExportRootPath = exportRootPath.Replace("'", "''");

			return
				"$ErrorActionPreference='Stop'; " +
				"$ProgressPreference='SilentlyContinue'; $VerbosePreference='SilentlyContinue'; $WarningPreference='SilentlyContinue'; " +
				"Import-Module Hyper-V -ErrorAction Stop; " +
				$"$vmName = '{escapedVmName}'; " +
				$"$exportRootPath = '{escapedExportRootPath}'; " +
				"$copyRootPath = Join-Path $exportRootPath '_ImportedVm'; " +
				"$copyVmPath = Join-Path $copyRootPath 'VirtualMachine'; " +
				"$copyVhdPath = Join-Path $copyRootPath 'VirtualHardDisks'; " +
				"$copySnapshotPath = Join-Path $copyRootPath 'Snapshots'; " +
				"$copyPagingPath = Join-Path $copyRootPath 'SmartPaging'; " +
				"New-Item -ItemType Directory -Path $copyVmPath -Force | Out-Null; " +
				"New-Item -ItemType Directory -Path $copyVhdPath -Force | Out-Null; " +
				"New-Item -ItemType Directory -Path $copySnapshotPath -Force | Out-Null; " +
				"New-Item -ItemType Directory -Path $copyPagingPath -Force | Out-Null; " +
				"$vmcxPath = Get-ChildItem -Path $exportRootPath -Filter '*.vmcx' -Recurse -ErrorAction Stop | Select-Object -First 1 -ExpandProperty FullName; " +
				"if (-not $vmcxPath) { throw 'No .vmcx configuration file found in clone directory'; }; " +
				"$importedVm = Import-VM -Path $vmcxPath -Copy -GenerateNewId -VirtualMachinePath $copyVmPath -VhdDestinationPath $copyVhdPath -SnapshotFilePath $copySnapshotPath -SmartPagingFilePath $copyPagingPath -ErrorAction Stop; " +
				"if ($importedVm.Name -ne $vmName) { Rename-VM -VM $importedVm -NewName $vmName -ErrorAction Stop | Out-Null; } " +
				"Get-VM -Name $vmName -ErrorAction Stop | Out-Null; " +
				"Write-Output 'IMPORT_COMPLETE'";
		}

		private static void ScheduleSetupClPendingRequest(string virtualDiskPath)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(virtualDiskPath);

			string escapedVirtualDiskPath = virtualDiskPath.Replace("'", "''");
			string script =
				"$ErrorActionPreference='Stop'; " +
				$"$vhdPath = '{escapedVirtualDiskPath}'; " +
				"$vhd = Mount-VHD -Path $vhdPath -Passthru -ErrorAction Stop; " +
				"try { " +
				"  $disk = $vhd | Get-Disk -ErrorAction Stop; " +
				"  $windowsPartition = Get-Partition -DiskNumber $disk.Number -ErrorAction Stop | Sort-Object PartitionNumber | ForEach-Object { $partition = $_; $accessPath = ($partition.AccessPaths | Where-Object { $_ -match '^[A-Z]:\\$' } | Select-Object -First 1); if ([string]::IsNullOrWhiteSpace($accessPath)) { $volume = $partition | Get-Volume -ErrorAction SilentlyContinue; if ($volume -and -not [string]::IsNullOrWhiteSpace($volume.DriveLetter)) { $accessPath = $volume.DriveLetter + ':\\'; } } if (-not [string]::IsNullOrWhiteSpace($accessPath) -and (Test-Path (Join-Path $accessPath 'Windows\\System32\\Config\\SYSTEM'))) { [PSCustomObject]@{ AccessPath = $accessPath; PartitionNumber = $partition.PartitionNumber } } } | Select-Object -First 1; " +
				"  if ($null -eq $windowsPartition) { throw 'The cloned virtual disk does not contain a Windows SYSTEM hive.'; } " +
				"  [Console]::WriteLine((Join-Path $windowsPartition.AccessPath 'Windows\\System32\\Config\\SYSTEM')); " +
				"} finally { Dismount-VHD -Path $vhdPath -ErrorAction SilentlyContinue | Out-Null; }";

			string systemHivePath = RunPowerShellScript(script)
				.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
				.LastOrDefault()?.Trim() ?? string.Empty;

			if (string.IsNullOrWhiteSpace(systemHivePath) || !File.Exists(systemHivePath))
			{
				throw new InvalidOperationException("The offline SYSTEM hive was not found on the cloned virtual disk.");
			}

			int result = BackupEngineInterop.ScheduleOfflineSystemSetupCl(systemHivePath);
			if (result != 0)
			{
				var errorBuffer = new StringBuilder(4096);
				BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
				throw new InvalidOperationException($"Failed to schedule SetupCl for the cloned Hyper-V system. {errorBuffer}".Trim());
			}
		}

		private static void RegenerateHyperVVirtualMachineMacAddress(string vmName)
		{
			string escapedVmName = vmName.Replace("'", "''");
			string script =
				"$ErrorActionPreference='Stop'; " +
				"Import-Module Hyper-V -ErrorAction Stop; " +
				$"$vmName = '{escapedVmName}'; " +
				"$vm = Get-VM -Name $vmName -ErrorAction Stop; " +
				"$adapters = Get-VMNetworkAdapter -VM $vm -ErrorAction Stop; " +
				"foreach ($adapter in $adapters) { " +
				"  $adapter | Set-VMNetworkAdapter -DynamicMacAddress -ErrorAction Stop; " +
				"}";

			RunPowerShellScript(script);
		}

		private static string RunPowerShellScript(string script)
		{
			using var process = Process.Start(new ProcessStartInfo
			{
				FileName = "powershell.exe",
				Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				CreateNoWindow = true
			}) ?? throw new InvalidOperationException("Failed to start the PowerShell process.");

			Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
			Task<string> errorTask = process.StandardError.ReadToEndAsync();

			process.WaitForExit();
			Task.WaitAll(outputTask, errorTask);

			string output = outputTask.Result;
			string errors = errorTask.Result;

			if (process.ExitCode != 0)
			{
				throw new InvalidOperationException($"PowerShell script failed. {errors}".Trim());
			}

			return output;
		}

		private static List<int> GetCurrentSystemDiskIndexes()
		{
			var systemDiskIndexes = new List<int>();

			try
			{
				using var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
				foreach (System.Management.ManagementObject os in searcher.Get())
				{
					string systemDrive = os["SystemDrive"]?.ToString() ?? "C:";
					systemDrive = systemDrive.TrimEnd('\\');

					using var diskSearcher = new System.Management.ManagementObjectSearcher(
						$"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{systemDrive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

					foreach (System.Management.ManagementObject partition in diskSearcher.Get())
					{
						using var physicalDiskSearcher = new System.Management.ManagementObjectSearcher(
							$"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

						foreach (System.Management.ManagementObject disk in physicalDiskSearcher.Get())
						{
							if (disk["Index"] != null && int.TryParse(disk["Index"].ToString(), out int diskIndex))
							{
								systemDiskIndexes.Add(diskIndex);
							}
						}
					}
				}
			}
			catch
			{
				// If WMI query fails, return empty list
			}

			return systemDiskIndexes;
		}
	}

	/// <summary>
	/// Represents the directory structure for a Hyper-V clone operation.
	/// </summary>
	public record CloneHyperVPaths(
		string RootDirectory,
		string HyperVSystemDirectory,
		string HyperVDiskDirectory,
		string VirtualDiskPath,
		string VmName);
}
