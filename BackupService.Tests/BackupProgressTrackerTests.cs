using System;
using System.Threading;
using SecureServerBackupService;
using Xunit;

namespace SecureServerBackupService.Tests;

public sealed class BackupProgressTrackerTests
{
    [Fact]
    public void StartJob_ThenGetProgress_ReturnsRunningState()
    {
        BackupProgressTracker tracker = new();
        Guid jobId = Guid.NewGuid();

        tracker.StartJob(jobId);
        var progress = tracker.GetProgress(jobId);

        Assert.NotNull(progress);
        Assert.True(progress.IsRunning);
        Assert.Equal(0, progress.Percentage);
        Assert.Equal("Starting backup...", progress.Message);
    }

    [Fact]
    public void StartVerification_UpdatesVerificationState()
    {
        BackupProgressTracker tracker = new();
        Guid jobId = Guid.NewGuid();
        tracker.StartJob(jobId);

        tracker.StartVerification(jobId);
        var progress = tracker.GetProgress(jobId);

        Assert.NotNull(progress);
        Assert.True(progress.IsVerifying);
        Assert.Equal("Starting verification...", progress.Message);
    }

    [Fact]
    public void UpdateProgress_WithFileMessage_StoresCurrentFile()
    {
        BackupProgressTracker tracker = new();
        Guid jobId = Guid.NewGuid();
        tracker.StartJob(jobId);

        tracker.UpdateProgress(jobId, 25, "Backing up: C:\\data\\file.txt");
        var progress = tracker.GetProgress(jobId);

        Assert.NotNull(progress);
        Assert.Equal("Backing up: C:\\data\\file.txt", progress.CurrentFile);
    }

    [Fact]
    public void UpdateProgress_WithGeneralMessage_ClearsCurrentFile()
    {
        BackupProgressTracker tracker = new();
        Guid jobId = Guid.NewGuid();
        tracker.StartJob(jobId);
        tracker.UpdateProgress(jobId, 25, "Backing up: C:\\data\\file.txt");

        tracker.UpdateProgress(jobId, 50, "Phase changed");
        var progress = tracker.GetProgress(jobId);

        Assert.NotNull(progress);
        Assert.Equal("Phase changed", progress.Message);
        Assert.Equal(string.Empty, progress.CurrentFile);
    }

    [Fact]
    public void CompleteJob_WithSuccess_MarksJobCompleted()
    {
        BackupProgressTracker tracker = new();
        Guid jobId = Guid.NewGuid();
        tracker.StartJob(jobId);

        tracker.CompleteJob(jobId, true);
        var progress = tracker.GetProgress(jobId);

        Assert.NotNull(progress);
        Assert.False(progress.IsRunning);
        Assert.True(progress.Success);
        Assert.Equal(100, progress.Percentage);
    }

    [Fact]
    public void CompleteJob_AfterVerificationSuccess_NormalizesCompletedState()
    {
        BackupProgressTracker tracker = new();
        Guid jobId = Guid.NewGuid();
        tracker.StartJob(jobId);
        tracker.StartVerification(jobId);
        tracker.UpdateProgress(jobId, 50, "Processing: WDrive");

        tracker.CompleteJob(jobId, true);
        var progress = tracker.GetProgress(jobId);

        Assert.NotNull(progress);
        Assert.False(progress.IsRunning);
        Assert.False(progress.IsVerifying);
        Assert.True(progress.Success);
        Assert.Equal(100, progress.Percentage);
        Assert.Equal("Backup completed successfully!", progress.Message);
        Assert.Equal(string.Empty, progress.CurrentFile);
    }

    [Fact]
    public void RequestCancellation_CancelsJobToken()
    {
        BackupProgressTracker tracker = new();
        Guid jobId = Guid.NewGuid();
        tracker.StartJob(jobId);

        tracker.RequestCancellation(jobId);
        CancellationToken token = tracker.GetCancellationToken(jobId);

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void GetCancellationToken_ForUnknownJob_ReturnsNone()
    {
        BackupProgressTracker tracker = new();

        CancellationToken token = tracker.GetCancellationToken(Guid.NewGuid());

        Assert.Equal(CancellationToken.None, token);
    }
}
