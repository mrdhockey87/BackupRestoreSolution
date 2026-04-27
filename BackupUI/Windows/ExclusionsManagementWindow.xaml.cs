using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace SecureServerBackup
{
    public partial class ExclusionsManagementWindow : Window
    {
        public List<string> Exclusions { get; private set; }

        public ExclusionsManagementWindow(List<string> currentExclusions)
        {
            InitializeComponent();
            
            // Initialize with current exclusions
            Exclusions = new List<string>(currentExclusions ?? new List<string>());
            
            LoadExclusions();
            UpdateStatus();
        }

        private void LoadExclusions()
        {
            lstExclusions.Items.Clear();
            
            foreach (var exclusion in Exclusions)
            {
                lstExclusions.Items.Add(new ExclusionItem
                {
                    Path = exclusion,
                    Icon = GetIconForExclusion(exclusion)
                });
            }
            
            // Show/hide "no exclusions" message
            txtNoExclusions.Visibility = Exclusions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private string GetIconForExclusion(string exclusion)
        {
            if (exclusion.StartsWith("*"))
            {
                return "📄"; // Pattern/extension
            }
            else if (Directory.Exists(exclusion))
            {
                return "📁"; // Folder
            }
            else if (File.Exists(exclusion))
            {
                return "📝"; // File
            }
            else if (exclusion.Contains("*") || exclusion.Contains("?"))
            {
                return "📄"; // Wildcard pattern
            }
            else
            {
                return "❓"; // Unknown/invalid
            }
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select File to Exclude",
                Multiselect = true,
                CheckFileExists = true,
                Filter = "All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    AddExclusion(file);
                }
            }
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderBrowserDialog
            {
                Description = "Select Folder to Exclude",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                AddExclusion(dialog.SelectedPath);
            }
        }

        private void AddPattern_Click(object sender, RoutedEventArgs e)
        {
            AddPatternFromTextBox();
        }

        private void TxtExtensionPattern_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddPatternFromTextBox();
                e.Handled = true;
            }
        }

        private void AddPatternFromTextBox()
        {
            string pattern = txtExtensionPattern.Text.Trim();
            
            if (string.IsNullOrWhiteSpace(pattern))
            {
                MessageBox.Show("Please enter a file extension pattern.", "No Pattern", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Validate pattern format
            if (!pattern.StartsWith("*"))
            {
                // Auto-add * prefix if missing
                pattern = "*" + pattern;
            }

            // Validate that it's a reasonable pattern
            if (!pattern.Contains(".") && !pattern.Contains("?"))
            {
                var result = MessageBox.Show(
                    $"The pattern '{pattern}' doesn't contain a file extension.\n\n" +
                    "Did you mean to add '.' (e.g., '*.tmp' instead of '*tmp')?\n\n" +
                    "Add it anyway?",
                    "Confirm Pattern",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            AddExclusion(pattern);
            txtExtensionPattern.Clear();
        }

        private void AddExclusion(string exclusion)
        {
            // Normalize path separators
            exclusion = exclusion.Replace("/", "\\");
            
            // Check for duplicates
            if (Exclusions.Any(e => e.Equals(exclusion, StringComparison.OrdinalIgnoreCase)))
            {
                txtStatus.Text = $"Exclusion already exists: {exclusion}";
                return;
            }

            Exclusions.Add(exclusion);
            LoadExclusions();
            UpdateStatus();
            
            txtStatus.Text = $"Added: {exclusion}";
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (lstExclusions.SelectedItems.Count == 0)
            {
                return;
            }

            var selectedItems = lstExclusions.SelectedItems.Cast<ExclusionItem>().ToList();
            
            var result = MessageBox.Show(
                $"Remove {selectedItems.Count} exclusion(s)?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            foreach (var item in selectedItems)
            {
                Exclusions.Remove(item.Path);
            }
            
            LoadExclusions();
            UpdateStatus();
            
            txtStatus.Text = $"Removed {selectedItems.Count} exclusion(s).";
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (Exclusions.Count == 0)
            {
                return;
            }

            var result = MessageBox.Show(
                $"Remove ALL {Exclusions.Count} custom exclusions?\n\n" +
                "System exclusions (pagefile.sys, etc.) will remain and cannot be removed.",
                "Confirm Clear All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            Exclusions.Clear();
            LoadExclusions();
            UpdateStatus();
            
            txtStatus.Text = "All custom exclusions removed.";
        }

        private void UpdateStatus()
        {
            if (Exclusions.Count == 0)
            {
                txtStatus.Text = "No custom exclusions defined. System exclusions still apply.";
            }
            else
            {
                int fileCount = Exclusions.Count(e => File.Exists(e));
                int folderCount = Exclusions.Count(e => Directory.Exists(e));
                int patternCount = Exclusions.Count(e => e.Contains("*") || e.Contains("?"));
                
                txtStatus.Text = $"{Exclusions.Count} exclusion(s): {fileCount} file(s), {folderCount} folder(s), {patternCount} pattern(s)";
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Helper class for displaying exclusions in ListBox
        private class ExclusionItem
        {
            public string Path { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
        }
    }
}
