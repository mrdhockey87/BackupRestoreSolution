# Version 5.13.3.17 - Retention Panel Initial Visibility Fix

## Issue

When the BackupWindowNew opened with "Full Backup" preselected (which is the default), the retention settings panel ("Keep last N backups") did not appear initially. Users had to:
1. Click a different backup type (e.g., Incremental)
2. Click back to Full Backup
3. Only then would the retention panel appear

This was confusing because Full Backup was already selected, but its associated settings were hidden.

## Root Cause

The `BackupType_Changed` event handler only fires when the user **clicks** a radio button. It does not fire when:
- The window initially loads with a preselected value (`IsChecked="True"` in XAML)
- Code programmatically sets the checked state during initialization

Since the XAML has:
```xaml
<RadioButton Name="rbFullBackup" Content="Full Backup" GroupName="BackupType" IsChecked="True" ... />
```

And the retention panel has:
```xaml
<StackPanel Name="pnlRetentionSettings" Visibility="Collapsed">
```

The panel remained collapsed on initial load, even though Full Backup was selected.

## Solution

Added visibility update logic to the `BackupWindowNew_Loaded` event handler. After loading drives and pre-selecting items (when editing), the code now checks if Full Backup is selected and shows the retention panel accordingly.

### Code Changes

**File: BackupUI/Windows/BackupWindowNew.xaml.cs**

```csharp
private async void BackupWindowNew_Loaded(object sender, RoutedEventArgs e)
{
    try
    {
        await LoadDrives();
        
        // Pre-select items if editing a job
        if (_pathsToPreselect != null && _pathsToPreselect.Count > 0)
        {
            PreSelectItems(_pathsToPreselect);
        }

        // Update retention settings visibility based on initially selected backup type
        // This ensures the panel shows correctly when Full Backup is preselected
        if (pnlRetentionSettings != null)
        {
            bool isFullBackup = rbFullBackup?.IsChecked == true;
            pnlRetentionSettings.Visibility = isFullBackup ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Error loading drives: {ex.Message}\n\nStack Trace: {ex.StackTrace}", 
            "Error",
            MessageBoxButton.OK, 
            MessageBoxImage.Error);
    }
}
```

## Behavior After Fix

### Creating New Backup Job
1. Window opens with "Full Backup" preselected
2. Retention panel is **immediately visible**
3. Shows "Keep last N backups" settings
4. User can configure retention without changing backup types

### Editing Existing Job
- **Full Backup job**: Retention panel visible on load
- **Incremental/Differential job**: Retention panel hidden on load
- **Clone jobs**: Retention panel hidden on load

### Changing Backup Types
- Select Full Backup ? Retention panel shows
- Select any other type ? Retention panel hides
- Works correctly via `BackupType_Changed` event handler

## Testing Checklist

- [x] Open new backup window ? Full Backup selected ? Retention panel visible
- [x] Edit Full Backup job ? Retention panel visible on load
- [x] Edit Incremental job ? Retention panel hidden on load
- [x] Edit Differential job ? Retention panel hidden on load
- [x] Edit Clone job ? Retention panel hidden on load
- [x] Change from Full to Incremental ? Panel hides
- [x] Change from Incremental to Full ? Panel shows
- [x] Build successful
- [x] No runtime errors

## User Experience Improvement

### Before Fix
? Open new backup window ? Full Backup selected ? Retention panel hidden (confusing!)
? Must change to Incremental then back to Full to see panel
? Extra clicks required for every new backup

### After Fix
? Open new backup window ? Full Backup selected ? Retention panel visible immediately
? All relevant settings visible without toggling
? Intuitive, predictable behavior
? Matches user expectations

## Technical Notes

### Event Timing
1. **InitializeComponent()** - XAML parsed, controls created, `rbFullBackup.IsChecked = true`
2. **BackupWindowNew_Loaded** - Window fully loaded, drives loading starts
3. After `LoadDrives()` completes - Visibility update logic runs
4. Panel visibility updated based on current selection

### Why Not in Constructor?
Setting visibility in the constructor (after `InitializeComponent()`) would work, but:
- May cause brief flicker as controls are still being laid out
- Better to wait until window is fully loaded and stable
- Keeps initialization logic grouped in Loaded event

### Alternative Solutions Considered

1. **Set default visibility to Visible in XAML**
   - Issue: Shows for all backup types initially, then hides
   - Causes visual flicker

2. **Use Loaded event on panel itself**
   - More complex, requires additional event handler
   - Less maintainable

3. **Call BackupType_Changed manually in constructor**
   - Requires checking if controls are initialized
   - More fragile, depends on initialization order

The chosen solution (visibility check in `BackupWindowNew_Loaded`) is:
- Clean and maintainable
- Runs at the right time (after controls are ready)
- Minimal code changes
- Consistent with existing patterns

## Version History

**Version 5.13.3.17** - Fixed retention panel initial visibility (this version)
**Version 5.13.3.16** - Added retention panel with conditional visibility
**Version 5.13.3.15** - Implemented backup retention feature

## Related Files

- `BackupUI/Windows/BackupWindowNew.xaml` - Window definition with retention panel
- `BackupUI/Windows/BackupWindowNew.xaml.cs` - Event handlers and visibility logic
- `BackupUI/Models/BackupJob.cs` - RetainFullBackupCount property
- `BackupService/BackupExecutor.cs` - Backup retention implementation

## Conclusion

This fix ensures the UI correctly reflects the selected backup type from the moment the window appears. Users no longer need to toggle backup types to access retention settings, providing a smoother, more intuitive experience.
