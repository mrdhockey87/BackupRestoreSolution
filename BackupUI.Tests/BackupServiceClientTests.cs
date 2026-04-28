using System;
using System.Threading.Tasks;
using SecureServerBackup.Services;
using SecureServerBackupCommon;
using SecureServerBackupService;
using Xunit;

namespace SecureServerBackup.Tests;

public sealed class BackupServiceClientTests
{
    [Fact]
    public async Task RunBackupNowAsync_WhenServiceResponds_ReturnsTrue()
    {
        string pipeName = $"BackupServiceClientTests-{Guid.NewGuid():N}";
        using BackupServiceCommunication service = new(pipeName);
        BackupServiceClient client = new(pipeName);
        Guid jobId = Guid.NewGuid();
        Guid receivedJobId = Guid.Empty;

        service.CommandReceived += (_, args) => receivedJobId = args.JobId;
        await service.StartAsync(default);

        bool result = await client.RunBackupNowAsync(jobId);

        Assert.True(result);
        Assert.Equal(jobId, receivedJobId);

        await service.StopAsync(default);
    }

    [Fact]
    public async Task AbortBackupAsync_WhenServiceResponds_RaisesAbortCommand()
    {
        string pipeName = $"BackupServiceClientTests-{Guid.NewGuid():N}";
        using BackupServiceCommunication service = new(pipeName);
        BackupServiceClient client = new(pipeName);
        bool wasAbort = false;

        service.CommandReceived += (_, args) => wasAbort = args.IsAbort;
        await service.StartAsync(default);

        bool result = await client.AbortBackupAsync(Guid.NewGuid());

        Assert.True(result);
        Assert.True(wasAbort);

        await service.StopAsync(default);
    }

    [Fact]
    public async Task GetProgressAsync_WhenServiceProvidesProgress_ReturnsPayload()
    {
        string pipeName = $"BackupServiceClientTests-{Guid.NewGuid():N}";
        using BackupServiceCommunication service = new(pipeName);
        BackupServiceClient client = new(pipeName);
        Guid jobId = Guid.NewGuid();

        service.ProgressQueried += (_, args) =>
        {
            args.Progress = new BackupProgress
            {
                JobId = jobId,
                IsRunning = true,
                Percentage = 77,
                Message = "Restoring"
            };
        };

        await service.StartAsync(default);

        BackupProgress? progress = await client.GetProgressAsync(jobId);

        Assert.NotNull(progress);
        Assert.Equal(jobId, progress.JobId);
        Assert.Equal(77, progress.Percentage);
        Assert.Equal("Restoring", progress.Message);

        await service.StopAsync(default);
    }

    [Fact]
    public async Task GetServiceVersionAsync_WhenServiceResponds_ReturnsVersionText()
    {
        string pipeName = $"BackupServiceClientTests-{Guid.NewGuid():N}";
        using BackupServiceCommunication service = new(pipeName);
        BackupServiceClient client = new(pipeName);

        await service.StartAsync(default);

        string? version = await client.GetServiceVersionAsync();

        Assert.False(string.IsNullOrWhiteSpace(version));

        await service.StopAsync(default);
    }

    [Fact]
    public async Task RunBackupNowAsync_WhenServiceIsUnavailable_ReturnsFalse()
    {
        BackupServiceClient client = new($"BackupServiceClientTests-{Guid.NewGuid():N}");

        bool result = await client.RunBackupNowAsync(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task GetProgressAsync_WhenServiceIsUnavailable_ReturnsNull()
    {
        BackupServiceClient client = new($"BackupServiceClientTests-{Guid.NewGuid():N}");

        BackupProgress? progress = await client.GetProgressAsync(Guid.NewGuid());

        Assert.Null(progress);
    }
}
