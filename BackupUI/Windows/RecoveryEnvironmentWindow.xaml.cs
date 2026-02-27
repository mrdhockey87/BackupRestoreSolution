using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Documents;

namespace BackupUI.Windows
{
    public partial class RecoveryEnvironmentWindow : Window
    {
        private string isoPath = string.Empty;

        public RecoveryEnvironmentWindow()
        {
            InitializeComponent();
            CheckIsoFile();
        }

        private void CheckIsoFile()
        {
            try
            {
                // Get application directory
                var appDir = AppDomain.CurrentDomain.BaseDirectory;

                // Look for ISO in LinuxRestore subdirectory
                var linuxRestoreDir = Path.Combine(appDir, "LinuxRestore");
                isoPath = Path.Combine(linuxRestoreDir, "BackupRestore_Recovery.iso");

                // Display the path
                txtIsoPath.Text = isoPath;

                // Check if file exists
                if (File.Exists(isoPath))
                {
                    var fileInfo = new FileInfo(isoPath);
                    txtIsoStatus.Text = $"✓ ISO Found ({FormatBytes(fileInfo.Length)})";
                    txtIsoStatus.Foreground = System.Windows.Media.Brushes.Green;
                    btnOpenIsoLocation.IsEnabled = true;
                    txtIsoNote.Text = "";
                }
                else
                {
                    txtIsoStatus.Text = "✗ ISO File Not Found";
                    txtIsoStatus.Foreground = System.Windows.Media.Brushes.Red;
                    btnOpenIsoLocation.IsEnabled = false;
                    txtIsoNote.Text = "Note: The ISO file should be created during application deployment. " +
                        "If missing, you can build it manually using the BUILD-AND-CREATE-ISO.ps1 script in the LinuxRestore directory.";
                }
            }
            catch (Exception ex)
            {
                txtIsoStatus.Text = $"✗ Error: {ex.Message}";
                txtIsoStatus.Foreground = System.Windows.Media.Brushes.Red;
                btnOpenIsoLocation.IsEnabled = false;
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        private void OpenRufusWebsite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://rufus.ie",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open website: {ex.Message}\n\nPlease manually visit: https://rufus.ie",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RufusDownload_Click(object sender, RoutedEventArgs e)
        {
            OpenRufusWebsite_Click(sender, e);
        }

        private void OpenIsoLocation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(isoPath))
                {
                    // Open folder and select the ISO file
                    Process.Start("explorer.exe", $"/select,\"{isoPath}\"");
                }
                else
                {
                    MessageBox.Show("ISO file not found. Please ensure the file exists at the specified location.",
                        "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open folder: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrintInstructions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create a simple HTML file with instructions for printing
                var tempFile = Path.Combine(Path.GetTempPath(), "RecoveryUSB_Instructions.html");

                var html = GenerateInstructionsHtml();
                File.WriteAllText(tempFile, html);

                // Open in default browser for printing
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create printable instructions: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GenerateInstructionsHtml()
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <title>Recovery USB Creation Instructions</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 40px; line-height: 1.6; }}
        h1 {{ color: #008B8B; border-bottom: 3px solid #008B8B; padding-bottom: 10px; }}
        h2 {{ color: #008B8B; margin-top: 30px; }}
        .step {{ background: #E0F7F7; padding: 20px; margin: 20px 0; border-radius: 5px; }}
        .step-number {{ display: inline-block; width: 30px; height: 30px; background: #008B8B; 
                       color: white; text-align: center; line-height: 30px; border-radius: 50%; 
                       font-weight: bold; margin-right: 10px; }}
        .warning {{ background: #FFE4E1; padding: 15px; border-left: 5px solid #8B0000; margin: 15px 0; }}
        .code {{ background: #F5F5F5; padding: 10px; font-family: Consolas, monospace; 
                border-radius: 3px; margin: 10px 0; }}
        .option {{ background: #E6F4EA; padding: 15px; margin: 10px 0; border-radius: 5px; }}
        ul {{ margin-left: 20px; }}
        @media print {{
            body {{ margin: 20px; }}
            .no-print {{ display: none; }}
        }}
    </style>
</head>
<body>
    <h1>Recovery USB Creation Instructions</h1>
    <p>Follow these steps to create a bootable USB drive with LinuxRestore tools.</p>

    <div class=""step"">
        <h2><span class=""step-number"">1</span>Download Rufus</h2>
        <ul>
            <li>Visit <strong>https://rufus.ie</strong></li>
            <li>Click ""Download"" button (latest version)</li>
            <li>Save rufus.exe to your Downloads folder (no installation needed)</li>
        </ul>
    </div>

    <div class=""step"">
        <h2><span class=""step-number"">2</span>Locate Recovery ISO File</h2>
        <p>The recovery ISO file is located at:</p>
        <div class=""code"">{isoPath}</div>
    </div>

    <div class=""step"">
        <h2><span class=""step-number"">3</span>Create Bootable USB with Rufus</h2>
        <div class=""warning"">
            <strong>⚠️ WARNING:</strong> This will erase ALL data on the USB drive!
        </div>
        <ol>
            <li><strong>Insert USB Drive</strong> (Minimum 2 GB, 4 GB+ recommended)</li>
            <li><strong>Run Rufus</strong> - Double-click rufus.exe, allow admin permissions</li>
            <li><strong>Configure Settings:</strong>
                <ul>
                    <li><strong>Device:</strong> Select your USB drive</li>
                    <li><strong>Boot selection:</strong> Click ""SELECT"" and choose BackupRestore_Recovery.iso</li>
                    <li><strong>Partition scheme:</strong> MBR (for BIOS/UEFI compatibility)</li>
                    <li><strong>File system:</strong> FAT32 (default)</li>
                </ul>
            </li>
            <li><strong>Start Creation:</strong>
                <ul>
                    <li>Click ""START"" button</li>
                    <li>If prompted, select ""Write in DD Image mode""</li>
                    <li>Wait for completion (2-5 minutes)</li>
                </ul>
            </li>
        </ol>
    </div>

    <div class=""step"">
        <h2><span class=""step-number"">4</span>Boot from Recovery USB</h2>
        <ol>
            <li>Insert USB drive into computer</li>
            <li>Restart computer</li>
            <li>Press boot menu key (F12, F2, DEL, or ESC)</li>
            <li>Select USB drive from boot menu</li>
        </ol>
    </div>

    <div class=""step"">
        <h2><span class=""step-number"">5</span>Using the 3 Restore Options</h2>

        <div class=""option"">
            <h3>Option 1: Graphical Interface (restore_gui) - EASIEST</h3>
            <div class=""code"">$ ./restore_gui</div>
            <ul>
                <li>Browse for backup (.ssb file or folder)</li>
                <li>Select destination (partition or directory)</li>
                <li>Click ""Restore"" button</li>
            </ul>
        </div>

        <div class=""option"">
            <h3>Option 2: Terminal UI (restore_tui) - RECOMMENDED</h3>
            <div class=""code"">$ ./restore_tui</div>
            <ul>
                <li>Use arrow keys to navigate menus</li>
                <li>Enter to select options</li>
                <li>Follow 3-step wizard: Select backup → Choose destination → Confirm</li>
            </ul>
        </div>

        <div class=""option"">
            <h3>Option 3: Command Line (restore_cli) - ADVANCED</h3>
            <div class=""code"">$ ./restore_cli /media/backups/ServerBackup_Full.ssb /mnt/sda1</div>
            <ul>
                <li>First argument: backup source (.ssb file or folder)</li>
                <li>Second argument: destination path</li>
                <li>No prompts - runs immediately</li>
            </ul>
        </div>
    </div>

    <button class=""no-print"" onclick=""window.print()""
            style=""background:#008B8B; color:white; padding:15px 30px; border:none; 
                   border-radius:5px; font-size:16px; cursor:pointer; margin:20px 0;"">
        Print Instructions
    </button>
</body>
</html>";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
