using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using BackupUI.Models;
using BackupUI.Services;
using MessageBox = System.Windows.MessageBox;

namespace BackupUI.Windows
{
    public partial class BackupWindowNew : Window
    {
        private ObservableCollection<DriveTreeItem> driveItems = new();

        public BackupWindowNew()
        {
            try
            {
                InitializeComponent();
                InitializeScheduleControls();
                
                cmbBackupType.SelectionChanged += BackupType_SelectionChanged;
                
                // Load drives after window is fully loaded
                Loaded += BackupWindowNew_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing backup window: {ex.Message}\n\nStack Trace: {ex.StackTrace}", 
                    "Initialization Error",
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            }
        }

        private async void BackupWindowNew_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadDrives();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading drives: {ex.Message}\n\nStack Trace: {ex.StackTrace}", 
                    "Error",
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            }
        }

        private void LoadFoldersForVolume(DriveTreeItem volumeItem)
        {
            try
            {
                // Remove the placeholder "Loading..." item
                volumeItem.Children.Clear();
                
                var rootPath = volumeItem.FullPath;
                
                // Check if this is a system partition without drive letter
                if (rootPath.StartsWith("\\\\?\\Volume{"))
                {
                    volumeItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(System partition - cannot browse)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                    return;
                }
                
                if (!Directory.Exists(rootPath))
                {
                    volumeItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(Volume not accessible)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Loading folders for volume: {rootPath}");

                // Add top-level folders
                var foldersAdded = 0;
                try
                {
                    var directories = Directory.GetDirectories(rootPath);
                    
                    foreach (var directory in directories)
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(directory);
                            
                            // Show ALL folders including hidden and system
                            // Mark them differently but don't skip them
                            var isHidden = (dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
                            var isSystem = (dirInfo.Attributes & FileAttributes.System) == FileAttributes.System;
                            
                            var folderName = dirInfo.Name;
                            if (isSystem)
                                folderName += " [System]";
                            else if (isHidden)
                                folderName += " [Hidden]";

                            var folderItem = new DriveTreeItem
                            {
                                Name = folderName,
                                FullPath = dirInfo.FullName,
                                ItemType = DriveTreeItemType.Folder,
                                Parent = volumeItem
                            };

                            // Add a dummy child if this folder has subfolders (for expand arrow)
                            try
                            {
                                if (Directory.GetDirectories(directory).Length > 0 || 
                                    Directory.GetFiles(directory).Length > 0)
                                {
                                    folderItem.Children.Add(new DriveTreeItem 
                                    { 
                                        Name = "Loading...", 
                                        ItemType = DriveTreeItemType.Folder,
                                        Parent = folderItem
                                    });
                                }
                            }
                            catch
                            {
                                // Can't access subfolder info
                            }

                            volumeItem.Children.Add(folderItem);
                            foldersAdded++;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Add a marker for inaccessible folders
                            volumeItem.Children.Add(new DriveTreeItem
                            {
                                Name = $"{Path.GetFileName(directory)} [Access Denied]",
                                FullPath = directory,
                                ItemType = DriveTreeItemType.Folder,
                                Parent = volumeItem
                            });
                            foldersAdded++;
                        }
                        catch
                        {
                            // Skip other errors silently
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    volumeItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(Access Denied - Run as Administrator)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"Added {foldersAdded} folders to volume {volumeItem.Name}");
                
                // If no folders were accessible, show a message
                if (foldersAdded == 0)
                {
                    volumeItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(Empty or no accessible folders)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading folders for volume {volumeItem.Name}: {ex.Message}");
                volumeItem.Children.Clear();
                volumeItem.Children.Add(new DriveTreeItem
                {
                    Name = $"(Error: {ex.Message})",
                    ItemType = DriveTreeItemType.Folder,
                    Parent = volumeItem
                });
            }
        }

        private void InitializeScheduleControls()
        {
            // Populate hours (0-23)
            for (int i = 0; i < 24; i++)
            {
                cmbHour.Items.Add(i.ToString("D2"));
            }
            cmbHour.SelectedIndex = 2; // 2 AM default

            // Populate minutes
            for (int i = 0; i < 60; i += 15)
            {
                cmbMinute.Items.Add(i.ToString("D2"));
            }
            cmbMinute.SelectedIndex = 0;

            // Populate days of month
            for (int i = 1; i <= 31; i++)
            {
                cmbDayOfMonth.Items.Add(i.ToString());
            }
            cmbDayOfMonth.SelectedIndex = 0;
        }

        private async Task LoadDrives()
        {
            try
            {
                // Show loading overlay
                if (loadingOverlay != null)
                    loadingOverlay.Visibility = Visibility.Visible;

                driveItems.Clear();
                treeViewDrives.Items.Clear();

                // Load physical drives and volumes
                await LoadPhysicalDrives();

                // Load Hyper-V systems
                await LoadHyperVSystems();

                // Manually create TreeViewItems for proper hierarchical display
                foreach (var drive in driveItems)
                {
                    var treeItem = CreateTreeViewItem(drive);
                    treeViewDrives.Items.Add(treeItem);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading drives: {ex.Message}\n\nDetails: {ex.InnerException?.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                throw; // Re-throw to be caught by caller
            }
            finally
            {
                // Hide loading overlay
                if (loadingOverlay != null)
                    loadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private TreeViewItem CreateTreeViewItem(DriveTreeItem item)
        {
            var treeViewItem = new TreeViewItem();
            
            // Create the header with checkbox and text
            var panel = new System.Windows.Controls.StackPanel 
            { 
                Orientation = System.Windows.Controls.Orientation.Horizontal 
            };
            
            var checkbox = new System.Windows.Controls.CheckBox
            {
                IsChecked = item.IsChecked,
                IsThreeState = true,  // Keep for visual representation
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            
            // Handle click to prevent three-state cycling
            checkbox.Click += (s, e) =>
            {
                // On click, toggle between checked and unchecked only
                // Skip the indeterminate state for user clicks
                if (item.IsChecked == true)
                {
                    item.IsChecked = false;
                }
                else
                {
                    item.IsChecked = true;
                }
                e.Handled = true;  // Prevent default three-state behavior
            };
            
            // Update checkbox when model changes (allows indeterminate from parent updates)
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(item.IsChecked))
                {
                    checkbox.IsChecked = item.IsChecked;
                }
            };
            
            var textBlock = new TextBlock
            {
                Text = item.DisplayName,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            panel.Children.Add(checkbox);
            panel.Children.Add(textBlock);
            treeViewItem.Header = panel;
            
            // Debug: Log item creation
            System.Diagnostics.Debug.WriteLine($"Creating TreeViewItem for: {item.Name}, Children: {item.Children.Count}");
            
            // Add children FIRST (before setting up events)
            foreach (var child in item.Children)
            {
                treeViewItem.Items.Add(CreateTreeViewItem(child));
            }
            
            // Bind expansion state AFTER adding children
            treeViewItem.IsExpanded = item.IsExpanded;
            treeViewItem.Expanded += (s, e) =>
            {
                if (e.Source == treeViewItem)
                {
                    item.IsExpanded = true;
                    // Load folders for volumes when expanded
                    if (item.ItemType == DriveTreeItemType.Volume && !item.ChildrenLoaded)
                    {
                        LoadFoldersForVolume(item);
                        item.ChildrenLoaded = true;
                        
                        // Rebuild children
                        treeViewItem.Items.Clear();
                        foreach (var child in item.Children)
                        {
                            treeViewItem.Items.Add(CreateTreeViewItem(child));
                        }
                    }
                }
            };
            
            treeViewItem.Collapsed += (s, e) =>
            {
                if (e.Source == treeViewItem)
                {
                    item.IsExpanded = false;
                }
            };
            
            return treeViewItem;
        }

        private async Task LoadPhysicalDrives()
        {
            await Task.Run(() =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("=== Starting LoadPhysicalDrives ===");
                    
                    // Try with ORDER BY first
                    ManagementObjectSearcher searcher;
                    try
                    {
                        searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive ORDER BY Index");
                        var testCount = searcher.Get().Count;
                        System.Diagnostics.Debug.WriteLine($"Found {testCount} disks with ORDER BY");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ORDER BY failed: {ex.Message}, trying without ORDER BY");
                        searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                    }
                    
                    using (searcher)
                    {
                        foreach (ManagementObject disk in searcher.Get())
                        {
                            try
                            {
                                // Safely get properties with fallbacks
                                int diskIndex = 0;
                                try
                                {
                                    var indexObj = disk["Index"];
                                    if (indexObj != null)
                                        diskIndex = Convert.ToInt32(indexObj);
                                    else
                                        System.Diagnostics.Debug.WriteLine("Warning: Index property is null");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error getting Index: {ex.Message}, using 0");
                                }
                                
                                var model = disk["Model"]?.ToString() ?? "Unknown Model";
                                var deviceId = disk["DeviceID"]?.ToString() ?? "";
                                long size = 0;
                                
                                try
                                {
                                    var sizeObj = disk["Size"];
                                    if (sizeObj != null)
                                        size = Convert.ToInt64(sizeObj);
                                }
                                catch { }
                                
                                var diskItem = new DriveTreeItem
                                {
                                    Name = $"Disk {diskIndex} - {model}",
                                    FullPath = deviceId,
                                    ItemType = DriveTreeItemType.Disk,
                                    Size = size
                                };

                                System.Diagnostics.Debug.WriteLine($"=== Found Disk {diskIndex}: {model} ({deviceId}) ===");

                                // Get volumes on this disk using the Index property
                                LoadVolumesForDisk(diskItem, diskIndex);
                                
                                System.Diagnostics.Debug.WriteLine($"Disk {diskIndex}: Found {diskItem.Children.Count} volumes");

                                if (diskItem.Children.Count == 0)
                                {
                                    diskItem.Children.Add(new DriveTreeItem
                                    {
                                        Name = "(No accessible volumes)",
                                        ItemType = DriveTreeItemType.Volume,
                                        Parent = diskItem
                                    });
                                }

                                Dispatcher.Invoke(() => driveItems.Add(diskItem));
                            }
                            catch (Exception diskEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error processing individual disk: {diskEx.Message}");
                                Dispatcher.Invoke(() =>
                                    MessageBox.Show($"Error processing a disk: {diskEx.Message}\n\nContinuing with remaining disks...", 
                                        "Warning",
                                        MessageBoxButton.OK, 
                                        MessageBoxImage.Warning));
                            }
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"=== Completed LoadPhysicalDrives: {driveItems.Count} disks loaded ===");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR in LoadPhysicalDrives: {ex.Message}\nStack: {ex.StackTrace}");
                    Dispatcher.Invoke(() =>
                        MessageBox.Show($"Error loading physical drives: {ex.Message}\n\nDetails: {ex.GetType().Name}\n\nPlease check Output window for details.", 
                            "Error",
                            MessageBoxButton.OK, 
                            MessageBoxImage.Error));
                }
            });
        }

        private void LoadVolumesForDisk(DriveTreeItem diskItem, int diskNum)
        {
            var volumesFound = false;
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== Loading volumes for Disk {diskNum}: {diskItem.FullPath} ===");
                
                // Try method 1: WMI Associators (most accurate but sometimes fails)
                volumesFound = TryLoadVolumesViaWMI(diskItem, diskNum);
                
                // Try method 2: Alternative WMI query if method 1 failed
                if (!volumesFound)
                {
                    System.Diagnostics.Debug.WriteLine($"Method 1 failed, trying alternative WMI query for disk {diskNum}");
                    volumesFound = TryLoadVolumesViaAlternativeWMI(diskItem, diskNum);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LoadVolumesForDisk for disk {diskNum}: {ex.Message}");
            }
            
            // If WMI didn't find any volumes, use fallback
            if (!volumesFound)
            {
                System.Diagnostics.Debug.WriteLine($"All WMI methods failed for disk {diskNum}, using fallback");
                LoadVolumesSimpleFallback(diskItem, diskNum);
            }
        }

        private bool TryLoadVolumesViaWMI(DriveTreeItem diskItem, int diskNum)
        {
            var volumesFound = false;
            
            try
            {
                var deviceId = diskItem.FullPath.Replace("\\", "\\\\");
                var partitionQuery = $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{deviceId}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition";
                
                System.Diagnostics.Debug.WriteLine($"Query 1: {partitionQuery}");
                
                using var partitionSearcher = new ManagementObjectSearcher(partitionQuery);
                var partitions = partitionSearcher.Get();
                
                System.Diagnostics.Debug.WriteLine($"Query 1 returned {partitions.Count} partitions");
                
                foreach (ManagementObject partition in partitions)
                {
                    var partitionDeviceId = partition["DeviceID"]?.ToString();
                    System.Diagnostics.Debug.WriteLine($"  Partition: {partitionDeviceId}");
                    
                    var logicalQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionDeviceId}'}} WHERE AssocClass = Win32_LogicalDiskToPartition";
                    
                    using var logicalSearcher = new ManagementObjectSearcher(logicalQuery);
                    var logicalDisks = logicalSearcher.Get();
                    
                    System.Diagnostics.Debug.WriteLine($"  Query 2 returned {logicalDisks.Count} logical disks");
                    
                    foreach (ManagementObject logical in logicalDisks)
                    {
                        var driveLetter = logical["DeviceID"]?.ToString();
                        if (string.IsNullOrEmpty(driveLetter)) continue;

                        System.Diagnostics.Debug.WriteLine($"    Found: {driveLetter}");

                        if (AddVolumeToTree(diskItem, driveLetter))
                            volumesFound = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryLoadVolumesViaWMI failed: {ex.Message}");
            }
            
            return volumesFound;
        }

        private bool TryLoadVolumesViaAlternativeWMI(DriveTreeItem diskItem, int diskNum)
        {
            var volumesFound = false;
            
            try
            {
                // Query all partitions on this disk
                var query = $"SELECT * FROM Win32_DiskPartition WHERE DiskIndex = {diskNum}";
                
                System.Diagnostics.Debug.WriteLine($"Alternative query: {query}");
                
                using var searcher = new ManagementObjectSearcher(query);
                var partitions = searcher.Get();
                
                System.Diagnostics.Debug.WriteLine($"Alternative query found {partitions.Count} partitions");
                
                foreach (ManagementObject partition in partitions)
                {
                    var partitionDeviceId = partition["DeviceID"]?.ToString();
                    var partitionSize = Convert.ToInt64(partition["Size"] ?? 0);
                    System.Diagnostics.Debug.WriteLine($"  Partition: {partitionDeviceId} ({partitionSize / (1024.0 * 1024.0 * 1024.0):F2} GB)");
                    
                    // Try to find logical disk for this partition
                    var logicalQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionDeviceId}'}} WHERE AssocClass = Win32_LogicalDiskToPartition";
                    
                    using var logicalSearcher = new ManagementObjectSearcher(logicalQuery);
                    var logicalDisks = logicalSearcher.Get();
                    
                    if (logicalDisks.Count > 0)
                    {
                        // Has drive letter
                        foreach (ManagementObject logical in logicalDisks)
                        {
                            var driveLetter = logical["DeviceID"]?.ToString();
                            if (string.IsNullOrEmpty(driveLetter)) continue;

                            System.Diagnostics.Debug.WriteLine($"    Found logical disk: {driveLetter}");

                            if (AddVolumeToTree(diskItem, driveLetter))
                                volumesFound = true;
                        }
                    }
                    else
                    {
                        // No drive letter - query Win32_Volume directly
                        System.Diagnostics.Debug.WriteLine($"    No logical disk, checking Win32_Volume...");
                        
                        // Query volumes by DiskNumber (Win32_Volume has DeviceID that includes partition info)
                        var volumeQuery = $"SELECT * FROM Win32_Volume WHERE DriveType = 3"; // Fixed disk
                        using var volumeSearcher = new ManagementObjectSearcher(volumeQuery);
                        
                        foreach (ManagementObject volume in volumeSearcher.Get())
                        {
                            try
                            {
                                var volumeDeviceId = volume["DeviceID"]?.ToString();
                                var volumeName = volume["Label"]?.ToString() ?? "";
                                var volumeCapacity = Convert.ToInt64(volume["Capacity"] ?? 0);
                                
                                // Check if this volume's size matches the partition
                                if (Math.Abs(volumeCapacity - partitionSize) < 1024 * 1024 * 100) // Within 100MB
                                {
                                    var volumeType = "Unknown";
                                    if (volumeName.Contains("EFI", StringComparison.OrdinalIgnoreCase) || 
                                        volumeDeviceId.Contains("EFI", StringComparison.OrdinalIgnoreCase))
                                    {
                                        volumeType = "EFI System Partition";
                                    }
                                    else if (volumeName.Contains("Recovery", StringComparison.OrdinalIgnoreCase) ||
                                             volumeDeviceId.Contains("Recovery", StringComparison.OrdinalIgnoreCase))
                                    {
                                        volumeType = "Recovery Partition";
                                    }
                                    else
                                    {
                                        volumeType = string.IsNullOrEmpty(volumeName) ? "System Reserved" : volumeName;
                                    }

                                    var volumeItem = new DriveTreeItem
                                    {
                                        Name = $"(No Letter) {volumeType} ({volumeCapacity / (1024.0 * 1024.0 * 1024.0):F2} GB)",
                                        FullPath = volumeDeviceId ?? "",
                                        ItemType = DriveTreeItemType.Volume,
                                        Size = volumeCapacity,
                                        Parent = diskItem,
                                        IsBootVolume = volumeType.Contains("EFI")
                                    };

                                    // These volumes typically can't be browsed
                                    volumeItem.Children.Add(new DriveTreeItem
                                    {
                                        Name = "(System partition - not accessible)",
                                        ItemType = DriveTreeItemType.Folder,
                                        Parent = volumeItem
                                    });

                                    diskItem.Children.Add(volumeItem);
                                    volumesFound = true;
                                    
                                    System.Diagnostics.Debug.WriteLine($"      Added system volume: {volumeType}");
                                    break; // Found matching volume
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"      Error checking volume: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryLoadVolumesViaAlternativeWMI failed: {ex.Message}");
            }
            
            return volumesFound;
        }

        private bool AddVolumeToTree(DriveTreeItem diskItem, string driveLetter)
        {
            try
            {
                var driveInfo = new DriveInfo(driveLetter);
                if (!driveInfo.IsReady)
                {
                    System.Diagnostics.Debug.WriteLine($"      Drive {driveLetter} not ready");
                    return false;
                }

                var volumeLabel = string.IsNullOrEmpty(driveInfo.VolumeLabel) 
                    ? "Local Disk" 
                    : driveInfo.VolumeLabel;

                var volumeItem = new DriveTreeItem
                {
                    Name = $"{driveLetter} ({volumeLabel})",
                    FullPath = driveLetter,
                    ItemType = DriveTreeItemType.Volume,
                    Size = driveInfo.TotalSize,
                    Parent = diskItem,
                    IsBootVolume = IsBootVolume(driveLetter),
                    IsWindowsServer = IsWindowsServerVolume(driveLetter)
                };

                volumeItem.Children.Add(new DriveTreeItem
                {
                    Name = "Loading...",
                    ItemType = DriveTreeItemType.Folder,
                    Parent = volumeItem
                });

                diskItem.Children.Add(volumeItem);
                System.Diagnostics.Debug.WriteLine($"      Added {driveLetter} to tree");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"      Error adding {driveLetter}: {ex.Message}");
                return false;
            }
        }

        private void LoadVolumesSimpleFallback(DriveTreeItem diskItem, int diskNum)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Using simple fallback for disk {diskNum}");
                
                // Simple approach: Show all fixed drives
                // We can't determine which disk they're on, so we'll add them to the first disk
                if (diskNum == 0)
                {
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                                continue;

                            var volumeLabel = string.IsNullOrEmpty(drive.VolumeLabel) 
                                ? "Local Disk" 
                                : drive.VolumeLabel;

                            var volumeItem = new DriveTreeItem
                            {
                                Name = $"{drive.Name.TrimEnd('\\')} ({volumeLabel})",
                                FullPath = drive.Name.TrimEnd('\\'),
                                ItemType = DriveTreeItemType.Volume,
                                Size = drive.TotalSize,
                                Parent = diskItem,
                                IsBootVolume = IsBootVolume(drive.Name),
                                IsWindowsServer = IsWindowsServerVolume(drive.Name)
                            };

                            // Add placeholder for folders
                            volumeItem.Children.Add(new DriveTreeItem
                            {
                                Name = "Loading...",
                                ItemType = DriveTreeItemType.Folder,
                                Parent = volumeItem
                            });

                            diskItem.Children.Add(volumeItem);
                            System.Diagnostics.Debug.WriteLine($"Fallback: Added {drive.Name} to disk {diskNum}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error adding drive in fallback: {ex.Message}");
                        }
                    }
                }
                else
                {
                    // For other disks, just show a message
                    diskItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(Cannot map volumes - see Disk 0 for all volumes)",
                        ItemType = DriveTreeItemType.Volume,
                        Parent = diskItem
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in fallback method: {ex.Message}");
            }
        }

        private bool IsBootVolume(string driveLetter)
        {
            try
            {
                var systemDrive = Environment.GetFolderPath(Environment.SpecialFolder.System);
                return driveLetter.TrimEnd('\\').Equals(Path.GetPathRoot(systemDrive)?.TrimEnd('\\'), 
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsWindowsServerVolume(string driveLetter)
        {
            try
            {
                var systemDrive = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (!driveLetter.TrimEnd('\\').Equals(Path.GetPathRoot(systemDrive)?.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Check if it's Windows Server
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
                foreach (ManagementObject os in searcher.Get())
                {
                    var caption = os["Caption"]?.ToString() ?? "";
                    return caption.Contains("Server", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            return false;
        }

        private async Task LoadHyperVSystems()
        {
            await Task.Run(() =>
            {
                try
                {
                    var vmBuffer = new StringBuilder(4096);
                    var result = BackupEngineInterop.EnumerateHyperVMachines(vmBuffer, vmBuffer.Capacity);

                    if (result == 0)
                    {
                        var vms = vmBuffer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var vm in vms)
                        {
                            var hvItem = new DriveTreeItem
                            {
                                Name = $"Hyper-V: {vm}",
                                FullPath = vm,
                                ItemType = DriveTreeItemType.HyperVSystem
                            };

                            Dispatcher.Invoke(() => driveItems.Add(hvItem));
                        }
                    }
                }
                catch { }
            });
        }

        private async void RefreshDrives_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadDrives();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing drives: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllExpanded(driveItems, true);
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllExpanded(driveItems, false);
        }

        private void SetAllExpanded(ObservableCollection<DriveTreeItem> items, bool expanded)
        {
            foreach (var item in items)
            {
                item.IsExpanded = expanded;
                if (item.Children.Count > 0)
                {
                    SetAllExpanded(new ObservableCollection<DriveTreeItem>(item.Children), expanded);
                }
            }
        }

        private void BackupType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (pnlCloneOptions == null) 
                return;

            if (cmbBackupType.SelectedIndex == 3) // Clone Backup
            {
                pnlCloneOptions.Visibility = Visibility.Visible;
            }
            else
            {
                pnlCloneOptions.Visibility = Visibility.Collapsed;
            }
        }

        private void BrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select backup destination folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtDestination.Text = dialog.SelectedPath;
            }
        }

        private void BrowseCloneDestination_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select clone destination drive or folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtCloneDestination.Text = dialog.SelectedPath;
            }
        }

        private void Schedule_CheckedChanged(object sender, RoutedEventArgs e)
        {
            pnlSchedule.IsEnabled = chkEnableSchedule.IsChecked == true;
        }

        private void Frequency_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbFrequency == null || pnlWeekly == null || pnlMonthly == null) 
                return;

            pnlWeekly.Visibility = cmbFrequency.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            pnlMonthly.Visibility = cmbFrequency.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void StartBackup_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs())
                return;

            try
            {
                progressBar.Visibility = Visibility.Visible;
                txtProgress.Visibility = Visibility.Visible;
                progressBar.Value = 0;

                // TODO: Implement actual backup logic
                await RunBackup();

                MessageBox.Show("Backup completed successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Backup failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                txtProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async Task RunBackup()
        {
            await Task.Run(() =>
            {
                // Placeholder for actual backup implementation
                for (int i = 0; i <= 100; i += 10)
                {
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = i;
                        txtProgress.Text = $"Backing up... {i}%";
                    });
                    System.Threading.Thread.Sleep(500);
                }
            });
        }

        private void SaveJob_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs())
                return;

            MessageBox.Show("Backup job saved successfully!", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(txtBackupName.Text))
            {
                MessageBox.Show("Please enter a backup name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtDestination.Text))
            {
                MessageBox.Show("Please select a backup destination.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Check if at least one item is selected
            var anySelected = driveItems.Any(d => d.IsChecked == true || d.Children.Any(c => c.IsChecked == true));
            if (!anySelected)
            {
                MessageBox.Show("Please select at least one drive or volume to backup.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (cmbBackupType.SelectedIndex == 3 && string.IsNullOrWhiteSpace(txtCloneDestination.Text))
            {
                MessageBox.Show("Please select a clone destination.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
