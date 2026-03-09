using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace BackupUI.Windows
{
    public partial class TempPathSelectionDialog : Window
    {
        public string SelectedTempPath { get; private set; }

        public TempPathSelectionDialog()
        {
            InitializeComponent();
            
            // Set default temp path
            SelectedTempPath = Path.GetTempPath();
            txtTempPath.Text = SelectedTempPath;
            
            // Update space info
            UpdateSpaceInfo();
        }

        private void UpdateSpaceInfo()
        {
            try
            {
                if (string.IsNullOrEmpty(SelectedTempPath))
                {
                    txtSpaceInfo.Text = "No path selected";
                    return;
                }

                // Get drive info
                string root = Path.GetPathRoot(SelectedTempPath);
                if (string.IsNullOrEmpty(root))
                {
                    txtSpaceInfo.Text = "Cannot determine drive";
                    return;
                }

                DriveInfo drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    long freeGB = drive.AvailableFreeSpace / (1024 * 1024 * 1024);
                    long totalGB = drive.TotalSize / (1024 * 1024 * 1024);
                    
                    txtSpaceInfo.Text = $"Drive {drive.Name} - Free: {freeGB:N0} GB / Total: {totalGB:N0} GB";
                    
                    // Warning if less than 10GB free
                    if (freeGB < 10)
                    {
                        txtSpaceInfo.Foreground = System.Windows.Media.Brushes.DarkOrange;
                        txtSpaceInfo.Text += " ⚠️ Low disk space! Consider using a different drive.";
                    }
                    else
                    {
                        txtSpaceInfo.Foreground = (System.Windows.Media.Brush)FindResource("SecondaryText");
                    }
                }
                else
                {
                    txtSpaceInfo.Text = $"Drive {drive.Name} is not ready";
                }
            }
            catch (Exception ex)
            {
                txtSpaceInfo.Text = $"Error checking space: {ex.Message}";
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select temporary directory for WIM mount operations";
                dialog.SelectedPath = SelectedTempPath;
                dialog.ShowNewFolderButton = true;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SelectedTempPath = dialog.SelectedPath;
                    
                    // Ensure path ends with backslash
                    if (!SelectedTempPath.EndsWith("\\"))
                    {
                        SelectedTempPath += "\\";
                    }
                    
                    txtTempPath.Text = SelectedTempPath;
                    UpdateSpaceInfo();
                }
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Validate path
            if (string.IsNullOrEmpty(SelectedTempPath))
            {
                System.Windows.MessageBox.Show("Please select a temporary path.",
                    "No Path Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Check if directory exists or can be created
            try
            {
                if (!Directory.Exists(SelectedTempPath))
                {
                    var result = System.Windows.MessageBox.Show(
                        $"Directory does not exist:\n{SelectedTempPath}\n\nCreate it now?",
                        "Create Directory",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        Directory.CreateDirectory(SelectedTempPath);
                    }
                    else
                    {
                        return;
                    }
                }

                // Test write access
                string testFile = Path.Combine(SelectedTempPath, $"_wim_test_{Guid.NewGuid()}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Cannot use selected path:\n{ex.Message}\n\nPlease select a different location.",
                    "Invalid Path",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
