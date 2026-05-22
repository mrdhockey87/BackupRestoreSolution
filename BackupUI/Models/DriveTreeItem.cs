using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SecureServerBackup.Models
{
    public class DriveTreeItem : INotifyPropertyChanged
    {
        private bool? _isChecked = false;
        private bool _isExpanded = false;
        private bool _childrenLoaded = false;
        private bool _isSelectionEnabled = true;

        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string ResolvedPath { get; set; } = string.Empty;
        public string VirtualMachineName { get; set; } = string.Empty;
        public string VirtualDiskPath { get; set; } = string.Empty;
        public int PartitionNumber { get; set; }
        public DriveTreeItemType ItemType { get; set; }
        public long Size { get; set; }
        public bool IsBootVolume { get; set; }
        public bool IsWindowsServer { get; set; }
        /// <summary>True for no-drive-letter system partitions (EFI, Recovery, System Reserved).</summary>
        public bool IsHiddenPartition { get; set; }
        public bool IsRemovableNetworkPath { get; set; }
        public ObservableCollection<DriveTreeItem> Children { get; set; } = new();
        public DriveTreeItem? Parent { get; set; }

        // Indicates if children have been loaded (for lazy loading)
        public bool ChildrenLoaded
        {
            get => _childrenLoaded;
            set
            {
                _childrenLoaded = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelectionEnabled
        {
            get => _isSelectionEnabled;
            set
            {
                if (_isSelectionEnabled != value)
                {
                    _isSelectionEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ParticipatesInCheckState));
                }
            }
        }

        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                    UpdateChildren(value);
                    UpdateParent();
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                    
                    // Notify that expansion changed (for lazy loading)
                    OnExpansionChanged();
                }
            }
        }

        public string DisplayName
        {
            get
            {
                var name = Name;
                if (Size > 0)
                {
                    var sizeGB = Size / (1024.0 * 1024.0 * 1024.0);
                    name += $" ({sizeGB:F2} GB)";
                }
                if (IsBootVolume)
                {
                    name += " [Boot Volume]";
                }
                if (IsWindowsServer)
                {
                    name += " [Windows Server]";
                }
                return name;
            }
        }

        public bool IsLoadingPlaceholder => string.Equals(Name, "Loading...", StringComparison.Ordinal);

        public bool ParticipatesInCheckState => !IsLoadingPlaceholder && IsSelectionEnabled;

        private void UpdateChildren(bool? value)
        {
            if (value.HasValue && Children != null)
            {
                foreach (var child in Children)
                {
                    if (!child.ParticipatesInCheckState)
                    {
                        continue;
                    }

                    child._isChecked = value;
                    child.OnPropertyChanged(nameof(IsChecked));
                    child.UpdateChildren(value);
                }
            }
        }

        private void UpdateParent()
        {
            if (Parent == null) return;

            if (!TryGetAggregateCheckState(Parent.Children, out bool? aggregateState))
            {
                return;
            }

            Parent._isChecked = aggregateState;
            Parent.OnPropertyChanged(nameof(IsChecked));
            Parent.UpdateParent();
        }

        public void RefreshCheckStateFromChildren()
        {
            if (!TryGetAggregateCheckState(Children, out bool? aggregateState))
            {
                return;
            }

            if (_isChecked != aggregateState)
            {
                _isChecked = aggregateState;
                OnPropertyChanged(nameof(IsChecked));
            }

            UpdateParent();
        }

        private static bool TryGetAggregateCheckState(System.Collections.Generic.IEnumerable<DriveTreeItem> items, out bool? aggregateState)
        {
            bool foundSelectableChild = false;
            bool allChecked = true;
            bool anyChecked = false;

            foreach (DriveTreeItem item in items)
            {
                if (!item.ParticipatesInCheckState)
                {
                    continue;
                }

                foundSelectableChild = true;

                if (item.IsChecked != true)
                {
                    allChecked = false;
                }

                if (item.IsChecked == true || item.IsChecked == null)
                {
                    anyChecked = true;
                }
            }

            if (!foundSelectableChild)
            {
                aggregateState = false;
                return false;
            }

            aggregateState = allChecked ? true : (anyChecked ? null : false);
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? ExpansionChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected void OnExpansionChanged()
        {
            ExpansionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public enum DriveTreeItemType
    {
        Disk,
        Volume,
        Folder,
        File,
        HyperVSystem,
        HyperVVirtualDisk,
        HyperVVolume,
        NetworkRoot,        // Root "Network Locations" node
        NetworkDrive,       // Mapped network drive (Z:\)
        NetworkShare,       // UNC path (\\server\share)
        NetworkBrowser      // "Add Network Path..." option
    }

}
