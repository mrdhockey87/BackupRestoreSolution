using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace SecureServerBackup.Windows
{
    public partial class ImageSelectionDialog : Window
    {
        public int SelectedImageIndex { get; private set; } = -1;

        /// <param name="images">List of images in the backup.</param>
        /// <param name="actionLabel">Label for the primary action button, e.g. "Mount Selected" or "Verify Selected".</param>
        /// <param name="subtitleText">Optional subtitle shown below the header.</param>
        public ImageSelectionDialog(List<BackupImageInfo> images, string actionLabel = "Mount Selected", string? subtitleText = null)
        {
            InitializeComponent();

            ArgumentNullException.ThrowIfNull(images);
            if (images.Count == 0)
                throw new ArgumentException("No images provided", nameof(images));

            btnAction.Content = actionLabel;
            if (subtitleText != null)
                txtSubtitle.Text = subtitleText;

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
                    "Please select a restore point.",
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
    /// Information about a single backup image/restore point
    /// </summary>
    public class BackupImageInfo
    {
        public int ImageIndex { get; set; }
        public DateTime ImageDate { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ImageType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
