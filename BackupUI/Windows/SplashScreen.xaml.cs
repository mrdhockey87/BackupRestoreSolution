using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BackupUI.Windows
{
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
            LoadLogo();
            LoadVersion();
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
