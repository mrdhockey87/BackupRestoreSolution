# Splash Screen - Secure Server Backup

## Version 5.13.11.4 - Professional Splash Screen with Adaptive Logo Sizing

### Overview
The application now displays a professional splash screen on startup featuring:
- **Title:** "Secure Server Backup"
- **Adaptive Logo:** Automatically selects appropriate size based on screen DPI
- **Loading Status:** Real-time status messages during initialization
- **Smooth Animations:** Fade-in/fade-out transitions
- **Turquoise Theme:** Matches application branding

---

## Logo Requirements

The splash screen uses **3 logo files** of different sizes to provide optimal display quality across all screen types:

### Logo Files (in `Assets` folder)

1. **logo_small.png**
   - **Size:** 128x128 pixels (recommended)
   - **Used for:** Standard displays (100-149% DPI scaling)
   - **Target:** 1080p monitors, standard laptops

2. **logo_medium.png**
   - **Size:** 192x192 pixels (recommended)
   - **Used for:** High DPI displays (150-199% scaling)
   - **Target:** 1440p monitors, high-DPI laptops, Surface devices

3. **logo_large.png**
   - **Size:** 256x256 pixels (recommended)
   - **Used for:** 4K displays (200%+ scaling)
   - **Target:** 4K/UHD monitors, retina displays, 8K displays

---

## File Structure

```
BackupRestoreSolution/
├── BackupUI/
│   ├── Assets/
│   │   ├── logo_small.png   ← 128x128px for standard displays
│   │   ├── logo_medium.png  ← 192x192px for high DPI displays
│   │   └── logo_large.png   ← 256x256px for 4K displays
│   ├── Windows/
│   │   ├── SplashScreen.xaml
│   │   └── SplashScreen.xaml.cs
│   ├── App.xaml
│   └── App.xaml.cs
```

---

## How Logo Selection Works

The splash screen automatically detects screen DPI and selects the appropriate logo using **pack:// URIs** for embedded resources:

```csharp
var dpiScale = VisualTreeHelper.GetDpi(this);
double scaleFactor = Math.Max(dpiScale.DpiScaleX, dpiScale.DpiScaleY);

Uri logoUri;
if (scaleFactor >= 2.0)
    logoUri = new Uri("pack://application:,,,/Assets/logo_large.png", UriKind.Absolute);
else if (scaleFactor >= 1.5)
    logoUri = new Uri("pack://application:,,,/Assets/logo_medium.png", UriKind.Absolute);
else
    logoUri = new Uri("pack://application:,,,/Assets/logo_small.png", UriKind.Absolute);

var bitmap = new BitmapImage();
bitmap.BeginInit();
bitmap.UriSource = logoUri;
bitmap.EndInit();
```

### pack:// URI Format
- `pack://application:,,,/` - Current application assembly
- `/Assets/logo_small.png` - Resource path in project

### Why pack:// URIs?
- **Embedded Resources:** Files are compiled into the assembly (not separate files)
- **No File System Access:** Works even if output folder is missing
- **Standard WPF:** This is the correct way to access embedded images in WPF
- **No File.Exists() Needed:** If resource doesn't exist, loading throws exception (caught gracefully)

### Fallback System
If the preferred logo size is not found, the system automatically tries other sizes in this order:
1. Medium logo
2. Large logo
3. Small logo

If no logos are found, the splash screen displays **without the logo** (text only).

---

## Logo Design Guidelines

### Format
- **File Format:** PNG (with transparency support)
- **Color Mode:** RGB or RGBA
- **Transparency:** Recommended for clean appearance

### Design Recommendations
- **Shape:** Square or nearly square (1:1 aspect ratio)
- **Content:** Company logo, application icon, or branding element
- **Colors:** Should match turquoise theme (#20B2AA, #00CED1, #48D1CC)
- **Clarity:** High contrast, clear at small sizes
- **Background:** Transparent or matching window background (#F5FFFF)

### Size Requirements
| Display Type | DPI Scaling | Logo Size | Recommended Pixels |
|--------------|-------------|-----------|-------------------|
| Standard (1080p) | 100% | Small | 128x128 |
| High DPI (1440p) | 125-150% | Medium | 192x192 |
| 4K (2160p) | 200%+ | Large | 256x256 |

---

## Startup Sequence

The splash screen shows during application initialization:

1. **Show Splash** (immediate)
   - Display: "Secure Server Backup"
   - Status: "Loading..."

2. **Check Components** (500ms)
   - Status: "Checking components..."

3. **Verify BackupEngine.dll** (300ms)
   - Status: "Verifying BackupEngine.dll..."

4. **Initialize Services** (500ms)
   - Status: "Initializing services..."

5. **Load Main Window** (300ms)
   - Status: "Loading main window..."

6. **Ready** (200ms)
   - Status: "Ready!"

7. **Fade Out** (300ms)
   - Smooth opacity animation (1.0 → 0.0)

8. **Show Main Window**

**Total Display Time:** ~2.1 seconds

---

## Adding Logo Files

### Option 1: Copy Files Manually
1. Create `Assets` folder in `BackupUI` project directory
2. Copy your 3 PNG logo files to `Assets` folder
3. Right-click each file in Visual Studio Solution Explorer
4. Select **Properties**
5. Set **Build Action** to **Resource** (NOT Content!)
6. Copy to Output Directory should be **Do not copy** (resources are embedded)

### Option 2: Add via Visual Studio
1. Right-click `BackupUI` project in Solution Explorer
2. Select **Add → New Folder** → Name it `Assets`
3. Right-click `Assets` folder
4. Select **Add → Existing Item...**
5. Navigate to your logo files
6. Select all 3 PNG files and click **Add**
7. For each file:
   - Right-click → **Properties**
   - **Build Action:** Resource
   - **Copy to Output Directory:** Do not copy

### Option 3: Edit .csproj Directly
Add this to `BackupUI.csproj`:
```xml
<ItemGroup>
  <Resource Include="Assets\logo_small.png" />
  <Resource Include="Assets\logo_medium.png" />
  <Resource Include="Assets\logo_large.png" />
</ItemGroup>
```

**IMPORTANT:** Use `<Resource>` NOT `<Content>`! Resources are embedded in the assembly and accessed via pack:// URIs.

---

## Features

### ✅ Adaptive Logo Sizing
- Automatically detects screen DPI
- Selects appropriate logo size
- Prevents blurry/pixelated logos
- Looks sharp on all displays

### ✅ Professional Design
- Frameless window with rounded corners
- Turquoise theme integration
- Indeterminate progress bar
- Smooth fade-out animation

### ✅ Real-Time Status
- Shows initialization steps
- Updates status messages
- Provides user feedback
- Prevents "frozen" appearance

### ✅ Robust Error Handling
- Graceful logo fallback
- Continues without logo if files missing
- Debug logging for troubleshooting
- Never crashes on startup

### ✅ Performance Optimized
- Async initialization
- Non-blocking UI thread
- Minimal display time (~2 seconds)
- Smooth transitions

---

## Customization

### Change Display Duration
Edit `App.xaml.cs` and adjust `Task.Delay()` values:
```csharp
await Task.Delay(500); // Increase for longer display
```

### Disable Splash Screen
Comment out the splash screen call in `App.xaml.cs`:
```csharp
// await ShowSplashScreenAndInitialize(e);
var mainWindow = new MainWindow();
mainWindow.Show();
```

### Change Logo Selection Logic
Edit `SplashScreen.xaml.cs` `LoadLogo()` method to customize DPI thresholds:
```csharp
if (scaleFactor >= 2.5)  // Adjust threshold
    logoFileName = "logo_large.png";
```

---

## Testing

### Test Different DPI Settings
1. Right-click desktop → **Display settings**
2. Change **Scale** setting (100%, 125%, 150%, 175%, 200%)
3. Run application
4. Verify correct logo loads (check debug output)

### Debug Logging
Check Visual Studio **Output** window for messages like:
```
Loaded logo: C:\...\Assets\logo_medium.png (scale factor: 1.50)
```

### Test Missing Logos
1. Temporarily rename logo files
2. Run application
3. Verify splash displays without logo (no crash)
4. Check debug output for fallback messages

---

## Production Deployment

### Build Configuration
Ensure logos are included in build output:
1. Build solution
2. Check `artifacts\bin\Release\Assets\` folder
3. Verify all 3 PNG files are present
4. If missing, check **Build Action** and **Copy to Output**

### Installation Package
Include Assets folder in installer:
- **Files:** All 3 PNG logo files
- **Location:** Same directory as BackupUI.exe
- **Subfolder:** Assets\

---

## Troubleshooting

### Logo Not Displaying
1. **Check Build Action:**
   - **MUST be:** Resource (NOT Content!)
   - Right-click PNG file → Properties → Build Action = Resource
   - Copy to Output Directory = Do not copy

2. **Check Debug Output:**
   - Look for "Logo resource not found" messages
   - Verify pack:// URI is correct
   - Check for exception messages

3. **Verify File Names:**
   - Must be exactly: `logo_small.png`, `logo_medium.png`, `logo_large.png`
   - Case-sensitive on some systems
   - No spaces or special characters

4. **Verify Location:**
   - Files must be in `Assets` folder in project
   - Check Solution Explorer shows Assets folder with PNG files
   - Files must be in BackupUI project, not solution folder

### Splash Screen Not Showing
1. **Check App.xaml:**
   - Verify `StartupUri` is removed
   - Should create window in code

2. **Check App.xaml.cs:**
   - Verify `OnStartup` is `async void`
   - Verify `ShowSplashScreenAndInitialize()` is called
   - Check for exceptions in Output window

### Slow Startup
1. **Reduce Delays:**
   - Lower `Task.Delay()` values in `ShowSplashScreenAndInitialize()`
   - Minimum recommended: 100ms per step

2. **Remove Unnecessary Steps:**
   - Comment out initialization steps you don't need
   - Keep only essential checks

---

## Version History

### 5.13.11.4 (March 10, 2026)
- ✅ Initial splash screen implementation
- ✅ Adaptive logo sizing (3 sizes)
- ✅ DPI-aware logo selection
- ✅ Status message updates
- ✅ Fade-out animation
- ✅ Async initialization
- ✅ Graceful error handling

---

## Support

For issues or questions:
1. Check debug output in Visual Studio
2. Verify logo files are in Assets folder
3. Confirm Build Action settings
4. Review this README

**Enterprise-grade professional startup experience!** 🚀
