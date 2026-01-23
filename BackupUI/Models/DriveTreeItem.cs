using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BackupUI.Models
{
    public class DriveTreeItem : INotifyPropertyChanged
    {
        private bool? _isChecked = false;
        private bool _isExpanded = false;
        private bool _childrenLoaded = false;

        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public DriveTreeItemType ItemType { get; set; }
        public long Size { get; set; }
        public bool IsBootVolume { get; set; }
        public bool IsWindowsServer { get; set; }
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

        private void UpdateChildren(bool? value)
        {
            if (value.HasValue && Children != null)
            {
                foreach (var child in Children)
                {
                    child._isChecked = value;
                    child.OnPropertyChanged(nameof(IsChecked));
                    child.UpdateChildren(value);
                }
            }
        }

        private void UpdateParent()
        {
            if (Parent == null) return;

            bool? allChecked = true;
            bool? anyChecked = false;

            foreach (var sibling in Parent.Children)
            {
                if (sibling.IsChecked == false)
                    allChecked = false;
                if (sibling.IsChecked == true || sibling.IsChecked == null)
                    anyChecked = true;
            }

            Parent._isChecked = allChecked == true ? true : (anyChecked == true ? null : false);
            Parent.OnPropertyChanged(nameof(IsChecked));
            Parent.UpdateParent();
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
        HyperVVolume
    }
}
