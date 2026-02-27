# SVG Icon Integration for Activity Tab Warnings

## Overview
Enhanced the Activity tab warning system to display SVG icons instead of emoji characters, providing a more professional and scalable visual indicator for errors and warnings.

## Changes Made

### 1. BackupLogger Service Enhancements (`BackupUI\Services\BackupLogger.cs`)

**Added Separate Detection Methods:**
```csharp
// Check for unread errors only (not warnings)
public static bool HasUnreadErrors()
{
    return LoadLogs().Any(l => !l.IsRead && l.Level == BackupLogLevel.Error);
}

// Check for unread warnings only (not errors)
public static bool HasUnreadWarnings()
{
    return LoadLogs().Any(l => !l.IsRead && l.Level == BackupLogLevel.Warning);
}
```

**Previous Implementation:**
- Single method `HasUnreadErrors()` checked for BOTH errors and warnings
- Could not differentiate between error severity and warning severity

**New Implementation:**
- Separate detection for errors vs warnings
- Allows displaying different icons based on severity level

### 2. MainWindow Activity Tab Warning (`BackupUI\MainWindow.xaml.cs`)

**Complete Redesign of UpdateActivityTabWarning() Method:**

#### SVG Icon Display Logic
1. **Check Severity Levels**
   ```csharp
   bool hasUnreadErrors = BackupLogger.HasUnreadErrors();
   bool hasUnreadWarnings = BackupLogger.HasUnreadWarnings();
   ```

2. **Priority System**
   - Errors take priority over warnings
   - If errors exist: Display `error_icon.svg` with dark red text (#8B0000)
   - If only warnings exist: Display `warning_icon.svg` with orange text (#FF8C00)
   - If neither exist: Display plain "Activity" text with black color

3. **Dynamic Header Construction**
   ```csharp
   var headerPanel = new StackPanel { Orientation = Horizontal };
   var textBlock = new TextBlock { Text = "Activity ", ... };
   var iconViewer = new SvgViewbox { Width = 16, Height = 16, ... };
   ```

4. **Icon File Resolution**
   - Looks for SVG files in `Images` folder relative to application directory
   - Path: `{AppDir}\Images\error_icon.svg` or `warning_icon.svg`
   - Falls back to emoji if SVG file not found
   - Gracefully handles file loading errors

#### Visual Hierarchy
```
[Activity Tab]
├─ No unread issues    → "Activity" (black text, no icon)
├─ Warnings only       → "Activity ⚠" (orange text, warning_icon.svg)
└─ Errors (any)        → "Activity ⚠" (dark red text, error_icon.svg)
```

### 3. Icon Specifications

**SVG Files Used:**
- `Images/error_icon.svg` - Error severity indicator
- `Images/warning_icon.svg` - Warning severity indicator

**Display Properties:**
- Width: 16px
- Height: 16px
- Vertical alignment: Center
- Left margin: 4px (spacing from text)

### 4. User Experience Flow

**Scenario 1: New Error Occurs**
1. Backup job fails with error
2. Activity tab header shows "Activity" + error icon (red text)
3. User clicks Activity tab
4. `MarkAllErrorsAsRead()` is called
5. Icon disappears, text returns to black
6. Icon reappears only when new error occurs

**Scenario 2: New Warning Occurs**
1. Backup job completes with warnings
2. Activity tab header shows "Activity" + warning icon (orange text)
3. User clicks Activity tab
4. `MarkAllErrorsAsRead()` marks warnings as read (method name is legacy)
5. Icon disappears, text returns to black
6. Icon reappears only when new warning occurs

**Scenario 3: Mixed Errors and Warnings**
1. Multiple backups run with different severities
2. Activity tab shows ERROR icon (highest severity wins)
3. User views Activity tab
4. Both errors AND warnings are marked as read
5. Icon disappears
6. Next issue triggers appropriate icon

### 5. Technical Implementation Details

**SharpVectors.WPF Integration:**
```csharp
using SharpVectors.Converters;

var iconViewer = new SvgViewbox
{
    Width = 16,
    Height = 16,
    Source = new Uri(iconPath, UriKind.Absolute)
};
```

**File Path Construction:**
```csharp
string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
string iconPath = Path.Combine(baseDir, "Images", iconFileName);
```

**Error Handling:**
- Try-catch around SVG loading
- File existence check before loading
- Fallback to emoji if SVG fails: `⚠️`
- Debug logging for troubleshooting

### 6. Benefits

✅ **Professional Appearance**: Vector graphics scale perfectly at any DPI
✅ **Clear Visual Hierarchy**: Different icons for errors vs warnings
✅ **Consistent Design**: SVG icons match application theme
✅ **Accessibility**: Larger, clearer icons than emoji
✅ **Maintainable**: Easy to update icons by replacing SVG files
✅ **Graceful Degradation**: Falls back to emoji if SVG unavailable
✅ **Performance**: SVG loaded once per update, cached by WPF

### 7. Color Coding

| Severity | Text Color | Icon | Meaning |
|----------|-----------|------|---------|
| None | Black (#000000) | (none) | No unread issues |
| Warning | Orange (#FF8C00) | warning_icon.svg | Warnings require attention |
| Error | Dark Red (#8B0000) | error_icon.svg | Errors require immediate action |

### 8. Periodic Update Behavior

**Timer Integration:**
```csharp
var timer = new DispatcherTimer();
timer.Interval = TimeSpan.FromSeconds(30);
timer.Tick += (s, e) => UpdateActivityTabWarning();
```

- Checks for new errors/warnings every 30 seconds
- Updates icon automatically without user action
- Icon appears while application is running
- Persists across application restarts (unread status saved in logs)

### 9. Future Enhancements (Optional)

1. **Animated Icons**: Pulse or glow effect for urgent errors
2. **Count Display**: Show number of unread errors/warnings
3. **Tooltip**: Hover over icon to see summary (e.g., "3 errors, 5 warnings")
4. **Custom Icons**: Allow users to provide custom SVG files
5. **Sound Notification**: Audio alert when error icon appears

## Testing Scenarios

### Test 1: Error Icon Display
1. Run backup that generates error
2. Verify Activity tab shows error icon with red text
3. Click Activity tab
4. Verify icon disappears

### Test 2: Warning Icon Display
1. Run backup that generates warning (no errors)
2. Verify Activity tab shows warning icon with orange text
3. Click Activity tab
4. Verify icon disappears

### Test 3: Priority - Error Over Warning
1. Run backup with 1 error and 5 warnings
2. Verify Activity tab shows ERROR icon (not warning)
3. Text should be dark red

### Test 4: SVG File Missing
1. Rename/delete SVG files temporarily
2. Run backup with error
3. Verify fallback to emoji: "Activity ⚠️"
4. Check debug output for "SVG icon not found" message

### Test 5: Icon Persistence
1. Generate error without viewing Activity tab
2. Close and reopen application
3. Verify error icon still appears (unread status persisted)

## Version
This feature is included in version **5.13.6.36**

## Files Modified
- `BackupUI\Services\BackupLogger.cs` - Added separate error/warning detection methods
- `BackupUI\MainWindow.xaml.cs` - Complete redesign of UpdateActivityTabWarning() with SVG support

## Dependencies
- **SharpVectors.WPF** NuGet package (already installed)
- **SVG Files**: `Images/error_icon.svg`, `Images/warning_icon.svg`

## Build Status
✅ Build successful - all changes compile without errors
