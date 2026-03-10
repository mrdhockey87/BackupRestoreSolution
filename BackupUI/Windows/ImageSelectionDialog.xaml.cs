using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace BackupUI.Windows
{
    public partial class ImageSelectionDialog : Window
    {
        public int SelectedImageIndex { get; private set; } = -1;

        public ImageSelectionDialog(List<BackupImageInfo> images)
        {
            InitializeComponent();
            
            if (images == null || images.Count == 0)
            {
                throw new ArgumentException("No images provided", nameof(images));
            }

            // Sort images by date (most recent first)
            var sortedImages = images.OrderByDescending(i => i.ImageDate).ToList();
            
            dgImages.ItemsSource = sortedImages;
            
            // Pre-select most recent (first row after sorting)
            if (sortedImages.Count > 0)
            {
                dgImages.SelectedIndex = 0;
            }
        }

        private void btnMount_Click(object sender, RoutedEventArgs e)
        {
            if (dgImages.SelectedItem is BackupImageInfo selectedImage)
            {
                SelectedImageIndex = selectedImage.ImageIndex;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(
                    "Please select a restore point to mount.",
                    "No Selection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void dgImages_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Double-click to mount
            if (dgImages.SelectedItem != null)
            {
                btnMount_Click(sender, e);
            }
        }
    }

    /// <summary>
    /// Information about a single WIM image/restore point
    /// </summary>
    public class BackupImageInfo
    {
        public int ImageIndex { get; set; }
        public DateTime ImageDate { get; set; }
        public string ImageType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
