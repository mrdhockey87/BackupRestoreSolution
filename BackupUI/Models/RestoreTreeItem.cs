using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SecureServerBackup.Models
{
    public class RestoreTreeItem : INotifyPropertyChanged
    {
        private bool? _isChecked = false;
        private bool _isExpanded = false;

        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public RestoreItemType ItemType { get; set; }
        public long Size { get; set; }
        public ObservableCollection<RestoreTreeItem> Children { get; set; } = new();
        public RestoreTreeItem? Parent { get; set; }

        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));

                    // Update children
                    if (value.HasValue)
                    {
                        foreach (var child in Children)
                        {
                            child.IsChecked = value;
                        }
                    }

                    // Update parent
                    Parent?.UpdateCheckState();
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
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        private void UpdateCheckState()
        {
            bool? newState = null;

            if (Children.Count > 0)
            {
                bool allChecked = true;
                bool allUnchecked = true;

                foreach (var child in Children)
                {
                    if (child.IsChecked == true)
                        allUnchecked = false;
                    else if (child.IsChecked == false)
                        allChecked = false;
                    else
                    {
                        allChecked = false;
                        allUnchecked = false;
                    }
                }

                if (allChecked)
                    newState = true;
                else if (allUnchecked)
                    newState = false;
                else
                    newState = null;
            }

            if (_isChecked != newState)
            {
                _isChecked = newState;
                OnPropertyChanged(nameof(IsChecked));
                Parent?.UpdateCheckState();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum RestoreItemType
    {
        Disk,
        Volume,
        Folder,
        File
    }
}
