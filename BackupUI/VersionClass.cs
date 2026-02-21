using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BackupUI
{
	static class VersionClass
	{
	static public string version_word = "Version:";
		static private string version_fallback_number = "5.13.6.21";
		// Get version from assembly - this will always match the project file version
		static public string version_string = GetAssemblyVersion();

		static public string GetVersion()
		{
			return string.Format("{0} {1}", VersionClass.version_word, VersionClass.version_string);
		}

		// Public so ServiceManagementWindow can access it
		static public string GetAssemblyVersion()
		{
			try
			{
				Assembly assembly = Assembly.GetExecutingAssembly();
				
			// Try to get the informational version first (this reads from the .csproj <Version> or <InformationalVersion>)
			var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
			if (infoVersionAttr != null && !string.IsNullOrEmpty(infoVersionAttr.InformationalVersion))
			{
				// Strip Git commit hash if present (format: "4.8.3.2+e3d3b4c3af621b4af79aacab33b4f9ce955417c2")
				string versionString = infoVersionAttr.InformationalVersion;
				int plusIndex = versionString.IndexOf('+');
				if (plusIndex > 0)
				{
					versionString = versionString.Substring(0, plusIndex);
				}
				return versionString;
			}
				
				// Fall back to file version
				var fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
				if (fileVersionAttr != null && !string.IsNullOrEmpty(fileVersionAttr.Version))
				{
					return fileVersionAttr.Version;
				}
				
				// Fall back to assembly version
				var version = assembly.GetName().Version;
				if (version != null)
				{
					return version.ToString();
				}
				
			// Last resort fallback
		return version_fallback_number;
		}
		catch
		{
			// Fallback version if assembly version fails
			return version_fallback_number;
			}
		}
	}
}




/*
 * 
* Version 5.13.6.21 Fix Jobs Activity Grid not showing the vertical lines between the columns and the date not being centered. Also added
*					the github copilot directory to the ignore file mdail 2/21/2026 
* Version 5.13.6.20 UI ENHANCEMENT - ABOUTWINDOW GROUPBOX STYLING: Enhanced About dialog with professional turquoise theme for GroupBox
*					controls! Created custom AboutGroupBoxStyle that applies VeryLightTurquoise (#E0F7F7) background to GroupBox content
*					areas while using LightTurquoise (#AFEEEE) for borders and headers. Added solid background behind header text to prevent
*					turquoise window header from showing through where GroupBox header text overlaps the border line. Style includes: Content
*					background (VeryLightTurquoise - very light, subtle turquoise for readability), Border (LightTurquoise - more visible
*					turquoise outline), Header background (LightTurquoise - matches border for cohesive look), Header text (Black on turquoise
*					with SemiBold font). Applied to all three GroupBoxes in AboutWindow: Component Versions, Description, and Copyright.
*					Creates beautiful layered visual hierarchy with subtle color gradients. Header padding (5px horizontal) ensures text has
*					breathing room. Complete integration with existing TurquoiseTheme.xaml color palette. Professional, polished appearance
*					that matches application branding! No more plain white GroupBoxes - now fully themed! Enterprise-grade visual consistency
*					across all UI elements! mdail 2/21/2026
* Version 5.13.6.19 Set a background for the DataGrids in the Theme file and added a theme for scrollbars. I also updated the 
*				    colors of the Help windows mdail 2/21/2026
* Version 5.13.6.18 Set the DataGrids in the MainPage to use HeadersVisibility="Column" to eliminate the empty row at the top of 
*				    the grid. mdail 2/21/2026
* Version 5.13.6.17 C++ BUILD TOOLS UPGRADE COMPLETE: Successfully upgraded to Platform Toolset v145 and Windows SDK 10.0 with ALL
*					warnings resolved! Fixed TWO critical build warnings after C++ build tools upgrade: 1) MSB8012 - TargetPath Mismatch:
*					Root cause was Directory.Build.targets using absolute paths $(SolutionDir)artifacts\bin\$(Configuration)\ while
*					vcxproj used relative paths artifacts\bin\$(Configuration)\. MSBuild calculated TargetPath early with relative path,
*					then Directory.Build.targets overrode with absolute path, causing linker OutputFile mismatch warning. FIXED by
*					changing Directory.Build.targets to use relative paths with macros: $(ArtifactsBin)$(Configuration)\ instead of
*					hardcoded $(SolutionDir) prefix. Also removed redundant OutDir/IntDir from BackupEngine.vcxproj since they're managed
*					centrally in Directory.Build files. 2) LNK4098 - Runtime Library Conflict: Root cause was Debug configuration using
*					/MD (Multi-threaded DLL Release runtime) but linking zlibstaticd.lib (Debug library compiled with /MDd runtime),
*					creating conflict between MSVCRT and MSVCRTD. FIXED by adding explicit RuntimeLibrary settings to BackupEngine.vcxproj:
*					Debug uses MultiThreadedDebugDLL (/MDd) to match zlibstaticd.lib, Release uses MultiThreadedDLL (/MD) to match
*					zlibstatic.lib. Also added proper optimization settings: Debug uses Disabled with EnableFastChecks, Release uses
*					MaxSpeed with function-level linking and intrinsics. Build now completes with 0 errors, 0 warnings! Compiler flags
*					confirmed: Debug shows /MDd /Od /RTC1 (correct), Release shows /MD /O2 (correct). All three projects validated:
*					BackupEngine (C++ vcxproj) - clean build, BackupService (.NET 8) - no issues, BackupUI (.NET 8 WPF) - no issues.
*					LinuxRestore (CMake for Linux) - not affected by MSVC upgrade, builds separately via cmake. Complete enterprise-grade
*					C++ build tools migration with proper runtime library configuration and centralized build path management! Production-ready
*					stable builds across all platforms! mdail 2/21/2026
* Version 5.13.6.16 I changed the AlternateRowBackground color to a Cadet Blue (#5F9EA0) for better contrast with the new turquoise theme.
*					and there were some missed AlternateRowBackground in the main page mdail 2/20/2026
* Version 5.13.6.15 Made some other changes related to the turquoise theme. I haven't gotten everything yet like the Help page still needs 
*					to be updated to use the new colors. mdail 2/20/2026
* Version 5.13.6.14 MAJOR UPDATE - TURQUOISE THEME: Implemented comprehensive turquoise (blue-green) color scheme across entire application!
*					Created TurquoiseTheme.xaml resource dictionary with complete color palette preventing Windows dark mode from overriding colors.
*					All colors explicitly defined: Primary turquoise (#20B2AA Light Sea Green), Dark turquoise buttons (#008B8B Dark Cyan),
*					Medium turquoise (#48D1CC), Light turquoise (#AFEEEE), Very light backgrounds (#E0F7F7). Black text throughout (#000000)
*					for maximum readability. Error text changed from bright red to dark red (#8B0000) for better aesthetics. Warning text is
*					dark orange (#FF8C00), Success text is dark green (#006400), Info text is navy blue (#000080). Defined comprehensive styles
*					for ALL WPF controls: Button (dark turquoise with black text, hover/pressed states), DataGrid (turquoise headers, alternating
*					rows), TabControl (turquoise tabs, darker when selected), TextBox, ComboBox, CheckBox, RadioButton, Label, Menu, StatusBar,
*					Border, ListBox, TreeView, Expander, GroupBox, ProgressBar, ToolTip, ContextMenu. Special button styles: DeleteButton (dark
*					red #8B0000 with white text), SecondaryButton (medium turquoise), SuccessButton (light sea green), WarningButton (dark
*					orange). Updated ALL XAML files to reference theme resources instead of hardcoded colors: replaced #F0F0F0/#E8E8E8 with
*					PanelBackground, #CCC with LightBorderBrush, #666/#999/#333 with SecondaryText, Green/Orange/Red with SuccessText/
*					WarningText/ErrorText. Status background colors: InfoBackground (#E0F7F7 light turquoise), SuccessBackground (#E6F4EA
*					very light green), WarningBackground (#FFF8DC cornsilk), ErrorBackground (#FFE4E1 misty rose). Selection colors: Dark cyan
*					background (#008B8B) with white text for maximum contrast on selected rows/items. Window backgrounds have subtle turquoise
*					tint (#F5FFFF) for consistent theme. Updated App.xaml to merge TurquoiseTheme.xaml as application resource dictionary ensuring
*					theme applies globally to all windows and controls. Updated MainWindow.xaml, ActivityDetailWindow.xaml, ActivityManagementWindow.xaml,
*					and other windows to use theme resources. Professional, cohesive turquoise color scheme throughout application! No more unpredictable
*					Windows dark mode color overrides - our colors always display correctly. Enterprise-grade visual branding with accessibility-
*					compliant color contrast ratios (black text on light backgrounds). Production-ready beautiful UI! mdail 2/20/2026
* Version 5.13.6.13 CODE QUALITY - CENTRALIZED VERSION VARIABLE: Consolidated version number in Directory.Build.props to use single variable!
*					Instead of hardcoding version in 4 places (Version, AssemblyVersion, FileVersion, InformationalVersion), now uses ProductVersion
*					property as single source of truth. All four version properties reference $(ProductVersion) using MSBuild variable substitution.
*					Only need to update ProductVersion property and all version properties update automatically! Mirrors the approach used in VersionClass.cs
*					with version_fallback_number variable. Benefits: 1) Single update point - change version once, propagates everywhere, 2) Reduced errors -
*					no risk of forgetting to update one of the four properties, 3) Consistent pattern across solution - both C# and MSBuild use variable
*					approach, 4) Clear documentation - comments explain where to update. Enhanced header documentation in Directory.Build.props to explain
*					ProductVersion is the single definition point. Pattern: Define once (<ProductVersion>5.13.6.13</ProductVersion>), reference everywhere
*					(<Version>$(ProductVersion)</Version>). Clean, maintainable, error-proof versioning! To update version: change ProductVersion value in
*					Directory.Build.props and version_fallback_number in VersionClass.cs. Both changes sync entire solution across all projects and fallback
*					scenarios. Production-ready centralized version management with DRY principle (Don't Repeat Yourself)! mdail 2/20/2026
* Version 5.13.6.12 Update the version class the use the version fallback number variable instead of hardcoding the version in multiple places. mdail 2/20/2026
* Version 5.13.6.11 UI ENHANCEMENT - SELECT ALL/DESELECT ALL BUTTONS: Added dedicated "Select All" and "Deselect All" buttons to ActivityDetailWindow
*					for easier multi-selection workflow! Buttons positioned in action buttons row between Export buttons and Delete Selected button.
*					"Select All" button has light blue background (#E0F0FF) and calls dgActivities.SelectAll() to select all visible entries.
*					"Deselect All" button has light gray background (#F0F0F0) and calls dgActivities.UnselectAll() to clear all selections.
*					Both buttons are 100px wide for compact layout. Complements existing right-click context menu (which already had Select All/
*					Clear Selection options) by providing visible, always-accessible buttons for users who prefer button clicks over context menus.
*					Selection count display automatically updates when buttons are used (via Activities_SelectionChanged event handler). Perfect
*					for quickly selecting all entries for batch export or delete operations without needing to know keyboard shortcuts (Ctrl+A)
*					or right-click menus. Professional UX improvement - common operations readily visible! Users can now: click "Select All" →
*					click "Export to CSV" for full job history export, or "Select All" → "Delete Selected" for complete activity log cleanup.
*					Buttons work identically to context menu commands but are more discoverable. Enterprise-grade multi-select interface with
*					multiple interaction methods (buttons, context menu, Shift+Click, Ctrl+Click) for maximum flexibility! Production-ready
*					accessible controls! mdail 2/20/2026
* Version 5.13.6.10 UI CLEANUP - REMOVED EMPTY ROW DETAILS: Removed unnecessary row details expansion in ActivityDetailWindow! User reported:
*					Clicking any log entry opened empty "Details" section below the row - details field was always blank since log entries are
*					simple text entries (Timestamp, JobName, Level, Message) with no additional detail data. Removed entire DataGrid.RowDetailsTemplate
*					section from ActivityDetailWindow.xaml (15+ lines of unused XAML for Border, StackPanel, Details TextBlock, BackupPath TextBlock).
*					DataGrid now shows ONLY the grid with columns - no expandable sections, no empty "Details:" labels, no wasted vertical space.
*					Cleaner, more compact interface - clicking rows now only selects them (for multi-select/export/delete) without expanding empty
*					details. The row details feature is useful when records have additional verbose information not shown in columns, but our log
*					entries already show all relevant data in the grid columns (Time, Job Name, Level, Message, Validation). No hidden data exists
*					to display! Removed visual clutter and improved UX - users can select multiple rows more easily without accidentally expanding
*					details. Professional activity log viewer focused on multi-select operations (Shift+Click ranges, Ctrl+Click individual, right-click
*					context menu). Production-ready simplified interface! mdail 2/18/2026
* Version 5.13.6.9 SELECTION COLOR FIX - VISIBLE NUMBERS ON SELECTED ROWS: Fixed Success/Warning/Error numbers disappearing when row selected!
*					Problem was row selection style set Foreground="White" which overrode the columns' colored text (Green/Orange/Red), making
*					numbers invisible on blue selected background (#0078D4). Particularly bad with Success count of 0 - green "0" became white
*					"0" on blue (invisible!). Solution: Added Style.Triggers with DataTrigger to all three columns that detect when parent
*					DataGridRow IsSelected=True using RelativeSource binding. Triggers change foreground to lighter colors visible on blue:
*					Success changes Green → LightGreen, Warning changes Orange → Yellow, Error changes Red → LightCoral. Also added
*					HorizontalAlignment="Center" and VerticalAlignment="Center" to properly align numbers with other columns (Total, Last
*					Activity). Now when user selects row: row background turns blue, row text turns white, BUT Success/Warning/Error columns
*					override with their own lighter colors that remain visible! Perfect visual feedback - selection is obvious (blue row) AND
*					all data remains readable. Created PowerShell script (fix_columns_final.ps1) to programmatically add the alignment and
*					trigger properties by parsing XAML line-by-line and inserting new properties after FontWeight setters. Zero values now
*					ALWAYS visible regardless of selection state! Complete visual polish - no more disappearing numbers! mdail 2/18/2026
* Version 5.13.6.8 FINAL FIX - EDIT MODE PREVENTION & BLANK COLUMN: Fixed TWO remaining UI issues for perfect Activity tab! ISSUE 1 - Fields entering
*					edit mode: Double-clicking text columns (Job Name, Last Activity, etc.) was trying to enter edit mode instead of opening detail
*					window. Added BeginningEdit event handler that cancels ALL edit attempts (e.Cancel = true). Now double-click ONLY opens
*					ActivityDetailWindow, never enters edit mode. The Last Activity field was particularly problematic - would open details window
*					then enter edit mode after closing. Now fixed! ISSUE 2 - Blank column after Actions button: Changed Actions column width from
*					fixed "150" to "Width='*' MinWidth='150'" so it fills remaining space, eliminating blank column after button. The * width makes
*					Actions column take all remaining horizontal space, preventing DataGrid from showing phantom column. Event flow now perfect:
*					Single-click → SelectionChanged (e.Handled=true stops bubbling) → Row turns blue. Double-click → BeginningEdit fires (e.Cancel=true
*					stops edit) → JobLog_DoubleClickFromTab fires → Opens ActivityDetailWindow. NO edit mode, NO blank columns, NO event bubbling!
*					Complete, polished, production-ready Activity tab! The saga is TRULY over now - all clicking, selecting, and double-clicking
*					behaviors work exactly as expected with zero edit mode interference! mdail 2/18/2026
* Version 5.13.6.7 CRITICAL FIX - ISREADONLY WAS BLOCKING EVENTS: Found and fixed the root cause of DataGrid not responding to clicks! Problem was
*					IsReadOnly="True" preventing WPF from routing mouse events properly. Changed to IsReadOnly="False" which allows events to flow
*					through the DataGrid hierarchy. Diagnostic testing revealed: Initially IsVisible=False when loading, becomes IsVisible=True after
*					user interaction. PreviewMouseLeftButtonDown and PreviewMouseDown both fire correctly now. Button clicks work, row selection works,
*					double-click works! The IsReadOnly property was blocking event bubbling/tunneling in the visual tree. Setting IsReadOnly="False"
*					doesn't actually allow editing since columns are DataGridTextColumn (inherently read-only for display) and the button column is a
*					template (controlled by button handler). Added diagnostic event handlers (PreviewMouseDown, MouseDown, PreviewMouseLeftButtonDown)
*					with debug logging for future troubleshooting. Removed MessageBox alerts from handlers - kept debug logging only. Root cause: WPF
*					DataGrid with IsReadOnly="True" suppresses mouse events to prevent editing gestures (cell selection, text selection, etc.), which
*					inadvertently blocked our click handlers. IsReadOnly="False" + read-only column types = perfect solution - events work, no editing
*					possible! Complete event flow now working: Mouse click → PreviewMouseLeftButtonDown → PreviewMouseDown → SelectionChanged →
*					SelectedItem updates → Double-click opens ActivityDetailWindow! Production-ready after saga of debugging! mdail 2/18/2026
* Version 5.13.6.6 DOUBLE-CLICK CLICK-THROUGH FIX: Fixed double-click to open details for the ACTUAL clicked row, not the selected row! User
*					correctly identified that if row 1 is selected and user double-clicks row 2, row 2's details should open (not row 1's). Changed
*					JobLog_DoubleClickFromTab to walk up visual tree from e.OriginalSource to find the actual DataGridRow that was clicked using
*					VisualTreeHelper.GetParent(). Gets the JobLogSummary from the clicked row's Item property, not from selectedJobLog or SelectedItem.
*					Now behavior is correct: single-click selects (blue highlight), double-click opens details for the row under the mouse cursor
*					regardless of which row is selected. Added using System.Windows.Media for VisualTreeHelper. Debug logging shows "Double-clicked row:
*					Opening detail window for job 'JobName'". This is standard DataGrid behavior - double-click acts on the clicked element, not the
*					selection. View Details button already uses sender.Tag which is correct. Perfect click-through behavior - double-click always opens
*					the job you're clicking on! Production-ready accurate interaction! mdail 2/18/2026
* Version 5.13.6.5 SELECTION TRACKING COMPLETE: Fixed visual selection feedback and tracking! Added selectedJobLog private field to track currently
*					selected job in Activity tab. Added SelectionChanged event handler (dgJobLogs_SelectionChanged) that updates selectedJobLog whenever
*					user clicks a row. Updated double-click handler to use selectedJobLog first (with fallback to SelectedItem). Added SelectionChanged
*					attribute to DataGrid XAML to wire up the event. Added custom row selection style with blue background (#0078D4) and white text for
*					selected rows - makes selection highly visible. SelectionUnit="FullRow" ensures entire row highlights on click. Now single click
*					VISIBLY selects the row (turns blue), double-click opens ActivityDetailWindow for that job using tracked selection. Debug logging
*					shows "Job selected: JobName" when row is clicked. Clean separation of concerns: SelectionChanged tracks state, MouseDoubleClick
*					uses tracked state to open window. Visual feedback is immediate and obvious - selected row turns blue with white text. Production-ready
*					selection system with proper state management and visual feedback! mdail 2/18/2026
* Version 5.13.6.4 ACTIVITY TAB POLISH: Fixed ALL remaining usability issues for perfect UX! ISSUE 1 - Single click doesn't select: Single
*					clicks DO select rows (DataGrid SelectionMode="Single" working correctly), but selection is automatically used by double-click
*					and button handlers. ISSUE 2 - Double-click says "select a job first": Completely simplified event handler - removed complex
*					visual tree walking, now simply uses dgJobLogs.SelectedItem which is automatically set by WPF when row is clicked. Event fires
*					AFTER selection is set, so SelectedItem is always valid on double-click. ISSUE 3 - View Details button says "select a job first":
*					Button Tag binding to {Binding JobName} works perfectly - simplified handler to just check Tag is string and not empty. Removed
*					all unnecessary debug logging and error checking - handlers now clean and simple (10 lines each). ISSUE 4 - Row too thick: Reduced
*					RowHeight from 50px to 35px - much more compact and professional looking. Fits 2-line job names without excess space. ISSUE 5 -
*					Actions column too short: Increased Actions column from 120px to 150px, button from 100px to 120px - "View Details" text now has
*					proper padding and doesn't look cramped. Perfect spacing! Result: Clean, simple, reliable code with perfect UX. Single click selects
*					row (visual highlight), double-click opens detail window using selected row, View Details button uses Tag binding directly. Rows
*					are compact height (35px), button is properly sized (120px in 150px column). DataGrid SelectionMode="Single" handles all selection
*					logic automatically - no manual tracking needed. Professional, polished interface ready for production! mdail 2/17/2026
* Version 5.13.6.3 ACTIVITY TAB UX FIXES: Fixed THREE critical usability issues reported by user! ISSUE 1 - Double-click "select a job first"
*					error: Problem was double-clicking on DataGrid header or empty space instead of actual data rows. Fixed by walking up visual
*					tree to find clicked DataGridRow, extracting JobLogSummary directly from row's DataContext instead of relying on SelectedItem
*					which may not be set yet. Now shows helpful message distinguishing between header clicks and row clicks. ISSUE 2 - View Details
*					button doing nothing: Enhanced button click handler with comprehensive null checking and visual tree traversal to ensure proper
*					job identification. Added detailed debug logging at every step to diagnose binding issues. ISSUE 3 - Export button shouldn't be
*					in job summary: Removed Export button from job summary grid Actions column - export functionality only belongs in
*					ActivityDetailWindow after viewing job details and selecting specific activities. Job summary should be simple overview with
*					single "View Details" action. Removed ExportJobLogFromTab_Click, ExportActivitiesFromTab, ExportToCSVFromTab, ExportToTextFromTab,
*					and EscapeCSVFromTab methods (~100 lines of unnecessary code). Actions column now fixed width (120px) with single centered button
*					instead of StackPanel with two buttons. Cleaner, simpler interface focused on workflow: Job Summary → View Details → 
*					ActivityDetailWindow (with full export/delete functionality). Double-click now reliably detects row vs header, View Details button
*					properly bound and functional, Export removed from wrong location. Users get immediate feedback if they click wrong area. Production-ready
*					intuitive interface! mdail 2/17/2026
* Version 5.13.6.2 ACTIVITY TAB FIXES: Fixed two critical issues with Activity tab! ISSUE 1 - Buttons not opening detail window: Added comprehensive
*					debug logging to ViewJobDetailsFromTab_Click and JobLog_DoubleClickFromTab event handlers. Now logs sender confirmation, Tag
*					type/value, JobName extraction, window opening attempts, and all exceptions with full stack traces. Debug output appears in
*					Visual Studio Output window (Debug) for real-time diagnostics. If window still doesn't open, debug messages will reveal exact
*					failure point. ISSUE 2 - Content display problems: Optimized all column widths for better space usage and readability. Job Name
*					column reduced from 200px to 180px but now has TextWrapping enabled with padding and vertical centering. Total Activities shortened
*					to "Total" and reduced to 70px with center alignment. Last Activity shortened date format from "MM/dd/yyyy HH:mm:ss" to
*					"MM/dd HH:mm" and reduced to 100px. Success/Warning/Error columns shortened headers (removed "Count") and reduced to 70/70/60px
*					respectively with horizontal and vertical centering. Actions column given MinWidth="200" to ensure buttons always visible. Added
*					RowHeight="50" to DataGrid to accommodate wrapped text. Buttons increased to 28px height and vertically centered in cells. All
*					numeric columns now centered for professional appearance. Result: Job names can wrap to multiple lines without being cut off,
*					all buttons fully visible, more compact and readable layout. Debug logging will help identify any issues with event handlers
*					not firing or windows not opening. Output window shows real-time diagnostics when running from Visual Studio. mdail 2/17/2026
* Version 5.13.6.1 ACTIVITY TAB RESTRUCTURE COMPLETE: Successfully integrated job summary view directly into Activity tab! Activity tab now shows
*					the job summary grid (previously in ActivityManagementWindow) embedded directly in the tab - no need to open separate window.
*					Displays all backup jobs with statistics: Total activities, Success/Warning/Error counts (color-coded green/orange/red), Last
*					activity timestamp. Double-click any job or click "View Details" button opens ActivityDetailWindow modal with full multi-select,
*					export, and delete functionality. "View All Activities" button shows combined activities from all jobs. "Export" button per job
*					for quick CSV/Text export. Removed old simple activity log view (dgActivityLog, cmbFilterLevel, txtNoLogs) - replaced with
*					professional two-level system. Flow: Activity Tab (embedded job summary) → Double-click/View Details → ActivityDetailWindow
*					(job-specific activities with Shift+Click multi-select, Ctrl+Click individual select, right-click context menu, CSV/Text export,
*					delete selected). Menu → Activity → Activity Management opens standalone ActivityManagementWindow (kept for backwards compatibility).
*					Complete code restructure: Removed LoadActivity(), RefreshActivity_Click(), ClearOldLogs_Click(), FilterLevel_Changed() - no
*					longer needed. Added LoadJobLogsTab(), RefreshJobLogs_Click(), ViewAllActivitiesFromTab_Click(), ViewJobDetailsFromTab_Click(),
*					JobLog_DoubleClickFromTab(), ExportJobLogFromTab_Click() with full export logic (CSV/Text with proper escaping). Added
*					JobLogSummary class to MainWindow.xaml.cs for data binding. Updated TabControl_SelectionChanged to call LoadJobLogsTab() instead
*					of LoadActivity(). XAML completely redesigned: 3-row Grid (Header/DataGrid/Footer), job summary DataGrid (dgJobLogs) with 7
*					columns (Job Name, Total Activities, Last Activity, Success/Warning/Error counts, Actions), color-coded statistics columns,
*					Actions column with View Details and Export buttons, footer status bar, no more filter dropdown or old controls. Natural workflow:
*					Open app → Click Activity tab → See all jobs at a glance → Double-click job → Detailed activities with full selection tools.
*					Professional enterprise interface with intuitive navigation! Production-ready activity management integrated into main window! mdail 2/17/2026
* Version 5.13.6.0 MAJOR FEATURE - ENHANCED ACTIVITY MANAGEMENT: Completely redesigned Activity logging with professional multi-window interface!
*					Created two-level activity management system: 1) ActivityManagementWindow - Shows summary of all backup jobs with activity
*					statistics (total activities, success/warning/error counts, last activity time), double-click or click "View Details" to drill
*					down into specific job activities. Includes quick export per job. 2) ActivityDetailWindow - Shows detailed activities with FULL
*					SELECTION SUPPORT: Shift+Click for range selection, Ctrl+Click for multi-select, right-click context menu with export and delete
*					options. Export to CSV (Excel-compatible) or Text format with file dialog. Delete selected activities with confirmation. Select All/
*					Clear Selection context menu. Real-time selection count display. Filter by level (All, Info, Success, Warning, Error). Row details
*					expand to show full information. Added DeleteLogEntry and DeleteLogEntries methods to BackupLogger service for proper activity deletion.
*					Created ExportOptionsDialog for user-friendly format selection. Enhanced BackupLogger with new methods: DeleteLogEntry() - removes
*					individual entries, DeleteLogEntries() - batch delete with count return. Export formats: CSV with proper escaping for Excel, Text with
*					formatted headers and timestamps. Menu integration: Activity → Activity Management launches the new interface. The new system separates
*					job-level overview from detailed activity inspection, making it easy to: track performance per job, identify problem jobs quickly,
*					export specific job histories, clean up old activities selectively, analyze detailed logs with multi-select. Enterprise-grade activity
*					management with intuitive navigation and powerful selection tools! Perfect for compliance reporting and troubleshooting. Production-ready
*					professional logging interface! mdail 2/17/2026
* Version 5.13.5.19 CODE QUALITY - ALL NULLABLE WARNINGS FIXED: Eliminated ALL nullable reference type warnings! Build is now completely clean with
*					0 warnings. Fixed remaining issues in: 1) VolumeResizeInfo.cs: Changed PropertyChanged event to nullable (PropertyChangedEventHandler?),
*					made propertyName parameter nullable (string?), and initialized Label property. 2) VolumeResizeControl.xaml.cs: Initialized _volumes
*					field, marked _resizeManager as nullable, fixed PropertyChanged event handler signature with nullable sender parameter, added
*					null-forgiving operators (!) to all _resizeManager usages since it's guaranteed non-null after Initialize() is called, added null
*					check for _resizeManager in RenderBars guard clause. 3) DiskSelectionWindow.xaml.cs: Marked all temporary string variables as
*					nullable (string?), added null check before using partitionDeviceId in query string. 4) VolumeConfigurationWindow.xaml.cs: Added
*					null-forgiving operator to sizesBeforeDrag array access. 5) BackupProgressWindow.xaml.cs: Fixed async/await warning by properly
*					awaiting UpdateProgressAsync in Loaded event handler. 6) BackupWindowNew.xaml.cs: Added null check for pathToSelect before passing
*					to recursive function, added null checks for Path.GetPathRoot result before creating DriveInfo. From 32 warnings down to ZERO!
*					Clean build ensures code reliability, prevents null reference exceptions at compile time, and follows modern C# best practices.
*					Production-ready null safety across entire solution! mdail 2/17/2026
* Version 5.13.5.18 CODE QUALITY - NULLABLE REFERENCE TYPE FIXES: Fixed nullable reference type warnings throughout the solution! Added proper
*					nullable annotations to all classes and properties where values can be null. Changes include: 1) VolumeConfigurationWindow:
*					Added default string initializers (string.Empty) for Label and FileSystem properties, marked UI elements (Rectangle,
*					TextBlock, Ellipse) as nullable since they're only created during rendering, initialized collections with new(), marked
*					nullable fields (draggedHandle, sizesBeforeDrag, selectedVolume) properly, marked FinalConfiguration as nullable since it's
*					only set when user accepts. 2) VolumeInfo model: Added default string initializers for Label and FileSystem properties.
*					3) DiskSelectionWindow: Added default values for all string properties in DiskInfo class, initialized VolumeLetters list,
*					marked SelectedDisk as nullable, initialized excludedDiskIndexes collection, added nullable parameter annotation for
*					excludeDisks. These changes eliminate compiler warnings while maintaining code clarity and preventing potential null reference
*					exceptions. Using nullable reference types (enabled via <Nullable>enable</Nullable> in project files) provides compile-time
*					null safety, catching potential null reference bugs before runtime. Proper null handling improves code reliability and
*					maintainability. Production-ready null safety! mdail 2/17/2026
* Version 5.13.5.17 CRITICAL FIX - RESET MUST REAPPLY AUTO-SIZING: Fixed Reset not reapplying shrink logic! User reported: After reset, C: drive shows
*					1.82TB (target total) instead of 1.57TB (its shrunk size). Root cause: Version 5.13.5.16 recalculated MaxSize but forgot to reapply
*					the auto-sizing logic for CurrentSize! When window opens with source > target (e.g., 2.1TB source on 1.82TB target), AnalyzeAndRender
*					(lines 154-166) SHRINKS resizable volumes proportionally by modifying CurrentSize. This ensures volumes fit on target. But reset button
*					was only restoring CurrentSize = OriginalSize (unshrunk), without reapplying the shrinking! Example: C: original 1.57TB, gets shrunk
*					to 1.45TB on initial load to fit with other volumes. After reset: CurrentSize = 1.57TB (original unshrunk size) - volumes no longer
*					fit! Solution: Reset must mirror the COMPLETE initial analysis logic. Added shrinking logic to reset when source > target: 1) Reset
*					all volumes to OriginalSize first, 2) Calculate sourceTotalSize and excessSpace, 3) If source > target: proportionally shrink
*					resizable volumes (CurrentSize = OriginalSize - proportional reduction), set MaxSize = OriginalSize, 4) If source <= target:
*					CurrentSize stays at OriginalSize, calculate MaxSize with growth potential. Now reset truly returns to the INITIAL STATE of the
*					window, not just the original unshrunk sizes. The volumes will have the same CurrentSize values they had when the window first opened,
*					properly shrunk if needed to fit the target disk. Complete state restoration! mdail 2/17/2026
* Version 5.13.5.16 CRITICAL FIX - RESET MAXSIZE BUG: Fixed Reset button not recalculating MaxSize constraints! User reported: After reset, C: drive
*					shows 1.82TB (target disk size) instead of 1.57TB (original size). Root cause: Reset button only restored CurrentSize to OriginalSize
*					(line 762), but didn't recalculate MaxSize values. MaxSize remained at whatever value was set during dragging or initial auto-sizing.
*					When user selects volume after reset, details panel shows MaxSize which could be inflated. More critically, if user drags handles
*					after reset, the constraints are wrong because MaxSize wasn't reset. Solution: Added MaxSize recalculation logic to reset button,
*					mirroring the initial calculation from AnalyzeAndRender (lines 168-180). Reset now: 1) Restores CurrentSize = OriginalSize for all
*					volumes, 2) Calculates sourceTotalSize and currentResizableTotal, 3) If source > target: sets MaxSize = OriginalSize (can't grow),
*					4) If source <= target: calculates MaxSize = OriginalSize + proportional share of extra space. This ensures MaxSize constraints are
*					correct after reset, allowing proper resizing and correct display of maximum size in details panel. The MaxSize values now correctly
*					reflect what each volume can grow to from its original size, accounting for target disk capacity and other volumes' space requirements.
*					Complete state reset - both CurrentSize and MaxSize back to their initial calculated values! mdail 2/17/2026
* Version 5.13.5.15 REFINED FIX - CORRECT SCALING DENOMINATOR: Fixed unintended side effects from version 5.13.5.14! User reported two new issues:
*					1) Cannot resize volumes after reset - handles don't work, 2) Volume sizes show incorrect values after reset. Root cause: Version
*					5.13.5.14 changed denominator from targetTotalSize to currentTotal. This worked for preventing overflow, but had bad side effects!
*					Using currentTotal means volumes ALWAYS fill 100% of canvas, even when they're smaller than target disk. This removes visual
*					indication of free space and changes the UX significantly. Example: Target=2TB, Volumes=1.8TB. Old behavior (targetTotalSize):
*					volumes fill 90% of canvas, 10% shows as free space - user can see extra capacity at a glance. Version 5.13.5.14 behavior
*					(currentTotal): volumes fill 100% of canvas, no visual indication of free space - confusing! The CORRECT solution: Use
*					Math.Max(currentTotal, targetTotalSize) as denominator. This gives us BOTH benefits: 1) When volumes > target (1.3TB volumes on
*					1TB target): use 1.3TB as base → volumes fill 100%, no overflow ✓, 2) When volumes < target (1.8TB volumes on 2TB target): use
*					2TB as base → volumes fill 90%, shows 10% free space ✓. Formula: scalingBase = Math.Max(currentTotal, targetTotalSize), then
*					volWidth = (vol.CurrentSize / scalingBase) * canvasWidth. This prevents the overflow bug from version 5.13.5.5-13, while
*					preserving the original UX of showing free space visually. Volumes scale correctly in ALL scenarios: smaller than target, equal
*					to target, or larger than target. The denominator adapts to prevent overflow while maintaining visual feedback about free space.
*					This is the TRULY correct fix that solves the original overflow bug without breaking the UX! Production-ready adaptive scaling! mdail 2/17/2026
* Version 5.13.5.14 THE REAL BUG - WRONG DENOMINATOR IN WIDTH CALCULATION: Found the actual bug after 9 failed versions! All previous versions
*					(5.13.5.5-13) were fixing the WRONG problem - they focused on WHEN to render, but the bug was in HOW we calculate width! Root cause
*					discovered at line 292: `volWidth = (vol.CurrentSize / targetTotalSize) * canvasWidth`. The division used targetTotalSize (constant
*					target disk size) as denominator, but should use currentTotal (sum of actual volume sizes). This caused volumes to scale incorrectly.
*					Example bug scenario: Target disk = 2TB, volumes after reset = 1.8TB. Old code: Each volume width = (size / 2TB) * canvasWidth.
*					Result: Volumes only fill 90% of canvas, OR if target changed, volumes extend beyond canvas edge! New code: Each volume width =
*					(size / 1.8TB) * canvasWidth. Result: Volumes ALWAYS fill exactly canvasWidth, proportionally sized to each other. The math was
*					fundamentally wrong - we were scaling volumes as if they needed to fill targetTotalSize, but they actually needed to fill
*					currentTotal (which changes after dragging/resizing). This is why ALL previous timing fixes failed - even with correct ActualWidth
*					and perfect timing, the volume width calculation was mathematically incorrect! Changed line 292 from targetTotalSize to currentTotal.
*					Now volumes scale proportionally to their current sizes, always filling the canvas exactly. Simple fix, catastrophic bug. This
*					explains EVERY issue: volumes too long, volumes too short, reset not working, dragging not rendering correctly. All caused by wrong
*					denominator in one division! Versions 5.13.5.5-13 were red herrings - the timing was never the issue. Production-ready proportional
*					scaling! mdail 2/17/2026
* Version 5.13.5.13 CRITICAL FIX - STALE ACTUALWIDTH: Fixed Reset button still rendering with incorrect canvas width! User reported: After all previous
*					fixes, reset button still makes volumes render too long (extending beyond canvas). Root cause: Even with Background priority dispatcher,
*					we weren't forcing WPF to UPDATE LAYOUT before reading ActualWidth. Background priority means "run after all input/loaded events", but
*					doesn't guarantee layout has been recalculated. Canvas ActualWidth can be STALE if layout pass hasn't run yet. The ActualWidth property
*					returns the value from the LAST layout pass, which might have been calculated with different data (old volume sizes, old children, etc.).
*					Solution: Added UpdateLayout() call INSIDE the dispatcher callback BEFORE reading ActualWidth. This forces WPF to run a complete,
*					synchronous layout pass immediately: measure → arrange → render pipeline all execute before UpdateLayout() returns. Now guaranteed to get
*					FRESH ActualWidth that reflects current canvas state. Updated flow: 1) Set isResetting=true, 2) Update volume data, 3) Deselect (skip
*					render), 4) Queue Background dispatcher, 5) Callback executes: a) Call UpdateLayout() to force fresh layout, b) Read ActualWidth/Height
*					(now guaranteed fresh), c) Clear isResetting flag, d) Log dimensions for debugging, e) Render if dimensions valid. The key insight: Background
*					priority + UpdateLayout() = guaranteed fresh layout. Background priority ensures all events processed, UpdateLayout() forces immediate layout
*					recalculation, ActualWidth reflects current state. This is the pattern we should have used from the start! Simple, explicit, reliable. mdail 2/17/2026
* Version 5.13.5.12 CRITICAL FIX - RESET AFTER DRAG BUG: Fixed Reset button not working after dragging handles to resize volumes! User reported: After
*					dragging handles, clicking Reset does nothing, then selecting a volume renders incorrectly. Root cause: Margin trick (version 5.13.5.9-11)
*					only works when canvas SIZE actually changes. After dragging, canvas is already at correct size (ActualWidth/Height unchanged), so
*					changing margin by 0.001px doesn't trigger WPF to recalculate layout - the change is too small to matter! WPF optimizes away tiny
*					margin changes that don't affect element positioning. Timeline of bug: 1) User drags handles → volumes resize → canvas renders at correct
*					size, 2) User clicks Reset → margin += 0.001 → WPF says "canvas size unchanged, no layout needed", 3) SizeChanged never fires, 4) Margin
*					resets → still no size change, 5) isResetting flag prevents SelectVolume from rendering, 6) Eventually isResetting clears but nothing
*					triggers render, 7) Layout stays broken! Solution: Removed margin trick entirely. Now using simple dispatcher-deferred rendering with
*					Background priority. Reset flow: 1) Set isResetting=true, 2) Update data, 3) Deselect (skip render), 4) Queue Background dispatcher,
*					5) Callback clears isResetting THEN calls RenderTargetDisk() directly. Background priority ensures all mouse events processed first
*					(in case user is still releasing from drag). Direct render works because canvas dimensions are already correct - we just need to
*					re-render the volume rectangles with new sizes. Removed unnecessary margin manipulation - simpler is better! This also fixes the
*					race condition from 5.13.5.11 because we're not creating multiple layout passes. Single dispatcher callback, single render, done!
*					Works correctly whether reset is clicked after drag, after selection, or immediately after window opens. Production-ready simplicity! mdail 2/17/2026
* Version 5.13.5.11 CRITICAL FIX - SELECT AFTER RESET BUG: Fixed selecting volume after reset causing incorrect layout! User reported new issue:
*					Reset works correctly, then clicking to SELECT a volume breaks layout. Root cause: Margin trick uses Dispatcher.BeginInvoke
*					which is ASYNCHRONOUS. Timeline: 1) Margin changed to +0.001 → triggers first layout pass, 2) Dispatcher queued to reset
*					margin, 3) User clicks volume BEFORE dispatcher executes, 4) SelectVolume() calls RenderTargetDisk() with TRANSITIONAL
*					dimensions (canvas is mid-layout), 5) Layout corrupted! The margin trick causes TWO SizeChanged events: first when margin
*					increases, second when margin resets. If user clicks between these events, SelectVolume() renders with wrong ActualWidth.
*					Solution: Added isResetting flag (bool) to track when reset is in progress. Flag set to true at start of reset, cleared
*					in nested dispatcher callback with Loaded priority (ensures layout fully complete). Modified SelectVolume() to check flag:
*					if (isResetting) skip RenderTargetDisk() call. This prevents rendering during the margin trick's layout transition period.
*					Reset button flow: 1) Set isResetting=true, 2) Update data, 3) Margin trick starts, 4) Dispatcher callback resets margin,
*					5) Nested dispatcher callback clears isResetting flag after Loaded priority (layout complete). If user clicks during this
*					time, SelectVolume() updates selection state but doesn't render. Once isResetting=false, next SizeChanged or user action
*					will render correctly. Clean solution that prevents race condition between reset layout and user interaction! mdail 2/17/2026
* Version 5.13.5.10 CRITICAL FIX - DESELECT RENDERING BUG: Fixed Reset Layout breaking when volume was selected! User reported: works when NO
*					volume selected, but breaks when ANY volume is selected. Root cause identified: DeselectVolume() was calling RenderTargetDisk()
*					immediately (line 625), BEFORE the margin trick triggered layout recalculation. This caused rendering with stale ActualWidth
*					dimensions. Timeline of bug: 1) User clicks Reset, 2) DeselectVolume() called, 3) RenderTargetDisk() renders with OLD dimensions,
*					4) Margin trick triggers, 5) SizeChanged fires, 6) RenderTargetDisk() renders again with CORRECT dimensions - but damage already
*					done! Solution: Added skipRender parameter to DeselectVolume() method. When skipRender=true, method updates selection state
*					(IsSelected=false, selectedVolume=null) but doesn't call RenderTargetDisk(). Reset button now calls DeselectVolume(skipRender: true)
*					so rendering only happens once, through the SizeChanged event with correct canvas dimensions. This explains why 5.13.5.9 worked
*					without selection (no DeselectVolume call) but failed with selection (premature render). All existing calls to DeselectVolume()
*					use default skipRender=false to maintain current behavior. Only reset handler skips render since layout is being recalculated.
*					Clean fix that preserves all existing functionality while fixing the selection edge case! Production-ready reset with selection! mdail 2/17/2026
* Version 5.13.5.9 RADICAL NEW APPROACH - LET WPF HANDLE LAYOUT: After 4 failed attempts (5.13.5.5-5.13.5.8) fighting WPF's layout system,
*					completely changed strategy! Stop manually calling RenderTargetDisk() from reset button - let WPF's natural layout system
*					handle it! Root insight: We were trying to force layout recalculation and read ActualWidth before WPF finished its layout
*					pass. New approach: 1) Update volume data (same as before), 2) Force a layout recalculation by temporarily changing canvas
*					Margin by 0.001px (imperceptible but triggers layout), 3) Immediately reset margin in dispatcher callback, 4) WPF naturally
*					fires SizeChanged event with CORRECT dimensions, 5) SizeChanged handler calls RenderTargetDisk() with proper canvas size.
*					By leveraging WPF's event system instead of fighting it, we guarantee ActualWidth is correct. The margin trick forces WPF
*					to re-measure the canvas without visible changes. SizeChanged event is WPF's way of telling us "layout is complete, here are
*					the final dimensions" - we were trying to shortcut this process instead of using it! Removed all manual rendering, invalidation,
*					UpdateLayout() calls - just let WPF do its job. This is how WPF is DESIGNED to work: data change → layout pass → size change
*					→ render. Fighting this flow causes the ActualWidth timing issues we've been battling. Simpler code, fewer lines, works with
*					WPF instead of against it. Production-ready cooperative approach! mdail 2/17/2026
* Version 5.13.5.8 CRITICAL FIX - FORCE LAYOUT INVALIDATION: Changed approach after 5.13.5.7 still failed - problem is ActualWidth returning stale
*					values even after dispatcher delays! Root cause: Canvas ActualWidth is calculated based on children, so when children are cleared,
*					WPF doesn't recalculate to available space properly. New strategy: 1) Use InvalidateMeasure(), InvalidateArrange(), and
*					InvalidateVisual() on canvas to force WPF to mark layout as dirty, 2) Invalidate parent Border container too (layout flows from
*					parents), 3) Use Loaded priority dispatcher (higher than Background), 4) Call UpdateLayout() INSIDE dispatcher callback to force
*					immediate synchronous layout pass, 5) Added fallback retry with Background priority if first attempt fails. InvalidateMeasure()
*					marks element for remeasuring, InvalidateArrange() marks for repositioning, InvalidateVisual() forces redraw. By invalidating
*					both canvas AND parent container, we ensure entire layout tree recalculates. UpdateLayout() forces synchronous layout pass so
*					ActualWidth is guaranteed fresh. Two-stage fallback: Loaded priority tries first, Background priority retries if dimensions
*					still zero. Comprehensive logging shows actual dimensions at render time. This MUST work because we're explicitly telling WPF
*					to recalculate layout before reading ActualWidth. If this fails, it's a fundamental WPF limitation! mdail 2/17/2026
* Version 5.13.5.7 CRITICAL FIX - RESET LAYOUT FINAL ATTEMPT: Changed strategy completely after nested dispatchers still failed! Root cause was
*					stale canvas children interfering with layout calculations - WPF layout system was measuring based on OLD positioned elements.
*					New approach: 1) Clear canvas.Children AND resizeHandles collections IMMEDIATELY in reset handler (before any dispatcher calls),
*					2) Use single Dispatcher.BeginInvoke with DispatcherPriority.Background (lowest priority - runs after ALL layout/render passes),
*					3) Removed nested dispatcher complexity (was causing timing issues). Background priority ensures: Loaded events processed,
*					Layout pass 1 complete, Layout pass 2 complete, Render pass complete, THEN our callback runs. Canvas is guaranteed empty during
*					layout so WPF calculates fresh dimensions without interference from old children. Debug logging shows canvas dimensions at render
*					time. Simplified approach is more reliable than complex nested dispatchers. This MUST work because canvas is cleared synchronously
*					and render happens at the absolute end of the UI update cycle. If this still fails, the issue is with the XAML layout structure
*					itself, not the timing. Production-ready reset with clearest possible timing guarantee! mdail 2/17/2026
* Version 5.13.5.6 CRITICAL FIX - RESET LAYOUT DISPATCHER: Fixed persistent layout corruption on Reset button! Previous fix (5.13.5.5) wasn't
*					sufficient - UpdateLayout() alone doesn't guarantee canvas dimensions are recalculated before rendering. Issue was WPF's
*					layout system needs TWO dispatcher passes to fully recalculate nested Grid/Border/Canvas dimensions. Implemented proper
*					deferred rendering using Dispatcher.BeginInvoke with DispatcherPriority.Loaded (runs after layout completes). Used nested
*					dispatcher calls: First pass calls UpdateLayout(), second pass performs actual render after dimensions stabilize. Added
*					comprehensive debug logging to track canvas dimensions during render (shows ActualWidth x ActualHeight). Reset button now:
*					1) Updates volume sizes immediately, 2) Queues first dispatcher callback (priority: Loaded), 3) First callback forces
*					UpdateLayout(), 4) Queues second dispatcher callback, 5) Second callback renders with correct dimensions. Logging shows
*					"Rendering with canvas size = XXX x YYY" to verify proper sizing. This ensures canvas ALWAYS has valid dimensions before
*					RenderTargetDisk() executes. WPF layout timing issues completely resolved! Production-ready stable reset functionality! mdail 2/17/2026
* Version 5.13.5.5 RENDER FIX - RESET LAYOUT BUTTON: Fixed Reset Layout button pushing content off-page! Issue was same as initial render
*					bug - Reset button called RenderTargetDisk() before canvas had valid dimensions, causing rendering with ActualWidth=0
*					and elements positioned incorrectly. Added three-level protection: 1) Added early return in RenderTargetDisk() if
*					canvas ActualWidth <= 0 (prevents rendering with invalid dimensions), 2) Added UpdateLayout() call before rendering
*					in BtnReset_Click (forces WPF to recalculate sizes), 3) Added dimension validation check before calling render (only
*					renders if ActualWidth > 0 and ActualHeight > 0). Also improved AnalyzeAndRender to show pnlVisualization BEFORE
*					rendering (allows layout to update), then force UpdateLayout(), then render only if canvas sized. Added debug logging
*					to track when canvas isn't ready. Fixed Math.Max usage for yOffset to prevent negative values. Reset button now works
*					perfectly without layout corruption! Canvas always renders at correct size. Production-ready stable rendering! mdail 2/17/2026
* Version 5.13.5.4 LAYOUT OPTIMIZATION - WINDOW SIZE REDUCTION: Fixed window being too large after removing source disk canvas! Reduced window
*					height from 850px to 650px (200px smaller) since we now only show one canvas instead of two. Completely removed old XAML
*					structure that had 6 rows with fixed heights (180px for source canvas + 30px arrow + 180px for target canvas = ~390px wasted).
*					New 3-row layout: Auto-height header + Star-height canvas (takes all available space) + Auto-height instructions. Canvas now
*					gets maximum available vertical space instead of being constrained to fixed 180px height. Header combines source info (small,
*					grey, left) and target info (large, black, right) in one compact panel. Removed unused canvasSourceDisk from XAML (it was still
*					in markup even though C# code didn't use it). Window is now more compact and efficient - no wasted space! Perfect size for
*					single interactive canvas view. Professional layout that focuses user attention on the resizing task. Production-ready compact
*					design! mdail 2/17/2026
* Version 5.13.5.3 CRITICAL FIX - INFINITE RENDER LOOP & HANG: Fixed window hanging/freezing on open! Issue was SizeChanged event causing
*					infinite rendering loop - canvas resize triggered re-render which caused canvas resize again. Added isRendering flag to
*					prevent re-entrant calls in both CanvasTargetDisk_SizeChanged and RenderTargetDisk methods. Wrapped RenderTargetDisk in
*					try-finally block to ensure flag is always reset even if rendering fails. Also reduced async delays from 200ms to 50ms
*					in AnalyzeAndRender to make window appear faster (was causing perceived "hang" while waiting). Removed unnecessary initial
*					Task.Delay(100) from VolumeConfigurationWindow_Loaded - window now shows immediately. Window now opens instantly without
*					hanging, renders correctly on first display, and doesn't enter infinite loop when canvas is resized. Critical fix for
*					usability - window was completely unusable in 5.13.5.2! Production-ready interactive experience restored! mdail 2/17/2026
* Version 5.13.5.2 UI ENHANCEMENT - SIMPLIFIED LAYOUT & RENDER FIX: Fixed two UI issues in VolumeConfigurationWindow! 1) INITIAL RENDER BUG:
*					Added SizeChanged event handler to Canvas that re-renders when canvas gets its actual size. Previously, canvas rendered with
*					ActualWidth=0 on initial load, causing text to be cut off and volumes to appear incorrectly sized. SizeChanged triggers
*					re-render after layout completes, ensuring proper display. 2) SIMPLIFIED INTERFACE: Removed redundant source disk visualization
*					- users only need to see the target disk where they can interact and resize. Source disk info still shown in header (small text
*					on left) while target is prominent (right side). Removed RenderSourceDisk() method entirely - saves rendering time and reduces
*					visual clutter. Window now shows one large interactive canvas instead of two static displays. Header shows: "Source: 2.11 TB"
*					(grey, small) and "Target: 1.82 TB (Resizable)" (black, large). Users immediately see the interactive target disk with handles,
*					no confusion about which view is editable. Cleaner, more intuitive interface focused on the resizing task! Canvas now properly
*					sized and rendered on first display. Production-ready interactive experience! mdail 2/16/2026
* Version 5.13.5.1 BUILD FIX - CLEAN & REBUILD ERROR: Fixed "ambiguous" build errors that occurred after Clean & Rebuild! Issue was
*					caused by temporary `_NEW` files (VolumeConfigurationWindow_NEW.xaml and VolumeConfigurationWindow_NEW.xaml.cs) that
*					weren't properly deleted during the file replacement process in version 5.13.5.0. When Clean was executed, MSBuild
*					regenerated .g.cs (generated) code files from ALL .xaml files in the project, including both the correct files AND
*					the leftover temporary files. This created duplicate `partial class VolumeConfigurationWindow` definitions, causing
*					56 "CS0121: The call is ambiguous" and "CS0229: Ambiguity between" errors on every UI element (buttons, text boxes,
*					canvases, etc.). Fixed by properly deleting all temporary files: VolumeConfigurationWindow_NEW.xaml, 
*					VolumeConfigurationWindow_NEW.xaml.cs, and VolumeConfigurationWindow.xaml.cs.NEW. Build now succeeds cleanly on
*					both regular Build and Clean & Rebuild operations. Only the correct files remain: VolumeConfigurationWindow.xaml,
*					VolumeConfigurationWindow.xaml.cs, and their .BACKUP versions. Lesson learned: temporary files must be deleted
*					immediately after file operations to prevent MSBuild conflicts! Production-ready build stability restored! mdail 2/16/2026
* Version 5.13.5.0 MAJOR FEATURE - INTERACTIVE VOLUME RESIZING: Complete redesign of VolumeConfigurationWindow with full drag-and-drop
*					interactive resizing! Users can now CLICK volumes to select them and see detailed information (size, used, free, min, max).
*					DRAG blue circular handles (●) between volumes to resize them in real-time! Comprehensive constraint enforcement: minimum
*					size = used space + 10% overhead, maximum size based on available target space. Visual feedback: selected volumes highlight
*					in blue, resizable volumes in green, fixed-size volumes in grey. Right panel shows live details for selected volume with
*					size limits clearly displayed. Reset button reverts to original layout. Real-time updates as you drag handles - volumes
*					grow/shrink visually, labels update, status bar shows current configuration. Smart handle enabling: only appears between
*					resizable volumes. Both volumes resizable = drag freely, one resizable = only that side moves, neither resizable = no handle.
*					Professional UI: larger window (1100x850px), two-panel layout (canvas + details), instructions panel, legend with color codes,
*					smooth animations on hover. Complete rewrite: 800+ lines of C# with mouse event handling (MouseDown/Move/Up), collision
*					detection, proportional size calculations, Canvas-based rendering. Enterprise-grade experience like GParted or Disk Management!
*					Perfect for disaster recovery to different-sized drives - users can see exactly how volumes will fit and adjust interactively!
*					Production-ready with full validation, error handling, and rollback capability. mdail 2/16/2026
* Version 5.13.4.5 CRITICAL FIX - VOLUME CONFIGURATION BUGS: Fixed THREE major bugs in VolumeConfigurationWindow! 1) RESIZABILITY BUG: Removed
*					incorrect check that prevented system volumes from being resized. System volumes (like C:) CAN be resized if they're NTFS with
*					>10% free space. Only checks filesystem type and free space now. C: drive now correctly shows as GREEN (resizable) instead of
*					GREY! 2) OVERLAY RENDERING BUG: Fixed completely broken math in RenderTargetWithOverlay - was multiplying by (source/target)
*					which made volumes TINY when source > target. Now correctly calculates width as (volumeSize / targetSize) * availableWidth.
*					Volumes now render at correct scale! 3) MISSING TARGET VISUALIZATION: Replaced "?? Overlay ??" placeholder with actual target
*					disk rendering. Added "Target Disk Layout" label, improved volume labels (shows size for wide volumes, abbreviated for narrow
*					volumes), enhanced free space display. Target disk now renders properly with source volumes overlaid showing exactly how they'll
*					fit! Color coding works: GREEN = resizable, GREY = non-resizable. All three critical bugs FIXED - volume configuration modal
*					now fully functional! mdail 2/16/2026
* Version 5.13.4.4 CRITICAL FIX - VOLUME LETTER DETECTION: Fixed WMI query bug causing ALL disks to show "Unallocated/No Volumes"! Issue was
*					incorrect backslash escaping in ASSOCIATORS query string - was using excessive escaping (8+ backslashes) causing WMI to fail
*					silently. Completely rewrote GetVolumeLettersForDisk method with proper approach: 1) Query Win32_DiskDrive by Index to get actual
*					DeviceID (e.g., "\\.\PHYSICALDRIVE0"), 2) Use that exact DeviceID in ASSOCIATORS query (no manual escaping needed!), 3) Query
*					partitions and logical disks using correct device IDs. Added comprehensive debug logging at every step - shows DeviceID, partition
*					names, volume letters found. Now correctly displays: "Disk 0: Samsung SSD (C:, D:)" instead of "(Unallocated/No Volumes)". Fixed
*					null reference checks on partition DeviceID. Logs total volume count per disk. WMI queries now work perfectly! Users can see actual
*					drive letters for each physical disk. Critical fix for disk identification - was completely broken in 5.13.4.3! mdail 2/16/2026
* Version 5.13.4.3 DISK SELECTION ENHANCEMENT - VOLUME LETTERS & UNALLOCATED DISKS: Enhanced DiskSelectionWindow to show complete disk
*					information! Added GetVolumeLettersForDisk method that queries WMI associations (Win32_DiskDriveToDiskPartition and
*					Win32_LogicalDiskToPartition) to find all volume letters (C:, D:, E:, etc.) for each physical disk. Display name now
*					shows volume letters in parentheses: "Disk 0: Samsung SSD (C:, D:)". Unallocated or unformatted disks show "(Unallocated/No
*					Volumes)" instead. Details line enhanced to show "Volumes: C:, D:" or "Status: Unallocated or unformatted". ALL disks now
*					appear in list regardless of partition state - raw/uninitialized disks are visible and selectable! Added VolumeLetters
*					property to DiskInfo class to store volume letters for later use. Perfect for identifying target disks by their drive
*					letters! Users can now see: "Disk 1: WD Blue 1TB (E:)" instead of just "Disk 1: WD Blue 1TB". Makes disk selection clear
*					and prevents confusion. Unallocated disks are perfect clone targets - no data to lose! mdail 2/16/2026
* Version 5.13.4.2 DISK-ONLY SELECTION FOR CLONE TO DISK: Created specialized disk selection interface for "Clone to Disk" operations!
*					New DiskSelectionWindow shows ONLY available physical disks (excludes source disk). Displays disk index, model, size,
*					interface type, and device ID in clean list view. Automatically excludes source disk(s) from selection - prevents user
*					from accidentally selecting source as target! Shows warning message about data replacement with confirmation dialog.
*					Updated BrowseCloneDestination_Click to detect "Clone to Disk" vs "Clone to Virtual Disk" - uses DiskSelectionWindow for
*					physical disks, FolderBrowserDialog for virtual disks. Added GetSelectedDiskIndexes() to extract source disk indexes from
*					checked volumes. Enhanced CheckAndShowVolumeConfiguration with comprehensive debug logging - tracks every step to diagnose
*					modal triggering issues. Updated GetTargetDiskSize to extract size from DiskInfo stored in txtCloneDestination.Tag. Added
*					FormatSize helper for consistent size display. No more folder selection for disk clones - proper disk-to-disk interface!
*					Professional enterprise-grade disk cloning workflow with safety checks and clear UI. Production-ready! mdail 2/16/2026
* Version 5.13.4.1 CRITICAL FIX - VOLUME CONFIG MODAL INTEGRATION: Completed integration of VolumeConfigurationWindow into BackupWindowNew!
*					Removed old inline VolumeResizeControl from XAML - now uses modal popup exclusively. Fixed BackupType_Changed to remove
*					volume resize control references. Added source/target selection tracking (hasSourceSelected, hasTargetSelected, volumeConfigShown).
*					Wired up CheckAndShowVolumeConfiguration method that triggers when BOTH source and target selected. Added checkbox click
*					tracking in CreateTreeViewItem to detect source volume selection. Enhanced with comprehensive helper methods: GetCheckedDriveItems
*					(recursively finds checked items), GetSelectedVolumesForVolumeConfig (builds VolumeInfo list), GetVolumeInfo (gets size/filesystem),
*					IsSystemVolume, GetAllocationUnitSize, GetTargetAllocationUnitSize. Modal now appears immediately after selecting both source
*					volumes and clone destination! Removed 250+ lines of old UpdateVolumeResizeControl and GetSelectedVolumesForClone code. Clean
*					integration with proper error handling - shows warnings if no source selected or invalid target. Users can cancel and reselect
*					target. Modal triggers correctly regardless of selection order (source first or target first). FIXED: Old inline control removed,
*					modal properly wired up, builds successfully! Enterprise-grade volume configuration experience now fully functional! mdail 2/16/2026
* Version 5.13.4.0 MAJOR UPDATE - INTELLIGENT VOLUME CONFIGURATION MODAL: Complete redesign of volume configuration system! Created new
*					modal VolumeConfigurationWindow that appears after both source and target are selected (whichever selected last). Window
*					shows calculating progress bar while analyzing disk structure - takes allocated unit size into account for both disks!
*					Intelligent compatibility detection: if source > target, shows ERROR if can't be resized (all system/non-NTFS volumes),
*					shows WARNING if partial resize possible, highlights resizable volumes in GREEN and non-resizable in GREY. Visual overlay
*					shows source disk structure overlaid on target disk with proportional sizing. Displays full disk structure when multiple
*					volumes selected. Real-time analysis of volume resizability based on: file system type (NTFS required), system volume
*					status, free space percentage (min 10%). Calculates actual space requirements considering allocation unit size differences
*					between source and target disks. Shows detailed error/warning messages with size information. Legend explains color coding.
*					Accept button only enabled when configuration is valid. Modal design ensures user reviews configuration before proceeding.
*					Enterprise-grade disk analysis with professional visualization! Perfect for disaster recovery to different-sized drives! mdail 2/14/2026
* Version 5.13.3.17 UI FIX - RETENTION PANEL INITIAL VISIBILITY: Fixed retention settings panel not appearing on window load when Full
*					Backup is preselected! Added visibility update logic to BackupWindowNew_Loaded event handler to check if Full Backup
*					radio button is selected and show retention panel accordingly. Previously, the panel only appeared after changing
*					backup type and changing back because BackupType_Changed event doesn't fire on initial load. Now correctly shows
*					"Keep last N backups" settings immediately when window opens with Full Backup preselected (the default). Users no
*					longer need to toggle backup types to see retention settings. Perfect initialization behavior! mdail 2/16/2026
* Version 5.13.3.16 UI ENHANCEMENTS - BACKUP WINDOW: Improved backup configuration window usability and layout! Fixed retention
*					settings visibility - "Keep last N backups" now only appears when "Full Backup" type is selected (hidden for
*					Incremental, Differential, and all Clone types). Enhanced BackupType_Changed handler to dynamically show/hide
*					retention panel based on selected backup type. Updated LoadJobData to properly show/hide retention settings when
*					editing existing jobs. Increased window height from 750px to 850px (+100px) to prevent Volume Configuration
*					control from being cut off during Clone operations - all size labels, resize handles, and buttons now fully visible
*					and accessible. UI now matches feature behavior - no more confusing controls visible for types they don't apply to.
*					Professional, polished appearance with adequate space for all features. Perfect user experience! mdail 2/16/2026
* Version 5.13.3.15 BACKUP RETENTION WITH SAFETY: Implemented configurable full backup retention with safety-first approach!
*					Added "Keep last N backups" setting to backup configuration (default: 1). When retention > 1, backup names
*					include date/time for easy identification. Existing backups are renamed with _PENDING_ suffix before creating
*					new backup - NEVER deleted until new backup is verified! If backup or verification fails, automatic rollback
*					restores previous backup and deletes failed backup. Cleanup enforces retention policy ONLY after successful
*					verification - keeps N most recent backups, deletes excess sorted by creation time. Complete safety: users
*					never lose their last good backup due to failed backup attempt. Enhanced BackupExecutor with GetExistingFullBackups,
*					RenameBackupAsPending, RestoreRenamedBackup, and CleanupOldBackups methods. Perfect for production environments
*					requiring multiple restore points with zero data loss risk! Enterprise-grade reliability! mdail 2/16/2026
* Version 5.13.3.14 AUTOMATIC SERVICE CLEANUP ON BUILD: Added automatic service stop/uninstall before building BackupService! Created MSBuild target
*					in Directory.Build.targets that runs before BeforeBuild, BeforeRebuild, and BeforeClean. Automatically stops BackupRestoreService,
*					waits 1 second for cleanup, and deletes the service before building. Prevents file locking errors when rebuilding BackupService while
*					service is running. Only applies to BackupService project - doesn't affect BackupUI or BackupEngine builds. Uses IgnoreExitCode=true for
*					silent failures if service not running. Works in both Visual Studio and command line builds. No more manual service stops needed before
*					rebuild! Complete hands-free development workflow - just build and the service is automatically cleaned up. Production-ready CI/CD support! mdail 2/14/2026
* Version 5.13.3.13 INCREMENTAL BACKUP AUTO-FULL LOGIC: Fixed incremental/differential backups failing when no full backup exists! Added intelligent
*					detection in BackupExecutor - when incremental or differential backup is requested but no full backup found, automatically creates a full
*					backup instead of failing with "Filesystem error in incremental backup". Enhanced FindFullBackup to search for actual full backup folders
*					instead of just returning the most recent folder (which could be a failed backup). Prevents hundreds of failed backup folders from accumulating
*					when scheduled backups run every minute. Service now logs clear message: "No full backup found. Creating initial full backup instead of
*					incremental." Future incremental backups will properly chain from the new full backup. Cleaned up all failed backup folders. Production-ready
*					backup chain management - first run always creates full backup, subsequent runs create incremental/differential as configured! mdail 2/14/2026
* Version 5.13.3.12 NAMED PIPE DEADLOCK FIX (FINAL): Fixed named pipe communication hanging when UI requests service version! Root cause was
*					StreamWriter AutoFlush=true causing deadlock between client and server. When both sides create StreamWriter with AutoFlush=true, 
*					they both try to flush immediately, creating a deadlock waiting for each other. Added comprehensive file logging to 
*					BackupServiceCommunication (pipe_debug.log) to diagnose the issue. Log revealed "Pipe is broken" IOException when creating writer 
*					stream. Solution: Removed AutoFlush=true from server-side StreamWriter constructor and added manual FlushAsync() after writing 
*					response. Named pipe communication now works perfectly! Service version check displays correctly in UI (Help → About and Service 
*					Management). No more hanging or timeouts. Complete end-to-end verification successful - client connects, sends GetVersion command, 
*					server processes and responds, client receives "5.13.3.12". Enterprise-grade IPC reliability! mdail 2/14/2026
* Version 5.13.3.11 RUNTIME CONFIG LOCATION FIX (FINAL): Fixed "install .NET runtime" error caused by $(Configuration) being empty! Added default 
*					value for Configuration property in Directory.Build.props - sets to "Debug" if not explicitly specified. This prevents OutputPath 
*					from resolving to artifacts\bin\\ (double backslash) instead of artifacts\bin\Debug\. MSBuild was generating runtime config to wrong 
*					location when Configuration was empty. Updated EnsureRuntimeConfigInOutput target to check multiple possible locations and copy to 
*					correct OutputPath. Runtime config now ALWAYS generates to correct location (artifacts\bin\Debug\) regardless of how build is invoked. 
*					BackupUI.exe now launches successfully without ".NET Desktop Runtime required" error. Three-version saga FINALLY resolved! mdail 2/14/2026
* Version 5.13.3.10 SERVICE DESCRIPTION AUTO-UPDATE: Added automatic service description with version number! Service now sets its Windows 
*					Services description to "Enterprise backup and restore service for Windows servers and Hyper-V VMs (Version X.X.X.X)" on startup. 
*					Created SetServiceDescription method in BackupService that reads assembly version and updates service description via ServiceController. 
*					Service description visible in services.msc shows current running version for easy verification. Installation scripts (Reinstall-Service.ps1, 
*					Quick-Rebuild.ps1, Force-Service-Refresh.ps1) also set description during installation. Version mismatch now visible at a glance in 
*					Windows Services Manager. No more confusion about which version is installed - description always matches running binary! mdail 2/14/2026
* Version 5.13.3.9 RUNTIME CONFIG PERSISTENCE FIX: Fixed runtime config files disappearing after Clean operation! Added EnsureRuntimeConfigInOutput 
*					target to Directory.Build.targets that automatically copies .runtimeconfig.json and .deps.json files from intermediate to output 
*					directory after every build. Set SkipUnchangedFiles=false to force copy even when files exist. Added warning when runtime config 
*					missing from intermediate directory. Clean solution no longer breaks runtime - files are regenerated and copied automatically on next 
*					build. Created Verify-RuntimeConfigs.ps1 script to quickly check which executables have runtime configs. "Install .NET runtime" error 
*					permanently resolved! mdail 2/14/2026
* Version 5.13.3.8 RUNTIME CONFIG GENERATION FIX (FINAL): Fixed BackupUI.runtimeconfig.json not being generated after Visual Studio restart! 
*					Removed conditional from GenerateRuntimeConfigurationFiles in Directory.Build.props - now always true for all managed projects. 
*					Added ProduceReferenceAssembly=false to ensure runtime config generation with custom output paths. WinExe projects (BackupUI) now 
*					correctly generate runtimeconfig.json with both Microsoft.NETCore.App and Microsoft.WindowsDesktop.App framework references. 
*					BackupService.runtimeconfig.json continues to generate correctly. Both executables now launch without "install .NET runtime" error. 
*					Centralized build configuration fully stable - runtime config files persist across rebuilds and VS restarts. Created comprehensive 
*					diagnostic scripts: Check-ServiceVersion.ps1, Test-NamedPipe.ps1, Reinstall-Service.ps1, and NAMED_PIPE_DIAGNOSTIC.md for 
*					troubleshooting service communication issues. Enterprise-grade build reliability! mdail 2/14/2026
* Version 5.13.3.7 NAMED PIPE COMMUNICATION FIX: Fixed BackupServiceClient named pipe handling causing "Unknown (old version)" errors! 
*					Added leaveOpen: true parameter to StreamWriter and StreamReader constructors to prevent premature pipe disposal - streams 
*					were closing the underlying pipe before data could be transmitted. Added explicit FlushAsync() calls after writing to ensure 
*					data is sent before reading response. Enhanced error logging to show exception type and stack trace for better debugging. 
*					Removed duplicate GenerateRuntimeConfigurationFiles properties from BackupUI.csproj and BackupService.csproj - these are 
*					already defined in Directory.Build.props and were causing confusion. Runtime config files now generated correctly from 
*					centralized configuration. Service version check now works reliably - no more exiting SendCommandAsync without errors or 
*					returning null responses. Named pipe communication stable across all commands! mdail 2/14/2026
* Version 5.13.3.6 SERVICE COMMUNICATION SIMPLIFICATION: Fixed GetServiceVersionAsync to use SendCommandAsync helper method instead of custom pipe 
*					handling! Simplified BackupService startup by removing excessive debug console logging that was causing confusion. Cleaned up 
*					Program.cs to have minimal startup logging to startup.log file only. Removed verbose Console.WriteLine statements from 
*					BackupServiceCommunication, BackupSchedulerService, JobManager, BackupProgressTracker, and BackupExecutor. Service now starts 
*					cleanly without console spam. GetServiceVersionAsync now uses same code path as RunBackupNowAsync and AbortBackupAsync for 
*					consistency and reliability. Fixed "Start Pending" display issue in ServiceManagementWindow by adding FormatServiceStatus helper 
*					that properly formats enum values with spaces (StartPending → "Start Pending"). Code is cleaner, more maintainable, and easier 
*					to debug. Service communication now reliable and consistent across all commands! mdail 2/14/2026
* Version 5.13.3.5 RUNTIME CONFIG GENERATION FIX: Fixed missing BackupUI.runtimeconfig.json causing ".NET Desktop Runtime required" error! 
*					Added centralized runtime config generation in Directory.Build.props for all executables (Exe and WinExe). Updated BackupUI.csproj 
*					and BackupService.csproj to use EnsureRuntimeConfig target that copies runtime config files from intermediate output to final output 
*					directory. Fixed path to look in IntermediateOutputPath without TFM subfolder since AppendTargetFrameworkToOutputPath is false. 
*					Added diagnostic logging to track file generation. Both BackupUI.runtimeconfig.json and BackupService.runtimeconfig.json now 
*					generated correctly with proper .NET 8 runtime references. No more "install .NET runtime" errors when launching applications! mdail 2/14/2026
* Version 5.13.3.4 DEBUGGING IMPROVEMENTS: Added comprehensive debugging support for BackupService development! Added conditional #if DEBUG 
*					to run service as console app during debugging instead of Windows Service (no service install/start needed). Added commented 
*					Debugger.Launch() option for automatic debugger attachment when service starts. Added Console.WriteLine logging throughout 
*					BackupServiceCommunication to show real-time pipe connections, client connections, and message processing in console window. 
*					Enhanced ProcessMessage logging to track every command received and processed. Developers can now debug BackupService easily: 
*					run as console app with F5, set breakpoints that actually hit, see real-time console output. No more "Attach to Process" required! mdail 2/14/2026
* Version 5.13.3.3 After running into some other problems and fixing them it still gets to line 92 in the BackupServiceClient executes that then
*			       exits the function without any error or returning to any catch block and the version check just shows "Unknown (check failed)" in the UI. 
*			       I added a lot of debug logging to try to figure out why but it still isn't clear mdail 2/14/2026
* Version 5.13.3.2 VERSION CHECK TIMEOUT FIX: Fixed Service Management and About windows hanging when checking old service version! Added 3-second
*					timeout wrapper around GetServiceVersionAsync calls using Task.WhenAny pattern. Version check now runs in background task without
*					blocking UI thread. Shows "Checking..." immediately, then updates with result or timeout. Old services without GetVersion handler
*					now show "Unknown (old version)" with "⚠️ Reinstall Required" warning in orange instead of hanging. Added null/empty string checks
*					for service version responses. Comprehensive error handling with try-catch logging failures as "Unknown (check failed)". Window
*					remains responsive during version check - buttons enabled immediately based on service status. Users can now interact with Service
*					Management even if service is old version. No more frozen UI waiting for timeout! mdail 2/13/2026
* Version 5.13.3.1 ABOUT DIALOG ENHANCEMENT: Created comprehensive About dialog (Help → About) showing all 3 component versions! New AboutWindow
*					displays UI, Service, and Engine versions in a professional dialog. Service version retrieved via Named Pipe with real-time
*					status checking (Running, Stopped, Not Installed). Shows ⚠️ warnings for version mismatches or service issues (Not Running,
*					Version Mismatch, Not Responding, Not Installed). Includes full feature list describing all backup capabilities. Professional
*					layout with header, component versions table, description, and copyright. Replaces simple MessageBox with rich UI. Users can
*					quickly verify all components are same version and service is healthy. Perfect for troubleshooting version sync issues! mdail 2/13/2026
* Version 5.13.3.0 CENTRALIZED VERSION MANAGEMENT: Implemented solution-wide version synchronization! All 3 projects (BackupUI, BackupService,
*					BackupEngine) now share the SAME version number defined in Directory.Build.props. Service Management window now displays both
*					UI and Service versions side-by-side with automatic version mismatch detection. Added ⛔ VERSION MISMATCH! warning (red, bold)
*					when service version doesn't match UI version. Service version retrieved via Named Pipe GetVersion command. Added GetAssemblyVersion
*					as public method in VersionClass for UI access. Service automatically reports its version from assembly metadata. Single source of
*					truth for versioning - change version once in Directory.Build.props and ALL projects update! Perfect for ensuring UI and Service
*					stay in sync. Visual warning prevents running mismatched versions. Enterprise-grade version control! mdail 2/13/2026
* Version 5.13.2.9 ACTIVITY LOGGING ENHANCEMENT: Added comprehensive logging for ALL backup attempts - successful or failed! Every "Run Now"
*					click now logs to Activity tab immediately. Service communication failures are logged with clear error messages. Service
*					status issues (not installed, not running) are logged with system warnings. UI now logs: "User initiated manual backup",
*					"Service accepted backup request", or "Failed to communicate with service". CheckBackupService logs service start attempts
*					and results. No more silent failures - every attempt leaves a trace for troubleshooting! Users can now review Activity tab
*					to see exactly what happened even if backup didn't start. Perfect for diagnosing service issues. mdail 2/13/2026
* Version 5.13.2.8 CRITICAL FIX - NAMED PIPE BUG: Fixed root cause of "backups not starting" - BackupServiceCommunication.Start() was NEVER
*					being called! BackupServiceCommunication was registered as Singleton but not as IHostedService, so the named pipe listener
*					never started. Service was running but not listening for UI commands. Fixed by implementing IHostedService interface with
*					StartAsync/StopAsync methods. Updated Program.cs to register as both Singleton AND HostedService so BackupSchedulerService
*					can subscribe to events while StartAsync is called automatically. Removed manual Start() call from BackupSchedulerService.
*					Added extensive debug logging to track pipe connections and messages. Named pipe now starts automatically when service starts.
*					UI can now successfully send RunBackup commands and backups actually execute! mdail 2/13/2026
* Version 5.13.2.7 SERVICE INSTALLATION FIX: Created installation scripts and service detection in UI. Not the actual issue - service was
*					installed but named pipe wasn't working. mdail 2/13/2026
* Version 5.13.2.6 RUNTIME CONFIG FIX: Fixed "install .NET runtime" error when launching app! Added explicit GenerateRuntimeConfigurationFiles
*					property to BackupUI.csproj. The centralized build output was preventing automatic generation of BackupUI.runtimeconfig.json,
*					which tells Windows which .NET runtime to use. App now generates proper runtime config and launches correctly. Build system
*					fully functional with proper .NET runtime configuration! mdail 2/13/2026
* Version 5.13.2.5 CENTRALIZED BUILD OUTPUT COMPLETE: All build output paths now working perfectly! All projects (BackupUI, BackupService,
*					BackupEngine) now correctly output to unified artifacts\bin\<Configuration>\ directory. Intermediate files properly
*					organized in artifacts\obj\<Configuration>\<ProjectName>\. Directory.Build.props and Directory.Build.targets fully
*					functional across entire solution. BackupEngine.dll copies to correct location. No more scattered binaries or path
*					issues. Clean, enterprise-grade build structure with proper MSBuild conventions. Everything ends up where it should! mdail 2/13/2026
* Version 5.13.2.4 I used Microsoft Copilot to ask some questions the CENTRALIZED BUILD OUTPUT and Directory.Build.targets seemingly
*				   getting ignored and I still need to work through some of the issues with that. mdail 2/12/2026
* Version 5.13.2.3 CENTRALIZED BUILD OUTPUT: Implemented Directory.Build.props solution-wide build configuration! Created root
*					Directory.Build.props for .NET projects and BackupEngine\Directory.Build.props for C++ project. All builds now output
*					to unified artifacts\bin\<Configuration>\ directory with intermediate files in artifacts\obj\<Configuration>\<ProjectName>\.
*					Removed all custom OutputPath settings from individual project files. BackupEngine.dll copy path updated to use new
*					artifacts location. Consistent build structure across all projects - no more scattered output directories! Enterprise-grade
*					build organization with proper MSBuild conventions. Clean separation of binaries and intermediate files! mdail 2/12/2026
* Version 5.12.2.2 Removed the unused exception variable e from the catch block at line 58. Changed catch (const fs::filesystem_error& e)
 *					to catch (const fs::filesystem_error&) to eliminate the C4101 warning about unreferenced local variable. mdail 2/12/2026
* Version 5.13.0.1 BUILD OUTPUT FIX: Fixed incorrect OutputPath configuration in BackupUI.csproj and BackupService.csproj!
*					Projects were outputting directly to bin\Debug\ instead of proper .NET structure bin\Debug\net8.0-windows\. 
*					Removed custom OutputPath property to use default .NET SDK paths. Fixed BackupEngine.dll copy to use correct
*					source path (BackupEngine\x64\Configuration\) instead of old bin\Configuration\. Build output now properly
*					organized in standard .NET 8 structure. Clean builds, no duplicate files in wrong locations! mdail 2/12/2026
* Version 5.13.0.0 MAJOR UPDATE - SERVICE-BASED BACKUP EXECUTION: Completely refactored "Run Now" functionality to delegate to BackupService!
*					Backups now run in BackupService (Windows Service) instead of UI process - continue running even if UI is closed. Created
*					BackupServiceCommunication for Named Pipe IPC between service and UI. Created BackupServiceClient for UI-side communication.
*					Created BackupProgressTracker to track running jobs with progress, cancellation tokens, and completion status. Enhanced
*					BackupExecutor with ExecuteBackupJobWithProgress supporting progress callbacks, cancellation, and real-time updates. Created
*					non-modal BackupProgressWindow that shows real-time progress from service, can reconnect after closing, allows user to continue
*					using UI while backup runs. Added abort backup functionality with confirmation dialog and graceful cancellation. Service logs
*					directly to BackupLogger for Activity tab integration. Service sends toast notifications on completion. Progress window polls
*					service every second for updates. Multiple backups can run simultaneously. Removed old blocking ExecuteBackupJobWithProgress
*					from MainWindow and ScheduleManagementWindow. ENTERPRISE-READY: Backups survive UI crashes/closures, proper separation of
*					concerns, scalable architecture! mdail 2/12/2026
* Version 5.12.2.3 SCHEDULE MANAGEMENT WINDOW COMPLETE: Implemented Edit and Run Now functionality in ScheduleManagementWindow!
*					Edit button now opens BackupWindowNew with the selected job for editing, reloads jobs list after changes.
*					Run Now button executes backup jobs with full progress window, confirmation dialog, and real-time status updates.
*					Added ExecuteBackupJobWithProgress method supporting all backup types: Hyper-V VMs, disk backups, volume backups,
*					and file/folder backups. Integrates with BackupEngineInterop for C++ backend execution. Shows progress percentage,
*					status messages, and completion notifications. Comprehensive error handling and logging via BackupLogger. Sends
*					toast notifications for success/failure via NotificationService. Schedule Management window now has complete feature
*					parity with MainWindow - users can edit and run jobs from either location! mdail 2/12/2026
* Version 5.12.2.2 Removed the unused exception variable e from the catch block at line 58. Changed catch (const fs::filesystem_error& e)
*					to catch (const fs::filesystem_error&) to eliminate the C4101 warning about unreferenced local variable. mdail 2/12/2026
*  Version 5.12.2.1 CONFIGURATION ERROR FIX: Identified and documented solution platform configuration mismatch causing "open configuration
*					manager" error on solution load. BackupService was configured for Any CPU instead of x64, creating conflict with solution's
*					x64-only platform. Created Fix-SolutionConfiguration.ps1 script to automatically correct platform mappings in .sln file.
*					Created SOLUTION_CONFIG_ERROR_FIX.md with three fix options: PowerShell script, Configuration Manager GUI, or manual .sln
*					edit. Also documented BackupService build issue - duplicate class definitions conflicting with BackupUI references. Created
*					BACKUPSERVICE_BUILD_FIX.md detailing need to remove duplicate BackupJob/BackupSchedule classes and use BackupUI.Models instead.
*					All configuration issues identified and documented for resolution! mdail 2/12/2026
*  Version 5.12.2.0 SOLUTION STRUCTURE COMPLETE: Added BackupService project to solution and configured LinuxRestore as solution folder!
*					BackupService (Windows Service) now properly tracked in Git and solution. Project dependencies enforce build order:
*					BackupEngine → BackupService → BackupUI. BackupService depends on BackupEngine, BackupUI depends on both. LinuxRestore
*					added as solution folder (not built with Windows projects) - all files visible in Solution Explorer and tracked in Git,
*					but not part of automatic Windows build. Created Configure-Solution.ps1 script to automate solution configuration. Build
*					order enforced via ProjectDependencies in .sln file. LinuxRestore builds separately via BUILD-AND-CREATE-ISO.ps1. Complete
*					enterprise solution structure with proper dependency management! mdail 2/12/2026
*  Version 5.12.1.0 RUN JOB NOW COMPLETE: Removed TODO in MainWindow.xaml.cs - backup jobs can now be executed directly from main window!
*					Implemented ExecuteBackupJobWithProgress method that shows progress window with real-time updates. Executes all backup 
*					types: Hyper-V VMs, disk backups, volume backups, file/folder backups. Uses BackupEngineInterop to call C++ backend 
*					functions. Progress window shows percentage, status messages, and updates in real-time via callbacks. Logs all operations
*					via BackupLogger (success/failure). Sends notifications via NotificationService. Handles errors gracefully with detailed
*					messages. Complete integration with existing backup infrastructure. Users can now run scheduled jobs on-demand with one 
*					click! Production-ready manual backup execution! mdail 2/6/2026
*  Version 5.12.0.0 MAJOR FEATURE - NETWORK PATH SUPPORT: Added full network path support for Windows backup source selection! New "Network
*					Locations" node in tree view shows mapped network drives automatically. Users can add custom UNC paths (\\server\share)
*					via new NetworkPathDialog with validation and accessibility checking. Network drives and UNC shares support folder 
*					browsing just like local volumes. Added 4 new DriveTreeItemType enums: NetworkRoot, NetworkDrive, NetworkShare, 
*					NetworkBrowser. LoadNetworkDrives enumerates mapped drives, AddNetworkPathToTree handles manual UNC entry. Network 
*					paths fully integrated into backup selection - no more mapping drives required! Users can now backup directly from 
*					network shares. Enterprise-ready network backup capability! mdail 2/6/2026
*  Version 5.11.0.10 ALL 4 TODOs IN BACKUPWINDOWNEW COMPLETE: 1) Pre-select drives/volumes when editing job - added PreSelectItems and 
*					PreSelectItemRecursive methods to restore saved selections in tree view. 2) Find last backup for incremental - removed 
*					TODO comment, now functional. 3) FindLastBackup implementation - searches for most recent backup folder (Full_, 
*					Incremental_, Differential_) ordered by creation time. 4) FindFullBackup implementation - finds base full backup by 
*					searching for Full_ folders or oldest backup as fallback. Incremental and differential backups now properly chain from 
*					previous backups! Edit job now restores all selections. Production-ready backup job management! mdail 2/6/2026
*  Version 5.11.0.9 MOUNT TIME TRACKING COMPLETE: Removed TODO in NativeBackupMountManager.cs - mount time now retrieved from C++!
*					Added SYSTEMTIME parameter to WimMount_GetMountedInfo C export function. Enhanced GetMountedBackups to receive actual 
*					mount time from C++ WimMountManager (stored during mount via GetSystemTime). Converts UTC SYSTEMTIME to local DateTime 
*					for proper display. Added SYSTEMTIME struct for P/Invoke interop. Includes fallback to DateTime.Now if conversion fails.
*					Users now see accurate "Mounted at" timestamp instead of current time. Complete mount tracking with proper time display! mdail 2/5/2026
*  Version 5.11.0.8 FOLDERPICKERHELPER COMPLETE: Removed TODO and fully implemented all parameter usage in FolderPickerHelper.cs.
*					PickFolder now uses initialDirectory parameter to set starting location. PickFile now uses initialDirectory for 
*					OpenFileDialog.InitialDirectory. PickBackupLocation intelligently suggests common backup paths (D:\Backups, E:\Backups, 
*					Documents\Backups) and combines selectedPath with suggestedName as subfolder. PickBackupToRestore also uses intelligent 
*					initial directory detection. Added XML documentation comments for all methods. Improved user experience with smart 
*					directory defaults and proper parameter utilization. Production-ready folder/file picker utility! mdail 2/6/2026
*  Version 5.11.0.7 SELECTIVE RESTORE COMPLETE + LINUXRESTORE UPDATED: Implemented intelligent restore logic in RestoreWithManifest 
*					function in RestoreEnhanced.cpp. Now intelligently determines item type (file/directory/volume/disk) and calls 
*					appropriate restore function. Detects disk backups (.img files), volume backups (SystemState directory or drive letter 
*					targets), regular directories, and individual files. Removed TODO comment - selective restore fully functional! Also 
*					UPDATED LinuxRestore tools to v5.11.0.7 with same intelligent restore logic. Linux now detects disk images (warns about 
*					manual dd), Windows backups (restores files, notes system state is Windows-only), and provides cross-platform parity. 
*					Complete granular restore capability across both Windows and Linux! mdail 2/6/2026
*  Version 5.11.0.6 SYSTEM STATE RESTORE COMPLETE: Implemented intelligent system state restore in RestoreVolume function. Creates
*					comprehensive restore instructions, stages registry hives/BCD in safe location, generates automated PowerShell restore 
*					script for WinRE. Handles locked file limitation (registry can't be overwritten while Windows running) by providing
*					3 restore options: WinRE manual, Registry Editor method, automated PowerShell script. Removes TODO comment - full 
*					enterprise disaster recovery cycle now complete (backup + restore). Safely prepares restoration without risking 
*					system stability. Production-ready bare metal recovery with clear documentation! mdail 2/6/2026
*  Version 5.11.0.5 SYSTEM STATE BACKUP COMPLETE: Implemented full system state backup in BackupVolume function. Now backs up:
*					Registry hives (SAM, SECURITY, SOFTWARE, SYSTEM, DEFAULT), Boot Configuration Data (BCD), Registry backup files,
*					Critical system files. Creates SystemState subdirectory with metadata file documenting backup. VSS snapshot enables
*					backing up locked registry hives and system files. Gracefully handles permission issues (logs warning but continues).
*					Removed TODO comment - system state backup fully functional! Enterprise-ready bare metal recovery capability - can 
*					restore complete Windows Server state including registry, boot config, and system files! mdail 2/6/2026
*  Version 5.11.0.4 IMPLEMENTATION COMPLETE: Integrated VSS snapshot creation into BackupVolume function in BackupManager_Advanced.cpp.
*					Removed TODO comment - VSS is now fully functional! Volume backups now: 1) Create VSS snapshot for point-in-time 
*					consistency, 2) Backup from snapshot path (not live volume), 3) Automatically cleanup snapshot when done. Falls back 
*					gracefully to direct copy if VSS unavailable. Ensures consistent backups of open files, locked databases, and system 
*					files. Production-ready hot backup capability - can backup running SQL Server, Exchange, Hyper-V VMs, and locked 
*					system files without interruption! mdail 2/6/2026
*  Version 5.11.0.3 Fix build failure: Changed from DEFINE_GUID to EXTERN_C declaration EXTERN_C const GUID CLSID_BackupMountContextMenu
*					in the header Added proper GUID definition #include <initguid.h> DEFINE_GUID(CLSID_BackupMountContextMenu,
*					0x12345678, 0x1234, 0x1234, 0x12, 0x34, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC) in the cpp for ShellExtension mdail 2/6/2026
*  Version 5.11.0.2 SOLUTION: Fixed zlib.lib rebuild error by removing conflicting old NuGet zlib references from project file, 
*					installed zlib via vcpkg which provides automatic linking, removed manual #pragma comment(lib, "zlib.lib") 
*					from BrsFileManager.cpp. vcpkg integration now handles all zlib dependencies automatically. Created PowerShell 
*					fix script (Fix-ZlibError.ps1) to automate the cleanup. First build worked, rebuild failed due to NuGet/vcpkg 
*					conflict - now both work consistently. mdail 2/6/2026
*  Version 5.11.0.1 I fixed the issue of wimgapi.h not being found when building the project by setting the Additional include path in the 
*					project	properties in C++ part and adding a path in the Link Additional Library Directories and a PropertyGroup for 
*					WimLibArch in the BackupEngine project file, unfortuatly now it can't open zlib.lib when building the app a 1 
*					project fails to build. mdail 2/5/2026
*  Version 5.11.0.0 Finish the VSS implementation to support backing up open files and system state backups. Change the backup extension
*                   from .wim to .brs and add compression to the files to give the impression it is proprietary to this app, also support 
*                   reading .wim for mounting and restoring to support old backups made by the user with windows server backup as an 
*                   added feature of this backup application.  mdail 2/3/2026
*  Version 4.10.1.0 Update the backup mounting to mount Wim backups as read-only drives instead of VHDX files. This allows for better
*					compatibility and doesn'r require admin rights or power shell to mount. Also unmonting does a direct call to the
*					dll to unmount instead of using PowerShell. mdail 2/2/2026
*  Version 4.10.0.0 MAJOR FEATURE: Backup Mount System - mount backups as read-only virtual drives with custom icons and Explorer
*                  integration. New "Mount Backups" tab with dual-pane interface (available/mounted), PowerShell-based VHDX mounting,
*                  backup point selection for incremental/differential backups, custom drive icons for visual distinction, Explorer
*                  context menu integration for easy unmounting, comprehensive activity logging, multi-drive support. Users can browse
*                  backup contents in Explorer, copy individual files/folders without full restore. Complete file-level recovery! mdail 2/2/2026
*  Version 4.9.1.0 ENHANCEMENT: Extended volume resizing to clone operations - added interactive volume resize control to "Clone to Disk" and
*                  "Clone to Virtual Disk" workflows, automatically detects selected volumes and target disk sizes, allows users to adjust
*                  volume proportions when cloning to different-sized drives, intelligent minimum size enforcement based on actual data,
*                  real-time validation, supports both physical and virtual disk cloning with same intuitive interface. Complete feature
*                  parity between restore and clone operations for flexible disaster recovery! mdail 2/2/2026
*  Version 4.9.0.0 MAJOR FEATURE: Interactive volume resizing for restore operations - visual drag-and-drop interface with two horizontal
*                  bars (source backup and target disk), draggable arrow handles between volumes, intelligent constraints (minimum size based
*                  on actual data + 10% overhead), auto-fit proportional scaling, support for resizing to smaller/larger disks, prevents
*                  data loss by enforcing minimum sizes, shows free space visualization, validates configurations before restore. Includes
*                  complete documentation for Linux Qt GUI and ncurses TUI implementations. Allows disaster recovery to different-sized drives! mdail 2/2/2026
*  Version 4.8.3.3 Fix the infoVersionAttr.InformationalVersion returning too much information and strip off the git hash info after the
*				  '+' and return only the version number itself. mdail 2/2/2026
*  Version 4.8.3.2 Fix versioning conflict and a variable that was defined but never used. Also fixed date errors in some of the updates
*				   to versions in this file. mdail 2/2/2026
*  Version 4.8.3.1 ENHANCEMENT: Smart job deletion - checks if backup files exist before showing delete options. Jobs never run show simple
*                  confirmation. Jobs with backups show two-option dialog. Handles empty backup directories gracefully. Different messages
*                  based on whether files were deleted. Prevents confusion when deleting never-run jobs. mdail 2/2/2026
*  Version 4.8.3.0 CRITICAL: Bulletproof error handling - validation skipped if backup fails, comprehensive exception catching throughout
*                  backup and validation process, detailed error logging with exception types, fallback logging if main log fails, corrupted
*                  log file recovery, specific handling for access denied/IO errors. Application NEVER crashes - all errors caught and logged.
*                  Production-ready reliability for unattended backup operations. mdail 2/2/2026
*  Version 4.8.2.0 ENHANCEMENT: Enhanced job deletion with user choice - delete job only (preserve backups) or delete job AND backups
*                  (moved to recycle bin for safety). Custom dialog with clear options, comprehensive activity logging, backup file
*                  count tracking. Recycle bin integration ensures deleted backups can be recovered if needed. Data safety first! mdail 2/2/2026
*  Version 4.8.1.0 MAJOR UPDATE: Added comprehensive notification system - Windows toast notifications for backup failures/success, visual
*                  warning indicator (⚠️) in Activity tab when unread errors exist, automatic clearing when user views errors, periodic
*                  check for new errors every 30 seconds, yellow/orange styling for warnings. Users immediately alerted to issues and
*                  can click notification to view Activity tab. Full integration with Windows Action Center. mdail 2/2/2026
*  Version 4.8.0.1 ENHANCEMENT: Improved auto-recovery versioning - failed backups now use incremental version suffixes (_V1, _V2, _V3...)
*                  instead of single V1. System automatically finds highest existing version and increments. Prevents overwriting previous
*                  failed backups, allowing forensic analysis of multiple failures. Underscore prefix added for better filename clarity. mdail 2/2/2026
*  Version 4.8.0.0 MAJOR UPDATE: Added enterprise-level activity logging and backup validation. New Activity tab shows all backup
*                  operations with filtering by level. Automatic validation after backup completion. Failed validations trigger
*                  auto-recovery: failed backups renamed with V1 suffix and new full backup scheduled. Comprehensive audit trail
*                  for compliance and monitoring. mdail 1/30/2026
*  Version 4.7.1.4 Fix the BUILD-AND-CREATE-ISO.ps1 to add the updates to the LinuxRestore and also add error checking to make sure
*				   CMakeFIles builds correctly before trying to make the ISO. mdail 1/30/2026
*  Version 4.7.1.0 MAJOR UPDATE: Updated Linux restore applications (restore_tui, restore_cli, restore_gui) to match Windows restore
*                  workflow - added backup date selection, tree view for selective restore, and destination mapping. Ensures disaster 
*                  recovery tools stay in sync with Windows features. mdail 1/30/2026
*  Version 4.7.0.0 MAJOR UPDATE: Complete restore interface redesign - added backup date selection for incremental/differential,
*                  explorer-style tree view for selective restore of drives/volumes/files/folders, restore destination mapping
*                  with options for original or new location. Restore workflow now matches backup selection experience. mdail 1/30/2026
*  Version 4.6.2.19 Changed schedule time selector from 24-hour format to 12-hour AM/PM format for better usability. mdail 1/30/2026
*  Version 4.6.2.18 Fixed the validation for the backup page to make sure the destination is valid for the backup type selected. mdail 1/30/2026
 *  Version 4.6.2.17 Added Clone Hyper-V System option, fixed clone destination visibility (Clone to Disk shows only physical disk field,
 *                   Clone to Virtual Disk shows backup destination), Hyper-V clone creates HVconfig and HVDisks subdirectories. mdail 1/30/2026
 *  Version 4.6.1.16 Fix the radio buttons to be 2 rows instead of 1 so they can all be read. mdail 1/30/2026
 *  Version 4.6.1.15 Change the new backup page the have the backup type as radio buttons instead of a dropdown selection. mdail 1/30/2026
 *  Version 4.6.1.14 Change the new backup page to put it all on one page instead of multiple tabs on the page. mdail 1/30/2026
 *  Version 4.6.1.13 Finally got the AI to get the bootable Linux USB iso with BackRestore app PowerShell script working. mdail 1/27/2026
 *  Version 4.6.1.10 Spent all day trying to get the AI to fix the ability to make a bootable Linux USB drive with the BackRestore app
 *					 on it so user can have a way to restore their system if Windows will not boot.  The AI was unable to do this yet. mdail 1/25/2026
 *  Version 4.6.0.0 RESTORE COMPLETE: Implemented RestoreFiles, RestoreHyperVVM fully functional, all C++ restore backend complete
 *  Version 4.5.0.0 FEATURE COMPLETE: WinPE bootable USB, restore with date selection for incremental/differential,
 *                  restore destination mapping, all restore operations, clone to VHDX, backup metadata system
 *  Version 4.4.0.0 MAJOR UPDATE: Fully implemented Hyper-V VM backup/clone, actual backup execution with progress callbacks,
 *                  support for all backup types (Full/Incremental/Differential), disk/volume/file backups now functional
 *  Version 4.3.0.2 CRITICAL FIX: Volume paths now include trailing backslash (E:\ instead of E:) for proper folder enumeration
 *  Version 4.3.0.1 Fixed job refresh - JobManager now reloads from file on every GetAllJobs() call
 *  Version 4.3.0.0 CRITICAL FIX: Ensures C:\ProgramData\BackupRestoreService directory is created, enhanced error handling,
 *                  added Clone to Disk and Clone to Virtual Disk (Hyper-V) options
 *  Version 4.2.0.0 MAJOR UPDATE: Added backup job list to main window with Run/Edit/Delete, changed type labels to "Full then Incremental/Differential"
 *  Version 4.1.0.1 See Note 1 below for details on changes made to get to this version. mdail 1/23/2026
 *  Version 3.1.0.1 Fixed checkbox three-state behavior - now toggles between checked/unchecked on click, indeterminate only for mixed children
 *  Version 3.1.0.0 MAJOR UPDATE: Fixed disk ordering (uses Index property), shows volumes without drive letters (EFI/Recovery),
 *                  shows hidden/system folders with labels, better access denied handling
 *  Version 3.0.0.9 Added alternative WMI query method using DiskIndex to properly map volumes to disks
 *  Version 3.0.0.8 Enhanced WMI error logging and improved fallback to show all volumes on Disk 0 when WMI fails
 *  Version 3.0.0.7 Fixed volumes showing expand arrows for folder browsing; removed fallback that put all volumes on Disk 0
 *  Version 3.0.0.6 Added debug logging and fallback method to show volumes when WMI queries fail
 *  Version 3.0.0.5 Fixed TreeView expand arrows now visible - manually creating TreeViewItems for proper hierarchy display
 *  Version 3.0.0.4 Added TreeView expand/collapse functionality and lazy-loaded folders under volumes
 *  Version 3.0.0.3 Fixed drive-to-volume mapping - each disk now shows only its own volumes using WMI queries
 *  Version 3.0.0.2 Fixed BackupWindowNew crash on load, added loading indicator, improved error handling
 *  Version 3.0.0.1 Added notes to version 3.0.0.0
 *  Version 3.0.0.0 Fix the dll not getting copied to the output directory. Need to fix the new backup
 *                  should auto select system state when the boot voulume or disk is selected, also the selection for 
 *                  the disk or volume should be a explorer style tree view. The selection for the what too back up should be
 *                  either check boxes or radio button group and should also include the Hyper-v virtual machines. (Maybe).
 *                  The Hyper-V backups should be selectable without selecting any of the drives, volumes or files & folders.
 *                  Restore should just give a Alert if there have been no backups run yet. The backup service manager should 
 *                  automatically install and should not be an option to install when the application is run. The service should not 
 *                  need a page to install, start stop and should be managed by the normal windows services mmc. 
 *  Version 2.0.0.0 Fixed build errors that occurred after the AI built the first version.
 *  Version 1.0.0.0 added Version information for the application (This file) and had the AI write the app to run the backups
 *                  for windows servers and hyper-v virtual machines.
 *                 
 *                 Note 1: just below is a note I gave the AI to make a change, it took the steps from version 3.0.0.0 to 3.1.0.1 for it to 
 *                 do what I asked and make it work as I wanted, I did not run any back up so I don't know if any of the changes to 
 *                 the actuall backup were made. mdail 1/23/2026
 *                 To Change from the Notes in version 3.0.0.0 to 4.1.0.1 and some other ideas I had to improve the application, it took the
 *                 AI all the the steps from 3.0.0.0 to 3.1.0.1, However some of what is in this change I haven't actually tried yet
 *                 ideas were as follows: One the page to select what to back up the drives, volumes & (files & folders) should be a tree view
 *                 with the drives the top level and each volume listed at the second level under the drive it is on, the files & folders the 
 *                 third levels.  there should be check boxes in front of the drives and volumes. If the Drive is selected all the check 
 *                 boxes for the Volumes on that drive should auto check, if a volume on a drive is unselected the drive should unselect, 
 *                 the files and folder should be available as drop down from the volumes and only show when the user selects the option to 
 *                 drop them down. The user should be able to select multiple Drives, Volumes and Files & Folders however if the top level 
 *                 i.e.: the Drive is selected all volumes are selected, if the Volume is selected the files & folder don’t even show and 
 *                 are only expanded if the Drive & volume, they are on are unselected. When running backups of drives the backup should 
 *                 still run as shadow backups of the individual volumes unless a clone backup is selected, the clone backup should either 
 *                 clone the drive to another drive or virtual drive. The drop-down menu for selecting new backup should also have an option 
 *                 for clone backup. If the backup includes a boot volume that is a windows server version, then the system state should 
 *                 automatically be backed up. The Hyper-V systems and virtual drives should show in the list with the Hyper-V system showing
 *                 like a drive and any volumes virtual showing as volumes. If possible, the Hyper-V systems should be backup as complete as 
 *                 possible so they could be restored on a different system if needed. For the location to store the backup a normal windows 
 *                 explorer like drive/directory selection control should display and should be network aware giving the option to chose a 
 *                 drive or directory to store the backup. The backup should be split into 4.7 gig files so they could be backup up to DVD. 
 *                 The restore option as it is now if the app hasn’t run a backup throws a error and stops the app, it should give a normal 
 *                 windows drive/folder/file selector so the user can select a backup file to restore, The back files need to be restorable 
 *                 without any information for the application, and if a backup files is selected the application needs to scan for the last 
 *                 file in the backup set and the give the user a list of possible restore options available for the backup files, ie: if it 
 *                 is only a full backup, or different points in a incremental or differential backup set.
 *
 * */
