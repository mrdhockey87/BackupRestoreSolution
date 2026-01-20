using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace BackupUI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Check if running as administrator
            if (!IsRunningAsAdministrator())
            {
                // Restart the application with administrator privileges
                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Environment.CurrentDirectory,
                        FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "",
                        Verb = "runas" // This triggers UAC elevation
                    };

                    Process.Start(processInfo);
                }
                catch (Exception)
                {
                    // User cancelled the UAC prompt
                    MessageBox.Show(
                        "This application requires administrator privileges to access backup services, VSS snapshots, and Hyper-V.\n\nPlease run as Administrator.",
                        "Administrator Rights Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                // Shutdown the current non-elevated instance
                Shutdown();
                return;
            }

            // Check if BackupEngine.dll exists
            CheckBackupEngineDll();

            base.OnStartup(e);
        }

        private void CheckBackupEngineDll()
        {
            try
            {
                var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BackupEngine.dll");
                
                if (!File.Exists(dllPath))
                {
                    MessageBox.Show(
                        $"Critical Error: BackupEngine.dll not found!\n\n" +
                        $"Expected location: {dllPath}\n\n" +
                        $"Please ensure:\n" +
                        $"1. BackupEngine project is built first\n" +
                        $"2. BackupEngine.dll is in the same directory as BackupUI.exe\n" +
                        $"3. Build the entire solution (Build ? Rebuild Solution)",
                        "Missing DLL",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    // DLL exists, log its location for debugging
                    Debug.WriteLine($"BackupEngine.dll found at: {dllPath}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error checking for BackupEngine.dll: {ex.Message}",
                    "Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
