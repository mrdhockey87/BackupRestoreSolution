using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using SecureServerBackup.Models;

namespace SecureServerBackup.Controls
{
    public partial class VolumeResizeControl : UserControl
    {
        private List<VolumeResizeInfo> _volumes = new();
        private VolumeResizeManager? _resizeManager;
        private long _targetDiskSize;
        private Dictionary<Thumb, int> _thumbToVolumeIndex;

        public VolumeResizeControl()
        {
            InitializeComponent();
            _thumbToVolumeIndex = new Dictionary<Thumb, int>();
        }

        /// <summary>
        /// Initializes the control with volume and disk information
        /// </summary>
        public void Initialize(List<VolumeResizeInfo> volumes, long targetDiskSizeBytes)
        {
            _volumes = volumes ?? throw new ArgumentNullException(nameof(volumes));
            _targetDiskSize = targetDiskSizeBytes;
            _resizeManager = new VolumeResizeManager(_volumes, _targetDiskSize);

            // Subscribe to property changes
            foreach (var volume in _volumes)
            {
                volume.PropertyChanged += Volume_PropertyChanged;
            }

            RenderBars();
        }

        private void Volume_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VolumeResizeInfo.TargetSize))
            {
                RenderBars();
            }
        }

        private void RenderBars()
        {
            if (_volumes == null || _volumes.Count == 0 || _resizeManager == null)
                return;

            RenderSourceBar();
            RenderTargetBar();
            RenderArrows();
            UpdateSizeLabels();
        }

        private void RenderSourceBar()
        {
            SourceBarContainer.Child = null;

            var canvas = new Canvas();
            long totalSize = _volumes.Sum(v => v.OriginalSize);
            double availableWidth = SourceBarContainer.ActualWidth > 0 ? SourceBarContainer.ActualWidth - 4 : 750;

            double currentX = 2;

            foreach (var volume in _volumes)
            {
                double volumeWidth = (volume.OriginalSize / (double)totalSize) * availableWidth;

                var border = new Border
                {
                    Width = volumeWidth,
                    Height = 46,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(4),
                    Background = GetVolumeColor(volume.Index),
                    Margin = new Thickness(0)
                };

                Canvas.SetLeft(border, currentX);
                Canvas.SetTop(border, 2);

                var textBlock = new TextBlock
                {
                    Text = $"{volume.Label}\n{volume.OriginalSizeGB:F2} GB",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                };

                var grid = new Grid();
                grid.Children.Add(textBlock);
                border.Child = grid;

                canvas.Children.Add(border);
                currentX += volumeWidth;
            }

            SourceBarContainer.Child = canvas;
        }

        private void RenderTargetBar()
        {
            TargetBarContainer.Child = null;
            _thumbToVolumeIndex.Clear();

            var canvas = new Canvas();
            double availableWidth = TargetBarContainer.ActualWidth > 0 ? TargetBarContainer.ActualWidth - 4 : 750;

            double currentX = 2;

            foreach (var volume in _volumes)
            {
                double volumeWidth = (volume.TargetSize / (double)_targetDiskSize) * availableWidth;

                var border = new Border
                {
                    Width = volumeWidth,
                    Height = 46,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(4),
                    Background = GetVolumeColor(volume.Index),
                    Margin = new Thickness(0)
                };

                Canvas.SetLeft(border, currentX);
                Canvas.SetTop(border, 2);

                var textBlock = new TextBlock
                {
                    Text = $"{volume.Label}\n{volume.TargetSizeGB:F2} GB",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                };

                var grid = new Grid();
                grid.Children.Add(textBlock);
                border.Child = grid;

                canvas.Children.Add(border);

                // Add resize thumb if not the last volume
                if (volume.Index < _volumes.Count - 1)
                {
                    // Create a button as a resize handle (simpler than Thumb template)
                    var thumbButton = new Button
                    {
                        Width = 16,
                        Height = 46,
                        Cursor = System.Windows.Input.Cursors.SizeWE,
                        Background = Brushes.Transparent,
                        BorderBrush = Brushes.Transparent,
                        Padding = new Thickness(0)
                    };

                    // Create arrow visual as content
                    var path = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M 0,0 L 8,10 L 0,20 Z"),
                        Fill = new SolidColorBrush(Color.FromRgb(255, 107, 107)),
                        Stroke = Brushes.DarkRed,
                        StrokeThickness = 1,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    thumbButton.Content = path;

                    Canvas.SetLeft(thumbButton, currentX + volumeWidth - 8);
                    Canvas.SetTop(thumbButton, 2);

                    // Store association
                    int capturedIndex = volume.Index;
                    double capturedStartX = currentX + volumeWidth - 8;
                    
                    // Mouse down to start drag
                    thumbButton.MouseMove += (s, args) =>
                    {
                        if (args.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                        {
                            double currentMouseX = args.GetPosition(canvas).X;
                            double deltaX = currentMouseX - capturedStartX;
                            
                            double availableWidth = TargetBarContainer.ActualWidth > 0 ? TargetBarContainer.ActualWidth - 4 : 750;
                            long sizeChange = (long)(deltaX / availableWidth * _targetDiskSize);
                            long newSize = _volumes[capturedIndex].TargetSize + sizeChange;

                            int dragDirection = sizeChange > 0 ? 1 : -1;
                            _resizeManager!.ResizeVolume(capturedIndex, newSize, dragDirection);
                        }
                    };

                    canvas.Children.Add(thumbButton);
                }

                currentX += volumeWidth;
            }

            // Show free space
            long freeSpace = _resizeManager!.RemainingSpace;
            if (freeSpace > 0)
            {
                double freeWidth = (freeSpace / (double)_targetDiskSize) * availableWidth;

                var freeRect = new Rectangle
                {
                    Width = freeWidth,
                    Height = 46,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = new SolidColorBrush(Color.FromArgb(50, 158, 158, 158))
                };

                Canvas.SetLeft(freeRect, currentX);
                Canvas.SetTop(freeRect, 2);
                canvas.Children.Add(freeRect);

                var freeText = new TextBlock
                {
                    Text = $"Free\n{(freeSpace / (1024.0 * 1024.0 * 1024.0)):F2} GB",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 10,
                    Foreground = Brushes.Gray
                };

                Canvas.SetLeft(freeText, currentX + freeWidth/2 - 30);
                Canvas.SetTop(freeText, 15);
                canvas.Children.Add(freeText);
            }

            TargetBarContainer.Child = canvas;
        }

        private void RenderArrows()
        {
            ArrowCanvas.Children.Clear();

            double availableWidth = ArrowCanvas.ActualWidth > 0 ? ArrowCanvas.ActualWidth : 750;
            double currentX = 2;

            long totalSize = _volumes.Sum(v => v.OriginalSize);

            foreach (var volume in _volumes)
            {
                double volumeWidth = (volume.OriginalSize / (double)totalSize) * availableWidth;

                // Draw vertical line from source to target
                var line = new Line
                {
                    X1 = currentX + volumeWidth / 2,
                    Y1 = 5,
                    X2 = currentX + volumeWidth / 2,
                    Y2 = 55,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };

                ArrowCanvas.Children.Add(line);

                currentX += volumeWidth;
            }
        }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!(sender is Thumb thumb) || !_thumbToVolumeIndex.ContainsKey(thumb))
                return;

            int volumeIndex = _thumbToVolumeIndex[thumb];
            var volume = _volumes[volumeIndex];

            double availableWidth = TargetBarContainer.ActualWidth > 0 ? TargetBarContainer.ActualWidth - 4 : 750;
            double pixelChange = e.HorizontalChange;

            // Convert pixel change to size change
            long sizeChange = (long)(pixelChange / availableWidth * _targetDiskSize);
            long newSize = volume.TargetSize + sizeChange;

            int dragDirection = sizeChange > 0 ? 1 : -1;

            // Attempt resize
            bool success = _resizeManager!.ResizeVolume(volumeIndex, newSize, dragDirection);

            if (!success)
            {
                e.Handled = true;
            }
        }

        private void UpdateSizeLabels()
        {
            long totalOriginal = _volumes.Sum(v => v.OriginalSize);
            long totalTarget = _volumes.Sum(v => v.TargetSize);
            long freeSpace = _resizeManager!.RemainingSpace;

            txtSourceSize.Text = $"Total: {(totalOriginal / (1024.0 * 1024.0 * 1024.0)):F2} GB";
            txtTargetUsed.Text = $"Used: {(totalTarget / (1024.0 * 1024.0 * 1024.0)):F2} GB";
            txtTargetFree.Text = $"Free: {(freeSpace / (1024.0 * 1024.0 * 1024.0)):F2} GB";

            // Validate
            var (isValid, errorMsg) = _resizeManager!.Validate();
            if (!isValid)
            {
                txtTargetFree.Foreground = Brushes.Red;
                txtTargetFree.Text += " ?";
            }
            else
            {
                txtTargetFree.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            }
        }

        private Brush GetVolumeColor(int index)
        {
            var colors = new[]
            {
                Color.FromRgb(66, 165, 245),   // Blue
                Color.FromRgb(102, 187, 106),  // Green
                Color.FromRgb(255, 167, 38),   // Orange
                Color.FromRgb(171, 71, 188),   // Purple
                Color.FromRgb(239, 83, 80),    // Red
                Color.FromRgb(38, 198, 218),   // Cyan
            };

            return new SolidColorBrush(colors[index % colors.Length]);
        }

        private void btnAutoFit_Click(object sender, RoutedEventArgs e)
        {
            _resizeManager!.AutoFit();
            RenderBars();
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            foreach (var volume in _volumes)
            {
                volume.TargetSize = volume.OriginalSize;
            }
            RenderBars();
        }

        /// <summary>
        /// Gets the configured volume sizes for restore
        /// </summary>
        public List<VolumeResizeInfo> GetConfiguredVolumes()
        {
            return _volumes;
        }

        /// <summary>
        /// Validates the current configuration
        /// </summary>
        public (bool IsValid, string ErrorMessage) Validate()
        {
            return _resizeManager!.Validate();
        }
    }
}
