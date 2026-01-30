using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    public partial class RestoreWindow : Window
    {
        private ObservableCollection<BackupDateItem> backupDates = new();
        private ObservableCollection<RestoreTreeItem> restoreItems = new();
        private string selectedBackupPath = "";

        public RestoreWindow()
        {
            InitializeComponent();
        }

        #region Step 1: Select Backup & Date

        private void BrowseBackup_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select Backup Folder",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtBackupSource.Text = dialog.SelectedPath;
            }
        }

        private async void LoadBackup_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtBackupSource.Text))
            {
                MessageBox.Show("Please select a backup folder.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(txtBackupSource.Text))
            {
                MessageBox.Show("The selected backup folder does not exist.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await LoadBackupDates(txtBackupSource.Text);
        }

        private async Task LoadBackupDates(string backupPath)
        {
            backupDates.Clear();
            lstBackupDates.ItemsSource = null;

            await Task.Run(() =>
            {
                try
                {
                    // Call C++ to enumerate backup dates/snapshots
                    var buffer = new StringBuilder(32768);
                    int result = BackupEngineInterop.EnumerateBackupDates(
                        backupPath, buffer, buffer.Capacity);

                    if (result == 0)
                    {
                        var dates = ParseBackupDates(buffer.ToString(), backupPath);
                        
                        Dispatcher.Invoke(() =>
                        {
                            foreach (var date in dates)
                            {
                                backupDates.Add(date);
                            }

                            lstBackupDates.ItemsSource = backupDates;
                            
                            if (backupDates.Count > 0)
                            {
                                txtBackupInfo.Text = $"Found {backupDates.Count} backup point(s). Select a date to restore from.";
                                lstBackupDates.SelectedIndex = backupDates.Count - 1; // Select most recent
                            }
                            else
                            {
                                txtBackupInfo.Text = "No valid backups found in the selected folder.";
                            }
                        });
                    }
                    else
                    {
                        var error = new StringBuilder(1024);
                        BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
                        
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Failed to load backup dates: {error}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Failed to load backup dates: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        private List<BackupDateItem> ParseBackupDates(string data, string basePath)
        {
            var dates = new List<BackupDateItem>();
            
            // Format: Date|Type|Size|Path
            // Example: 2026-01-30 14:30:00|Full|2.5 GB|D:\Backups\Full_20260130_143000
            var lines = data.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var parts = line.Trim().Split('|');
                if (parts.Length >= 4)
                {
                    if (DateTime.TryParse(parts[0], out DateTime date))
                    {
                        dates.Add(new BackupDateItem
                        {
                            Date = date,
                            BackupType = parts[1],
                            Size = parts[2],
                            BackupPath = parts[3]
                        });
                    }
                }
            }

            // Sort by date descending (most recent first)
            return dates.OrderByDescending(d => d.Date).ToList();
        }

        private void Step1Next_Click(object sender, RoutedEventArgs e)
        {
            if (lstBackupDates.SelectedItem == null)
            {
                MessageBox.Show("Please select a backup date to restore from.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedDate = (BackupDateItem)lstBackupDates.SelectedItem;
            selectedBackupPath = selectedDate.BackupPath;

            // Load contents for selected backup
            LoadBackupContents(selectedBackupPath);

            // Show Step 2
            pnlStep1.Visibility = Visibility.Collapsed;
            pnlStep2.Visibility = Visibility.Visible;
        }

        #endregion

        #region Step 2: Select What to Restore

        private async void LoadBackupContents(string backupPath)
        {
            restoreItems.Clear();
            treeRestoreContents.Items.Clear();

            await Task.Run(() =>
            {
                try
                {
                    var buffer = new StringBuilder(65536);
                    int result = BackupEngineInterop.ListBackupContents(
                        backupPath, buffer, buffer.Capacity);

                    if (result == 0)
                    {
                        var contents = buffer.ToString();
                        
                        Dispatcher.Invoke(() =>
                        {
                            BuildRestoreTree(contents);
                        });
                    }
                    else
                    {
                        var error = new StringBuilder(1024);
                        BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
                        
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Failed to load backup contents: {error}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Failed to load backup contents: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        private void BuildRestoreTree(string contents)
        {
            // Parse contents and build hierarchical tree
            // Format: Type|Path|Size
            // Example: Disk|\\.\PHYSICALDRIVE0|500 GB
            //          Volume|C:\|250 GB
            //          Folder|C:\Users|10 GB
            //          File|C:\Users\file.txt|1 MB

            var lines = contents.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var rootItems = new Dictionary<string, RestoreTreeItem>();

            foreach (var line in lines)
            {
                var parts = line.Trim().Split('|');
                if (parts.Length >= 3)
                {
                    var type = parts[0];
                    var path = parts[1];
                    var size = parts[2];

                    if (type == "Disk")
                    {
                        var diskItem = new RestoreTreeItem
                        {
                            Name = path,
                            FullPath = path,
                            ItemType = RestoreItemType.Disk,
                            Size = ParseSize(size)
                        };
                        rootItems[path] = diskItem;
                        restoreItems.Add(diskItem);
                    }
                    else if (type == "Volume")
                    {
                        var volumeItem = new RestoreTreeItem
                        {
                            Name = $"{path} ({size})",
                            FullPath = path,
                            ItemType = RestoreItemType.Volume,
                            Size = ParseSize(size)
                        };
                        
                        // Find parent disk if exists
                        var parentDisk = FindParentDisk(path, rootItems);
                        if (parentDisk != null)
                        {
                            volumeItem.Parent = parentDisk;
                            parentDisk.Children.Add(volumeItem);
                        }
                        else
                        {
                            restoreItems.Add(volumeItem);
                        }
                    }
                    else if (type == "Folder" || type == "File")
                    {
                        var itemType = type == "Folder" ? RestoreItemType.Folder : RestoreItemType.File;
                        var item = new RestoreTreeItem
                        {
                            Name = Path.GetFileName(path),
                            FullPath = path,
                            ItemType = itemType,
                            Size = ParseSize(size)
                        };

                        // Find parent volume or folder
                        var parent = FindParentItem(path, restoreItems);
                        if (parent != null)
                        {
                            item.Parent = parent;
                            parent.Children.Add(item);
                        }
                    }
                }
            }

            // Create TreeViewItems
            foreach (var item in restoreItems)
            {
                var treeViewItem = CreateRestoreTreeViewItem(item);
                treeRestoreContents.Items.Add(treeViewItem);
            }
        }

        private RestoreTreeItem? FindParentDisk(string volumePath, Dictionary<string, RestoreTreeItem> disks)
        {
            // Simple heuristic: return first disk
            // In production, would query actual disk-volume mapping
            return disks.Values.FirstOrDefault();
        }

        private RestoreTreeItem? FindParentItem(string path, ObservableCollection<RestoreTreeItem> items)
        {
            var parentPath = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parentPath))
                return null;

            foreach (var item in items)
            {
                var found = FindItemByPath(parentPath, item);
                if (found != null)
                    return found;
            }

            return null;
        }

        private RestoreTreeItem? FindItemByPath(string path, RestoreTreeItem item)
        {
            if (item.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                return item;

            foreach (var child in item.Children)
            {
                var found = FindItemByPath(path, child);
                if (found != null)
                    return found;
            }

            return null;
        }

        private long ParseSize(string sizeStr)
        {
            // Parse "2.5 GB" format to bytes
            var parts = sizeStr.Trim().Split(' ');
            if (parts.Length == 2 && double.TryParse(parts[0], out double value))
            {
                var unit = parts[1].ToUpper();
                return unit switch
                {
                    "B" => (long)value,
                    "KB" => (long)(value * 1024),
                    "MB" => (long)(value * 1024 * 1024),
                    "GB" => (long)(value * 1024 * 1024 * 1024),
                    "TB" => (long)(value * 1024L * 1024 * 1024 * 1024),
                    _ => 0
                };
            }
            return 0;
        }

        private TreeViewItem CreateRestoreTreeViewItem(RestoreTreeItem item)
        {
            var treeViewItem = new TreeViewItem();
            
            // Create header with checkbox
            var panel = new StackPanel 
            { 
                Orientation = System.Windows.Controls.Orientation.Horizontal 
            };
            
            var checkbox = new System.Windows.Controls.CheckBox
            {
                IsChecked = item.IsChecked,
                IsThreeState = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            
            // Handle checkbox click
            checkbox.Click += (s, e) =>
            {
                if (item.IsChecked == true)
                    item.IsChecked = false;
                else
                    item.IsChecked = true;
                e.Handled = true;
            };
            
            // Update checkbox when model changes
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(item.IsChecked))
                    checkbox.IsChecked = item.IsChecked;
            };
            
            var textBlock = new TextBlock
            {
                Text = item.Name,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            panel.Children.Add(checkbox);
            panel.Children.Add(textBlock);
            treeViewItem.Header = panel;
            
            // Add children
            foreach (var child in item.Children)
            {
                treeViewItem.Items.Add(CreateRestoreTreeViewItem(child));
            }
            
            // Bind expansion
            treeViewItem.IsExpanded = item.IsExpanded;
            treeViewItem.Expanded += (s, e) =>
            {
                if (e.Source == treeViewItem)
                    item.IsExpanded = true;
            };
            treeViewItem.Collapsed += (s, e) =>
            {
                if (e.Source == treeViewItem)
                    item.IsExpanded = false;
            };
            
            return treeViewItem;
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllExpanded(restoreItems, true);
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllExpanded(restoreItems, false);
        }

        private void SetAllExpanded(ObservableCollection<RestoreTreeItem> items, bool expanded)
        {
            foreach (var item in items)
            {
                item.IsExpanded = expanded;
                if (item.Children.Count > 0)
                    SetAllExpanded(new ObservableCollection<RestoreTreeItem>(item.Children), expanded);
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in restoreItems)
                item.IsChecked = true;
        }

        private void UnselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in restoreItems)
                item.IsChecked = false;
        }

        private void Step2Back_Click(object sender, RoutedEventArgs e)
        {
            pnlStep2.Visibility = Visibility.Collapsed;
            pnlStep1.Visibility = Visibility.Visible;
        }

        private void Step2Next_Click(object sender, RoutedEventArgs e)
        {
            // Check if at least one item is selected
            bool anySelected = restoreItems.Any(item => HasCheckedItem(item));
            
            if (!anySelected)
            {
                MessageBox.Show("Please select at least one item to restore.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Show Step 3
            pnlStep2.Visibility = Visibility.Collapsed;
            pnlStep3.Visibility = Visibility.Visible;
        }

        private bool HasCheckedItem(RestoreTreeItem item)
        {
            if (item.IsChecked == true)
                return true;

            foreach (var child in item.Children)
            {
                if (HasCheckedItem(child))
                    return true;
            }

            return false;
        }

        #endregion

        #region Step 3: Select Restore Destination

        private void RestoreLocation_Changed(object sender, RoutedEventArgs e)
        {
            if (pnlNewLocation != null)
            {
                pnlNewLocation.Visibility = rbNewLocation?.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void BrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select Restore Destination",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtRestoreDestination.Text = dialog.SelectedPath;
            }
        }

        private void HyperV_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (pnlHyperV != null)
            {
                pnlHyperV.Visibility = chkRestoreAsHyperV?.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void BrowseVMStorage_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select VM Storage Path",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtVMStorage.Text = dialog.SelectedPath;
            }
        }

        private void Step3Back_Click(object sender, RoutedEventArgs e)
        {
            pnlStep3.Visibility = Visibility.Collapsed;
            pnlStep2.Visibility = Visibility.Visible;
        }

        private async void StartRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateRestoreInputs())
                return;

            var result = MessageBox.Show(
                "Are you sure you want to start the restore operation?\n\nThis may overwrite existing files.",
                "Confirm Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            await ExecuteRestore();
        }

        private bool ValidateRestoreInputs()
        {
            if (rbNewLocation?.IsChecked == true && string.IsNullOrWhiteSpace(txtRestoreDestination.Text))
            {
                MessageBox.Show("Please select a restore destination.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (chkRestoreAsHyperV?.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(txtVMName.Text))
                {
                    MessageBox.Show("Please enter a VM name.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(txtVMStorage.Text))
                {
                    MessageBox.Show("Please select VM storage path.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Restore Execution

        private async Task ExecuteRestore()
        {
            progressBar.Visibility = Visibility.Visible;
            txtProgress.Visibility = Visibility.Visible;
            txtProgress.Text = "Starting restore...";

            try
            {
                BackupEngineInterop.ProgressCallback callback = (percentage, message) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = percentage;
                        txtProgress.Text = message ?? $"Restoring... {percentage}%";
                    });
                };

                await Task.Run(() =>
                {
                    // Build list of items to restore
                    var itemsToRestore = CollectSelectedItems();
                    
                    // Determine destination
                    var destPath = rbOriginalLocation?.IsChecked == true
                        ? ""
                        : txtRestoreDestination.Text;

                    int result = 0;

                    if (chkRestoreAsHyperV?.IsChecked == true)
                    {
                        // Restore as Hyper-V VM
                        result = BackupEngineInterop.RestoreHyperVVM(
                            selectedBackupPath,
                            txtVMName.Text,
                            txtVMStorage.Text,
                            chkStartVM?.IsChecked == true,
                            callback);
                    }
                    else
                    {
                        // Build restore manifest (paths to restore)
                        var manifest = string.Join("\n", itemsToRestore);
                        
                        // Call C++ restore with manifest
                        result = BackupEngineInterop.RestoreWithManifest(
                            selectedBackupPath,
                            destPath,
                            manifest,
                            chkOverwrite?.IsChecked == true,
                            chkRestoreSystemState?.IsChecked == true,
                            chkPreservePermissions?.IsChecked == true,
                            callback);
                    }

                    if (result != 0)
                    {
                        var error = new StringBuilder(1024);
                        BackupEngineInterop.GetLastErrorMessage(error, error.Capacity);
                        throw new Exception($"Restore failed: {error}");
                    }
                });

                MessageBox.Show("Restore completed successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Restore failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                txtProgress.Visibility = Visibility.Collapsed;
            }
        }

        private List<string> CollectSelectedItems()
        {
            var items = new List<string>();
            
            foreach (var item in restoreItems)
            {
                CollectSelectedItemsRecursive(item, items);
            }

            return items;
        }

        private void CollectSelectedItemsRecursive(RestoreTreeItem item, List<string> items)
        {
            if (item.IsChecked == true)
            {
                // This item and all children are selected
                items.Add(item.FullPath);
            }
            else if (item.IsChecked == null)
            {
                // Partial selection - check children
                foreach (var child in item.Children)
                {
                    CollectSelectedItemsRecursive(child, items);
                }
            }
        }

        #endregion

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
