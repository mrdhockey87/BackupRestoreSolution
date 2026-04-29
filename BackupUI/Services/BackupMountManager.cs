using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SecureServerBackupCommon;

namespace SecureServerBackup.Services
{
    /// <summary>
    /// Manages mounting and unmounting backup virtual disks as drive letters
    /// </summary>
    public class BackupMountManager
    {
        private static readonly Dictionary<string, MountedBackup> _mountedBackups = new();
        private static readonly object _lockObject = new();

        /// <summary>
        /// Represents a mounted backup drive
        /// </summary>
        public class MountedBackup
        {
            public string BackupPath { get; set; } = "";
            public string DriveLetter { get; set; } = "";
            public string BackupName { get; set; } = "";
            public DateTime BackupDate { get; set; }
            public string BackupType { get; set; } = ""; // Full, Incremental, Differential
            public DateTime MountTime { get; set; }
            public bool IsReadOnly { get; set; } = true;
        }

        public static (bool Success, string DriveLetter, string Error) MountVirtualDiskReadOnly(string vhdxPath)
        {
            return MountVHDX(vhdxPath, readOnly: true);
        }

        public static (bool Success, string Error) UnmountVirtualDisk(string vhdxPath)
        {
            return UnmountVHDX(vhdxPath);
        }

        /// <summary>
        /// Mounts a backup VHDX/VHD file as a read-only drive
        /// </summary>
        public static (bool Success, string DriveLetter, string Error) MountBackup(
            string vhdxPath, 
            string backupName, 
            string backupType,
            DateTime backupDate)
        {
            lock (_lockObject)
            {
                try
                {
                    BackupLogger.LogInfo("BackupMount", $"Mounting backup: {backupName}", vhdxPath);

                    if (!File.Exists(vhdxPath))
                    {
                        return (false, "", $"Backup file not found: {vhdxPath}");
                    }

                    // Mount the VHDX using PowerShell
                    var (success, driveLetter, error) = MountVHDX(vhdxPath, readOnly: true);

                    if (!success)
                    {
                        BackupLogger.LogError("BackupMount", "Failed to mount backup", error);
                        return (false, "", error);
                    }

                    // Register the mounted backup
                    var mountedBackup = new MountedBackup
                    {
                        BackupPath = vhdxPath,
                        DriveLetter = driveLetter,
                        BackupName = backupName,
                        BackupDate = backupDate,
                        BackupType = backupType,
                        MountTime = DateTime.Now,
                        IsReadOnly = true
                    };

                    _mountedBackups[driveLetter] = mountedBackup;

                    // Set custom icon for the drive
                    SetCustomDriveIcon(driveLetter);

                    // Add registry entry for context menu unmount
                    RegisterContextMenu(driveLetter);

                    BackupLogger.LogSuccess("BackupMount", 
                        $"Backup mounted successfully as {driveLetter}:", 
                        $"{backupName} ({backupType}) - {backupDate:yyyy-MM-dd}");

                    return (true, driveLetter, "");
                }
                catch (Exception ex)
                {
                    BackupLogger.LogError("BackupMount", "Exception mounting backup", ex.Message);
                    return (false, "", ex.Message);
                }
            }
        }

        /// <summary>
        /// Unmounts a backup drive by drive letter
        /// </summary>
        public static (bool Success, string Error) UnmountBackup(string driveLetter)
        {
            lock (_lockObject)
            {
                try
                {
                    if (!_mountedBackups.ContainsKey(driveLetter))
                    {
                        return (false, "Drive not found in mounted backups");
                    }

                    var mountedBackup = _mountedBackups[driveLetter];

                    BackupLogger.LogInfo("BackupMount", 
                        $"Unmounting backup from {driveLetter}:", 
                        mountedBackup.BackupName);

                    // Unmount the VHDX
                    var (success, error) = UnmountVHDX(mountedBackup.BackupPath);

                    if (!success)
                    {
                        BackupLogger.LogError("BackupMount", "Failed to unmount backup", error);
                        return (false, error);
                    }

                    // Remove custom icon
                    RemoveCustomDriveIcon(driveLetter);

                    // Remove context menu registry
                    UnregisterContextMenu(driveLetter);

                    // Remove from tracking
                    _mountedBackups.Remove(driveLetter);

                    BackupLogger.LogSuccess("BackupMount", 
                        $"Backup unmounted successfully from {driveLetter}:", 
                        mountedBackup.BackupName);

                    return (true, "");
                }
                catch (Exception ex)
                {
                    BackupLogger.LogError("BackupMount", "Exception unmounting backup", ex.Message);
                    return (false, ex.Message);
                }
            }
        }

        /// <summary>
        /// Unmounts all mounted backups
        /// </summary>
        public static void UnmountAll()
        {
            lock (_lockObject)
            {
                var driveLetters = _mountedBackups.Keys.ToList();
                
                foreach (var driveLetter in driveLetters)
                {
                    UnmountBackup(driveLetter);
                }
            }
        }

        /// <summary>
        /// Gets list of currently mounted backups
        /// </summary>
        public static List<MountedBackup> GetMountedBackups()
        {
            lock (_lockObject)
            {
                return _mountedBackups.Values.ToList();
            }
        }

        /// <summary>
        /// Checks if a drive letter is a mounted backup
        /// </summary>
        public static bool IsMountedBackup(string driveLetter)
        {
            lock (_lockObject)
            {
                return _mountedBackups.ContainsKey(driveLetter);
            }
        }

        #region VHDX Mounting

        /// <summary>
        /// Mounts a VHDX file using PowerShell Mount-DiskImage
        /// </summary>
        private static (bool Success, string DriveLetter, string Error) MountVHDX(string vhdxPath, bool readOnly)
        {
            try
            {
                // Use PowerShell to mount the VHDX
                string script = $@"
                    $disk = Mount-DiskImage -ImagePath '{vhdxPath}' -Access ReadOnly -PassThru -ErrorAction Stop
                    $partition = Get-Partition -DiskNumber $disk.Number | Where-Object {{ $_.DriveLetter }} | Sort-Object Size -Descending | Select-Object -First 1
                    if ($partition) {{
                        $partition.DriveLetter + ':'
                    }}
                ";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    string driveLetter = output.Trim();
                    return (true, driveLetter, "");
                }
                else
                {
                    return (false, "", errors);
                }
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        /// <summary>
        /// Unmounts a VHDX file
        /// </summary>
        private static (bool Success, string Error) UnmountVHDX(string vhdxPath)
        {
            try
            {
                string script = $@"
                    Dismount-DiskImage -ImagePath '{vhdxPath}' -ErrorAction Stop
                ";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    return (true, "");
                }
                else
                {
                    return (false, errors);
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        #endregion

        #region Custom Drive Icon

        /// <summary>
        /// Sets a custom icon for the mounted backup drive
        /// </summary>
        private static void SetCustomDriveIcon(string driveLetter)
        {
            try
            {
                // Create custom icon in resources or use embedded icon
                string iconPath = GetBackupDriveIconPath();

                // Set the icon via registry
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\Explorer.exe\Drives\{driveLetter}\DefaultIcon");
                key?.SetValue("", iconPath);

                // Refresh Explorer
                RefreshExplorer();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set custom drive icon: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes custom drive icon
        /// </summary>
        private static void RemoveCustomDriveIcon(string driveLetter)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKey($@"Software\Classes\Applications\Explorer.exe\Drives\{driveLetter}\DefaultIcon", false);
                RefreshExplorer();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to remove custom drive icon: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the path to the backup drive icon
        /// </summary>
        private static string GetBackupDriveIconPath()
        {
            // Use system drive icon with overlay or custom icon
            // For now, use a distinctive system icon
            return @"%SystemRoot%\System32\imageres.dll,54"; // CD/DVD icon
        }

        #endregion

        #region Context Menu

        /// <summary>
        /// Registers context menu for unmounting via Explorer right-click
        /// </summary>
        private static void RegisterContextMenu(string driveLetter)
        {
            try
            {
                // Register in HKEY_CURRENT_USER for current user only
                string keyPath = $@"Software\Classes\Drive\shell\UnmountBackup";
                
                using (var shellKey = Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    shellKey?.SetValue("", "Unmount Backup Drive");
                    shellKey?.SetValue("Icon", @"%SystemRoot%\System32\imageres.dll,84"); // Eject icon
                }

                using (var commandKey = Registry.CurrentUser.CreateSubKey($@"{keyPath}\command"))
                {
                    string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    string command = $"\"{exePath}\" /unmount \"%1\"";
                    commandKey?.SetValue("", command);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to register context menu: {ex.Message}");
            }
        }

        /// <summary>
        /// Unregisters context menu
        /// </summary>
        private static void UnregisterContextMenu(string driveLetter)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Drive\shell\UnmountBackup", false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to unregister context menu: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        /// <summary>
        /// Refreshes Windows Explorer to show icon changes
        /// </summary>
        private static void RefreshExplorer()
        {
            try
            {
                // Notify shell of change
                SHChangeNotify(0x8000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // Ignore errors
            }
        }

        #endregion
    }
}
