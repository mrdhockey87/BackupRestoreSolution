using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Threading.Tasks;
using SecureServerBackup.Models;

namespace SecureServerBackup.Windows
{
    /// <summary>
    /// Interactive Volume Configuration Window - Version 5.13.5.0
    /// Allows users to visually resize volumes by clicking and dragging
    /// </summary>
    public partial class VolumeConfigurationWindow : Window
    {
        #region Data Structures

        /// <summary>
        /// Enhanced volume info with interactive properties
        /// </summary>
        private class InteractiveVolumeInfo
        {
            public string Label { get; set; } = string.Empty;
            public long OriginalSize { get; set; }      // Size from source
            public long CurrentSize { get; set; }       // User-modified size
            public long UsedSpace { get; set; }
            public long FreeSpace => CurrentSize - UsedSpace;
            public long MinSize { get; set; }           // Minimum = Used + 10%
            public long MaxSize { get; set; }           // Maximum based on available space
            public bool IsResizable { get; set; }
            public bool IsSystemVolume { get; set; }
            public string FileSystem { get; set; } = string.Empty;
            public int AllocationUnitSize { get; set; }

            // Original VolumeInfo so restore metadata is preserved on Accept
            public VolumeInfo? Source { get; set; }

            // UI State
            public int Index { get; set; }
            public bool IsSelected { get; set; }
            public Rectangle? UIElement { get; set; }    // Visual rectangle on canvas
            public TextBlock? LabelElement { get; set; } // Label text
        }

        /// <summary>
        /// Represents a draggable resize handle between two volumes
        /// </summary>
        private class ResizeHandle
        {
            public int LeftVolumeIndex { get; set; }    // Volume on left side
            public int RightVolumeIndex { get; set; }   // Volume on right side
            public double CenterX { get; set; }         // X position of handle center
            public Ellipse? UIElement { get; set; }      // Visual handle circle
            public bool IsEnabled { get; set; }         // Can this handle be dragged?
        }

        #endregion

        #region Private Fields

        private List<InteractiveVolumeInfo> volumes = new();
        private List<ResizeHandle> resizeHandles = new();
        private long targetTotalSize;
        private int sourceAllocationUnitSize;
        private int targetAllocationUnitSize;
        
        // Drag state
        private ResizeHandle? draggedHandle;
        private Point dragStartPoint;
        private List<long>? sizesBeforeDrag;
        
        // Selected volume state
        private InteractiveVolumeInfo? selectedVolume;
        
        // Rendering state
        private bool isRendering = false;
        private bool isResetting = false; // Track when reset is in progress
        
        // Constants
        private const double CANVAS_HEIGHT = 80;
        private const double HANDLE_RADIUS = 8;
        private const double HANDLE_HIT_RADIUS = 16; // Larger hit area for easier clicking
        private const double MIN_VOLUME_WIDTH = 30;  // Minimum visual width
        
        #endregion

        #region Constructor and Initialization

        public VolumeInfo[]? FinalConfiguration { get; private set; }

        public VolumeConfigurationWindow(List<VolumeInfo> sourceVols, long targetSize, int sourceAUS, int targetAUS)
        {
            InitializeComponent();
            
            targetTotalSize = targetSize;
            sourceAllocationUnitSize = sourceAUS;
            targetAllocationUnitSize = targetAUS;
            
            // Convert VolumeInfo to InteractiveVolumeInfo
            volumes = sourceVols.Select((v, idx) => new InteractiveVolumeInfo
            {
                Index = idx,
                Label = v.Label,
                OriginalSize = v.Size,
                CurrentSize = v.Size,  // Initially same as original
                UsedSpace = v.UsedSpace,
                IsResizable = CanVolumeBeResized(v),
                IsSystemVolume = v.IsSystemVolume,
                FileSystem = v.FileSystem,
                AllocationUnitSize = v.AllocationUnitSize,
                MinSize = CalculateMinimumSize(v),
                MaxSize = v.Size,  // Will be recalculated based on target
                Source = v
            }).ToList();
            
            resizeHandles = new List<ResizeHandle>();
            
            Loaded += VolumeConfigurationWindow_Loaded;
        }

        private async void VolumeConfigurationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Start analysis immediately - window is already shown
            await AnalyzeAndRender();
        }

        #endregion

        #region Analysis and Initial Rendering

        private async Task AnalyzeAndRender()
        {
            try
            {
                pnlCalculating.Visibility = Visibility.Visible;
                txtCalculatingStatus.Text = "Analyzing disk compatibility...";
                await Task.Delay(50); // Short delay to let UI update

                // Calculate total source size
                long sourceTotalSize = volumes.Sum(v => v.OriginalSize);
                
                // Check if source fits on target
                long totalUsedSpace = volumes.Sum(v => CalculateActualUsedSpace(v));
                long requiredSpace = (long)(totalUsedSpace * 1.05); // 5% overhead
                
                txtCalculatingStatus.Text = "Calculating volume constraints...";
                await Task.Delay(50);

                // Calculate max sizes for resizable volumes
                long nonResizableSpace = volumes.Where(v => !v.IsResizable).Sum(v => v.CurrentSize);
                long availableForResizable = targetTotalSize - nonResizableSpace;
                long currentResizableTotal = volumes.Where(v => v.IsResizable).Sum(v => v.CurrentSize);
                
                // If source > target, we need to shrink resizable volumes proportionally
                if (sourceTotalSize > targetTotalSize)
                {
                    long excessSpace = sourceTotalSize - targetTotalSize;
                    
                    foreach (var vol in volumes.Where(v => v.IsResizable))
                    {
                        // Proportionally reduce size
                        double shrinkRatio = (double)vol.CurrentSize / currentResizableTotal;
                        long reduction = (long)(excessSpace * shrinkRatio);
                        vol.CurrentSize = Math.Max(vol.MinSize, vol.OriginalSize - reduction);
                        vol.MaxSize = vol.OriginalSize; // Can't grow larger than original
                    }
                }
                else
                {
                    // Source fits, calculate max sizes
                    long extraSpace = targetTotalSize - sourceTotalSize;
                    
                    foreach (var vol in volumes)
                    {
                        if (vol.IsResizable)
                        {
                            // Can grow up to original size + proportional share of extra space
                            double growthShare = (double)vol.CurrentSize / currentResizableTotal;
                            vol.MaxSize = vol.OriginalSize + (long)(extraSpace * growthShare);
                        }
                        else
                        {
                            vol.MaxSize = vol.CurrentSize; // Fixed size
                        }
                    }
                }

                txtCalculatingStatus.Text = "Rendering interactive display...";
                await Task.Delay(50);

                // Update UI text
                txtSourceDiskInfo.Text = $"Source Disk ({volumes.Count} volume{(volumes.Count > 1 ? "s" : "")})";
                txtSourceSize.Text = $"Total: {FormatSize(sourceTotalSize)}";
                txtTargetDiskInfo.Text = "Target Disk (Resizable)";
                txtTargetSize.Text = $"Total: {FormatSize(targetTotalSize)}";


                long currentTotal = volumes.Sum(v => v.CurrentSize);
                long availableSpace = targetTotalSize - currentTotal;
                txtSpaceInfo.Text = $"Source: {FormatSize(sourceTotalSize)} ? Target: {FormatSize(targetTotalSize)} " +
                                   $"(Extra space: {FormatSize(availableSpace)})";

                txtDescription.Text = "Click a volume to select it. Drag the blue handles (?) to resize volumes. " +
                                     "Grey volumes cannot be resized.";

                // Hide progress first to allow layout to update
                pnlCalculating.Visibility = Visibility.Collapsed;
                pnlVisualization.Visibility = Visibility.Visible;
                
                // Force layout update before rendering
                canvasTargetDisk.UpdateLayout();
                
                // Render target disk only if canvas has valid dimensions
                if (canvasTargetDisk.ActualWidth > 0 && canvasTargetDisk.ActualHeight > 0)
                {
                    RenderTargetDisk();
                }
                else
                {
                    // If canvas not sized yet, SizeChanged will trigger render
                    System.Diagnostics.Debug.WriteLine("Canvas not sized yet, will render on SizeChanged");
                }

                // Check for warnings
                if (requiredSpace > targetTotalSize)
                {
                    int resizableCount = volumes.Count(v => v.IsResizable);
                    if (resizableCount > 0)
                    {
                        ShowWarning($"Source ({FormatSize(sourceTotalSize)}) is larger than target ({FormatSize(targetTotalSize)}). " +
                                   $"{resizableCount} volume(s) have been automatically resized to fit. You can adjust sizes by dragging the handles.");
                    }
                    else
                    {
                        ShowWarning($"Source ({FormatSize(sourceTotalSize)}) is larger than target ({FormatSize(targetTotalSize)}) " +
                                   $"and no volumes can be resized. Please select a larger target disk.");
                        btnAccept.IsEnabled = false;
                    }
                }

                txtStatus.Text = "Ready. Click a volume or drag a handle to resize.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error analyzing disk: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        #endregion

        #region Rendering Methods

        // Source disk rendering removed - only target disk is shown for clarity

        private void RenderTargetDisk()
        {
            if (isRendering)
            {
                System.Diagnostics.Debug.WriteLine("RenderTargetDisk: Already rendering, skipping");
                return;
            }
            
            // Don't render if canvas hasn't been sized yet
            if (canvasTargetDisk.ActualWidth <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"RenderTargetDisk: Canvas width is {canvasTargetDisk.ActualWidth}, skipping render");
                return;
            }

            try
            {
                isRendering = true;

                canvasTargetDisk.Children.Clear();
                resizeHandles.Clear();

                double canvasWidth = canvasTargetDisk.ActualWidth;
                double canvasHeight = canvasTargetDisk.ActualHeight > 0 ? canvasTargetDisk.ActualHeight : 300;
                double yOffset = Math.Max(0, (canvasHeight - CANVAS_HEIGHT) / 2);
                
                System.Diagnostics.Debug.WriteLine($"RenderTargetDisk: Rendering with canvas size = {canvasWidth} x {canvasHeight}, yOffset = {yOffset}");


                long currentTotal = volumes.Sum(v => v.CurrentSize);
                
                // Use the LARGER of currentTotal or targetTotalSize as denominator
                // This prevents overflow when volumes exceed target, but also shows free space when they don't
                long scalingBase = Math.Max(currentTotal, targetTotalSize);
                
                double currentX = 0;

            for (int i = 0; i < volumes.Count; i++)
            {
                var vol = volumes[i];
                // Scale based on the larger value to prevent overflow and show free space
                double volWidth = Math.Max(MIN_VOLUME_WIDTH, (vol.CurrentSize / (double)scalingBase) * canvasWidth);

                // Create volume rectangle
                Rectangle rect = new Rectangle
                {
                    Width = volWidth - 4,
                    Height = CANVAS_HEIGHT,
                    Tag = i, // Store index
                    Cursor = Cursors.Hand
                };

                // Set style based on selection and resizability
                if (vol.IsSelected)
                {
                    rect.Fill = new SolidColorBrush(Color.FromRgb(33, 150, 243));
                    rect.Stroke = new SolidColorBrush(Color.FromRgb(25, 118, 210));
                    rect.StrokeThickness = 3;
                }
                else if (vol.IsResizable)
                {
                    rect.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                    rect.Stroke = new SolidColorBrush(Color.FromRgb(56, 142, 60));
                    rect.StrokeThickness = 2;
                }
                else
                {
                    rect.Fill = new SolidColorBrush(Color.FromRgb(158, 158, 158));
                    rect.Stroke = new SolidColorBrush(Color.FromRgb(97, 97, 97));
                    rect.StrokeThickness = 2;
                    rect.Opacity = 0.7;
                }

                rect.RadiusX = 4;
                rect.RadiusY = 4;

                Canvas.SetLeft(rect, currentX);
                Canvas.SetTop(rect, yOffset);
                canvasTargetDisk.Children.Add(rect);

                vol.UIElement = rect;

                // Label
                TextBlock label = new TextBlock
                {
                    Tag = i,
                    TextAlignment = TextAlignment.Center,
                    Foreground = Brushes.Black,
                    Width = volWidth - 4,
                    IsHitTestVisible = false
                };

                if (volWidth > 80)
                {
                    label.Text = $"{vol.Label}\n{FormatSize(vol.CurrentSize)}";
                    label.FontSize = 10;
                    Canvas.SetTop(label, yOffset + CANVAS_HEIGHT / 2 - 12);
                }
                else if (volWidth > 40)
                {
                    label.Text = vol.Label.Length > 4 ? vol.Label.Substring(0, 4) : vol.Label;
                    label.FontSize = 9;
                    Canvas.SetTop(label, yOffset + CANVAS_HEIGHT / 2 - 6);
                }

                if (volWidth > 40)
                {
                    Canvas.SetLeft(label, currentX);
                    canvasTargetDisk.Children.Add(label);
                    vol.LabelElement = label;
                }

                currentX += volWidth;

                // Add resize handle if next volume is also resizable
                if (i < volumes.Count - 1)
                {
                    var nextVol = volumes[i + 1];
                    bool canResize = vol.IsResizable || nextVol.IsResizable;

                    ResizeHandle handle = new ResizeHandle
                    {
                        LeftVolumeIndex = i,
                        RightVolumeIndex = i + 1,
                        CenterX = currentX,
                        IsEnabled = canResize
                    };

                    if (canResize)
                    {
                        Ellipse handleEllipse = new Ellipse
                        {
                            Width = HANDLE_RADIUS * 2,
                            Height = HANDLE_RADIUS * 2,
                            Fill = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                            Stroke = new SolidColorBrush(Color.FromRgb(25, 118, 210)),
                            StrokeThickness = 2,
                            Cursor = Cursors.SizeWE,
                            Tag = handle,
                            Opacity = 0.8
                        };

                        Canvas.SetLeft(handleEllipse, currentX - HANDLE_RADIUS);
                        Canvas.SetTop(handleEllipse, yOffset + CANVAS_HEIGHT / 2 - HANDLE_RADIUS);
                        canvasTargetDisk.Children.Add(handleEllipse);

                        handle.UIElement = handleEllipse;
                    }

                    resizeHandles.Add(handle);
                }
            }

            // Add free space indicator if any
            long freeSpace = targetTotalSize - currentTotal;
            if (freeSpace > 0 && currentX < canvasWidth - 10)
            {
                double freeWidth = canvasWidth - currentX;
                
                Rectangle freeRect = new Rectangle
                {
                    Width = freeWidth,
                    Height = CANVAS_HEIGHT,
                    Fill = Brushes.Transparent,
                    Stroke = new SolidColorBrush(Color.FromRgb(189, 189, 189)),
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    StrokeThickness = 2,
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(freeRect, currentX);
                Canvas.SetTop(freeRect, yOffset);
                canvasTargetDisk.Children.Add(freeRect);

                TextBlock freeLabel = new TextBlock
                {
                    Text = $"Free\n{FormatSize(freeSpace)}",
                    FontSize = 10,
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(117, 117, 117)),
                    Width = freeWidth,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(freeLabel, currentX);
                Canvas.SetTop(freeLabel, yOffset + CANVAS_HEIGHT / 2 - 12);
                canvasTargetDisk.Children.Add(freeLabel);
            }
            }
            finally
            {
                isRendering = false;
            }
        }

        #endregion

        #region Mouse Event Handlers

        private void CanvasTargetDisk_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point clickPoint = e.GetPosition(canvasTargetDisk);

            // Check if clicked on a resize handle
            foreach (var handle in resizeHandles.Where(h => h.IsEnabled && h.UIElement != null))
            {
                double handleLeft = Canvas.GetLeft(handle.UIElement);
                double handleTop = Canvas.GetTop(handle.UIElement);
                double dx = clickPoint.X - (handleLeft + HANDLE_RADIUS);
                double dy = clickPoint.Y - (handleTop + HANDLE_RADIUS);
                double distance = Math.Sqrt(dx * dx + dy * dy);

                if (distance <= HANDLE_HIT_RADIUS)
                {
                    // Start dragging handle
                    draggedHandle = handle;
                    dragStartPoint = clickPoint;
                    sizesBeforeDrag = volumes.Select(v => v.CurrentSize).ToList();
                    canvasTargetDisk.CaptureMouse();
                    txtStatus.Text = "Dragging... Release to apply resize.";
                    return;
                }
            }

            // Check if clicked on a volume
            foreach (var vol in volumes)
            {
                if (vol.UIElement != null)
                {
                    double left = Canvas.GetLeft(vol.UIElement);
                    double top = Canvas.GetTop(vol.UIElement);
                    double width = vol.UIElement.Width;
                    double height = vol.UIElement.Height;

                    if (clickPoint.X >= left && clickPoint.X <= left + width &&
                        clickPoint.Y >= top && clickPoint.Y <= top + height)
                    {
                        SelectVolume(vol);
                        return;
                    }
                }
            }

            // Clicked empty space - deselect
            DeselectVolume();
        }

        private void CanvasTargetDisk_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggedHandle == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point currentPoint = e.GetPosition(canvasTargetDisk);
            double deltaX = currentPoint.X - dragStartPoint.X;

            // Calculate new sizes
            var leftVol = volumes[draggedHandle.LeftVolumeIndex];
            var rightVol = volumes[draggedHandle.RightVolumeIndex];

            // Convert pixel delta to size delta (proportional to canvas width)
            double canvasWidth = canvasTargetDisk.ActualWidth;
            long sizeDelta = (long)((deltaX / canvasWidth) * targetTotalSize);

            // Calculate new sizes
            long newLeftSize = sizesBeforeDrag![leftVol.Index] + sizeDelta;
            long newRightSize = sizesBeforeDrag[rightVol.Index] - sizeDelta;

            // Apply constraints
            bool leftResizable = leftVol.IsResizable;
            bool rightResizable = rightVol.IsResizable;

            if (leftResizable && rightResizable)
            {
                // Both resizable - constrain both
                newLeftSize = Math.Max(leftVol.MinSize, Math.Min(leftVol.MaxSize, newLeftSize));
                newRightSize = Math.Max(rightVol.MinSize, Math.Min(rightVol.MaxSize, newRightSize));
                
                // Maintain total size
                long totalBefore = sizesBeforeDrag[leftVol.Index] + sizesBeforeDrag[rightVol.Index];
                long totalAfter = newLeftSize + newRightSize;
                if (totalAfter != totalBefore)
                {
                    long adjustment = totalBefore - totalAfter;
                    if (sizeDelta > 0) // Growing left
                    {
                        newLeftSize += adjustment;
                        if (newLeftSize > leftVol.MaxSize)
                        {
                            newLeftSize = leftVol.MaxSize;
                            newRightSize = totalBefore - newLeftSize;
                        }
                    }
                    else // Growing right
                    {
                        newRightSize += adjustment;
                        if (newRightSize > rightVol.MaxSize)
                        {
                            newRightSize = rightVol.MaxSize;
                            newLeftSize = totalBefore - newRightSize;
                        }
                    }
                }
            }
            else if (leftResizable)
            {
                // Only left is resizable
                newLeftSize = Math.Max(leftVol.MinSize, Math.Min(leftVol.MaxSize, newLeftSize));
                newRightSize = sizesBeforeDrag[rightVol.Index]; // Fixed
            }
            else if (rightResizable)
            {
                // Only right is resizable
                newLeftSize = sizesBeforeDrag[leftVol.Index]; // Fixed
                newRightSize = Math.Max(rightVol.MinSize, Math.Min(rightVol.MaxSize, newRightSize));
            }

            // Apply new sizes
            leftVol.CurrentSize = newLeftSize;
            rightVol.CurrentSize = newRightSize;

            // Re-render
            RenderTargetDisk();

            // Update details if one of these volumes is selected
            if (selectedVolume != null && (selectedVolume.Index == leftVol.Index || selectedVolume.Index == rightVol.Index))
            {
                UpdateDetailsPanel();
            }

            UpdateStatusBar();
        }

        private void CanvasTargetDisk_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (draggedHandle != null)
            {
                draggedHandle = null;
                dragStartPoint = default;
                sizesBeforeDrag = null;
                canvasTargetDisk.ReleaseMouseCapture();
                txtStatus.Text = "Resize complete. Drag another handle or click Accept.";
            }
        }

        #endregion

        #region Selection Management

        private void SelectVolume(InteractiveVolumeInfo vol)
        {
            // Deselect previous
            if (selectedVolume != null)
            {
                selectedVolume.IsSelected = false;
            }

            // Select new
            selectedVolume = vol;
            vol.IsSelected = true;

            // Update UI - but skip rendering if reset is in progress
            if (!isResetting)
            {
                RenderTargetDisk();
            }
            UpdateDetailsPanel();

            pnlVolumeDetails.Visibility = Visibility.Visible;
            txtNoSelection.Visibility = Visibility.Collapsed;

            txtStatus.Text = $"Selected: {vol.Label} - Drag handles to resize, or click another volume.";
        }

        private void DeselectVolume(bool skipRender = false)
        {
            if (selectedVolume != null)
            {
                selectedVolume.IsSelected = false;
                selectedVolume = null;
                
                // Only render if not skipped (e.g., during reset where layout is being recalculated)
                if (!skipRender)
                {
                    RenderTargetDisk();
                }
            }

            pnlVolumeDetails.Visibility = Visibility.Collapsed;
            txtNoSelection.Visibility = Visibility.Visible;
            txtStatus.Text = "No volume selected. Click a volume to see details.";
        }

        private void UpdateDetailsPanel()
        {
            if (selectedVolume == null) return;

            txtSelectedVolume.Text = $"{selectedVolume.Label} ({selectedVolume.FileSystem})";
            txtVolSize.Text = FormatSize(selectedVolume.CurrentSize);
            txtVolUsed.Text = FormatSize(selectedVolume.UsedSpace);
            txtVolFree.Text = FormatSize(selectedVolume.FreeSpace);
            txtVolMin.Text = FormatSize(selectedVolume.MinSize);
            txtVolMax.Text = FormatSize(selectedVolume.MaxSize);
        }

        #endregion

        #region Canvas Events

        /// <summary>
        /// Handle canvas size changes to re-render at correct size
        /// </summary>
        private void CanvasTargetDisk_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Prevent re-entrant calls that could cause infinite loop
            if (isRendering)
                return;

            // Re-render when canvas size changes (fixes initial display issue)
            if (volumes != null && volumes.Count > 0 && canvasTargetDisk.ActualWidth > 0)
            {
                RenderTargetDisk();
            }
        }

        #endregion

        #region Helper Methods

        private bool CanVolumeBeResized(VolumeInfo volume)
        {
            // Criteria: NTFS and at least 10% free space
            if (!volume.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                return false;

            double freePercentage = (double)volume.FreeSpace / volume.Size * 100;
            if (freePercentage < 10)
                return false;

            return true;
        }

        private long CalculateMinimumSize(VolumeInfo volume)
        {
            // Minimum = Used space + 10% overhead
            return (long)(volume.UsedSpace * 1.10);
        }

        private long CalculateActualUsedSpace(InteractiveVolumeInfo volume)
        {
            // Account for allocation unit size differences
            if (sourceAllocationUnitSize == targetAllocationUnitSize)
                return volume.UsedSpace;

            long sourceUnits = (volume.UsedSpace + sourceAllocationUnitSize - 1) / sourceAllocationUnitSize;
            return sourceUnits * targetAllocationUnitSize;
        }

        private void UpdateStatusBar()
        {
            long currentTotal = volumes.Sum(v => v.CurrentSize);
            long extraSpace = targetTotalSize - currentTotal;
            
            long sourceTotalSize = volumes.Sum(v => v.OriginalSize);
            txtSpaceInfo.Text = $"Source: {FormatSize(sourceTotalSize)} ? Current: {FormatSize(currentTotal)} " +
                               $"(Free: {FormatSize(extraSpace)})";
        }

        private void ShowWarning(string message)
        {
            pnlWarning.Visibility = Visibility.Visible;
            txtWarningMessage.Text = message;
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        #endregion

        #region Button Handlers

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Reset all volumes to their original sizes?",
                "Confirm Reset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Flag that reset is in progress (prevents SelectVolume from rendering prematurely)
                isResetting = true;
                
                // Reset to original sizes FIRST
                foreach (var vol in volumes)
                {
                    vol.CurrentSize = vol.OriginalSize;
                }

                // Recalculate constraints after reset (same logic as initial AnalyzeAndRender)
                long sourceTotalSize = volumes.Sum(v => v.OriginalSize);
                long currentResizableTotal = volumes.Where(v => v.IsResizable).Sum(v => v.CurrentSize);
                
                if (sourceTotalSize > targetTotalSize)
                {
                    // Source larger than target - shrink resizable volumes proportionally
                    long excessSpace = sourceTotalSize - targetTotalSize;
                    
                    foreach (var vol in volumes.Where(v => v.IsResizable))
                    {
                        // Proportionally reduce size (same as initial auto-sizing)
                        double shrinkRatio = (double)vol.CurrentSize / currentResizableTotal;
                        long reduction = (long)(excessSpace * shrinkRatio);
                        vol.CurrentSize = Math.Max(vol.MinSize, vol.OriginalSize - reduction);
                        vol.MaxSize = vol.OriginalSize; // Can't grow larger than original
                    }
                    
                    // Non-resizable volumes keep original size
                    foreach (var vol in volumes.Where(v => !v.IsResizable))
                    {
                        vol.MaxSize = vol.CurrentSize;
                    }
                }
                else
                {
                    // Source fits - calculate growth potential
                    long extraSpace = targetTotalSize - sourceTotalSize;
                    
                    foreach (var vol in volumes)
                    {
                        if (vol.IsResizable)
                        {
                            // Can grow up to original size + proportional share of extra space
                            double growthShare = (double)vol.CurrentSize / currentResizableTotal;
                            vol.MaxSize = vol.OriginalSize + (long)(extraSpace * growthShare);
                        }
                        else
                        {
                            vol.MaxSize = vol.CurrentSize;
                        }
                    }
                }

                // Deselect without rendering (we'll render once at the end)
                DeselectVolume(skipRender: true);
                
                UpdateStatusBar();
                txtStatus.Text = "Layout reset to original configuration.";

                // Use dispatcher to defer render until after current message processing completes
                // This ensures any pending mouse events are processed first
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Force WPF to recalculate layout before reading ActualWidth
                    canvasTargetDisk.UpdateLayout();
                    
                    isResetting = false; // Clear flag before rendering
                    
                    double width = canvasTargetDisk.ActualWidth;
                    double height = canvasTargetDisk.ActualHeight;
                    
                    System.Diagnostics.Debug.WriteLine($"Reset: Rendering at {width} x {height}");
                    
                    // Now render with fresh canvas dimensions
                    if (width > 0 && height > 0)
                    {
                        RenderTargetDisk();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Reset: Canvas dimensions are zero!");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            // Validate configuration
            long totalSize = volumes.Sum(v => v.CurrentSize);
            if (totalSize > targetTotalSize)
            {
                MessageBox.Show(
                    $"Total size ({FormatSize(totalSize)}) exceeds target disk capacity ({FormatSize(targetTotalSize)}).\n\n" +
                    "Please adjust volume sizes before accepting.",
                    "Invalid Configuration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            foreach (var vol in volumes)
            {
                if (vol.CurrentSize < vol.MinSize)
                {
                    MessageBox.Show(
                        $"Volume {vol.Label} is too small ({FormatSize(vol.CurrentSize)}).\n\n" +
                        $"Minimum size is {FormatSize(vol.MinSize)}.",
                        "Invalid Configuration",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (vol.CurrentSize > vol.MaxSize)
                {
                    MessageBox.Show(
                        $"Volume {vol.Label} is too large ({FormatSize(vol.CurrentSize)}).\n\n" +
                        $"Maximum size is {FormatSize(vol.MaxSize)}.",
                        "Invalid Configuration",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            // Return final configuration (convert back to VolumeInfo, preserving restore metadata)
            FinalConfiguration = volumes.Select(v => new VolumeInfo
            {
                Label = v.Label,
                Size = v.CurrentSize,           // user-adjusted size
                UsedSpace = v.UsedSpace,
                IsResizable = v.IsResizable,
                IsSystemVolume = v.IsSystemVolume,
                FileSystem = v.FileSystem,
                AllocationUnitSize = v.AllocationUnitSize,
                // Propagate restore-layout metadata so ordering is preserved downstream
                ImageIndex            = v.Source?.ImageIndex            ?? 0,
                PartitionNumber       = v.Source?.PartitionNumber       ?? 0,
                PartitionOffsetBytes  = v.Source?.PartitionOffsetBytes  ?? 0,
                PartitionLengthBytes  = v.Source?.PartitionLengthBytes  ?? 0,
                PartitionStyle        = v.Source?.PartitionStyle        ?? string.Empty,
                PartitionType         = v.Source?.PartitionType         ?? string.Empty,
                SourceVolumeGuidPath  = v.Source?.SourceVolumeGuidPath  ?? string.Empty,
                SourceVolumeMountPath = v.Source?.SourceVolumeMountPath ?? string.Empty,
                IsBootVolume          = v.Source?.IsBootVolume          ?? false,
                TargetSize            = v.CurrentSize
            }).ToArray();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion
    }
}
