using System;
using System.Collections.Generic;
using System.Linq;

namespace BackupUI.Models
{
    /// <summary>
    /// Manages volume resizing logic, constraints, and validation
    /// </summary>
    public class VolumeResizeManager
    {
        private readonly List<VolumeResizeInfo> _volumes;
        private readonly long _targetDiskSize;

        public VolumeResizeManager(List<VolumeResizeInfo> volumes, long targetDiskSize)
        {
            _volumes = volumes ?? throw new ArgumentNullException(nameof(volumes));
            _targetDiskSize = targetDiskSize;

            // Initialize target sizes to original sizes
            foreach (var volume in _volumes)
            {
                volume.TargetSize = volume.OriginalSize;
            }
        }

        /// <summary>
        /// Gets the total allocated size across all volumes
        /// </summary>
        public long TotalAllocatedSize => _volumes.Sum(v => v.TargetSize);

        /// <summary>
        /// Gets remaining free space on target disk
        /// </summary>
        public long RemainingSpace => _targetDiskSize - TotalAllocatedSize;

        /// <summary>
        /// Attempts to resize a volume, adjusting adjacent volumes as needed
        /// </summary>
        /// <param name="volumeIndex">Index of the volume to resize</param>
        /// <param name="newSize">Requested new size in bytes</param>
        /// <param name="dragDirection">Direction of resize: 1 for grow, -1 for shrink</param>
        /// <returns>True if resize was successful, false if constraints prevent it</returns>
        public bool ResizeVolume(int volumeIndex, long newSize, int dragDirection)
        {
            if (volumeIndex < 0 || volumeIndex >= _volumes.Count)
                return false;

            var volume = _volumes[volumeIndex];
            long currentSize = volume.TargetSize;
            long sizeDelta = newSize - currentSize;

            // Check if new size is below minimum
            if (newSize < volume.MinimumSize)
                return false;

            // Check if growing would exceed disk capacity
            if (sizeDelta > 0 && TotalAllocatedSize + sizeDelta > _targetDiskSize)
                return false;

            // If shrinking, we can do it directly
            if (sizeDelta <= 0)
            {
                volume.TargetSize = newSize;
                return true;
            }

            // If growing, we need to shrink adjacent volumes or use free space
            if (sizeDelta > 0)
            {
                long availableSpace = RemainingSpace;

                // Try to take from adjacent volumes
                if (availableSpace < sizeDelta)
                {
                    long neededSpace = sizeDelta - availableSpace;
                    
                    // Try to shrink volumes after this one (if dragging right)
                    if (dragDirection > 0 && volumeIndex < _volumes.Count - 1)
                    {
                        if (!TryShrinkVolumes(volumeIndex + 1, _volumes.Count, neededSpace))
                            return false;
                    }
                    // Try to shrink volumes before this one (if dragging left)
                    else if (dragDirection < 0 && volumeIndex > 0)
                    {
                        if (!TryShrinkVolumes(0, volumeIndex, neededSpace))
                            return false;
                    }
                    else
                    {
                        return false; // Can't grow, no adjacent volumes to shrink
                    }
                }

                volume.TargetSize = newSize;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to shrink a range of volumes to free up space
        /// </summary>
        private bool TryShrinkVolumes(int startIndex, int endIndex, long spaceNeeded)
        {
            long totalShrinkable = 0;

            // Calculate how much we can shrink in this range
            for (int i = startIndex; i < endIndex && i < _volumes.Count; i++)
            {
                long shrinkable = _volumes[i].TargetSize - _volumes[i].MinimumSize;
                totalShrinkable += shrinkable;
            }

            if (totalShrinkable < spaceNeeded)
                return false; // Can't free up enough space

            // Distribute the shrinkage proportionally
            long remainingToShrink = spaceNeeded;
            for (int i = startIndex; i < endIndex && i < _volumes.Count && remainingToShrink > 0; i++)
            {
                var vol = _volumes[i];
                long shrinkable = vol.TargetSize - vol.MinimumSize;
                
                if (shrinkable > 0)
                {
                    long shrinkAmount = Math.Min(shrinkable, remainingToShrink);
                    vol.TargetSize -= shrinkAmount;
                    remainingToShrink -= shrinkAmount;
                }
            }

            return true;
        }

        /// <summary>
        /// Validates if the current configuration is valid for restore
        /// </summary>
        public (bool IsValid, string ErrorMessage) Validate()
        {
            // Check total size doesn't exceed target
            if (TotalAllocatedSize > _targetDiskSize)
            {
                return (false, $"Total volume size ({FormatBytes(TotalAllocatedSize)}) exceeds target disk capacity ({FormatBytes(_targetDiskSize)})");
            }

            // Check each volume meets minimum size
            foreach (var volume in _volumes)
            {
                if (volume.TargetSize < volume.MinimumSize)
                {
                    return (false, $"Volume '{volume.Label}' size ({FormatBytes(volume.TargetSize)}) is below minimum required ({FormatBytes(volume.MinimumSize)})");
                }
            }

            return (true, string.Empty);
        }

        /// <summary>
        /// Auto-fits all volumes proportionally to the target disk
        /// </summary>
        public void AutoFit()
        {
            long totalOriginalSize = _volumes.Sum(v => v.OriginalSize);
            
            if (totalOriginalSize == 0)
                return;

            // Calculate scaling factor
            double scaleFactor = (double)_targetDiskSize / totalOriginalSize;

            foreach (var volume in _volumes)
            {
                long scaledSize = (long)(volume.OriginalSize * scaleFactor);
                
                // Ensure it's at least the minimum size
                volume.TargetSize = Math.Max(scaledSize, volume.MinimumSize);
            }

            // Adjust if we're slightly over due to rounding
            long overflow = TotalAllocatedSize - _targetDiskSize;
            if (overflow > 0)
            {
                // Shrink the largest volume that has room
                var largestShrinkable = _volumes
                    .Where(v => v.TargetSize - v.MinimumSize > 0)
                    .OrderByDescending(v => v.TargetSize - v.MinimumSize)
                    .FirstOrDefault();

                if (largestShrinkable != null)
                {
                    largestShrinkable.TargetSize -= overflow;
                }
            }
        }

        private static string FormatBytes(long bytes)
        {
            double gb = bytes / (1024.0 * 1024.0 * 1024.0);
            return $"{gb:F2} GB";
        }
    }
}
