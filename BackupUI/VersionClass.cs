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
				return "4.8.3.3";
			}
			catch
			{
				// Fallback version if assembly version fails
				return "4.8.3.3";
			}
		}
	}
}

/*
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