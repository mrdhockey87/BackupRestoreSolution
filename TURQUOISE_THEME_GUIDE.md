# TURQUOISE THEME IMPLEMENTATION GUIDE

## Overview
Version 5.13.6.14 introduces a comprehensive turquoise (blue-green) color scheme throughout the Backup & Restore application. All colors are explicitly defined to prevent Windows dark mode from causing unpredictable color overrides.

## Color Palette

### Primary Turquoise Colors
- **PrimaryTurquoise**: `#20B2AA` (Light Sea Green) - Used for accent text
- **DarkTurquoise**: `#00CED1` (Dark Turquoise) - Alternative primary color
- **MediumTurquoise**: `#48D1CC` (Medium Turquoise) - Secondary buttons, selected tabs
- **LightTurquoise**: `#AFEEEE` (Pale Turquoise) - Hover states, status bar
- **VeryLightTurquoise**: `#E0F7F7` - Very light backgrounds for panels

### Button Colors
- **ButtonBackground**: `#008B8B` (Dark Cyan) - Default button background
- **ButtonForeground**: `#000000` (Black) - Button text
- **ButtonHover**: `#20B2AA` (Light Sea Green) - Hover state
- **ButtonPressed**: `#5F9EA0` (Cadet Blue) - Pressed state

### Text Colors
- **PrimaryText**: `#000000` (Black) - Main text
- **SecondaryText**: `#333333` (Dark Gray) - Secondary/disabled text
- **ErrorText**: `#8B0000` (Dark Red) - Error messages
- **WarningText**: `#FF8C00` (Dark Orange) - Warnings
- **SuccessText**: `#006400` (Dark Green) - Success messages
- **InfoText**: `#000080` (Navy Blue) - Info messages

### Background Colors
- **WindowBackground**: `#F5FFFF` (Very light turquoise tint)
- **PanelBackground**: `#E0F7F7` (Light turquoise) - Headers, panels
- **AlternateRowBackground**: `#F0FAFA` (Very light turquoise) - DataGrid alternating rows
- **HeaderBackground**: `#B0E0E6` (Powder Blue) - Column headers
- **StatusBarBackground**: `#AFEEEE` (Pale Turquoise)

### Status-Specific Colors
- **SuccessBackground**: `#E6F4EA` (Very light green tint)
- **WarningBackground**: `#FFF8DC` (Cornsilk)
- **ErrorBackground**: `#FFE4E1` (Misty Rose)
- **InfoBackground**: `#E0F7F7` (Light Turquoise)

### Border Colors
- **BorderBrush**: `#5F9EA0` (Cadet Blue) - Standard borders
- **LightBorderBrush**: `#B0E0E6` (Powder Blue) - Lighter borders

### Selection Colors
- **SelectionBackground**: `#008B8B` (Dark Cyan) - Selected items
- **SelectionForeground**: `#FFFFFF` (White) - Text on selected items

## Predefined Styles

### Button Styles
1. **TurquoiseButton** (Default) - Dark turquoise background, black text, rounded corners
2. **DeleteButton** - Dark red (#8B0000) with white text for delete/remove actions
3. **SecondaryButton** - Medium turquoise for less prominent actions
4. **SuccessButton** - Light sea green for confirm/success actions
5. **WarningButton** - Dark orange with white text for cautionary actions

### Control Styles
All standard WPF controls have default styles defined:
- Window, TextBlock, Button, DataGrid, Menu, StatusBar, TextBox, ComboBox
- CheckBox, RadioButton, Label, TabControl, ScrollViewer, Border
- ListBox, TreeView, Expander, GroupBox, ProgressBar, ToolTip, ContextMenu

## Usage

### In XAML
Theme is automatically applied via App.xaml resource dictionary merge:

```xaml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Themes/TurquoiseTheme.xaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

### Using Theme Colors
Reference colors using StaticResource:

```xaml
<!-- Background -->
<StackPanel Background="{StaticResource PanelBackground}">

<!-- Text Color -->
<TextBlock Foreground="{StaticResource PrimaryText}"/>
<TextBlock Foreground="{StaticResource ErrorText}"/>

<!-- Border -->
<Border BorderBrush="{StaticResource LightBorderBrush}"/>
```

### Using Button Styles
```xaml
<!-- Default turquoise button (applied automatically) -->
<Button Content="OK"/>

<!-- Delete button (dark red) -->
<Button Content="Delete" Style="{StaticResource DeleteButton}"/>

<!-- Secondary button (medium turquoise) -->
<Button Content="Cancel" Style="{StaticResource SecondaryButton}"/>

<!-- Success button -->
<Button Content="Save" Style="{StaticResource SuccessButton}"/>
```

## Files Updated

### Core Theme Files
- **BackupUI\Themes\TurquoiseTheme.xaml** - NEW: Complete theme resource dictionary
- **BackupUI\App.xaml** - Updated to merge theme

### Windows Updated
- BackupUI\MainWindow.xaml
- BackupUI\Windows\ActivityDetailWindow.xaml
- BackupUI\Windows\ActivityManagementWindow.xaml
- BackupUI\Windows\AboutWindow.xaml
- BackupUI\Windows\BackupWindowNew.xaml
- BackupUI\Windows\DiskSelectionWindow.xaml
- BackupUI\Windows\ExportOptionsDialog.xaml
- BackupUI\Windows\RestoreWindow.xaml
- BackupUI\Windows\RestoreWindowNew.xaml
- BackupUI\Windows\ServiceManagementWindow.xaml
- BackupUI\Windows\VolumeConfigurationWindow.xaml
- BackupUI\Controls\VolumeResizeControl.xaml

## Benefits

1. **Consistent Branding** - Professional turquoise color scheme throughout
2. **Dark Mode Protection** - Explicit colors prevent Windows dark mode override
3. **Accessibility** - Black text on light backgrounds ensures readability
4. **Maintainability** - Change theme colors in ONE file (TurquoiseTheme.xaml)
5. **Visual Hierarchy** - Different button styles for different action types
6. **Professional Appearance** - Cohesive, polished UI with proper color semantics

## Color Philosophy

- **Turquoise/Cyan** - Primary brand color, buttons, accents
- **Black** - All standard text for maximum readability
- **Dark Red** - Error text and delete buttons (better than bright red)
- **Dark Green** - Success indicators
- **Dark Orange** - Warnings
- **White** - Text on dark backgrounds (selected items)
- **Light Turquoise** - Backgrounds, panels, subtle accents

## Testing Checklist

? Build successful
? All theme resources resolve correctly
? No dark mode color conflicts
? Button styles apply correctly
? DataGrid headers and rows use theme colors
? Error/Warning/Success messages use dark red/orange/green
? All windows use consistent color scheme

## Version Updates
- VersionClass.cs: version_fallback_number = "5.13.6.14"
- Directory.Build.props: ProductVersion = 5.13.6.14
- Comprehensive version notes added documenting theme implementation

---
**Implementation Complete**: 2/20/2026
**Verified**: Build successful, theme applied across all windows
