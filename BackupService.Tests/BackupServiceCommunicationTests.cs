using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using SecureServerBackupCommon;
using SecureServerBackupService;
using Xunit;

namespace SecureServerBackupService.Tests;

public sealed class BackupServiceCommunicationTests
{
    [Fact]
    public void IsExpectedPipeDisconnect_WhenBrokenPipeIOException_ReturnsTrue()
    {
        IOException exception = CreateIOException("Pipe is broken.", unchecked((int)0x8007006D));

        bool result = InvokeIsExpectedPipeDisconnect(exception);

        Assert.True(result);
    }

    [Fact]
    public void IsExpectedPipeDisconnect_WhenUnexpectedIOException_ReturnsFalse()
    {
        IOException exception = CreateIOException("Access denied.", unchecked((int)0x80070005));

        bool result = InvokeIsExpectedPipeDisconnect(exception);

        Assert.False(result);
    }

    [Fact]
    public void ProcessMessage_WithRunBackup_RaisesCommandReceived()
    {
        BackupServiceCommunication service = new();
        Guid expectedJobId = Guid.NewGuid();
        Guid receivedJobId = Guid.Empty;

        service.CommandReceived += (_, args) => receivedJobId = args.JobId;

        string response = InvokeProcessMessage(service, new ServiceCommand
        {
            CommandType = "RunBackup",
            Data = expectedJobId.ToString()
        });

        Assert.Equal(expectedJobId, receivedJobId);
        Assert.True(ReadSuccess(response));
    }

    [Fact]
    public void ProcessMessage_WithAbortBackup_RaisesAbortCommand()
    {
        BackupServiceCommunication service = new();
        bool wasAbort = false;

        service.CommandReceived += (_, args) => wasAbort = args.IsAbort;

        string response = InvokeProcessMessage(service, new ServiceCommand
        {
            CommandType = "AbortBackup",
            Data = Guid.NewGuid().ToString()
        });

        Assert.True(wasAbort);
        Assert.True(ReadSuccess(response));
    }

    [Fact]
    public void ProcessMessage_WithGetProgress_ReturnsSerializedProgress()
    {
        BackupServiceCommunication service = new();
        Guid jobId = Guid.NewGuid();

        service.ProgressQueried += (_, args) =>
        {
            args.Progress = new BackupProgress
            {
                JobId = jobId,
                IsRunning = true,
                Percentage = 40,
                Message = "Working"
            };
        };

        string response = InvokeProcessMessage(service, new ServiceCommand
        {
            CommandType = "GetProgress",
            Data = jobId.ToString()
        });

        BackupProgress? progress = JsonSerializer.Deserialize<BackupProgress>(response);
        Assert.NotNull(progress);
        Assert.Equal(jobId, progress.JobId);
        Assert.Equal(40, progress.Percentage);
    }

    [Fact]
    public void ProcessMessage_WithUnknownCommand_ReturnsFailure()
    {
        BackupServiceCommunication service = new();

        string response = InvokeProcessMessage(service, new ServiceCommand
        {
            CommandType = "Unknown"
        });

        Assert.False(ReadSuccess(response));
    }

    [Fact]
    public async Task StartAsync_WithClientCommand_RaisesRunBackupEvent()
    {
        string pipeName = $"BackupServiceCommunicationTests-{Guid.NewGuid():N}";
        using BackupServiceCommunication service = new(pipeName);
        Guid expectedJobId = Guid.NewGuid();
        Guid receivedJobId = Guid.Empty;

        service.CommandReceived += (_, args) => receivedJobId = args.JobId;

        await service.StartAsync(default);

        SecureServerBackup.Services.BackupServiceClient client = new(pipeName);
        bool result = await client.RunBackupNowAsync(expectedJobId);

        Assert.True(result);
        Assert.Equal(expectedJobId, receivedJobId);

        await service.StopAsync(default);
    }

    [Fact]
    public async Task StartAsync_WithProgressQuery_ReturnsSerializedProgress()
    {
        string pipeName = $"BackupServiceCommunicationTests-{Guid.NewGuid():N}";
        using BackupServiceCommunication service = new(pipeName);
        Guid jobId = Guid.NewGuid();

        service.ProgressQueried += (_, args) =>
        {
            args.Progress = new BackupProgress
            {
                JobId = jobId,
                IsRunning = true,
                Percentage = 55,
                Message = "Querying"
            };
        };

        await service.StartAsync(default);

        SecureServerBackup.Services.BackupServiceClient client = new(pipeName);
        BackupProgress? progress = await client.GetProgressAsync(jobId);

        Assert.NotNull(progress);
        Assert.Equal(55, progress.Percentage);
        Assert.Equal("Querying", progress.Message);

        await service.StopAsync(default);
    }

    [Fact]
    public void ProcessMessage_WithInvalidJson_ReturnsFailure()
    {
        BackupServiceCommunication service = new();
        MethodInfo method = typeof(BackupServiceCommunication).GetMethod("ProcessMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;

        string response = (string)method.Invoke(service, new object[] { "not-json" })!;

        Assert.False(ReadSuccess(response));
    }

    private static string InvokeProcessMessage(BackupServiceCommunication service, ServiceCommand command)
    {
        MethodInfo method = typeof(BackupServiceCommunication).GetMethod("ProcessMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (string)method.Invoke(service, new object[] { JsonSerializer.Serialize(command) })!;
    }

    private static bool ReadSuccess(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("Success").GetBoolean();
    }

    private static bool InvokeIsExpectedPipeDisconnect(IOException exception)
    {
        MethodInfo method = typeof(BackupServiceCommunication).GetMethod("IsExpectedPipeDisconnect", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, new object[] { exception })!;
    }

    private static IOException CreateIOException(string message, int hResult)
    {
        IOException exception = new(message);
        typeof(Exception).GetProperty("HResult", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(exception, hResult);
        return exception;
    }
}
