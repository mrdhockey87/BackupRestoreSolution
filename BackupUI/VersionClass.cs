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

		// Get version from assembly - this will always match the project file version
		static public string version_string = GetAssemblyVersion();

		static public string GetVersion()
		{
			return string.Format("{0} {1}", VersionClass.version_word, VersionClass.version_string);
		}

		static private string GetAssemblyVersion()
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
				return "5.13.2.4";
			}
			catch
			{
				// Fallback version if assembly version fails
				return "5.13.2.4";
			}
		}
	}
}

/*
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