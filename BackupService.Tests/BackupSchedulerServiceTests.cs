using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SecureServerBackupCommon;
using SecureServerBackupService;
using Xunit;

namespace SecureServerBackupService.Tests;

public sealed class BackupSchedulerServiceTests
{
	[Fact]
	public void LogJobMessage_WhenFilenameContainsError_LogsInfo()
	{
		string jobName = $"BackupSchedulerServiceTests_{Guid.NewGuid():N}";
		BackupJob job = new()
		{
			Name = jobName,
			DestinationPath = Path.GetTempPath()
		};

		try
		{
			InvokeLogJobMessage(job, @"Backing up file: C:\Data\error-report.txt");

			BackupLogEntry entry = Assert.Single(BackupLogger.GetLogsByJob(jobName), log => log.Message == @"Backing up file: C:\Data\error-report.txt");
			Assert.Equal(BackupLogLevel.Info, entry.Level);
		}
		finally
		{
			DeleteJobLog(jobName);
		}
	}

	[Fact]
	public void LogJobMessage_WhenMessageHasErrorPrefix_LogsError()
	{
		string jobName = $"BackupSchedulerServiceTests_{Guid.NewGuid():N}";
		BackupJob job = new()
		{
			Name = jobName,
			DestinationPath = Path.GetTempPath()
		};

		try
		{
			InvokeLogJobMessage(job, "[ERROR] Backup failed with code -5");

			BackupLogEntry entry = Assert.Single(BackupLogger.GetLogsByJob(jobName), log => log.Message == "[ERROR] Backup failed with code -5");
			Assert.Equal(BackupLogLevel.Error, entry.Level);
		}
		finally
		{
			DeleteJobLog(jobName);
		}
	}

	[Fact]
	public void LogJobMessage_WhenMessageHasWarningPrefix_LogsWarning()
	{
		string jobName = $"BackupSchedulerServiceTests_{Guid.NewGuid():N}";
		BackupJob job = new()
		{
			Name = jobName,
			DestinationPath = Path.GetTempPath()
		};

		try
		{
			InvokeLogJobMessage(job, "Warning: Could not restore process priority");

			BackupLogEntry entry = Assert.Single(BackupLogger.GetLogsByJob(jobName), log => log.Message == "Warning: Could not restore process priority");
			Assert.Equal(BackupLogLevel.Warning, entry.Level);
		}
		finally
		{
			DeleteJobLog(jobName);
		}
	}

	private static void DeleteJobLog(string jobName)
	{
		string logDirectory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
			"SecureServerBackupService",
			"Logs");
		string safeJobName = new string(jobName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
		string jobLogPath = Path.Combine(logDirectory, $"{safeJobName}.json");

		if (File.Exists(jobLogPath))
		{
			File.Delete(jobLogPath);
		}
	}

	private static void InvokeLogJobMessage(BackupJob job, string message)
	{
		MethodInfo method = typeof(BackupSchedulerService).GetMethod("LogJobMessage", BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new InvalidOperationException("LogJobMessage method was not found.");

		method.Invoke(null, [job, message, string.Empty]);
	}
}
