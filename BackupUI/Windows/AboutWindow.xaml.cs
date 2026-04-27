using System;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using SecureServerBackup.Services;

namespace SecureServerBackup.Windows
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            LoadVersions();
        }

        private async void LoadVersions()
        {
            // UI Version (always available)
            var uiVersion = VersionClass.GetAssemblyVersion();
            txtMainVersion.Text = $"Version {uiVersion}";
            txtUIVersion.Text = uiVersion;

            // Engine Version (same as UI - they share version from Directory.Build.props)
            txtEngineVersion.Text = uiVersion;

            // Service Version (query via Named Pipe if service is running)
            await LoadServiceVersionAsync();
        }

        private async Task LoadServiceVersionAsync()
        {
            txtServiceVersion.Text = "Checking...";
            txtServiceWarning.Visibility = Visibility.Collapsed;
            
            try
            {
                // Check if service is installed and running
                using var service = new ServiceController("BackupRestoreService");
                
                if (service.Status == ServiceControllerStatus.Running)
                {
                    try
                    {
                        // Query version via Named Pipe with timeout
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
                        
                        if (serviceVersion != null && !string.IsNullOrWhiteSpace(serviceVersion))
                        {
                            txtServiceVersion.Text = serviceVersion;
                            
                            // Check for version mismatch
                            var uiVersion = VersionClass.GetAssemblyVersion();
                            if (serviceVersion != uiVersion)
                            {
                                txtServiceWarning.Text = " ?? Version Mismatch!";
                                txtServiceWarning.Foreground = System.Windows.Media.Brushes.Red;
                                txtServiceWarning.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                txtServiceWarning.Visibility = Visibility.Collapsed;
                            }
                        }
                        else
                        {
                            // Timeout or null response - old service version
                            txtServiceVersion.Text = "Unknown (old version)";
                            txtServiceWarning.Text = " ?? Reinstall Required";
                            txtServiceWarning.Foreground = System.Windows.Media.Brushes.Orange;
                            txtServiceWarning.Visibility = Visibility.Visible;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Service version check error: {ex.Message}");
                        txtServiceVersion.Text = "Unknown (check failed)";
                        txtServiceWarning.Text = " ?? Check Failed";
                        txtServiceWarning.Foreground = System.Windows.Media.Brushes.Orange;
                        txtServiceWarning.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    txtServiceVersion.Text = $"N/A ({service.Status})";
                    txtServiceWarning.Text = $" ?? {service.Status}";
                    txtServiceWarning.Foreground = System.Windows.Media.Brushes.Orange;
                    txtServiceWarning.Visibility = Visibility.Visible;
                }
            }
            catch (InvalidOperationException)
            {
                // Service not installed
                txtServiceVersion.Text = "Not Installed";
                txtServiceWarning.Text = " ?? Not Installed";
                txtServiceWarning.Foreground = System.Windows.Media.Brushes.Orange;
                txtServiceWarning.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                txtServiceVersion.Text = $"Error: {ex.Message}";
                txtServiceWarning.Visibility = Visibility.Collapsed;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
