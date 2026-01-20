using System;
using System.IO;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using BackupUI.Services;

namespace BackupUI.Windows
{
    public partial class ServiceManagementWindow : Window
    {
        private readonly BackupServiceManager serviceManager = new();

        public ServiceManagementWindow()
        {
            InitializeComponent();
            _ = RefreshStatusAsync();
        }

        private async void RefreshStatus_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatusAsync();
        }

        private async Task RefreshStatusAsync()
        {
            try
            {
                bool isInstalled = await serviceManager.IsServiceInstalledAsync();
                txtInstalled.Text = isInstalled ? "Yes" : "No";

                if (isInstalled)
                {
                    var status = await serviceManager.GetServiceStatusAsync();
                    txtStatus.Text = status?.ToString() ?? "Unknown";

                    btnStart.IsEnabled = status != ServiceControllerStatus.Running;
                    btnStop.IsEnabled = status == ServiceControllerStatus.Running;
                    btnRestart.IsEnabled = status == ServiceControllerStatus.Running;
                    btnInstall.IsEnabled = false;
                    btnUninstall.IsEnabled = true;
                }
                else
                {
                    txtStatus.Text = "Not Installed";
                    btnStart.IsEnabled = false;
                    btnStop.IsEnabled = false;
                    btnRestart.IsEnabled = false;
                    btnInstall.IsEnabled = true;
                    btnUninstall.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to refresh status: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StartService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool success = await serviceManager.StartServiceAsync();
                if (success)
                {
                    MessageBox.Show("Service started successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshStatusAsync();
                }
                else
                {
                    MessageBox.Show("Failed to start service.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start service: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StopService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool success = await serviceManager.StopServiceAsync();
                if (success)
                {
                    MessageBox.Show("Service stopped successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshStatusAsync();
                }
                else
                {
                    MessageBox.Show("Failed to stop service.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop service: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RestartService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool success = await serviceManager.RestartServiceAsync();
                if (success)
                {
                    MessageBox.Show("Service restarted successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshStatusAsync();
                }
                else
                {
                    MessageBox.Show("Failed to restart service.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to restart service: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void InstallService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var servicePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BackupService.exe");
                
                if (!File.Exists(servicePath))
                {
                    MessageBox.Show($"Service executable not found at: {servicePath}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool success = await serviceManager.InstallServiceAsync(servicePath);
                if (success)
                {
                    MessageBox.Show("Service installed successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshStatusAsync();
                }
                else
                {
                    MessageBox.Show("Failed to install service. Make sure you run as Administrator.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to install service: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void UninstallService_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to uninstall the service?",
                "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    bool success = await serviceManager.UninstallServiceAsync();
                    if (success)
                    {
                        MessageBox.Show("Service uninstalled successfully.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        await RefreshStatusAsync();
                    }
                    else
                    {
                        MessageBox.Show("Failed to uninstall service. Make sure you run as Administrator.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to uninstall service: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
