using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SecureServerBackup.Windows
{
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
            LoadSavedPosition();
            LoadLogo();
            LoadVersion();
        }

        /// <summary>
        /// Loads the saved main window position and applies it to splash screen
        /// </summary>
        private void LoadSavedPosition()
        {
            try
            {
                // Path to saved window position (matches WindowPositionManager)
                string settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BackupRestoreApp",
                    "window-position.json");

                if (File.Exists(settingsPath))
                {
                    // Read saved position
                    var json = File.ReadAllText(settingsPath);
                    var position = JsonSerializer.Deserialize<SavedWindowPosition>(json);

                    if (position != null && IsPositionValid(position))
                    {
                        // Center splash screen on saved main window position
                        // Calculate center point of main window
                        double mainWindowCenterX = position.Left + (position.Width / 2);
                        double mainWindowCenterY = position.Top + (position.Height / 2);

                        // Position splash screen centered on main window's center
                        this.Left = mainWindowCenterX - (this.Width / 2);
                        this.Top = mainWindowCenterY - (this.Height / 2);

                        System.Diagnostics.Debug.WriteLine($"Splash positioned at saved main window location: {this.Left}, {this.Top}");
                        return;
                    }
                }

                // No saved position or invalid - center on primary screen
                CenterOnPrimaryScreen();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load saved position: {ex.Message}");
                // Fall back to centering
                CenterOnPrimaryScreen();
            }
        }

        /// <summary>
        /// Validates that the saved position would be visible on current screen configuration
        /// </summary>
        private bool IsPositionValid(SavedWindowPosition position)
        {
            try
            {
                // Calculate where splash screen would appear
                double splashLeft = position.Left + (position.Width / 2) - (this.Width / 2);
                double splashTop = position.Top + (position.Height / 2) - (this.Height / 2);

                var rect = new Rect(splashLeft, splashTop, this.Width, this.Height);

                // Check if splash screen would be visible on any screen
                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var workingArea = new Rect(
                        screen.WorkingArea.Left,
                        screen.WorkingArea.Top,
                        screen.WorkingArea.Width,
                        screen.WorkingArea.Height);

                    // Check if at least part of the window would be visible
                    if (workingArea.IntersectsWith(rect))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Centers the splash screen on the primary screen
        /// </summary>
        private void CenterOnPrimaryScreen()
        {
            try
            {
                var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
                if (primaryScreen != null)
                {
                    this.Left = primaryScreen.WorkingArea.Left + (primaryScreen.WorkingArea.Width - this.Width) / 2;
                    this.Top = primaryScreen.WorkingArea.Top + (primaryScreen.WorkingArea.Height - this.Height) / 2;
                    System.Diagnostics.Debug.WriteLine($"Splash centered on primary screen: {this.Left}, {this.Top}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to center on primary screen: {ex.Message}");
                // Ultimate fallback - let WPF handle it
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        /// <summary>
        /// Data class for saved window position (matches WindowPositionManager format)
        /// </summary>
        private class SavedWindowPosition
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public WindowState WindowState { get; set; }
        }

        /// <summary>
        /// Loads the appropriate logo based on screen DPI/resolution
        /// </summary>
        private void LoadLogo()
        {
            try
            {
                // Get current screen DPI scale factor
                var dpiScale = VisualTreeHelper.GetDpi(this);
                double scaleFactor = Math.Max(dpiScale.DpiScaleX, dpiScale.DpiScaleY);

                // Select logo based on DPI scale using pack:// URIs for embedded resources
                // Standard (100-149% scaling) - use small logo
                // High DPI (150-199% scaling) - use medium logo  
                // Very High DPI (200%+ scaling) - use large logo
                Uri logoUri;
                string selectedLogo;

                if (scaleFactor >= 2.0)
                {
                    logoUri = new Uri("pack://application:,,,/Assets/logo_large.png", UriKind.Absolute);
                    selectedLogo = "logo_large.png (4K displays, 200%+ DPI)";
                }
                else if (scaleFactor >= 1.5)
                {
                    logoUri = new Uri("pack://application:,,,/Assets/logo_medium.png", UriKind.Absolute);
                    selectedLogo = "logo_medium.png (high DPI displays, 150-199%)";
                }
                else
                {
                    logoUri = new Uri("pack://application:,,,/Assets/logo_small.png", UriKind.Absolute);
                    selectedLogo = "logo_small.png (standard displays, 100-149%)";
                }

                try
                {
                    // Try to load the selected logo from embedded resources
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = logoUri;
                    bitmap.EndInit();

                    imgLogo.Source = bitmap;
                    System.Diagnostics.Debug.WriteLine($"Loaded logo: {selectedLogo} (scale factor: {scaleFactor:F2})");
                }
                catch (Exception logoEx)
                {
                    // Resource not found - try fallback logos
                    System.Diagnostics.Debug.WriteLine($"Logo resource not found: {logoUri}, trying fallbacks... Error: {logoEx.Message}");

                    // Try fallback logos in order: medium, large, small
                    string[] fallbackUris = {
                        "pack://application:,,,/Assets/logo_medium.png",
                        "pack://application:,,,/Assets/logo_large.png",
                        "pack://application:,,,/Assets/logo_small.png"
                    };

                    bool loaded = false;
                    foreach (var fallbackUri in fallbackUris)
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(fallbackUri, UriKind.Absolute);
                            bitmap.EndInit();

                            imgLogo.Source = bitmap;
                            System.Diagnostics.Debug.WriteLine($"Loaded fallback logo: {fallbackUri}");
                            loaded = true;
                            break;
                        }
                        catch
                        {
                            // Continue to next fallback
                        }
                    }

                    if (!loaded)
                    {
                        System.Diagnostics.Debug.WriteLine("No logo resources found in Assets folder");
                        // Hide logo if no resources found
                        imgLogo.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading splash screen logo: {ex.Message}");
                // Hide logo on error
                imgLogo.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Loads the version number from VersionClass
        /// </summary>
        private void LoadVersion()
        {
            try
            {
                txtVersion.Text = VersionClass.GetVersion();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading version: {ex.Message}");
                txtVersion.Text = "Version: Unknown";
            }
        }

        /// <summary>
        /// Updates the status message displayed on the splash screen
        /// </summary>
        public void UpdateStatus(string status)
        {
            if (Dispatcher.CheckAccess())
            {
                txtStatus.Text = status;
            }
            else
            {
                Dispatcher.Invoke(() => txtStatus.Text = status);
            }
        }

        /// <summary>
        /// Shows the splash screen asynchronously and performs initialization
        /// </summary>
        public static async Task<SplashScreen> ShowAsync()
        {
            SplashScreen? splash = null;

            // Create and show splash screen on UI thread
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                splash = new SplashScreen();
                splash.Show();
            });

            // Small delay to ensure splash is visible
            await Task.Delay(100);

            return splash!;
        }

        /// <summary>
        /// Closes the splash screen with fade-out animation
        /// </summary>
        public async Task CloseAsync()
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                // Fade out animation
                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(300)
                };

                animation.Completed += (s, e) => Close();
                BeginAnimation(OpacityProperty, animation);

                // Wait for animation to complete
                await Task.Delay(300);
            });
        }
    }
}
