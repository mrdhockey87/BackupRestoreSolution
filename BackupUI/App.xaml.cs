using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;

namespace BackupUI
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Set Normal process priority to prevent Efficiency mode for UI operations
            // Only backup operations should run at lower priority
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                Debug.WriteLine("[App.OnStartup] Process priority set to Normal");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App.OnStartup] Warning: Failed to set process priority: {ex.Message}");
            }

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
                    CustomDialogService.ShowWarning(
                        "This application requires administrator privileges to access backup services, VSS snapshots, and Hyper-V.\n\nPlease run as Administrator.",
                        "Administrator Rights Required");
                }

                // Shutdown the current non-elevated instance
                Shutdown();
                return;
            }

            // Show splash screen and perform initialization
            await ShowSplashScreenAndInitialize(e);

            base.OnStartup(e);
        }

        /// <summary>
        /// Shows splash screen and performs application initialization
        /// </summary>
        private async Task ShowSplashScreenAndInitialize(StartupEventArgs e)
        {
            BackupUI.Windows.SplashScreen? splash = null;

            try
            {
                // Show splash screen
                splash = await BackupUI.Windows.SplashScreen.ShowAsync();

                // Perform initialization tasks
                splash.UpdateStatus("Checking components...");
                await Task.Delay(500); // Brief delay to show status

                // Check if BackupEngine.dll exists
                splash.UpdateStatus("Verifying BackupEngine.dll...");
                CheckBackupEngineDll();
                await Task.Delay(300);

                // Initialize services
                splash.UpdateStatus("Initializing services...");
                await Task.Delay(500);

                // Load main window (but don't show yet)
                splash.UpdateStatus("Loading main window...");
                var mainWindow = new MainWindow();
                await Task.Delay(300);

                // Close splash screen with fade
                splash.UpdateStatus("Ready!");
                await Task.Delay(200);
                await splash.CloseAsync();

                // Show main window
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                // Close splash if there's an error
                if (splash != null)
                {
                    try
                    {
                        await splash.CloseAsync();
                    }
                    catch { }
                }

                CustomDialogService.ShowError(
                    $"Error during application startup: {ex.Message}",
                    "Startup Error");

                Shutdown();
            }
        }

        private void CheckBackupEngineDll()
        {
            try
            {
                var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BackupEngine.dll");
                
                if (!File.Exists(dllPath))
                {
                    CustomDialogService.ShowError(
                        $"Critical Error: BackupEngine.dll not found!\n\n" +
                        $"Expected location: {dllPath}\n\n" +
                        $"Please ensure:\n" +
                        $"1. BackupEngine project is built first\n" +
                        $"2. BackupEngine.dll is in the same directory as BackupUI.exe\n" +
                        $"3. Build the entire solution (Build ? Rebuild Solution)",
                        "Missing DLL");
                }
                else
                {
                    // DLL exists, log its location for debugging
                    Debug.WriteLine($"BackupEngine.dll found at: {dllPath}");
                }
            }
            catch (Exception ex)
            {
                CustomDialogService.ShowError(
                    $"Error checking for BackupEngine.dll: {ex.Message}",
                    "Initialization Error");
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
