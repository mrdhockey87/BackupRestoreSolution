using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SecureServerBackup.Models;
using SecureServerBackup.Services;

namespace SecureServerBackup.Windows
{
    /// <summary>
    /// Prompts the user to choose which volume (or full-disk group) from a multi-image backup
    /// they want to restore. For disk and Hyper-V backups every image in the archive represents
    /// one partition, so the user can either pick a single volume or choose to restore the
    /// entire disk (all partitions in partition-order).
    /// </summary>
    public partial class RestoreVolumeSelectionDialog : Window
    {
        /// <summary>View model shown in the ListView.</summary>
        private sealed class VolumeRow
        {
            public int ImageIndex { get; init; }
            public string Label { get; init; } = string.Empty;
            public string PartitionType { get; init; } = string.Empty;
            public string FileSystem { get; init; } = string.Empty;
            public string SourceVolumeMountPath { get; init; } = string.Empty;
            public string OriginalSizeDisplay { get; init; } = string.Empty;
            public string UsedSizeDisplay { get; init; } = string.Empty;
            public VolumeInfo Source { get; init; } = null!;
        }

        private readonly List<VolumeInfo> _allVolumes;
        private readonly bool _isDiskOrHyperV;

        /// <summary>
        /// Single selected volume.  Null when the user chose the full-disk group.
        /// </summary>
        public VolumeInfo? SelectedVolume { get; private set; }

        /// <summary>
        /// Ordered list of all volumes to restore (populated when the user picks full-disk).
        /// </summary>
        public IReadOnlyList<VolumeInfo>? SelectedDiskGroup { get; private set; }

        /// <summary>True when the user confirmed a selection.</summary>
        public bool Confirmed { get; private set; }

        /// <param name="volumes">All volumes found in the backup, already sorted by partition offset.</param>
        /// <param name="isDiskOrHyperVBackup">
        ///   True for disk and Hyper-V backups.  Enables the "Select Full Disk" button and shows
        ///   the group-restore note.
        /// </param>
        /// <param name="subtitleOverride">Optional subtitle text for the dialog.</param>
        public RestoreVolumeSelectionDialog(
            IReadOnlyList<VolumeInfo> volumes,
            bool isDiskOrHyperVBackup,
            string? subtitleOverride = null)
        {
            InitializeComponent();

            ArgumentNullException.ThrowIfNull(volumes);

            _allVolumes = volumes.ToList();
            _isDiskOrHyperV = isDiskOrHyperVBackup;

            if (!string.IsNullOrWhiteSpace(subtitleOverride))
                txtSubtitle.Text = subtitleOverride;

            if (_isDiskOrHyperV)
            {
                btnSelectAll.Visibility = Visibility.Visible;
                txtDiskGroupNote.Visibility = Visibility.Visible;
                txtDiskGroupNote.Text =
                    "This is a full-disk backup. You may restore a single volume or click " +
                    "\u201cSelect Full Disk\u201d to reconstruct the entire disk in partition order.";
            }

            lvVolumes.ItemsSource = _allVolumes.Select(v => new VolumeRow
            {
                ImageIndex           = v.ImageIndex,
                Label                = BuildLabel(v),
                PartitionType        = DescribePartitionType(v),
                FileSystem           = string.IsNullOrWhiteSpace(v.FileSystem) ? "—" : v.FileSystem,
                SourceVolumeMountPath = string.IsNullOrWhiteSpace(v.SourceVolumeMountPath) ? "—" : v.SourceVolumeMountPath,
                OriginalSizeDisplay  = FormatSize(v.Size),
                UsedSizeDisplay      = v.UsedSpace > 0 ? FormatSize(v.UsedSpace) : "—",
                Source               = v
            }).ToList();

            if (_allVolumes.Count > 0)
                lvVolumes.SelectedIndex = 0;
        }

        // ── event handlers ────────────────────────────────────────────────

        private void LvVolumes_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            bool hasSelection = lvVolumes.SelectedItem != null;
            btnRestore.IsEnabled = hasSelection;
            txtSelectionHint.Text = hasSelection
                ? $"Press \u201cRestore\u201d to continue, or drag partition handles to resize."
                : "Select a volume above.";
        }

        private void LvVolumes_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lvVolumes.SelectedItem != null)
                Commit(singleVolume: true);
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            Commit(singleVolume: true);
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            Commit(singleVolume: false);
        }

        // ── helpers ───────────────────────────────────────────────────────

        private void Commit(bool singleVolume)
        {
            if (singleVolume)
            {
                if (lvVolumes.SelectedItem is not VolumeRow row)
                    return;

                SelectedVolume = row.Source;
                SelectedDiskGroup = null;
            }
            else
            {
                // Full disk: return all volumes ordered by partition offset then partition number
                SelectedDiskGroup = _allVolumes
                    .OrderBy(v => v.PartitionOffsetBytes)
                    .ThenBy(v => v.PartitionNumber)
                    .ToList();
                SelectedVolume = null;
            }

            Confirmed = true;
            DialogResult = true;
        }

        private static string BuildLabel(VolumeInfo v)
        {
            if (!string.IsNullOrWhiteSpace(v.Label))
                return v.Label;
            if (!string.IsNullOrWhiteSpace(v.SourceVolumeMountPath))
                return v.SourceVolumeMountPath.TrimEnd('\\');
            return $"Volume {v.PartitionNumber}";
        }

        private static string DescribePartitionType(VolumeInfo v)
        {
            if (v.IsBootVolume)  return "Boot";
            if (v.IsSystemVolume) return "System";
            return string.IsNullOrWhiteSpace(v.PartitionType) ? "Data" : v.PartitionType;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "—";
            double gb = bytes / (1024.0 * 1024.0 * 1024.0);
            if (gb >= 1.0) return $"{gb:F2} GB";
            double mb = bytes / (1024.0 * 1024.0);
            return $"{mb:F0} MB";
        }
    }
}
