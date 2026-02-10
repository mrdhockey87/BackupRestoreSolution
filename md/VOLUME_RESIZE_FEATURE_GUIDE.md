# Volume Resize Feature Implementation Guide

## Overview
The Volume Resize feature allows users to restore disk/volume backups to different-sized target drives with interactive visual controls.

## Version
**Added in**: Version 4.9.0.0

## Features

### Core Functionality
1. **Visual Representation**: Two horizontal bars showing source and target configurations
2. **Interactive Resizing**: Drag arrow handles to adjust volume sizes
3. **Intelligent Constraints**: Prevents invalid configurations
4. **Auto-Fit**: Automatically proportions volumes to target disk
5. **Data-Aware**: Respects actual data size, allows shrinking if data fits

### User Interface Components

#### Source Bar (Top)
- Displays original backed-up volumes
- Shows each volume's original size
- Color-coded for easy identification
- Read-only visualization

#### Target Bar (Bottom)
- Shows adjustable target volume configuration
- Draggable arrow handles between volumes
- Real-time size updates
- Free space visualization

#### Arrow Handles
- Located at volume boundaries
- Drag to resize adjacent volumes
- Visual feedback during drag
- Constrained to valid sizes

## Windows WPF Implementation

### Files Created
```
BackupUI/Models/VolumeResizeInfo.cs          - Volume data model
BackupUI/Models/VolumeResizeManager.cs       - Resize logic and validation
BackupUI/Controls/VolumeResizeControl.xaml   - UI layout
BackupUI/Controls/VolumeResizeControl.xaml.cs - UI logic
```

### Usage Example (Windows)

```csharp
// In RestoreWindowNew.xaml.cs

// 1. Create volume resize information
var volumes = new List<VolumeResizeInfo>
{
    new VolumeResizeInfo
    {
        Label = "C:",
        OriginalSize = 100L * 1024 * 1024 * 1024,  // 100 GB
        DataSize = 60L * 1024 * 1024 * 1024,        // 60 GB actual data
        Index = 0
    },
    new VolumeResizeInfo
    {
        Label = "D:",
        OriginalSize = 500L * 1024 * 1024 * 1024,  // 500 GB
        DataSize = 300L * 1024 * 1024 * 1024,      // 300 GB actual data
        Index = 1
    }
};

// 2. Initialize the control
long targetDiskSize = 750L * 1024 * 1024 * 1024;  // 750 GB target disk
volumeResizeControl.Initialize(volumes, targetDiskSize);

// 3. When user clicks Restore, validate and get configuration
var (isValid, errorMsg) = volumeResizeControl.Validate();
if (!isValid)
{
    MessageBox.Show(errorMsg, "Invalid Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
    return;
}

var configuredVolumes = volumeResizeControl.GetConfiguredVolumes();
// Use configuredVolumes for restore operation
```

### Integration Points

#### In RestoreWindowNew.xaml
Add the control to the restore wizard:

```xml
<TabItem Header="Volume Configuration">
    <local:VolumeResizeControl x:Name="volumeResizeControl" />
</TabItem>
```

#### Reading Backup Metadata
The backup metadata needs to include:
- Original volume sizes
- Actual data sizes (used space)
- Volume labels/identifiers

## Linux Implementation Guide

### Qt GUI Version (LinuxRestore/GUI)

#### Required Components

1. **VolumeResizeInfo Class** (C++)
```cpp
class VolumeResizeInfo {
public:
    std::string label;
    int64_t originalSize;
    int64_t dataSize;
    int64_t targetSize;
    int index;
    
    int64_t minimumSize() const {
        return dataSize + (dataSize / 10);  // 10% overhead
    }
};
```

2. **VolumeResizeManager Class** (C++)
```cpp
class VolumeResizeManager {
private:
    std::vector<VolumeResizeInfo> volumes;
    int64_t targetDiskSize;
    
public:
    bool resizeVolume(int index, int64_t newSize, int direction);
    void autoFit();
    std::pair<bool, std::string> validate();
    int64_t totalAllocatedSize() const;
    int64_t remainingSpace() const;
};
```

3. **VolumeResizeWidget (Qt Widget)**

```cpp
class VolumeResizeWidget : public QWidget {
    Q_OBJECT
    
public:
    VolumeResizeWidget(QWidget *parent = nullptr);
    void initialize(const std::vector<VolumeResizeInfo>& volumes, 
                   int64_t targetDiskSize);
    std::vector<VolumeResizeInfo> getConfiguredVolumes();
    std::pair<bool, std::string> validate();
    
protected:
    void paintEvent(QPaintEvent *event) override;
    void mousePressEvent(QMouseEvent *event) override;
    void mouseMoveEvent(QMouseEvent *event) override;
    void mouseReleaseEvent(QMouseEvent *event) override;
    
private:
    void drawSourceBar(QPainter& painter);
    void drawTargetBar(QPainter& painter);
    void drawArrows(QPainter& painter);
    
    std::vector<VolumeResizeInfo> m_volumes;
    VolumeResizeManager m_resizeManager;
    int64_t m_targetDiskSize;
    int m_draggingVolumeIndex = -1;
};
```

4. **Implementation Tips**

- Use `QPainter` for drawing bars
- Track mouse position for drag operations
- Use `QRect` for hit testing on arrow handles
- Emit signals when sizes change for live updates

### ncurses TUI Version (LinuxRestore/TUI)

#### Text-Based Visualization

```
???????????????????????????????????????????????????????????????????????????????
? Volume Resize Configuration                                                 ?
???????????????????????????????????????????????????????????????????????????????
?                                                                              ?
? Original Backup Volumes:                              Total: 600.00 GB      ?
? ??????????????????????????????????????????????????????????????????????????? ?
? ?  C: (100.00 GB) ?              D: (500.00 GB)                           ? ?
? ??????????????????????????????????????????????????????????????????????????? ?
?                   ?                                 ?                        ?
? Target Disk Configuration:            Used: 600.00 GB   Free: 150.00 GB    ?
? ??????????????????????????????????????????????????????????????????????????? ?
? ?  C: (100.00 GB) ?              D: (500.00 GB)                    [Free] ? ?
? ??????????????????????????????????????????????????????????????????????????? ?
?                   ? (Arrow 1)                       ? (Arrow 2)             ?
?                                                                              ?
? [1] Adjust Volume 1 boundary   [2] Adjust Volume 2 boundary                ?
? [A] Auto Fit   [R] Reset   [C] Continue                                    ?
?                                                                              ?
? Selected: Volume 1   Size: 100.00 GB   Min: 66.00 GB                       ?
? Use arrow keys to resize, Enter to confirm                                  ?
???????????????????????????????????????????????????????????????????????????????
```

#### TUI Controls

1. **Number keys (1-9)**: Select volume boundary to adjust
2. **Left/Right arrows**: Increase/decrease selected volume size
3. **Page Up/Down**: Large adjustments (±10 GB)
4. **A**: Auto-fit all volumes
5. **R**: Reset to original sizes
6. **C**: Continue with configuration

#### Implementation Approach

```cpp
class VolumeResizeTUI {
private:
    std::vector<VolumeResizeInfo> volumes;
    VolumeResizeManager resizeManager;
    int selectedBoundary = 0;
    
    void drawInterface();
    void drawBar(int y, const std::string& label, 
                const std::vector<VolumeResizeInfo>& vols, 
                int64_t totalSize);
    void handleInput(int ch);
    void adjustVolume(int direction);  // -1 shrink, +1 grow
    
public:
    bool show(std::vector<VolumeResizeInfo>& volumes, 
             int64_t targetDiskSize);
};
```

## Resize Logic

### Constraints

1. **Minimum Size**: `DataSize + 10%` overhead
2. **Maximum Size**: Cannot exceed target disk capacity
3. **Adjacent Volume Impact**: Growing one volume may shrink neighbors
4. **Total Capacity**: Sum of all volumes ? target disk size

### Resize Algorithm

```
When dragging arrow right (growing left volume):
1. Calculate size delta
2. Check if new size < minimum ? reject
3. Check if growth fits in free space ? allow
4. If not enough free space:
   - Try shrinking volumes to the right
   - Calculate total shrinkable space
   - If enough ? distribute shrinkage proportionally
   - If not enough ? reject

When dragging arrow left (shrinking left volume):
1. Calculate size delta
2. Check if new size < minimum ? reject
3. Allow shrinkage (frees up space)
```

### Auto-Fit Algorithm

```
1. Calculate scaling factor = targetDiskSize / totalOriginalSize
2. For each volume:
   scaledSize = originalSize * scalingFactor
   targetSize = max(scaledSize, minimumSize)
3. If total > targetDiskSize:
   - Find largest shrinkable volume
   - Shrink to fit
```

## Data Size Calculation

### Windows Implementation

Use `GetCompressedFileSize` or filesystem queries:

```csharp
// From backup metadata
long CalculateActualDataSize(string volumePath)
{
    long totalDataSize = 0;
    var files = Directory.GetFiles(volumePath, "*", SearchOption.AllDirectories);
    
    foreach (var file in files)
    {
        var info = new FileInfo(file);
        totalDataSize += info.Length;
    }
    
    return totalDataSize;
}
```

### Linux Implementation

```cpp
#include <sys/stat.h>
#include <dirent.h>

int64_t calculateActualDataSize(const std::string& volumePath) {
    int64_t totalSize = 0;
    // Walk directory tree
    // Sum st_blocks * 512 (or st_size)
    return totalSize;
}
```

## Testing Scenarios

### Test Case 1: Grow Volume
- Original: C: 100GB, D: 200GB ? Target: 500GB disk
- Grow C: to 150GB
- Expected: C: 150GB, D: 200GB, Free: 150GB

### Test Case 2: Shrink Below Data Size
- C: has 80GB of actual data
- Try to shrink C: to 70GB
- Expected: Rejected (below minimum 88GB = 80GB + 10%)

### Test Case 3: Auto-Fit
- Original: C: 100GB, D: 200GB (300GB total) ? Target: 600GB
- Auto-fit
- Expected: C: 200GB, D: 400GB (proportional scaling)

### Test Case 4: Exceed Capacity
- Try to grow volumes beyond target disk size
- Expected: Rejected with error message

## Integration Checklist

### Windows
- [ ] Add VolumeResizeControl to RestoreWindowNew
- [ ] Read volume sizes from backup metadata
- [ ] Calculate actual data sizes
- [ ] Validate configuration before restore
- [ ] Pass target sizes to C++ restore engine
- [ ] Update VersionClass to 4.9.0.0

### Linux GUI
- [ ] Implement VolumeResizeWidget in Qt
- [ ] Integrate with restore workflow
- [ ] Test with various disk sizes
- [ ] Add to build system

### Linux TUI
- [ ] Implement VolumeResizeTUI with ncurses
- [ ] Create keyboard-driven interface
- [ ] Test in terminal environments
- [ ] Document controls

## Future Enhancements

1. **Filesystem-Aware Resizing**: Consider filesystem limits (NTFS min/max, ext4 limits)
2. **Partition Alignment**: Ensure 1MB alignment for optimal performance
3. **Undo/Redo**: Allow users to undo resize operations
4. **Save/Load Presets**: Save resize configurations for reuse
5. **Visual Warnings**: Highlight volumes near capacity
6. **Smart Suggestions**: AI-driven optimal resize recommendations

## References

- **GParted**: Open-source partition editor with similar UI
- **Windows Disk Management**: Reference for partition visualization
- **Qt Documentation**: For Linux GUI implementation
- **ncurses Programming Guide**: For TUI implementation

---

**Document Version**: 1.0  
**Last Updated**: February 2, 2026  
**Author**: BackupRestore Development Team
