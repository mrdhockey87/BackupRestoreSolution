using System;
using System.Collections.Generic;
using System.IO;
using SecureServerBackupCommon;
using Xunit;

namespace SecureServerBackupService.Tests;

public sealed class CloneExecutionHelperVerificationTests : IDisposable
{
	private readonly string _tempDirectory;

	public CloneExecutionHelperVerificationTests()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), "SecureServerBackupTests", nameof(CloneExecutionHelperVerificationTests), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDirectory);
		CloneExecutionHelper.ResetTestOverrides();
	}

	[Fact]
	public void ExecuteExportHyperVSystemJob_WhenVmSourceSelected_ExportsWithoutImportOrVerification()
	{
		BackupJob job = new()
		{
			Name = "ExportJob",
			DestinationPath = _tempDirectory,
			Type = BackupType.ExportHyperVSystem
		};
		job.HyperVMachines.Add("SourceVm");

		string exportedDiskPath = Path.Combine(_tempDirectory, "ExportJob", "SourceVm", "Virtual Hard Disks", "SourceVm.vhdx");
		Directory.CreateDirectory(Path.GetDirectoryName(exportedDiskPath)!);
		File.WriteAllText(exportedDiskPath, string.Empty);

		var progressMessages = new List<string>();
		int importInvocationCount = 0;
		int setupClInvocationCount = 0;
		int macResetInvocationCount = 0;
		int verifyInvocationCount = 0;

		CloneExecutionHelper.CreateCloneHyperVVirtualDiskFromVmOverride = (_, clonePaths, renameArtifacts, _) =>
		{
			Assert.False(renameArtifacts);
			Assert.Equal(Path.Combine(_tempDirectory, "ExportJob"), clonePaths.RootDirectory);
			return exportedDiskPath;
		};
		CloneExecutionHelper.CreateCloneHyperVVirtualMachineFromExportOverride = (_, _) => importInvocationCount++;
		CloneExecutionHelper.ScheduleSetupClPendingRequestOverride = _ => setupClInvocationCount++;
		CloneExecutionHelper.RegenerateHyperVVirtualMachineMacAddressOverride = _ => macResetInvocationCount++;
		CloneExecutionHelper.VerifyHyperVCloneOverride = parameters =>
		{
			verifyInvocationCount++;
			return (0, CreatePassingReport());
		};

		string exportPath = CloneExecutionHelper.ExecuteExportHyperVSystemJob(job, (percentage, message) => progressMessages.Add($"{percentage}:{message}"));

		Assert.Equal(Path.Combine(_tempDirectory, "ExportJob"), exportPath);
		Assert.Equal(0, importInvocationCount);
		Assert.Equal(0, setupClInvocationCount);
		Assert.Equal(0, macResetInvocationCount);
		Assert.Equal(0, verifyInvocationCount);
		Assert.Contains(progressMessages, message => message.Contains("Starting Hyper-V System export 'ExportJob'", StringComparison.Ordinal));
		Assert.Contains(progressMessages, message => message.Contains("Hyper-V System export completed", StringComparison.Ordinal));
	}

	[Fact]
	public void ExecuteCloneHyperVSystemJob_WhenVmSourceCloneVerificationRuns_PassesExportedDiskPathExportRootAndTargetVmName()
	{
		BackupJob job = new()
		{
			Name = "CloneJob",
			DestinationPath = _tempDirectory,
			RenameHyperVSystem = true,
			RenameHyperVSystemName = "RenamedClone"
		};
		job.HyperVMachines.Add("SourceVm");

		string exportedDiskPath = Path.Combine(_tempDirectory, "RenamedClone", "Win10OEM", "Virtual Hard Disks", "SourceVm.avhdx");
		Directory.CreateDirectory(Path.GetDirectoryName(exportedDiskPath)!);
		File.WriteAllText(exportedDiskPath, string.Empty);

		var progressMessages = new List<string>();
		BackupEngineInterop.HyperVVerifyParams? capturedVerifyParams = null;
		int importInvocationCount = 0;
		int setupClInvocationCount = 0;
		int macResetInvocationCount = 0;

		CloneExecutionHelper.CreateCloneHyperVVirtualDiskFromVmOverride = (_, clonePaths, _, _) => exportedDiskPath;
		CloneExecutionHelper.CreateCloneHyperVVirtualMachineFromExportOverride = (vmName, exportRoot) =>
		{
			importInvocationCount++;
			Assert.Equal("RenamedClone", vmName);
			Assert.Equal(Path.Combine(_tempDirectory, "RenamedClone"), exportRoot);
		};
		CloneExecutionHelper.ScheduleSetupClPendingRequestOverride = virtualDiskPath =>
		{
			setupClInvocationCount++;
			Assert.Equal(exportedDiskPath, virtualDiskPath);
		};
		CloneExecutionHelper.RegenerateHyperVVirtualMachineMacAddressOverride = vmName =>
		{
			macResetInvocationCount++;
			Assert.Equal("RenamedClone", vmName);
		};
		CloneExecutionHelper.VerifyHyperVCloneOverride = parameters =>
		{
			capturedVerifyParams = parameters;
			return (0, CreatePassingReport());
		};

		CloneExecutionHelper.ExecuteCloneHyperVSystemJob(job, (percentage, message) => progressMessages.Add($"{percentage}:{message}"));

		Assert.Equal(1, importInvocationCount);
		Assert.Equal(1, setupClInvocationCount);
		Assert.Equal(1, macResetInvocationCount);
		Assert.NotNull(capturedVerifyParams);
		Assert.Equal("SourceVm", capturedVerifyParams.Value.SourceVmName);
		Assert.Equal("RenamedClone", capturedVerifyParams.Value.CloneVmName);
		Assert.Equal(exportedDiskPath, capturedVerifyParams.Value.CloneVhdxPath);
		Assert.Equal(Path.Combine(_tempDirectory, "RenamedClone", "Win10OEM"), capturedVerifyParams.Value.CloneExportPath);
		Assert.Contains(progressMessages, message => message.Contains("Verifying cloned Hyper-V VM 'RenamedClone'", StringComparison.Ordinal));
	}

	[Fact]
	public void ExecuteCloneHyperVSystemJob_WhenVmSourceCloneVerificationFails_ThrowsFirstFailureDetail()
	{
		BackupJob job = new()
		{
			Name = "CloneJob",
			DestinationPath = _tempDirectory
		};
		job.HyperVMachines.Add("SourceVm");

		string exportedDiskPath = Path.Combine(_tempDirectory, "CloneJob", "ExportedVm", "Virtual Hard Disks", "SourceVm.vhdx");
		Directory.CreateDirectory(Path.GetDirectoryName(exportedDiskPath)!);
		File.WriteAllText(exportedDiskPath, string.Empty);

		CloneExecutionHelper.CreateCloneHyperVVirtualDiskFromVmOverride = (_, _, _, _) => exportedDiskPath;
		CloneExecutionHelper.CreateCloneHyperVVirtualMachineFromExportOverride = (_, _) => { };
		CloneExecutionHelper.VerifyHyperVCloneOverride = _ =>
		{
			BackupEngineInterop.HyperVVerifyReport report = CreatePassingReport();
			report.OverallPass = false;
			report.FirstFailureDetail = "Parent locator present but parent file not found";
			return (-1, report);
		};

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
			CloneExecutionHelper.ExecuteCloneHyperVSystemJob(job, (_, _) => { }));

		Assert.Contains("Clone Hyper-V System verification failed", ex.Message, StringComparison.Ordinal);
		Assert.Contains("Parent locator present but parent file not found", ex.Message, StringComparison.Ordinal);
	}

	public void Dispose()
	{
		CloneExecutionHelper.ResetTestOverrides();

		if (Directory.Exists(_tempDirectory))
		{
			Directory.Delete(_tempDirectory, recursive: true);
		}
	}

	private static BackupEngineInterop.HyperVVerifyReport CreatePassingReport()
		=> new()
		{
			OverallPass = true,
			Checks = new BackupEngineInterop.HyperVVerifyCheckResult[32],
			SourceChecksum = string.Empty,
			CloneChecksum = string.Empty,
			FirstFailureDetail = string.Empty
		};
}
