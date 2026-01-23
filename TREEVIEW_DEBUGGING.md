# TreeView Debugging Guide - v3.0.0.5

## Problem
TreeView shows drives but no expand arrows (?) in front of them.

## Debugging Steps

### Step 1: Check Output Window
1. **Run in Debug**: Press F5
2. **Open Output Window**: View ? Output (or Ctrl+Alt+O)
3. **Select "Debug"** from the dropdown
4. **File ? New Backup**
5. **Look for these messages**:
   ```
   Disk 0: Found X volumes
   Creating TreeViewItem for: Disk 0 - XXX, Children: X
   Creating TreeViewItem for: C: (XXX), Children: 0
   ```

### Step 2: Interpret Results

**If you see "Found 0 volumes" for all disks:**
- WMI query is failing
- Volumes aren't being mapped to disks
- **Solution**: Check if you're running as Administrator

**If you see "Found X volumes" (X > 0):**
- Volumes are loading correctly
- **Check**: Do you see "Creating TreeViewItem" messages for each volume?
- **If NO**: Children aren't being added to TreeViewItems
- **If YES**: UI rendering issue

**If you see NO debug messages at all:**
- LoadDrives() isn't running
- Window might be crashing before load completes
- **Check**: Do you see the "Loading..." overlay?

### Step 3: Common Issues

#### Issue 1: Not Running as Administrator
**Symptom**: "Found 0 volumes" for all disks
**Cause**: WMI queries need admin rights to access disk information
**Fix**: 
- Right-click Visual Studio ? Run as Administrator
- Or: The app should auto-elevate via App.xaml.cs

#### Issue 2: WMI Service Not Running
**Symptom**: Exception in LoadPhysicalDrives
**Cause**: Windows Management Instrumentation service is stopped
**Fix**: 
```powershell
net start winmgmt
```

#### Issue 3: TreeViewItem Children Not Showing
**Symptom**: Volumes found but no expand arrows
**Cause**: TreeViewItem.Items.Add() called after TreeViewItem is added to parent
**Current Fix**: We moved Children addition BEFORE expansion event setup

### Step 4: Manual Test

If automatic detection fails, test with hardcoded data:

**Add this to BackupWindowNew_Loaded** (temporary):
```csharp
private async void BackupWindowNew_Loaded(object sender, RoutedEventArgs e)
{
    // TEST: Add dummy data to verify TreeView works
    var testDisk = new DriveTreeItem
    {
        Name = "TEST Disk 0",
        ItemType = DriveTreeItemType.Disk
    };
    
    testDisk.Children.Add(new DriveTreeItem
    {
        Name = "TEST C: (System)",
        ItemType = DriveTreeItemType.Volume,
        Parent = testDisk
    });
    
    testDisk.Children.Add(new DriveTreeItem
    {
        Name = "TEST D: (Data)",
        ItemType = DriveTreeItemType.Volume,
        Parent = testDisk
    });
    
    driveItems.Add(testDisk);
    
    // Create TreeViewItems from test data
    foreach (var drive in driveItems)
    {
        var treeItem = CreateTreeViewItem(drive);
        treeViewDrives.Items.Add(treeItem);
    }
    
    // If expand arrow shows now, WMI query is the problem
    // If still no arrow, TreeView rendering is the problem
}
```

### Expected Output in Debug Window

```
Disk 0: Found 2 volumes
Creating TreeViewItem for: Disk 0 - Samsung SSD, Children: 2
Creating TreeViewItem for: C: (Windows), Children: 0
Creating TreeViewItem for: D: (Recovery), Children: 0
Disk 1: Found 1 volumes
Creating TreeViewItem for: Disk 1 - WD Blue, Children: 1
Creating TreeViewItem for: E: (Backup), Children: 0
```

### Step 5: Verify TreeView Structure

**In Debug mode, inspect TreeView:**
1. Set breakpoint after `treeViewDrives.Items.Add(treeItem);`
2. In Immediate Window, type:
   ```csharp
   treeViewDrives.Items.Count
   ((TreeViewItem)treeViewDrives.Items[0]).Items.Count
   ((TreeViewItem)treeViewDrives.Items[0]).Header
   ```
3. **Expected**:
   - `Items.Count` = number of disks
   - First item's `Items.Count` = number of volumes on that disk
   - `Header` = StackPanel with checkbox and text

### Step 6: Check for Exceptions

**In Visual Studio:**
1. Debug ? Windows ? Exception Settings
2. Check "Common Language Runtime Exceptions"
3. Run again
4. See if any exceptions are thrown and caught silently

### Possible Causes Summary

| Symptom | Cause | Fix |
|---------|-------|-----|
| No expand arrows | No children added | Check debug output for "Found X volumes" |
| "Found 0 volumes" | WMI query failed | Run as Administrator |
| Exception on load | Missing assembly | Install System.Management NuGet package |
| Blank window | Crash during load | Check Output window for stack trace |
| Arrows on test data only | WMI mapping broken | Review LoadVolumesForDisk WMI query |

## Next Steps

Based on what you see in the Output window, we can:
1. **Fix WMI query** if volumes aren't being found
2. **Fix TreeView rendering** if volumes are found but arrows don't show
3. **Add error handling** if exceptions are being thrown

## Quick Fix Options

### Option 1: Simplify to DriveInfo (no WMI)
If WMI is the problem, fall back to simpler approach:
```csharp
foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
{
    var volumeItem = new DriveTreeItem
    {
        Name = $"{drive.Name} ({drive.VolumeLabel})",
        ItemType = DriveTreeItemType.Volume
    };
    // Just show all volumes, don't group by disk
}
```

### Option 2: Force Expand Arrows
Always add a dummy child to ensure arrows show:
```csharp
if (diskItem.Children.Count == 0)
{
    diskItem.Children.Add(new DriveTreeItem 
    { 
        Name = "Loading...", 
        ItemType = DriveTreeItemType.Volume 
    });
}
```

## Testing Checklist

- [ ] Run as Administrator
- [ ] Check Output window for debug messages
- [ ] Verify WMI service is running
- [ ] Try with test/dummy data
- [ ] Check Exception Settings for silent exceptions
- [ ] Inspect TreeView.Items in debugger
- [ ] Check if System.Management NuGet package is installed

## Contact for Help

If none of these work, provide:
1. **Output window contents** (all debug messages)
2. **Screenshot** of backup window
3. **Any error messages** shown
4. **OS version** (Windows 10/11/Server)
5. **Is Hyper-V installed?** (affects WMI queries)
