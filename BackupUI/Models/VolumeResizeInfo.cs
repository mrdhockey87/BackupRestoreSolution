using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BackupUI.Models
{
    /// <summary>
    /// Represents a volume in the resize interface with its original and target sizes
    /// </summary>
    public class VolumeResizeInfo : INotifyPropertyChanged
    {
        private long _targetSize;
        private bool _isResizing;

        /// <summary>
        /// Volume label/name (e.g., "C:", "System Reserved")
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Original volume size in bytes from backup
        /// </summary>
        public long OriginalSize { get; set; }

        /// <summary>
        /// Actual data size in bytes (used space in backup)
        /// </summary>
        public long DataSize { get; set; }

        /// <summary>
        /// Target size in bytes for restore (user adjustable)
        /// </summary>
        public long TargetSize
        {
            get => _targetSize;
            set
            {
                if (_targetSize != value)
                {
                    _targetSize = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TargetSizeGB));
                }
            }
        }

        /// <summary>
        /// Minimum allowed size (based on actual data + overhead)
        /// </summary>
        public long MinimumSize => DataSize + (long)(DataSize * 0.1); // 10% overhead

        /// <summary>
        /// Position in the volume list (0-based index)
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Indicates if this volume is currently being resized
        /// </summary>
        public bool IsResizing
        {
            get => _isResizing;
            set
            {
                if (_isResizing != value)
                {
                    _isResizing = value;
                    OnPropertyChanged();
                }
            }
        }

        // Computed properties for display
        public double OriginalSizeGB => OriginalSize / (1024.0 * 1024.0 * 1024.0);
        public double DataSizeGB => DataSize / (1024.0 * 1024.0 * 1024.0);
        public double TargetSizeGB => TargetSize / (1024.0 * 1024.0 * 1024.0);
        public double MinimumSizeGB => MinimumSize / (1024.0 * 1024.0 * 1024.0);

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
