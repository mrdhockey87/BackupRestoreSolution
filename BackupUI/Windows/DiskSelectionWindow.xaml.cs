using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Windows;

namespace BackupUI.Windows
{
    public partial class DiskSelectionWindow : Window
    {
        public class DiskInfo
        {
            public int DiskIndex { get; set; }
            public string DisplayName { get; set; } = string.Empty;
            public string Details { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public string Model { get; set; } = string.Empty;
            public string DeviceId { get; set; } = string.Empty;
            public List<string> VolumeLetters { get; set; } = new();
        }

        public DiskInfo? SelectedDisk { get; private set; }
        private List<int> excludedDiskIndexes = new();

        public DiskSelectionWindow(List<int>? excludeDisks = null)
        {
            InitializeComponent();
            excludedDiskIndexes = excludeDisks ?? new List<int>();
            
            Loaded += DiskSelectionWindow_Loaded;
        }

        private void DiskSelectionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAvailableDisks();
        }

        private void LoadAvailableDisks()
        {
            try
            {
                var disks = new List<DiskInfo>();

                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject disk in searcher.Get())
                    {
                        try
                        {
                            int diskIndex = Convert.ToInt32(disk["Index"]);
                            
                            // Skip excluded disks (source disks)
                            if (excludedDiskIndexes.Contains(diskIndex))
                                continue;

                            long sizeBytes = Convert.ToInt64(disk["Size"]);
                            string model = disk["Model"]?.ToString() ?? "Unknown";
                            string deviceId = disk["DeviceID"]?.ToString() ?? "";
                            string interfaceType = disk["InterfaceType"]?.ToString() ?? "Unknown";

                            // Get volume letters for this disk
                            var volumeLetters = GetVolumeLettersForDisk(diskIndex);

                            string sizeStr = FormatSize(sizeBytes);
                            
                            // Build display name with volume letters if available
                            string displayName = $"Disk {diskIndex}: {model}";
                            if (volumeLetters.Count > 0)
                            {
                                displayName += $" ({string.Join(", ", volumeLetters)})";
                            }
                            else
                            {
                                displayName += " (Unallocated/No Volumes)";
                            }

                            // Build details string
                            string details = $"Size: {sizeStr} | Interface: {interfaceType}";
                            if (volumeLetters.Count > 0)
                            {
                                details += $" | Volumes: {string.Join(", ", volumeLetters)}";
                            }
                            else
                            {
                                details += " | Status: Unallocated or unformatted";
                            }

                            disks.Add(new DiskInfo
                            {
                                DiskIndex = diskIndex,
                                DisplayName = displayName,
                                Details = details,
                                SizeBytes = sizeBytes,
                                Model = model,
                                DeviceId = deviceId,
                                VolumeLetters = volumeLetters
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error processing disk: {ex.Message}");
                        }
                    }
                }

                // Sort by disk index
                disks = disks.OrderBy(d => d.DiskIndex).ToList();

                lstDisks.ItemsSource = disks;

                if (disks.Count == 0)
                {
                    MessageBox.Show("No available target disks found.\n\nAll disks may be in use as source disks.",
                        "No Disks Available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading disks: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Gets the volume letters associated with a disk
        /// </summary>
        private List<string> GetVolumeLettersForDisk(int diskIndex)
        {
            var volumeLetters = new List<string>();

            try
            {
                // First, get the DeviceID from Win32_DiskDrive for this disk index
                string? deviceId = null;
                using (var diskSearcher = new ManagementObjectSearcher($"SELECT DeviceID FROM Win32_DiskDrive WHERE Index = {diskIndex}"))
                {
                    foreach (ManagementObject disk in diskSearcher.Get())
                    {
                        deviceId = disk["DeviceID"]?.ToString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(deviceId))
                {
                    System.Diagnostics.Debug.WriteLine($"[GetVolumeLetters] Could not find DeviceID for disk {diskIndex}");
                    return volumeLetters;
                }

                System.Diagnostics.Debug.WriteLine($"[GetVolumeLetters] Disk {diskIndex} DeviceID: {deviceId}");

                // Query Win32_DiskDrive to Win32_DiskPartition associations
                string diskQuery = $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{deviceId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition";
                
                System.Diagnostics.Debug.WriteLine($"[GetVolumeLetters] Disk query: {diskQuery}");
                
                using (var partitionSearcher = new ManagementObjectSearcher(diskQuery))
                {
                    foreach (ManagementObject partition in partitionSearcher.Get())
                    {
                        try
                        {
                            // Query Win32_DiskPartition to Win32_LogicalDisk associations
                            string? partitionDeviceId = partition["DeviceID"]?.ToString();
                            
                            System.Diagnostics.Debug.WriteLine($"[GetVolumeLetters] Found partition: {partitionDeviceId}");
                            
                            if (string.IsNullOrEmpty(partitionDeviceId))
                                continue;
                            
                            string logicalQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionDeviceId}'}} WHERE AssocClass=Win32_LogicalDiskToPartition";
                            
                            using (var logicalSearcher = new ManagementObjectSearcher(logicalQuery))
                            {
                                foreach (ManagementObject logical in logicalSearcher.Get())
                                {
                                    try
                                    {
                                        string? driveLetter = logical["DeviceID"]?.ToString();
                                        if (!string.IsNullOrEmpty(driveLetter))
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[GetVolumeLetters] Found volume: {driveLetter}");
                                            volumeLetters.Add(driveLetter);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[GetVolumeLetters] Error reading logical disk: {ex.Message}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[GetVolumeLetters] Error querying logical disks for partition: {ex.Message}");
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[GetVolumeLetters] Disk {diskIndex} total volumes found: {volumeLetters.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetVolumeLetters] Error getting volume letters for disk {diskIndex}: {ex.Message}");
            }

            return volumeLetters;
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void DiskList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            btnSelect.IsEnabled = lstDisks.SelectedItem != null;
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            if (lstDisks.SelectedItem is DiskInfo disk)
            {
                // Confirm selection
                var result = MessageBox.Show(
                    $"You have selected:\n\n{disk.DisplayName}\n{disk.Details}\n\n" +
                    $"?? WARNING: All data on this disk will be REPLACED!\n\n" +
                    $"Are you sure you want to use this disk as the clone target?",
                    "Confirm Target Disk",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result == MessageBoxResult.Yes)
                {
                    SelectedDisk = disk;
                    DialogResult = true;
                    Close();
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
