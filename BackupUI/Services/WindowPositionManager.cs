using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace SecureServerBackup.Services
{
    /// <summary>
    /// Manages window position persistence for the main window
    /// </summary>
    public static class WindowPositionManager
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BackupRestoreApp",
            "window-position.json");

        /// <summary>
        /// Saves the main window's position and size
        /// </summary>
        public static void SaveMainWindowPosition(Window window)
        {
            try
            {
                var position = new WindowPosition
                {
                    Left = window.Left,
                    Top = window.Top,
                    Width = window.Width,
                    Height = window.Height,
                    WindowState = window.WindowState
                };

                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

                // Save to JSON
                var json = JsonSerializer.Serialize(position, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save window position: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores the main window's position and size
        /// </summary>
        public static void RestoreMainWindowPosition(Window window)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    // First run - center window on screen
                    CenterWindow(window);
                    return;
                }

                var json = File.ReadAllText(SettingsPath);
                var position = JsonSerializer.Deserialize<WindowPosition>(json);

                if (position != null && IsPositionValid(position))
                {
                    window.Left = position.Left;
                    window.Top = position.Top;
                    window.Width = position.Width;
                    window.Height = position.Height;
                    window.WindowState = position.WindowState;
                }
                else
                {
                    // Invalid position - center window
                    CenterWindow(window);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to restore window position: {ex.Message}");
                // Fall back to centering window
                CenterWindow(window);
            }
        }

        /// <summary>
        /// Validates that the saved position is visible on current screen configuration
        /// </summary>
        private static bool IsPositionValid(WindowPosition position)
        {
            // Check if window is within any screen bounds
            var rect = new Rect(position.Left, position.Top, position.Width, position.Height);

            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var workingArea = new Rect(
                    screen.WorkingArea.Left,
                    screen.WorkingArea.Top,
                    screen.WorkingArea.Width,
                    screen.WorkingArea.Height);

                // Check if at least part of the window is visible
                if (workingArea.IntersectsWith(rect))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Centers the window on the primary screen
        /// </summary>
        private static void CenterWindow(Window window)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        /// <summary>
        /// Configures a child window to open relative to the main window
        /// </summary>
        public static void SetChildWindowPosition(Window childWindow, Window mainWindow)
        {
            childWindow.Owner = mainWindow;
            childWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        private class WindowPosition
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public WindowState WindowState { get; set; }
        }
    }
}
