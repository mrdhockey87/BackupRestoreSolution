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
        private readonly JobManager jobManager = new();
        private BackupJob? existingJob = null;

        public BackupWindowNew()
        {
            InitializeWindow();
        }

        public BackupWindowNew(BackupJob job)
        {
            existingJob = job;
            InitializeWindow();
            LoadJobData(job);
        }

        private void InitializeWindow()
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

        private void LoadJobData(BackupJob job)
        {
            // Set window title
            this.Title = $"Edit Backup - {job.Name}";

            // Load basic info
            txtBackupName.Text = job.Name;
            txtDestination.Text = job.DestinationPath;

            // Set backup type
            cmbBackupType.SelectedIndex = (int)job.Type;

            // Set options
            chkCompress.IsChecked = job.CompressData;
            chkVerify.IsChecked = job.VerifyAfterBackup;

            if (job.Target == BackupTarget.Disk || job.Target == BackupTarget.Volume)
            {
                // TODO: Pre-select drives/volumes in tree
                // This will require matching SourcePaths to tree items after LoadDrives completes
            }

            // Load schedule
            if (job.Schedule != null)
            {
                chkEnableSchedule.IsChecked = job.Schedule.Enabled;
                cmbFrequency.SelectedIndex = (int)job.Schedule.Frequency;
                cmbHour.SelectedItem = job.Schedule.Time.Hours.ToString("D2");
                cmbMinute.SelectedItem = job.Schedule.Time.Minutes.ToString("D2");

                if (job.Schedule.Frequency == ScheduleFrequency.Monthly)
                {
                    cmbDayOfMonth.SelectedItem = job.Schedule.DayOfMonth;
                }
                else if (job.Schedule.Frequency == ScheduleFrequency.Weekly)
                {
                    chkMonday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Monday);
                    chkTuesday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Tuesday);
                    chkWednesday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Wednesday);
                    chkThursday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Thursday);
                    chkFriday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Friday);
                    chkSaturday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Saturday);
                    chkSunday.IsChecked = job.Schedule.DaysOfWeek.Contains(DayOfWeek.Sunday);
                }
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
                
                System.Diagnostics.Debug.WriteLine($"=== LoadFoldersForVolume ===");
                System.Diagnostics.Debug.WriteLine($"Volume: {volumeItem.Name}");
                System.Diagnostics.Debug.WriteLine($"Path: '{rootPath}'");
                
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
                    System.Diagnostics.Debug.WriteLine($"ERROR: Directory does not exist: '{rootPath}'");
                    volumeItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(Volume not accessible)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Directory exists, enumerating folders...");

                // Add top-level folders
                var foldersAdded = 0;
                try
                {
                    var directories = Directory.GetDirectories(rootPath);
                    System.Diagnostics.Debug.WriteLine($"Found {directories.Length} directories");
                    
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
                            
                            System.Diagnostics.Debug.WriteLine($"  Added: {folderName}");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            // Add a marker for inaccessible folders
                            var folderName = $"{Path.GetFileName(directory)} [Access Denied]";
                            volumeItem.Children.Add(new DriveTreeItem
                            {
                                Name = folderName,
                                FullPath = directory,
                                ItemType = DriveTreeItemType.Folder,
                                Parent = volumeItem
                            });
                            foldersAdded++;
                            System.Diagnostics.Debug.WriteLine($"  Access denied: {Path.GetFileName(directory)}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"  Error processing folder {directory}: {ex.Message}");
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: Access denied to volume root");
                    volumeItem.Children.Add(new DriveTreeItem
                    {
                        Name = "(Access Denied - Run as Administrator)",
                        ItemType = DriveTreeItemType.Folder,
                        Parent = volumeItem
                    });
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"Total folders added: {foldersAdded}");
                
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
                System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR in LoadFoldersForVolume: {ex.Message}\nStack: {ex.StackTrace}");
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

                // Ensure the FullPath has a trailing backslash for directory enumeration
                var volumePath = driveLetter.TrimEnd('\\') + "\\";

                var volumeItem = new DriveTreeItem
                {
                    Name = $"{driveLetter} ({volumeLabel})",
                    FullPath = volumePath,  // Changed: Now includes trailing backslash (e.g., "E:\")
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
                System.Diagnostics.Debug.WriteLine($"      Added {driveLetter} to tree (path: {volumePath})");
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

                            // Ensure trailing backslash for directory enumeration
                            var volumePath = drive.Name.TrimEnd('\\') + "\\";
                            var displayName = drive.Name.TrimEnd('\\');

                            var volumeItem = new DriveTreeItem
                            {
                                Name = $"{displayName} ({volumeLabel})",
                                FullPath = volumePath,  // Changed: Now includes trailing backslash
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
                            System.Diagnostics.Debug.WriteLine($"Fallback: Added {drive.Name} to disk {diskNum} (path: {volumePath})");
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

            // Show clone options for both clone types (index 3 = Clone to Disk, index 4 = Clone to Virtual Disk)
            if (cmbBackupType.SelectedIndex == 3 || cmbBackupType.SelectedIndex == 4)
            {
                pnlCloneOptions.Visibility = Visibility.Visible;
                
                // Update label based on clone type
                if (txtCloneDestinationLabel != null)
                {
                    if (cmbBackupType.SelectedIndex == 3)
                        txtCloneDestinationLabel.Text = "Clone to Physical Disk:";
                    else
                        txtCloneDestinationLabel.Text = "Clone to Virtual Disk (.vhdx):";
                }
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

                var job = CreateJobFromInput();

                await ExecuteBackupJob(job);

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

        private async Task ExecuteBackupJob(BackupJob job)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Create progress callback
                    BackupEngineInterop.ProgressCallback progressCallback = (percentage, message) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            progressBar.Value = percentage;
                            txtProgress.Text = message ?? $"Progress: {percentage}%";
                        });
                    };

                    int result = -1;

                    // Execute based on job type
                    if (job.IsHyperVBackup && job.HyperVMachines.Count > 0)
                    {
                        // Hyper-V VM backup
                        foreach (var vmName in job.HyperVMachines)
                        {
                            var vmDestPath = Path.Combine(job.DestinationPath, vmName);
                            
                            Dispatcher.Invoke(() =>
                            {
                                txtProgress.Text = $"Backing up Hyper-V VM: {vmName}...";
                            });

                            result = BackupEngineInterop.BackupHyperVVM(
                                vmName,
                                vmDestPath,
                                progressCallback);

                            if (result != 0)
                            {
                                var errorBuffer = new StringBuilder(4096);
                                BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                throw new Exception($"Hyper-V backup failed: {errorBuffer}");
                            }
                        }
                    }
                    else if (job.Target == BackupTarget.Disk)
                    {
                        // Disk backup
                        foreach (var diskPath in job.SourcePaths)
                        {
                            // Extract disk number from path (e.g., "\\.\PHYSICALDRIVE0" -> 0)
                            var diskNumStr = diskPath.Replace("\\\\?\\PHYSICALDRIVE", "").Replace("\\\\.\\PHYSICALDRIVE", "");
                            if (int.TryParse(diskNumStr, out int diskNum))
                            {
                                var diskDestPath = Path.Combine(job.DestinationPath, $"Disk{diskNum}");
                                
                                result = BackupEngineInterop.BackupDisk(
                                    diskNum,
                                    diskDestPath,
                                    job.IncludeSystemState,
                                    job.CompressData,
                                    progressCallback);

                                if (result != 0)
                                {
                                    var errorBuffer = new StringBuilder(4096);
                                    BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                    throw new Exception($"Disk backup failed: {errorBuffer}");
                                }
                            }
                        }
                    }
                    else if (job.Target == BackupTarget.Volume)
                    {
                        // Volume backup
                        foreach (var volumePath in job.SourcePaths)
                        {
                            var volumeName = volumePath.TrimEnd('\\').Replace(":", "");
                            var volumeDestPath = Path.Combine(job.DestinationPath, volumeName);
                            
                            result = BackupEngineInterop.BackupVolume(
                                volumePath,
                                volumeDestPath,
                                job.IncludeSystemState,
                                job.CompressData,
                                progressCallback);

                            if (result != 0)
                            {
                                var errorBuffer = new StringBuilder(4096);
                                BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                throw new Exception($"Volume backup failed: {errorBuffer}");
                            }
                        }
                    }
                    else if (job.Target == BackupTarget.FilesAndFolders)
                    {
                        // Files/Folders backup
                        switch (job.Type)
                        {
                            case BackupType.Full:
                                foreach (var sourcePath in job.SourcePaths)
                                {
                                    result = BackupEngineInterop.BackupFiles(
                                        sourcePath,
                                        job.DestinationPath,
                                        progressCallback);

                                    if (result != 0)
                                    {
                                        var errorBuffer = new StringBuilder(4096);
                                        BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                        throw new Exception($"File backup failed: {errorBuffer}");
                                    }
                                }
                                break;

                            case BackupType.Incremental:
                                // TODO: Find last backup in destination
                                var lastBackup = FindLastBackup(job.DestinationPath);
                                
                                foreach (var sourcePath in job.SourcePaths)
                                {
                                    result = BackupEngineInterop.CreateIncrementalBackup(
                                        sourcePath,
                                        job.DestinationPath,
                                        lastBackup ?? job.DestinationPath,
                                        progressCallback);

                                    if (result != 0)
                                    {
                                        var errorBuffer = new StringBuilder(4096);
                                        BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                        throw new Exception($"Incremental backup failed: {errorBuffer}");
                                    }
                                }
                                break;

                            case BackupType.Differential:
                                var fullBackup = FindFullBackup(job.DestinationPath);
                                
                                foreach (var sourcePath in job.SourcePaths)
                                {
                                    result = BackupEngineInterop.CreateDifferentialBackup(
                                        sourcePath,
                                        job.DestinationPath,
                                        fullBackup ?? job.DestinationPath,
                                        progressCallback);

                                    if (result != 0)
                                    {
                                        var errorBuffer = new StringBuilder(4096);
                                        BackupEngineInterop.GetLastErrorMessage(errorBuffer, errorBuffer.Capacity);
                                        throw new Exception($"Differential backup failed: {errorBuffer}");
                                    }
                                }
                                break;
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = 100;
                        txtProgress.Text = "Backup completed!";
                    });
                }
                catch (Exception ex)
                {
                    throw new Exception($"Backup execution failed: {ex.Message}", ex);
                }
            });
        }

        private string? FindLastBackup(string destPath)
        {
            // TODO: Implement logic to find the most recent backup in destination
            // For now, return null to trigger full backup
            return null;
        }

        private string? FindFullBackup(string destPath)
        {
            // TODO: Implement logic to find the base full backup in destination
            // For now, return null to trigger full backup
            return null;
        }

        private void SaveJob_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs())
                return;

            try
            {
                var job = CreateJobFromInput();

                // If editing, preserve the ID
                if (existingJob != null)
                {
                    job.Id = existingJob.Id;
                    jobManager.UpdateJob(job);
                    MessageBox.Show($"Backup job '{job.Name}' updated successfully!\n\nJob saved to:\nC:\\ProgramData\\BackupRestoreService\\jobs.json", 
                        "Success",
                        MessageBoxButton.OK, 
                        MessageBoxImage.Information);
                }
                else
                {
                    jobManager.AddJob(job);
                    MessageBox.Show($"Backup job '{job.Name}' created successfully!\n\nJob saved to:\nC:\\ProgramData\\BackupRestoreService\\jobs.json", 
                        "Success",
                        MessageBoxButton.OK, 
                        MessageBoxImage.Information);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ERROR: Failed to save backup job!\n\n{ex.Message}\n\nPlease check:\n" +
                    "1. You have administrator rights\n" +
                    "2. C:\\ProgramData folder is accessible\n" +
                    "3. Antivirus is not blocking the save\n\n" +
                    $"Technical details:\n{ex.InnerException?.Message}", 
                    "Save Failed",
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                
                System.Diagnostics.Debug.WriteLine($"SaveJob failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private BackupJob CreateJobFromInput()
        {
            var job = new BackupJob
            {
                Id = Guid.NewGuid(),
                Name = txtBackupName.Text,
                Type = (BackupType)cmbBackupType.SelectedIndex,
                DestinationPath = txtDestination.Text,
                CompressData = chkCompress.IsChecked == true,
                VerifyAfterBackup = chkVerify.IsChecked == true
            };

            // Collect selected items from tree
            CollectSelectedItems(job);

            // Schedule
            if (chkEnableSchedule.IsChecked == true)
            {
                job.Schedule = new BackupSchedule
                {
                    JobId = job.Id,
                    Enabled = true,
                    Frequency = (ScheduleFrequency)cmbFrequency.SelectedIndex,
                    Time = new TimeSpan(
                        int.Parse(cmbHour.SelectedItem?.ToString() ?? "2"),
                        int.Parse(cmbMinute.SelectedItem?.ToString() ?? "0"),
                        0)
                };

                if (job.Schedule.Frequency == ScheduleFrequency.Weekly)
                {
                    if (chkMonday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Monday);
                    if (chkTuesday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Tuesday);
                    if (chkWednesday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Wednesday);
                    if (chkThursday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Thursday);
                    if (chkFriday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Friday);
                    if (chkSaturday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Saturday);
                    if (chkSunday.IsChecked == true) job.Schedule.DaysOfWeek.Add(DayOfWeek.Sunday);
                }
                else if (job.Schedule.Frequency == ScheduleFrequency.Monthly)
                {
                    job.Schedule.DayOfMonth = int.Parse(cmbDayOfMonth.SelectedItem?.ToString() ?? "1");
                }
            }

            return job;
        }

        private void CollectSelectedItems(BackupJob job)
        {
            foreach (var drive in driveItems)
            {
                if (drive.IsChecked == true)
                {
                    // Whole disk selected
                    if (drive.ItemType == DriveTreeItemType.Disk)
                    {
                        job.Target = BackupTarget.Disk;
                        job.SourcePaths.Add(drive.FullPath);
                    }
                    else if (drive.ItemType == DriveTreeItemType.HyperVSystem)
                    {
                        job.IsHyperVBackup = true;
                        job.HyperVMachines.Add(drive.FullPath);
                    }
                }
                else if (drive.IsChecked == null && drive.Children.Count > 0)
                {
                    // Partial selection - check children
                    CollectSelectedChildren(drive, job);
                }
            }

            // Determine target type if not already set
            if (job.Target == 0 && job.SourcePaths.Count > 0)
            {
                // Check if all sources are drive letters (volumes) or paths (files/folders)
                var firstPath = job.SourcePaths[0];
                if (firstPath.Length <= 3 && firstPath.EndsWith(":"))
                {
                    job.Target = BackupTarget.Volume;
                }
                else
                {
                    job.Target = BackupTarget.FilesAndFolders;
                }
            }
        }

        private void CollectSelectedChildren(DriveTreeItem parent, BackupJob job)
        {
            foreach (var child in parent.Children)
            {
                if (child.IsChecked == true)
                {
                    if (child.ItemType == DriveTreeItemType.Volume)
                    {
                        if (job.Target == 0) job.Target = BackupTarget.Volume;
                        job.SourcePaths.Add(child.FullPath);
                    }
                    else if (child.ItemType == DriveTreeItemType.Folder)
                    {
                        job.Target = BackupTarget.FilesAndFolders;
                        job.SourcePaths.Add(child.FullPath);
                    }
                }
                else if (child.IsChecked == null && child.Children.Count > 0)
                {
                    CollectSelectedChildren(child, job);
                }
            }
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

            if ((cmbBackupType.SelectedIndex == 3 || cmbBackupType.SelectedIndex == 4) && 
                string.IsNullOrWhiteSpace(txtCloneDestination.Text))
            {
                var cloneType = cmbBackupType.SelectedIndex == 3 ? "physical disk" : "virtual disk";
                MessageBox.Show($"Please select a {cloneType} destination.", "Validation Error",
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
