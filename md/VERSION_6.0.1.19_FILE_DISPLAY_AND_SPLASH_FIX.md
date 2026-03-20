# Version 6.0.1.19 - File Name Display in Progress Windows & Splash Screen Position Fix

## Release Date
[Current Date]

## Summary
Fixed two critical UX issues:
1. **File Names Now Display in Progress Windows**: BackupProgressWindow and MountProgressWindow now show real-time file/folder names during backup and mount operations
2. **Splash Screen Position Fixed**: Splash screen now appears at the saved main window location instead of wandering to different positions

---

## Issue #1: File Names Not Displaying in Progress Windows

### Problem
User reported: "I asked you to show the files & folder as the backup progressed on BackupProgressWindow and the same for the MountProgressWindow, but they never show on the UI as the backup or mount are running."

### Root Cause Analysis
1. **C++ callbacks WERE sending file names** like "Backing up: MyDocument.pdf" and "Processing: Video.mp4"
2. **Both progress windows had only ONE TextBlock** for ALL messages
3. File names fought with general progress messages ("Backing up volume 1 of 2...", "Mounting image...")
4. File names got overwritten immediately by generic messages
5. BackupProgressWindow's 1-second polling interval meant it usually caught only generic messages

### Solution - Dual TextBlock Architecture

#### Changes Made:

**1. BackupProgressWindow.xaml**
- Added `txtCurrentFile` TextBlock (Grid.Row=3, Gray color, smaller font)
- Positioned between percentage label and buttons
- Displays individual files like "Backing up: Report2024.xlsx"

**2. MountProgressWindow.xaml**
- Added `txtCurrentFile` TextBlock (Grid.Row=3, VerticalAlignment=Bottom, Gray color)
- Positioned below main status
- Displays "Processing: SystemFile.dll" messages

**3. BackupCommon/BackupProgress.cs** (NEW FILE - Shared DTO)
```csharp
public class BackupProgress
{
    public Guid JobId { get; set; }
    public bool IsRunning { get; set; }
    public int Percentage { get; set; }
    public string Message { get; set; } = "";
    public string CurrentFile { get; set; } = "";  // NEW: Current file being backed up
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
```

**4. BackupService/BackupProgressTracker.cs**
- Added `using BackupCommon;` to reference shared DTO
- Enhanced `BackupJobState` class:
  ```csharp
  public string CurrentFile { get; set; } = "";  // NEW: Track current file
  ```
- Enhanced `UpdateProgress` method with smart message parsing:
  ```csharp
  public void UpdateProgress(Guid jobId, int percentage, string message)
  {
      if (_runningJobs.TryGetValue(jobId, out var state))
      {
          state.Percentage = percentage;
          state.LastUpdate = DateTime.Now;

          // Parse message to distinguish file-level vs general progress
          if (message.Contains("Backing up:") || message.Contains("Processing:"))
          {
              // File-level message - store in CurrentFile
              state.CurrentFile = message;
          }
          else
          {
              // General progress message - store in Message
              state.Message = message;
              // Clear current file when general message arrives (new phase)
              if (!message.Contains("Capturing files") && !message.Contains("Mounting"))
              {
                  state.CurrentFile = "";
              }
          }
      }
  }
  ```
- Updated `GetProgress` to include CurrentFile in returned DTO

**5. BackupUI/Windows/BackupProgressWindow.xaml.cs**
```csharp
if (progress != null)
{
    progressBar.Value = progress.Percentage;
    txtProgress.Text = progress.Message;
    txtPercentage.Text = $"{progress.Percentage}%";

    // Display current file if available (v6.0.1.19)
    if (!string.IsNullOrEmpty(progress.CurrentFile))
    {
        txtCurrentFile.Text = progress.CurrentFile;
    }
    else
    {
        txtCurrentFile.Text = "";
    }
```

**6. BackupUI/Windows/MountProgressWindow.xaml.cs**
```csharp
public void SetStatus(string status, int percentage = -1)
{
    if (!_isClosed && txtStatus != null)
    {
        Dispatcher.Invoke(() =>
        {
            // Parse message to distinguish file-level vs general progress (v6.0.1.19)
            if (status.Contains("Processing:") || status.Contains("Backing up:"))
            {
                // File-level message - display in txtCurrentFile
                if (txtCurrentFile != null)
                {
                    txtCurrentFile.Text = status;
                }
            }
            else
            {
                // General progress message - display in txtStatus
                txtStatus.Text = status;
                
                // Clear current file when phase changes
                if (txtCurrentFile != null && 
                    !status.Contains("Capturing") && 
                    !status.Contains("Mounting") &&
                    !status.Contains("Processing"))
                {
                    txtCurrentFile.Text = "";
                }
            }

            if (percentage >= 0)
            {
                SetProgress(percentage);
            }
        });
    }
}
```

**7. BackupUI/Services/BackupServiceClient.cs**
- Added `using BackupCommon;` to use shared BackupProgress DTO
- Removed duplicate BackupProgress class definition (now in BackupCommon)

**8. BackupService/BackupServiceCommunication.cs**
- Added `using BackupCommon;` to use shared BackupProgress DTO
- Removed duplicate BackupProgress class definition (now in BackupCommon)

### Technical Details

#### Message Flow
1. C++ callback sends "Backing up: file.txt"
2. BackupExecutor.cs nativeCallback receives message
3. BackupProgressTracker.UpdateProgress parses message
4. Sets `state.CurrentFile = "Backing up: file.txt"`
5. BackupProgress DTO includes CurrentFile property
6. Named pipe transfers to UI
7. BackupProgressWindow polls, sees both Message AND CurrentFile
8. `txtProgress` shows "Capturing files...", `txtCurrentFile` shows "Backing up: file.txt"

#### Mount Window Direct Updates
- MountProgressWindow.SetStatus called directly by NativeBackupMountManager callback
- SetStatus parses message and routes to appropriate TextBlock
- Both TextBlocks update in real-time

#### Phase Change Handling
- When backup transitions from "Capturing files" to "Finalizing archive", CurrentFile is cleared
- When mount transitions from "Mounting image" to "Mount completed", txtCurrentFile cleared

### User Experience Improvement
**Before**: Progress window shows only "Backing up volume 1 of 2..." with percentage. User can't tell which files are being processed, feels unresponsive and slow.

**After**: Progress window shows BOTH:
- "Backing up volume 1 of 2..." (main status)
- "Backing up: ProjectPlans\Design_v3.docx" (current file)

User sees real-time file progress, similar to commercial backup tools (Veeam, Acronis, Macrium).

---

## Issue #2: Splash Screen Position Wandering

### Problem
User reported: "The splash screen still wonders starting up at location other than it should based on the mouse position when it starts to open. I asked you to make sure that the splash screen only appear at the location that the main window was the last time it closed."

### Root Cause
In **SplashScreen.xaml line 10**, `WindowStartupLocation="CenterScreen"` was set. This XAML property overrides any position set programmatically in the code-behind. Even though SplashScreen.xaml.cs (lines 48-49) was correctly setting `this.Left` and `this.Top` based on the saved main window position, WPF ignored these settings because `WindowStartupLocation="CenterScreen"` took precedence.

### Solution
Changed `WindowStartupLocation` from `"CenterScreen"` to `"Manual"` in SplashScreen.xaml line 10. This allows the code-behind's `LoadSavedPosition()` method to control the window position.

#### Changed File:
**BackupUI/Windows/SplashScreen.xaml** (line 10)
```xaml
<!-- BEFORE -->
WindowStartupLocation="CenterScreen"

<!-- AFTER -->
WindowStartupLocation="Manual"
```

### How It Works Now
1. SplashScreen constructor calls `LoadSavedPosition()` (line 16 of xaml.cs)
2. Reads `window-position.json` from AppData (lines 29-38)
3. Calculates center of saved main window position (lines 44-45)
4. Positions splash screen centered on that location (lines 48-49)
5. **With WindowStartupLocation="Manual"**, these Left/Top values are respected!
6. If no saved position exists or invalid, falls back to `CenterOnPrimaryScreen()` (lines 56-57)

---

## Files Modified

### Core Changes (File Name Display)
1. `BackupUI/Windows/BackupProgressWindow.xaml` - Added txtCurrentFile TextBlock
2. `BackupUI/Windows/BackupProgressWindow.xaml.cs` - Display CurrentFile in UI
3. `BackupUI/Windows/MountProgressWindow.xaml` - Added txtCurrentFile TextBlock
4. `BackupUI/Windows/MountProgressWindow.xaml.cs` - Enhanced SetStatus to parse and route messages
5. `BackupCommon/BackupProgress.cs` - **NEW FILE**: Shared DTO with CurrentFile property
6. `BackupService/BackupProgressTracker.cs` - Enhanced UpdateProgress with message parsing, added CurrentFile tracking
7. `BackupUI/Services/BackupServiceClient.cs` - Use shared BackupProgress from BackupCommon
8. `BackupService/BackupServiceCommunication.cs` - Use shared BackupProgress from BackupCommon

### Splash Screen Fix
9. `BackupUI/Windows/SplashScreen.xaml` - Changed WindowStartupLocation to Manual

### Version Updates
10. `BackupUI/VersionClass.cs` - Updated to 6.0.1.19 with comprehensive documentation
11. `Directory.Build.props` - Updated ProductVersion to 6.0.1.19

---

## Testing Instructions

### Test File Name Display
1. **Backup Test**:
   - Run a backup job with many files (e.g., disk backup)
   - Open BackupProgressWindow
   - **Expected**: See both "Backing up volume 1 of 2..." AND "Backing up: SpecificFile.txt" displayed simultaneously
   - Files should update in real-time (multiple times per second)

2. **Mount Test**:
   - Mount an SSB backup
   - Watch MountProgressWindow
   - **Expected**: See "Mounting image..." AND "Processing: SystemFile.dll" messages
   - Individual file names should appear during WIM image loading phase

### Test Splash Screen Position
1. **Setup**:
   - Move MainWindow to a specific monitor location (e.g., bottom-right corner of secondary monitor)
   - Close MainWindow (saves position to window-position.json)

2. **Test**:
   - Restart application
   - **Expected**: Splash screen appears centered on where MainWindow was last closed
   - **NOT Expected**: Splash screen appearing at mouse position, center of primary screen, or random location

3. **Multi-Monitor Test**:
   - Close MainWindow on Monitor 2
   - Restart application
   - **Expected**: Splash screen appears on Monitor 2 at saved MainWindow location
   - Move MainWindow to Monitor 1, close
   - Restart application
   - **Expected**: Splash screen now appears on Monitor 1 at new saved location

---

## Build Status
✅ Build successful with 0 errors, 0 warnings

---

## Why This Matters

### File Name Display
Enterprise users backing up 100,000+ files need to see progress to know:
1. Operation is working, not frozen
2. Which files are taking longest (large videos, databases)
3. If specific files are being skipped/problematic

This brings BackupRestoreSolution UX to professional-grade backup tool standards (Veeam, Acronis, Macrium Reflect).

### Splash Screen Position
Professional applications should remember where they were last used. Users expect consistency - if they position their backup window on a specific monitor (e.g., always on secondary monitor), the splash screen should appear there too, not jump around to different locations on each launch.

---

## Version History Context
- **6.0.1.17**: Process priority fix (only backup uses Efficiency mode, not mount/unmount)
- **6.0.1.18**: False backup failure fix (error -4 when backup succeeded with skipped files)
- **6.0.1.19**: File name display + splash screen position fixes (this release)
