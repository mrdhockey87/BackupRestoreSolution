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

		string exportedDiskPath = Path.Combine(_tempDirectory, "RenamedClone", "Virtual Hard Disks", "SourceVm.avhdx");
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
		Assert.Equal(Path.Combine(_tempDirectory, "RenamedClone"), capturedVerifyParams.Value.CloneExportPath);
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

		string exportedDiskPath = Path.Combine(_tempDirectory, "CloneJob", "Virtual Hard Disks", "SourceVm.vhdx");
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
