using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SecureServerBackup
{
    public partial class App : Application
    {
        // Held for the lifetime of the process; released automatically on exit.
        private static Mutex? _singleInstanceMutex;

        private const string MutexName = "SecureServerBackup_SingleInstance_Mutex";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Single-instance guard: allow only one main window at a time.
            _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                // Another instance is already running — activate its window and exit.
                ActivateExistingInstance();
                _singleInstanceMutex.Dispose();
                Shutdown();
                return;
            }

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

                    // Release the mutex before launching the elevated instance so it
                    // can acquire it successfully (we are about to exit anyway).
                    _singleInstanceMutex.ReleaseMutex();
                    _singleInstanceMutex.Dispose();
                    _singleInstanceMutex = null;

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
        /// Finds the already-running instance by executable name and brings its
        /// main window to the foreground, restoring it if minimized.
        /// </summary>
        private static void ActivateExistingInstance()
        {
            try
            {
                string? exeName = Path.GetFileNameWithoutExtension(
                    Process.GetCurrentProcess().MainModule?.FileName);

                if (string.IsNullOrEmpty(exeName))
                    return;

                foreach (Process proc in Process.GetProcessesByName(exeName))
                {
                    if (proc.Id == Environment.ProcessId)
                        continue;

                    IntPtr hWnd = proc.MainWindowHandle;
                    if (hWnd == IntPtr.Zero)
                        continue;

                    if (IsIconic(hWnd))
                        ShowWindow(hWnd, SW_RESTORE);

                    SetForegroundWindow(hWnd);
                    break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App.ActivateExistingInstance] {ex.Message}");
            }
        }

        /// <summary>
        /// Shows splash screen and performs application initialization
        /// </summary>
        private async Task ShowSplashScreenAndInitialize(StartupEventArgs e)
        {
            SecureServerBackup.Windows.SplashScreen? splash = null;

            try
            {
                // Show splash screen
                splash = await SecureServerBackup.Windows.SplashScreen.ShowAsync();

                // Perform initialization tasks
                splash.UpdateStatus("Checking components...");
                await Task.Delay(500); // Brief delay to show status

                // Check if SecureServerBackupEngine.dll exists
                splash.UpdateStatus("Verifying SecureServerBackupEngine.dll...");
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
            var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SecureServerBackupEngine.dll");

            if (!File.Exists(dllPath))
            {
                throw new FileNotFoundException(
                    $"SecureServerBackupEngine.dll was not found. Expected location: {dllPath}",
                    dllPath);
            }

            Debug.WriteLine($"SecureServerBackupEngine.dll found at: {dllPath}");
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
