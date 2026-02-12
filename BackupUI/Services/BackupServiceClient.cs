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
