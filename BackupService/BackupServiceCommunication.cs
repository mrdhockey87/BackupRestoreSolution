using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace BackupService
{
    /// <summary>
    /// Handles communication between BackupService and UI via Named Pipes
    /// Implements IHostedService to automatically start when service starts
    /// </summary>
    public class BackupServiceCommunication : IHostedService, IDisposable
    {
        private const string PipeName = "BackupRestoreServicePipe";
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _listenTask;

        public event EventHandler<BackupCommandEventArgs>? CommandReceived;
        public event EventHandler<ProgressQueryEventArgs>? ProgressQueried;

        // IHostedService implementation - called automatically when Windows Service starts
        public Task StartAsync(CancellationToken cancellationToken)
        {
            System.Diagnostics.Debug.WriteLine("BackupServiceCommunication: Starting named pipe listener...");
            _listenTask = Task.Run(ListenForConnectionsAsync, _cancellationTokenSource.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            System.Diagnostics.Debug.WriteLine("BackupServiceCommunication: Stopping named pipe listener...");
            _cancellationTokenSource.Cancel();
            if (_listenTask != null)
            {
                await _listenTask;
            }
        }

        private async Task ListenForConnectionsAsync()
        {
            System.Diagnostics.Debug.WriteLine("BackupServiceCommunication: Started listening for connections");
            
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous);

                    System.Diagnostics.Debug.WriteLine("BackupServiceCommunication: Waiting for client connection...");
                    await pipeServer.WaitForConnectionAsync(_cancellationTokenSource.Token);
                    System.Diagnostics.Debug.WriteLine("BackupServiceCommunication: Client connected!");

                    // Handle client in separate task so we can continue listening
                    _ = Task.Run(() => HandleClientAsync(pipeServer), _cancellationTokenSource.Token);
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
            
            System.Diagnostics.Debug.WriteLine("BackupServiceCommunication: Stopped listening");
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
                {
                    System.Diagnostics.Debug.WriteLine("BackupServiceCommunication: Invalid command (null)");
                    return CreateResponse(false, "Invalid command");
                }

                System.Diagnostics.Debug.WriteLine($"BackupServiceCommunication: Processing command: {command.CommandType}");

                switch (command.CommandType)
                {
                    case "RunBackup":
                        var jobId = Guid.Parse(command.Data ?? "");
                        System.Diagnostics.Debug.WriteLine($"BackupServiceCommunication: Raising RunBackup event for job: {jobId}");
                        CommandReceived?.Invoke(this, new BackupCommandEventArgs { JobId = jobId });
                        return CreateResponse(true, "Backup started");

                    case "AbortBackup":
                        var abortJobId = Guid.Parse(command.Data ?? "");
                        System.Diagnostics.Debug.WriteLine($"BackupServiceCommunication: Raising AbortBackup event for job: {abortJobId}");
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

                    case "GetVersion":
                        // Return service version from assembly
                        var assembly = Assembly.GetExecutingAssembly();
                        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                   ?? assembly.GetName().Version?.ToString()
                                   ?? "Unknown";
                        
                        // Strip Git commit hash if present
                        int plusIndex = version.IndexOf('+');
                        if (plusIndex > 0)
                        {
                            version = version.Substring(0, plusIndex);
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"BackupServiceCommunication: Returning version: {version}");
                        return CreateResponse(true, version);

                    default:
                        System.Diagnostics.Debug.WriteLine($"BackupServiceCommunication: Unknown command type: {command.CommandType}");
                        return CreateResponse(false, "Unknown command");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackupServiceCommunication: Error processing message: {ex.Message}");
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
