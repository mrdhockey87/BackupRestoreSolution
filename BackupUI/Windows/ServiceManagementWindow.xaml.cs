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
            
            // Show UI version immediately
            txtUIVersion.Text = VersionClass.GetAssemblyVersion();
            
            // Explicitly enable all buttons initially to prevent XAML defaults from interfering
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = false;
            btnRestart.IsEnabled = false;
            btnInstall.IsEnabled = false;
            btnUninstall.IsEnabled = false;
            
            System.Diagnostics.Debug.WriteLine("ServiceManagement: Window initialized, starting RefreshStatusAsync");
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
                System.Diagnostics.Debug.WriteLine($"ServiceManagement: IsServiceInstalled = {isInstalled}");
                
                txtInstalled.Text = isInstalled ? "Yes" : "No";

                if (isInstalled)
                {
                    System.Diagnostics.Debug.WriteLine("ServiceManagement: Service is installed");
                    var status = await serviceManager.GetServiceStatusAsync();
                    
                    // Format status with proper spacing (e.g., "StartPending" -> "Start Pending")
                    txtStatus.Text = status.HasValue ? FormatServiceStatus(status.Value) : "Unknown";

                    // Get service version via Named Pipe (with timeout and error handling)
                    if (status == ServiceControllerStatus.Running)
                    {
                        txtServiceVersion.Text = "Checking...";
                        txtVersionWarning.Visibility = Visibility.Collapsed;
                        
                        // Run version check in background with timeout
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var serviceClient = new BackupServiceClient();
                                
                                // Add 3-second timeout wrapper
                                var versionTask = serviceClient.GetServiceVersionAsync();
                                var timeoutTask = Task.Delay(3000);
                                var completedTask = await Task.WhenAny(versionTask, timeoutTask);
                                
                                string? serviceVersion = null;
                                if (completedTask == versionTask)
                                {
                                    serviceVersion = await versionTask;
                                }
                                
                                // Update UI on UI thread
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    if (serviceVersion != null && !string.IsNullOrWhiteSpace(serviceVersion))
                                    {
                                        txtServiceVersion.Text = serviceVersion;
                                        
                                        // Check for version mismatch
                                        var uiVersion = VersionClass.GetAssemblyVersion();
                                        if (serviceVersion != uiVersion)
                                        {
                                            txtVersionWarning.Text = " ? VERSION MISMATCH!";
                                            txtVersionWarning.Foreground = System.Windows.Media.Brushes.Red;
                                            txtVersionWarning.Visibility = Visibility.Visible;
                                        }
                                        else
                                        {
                                            txtVersionWarning.Visibility = Visibility.Collapsed;
                                        }
                                    }
                                    else
                                    {
                                        // Old service without GetVersion or timeout
                                        txtServiceVersion.Text = "Unknown (old version)";
                                        txtVersionWarning.Text = " ?? Reinstall Required";
                                        txtVersionWarning.Foreground = System.Windows.Media.Brushes.Orange;
                                        txtVersionWarning.Visibility = Visibility.Visible;
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Version check error: {ex.Message}");
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    txtServiceVersion.Text = "Unknown (check failed)";
                                    txtVersionWarning.Text = " ?? Check Failed";
                                    txtVersionWarning.Foreground = System.Windows.Media.Brushes.Orange;
                                    txtVersionWarning.Visibility = Visibility.Visible;
                                });
                            }
                        });
                    }
                    else
                    {
                        txtServiceVersion.Text = "N/A (service not running)";
                        txtVersionWarning.Text = " ?? Not Running";
                        txtVersionWarning.Foreground = System.Windows.Media.Brushes.Orange;
                        txtVersionWarning.Visibility = Visibility.Visible;
                    }

                    System.Diagnostics.Debug.WriteLine($"ServiceManagement: Setting buttons - Start={status != ServiceControllerStatus.Running}, Stop={status == ServiceControllerStatus.Running}");
                    btnStart.IsEnabled = status != ServiceControllerStatus.Running;
                    btnStop.IsEnabled = status == ServiceControllerStatus.Running;
                    btnRestart.IsEnabled = status == ServiceControllerStatus.Running;
                    btnInstall.IsEnabled = false;
                    btnUninstall.IsEnabled = true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ServiceManagement: Service is NOT installed - enabling Install button");
                    txtStatus.Text = "Not Installed";
                    txtServiceVersion.Text = "N/A (not installed)";
                    txtVersionWarning.Visibility = Visibility.Collapsed;
                    btnStart.IsEnabled = false;
                    btnStop.IsEnabled = false;
                    btnRestart.IsEnabled = false;
                    btnInstall.IsEnabled = true;
                    btnUninstall.IsEnabled = false;
                    
                    System.Diagnostics.Debug.WriteLine($"ServiceManagement: Button states BEFORE UpdateLayout - Install={btnInstall.IsEnabled}, Uninstall={btnUninstall.IsEnabled}");
                    
                    // Force UI update
                    this.UpdateLayout();
                    
                    System.Diagnostics.Debug.WriteLine($"ServiceManagement: Button states AFTER UpdateLayout - Install={btnInstall.IsEnabled}, Uninstall={btnUninstall.IsEnabled}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServiceManagement: RefreshStatusAsync error: {ex.Message}");
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

                // Install the service
                bool installSuccess = await serviceManager.InstallServiceAsync(servicePath);
                if (!installSuccess)
                {
                    MessageBox.Show("Failed to install service. Make sure you run as Administrator.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Service installed successfully - now start it automatically
                System.Diagnostics.Debug.WriteLine("ServiceManagement: Service installed, attempting to start...");

                // Give the system a moment to register the service
                await Task.Delay(1000);

                bool startSuccess = await serviceManager.StartServiceAsync();
                if (startSuccess)
                {
                    MessageBox.Show("Service installed and started successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Installation succeeded but start failed - still a partial success
                    MessageBox.Show("Service installed successfully, but failed to start automatically.\n\nPlease use the 'Start Service' button to start it manually.", 
                        "Partial Success",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                await RefreshStatusAsync();
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

        /// <summary>
        /// Formats ServiceControllerStatus enum to human-readable text with proper spacing
        /// </summary>
        private string FormatServiceStatus(ServiceControllerStatus status)
        {
            return status switch
            {
                ServiceControllerStatus.Running => "Running",
                ServiceControllerStatus.Stopped => "Stopped",
                ServiceControllerStatus.Paused => "Paused",
                ServiceControllerStatus.StartPending => "Start Pending",
                ServiceControllerStatus.StopPending => "Stop Pending",
                ServiceControllerStatus.ContinuePending => "Continue Pending",
                ServiceControllerStatus.PausePending => "Pause Pending",
                _ => status.ToString()
            };
        }
    }
}
