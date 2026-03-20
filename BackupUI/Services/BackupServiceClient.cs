using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BackupCommon;  // v6.0.1.19: Use shared BackupProgress DTO from BackupCommon

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

                using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

                var message = JsonSerializer.Serialize(command);
                await writer.WriteLineAsync(message);
                await writer.FlushAsync();

                var responseJson = await reader.ReadLineAsync();
                if (responseJson == null)
                    return null;

                return JsonSerializer.Deserialize<BackupProgress>(responseJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting progress: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
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
                var response = await SendCommandAsync(command);
                System.Diagnostics.Debug.WriteLine($"GetServiceVersionAsync: Response = {(response != null ? $"Success={response.Success}, Message={response.Message}" : "null")}");
                return response?.Message;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting service version: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        private async Task<ServiceResponse?> SendCommandAsync(ServiceCommand command)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                await pipe.ConnectAsync(Timeout);

                using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

                var message = JsonSerializer.Serialize(command);
                await writer.WriteLineAsync(message);
                await writer.FlushAsync();

                var responseJson = await reader.ReadLineAsync();
                if (responseJson == null)
                    return null;

                return JsonSerializer.Deserialize<ServiceResponse>(responseJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending command '{command.CommandType}': {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
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
}
