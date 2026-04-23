using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Windows;

namespace BackupUI.Windows
{
    public partial class VolumeSelectionWindow : Window
    {
        public class VolumeInfo
        {
            public string VolumePath { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string FileSystem { get; set; } = string.Empty;
            public bool IsBootVolume { get; set; }
        }

        public VolumeInfo? SelectedVolume { get; private set; }
        private readonly HashSet<string> _excludedVolumes = new(StringComparer.OrdinalIgnoreCase);

        public VolumeSelectionWindow(IEnumerable<string>? excludedVolumes = null)
        {
            InitializeComponent();
            if (excludedVolumes != null)
            {
                foreach (var volume in excludedVolumes.Where(v => !string.IsNullOrWhiteSpace(v)))
                {
                    _excludedVolumes.Add(volume.TrimEnd('\\'));
                }
            }

            Loaded += VolumeSelectionWindow_Loaded;
        }

        private void VolumeSelectionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadVolumes();
        }

        private void LoadVolumes()
        {
            try
            {
                var volumes = new List<VolumeInfo>();
                using var searcher = new ManagementObjectSearcher("SELECT DeviceID, FileSystem, VolumeName, DriveLetter, BootVolume FROM Win32_Volume WHERE DriveType = 3");
                foreach (ManagementObject volume in searcher.Get())
                {
                    string driveLetter = volume["DriveLetter"]?.ToString() ?? string.Empty;
                    string deviceId = volume["DeviceID"]?.ToString() ?? string.Empty;
                    string normalized = !string.IsNullOrWhiteSpace(driveLetter)
                        ? driveLetter.TrimEnd('\\')
                        : deviceId.TrimEnd('\\');

                    if (string.IsNullOrWhiteSpace(normalized) || _excludedVolumes.Contains(normalized))
                    {
                        continue;
                    }

                    bool isBootVolume = false;
                    if (bool.TryParse(volume["BootVolume"]?.ToString(), out var boot))
                    {
                        isBootVolume = boot;
                    }

                    if (isBootVolume)
                    {
                        continue;
                    }

                    string label = volume["VolumeName"]?.ToString() ?? "Unnamed Volume";
                    string fileSystem = volume["FileSystem"]?.ToString() ?? "Unknown";
                    string display = !string.IsNullOrWhiteSpace(driveLetter)
                        ? $"{driveLetter} - {label} ({fileSystem})"
                        : $"{label} ({fileSystem})";

                    volumes.Add(new VolumeInfo
                    {
                        VolumePath = !string.IsNullOrWhiteSpace(driveLetter) ? driveLetter : deviceId,
                        DisplayName = display,
                        FileSystem = fileSystem,
                        IsBootVolume = isBootVolume
                    });
                }

                lstVolumes.ItemsSource = volumes.OrderBy(v => v.DisplayName).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading volumes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void VolumeList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            btnSelect.IsEnabled = lstVolumes.SelectedItem != null;
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            if (lstVolumes.SelectedItem is VolumeInfo volume)
            {
                SelectedVolume = volume;
                DialogResult = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
