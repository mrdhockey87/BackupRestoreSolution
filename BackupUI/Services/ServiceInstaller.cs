using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace BackupUI.Services
{
    /// <summary>
    /// Helper class to install, start, and manage the BackupRestoreService
    /// </summary>
    public static class ServiceInstaller
    {
        private const string ServiceName = "BackupRestoreService";
        private const string ServiceDisplayName = "Backup & Restore Service";
        private const string ServiceDescription = "Enterprise backup and restore service for Windows servers and Hyper-V VMs";

        /// <summary>
        /// Check if service is installed
        /// </summary>
        public static bool IsServiceInstalled()
        {
            try
            {
                using var service = new ServiceController(ServiceName);
                var status = service.Status; // Will throw if service doesn't exist
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Check if service is running
        /// </summary>
        public static bool IsServiceRunning()
        {
            try
            {
                using var service = new ServiceController(ServiceName);
                return service.Status == ServiceControllerStatus.Running;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get service status
        /// </summary>
        public static ServiceControllerStatus? GetServiceStatus()
        {
            try
            {
                using var service = new ServiceController(ServiceName);
                return service.Status;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Install the service using sc.exe
        /// </summary>
        public static async Task<(bool success, string message)> InstallServiceAsync()
        {
            try
            {
                // Find BackupService.exe in the application directory
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var serviceExePath = Path.Combine(appDir, "BackupService.exe");

                if (!File.Exists(serviceExePath))
                {
                    return (false, $"Service executable not found at: {serviceExePath}");
                }

                // Use sc.exe to install the service
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"create {ServiceName} binPath= \"{serviceExePath}\" start= auto DisplayName= \"{ServiceDisplayName}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas" // Request elevation
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    // Set service description
                    await SetServiceDescriptionAsync();
                    
                    BackupLogger.LogServiceInfo($"Service installed successfully from: {serviceExePath}");
                    return (true, "Service installed successfully");
                }
                else
                {
                    string errorMsg = $"Service installation failed. Exit code: {process.ExitCode}\n{output}\n{error}";
                    BackupLogger.LogServiceError($"Service installation failed: {errorMsg}");
                    return (false, errorMsg);
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error installing service: {ex.Message}";
                BackupLogger.LogServiceError(errorMsg);
                return (false, errorMsg);
            }
        }

        /// <summary>
        /// Start the service
        /// </summary>
        public static async Task<(bool success, string message)> StartServiceAsync()
        {
            try
            {
                using var service = new ServiceController(ServiceName);
                
                if (service.Status == ServiceControllerStatus.Running)
                {
                    return (true, "Service is already running");
                }

                BackupLogger.LogServiceInfo("Starting service...");
                service.Start();
                
                // Wait up to 30 seconds for service to start
                await Task.Run(() => service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)));
                
                BackupLogger.LogServiceInfo("Service started successfully");
                return (true, "Service started successfully");
            }
            catch (Exception ex)
            {
                string errorMsg = $"Failed to start service: {ex.Message}";
                BackupLogger.LogServiceError(errorMsg);
                return (false, errorMsg);
            }
        }

        /// <summary>
        /// Stop the service
        /// </summary>
        public static async Task<(bool success, string message)> StopServiceAsync()
        {
            try
            {
                using var service = new ServiceController(ServiceName);
                
                if (service.Status == ServiceControllerStatus.Stopped)
                {
                    return (true, "Service is already stopped");
                }

                BackupLogger.LogServiceInfo("Stopping service...");
                service.Stop();
                
                // Wait up to 30 seconds for service to stop
                await Task.Run(() => service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)));
                
                BackupLogger.LogServiceInfo("Service stopped successfully");
                return (true, "Service stopped successfully");
            }
            catch (Exception ex)
            {
                string errorMsg = $"Failed to stop service: {ex.Message}";
                BackupLogger.LogServiceError(errorMsg);
                return (false, errorMsg);
            }
        }

        /// <summary>
        /// Uninstall the service using sc.exe
        /// </summary>
        public static async Task<(bool success, string message)> UninstallServiceAsync()
        {
            try
            {
                // Stop service first if running
                if (IsServiceRunning())
                {
                    await StopServiceAsync();
                    await Task.Delay(1000); // Give it a moment to fully stop
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"delete {ServiceName}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas" // Request elevation
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    BackupLogger.LogServiceInfo("Service uninstalled successfully");
                    return (true, "Service uninstalled successfully");
                }
                else
                {
                    string errorMsg = $"Service uninstall failed. Exit code: {process.ExitCode}\n{output}\n{error}";
                    BackupLogger.LogServiceError($"Service uninstall failed: {errorMsg}");
                    return (false, errorMsg);
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error uninstalling service: {ex.Message}";
                BackupLogger.LogServiceError(errorMsg);
                return (false, errorMsg);
            }
        }

        /// <summary>
        /// Install and start the service in one operation
        /// </summary>
        public static async Task<(bool success, string message)> InstallAndStartServiceAsync()
        {
            // Check if already installed
            if (IsServiceInstalled())
            {
                BackupLogger.LogServiceInfo("Service already installed, attempting to start...");
                return await StartServiceAsync();
            }

            // Install service
            var (installSuccess, installMessage) = await InstallServiceAsync();
            if (!installSuccess)
            {
                return (false, $"Installation failed: {installMessage}");
            }

            // Wait a moment for service registration to complete
            await Task.Delay(2000);

            // Start service
            var (startSuccess, startMessage) = await StartServiceAsync();
            if (!startSuccess)
            {
                return (false, $"Service installed but failed to start: {startMessage}");
            }

            return (true, "Service installed and started successfully");
        }

        private static async Task SetServiceDescriptionAsync()
        {
            try
            {
                var versionNumber = VersionClass.GetAssemblyVersion();
                var description = $"{ServiceDescription} (Version {versionNumber})";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"description {ServiceName} \"{description}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();
            }
            catch
            {
                // Ignore errors setting description - not critical
            }
        }
    }
}
