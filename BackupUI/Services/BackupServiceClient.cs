using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BackupUI.Services
{
    /// <summary>
    /// Client-side communication with BackupService via Named Pipes
    /// </summary>
    public class BackupServiceClient
    {
        private const string PipeName = "BackupRestoreServicePipe";
        private const int Timeout = 5000;

        public async Task<bool> RunBackupNowAsync(Guid jobId)
        {
            var command = new ServiceCommand
            {
                CommandType = "RunBackup",
                Data = jobId.ToString()
            };

            var response = await SendCommandAsync(command);
            return response?.Success ?? false;
        }

        public async Task<bool> AbortBackupAsync(Guid jobId)
        {
            var command = new ServiceCommand
            {
                CommandType = "AbortBackup",
                Data = jobId.ToString()
            };

            var response = await SendCommandAsync(command);
            return response?.Success ?? false;
        }

        public async Task<BackupProgress?> GetProgressAsync(Guid jobId)
        {
            var command = new ServiceCommand
            {
                CommandType = "GetProgress",
                Data = jobId.ToString()
            };

            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                await pipe.ConnectAsync(Timeout);

                using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8);

                var message = JsonSerializer.Serialize(command);
                await writer.WriteLineAsync(message);

                var responseJson = await reader.ReadLineAsync();
                if (responseJson == null)
                    return null;

                return JsonSerializer.Deserialize<BackupProgress>(responseJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting progress: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> GetServiceVersionAsync()
        {
            var command = new ServiceCommand
            {
                CommandType = "GetVersion",
                Data = null
            };

            try
            {
                System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 1: Creating pipe connection to '{PipeName}'");
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                
                System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 2: Connecting with {Timeout}ms timeout...");
                await pipe.ConnectAsync(Timeout);
                System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 3: Connected successfully! IsConnected={pipe.IsConnected}");

                using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8);

                var message = JsonSerializer.Serialize(command);
                System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 4: Sending command: {message}");
                
                // Write the command
                await writer.WriteLineAsync(message);
                await writer.FlushAsync();
                System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 5: Command sent and flushed, waiting for response...");

                // Read with timeout using CancellationToken
                using var cts = new CancellationTokenSource(Timeout);
                string? responseJson = null;
                
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 5a: Starting ReadLineAsync with {Timeout}ms timeout...");
                    responseJson = await reader.ReadLineAsync().WaitAsync(cts.Token);
                    System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 5b: ReadLineAsync completed");
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 5c: ReadLineAsync TIMED OUT after {Timeout}ms");
                    return null;
                }
                
                System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 6: Received response: {responseJson ?? "<NULL>"}");
                
                if (responseJson == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 7: Response was NULL, returning null");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 8: Deserializing response...");
                var response = JsonSerializer.Deserialize<ServiceResponse>(responseJson);
                System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 9: Deserialized - Success={response?.Success}, Message={response?.Message}");
                
                var result = response?.Message;
                System.Diagnostics.Debug.WriteLine($"[GetVersion] Step 10: Returning version: {result ?? "<NULL>"}");
                return result;
            }
            catch (TimeoutException tex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetVersion] TIMEOUT ERROR: {tex.Message}");
                return null;
            }
            catch (IOException ioex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetVersion] IO ERROR: {ioex.Message}");
                return null;
            }
            catch (JsonException jex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetVersion] JSON ERROR: {jex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetVersion] GENERAL ERROR: Type={ex.GetType().Name}, Message={ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[GetVersion] Stack Trace: {ex.StackTrace}");
                return null;
            }
        }

        private async Task<ServiceResponse?> SendCommandAsync(ServiceCommand command)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                await pipe.ConnectAsync(Timeout);

                using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8);

                var message = JsonSerializer.Serialize(command);
                await writer.WriteLineAsync(message);

                var responseJson = await reader.ReadLineAsync();
                if (responseJson == null)
                    return null;

                return JsonSerializer.Deserialize<ServiceResponse>(responseJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending command: {ex.Message}");
                return null;
            }
        }
    }

    public class ServiceCommand
    {
        public string CommandType { get; set; } = "";
        public string? Data { get; set; }
    }

    public class ServiceResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
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
