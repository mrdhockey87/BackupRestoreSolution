using System;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace BackupUI.Services
{
    public class BackupServiceManager
    {
        private const string ServiceName = "BackupRestoreService";

        public async Task<bool> IsServiceInstalledAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var sc = new ServiceController(ServiceName);
                    var status = sc.Status;
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<ServiceControllerStatus?> GetServiceStatusAsync()
        {
            return await Task.Run<ServiceControllerStatus?>(() =>
            {
                try
                {
                    using var sc = new ServiceController(ServiceName);
                    sc.Refresh();
                    return sc.Status;
                }
                catch
                {
                    return null;
                }
            });
        }

        public async Task<bool> StartServiceAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var sc = new ServiceController(ServiceName);
                    if (sc.Status != ServiceControllerStatus.Running)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> StopServiceAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var sc = new ServiceController(ServiceName);
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> RestartServiceAsync()
        {
            return await Task.Run(async () =>
            {
                if (await StopServiceAsync())
                {
                    await Task.Delay(2000);
                    return await StartServiceAsync();
                }
                return false;
            });
        }

        public async Task<bool> InstallServiceAsync(string executablePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"create {ServiceName} binPath= \"{executablePath}\" start= auto",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas"
                    };

                    using var process = System.Diagnostics.Process.Start(startInfo);
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> UninstallServiceAsync()
        {
            return await Task.Run(async () =>
            {
                try
                {
                    await StopServiceAsync();

                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"delete {ServiceName}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas"
                    };

                    using var process = System.Diagnostics.Process.Start(startInfo);
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
