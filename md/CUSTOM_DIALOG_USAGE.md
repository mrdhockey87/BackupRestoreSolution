# Custom Themed Dialog System

## Overview
The custom dialog system provides turquoise-themed dialogs that match your application's visual style. All dialogs feature:
- Consistent turquoise color scheme (#F5FFFF background, #B0E0E6 headers, #20B2AA borders, #008B8B buttons)
- Borderless, modern design
- Emoji icons for visual feedback (ℹ️⚠️❌❓✅)
- Automatic owner window detection for proper modal behavior
- MessageBox fallback for error resilience

## Files
- **CustomDialog.xaml** - Themed dialog window UI
- **CustomDialog.xaml.cs** - Dialog configuration and logic
- **CustomDialogService.cs** - Static service with helper methods

## Usage Examples

### Information Messages
```csharp
// Simple information message
CustomDialogService.ShowInfo("Operation completed successfully.", "Information");

// No mounted backups (replaced MessageBox.Show with MessageBoxImage.Information)
CustomDialogService.ShowInfo("No mounted backups to unmount.", "No Mounted Backups");
```

### Success Messages
```csharp
// Success notification
CustomDialogService.ShowSuccess("All backups unmounted successfully.", "Success");

// Backup unmounted successfully
CustomDialogService.ShowSuccess($"Backup unmounted successfully from {mountPath}", "Success");
```

### Warning Messages
```csharp
// Warning notification
CustomDialogService.ShowWarning("This operation cannot be undone.", "Warning");
```

### Error Messages
```csharp
// Simple error
CustomDialogService.ShowError("Failed to unmount:\n{error}", "Unmount Error");

// Error with exception details
CustomDialogService.ShowError($"Error unmounting backup:\n{ex.Message}", "Error");

// Import error
CustomDialogService.ShowError($"Failed to import backup:\n{ex.Message}", "Import Error");
```

### Yes/No Questions
```csharp
// Simple Yes/No question
var result = CustomDialogService.ShowQuestion("Are you sure you want to continue?", "Confirm");
if (result == CustomDialogResult.Yes)
{
    // User clicked Yes
}

// Unmount confirmation
var result = CustomDialogService.ShowQuestion(
    $"Unmount backup from {mountPath}?", 
    "Unmount Backup");
if (result == CustomDialogResult.Yes)
{
    // Proceed with unmount
}

// Unmount all confirmation
var result = CustomDialogService.ShowQuestion(
    $"Unmount all {mounted.Count} mounted backup(s)?", 
    "Unmount All");
if (result == CustomDialogResult.Yes)
{
    // Unmount all
}
```

### Yes/No/Cancel Questions
```csharp
// Three-option confirmation
var result = CustomDialogService.ShowConfirmation(
    "Do you want to save your changes before closing?", 
    "Save Changes");

switch (result)
{
    case CustomDialogResult.Yes:
        // Save and close
        break;
    case CustomDialogResult.No:
        // Close without saving
        break;
    case CustomDialogResult.Cancel:
        // Don't close
        break;
}
```

### OK/Cancel Questions
```csharp
// OK/Cancel with custom icon
var result = CustomDialogService.ShowOKCancel(
    "This will delete all temporary files. Continue?", 
    "Confirm", 
    DialogIcon.Warning);

if (result == CustomDialogResult.OK)
{
    // Proceed
}
```

### Advanced Usage with Owner Window
```csharp
// Specify owner window explicitly for modal behavior
var result = CustomDialogService.Show(
    this,  // Owner window
    "Custom message", 
    "Custom Title", 
    DialogButtons.YesNoCancel, 
    DialogIcon.Question);
```

## Migration from MessageBox

### Before (MessageBox)
```csharp
MessageBox.Show("Message", "Title", MessageBoxButton.OK, MessageBoxImage.Information);
MessageBox.Show("Error message", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
var result = MessageBox.Show("Question?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
if (result == MessageBoxResult.Yes) { /* ... */ }
```

### After (CustomDialogService)
```csharp
CustomDialogService.ShowInfo("Message", "Title");
CustomDialogService.ShowError("Error message", "Error");
var result = CustomDialogService.ShowQuestion("Question?", "Confirm");
if (result == CustomDialogResult.Yes) { /* ... */ }
```

## Dialog Types

### Buttons
- **DialogButtons.OK** - Single OK button
- **DialogButtons.OKCancel** - OK and Cancel buttons
- **DialogButtons.YesNo** - Yes and No buttons
- **DialogButtons.YesNoCancel** - Yes, No, and Cancel buttons

### Icons
- **DialogIcon.None** - No icon
- **DialogIcon.Information** - ℹ️ (blue)
- **DialogIcon.Warning** - ⚠️ (orange)
- **DialogIcon.Error** - ❌ (red)
- **DialogIcon.Question** - ❓ (turquoise)
- **DialogIcon.Success** - ✅ (green)

### Results
- **CustomDialogResult.None** - No action taken
- **CustomDialogResult.OK** - OK button clicked
- **CustomDialogResult.Cancel** - Cancel button or close button clicked
- **CustomDialogResult.Yes** - Yes button clicked
- **CustomDialogResult.No** - No button clicked

## Already Converted
The following MessageBox calls have been replaced with CustomDialogService:

### MainWindow.xaml.cs
- ✅ Line 1268-1272: Unmount backup confirmation (ShowQuestion)
- ✅ Line 1302-1305: Backup unmounted success (ShowSuccess)
- ✅ Line 1309-1312: Failed to unmount error (ShowError)
- ✅ Line 1318-1321: Error unmounting exception (ShowError)
- ✅ Line 1333-1336: No mounted backups info (ShowInfo)
- ✅ Line 1340-1344: Unmount all confirmation (ShowQuestion)
- ✅ Line 1350-1353: All backups unmounted success (ShowSuccess)

### ImportBackupWindow.xaml.cs
- ✅ Line 190-194: Import error (ShowError)

## Theme Colors
The custom dialogs use the following turquoise color palette:
- **Window Background**: #F5FFFF (Alice Blue)
- **Header Background**: #B0E0E6 (Powder Blue)
- **Border**: #20B2AA (Light Sea Green)
- **Button Background**: #008B8B (Dark Cyan)
- **Button Hover**: #20B2AA (Light Sea Green)
- **Button Pressed**: #008080 (Teal)

## Notes
- The custom dialog system automatically detects the main window as owner for proper modal behavior
- If the custom dialog fails for any reason, it falls back to standard MessageBox
- All dialogs are centered on their owner window
- The borderless design with rounded corners provides a modern appearance
- Long messages are automatically scrollable
