# Version 5.13.8.2 - Multi-Image Mount Support (C++ COMPLETE)

## What Was Implemented

### C++ BackupEngine Changes (✅ COMPLETE)

**WimMountManager.cpp:**
1. Added `imageIndex` parameter to `MountWim()` function
2. Updated `CreateMountPoint()` to include image index for unique paths
3. Mount path now includes image: `BackupMounts\WDrive_Image5_20260305_143022`
4. Validates image index against available images
5. Mounts specified image instead of hardcoded `1`

**New Export Functions:**
1. `WimMount_GetImageCount(wimPath, errorMsg, errorMsgSize)` - Returns number of images
2. `WimMount_GetImageInfo(wimPath, imageIndex, name, desc, errorMsg)` - Gets image metadata

**Function Signatures Updated:**
```cpp
// OLD
bool WimMount_MountWim(
    const wchar_t* wimPath,
    const wchar_t* backupName,
    const wchar_t* backupType,
    wchar_t* mountPath, int mountPathSize,
    wchar_t* errorMsg, int errorMsgSize
);

// NEW
bool WimMount_MountWim(
    const wchar_t* wimPath,
    const wchar_t* backupName,
    const wchar_t* backupType,
    int imageIndex,  // NEW: Which image to mount (1-based)
    wchar_t* mountPath, int mountPathSize,
    wchar_t* errorMsg, int errorMsgSize
);
```

### What Still Needs To Be Done

## C# Side (❌ TODO)

You need to update the C# code to use the new signatures:

### 1. Update P/Invoke Declarations

**File: `BackupUI/Services/NativeBackupMountManager.cs`**

Add the new declarations:

```csharp
[DllImport("BackupEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
private static extern int WimMount_GetImageCount(
    string wimPath,
    StringBuilder errorMsg,
    int errorMsgSize);

[DllImport("BackupEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
private static extern bool WimMount_GetImageInfo(
    string wimPath,
    int imageIndex,
    StringBuilder imageName,
    int imageNameSize,
    StringBuilder imageDescription,
    int imageDescriptionSize,
    StringBuilder errorMsg,
    int errorMsgSize);

[DllImport("BackupEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
private static extern bool WimMount_MountWim(
    string wimPath,
    string backupName,
    string backupType,
    int imageIndex,  // NEW parameter
    StringBuilder mountPath,
    int mountPathSize,
    StringBuilder errorMsg,
    int errorMsgSize);
```

### 2. Add Image Selection Methods

**File: `BackupUI/Services/BackupMountManager.cs`**

```csharp
public class WimImageInfo
{
    public int Index { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
}

public List<WimImageInfo> GetAvailableImages(string wimPath)
{
    var images = new List<WimImageInfo>();
    var errorMsg = new StringBuilder(1024);

    int count = WimMount_GetImageCount(wimPath, errorMsg, errorMsg.Capacity);
    if (count <= 0)
    {
        throw new Exception($"Failed to get image count: {errorMsg}");
    }

    for (int i = 1; i <= count; i++)
    {
        var name = new StringBuilder(256);
        var desc = new StringBuilder(512);

        if (WimMount_GetImageInfo(wimPath, i, name, name.Capacity, 
                                   desc, desc.Capacity, errorMsg, errorMsg.Capacity))
        {
            images.Add(new WimImageInfo
            {
                Index = i,
                Name = name.ToString(),
                Description = desc.ToString()
            });
        }
    }

    return images;
}
```

### 3. Update Mount Method

Update the existing `MountBackup` method to accept image index:

```csharp
public string MountBackup(string backupPath, int imageIndex = 1)
{
    var mountPath = new StringBuilder(260);
    var errorMsg = new StringBuilder(1024);

    string backupName = Path.GetFileNameWithoutExtension(backupPath);

    bool success = WimMount_MountWim(
        backupPath,
        backupName,
        "Backup",
        imageIndex,  // NEW: Pass the image index
        mountPath,
        mountPath.Capacity,
        errorMsg,
        errorMsg.Capacity
    );

    if (!success)
    {
        throw new Exception($"Failed to mount backup: {errorMsg}");
    }

    return mountPath.ToString();
}
```

### 4. Update UI (MainWindow.xaml.cs)

When user clicks "Mount" button on a backup:

```csharp
private async void MountBackup_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is string backupPath)
    {
        try
        {
            // Get available images
            var mountManager = new BackupMountManager();
            var images = mountManager.GetAvailableImages(backupPath);

            if (images.Count == 0)
            {
                MessageBox.Show("No images found in backup file.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // If only one image, mount it directly
            int selectedIndex = 1;

            // If multiple images, show selection dialog
            if (images.Count > 1)
            {
                var dialog = new ImageSelectionDialog(images);
                if (dialog.ShowDialog() != true)
                {
                    return; // User cancelled
                }
                selectedIndex = dialog.SelectedImageIndex;
            }

            // Mount the selected image
            string mountPath = mountManager.MountBackup(backupPath, selectedIndex);

            MessageBox.Show($"Backup mounted successfully!\n\nMount Path: {mountPath}",
                "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            // Refresh mounted backups list
            RefreshMountedBackups();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to mount backup: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
```

### 5. Create Image Selection Dialog

**File: `BackupUI/Windows/ImageSelectionDialog.xaml`**

```xaml
<Window x:Class="BackupUI.Windows.ImageSelectionDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Select Backup Image To Mount"
        Height="400" Width="600"
        WindowStartupLocation="CenterOwner">
    <Grid Margin="10">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="Select which backup point to mount:"
                   FontSize="14" FontWeight="Bold" Margin="0,0,0,10"/>

        <DataGrid Grid.Row="1" Name="dgImages" AutoGenerateColumns="False"
                  IsReadOnly="True" SelectionMode="Single">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Index" Binding="{Binding Index}" Width="60"/>
                <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="200"/>
                <DataGridTextColumn Header="Description" Binding="{Binding Description}" Width="*"/>
            </DataGrid.Columns>
        </DataGrid>

        <StackPanel Grid.Row="2" Orientation="Horizontal" 
                    HorizontalAlignment="Right" Margin="0,10,0,0">
            <Button Content="Mount" Width="100" Height="30" 
                    Margin="0,0,5,0" Click="Mount_Click"/>
            <Button Content="Cancel" Width="100" Height="30" 
                    Click="Cancel_Click"/>
        </StackPanel>
    </Grid>
</Window>
```

**File: `BackupUI/Windows/ImageSelectionDialog.xaml.cs`**

```csharp
public partial class ImageSelectionDialog : Window
{
    public int SelectedImageIndex { get; private set; }

    public ImageSelectionDialog(List<WimImageInfo> images)
    {
        InitializeComponent();
        dgImages.ItemsSource = images;
    }

    private void Mount_Click(object sender, RoutedEventArgs e)
    {
        if (dgImages.SelectedItem is WimImageInfo selected)
        {
            SelectedImageIndex = selected.Index;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("Please select an image to mount.", "Selection Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

## Testing Steps

1. **Create multi-image backup:**
   - Day 1: Full backup → WDrive.ssb (4 images)
   - Day 2: Incremental → WDrive.ssb (8 images)
   - Day 3: Incremental → WDrive.ssb (12 images)

2. **Test mounting:**
   - Open Mount Backups tab
   - Click Mount on WDrive.ssb
   - Should see dialog with 3 restore points:
     - Image 1-4: Day 1 Full
     - Image 5-8: Day 2 Incremental
     - Image 9-12: Day 3 Incremental
   - Select "Day 2 Incremental"
   - Mounts images 5-8
   - Check mount path includes `_Image5_`

3. **Verify:**
   - Can mount multiple images from same backup
   - Each mount has unique path
   - Can browse mounted images
   - Unmount works correctly

## Summary

**✅ C++ Backend:** Complete - Multi-image mount fully implemented
**❌ C# Interop:** TODO - Need to update P/Invoke declarations
**❌ C# Manager:** TODO - Need to add image selection methods
**❌ UI:** TODO - Need to add image selection dialog

The C++ engine is ready - just need to wire up the C# UI to use it!
