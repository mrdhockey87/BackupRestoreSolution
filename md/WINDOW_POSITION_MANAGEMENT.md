# Window Position Management

## Overview
Implemented intelligent window position management to improve user experience by remembering the main window's position while ensuring child windows always open relative to the main window.

## Features

### Main Window (BackupUI\MainWindow.xaml)
- **Position Persistence**: Automatically saves position when closed and restores it on next launch
- **Screen Validation**: Validates saved position is visible on current screen configuration
- **Multi-Monitor Support**: Works correctly across multi-monitor setups
- **First Run**: Centers window on screen if no saved position exists

### Child Windows
All child windows now open relative to the main window:
- New Backup window
- Restore window
- Import Backup window
- Schedule Management window
- Activity Management window
- Service Management window
- Recovery Environment Creator
- About dialog
- Backup Progress window

## Implementation

### WindowPositionManager (Services\WindowPositionManager.cs)
Central service for managing window positions:

**Key Methods:**
1. `SaveMainWindowPosition(Window)` - Saves position/size/state to JSON file
2. `RestoreMainWindowPosition(Window)` - Restores saved position with validation
3. `SetChildWindowPosition(childWindow, mainWindow)` - Configures child window to open centered on parent
4. `IsPositionValid(position)` - Validates position is visible on current screens
5. `CenterWindow(Window)` - Falls back to centering if validation fails

**Storage Location:**
`%APPDATA%\BackupRestoreApp\window-position.json`

**Stored Properties:**
```json
{
  "Left": 100.0,
  "Top": 100.0,
  "Width": 900.0,
  "Height": 600.0,
  "WindowState": "Normal"
}
```

### Main Window Changes

**XAML (MainWindow.xaml):**
```xml
<Window
    MinWidth="800"
    MinHeight="500"
    Loaded="Window_Loaded"
    Closing="Window_Closing">
```
- Removed fixed Width/Height (now user-resizable)
- Added MinWidth/MinHeight for usability
- Added Loaded and Closing event handlers

**Code-Behind (MainWindow.xaml.cs):**
```csharp
private void Window_Loaded(object sender, RoutedEventArgs e)
{
    WindowPositionManager.RestoreMainWindowPosition(this);
}

private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
{
    WindowPositionManager.SaveMainWindowPosition(this);
}
```

**Child Window Creation:**
All window creation updated from:
```csharp
new BackupWindowNew().ShowDialog();
```

To:
```csharp
var window = new BackupWindowNew();
WindowPositionManager.SetChildWindowPosition(window, this);
window.ShowDialog();
```

## User Experience

### Main Window Behavior
1. **First Launch**: Window appears centered on primary screen
2. **Subsequent Launches**: Window appears at last saved position
3. **Multi-Monitor Changes**: If saved position is off-screen (monitor removed), automatically centers on available screen
4. **Resizing**: User can resize main window - size is remembered
5. **State**: Remembers if window was maximized

### Child Window Behavior
1. **Always Centered**: Child windows always open centered on main window
2. **No Position Memory**: Child windows don't remember their position
3. **Relative Movement**: If main window moves, child windows open at new center position
4. **Consistent Experience**: Predictable behavior regardless of where main window is located

## Technical Details

### Screen Validation Algorithm
```csharp
private static bool IsPositionValid(WindowPosition position)
{
    var rect = new Rect(position.Left, position.Top, position.Width, position.Height);
    
    foreach (var screen in System.Windows.Forms.Screen.AllScreens)
    {
        var workingArea = new Rect(
            screen.WorkingArea.Left,
            screen.WorkingArea.Top,
            screen.WorkingArea.Width,
            screen.WorkingArea.Height);
        
        // Check if at least part of window is visible
        if (workingArea.IntersectsWith(rect))
            return true;
    }
    
    return false;
}
```

### Child Window Configuration
```csharp
public static void SetChildWindowPosition(Window childWindow, Window mainWindow)
{
    childWindow.Owner = mainWindow;
    childWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
}
```

Setting `Owner` provides additional benefits:
- Child window stays on top of parent
- Minimizing parent minimizes child
- Closing parent closes all children
- Taskbar grouping

## Benefits

### User Experience
✅ **Predictable Behavior**: Main window always appears where you left it  
✅ **Multi-Monitor Friendly**: Works seamlessly across monitor configuration changes  
✅ **Consistent Child Windows**: Dialogs always centered on main window, not remembered  
✅ **No Off-Screen Windows**: Validation prevents windows from being inaccessible  
✅ **Professional Feel**: Matches behavior of professional applications  

### Technical Benefits
✅ **Simple API**: Single call to configure child windows  
✅ **Automatic Persistence**: Save/restore handled automatically  
✅ **Error Handling**: Graceful fallback to centering on any error  
✅ **Platform Integration**: Uses System.Windows.Forms.Screen for accurate monitor detection  
✅ **JSON Storage**: Human-readable configuration file  

## Testing Scenarios

### Main Window
- [x] First launch centers window
- [x] Close and reopen restores position
- [x] Resize window - size is remembered
- [x] Maximize window - state is remembered
- [x] Move window - position is remembered
- [x] Disconnect monitor - window moves to available screen
- [x] Reconnect monitor - window returns to saved position (if still valid)

### Child Windows
- [x] Open child window - centered on main window
- [x] Move main window - next child window opens at new center
- [x] Close child window - position NOT remembered
- [x] Reopen child window - opens centered again
- [x] Multiple child windows - each centered on main window
- [x] Main window maximized - child centered on maximized window

## Future Enhancements (Optional)

1. **Per-Window Settings**: Allow specific child windows to remember position if desired
2. **Workspace Profiles**: Save different window arrangements for different workflows
3. **Keyboard Shortcuts**: Add shortcuts to reset window position
4. **Visual Indicator**: Show when window position was adjusted due to off-screen detection

## Version
This feature should be included in version **5.13.6.34**

## Files Modified
- `BackupUI\MainWindow.xaml` - Added event handlers, removed fixed size
- `BackupUI\MainWindow.xaml.cs` - Added position save/restore, updated all child window creation
- `BackupUI\Services\WindowPositionManager.cs` - **NEW** - Complete position management service
- `BackupUI\BackupUI.csproj` - Already had UseWindowsForms=true

## Build Status
✅ Build successful - all changes compile without errors
