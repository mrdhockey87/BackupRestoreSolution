using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BackupService
{
    /// <summary>
    /// Handles communication between BackupService and UI via Named Pipes
    /// </summary>
    public class BackupServiceCommunication : IDisposable
    {
        private const string PipeName = "BackupRestoreServicePipe";
        private NamedPipeServerStream? _pipeServer;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _listenTask;

        public event EventHandler<BackupCommandEventArgs>? CommandReceived;
        public event EventHandler<ProgressQueryEventArgs>? ProgressQueried;

        public void Start()
        {
            _listenTask = Task.Run(ListenForConnectionsAsync);
        }

        private async Task ListenForConnectionsAsync()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    _pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous);

                    await _pipeServer.WaitForConnectionAsync(_cancellationTokenSource.Token);

                    _ = Task.Run(() => HandleClientAsync(_pipeServer), _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Named pipe error: {ex.Message}");
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream pipeServer)
        {
            try
            {
                using var reader = new StreamReader(pipeServer, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipeServer, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                while (pipeServer.IsConnected)
                {
                    var message = await reader.ReadLineAsync();
                    if (message == null)
                        break;

                    var response = ProcessMessage(message);
                    await writer.WriteLineAsync(response);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Client handling error: {ex.Message}");
            }
            finally
            {
                pipeServer.Dispose();
            }
        }

        private string ProcessMessage(string message)
        {
            try
            {
                var command = JsonSerializer.Deserialize<ServiceCommand>(message);
                if (command == null)
                    return CreateResponse(false, "Invalid command");

                switch (command.CommandType)
                {
                    case "RunBackup":
                        var jobId = Guid.Parse(command.Data ?? "");
                        CommandReceived?.Invoke(this, new BackupCommandEventArgs { JobId = jobId });
                        return CreateResponse(true, "Backup started");

                    case "AbortBackup":
                        var abortJobId = Guid.Parse(command.Data ?? "");
                        CommandReceived?.Invoke(this, new BackupCommandEventArgs 
                        { 
                            JobId = abortJobId, 
                            IsAbort = true 
                        });
                        return CreateResponse(true, "Abort requested");

                    case "GetProgress":
                        var progressJobId = Guid.Parse(command.Data ?? "");
                        var args = new ProgressQueryEventArgs { JobId = progressJobId };
                        ProgressQueried?.Invoke(this, args);
                        return JsonSerializer.Serialize(args.Progress);

                    default:
                        return CreateResponse(false, "Unknown command");
                }
            }
            catch (Exception ex)
            {
                return CreateResponse(false, $"Error: {ex.Message}");
            }
        }

        private string CreateResponse(bool success, string message)
        {
            return JsonSerializer.Serialize(new { Success = success, Message = message });
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _pipeServer?.Dispose();
            _cancellationTokenSource.Dispose();
        }
    }

    public class ServiceCommand
    {
        public string CommandType { get; set; } = "";
        public string? Data { get; set; }
    }

    public class BackupCommandEventArgs : EventArgs
    {
        public Guid JobId { get; set; }
        public bool IsAbort { get; set; }
    }

    public class ProgressQueryEventArgs : EventArgs
    {
        public Guid JobId { get; set; }
        public BackupProgress? Progress { get; set; }
    }

    public class BackupProgress
    {
        public Guid JobId { get; set; }
        public bool IsRunning { get; set; }
        public int Percentage { get; set; }
        public string Message { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
