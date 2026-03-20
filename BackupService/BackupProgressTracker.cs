using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BackupCommon;  // v6.0.1.19: Reference shared BackupProgress DTO

namespace BackupService
{
    /// <summary>
    /// Tracks progress and state of running backup jobs
    /// </summary>
    public class BackupProgressTracker
    {
        private readonly ConcurrentDictionary<Guid, BackupJobState> _runningJobs = new();
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationTokens = new();

        public void StartJob(Guid jobId)
        {
            var state = new BackupJobState
            {
                JobId = jobId,
                IsRunning = true,
                Percentage = 0,
                Message = "Starting backup...",
                StartTime = DateTime.Now
            };

            _runningJobs[jobId] = state;
            _cancellationTokens[jobId] = new CancellationTokenSource();
        }

        public void UpdateProgress(Guid jobId, int percentage, string message)
        {
            if (_runningJobs.TryGetValue(jobId, out var state))
            {
                state.Percentage = percentage;
                state.LastUpdate = DateTime.Now;

                // Parse message to distinguish file-level vs general progress
                // File-level messages contain "Backing up:" or "Processing:"
                if (message.Contains("Backing up:") || message.Contains("Processing:"))
                {
                    // This is a file-level message - store in CurrentFile
                    state.CurrentFile = message;
                }
                else
                {
                    // This is a general progress message - store in Message
                    state.Message = message;
                    // Clear current file when general message arrives (new phase)
                    if (!message.Contains("Capturing files") && !message.Contains("Mounting"))
                    {
                        state.CurrentFile = "";
                    }
                }
            }
        }

        public void CompleteJob(Guid jobId, bool success, string? errorMessage = null)
        {
            if (_runningJobs.TryGetValue(jobId, out var state))
            {
                state.IsRunning = false;
                state.Success = success;
                state.ErrorMessage = errorMessage;
                state.EndTime = DateTime.Now;
                state.Percentage = success ? 100 : state.Percentage;

                // Keep completed jobs in memory for 10 minutes for UI to query
                _ = Task.Delay(TimeSpan.FromMinutes(10))
                    .ContinueWith(_ =>
                    {
                        _runningJobs.TryRemove(jobId, out BackupJobState? _);
                        if (_cancellationTokens.TryRemove(jobId, out var cts))
                        {
                            cts.Dispose();
                        }
                    });
            }
        }

        public BackupProgress? GetProgress(Guid jobId)
        {
            if (_runningJobs.TryGetValue(jobId, out var state))
            {
                return new BackupProgress
                {
                    JobId = jobId,
                    IsRunning = state.IsRunning,
                    Percentage = state.Percentage,
                    Message = state.Message,
                    CurrentFile = state.CurrentFile,  // NEW: Include current file in progress
                    Success = state.Success,
                    ErrorMessage = state.ErrorMessage
                };
            }

            return null;
        }

        public bool IsJobRunning(Guid jobId)
        {
            return _runningJobs.TryGetValue(jobId, out var state) && state.IsRunning;
        }

        public CancellationToken GetCancellationToken(Guid jobId)
        {
            if (_cancellationTokens.TryGetValue(jobId, out var cts))
            {
                return cts.Token;
            }

            return CancellationToken.None;
        }

        public void RequestCancellation(Guid jobId)
        {
            if (_cancellationTokens.TryGetValue(jobId, out var cts))
            {
                cts.Cancel();
            }
        }

        private class BackupJobState
        {
            public Guid JobId { get; set; }
            public bool IsRunning { get; set; }
            public int Percentage { get; set; }
            public string Message { get; set; } = "";
            public string CurrentFile { get; set; } = "";  // NEW: Track current file being backed up
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime LastUpdate { get; set; }
            public DateTime? EndTime { get; set; }
        }
    }
}
