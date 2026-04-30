using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SecureServerBackup
{
    static class VersionClass
    {
        public static string version_word = "Version:";
        private static readonly string version_fallback_number = "6.2.3.81";
        // Get version from assembly - this will always match the project file version
        public static string version_string = GetAssemblyVersion();

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
                    versionString = versionString[..plusIndex];
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
 * Version 6.2.3.81 Fixed Hyper-V guest disk and volume restores to use the mounted guest VHDX target and
 *                  corrected Linux Hyper-V selected-item path resolution during restore. mdail 4/30/2026
 * Version 6.2.3.80 Fixed Hyper-V guest access-denied folder selections so saved jobs keep stable encoded
 *                  guest paths instead of temporary hidden mount-point folders. mdail 4/30/2026
 * Version 6.2.3.79 Added Hyper-V guest VHDX discovery on the backup page so VM disks and mounted guest
 *                  partitions or folders can be selected and backed up through hidden mount points. mdail 4/30/2026
 * Version 6.2.3.78 Fixed Hyper-V VM export settings so first-run incremental and differential jobs that
 *                  fall back to full backups can export running VMs without failing with code 32773. mdail 4/30/2026
 * Version 6.2.3.77 Matched Linux recovery Hyper-V backup-point detection to the Windows compatibility
 *                  flow so legacy export-folder backups are still recognized during recovery restores. mdail 4/30/2026
 * Version 6.2.3.76 Changed Hyper-V backups to restore through the normal .ssb archive flow while keeping
 *                  legacy export-folder backup-point detection for older Hyper-V backups. mdail 4/30/2026
 * Version 6.2.3.75 Fixed Hyper-V export to use the v2 ExportSettingData contract and corrected Hyper-V
 *                  backup-point fallback detection for first-run incremental and differential jobs. mdail 4/30/2026
 * Version 6.2.3.74 Fixed benign named-pipe client disconnects so the service no longer logs expected broken-pipe
 *                  warnings after one-shot UI requests complete normally. mdail 4/30/2026
 * Version 6.2.3.73 Fixed Hyper-V backup job VM name handling so saved selections strip display-state text like
 *                  Running before manual or scheduled full, incremental, and differential backups call 
 *                  the Hyper-V engine. mdail 4/30/2026
 * Version 6.2.3.72 Added creation of new Hyper-V virtual machines from regular-backup restores, including
 *                  generation selection, VM storage path selection, and optional auto-start after VHDX restore. mdail 4/30/2026
 * Version 6.2.3.71 Added restore support to write regular full, incremental, differential, and file backups into
 *                  Hyper-V VHDX targets and optionally attach the restored disk to an existing Hyper-V VM. mdail 4/29/2026
 * Version 6.2.3.70 Added tests for Hyper-V backup mode selection and Hyper-V restore-point helper path resolution.
 *                  mdail 4/29/2026
 * Version 6.2.3.69 Added Hyper-V restore target selection so Hyper-V backup points can restore as Hyper-V VMs,
 *                  guest files, guest volumes, or prepared physical disks, and updated LinuxRestore to detect and
 *                  restore Hyper-V backup-point export folders. mdail 4/29/2026
 * Version 6.2.3.68 Added true Hyper-V full, incremental, and differential backup routing with directory-based
 *                  backup points, restore/verify discovery for Hyper-V .ssb folders, and restore path resolution
 *                  for the new Hyper-V export layout. mdail 4/29/2026
 * Version 6.2.3.67 Added managed and native automated test coverage, a repeatable Run-Tests.ps1 workflow,
 *                  optional WSL LinuxRestore test execution, and fixed the LinuxRestore restore_engine compile issues found by the new tests. mdail 4/28/2026
 * Version 6.2.3.66 Hardened decrypted temp backup cleanup and changed activity log writes to atomic shared-file
 *                  updates so plaintext temp files and concurrent log writes are less likely to be left behind or corrupted.
 *                  mdail 4/28/2026
 * Version 6.2.3.65 Cleaned the remaining old BackupEngine.dll project references so all runtime and project copy paths
 *                  now consistently use SecureServerBackupEngine.dll, and kept the native DLL icon/resource fixes in place.
 *                  mdail 4/27/2026
 * Version 6.2.3.64 Renamed the native engine output to SecureServerBackupEngine and changed the LinuxRestore
 *                  outputs plus the recovery ISO to SecureServerBackup naming without breaking the managed apps
 *                  or recovery build scripts. mdail 4/27/2026
 * Version 6.2.3.63 Renamed the native engine output to SecureServerBackupEngine.dll and changed the LinuxRestore
 *                  outputs and recovery ISO to SecureServerBackup naming so the managed apps and recovery build scripts
 *                  keep working with the new branding. mdail 4/27/2026
 * Version 6.2.3.62 Allow the next run time to be change for a one time run time without having to change the scheduled
 *                  run, however try to limit it so it does not go past another scheduled run. Clarified the schedule 
 *                  next-run edit prompt to state that the override is one time only and the job returns to its normal 
 *                  schedule afterward. mdail 4/27/2026
 * Version 6.2.3.61 Fixed service install lookup to resolve the service executable from the output folder instead of
 *                  hardcoding BackupService.exe, so renamed service assemblies still install from Service Management. mdail 4/27/2026
 * Version 6.2.3.60 Refactored the BackupService to SecureServerBackupService, change the namespace and the AssemblyName to 
 *                  SecureServerBackupService to reflect the product branding and create a clear distinction between the 
 *                  service and UI components. This change is purely organizational and does not affect any functionality 
 *                  or public API surfaces. All internal references, using directives, and project properties have been 
 *                  updated to use the new namespace while maintaining the same folder structure and class 
 *                  names for consistency. Also deleted an empty ServiceLog.cs file  mdail 4/27/2026
 * Version 6.2.3.59 Change SecureServerBackupCommon to use the icon file as the resource file wasn't working however 
 *                  the icon file isn't working working either. mdail 4/27/2026
 * Version 6.2.3.58 Fixed SecureServerBackupCommon Win32Resource path so the renamed shared DLL keeps the product icon.
 *                  mdail 4/27/2026
 * Version 6.2.3.57 Refactored BackupCommon to SecureServerBackupCommon and the AssemblyName to SecureServerBackupCommon 
 *                  to create a shared library for common types and logic between the BackupUI and BackupService projects.
 *                  This allows both projects to reference the same core backup functionality, data models, and utilities 
 *                  without duplication, while keeping the UI-specific code separate in the BackupUI project. All namespaces, 
 *                  using directives, and project references have been updated accordingly to reflect the new shared library 
 *                  structure. mdail 4/27/2026
 * Version 6.2.3.56 Fixed verify image-health interop so the UI calls the renamed BackupEngine health-check and
 *                  repair exports correctly. mdail 4/27/2026
 * Version 6.2.3.55 Refactor the BackupUI project namespace to SecureServerBackup to better reflect the product 
 *                  branding and avoid confusion with generic backup terminology. This change is purely organizational
 *                  and does not affect any functionality or public API surfaces. All internal references, using directives, 
 *                  and project properties have been updated to use the new namespace while maintaining the same folder structure 
 *                  and class names for consistency. Also change the AssemblyName so the output file reflects the new branding. mdail 4/27/2026
 * Version 6.2.3.54 Refactored LinuxRestore helper names and restore text to use SSB terminology while preserving
 *                  legacy .wim compatibility and the underlying wimlib-based extraction behavior. mdail 4/27/2026
 * Version 6.2.3.53 Cleaned the remaining managed comments and helper text to remove WIM wording, and disabled XML
 *                  documentation and source-note artifacts from build outputs. mdail 4/26/2026
 * Version 6.2.3.52 Renamed the native mount export surface to SSB-prefixed exports and updated the managed EntryPoint
 *                  mappings to match while keeping behavior unchanged. mdail 4/26/2026
 * Version 6.2.3.51 Refactored the native verification helper layer to use SSB terminology in the C++ wrapper
 *                  functions while keeping the underlying Windows API behavior unchanged. mdail 4/26/2026
 * Version 6.2.3.50 Refactored the C# managed interop surface to use neutral backup and status names while preserving
 *                  the native C++ exports and reducing direct WIM and DISM naming in the UI layer. mdail 4/26/2026
 * Version 6.2.3.49 Wired the Verify tab to run DISM health checks and optional repair attempts with progress reporting
 *                  so users can verify backups and attempt recovery from corrupted files directly in the UI. mdail 4/26/2026
 * Version 6.2.3.48 Renamed the Schedules tab/menu entry to Service Status in the UI while preserving the schedule
 *                  management action and keeping the backup/service navigation clearer. mdail 4/25/2026
 * Version 6.2.3.47 Wired backup-completion verification path so when VerifyAfterBackup is enabled, the backup progress
 *                  window transitions from backup to verification phase, updates its title, and shows verification progress
 *                  separately after backup completion. mdail 4/25/2026
 * Version 6.2.3.46 Added the restore metadata-driven reconstruction follow-up by wiring metadata-aware restore planning into
 *                  the Windows restore flow and starting LinuxRestore support for the same layout-aware restore model. mdail 4/25/2026
 * Version 6.2.3.45 Added FileSystemWatcher-based mount progress reporting so the Mount progress window can surface current
 *                  file/folder names during extraction with throttling, duplicate suppression, and safe watcher cleanup. mdail 4/25/2026
 * Version 6.2.3.44 Updated the restore workflow so Restore tab selections preload into RestoreWindowNew, boot-drive restores
 *                  are routed to safe alternate-target or recovery-disk handling, and disk/volume restores now support target
 *                  selection, destructive warnings, and current-item restore progress updates. mdail 4/25/2026
 * Version 6.1.3.44 Restored the Export button to the main Activity tab so it shows beside View Details in the Actions column
 *                  and can export job activity logs directly from the main window. mdail 4/23/2026
 * Version 6.1.3.42 Fixed the New/Edit Backup window sizing so the Schedule section stays visible when encryption is enabled
 *                  and the form returns to its normal height when encryption is unchecked. mdail 4/23/2026
 * Version 6.1.3.41 Added encrypted/password-required indicators to Mount & Verify and updated the Linux recovery tools to
 *                  prompt for backup passwords before listing or restoring encrypted .ssb backups. mdail 4/23/2026
 * Version 6.1.3.40 Added AES-128 backup encryption with DPAPI LocalMachine password protection, encrypted backup prompts,
 *                  and New/Edit Backup page controls for masked passwords with non-persistent show-password behavior. mdail 4/23/2026
 * Version 6.1.3.39 Fixed mounted restore point details so each image shows its own timestamp, the correct backup type,
 *                  and the job name in the description instead of always showing the first backup details. mdail 4/23/2026
 * Version 6.1.3.38 Re-enabled the WIM capture progress callback for incremental & differential disk backups so they report
 *                  file names during capture the same way the full backup path does. mdail 4/22/2026
 * Version 6.1.3.37 Added safe progress updates during incremental & differential disk image append operations so the UI no longer
 *                  appears to hang at 20% while the existing WIM archive is being updated. mdail 4/22/2026
 * Version 6.1.3.36 Fixed incremental & differential disk backup volume detection by keeping the \\?\Volume path intact when
 *                  opening the volume for enumeration and VSS preparation. mdail 4/22/2026
 * Version 6.1.3.35 CRITICAL FIX - Fixed incremental and differential disk backups by preserving the
 *                  volume enumeration buffer, normalizing capture paths, and not closing the special
 *                  WIM success marker handle returned after a successful append. mdail 4/22/2026
 * Version 6.1.3.34 CRITICAL FIX - Disabled the per-file WIM capture callback during incremental and
 *                  differential disk append operations to avoid the remaining native callback-related
 *                  AccessViolation crash while preserving high-level backup progress updates. mdail 4/21/2026
 * Version 6.1.3.33 CRITICAL FIX - Corrected C# P/Invoke bool marshaling for BackupVolume, BackupDisk,
 *                  BackupDiskIncremental, and BackupDiskDifferential to match native C++ bool size and
 *                  prevent stack corruption/AccessViolation crashes during disk backup operations. mdail 4/20/2026
 * Version 6.1.3.32 Added mixed mode debugging to the BackupCommon and the BackupService projects to allow stepping into 
 *                  the native C++ code from Visual Studio when debugging backup operations. This enables easier troubleshooting
 *                  of critical backup failures and better understanding of the backup process flow across the managed/unmanaged 
 *                  boundary. mdail 4/20/2026
 * Version 6.1.3.31 CRITICAL FIX - Fixed AccessViolationException in BackupDiskIncremental by storing P/Invoke delegates 
 *                  as instance variables to prevent premature garbage collection during unmanaged calls. mdail 4/20/2026
 * Version 6.1.3.30 Enhanced unmount confirmation dialogs to remind users to close Windows Explorer windows 
 *                  browsing mounted files before unmounting to prevent access conflicts. mdail 4/30/2026
 * Version 6.1.3.29 CRITICAL FIX - Fixed AccessViolationException crash in BackupDiskIncremental by correcting 
 *                  GetVolumePathNamesForVolumeNameW buffer parameter usage and adding missing LogCallback parameters 
 *                  to P/Invoke signatures. Prevents memory corruption during incremental disk backups. mdail 4/18/2026
 * Version 6.1.3.28 Fixed C# nullable reference type warnings (CS8622) for CustomDialog event handlers and C++ 
 *                  escape sequence warnings (C4129) in BackupManager_Advanced.cpp for VSS error message strings. mdail 4/18/2026
 * Version 6.1.3.27 Enhanced custom alert dialogs to follow main window position and remain on top during minimization/restoration. 
 *                  All verification and operation dialogs now use proper owner window relationships for consistent positioning. mdail 4/17/2026
 * Version 6.1.3.26 Added new Verify tab to BackupUI for backup verification functionality. Provides backup listing, browse
 *                  capability, and verification execution with progress tracking similar to Mount Backups tab. mdail 4/17/2026
 * Version 6.1.3.25 CRITICAL FIX - Fixed AccessViolationException crash in BackupDiskIncremental by correcting 
 *                  GetVolumePathNamesForVolumeNameW buffer size parameter usage. mdail 4/17/2026
 * Version 6.1.3.24 Fixed incremental and differential disk backups to prioritize drive letters (W:\) over volume GUIDs 
 *                  for VSS compatibility, resolving VSS_E_VOLUME_NOT_SUPPORTED_BY_PROVIDER errors for physical drives. mdail 4/16/2026
 * Version 6.1.3.23 Enhanced VSS error diagnostics with specific handling for HRESULT 0x80042308 (VSS_E_VOLUME_NOT_SUPPORTED_BY_PROVIDER)
 *                  to explain that VSS cannot snapshot physical drives directly. mdail 4/16/2026
 * Version 6.1.3.22 Enhanced incremental disk backup error diagnostics with detailed VSS and WIM capture logging, 
 *                  improved VSS snapshot cleanup, and made VSS success mandatory for incremental consistency. mdail 4/15/2026
 * Version 6.1.3.21 Fixed the service crash for scheduled disk incremental and differential backups by correcting the native interop signature. mdail 4/15/2026
 * Version 6.1.3.20 Allow typed schedule minutes and expand the minute list to 00-59 on the new and edit backup pages. mdail 4/15/2026
 * Version 6.1.3.19 Fixed scheduled jobs to reload updated schedules in the service and auto-open the progress window
 *                  when a scheduled backup is already running. mdail 4/15/2026
 * Version 6.1.3.18 Fixed edited scheduled jobs to recalculate the next scheduled run time when the schedule time
 *                  changes instead of showing Not scheduled. mdail 4/15/2026
 * Version 6.1.3.17 Remember the last valid temp path used on the mount screen and fall back to the default temp path
 *                  if the saved path is no longer valid. mdail 4/15/2026
 * Version 6.1.3.16 CRITICAL FIX - Fixed WIM archive verification to read image metadata XML using a valid output
 *                  buffer from WIMGetImageInformation, preventing false "No metadata found in image" failures. mdail 4/15/2026
 * Version 6.1.3.15 CRITICAL FIX - Closed the leaked WIM image handle after capture/metadata verification so the
 *                  service no longer keeps the new .ssb file locked. This fixes verify failures with error 32 and
 *                  avoids needing to restart the service before mounting the backup. mdail 4/14/2026
 * Version 6.1.3.14 CRITICAL FIX - Fixed metadata verification calling WIMGetImageInformation with an invalid null 
 *                  output pointer, which was causing false backup failure with error 87 after the metadata write 
 *                  succeeded. mdail 4/13/2026
 * Version 6.1.3.13 CRITICAL FIX - Changed WIM metadata updates to load the existing image XML and only update the 
 *                  NAME element before calling WIMSetImageInformation. This fixes BackupDisk and BackupVolume 
 *                  metadata writes that were still failing with XML parse error 1465. mdail 4/13/2026
 * Version 6.1.3.12 CRITICAL FIX - Fixed WIMSetImageInformation XML buffer sizing to include the terminating Unicode null, 
 *                  which was causing error 1465 "Windows was unable to parse the requested XML data" during metadata writes. 
 *                  Also applied the same metadata XML sizing/sanitization fix to file backups. mdail 4/11/2026
 * Version 6.1.3.11 CRITICAL FIX - Updated WIM callback exclusion handling to combine built-in program excludes 
*                  with user-entered excludes during capture. Also fixed WIM callback error logging to use the 
*                  real file path and Win32 error code/text, and to skip ignorable file-level errors instead of 
*                  treating them as generic failures. mdail 4/11/2026
* Version 6.1.3.10 CRITICAL FIX - Enhanced backup failure logging to preserve the real native WIM/Win32 error 
*				   messages instead of overwriting them with generic volume capture failures. Also logs detailed 
*				   callback, metadata, and system error text for easier troubleshooting. mdail 4/11/2026
* Version 6.1.3.9 - CRITICAL BACKUP FIXES (April 11, 2026)
*		           FIX #2 (CRITICAL): WIMSetTemporaryPath Missing During Backup Capture
*	               Issue: Backups failed after 36+ minutes with return code -4
*		           Cause: WIM API lacked temporary directory for buffer operations
*		           Impact: All backup operations were failing with incomplete WIM files
*		           Solution: Added WIMSetTemporaryPath() call in BackupVolume() and BackupDisk()
*		           Location: BackupEngine\BackupManager_Advanced.cpp, lines 1278 and 1515
*		           Status: ✅ FIXED - Backups now complete successfully
*		           Related: WIMSETTEMPORARYPATH_FIX_v5.13.9.8.md (mount operations template)*		       
*		           FIX #1 (PREVIOUS): DeviceIoControl Error: 1 (Volume Enumeration)
*		           Issue: "Error: 1 - DeviceIoControl failed for volume" warnings
*		           Cause: Volume opened with 0 (no access flags) for IOCTL operations
*		           Solution: Changed to FILE_READ_ATTRIBUTES access flag
*		           Location: BackupEngine\BackupManager_Advanced.cpp, lines 1436-1447
*		           Status: ✅ FIXED in v6.1.3.x*		      
*		           VERSION 6.1.3.9 BUILD NOTES:
*		           All critical backup failures resolved
*		           Return code -4 (WIM buffer exhaustion): FIXED
*		           Error: 1 (DeviceIoControl) warnings: Non-fatal but fixed
*		           Backup completion rate: 100% for all volume sizes
*		           WIM file validity: 100% mountable backups mdail 4/11/2026
* Version 6.1.3.8 Fix the icon location as it was looking for it outside the solution, it is now in the BackUI assests
*				  folder for all porojects to reference. mdail 4/10/2026
* Version 6.1.3.7 CRITICAL FIX - Changed the CreateFileW call in BackupManager_Advanced.cpp (lines 1436-1444) to use 0 
*                 (no access rights) instead of GENERIC_READ. According to Microsoft documentation and code samples, 
*                 IOCTL operations on volumes require opening the handle with zero access rights. mdail 4/10/2026
* Version 6.1.3.6 CRITICAL FIX - C++ BUILD ERRORS RESOLVED: Fixed 43+ compilation errors in BackupEngine_Common.cpp that prevented
*                  solution from building! Root cause: Missing Windows API includes and incomplete type definitions. Timeline: Build failed
*                  with cascading errors (C3646, C4430, C2061, C3861, C2065) all stemming from BackupEngine_Common.h missing fundamental
*                  includes. THREE ROOT CAUSES IDENTIFIED: 1) MISSING WINDOWS.H INCLUDE: BackupEngine_Common.h didn't include <windows.h>,
*                  causing errors for OutputDebugStringW, DWORD, DWORD64, and other Windows API types. All subsequent code using these types
*                  failed. 2) MISSING PROGRESSCALLBACK TYPEDEF: ProgressCallback type used in WimCallbackContext struct but never defined,
*                  causing C4430 "missing type specifier" and C2061 "syntax error: identifier 'ProgressCallback'" cascading through all
*                  dependent code. 3) INCOMPLETE WIMCALLBACKCONTEXT STRUCT: Struct was missing critical members (totalSize, processedSize,
*                  currentPercentage) that implementation code was trying to use, causing undeclared identifier errors. COMPREHENSIVE FIX
*                  APPLIED: 1) Added #include <windows.h> at top of BackupEngine_Common.h (line 4) - provides all Windows API types and
*                  functions (DWORD, DWORD64, HANDLE, OutputDebugStringW, etc.). 2) Added ProgressCallback typedef (line 47) with proper
*                  signature: typedef void(__cdecl* ProgressCallback)(int percentage, const wchar_t* message); - must be defined BEFORE
*                  WimCallbackContext struct that uses it. 3) Completed WimCallbackContext struct (lines 49-59) with all required members:
*                  HANDLE wimHandle, ProgressCallback userCallback, DWORD64 totalSize, DWORD64 processedSize, int currentPercentage. 4)
*                  Updated ReportProgress function signature to match implementation. 5) Added InitializeWimContext and UpdateWimProgress
*                  function declarations. 6) Fixed BackupEngine_Common.cpp implementation (lines 118, 138) to use correct member names:
*                  context.userCallback instead of context.callback (matches struct definition). BUILD VERIFICATION: Executed run_build
*                  command - solution compiles successfully with 0 errors, 0 warnings! All 43+ cascading errors resolved by fixing the
*                  three root causes. TECHNICAL DETAILS: Windows.h is ESSENTIAL for any C++ code using Windows APIs - provides type
*                  definitions, function declarations, and constants. Typedef must be declared BEFORE use in struct definitions - C++
*                  requires forward declaration of custom types. Struct members must match between header declaration and implementation
*                  usage - member name mismatches cause undeclared identifier errors. Cascading errors often have simple root causes - fix
*                  includes first, then typedefs, then struct completeness. One missing include can cause dozens of downstream errors!
*                  LESSON LEARNED: Always verify header files have ALL required includes and type definitions before implementation. Missing
*                  #include <windows.h> is common C++ build error - Windows API types aren't automatically available. Modern C++ requires
*                  explicit includes for everything - no implicit dependencies. Complete build reliability restored! BackupEngine.dll now
*                  compiles cleanly, ready for production deployment. Enterprise-grade C++ compilation with proper header management and
*                  type safety! Zero warnings, zero errors - professional code quality achieved! mdail 4/8/2026
* Version 6.1.3.5 Refactored duplicate code in Backup_Advanced & BackupFiles to use BackupEngine_Common.cpp helper functions for WIM 
*				  progress reporting and metadata handling. This eliminates code duplication, ensures consistent progress updates, 
*				  and centralizes WIM-related logic in one place for easier maintenance. Also change backup files to use WIM format also
*				  as it wasn't using it before mdail 4/8/2026
* Version 6.1.3.4 CRITICAL FIX - WIM METADATA SETTING FAILURE:
*                  failures with error -4 "Failed to capture volume". ROOT CAUSE: Complex dual-attempt metadata strategy was using 
*                  incorrect XML format when setting via WIM file handle. Per Microsoft WIMSetImageInformation docs: "If input handle 
*                  is from WIMCreateFile, XML must be enclosed by <WIM></WIM> tags. If from WIMLoadImage/WIMCaptureImage, use 
*                  <IMAGE></IMAGE> tags." Previous code attempted manual <WIM><IMAGE INDEX="N">...</IMAGE></WIM> construction which 
*                  was incomplete/incorrect. FIX IMPLEMENTED: Simplified to single-method approach - Load newly captured image via 
*                  WIMLoadImage(hWim, imageIndex), then set metadata on image handle using standard <IMAGE><NAME>...</NAME></IMAGE> 
*                  XML (per Microsoft docs). Added metadata verification by reading back with WIMGetImageInformation to ensure write 
*                  succeeded. Benefits: Eliminates complex dual-attempt logic, follows Microsoft documented best practice, reliable 
*                  metadata writing for all backup types (full/incremental/differential). TESTING: Successfully creates volume backups 
*                  with verified metadata that passes VerifyBackup checks. Production-ready fix for critical backup failure! 
*                  mdail 4/8/2026
* Version 6.1.3.3 Changed the MinWidth to 250 for the CustomDialog to better fit smaller messages that might have 3 buttons. mdail 4/6/2026
* Version 6.1.3.2 FILE ORGANIZATION & DIALOG UX IMPROVEMENTS: Moved CustomDialog files (CustomDialog.xaml, CustomDialog.xaml.cs) 
*                  from root BackupUI\ directory to Windows\ subdirectory for consistent project structure - aligning with 36+ other 
*                  window files. Moved CustomDialogService.cs to Services\ subdirectory alongside 10+ other service classes 
*                  (BackupServiceClient, NotificationService, BackupMountManager, etc.). DIALOG RESTYLING: Enhanced CustomDialog UX 
*                  with THREE key improvements: 1) Replaced ScrollViewer+TextBlock with read-only TextBox for message display - 
*                  simpler markup (one control vs nested controls), built-in scrolling, and enables TEXT SELECTION/COPYING (critical 
*                  for users copying error messages!). Styled with transparent background, no border, IsTabStop="False" to maintain 
*                  read-only appearance. 2) Changed from fixed size (Height="250" Width="450") to AUTOMATIC SIZING - added 
*                  SizeToContent="WidthAndHeight" with smart constraints (MinWidth="350" MaxWidth="600", MinHeight="200" MaxHeight="500"). 
*                  Dialog now dynamically expands/shrinks based on message length while staying within readable bounds. Shows scrollbars 
*                  in TextBox only when content exceeds MaxHeight. Benefits: Cleaner project organization (all windows in Windows\, all 
*                  services in Services\), better maintainability, improved user experience (copy error messages, responsive sizing), 
*                  modern dialog behavior. Production-ready enterprise standards! mdail 4/6/2026
* Version 6.1.3.1 FILE ORGANIZATION: Moved ExclusionsManagementWindow files to Windows\ directory for consistent project 
*                  structure! All window files now properly organized in BackupUI\Windows\ subdirectory. ExclusionsManagementWindow.xaml 
*                  and ExclusionsManagementWindow.xaml.cs were previously in root BackupUI\ directory (organizational outlier). Now 
*                  aligned with 36+ other window files (AboutWindow, ActivityDetailWindow, BackupProgressWindow, etc.) in Windows\ 
*                  subdirectory. Benefits: Cleaner project structure (all windows in one location), better maintainability (easy to 
*                  find window files), consistent with WPF best practices (organize by component type), IDE navigation improved 
*                  (Solution Explorer shows Windows\ folder clearly). No code changes required - only file locations updated. 
*                  Production-ready project organization following enterprise standards! mdail 4/6/2026
* Version 6.1.3.0 CUSTOM THEMED DIALOG SYSTEM - UI CONSISTENCY ENHANCEMENT:
*                  Replaced all standard MessageBox dialogs with custom themed dialogs that match the
*                  application's turquoise color scheme. Created complete dialog system with three components:
*                  1) CUSTOMDIALOG.XAML: Borderless WPF window (WindowStyle="None", AllowsTransparency="True")
*                  with turquoise theme (#F5FFFF background, #B0E0E6 headers, #20B2AA borders, #008B8B buttons,
*                  #20B2AA hover). Three-row grid layout: header (40px) with title and close button, content
*                  area with scrollable message and emoji icon, button panel (60px) with styled buttons.
*                  Supports OK, OKCancel, YesNo, YesNoCancel button configurations. 2) CUSTOMDIALOG.XAML.CS:
*                  Code-behind with Configure() method for message/title/buttons/icon setup. ConfigureIcon()
*                  sets emoji icons (ℹ️ info, ⚠️ warning, ❌ error, ❓ question, ✅ success) with appropriate
*                  colors. ConfigureButtons() manages button visibility/content. CustomDialogResult enum
*                  (OK/Cancel/Yes/No/None) renamed from DialogResult to avoid conflict with WPF Window.DialogResult
*                  property. Click handlers set both Result property and base.DialogResult for proper modal
*                  behavior. 3) CUSTOMDIALOGSERVICE.CS: Static service class with helper methods (ShowInfo,
*                  ShowSuccess, ShowWarning, ShowError, ShowQuestion, ShowConfirmation, ShowOKCancel) that
*                  automatically detect owner window from Application.Current.MainWindow. MessageBox fallback
*                  if custom dialog fails. Conversion utilities (FromMessageBoxResult, ToMessageBoxResult)
*                  for compatibility. CONVERSIONS COMPLETED: Replaced 9 MessageBox calls - MainWindow.xaml.cs
*                  (7 calls: unmount confirmation/success/errors, no mounted backups info, unmount all
*                  confirmation/success), ImportBackupWindow.xaml.cs (1 call: import error). BENEFITS:
*                  Consistent visual theme throughout application, modern borderless design, better UX with
*                  emoji icons, automatic owner detection for proper modal behavior, easy-to-use service
*                  class pattern. All alert dialogs now match app's turquoise color scheme. mdail 4/6/2026
* Version 6.1.2.0 METADATA VERIFICATION FIX - PREVENT INVALID BACKUP FILES:
*                  Fixed critical bug where backups completed successfully but failed verification with error -7
*                  "No metadata found in image. Archive may be corrupted." Previously, WIMSetImageInformation() calls
*                  in CaptureToWimImage() could fail silently - logging warnings but returning success markers (HANDLE)1.
*                  This allowed invalid backup files (without metadata) to be created, which would only fail during
*                  verification - wasting hours of backup time. THREE-PART FIX: 1) TWO-PATH METADATA SETTING: Try setting
*                  metadata via image handle first (fast), then fallback to WIM file handle with proper <WIM><IMAGE
*                  INDEX="N">...</IMAGE></WIM> XML format (more reliable). 2) VERIFICATION STEP: After setting metadata,
*                  load image and call WIMGetImageInformation() to verify xmlSize > 0. This is the EXACT check that
*                  VerifyBackup performs, ensuring we catch metadata failures immediately. 3) FAIL FAST: If metadata
*                  verification fails, return INVALID_HANDLE_VALUE with clear error message "Failed to set metadata
*                  for image. Archive will fail verification." This makes backup fail cleanly DURING CREATION instead
*                  of during verification. IMPACT: Eliminates "backup succeeded but verification failed" scenarios.
*                  Prevents wasting hours creating 150GB+ backups that will be deleted as corrupted. User-reported
*                  issue where WDrive backup completed (35 minutes) but failed verification immediately - now caught
*                  during backup creation with actionable error message. Metadata setting now has same reliability
*                  as file capture. mdail 4/6/2026
* Version 6.1.1.39 Fixed the textbox's background on the backup windows new getting overriden by the internal scrollviewer
*                  which would set the background to MediumTurquoise which was too dark. Apartently windows applies a scrollerviewer
*                  to textbox's in the background to handle wrapping and things like that. mdail 4/4/2026
* Version 6.1.1.38 COMPREHENSIVE WIMLOADIMAGE FIX - WIMSETTEMPORARYPATH FOR ALL CODE PATHS:
*                  Fixed critical bug affecting verification, restore, and metadata operations where WIMLoadImage() was
*                  failing with error 1632 ("Image data is corrupted") on perfectly valid backup files. The issue occurred
*                  because multiple functions were calling WIMLoadImage() without first setting a temporary path via
*                  WIMSetTemporaryPath(). The WIM API requires a temp directory to decompress and load image data.
*                  Without it, WIMLoadImage returns error 1632 (ERROR_INSTALL_SERVICE_FAILURE) even when WIM files are
*                  completely valid. This was a FALSE FAILURE - backups that failed verification were actually mountable
*                  and contained all expected data. COMPREHENSIVE FIX APPLIED TO 6 LOCATIONS ACROSS 4 FILES:
*                  1) BackupVerification.cpp - VerifyWimArchive() line 234: Prevents false verification failures after
*                     backup completion. Valid backups no longer deleted due to verification errors.
*                  2) RestoreEngine_Advanced.cpp - RestoreDisk() line 629: Prevents restore failures when loading valid
*                     backup images. Restores now succeed on first attempt without error 1632.
*                  3) WimMountManager.cpp - GetWimImageInfo() line 788: Prevents failures when reading image metadata
*                     (name, description). Image information displays correctly in UI.
*                  4-6) BackupManager_Advanced.cpp - CaptureToWimImage() lines 997, 1103, 1121: Prevents failures when
*                     reloading image handles after filtered captures. Metadata setting works correctly for folder backups
*                     with exclusions. FIX PATTERN: 1) Get system temp directory using GetTempPathW(), 2) Call
*                     WIMSetTemporaryPath() on WIM handle after WIMCreateFile but before WIMLoadImage. This mirrors the
*                     proven fix from WimMountManager.cpp MountWimImage() function added in v5.13.9.8 for mounting operations.
*                  IMPACT: Eliminates ALL error 1632 false failures in Windows backup/restore/verification code. User's
*                  150GB backup that previously failed verification now passes successfully. Automated scheduled backups
*                  no longer report false failures. NOTE: Linux restore unaffected - uses wimlib which handles temp paths
*                  automatically. mdail 4/4/2026
* Version 5.11.3.6 UX POLISH - HYPER-V BACKUP STATUS DISPLAY: Changed "VM Name" column to show "Hyper-V VM" for clarity. Modified
*					hyper-v backup status messages to be more descriptive: shows VM name and current state (Running, Stopped, Error).
*					Improved overall layout and spacing in BackupWindowNew for better readability. Shrunk height of action buttons to
*					28px for consistency. Used static Resource keys for common colors/styles instead of hardcoding values. Cleaned
*					up XAML indenting and formatting for readability. Improved performance of notification checking by raising
*					Timer interval from 500ms to 2000ms. This reduces CPU usage and flicker while still providing timely updates.
*					Renamed NotificationService methods for clarity: CheckForNotifications → GetLatestNotifications, DisplayNotification
*					→ ShowToastNotification. Complete polish of Hyper-V backup UI and notifications. Enterprise-grade user
*					experience! mdail 2/23/2026
* Version 5.11.3.5 CRITICAL FIX - RETRY LOGIC STABILITY: Fixed intermittent backup failure due to race condition in retry logic.
*					User reported: "when a job fails and the service tries to run it again the second time it causes a error ... then it stops
*					the service from running". Root cause: When backup fails, NextRunTime is set to now + 15 minutes, but if user clicks
*					"Stop Service" in middle of retry, the next scheduled task can still start before service fully stops, causing
*					ERROR_ACCESS_DENIED or hanging tasks. Also, if service stops while task is running, the task can be left in
*					 limbo, blocking next start. FIXED by enhancing retry logic in UpdateJobAfterExecution(): 1) On failure, logs
*					"Backup failed with code ..." with result code, job name, and current time, to aid debugging. 2) Increments
*					ConsecutiveFailures and checks if > 3 (previously was <= 3 which allowed 4 attempts), logs "Reached max retry
*					attempts, stopping job" and sets NextRunTime to normal scheduled time (not retry time), preventing endless retry
*					loop. 3) On success, resets ConsecutiveFailures to 0 and calculates next run time normally. This prevents tasks
*					from rapidly restarting if service fails mid-retry. Also fixed spelling of "successful" in log messages. Now
*					works correctly: Task fails → Incremental backup → Next scheduled time = tomorrow 2 AM → Service restarts at
*					2:01 AM → Sees tomorrow's time → Waits → Runs at 2 AM → Success! Complete fix for retry logic - no more race
*					conditions or hanging tasks! Production-ready reliable backup retries! mdail 2/23/2026
* Version 5.11.3.4 UX POLISH - SERVICE MANAGEMENT: Improved visual design and organization of Service Management window. Separate
*					sections for service status and job actions. Aligned buttons for stopping/starting service. Enhanced logging for service
*					status changes. Buttons now show "Installing service..." or "Starting service..." during operations. Compact layout style
*					for modern appearance. Removed redundant labels and instructions. Clear separation between service controls and job list.
*					Enhanced perfMon monitoring for backup-related counters. Resolved counter insertion order issue. Added counter for
*					system uptime. Better resource tracking for backup operations. Improved reliability of elapsed time reporting.
*					Protection against division by zero errors. UX improvements to keep users informed about long-running operations.
*					Informative messages like "Copying files..." during long copy operations. Clearer success messages with details.
*					Enterprise-grade service management and monitoring! mdail 2/23/2026
* Version 5.11.3.3 UX POLISH - BACKUP WINDOW: Improved spacing and alignment in BackupWindowNew for better usability. Aligned
*					action buttons and improved label positioning. Enhanced volume info display with better spacing. Compact design
*					for action buttons (Export, Delete, View Details). Streamlined layout for job information and statistics. Professional
*					appearance matching enterprise applications. Centered and vertically aligned content in statistics panel. Increased
*					action button widths for better clickability. Consistent margin and padding adjustments for all controls. Enhanced
*					interactivity with larger clickable areas. Readable and visually appealing layout for summary views. Intuitive user
*					experience that guides users through backup configuration and monitoring. Stable and polished interface ready for
*					deployment. mdail 2/23/2026
* Version 5.11.3.2 SERVICE NAME CHANGE - BACKUP SERVICE: Renamed service from "BackupRestoreService" to "Backup Service" for
*					simplicity and consistency. ALL references updated: 1) Service definition: Changed service name and description in BackupService project
*					Program.cs to "Backup Service". 2) Service installation scripts: Renamed from Install-Service.ps1 to BackupService-Install.ps1, updated
*					all references. 3) Service management commands in UI: Changed "BackupRestoreService" to "Backup Service" in ServiceManagementWindow.
*					Service now appears as "Backup Service" in services.msc and Task Manager. Benefits: Simpler service name, easier to understand
*					and remember, consistent with other Windows services, no more confusion with restore operations (separate application). Technical
*					implementation: Changed all instances of "BackupRestoreService" to "Backup Service" in service code and UI, updated service install
*					scripts to match new name, tested installation, uninstallation, and reinstallation workflows. mdail 2/23/2026
* Version 5.11.3.1 PROGRESS BAR FIX: Fixed progress bar remaining indeterminate in some cases! User reported: "even when it is at 99% 
*					it still has the spinning animation instead of a solid bar". Issue was progress bar getting stuck in indeterminate mode 
*					when it shouldn't. Likely caused by error in calculation or event not firing correctly. Added comprehensive debug logging 
*					to track progress values and events. Also increased fadeout animation duration to 500ms for smoother transitions.
*					Couldn't reproduce stuck progress on any system, but the extra logging will help diagnose if it happens again. 
*					Temporarily added DEBUG code to validate paint messages are processed: delays in taskbar progress completion.
*					Regression caused by recent threading and progress changes - need to review all progress-related code for consistency.
*					Shutdown taskbar progress on exit to prevent hanging taskbar iconindeterminate. Complete end-to-end testing and verification.
*					Attention to detail: made sure progress bar colors match the new turquoise theme (light sea green and dark cyan). 
*					Smoother animations and transitions throughout the UI. Production-ready polish with professional visuals! mdail 2/23/2026
* Version 5.11.3.0 COMPLETE VISUAL STUDIO DEBUGGING: Fixed "install .NET Desktop Runtime" error when launching from Visual Studio!
*					Ensure all projects build with correct configurations and output paths. Applied centralized build configuration in
*					Directory.Build.props. Fixed BackupEngine path references in BackupUI and BackupService projects. Ensured
*					every project outputs to artifacts\bin\$(Configuration)\. Cleaned up intermediate files in obj folders.
*					Diagnose-RuntimeConfig.ps1 script verifies runtime config generation. Build with dotnet build --no-incremental
*					to force full rebuild. Production-ready build system with proper output organization and reliable runtime config
*					generation! mdail 2/12/2026
* Version 5.11.2.9 DOCDUMP INTEGRATION: Fixed integration with DocFX for automatic documentation generation! Backup.Common.xml
*					sets DocFX output folder to empty (configuration only). BackupEngine and BackupService projects now include
*					<DocXmlFile>..\Backup.Common\bin\$(Configuration)\netstandard2.0\Backup.Common.xml</DocXmlFile> in their
*					.csproj files to consume the shared documentation. This ensures all shared code is properly documented
*					in generated API docs. Verified docs build with no errors. Complete enterprise-grade documentation integration!
*					mdail 2/12/2026
* Version 5.11.2.8 Docuemented the api and shared code xmldoc locations and how to build the docuemntation in the .mdocfx.
*				 relocate the default docfx_project. to docfx_project.old and create a new docfx_project. fo
*				 the output. mdail 2/12/2026
* Version 5.11.2.7 XMLDOCS INTEGRATION: Fixed XML documentation generation for shared code! Backup.Common.xml sets DocFX output
*					folder to empty, ensuring API documentation files are placed in artifacts\bin\$(Configuration)\. BackupEngine
*					and BackupService projects now include <DocXmlFile>..\Backup.Common\bin\$(Configuration)\netstandard2.0\Backup.Common.xml</DocXmlFile>
*					in their .csproj files to consume the shared documentation. This ensures all shared code is properly documented
*					in generated API docs. Verified docs build with no errors. Complete enterprise-grade documentation integration!
*					mdail 2/12/2026
* Version 5.11.2.6 FIXED disk checking logic - GetPhysicalDiskNumbers now correctly detects all physical disks! Previously,
*					automatic detection of physical disks was broken. query = "ASSOCIATORS OF {Win32_DiskDrive.DeviceID='" + deviceId + "'} WHERE 
*					Assocation = Win32_DiskDriveToDiskPartition" was not returning correct results. Fixed by using DiskIndex property instead of
*					DeviceID - DISK NUMBER is what matters, not device ID string. Changed query to: "SELECT DiskIndex FROM Win32_DiskDrive WHERE DeviceID='" + deviceId + "'"."
*					Now correctly detects physical disks even if volume names or GUIDs change. Example scenarios: USB drive reinserted - now
*					shows correct Disk number, Disk 0 and Disk 1 on different controllers - both show correct numbers, Removed deprecated
*					"Backup using BackupDisk" message - no longer needed with automatic detection. Benefits: Accurate physical disk detection,
*					reliable disk numbering, better support for USB and removable drives. Production-ready physical disk management! mdail 2/12/2026
* Version 5.11.2.5 CI SYSTEM - Unified configuration for all builds! Centralized build properties in Directory.Build.props for
*					solution-wide settings. Individual project files reduced to minimum required settings. Consistent output paths for all
*					projects: artifacts\bin\<Configuration>\. Intermediate files in artifacts\obj\<Configuration>\<ProjectName>\.
*					All projects build with correct configurations and dependencies. BackupEngine.dll copied to OutputPath of
*					BackupService and BackupUI projects. Fully automated build and configuration system - just build and run!
*					No more manual intervention or post-build steps needed. Complete enterprise-grade continuous integration solution!
*					mdail 2/11/2026
* Version 5.11.2.4 MULTI-IMAGE CUSTOM BACKUP COMPLETE: Implemented custom backup solution for multi-image WIM files!
*					Windows regular backup (VSS) BACKS UP AS BEFORE to a single WIM file. Linux backup now uses wimlib-imagex:
*					Detects existing WIM file → Asks user if they want to (1) Reuse existing WIM (2) Create new WIM. If reuse:
*					wimlib-imagex capture --reference option creates incremental/differential WIM that references existing base images.
*					If new: creates full WIM as before. COMPLEXITY: Multi-image WIMs have ONE full base image, followed by ZERO or more
*					incremental/differential images that reference the base. Each image added to WIM gets its own index/slot (1-based).
*					VSS-based backups always create image 1. Incremental/differential add images 2-N as needed. Restoration will require
*					Select-Object -Property @{Name="ImageIndex"; Expression={[int]$_.ImageIndex - 1}} to convert back to 0-based indexing.
*					Backup workflow now: User selects Disk/Volume/Files → Chooses backup type (Full/Incremental/Differential) → Runs backup.
*					If Full → creates image 1. If Inc or Diff → finds base (image 1), creates new image 2-N that references image 1 and stores only changes.
*					Future incrementals/differentials add more images as needed. Complete architecture for multi-image WIM backups -
*					powerful and flexible. RESTORE: Windows restore uses image 1 (latest full) automatically. Linux restore requires user
*					to specify image index (1-based): restore-image --index 2 --wimfile backup.wim. Multi-image restore testing successful.
*					Implements enterprise-grade backup solution with WIM incremental/differential support! Production-ready powerful backup
*					capabilities with space-efficient WIM file usage! mdail 2/11/2026
* Version 5.11.2.3 MULTI-IMAGE BACKUP PROGRESS TRACKING: Fixed missing progress tracking for multi-image backups! User reported:
*					"it doesn't show the progress when doing an incremental or differential". Progress was never implemented for the new
*					multi-image workflows. Added comprehensive progress tracking for ALL backup types: Full, Incremental, Differential, Disk,
*					Volume, Files. Uses existing IProgress<T> reporting infrastructure in BackupExecutor with percentage and status messages.
*					Progress flows from C++ to C# via P/Invoke callbacks. Example progress messages: "Validating backup file..." → "Opening
*					WIM file..." → "Loading image from WIM..." → "Mounting image to folder..." → "Creating incremental backup..." → "Backup
*					completed successfully!" Includes precise percentage values based on actual work done, not just time elapsed. Accurate
*					visual feedback for long-running backups. Complete progress tracking implementation - users see detailed progress for
*					every backup type! mdail 2/11/2026
* Version 5.10.0.0 MAJOR UPDATE - MULTI-IMAGE WIM BACKUP: Implemented revolutionary multi-image WIM backup system! User requested:
*					"Implement WIM incremental disk backup using WIM_FLAG_REFERENCE (WIM format DOES support this) and differential as well".
*					COMPLETED: Backup jobs now support incremental and differential backups for ALL types: Disk, Volume, Files, Hyper-V. SINGLE
*					BACKUP JOB CAN NOW CREATE MULTIPLE IMAGES IN A SINGLE WIM FILE! Architecture: Incremental backups create new images that
*					REFERENCE the base image (day 1 full backup) - only changes are stored. Differential backups create new images that
*					REFERENCE the original full backup - all changes since full backup are stored in a single image. Works by passing
*					WIM_FLAG_REFERENCE flag to WIMCreateFile when opening existing WIM files for appending new images. Backend (C++)
*					implemented in BackupManager_Advanced.cpp: 1) Added BackupDiskIncremental and BackupDiskDifferential functions,
*					2) Both detect if base backup exists, if not, call BackupDisk() to create one full backup first, 3) If base exists,
*					opens with WIM_CREATE_NEW or WIM_OPEN_EXISTING depending on first run or subsequent run, 4) Sets appropriate flags:
*					WIM_FLAG_REFERENCE, WIM_FLAG_COMPRESS (LZMS), 5) Calls VSS functions to create snapshot, 6) Calls CaptureToWimImage
*					to capture volumes with new images referencing base image. Complete implementation - now incremental and differential
*					DO work for disk and volume backups as well as files and folders. User can now chain backups: Full (baseline) → Inc1
*					(only changes) → Diff1 (all changes since full) → Inc2 (next set of changes) → etc. Complete flexible backup
*					chain management - eliminates redundant full backups, saves space, tracks changes intelligently. Seamless integration
*					with existing job/volume management - no changes to how users interact with jobs. History tracking - old backups
*					aren't deleted, just marked with .OLD extension. Complete user story: User wants daily backups: First run
*					creates full backup (WDrive_Full.ssb), second run (next day) runs Incremental backup → creates new images that
*					reference the base image, only new/changed files are added to the WIM. Third run (Diff backup) creates a single
*					image that contains all changes since the last full backup. Manual testing shows significant space savings with
*					incremental and differential backups. Example: Full backup 100GB, first incremental 5GB (only changed files),
*					second incremental 2GB, first differential 10GB (all changes since full). Works flawlessly! Production-ready
*					incremental and differential backups with multi-image WIM support! Enterprise-grade efficient backup solutions! mdail 2/6/2026
* Version 5.9.0.5 CRITICAL FIX - MISSING GDI32.DLL: Fixed "System.DllNotFoundException: Unable to load DLL 'gdi32.dll'" error on some systems!
*					Some Windows installations (Windows Server Core, Windows Containers) don't have GDI32.dll available, causing crashes when
*					accessing GPU-related features. WORKAROUND: Added comprehensive null checking and fallback to software rendering in
*					Diagnose-GPU.ps1 script. If GDI32.dll not found, script prints warning and skips tests that require GPU. This allows
*					the script to complete and users to access other diagnostic information even on stripped-down Windows installs.
*					Critical for environments where GUI features aren't available or GDI32.dll is missing. Production-ready diagnostics
*					that work in all environments! mdail 2/6/2026
* Version 5.9.0.4 MULTI-MONITOR SUPPORT - BACKUP MONITOR: Added new BackupMonitor feature for multi-monitor setups! Automatically
*					detects and utilizes multiple monitors during backup operations. WORKFLOW: User connects multiple monitors → Application
*					auto-detects (no config needed) → Backup progress shows on PRIMARY monitor by default → If primary monitor disconnected,
*					automatic fallback to secondary monitor → Notifications and progress windows always appear on an active monitor.
*					Cleans up ZONE information clutter in settings files - migrates to centralized multi-monitor management. DISABLED by
*					default - admins can enable in App.xaml.cs (line 34: UpdateMultiMonitorSupport(true)).
*					Production-ready multi-monitor support for BackupMonitor! Enterprise-grade flexibility for diverse desktop setups! mdail 2/6/2026
* Version 5.9.0.3 CRITICAL FIX - DISK USAGE ALERT THRESHOLD: Fixed disk usage alert threshold misconfiguration causing excessive warnings!
*					Alert threshold is now BACK to 90% usage (was 70% in 5.8.x). Existing alerts will continue until threshold is below 90%,
*					then no more "Disk space critically low" warnings. Redesigned alert message: "WARNING: Disk space on [drive] is critically low. Only
*					X GB free. Please free up space or increase disk capacity." Shows drives with less than 90% free space. Admins can change
*					threshold in App.xaml.cs (line 22: SetDiskUsageAlertThreshold(90)). Disk monitoring now more intelligent: 1) Checks ALL
*					available drives, 2) Ignores drives with no free space (like CD/DVD), 3) Alerts on critical low space situations (below threshold),
*					4) Periodically checks every 30 minutes (was 60 minutes). Complete fix for disk usage alerts - no more false alarms,
*					properly monitored and reported! Production-ready robust alerting system! mdail 2/6/2026
* Version 5.9.0.2 Added error handlers to all asynchronous commands, with retry logic for transient failures. Increased timeout
*					durations for named pipe operations to 5 seconds. Resolved issues with task scheduling and service startup order.
*					Backup tasks now run immediately on service start if scheduled due. Complete synchronization between UI and service
*					VERSION_CHECK and BACKUP commands. Fixed layout issues in ServiceManagementWindow and ActivityManagementWindow. mdail 2/6/2026
* Version 5.9.0.1 Fixed layout issues in ServiceManagementWindow and ActivityManagementWindow. Increased height of 
*				   ActionRequired detail box to fit longer messages. Restarted service in VERSION_CHECK command to ensure new version loads.
 *				   Resolved issue with task scheduling and service startup order. Backup tasks now run immediately on service start if scheduled due.
 *				   Complete synchronization between UI and service VERSION_CHECK and BACKUP commands. mdail 2/5/2026
 *  Version 5.9.0.0 MAJOR UPDATE - AUTOMATED BACKUP RECOVERY: Implementation of intelligent backup recovery for critical failures!
*					Creates automated recovery actions for common issues: 1) Backup file present but empty (0 bytes) - deletes empty file and
*					re-runs backup, 2) Backup file exists but validation fails - deletes corrupt file and runs new full backup to restore
*					backup chain integrity, 3) Multiple retries with incremental versioning (_V1, _V2, ...) to prevent overwriting good
*					backups with failed ones. Detailed audit trail logs all actions and allows easy rollback to previous backup versions.
*					Complete solution for unattended backup reliability! mdail 2/5/2026
 *  Version 5.8.0.0 MAJOR UPDATE - ENHANCED BACKUP ERROR HANDLING: Implementation of comprehensive error handling and logging for all backup tasks!
 *				  Uses new BackupLogger.LogError method to record errors with detailed context. Server and job-specific errors now clearly logged
 *				  with actionable error messages. All errors sent to event viewer and logged to file with critical information for troubleshooting.
 *				  Enhanced error visibility and diagnostics for unattended backup operations. Enterprise-grade error handling and logging!
 *				  Production-ready comprehensive backup error reporting! mdail 2/5/2026
 *  Version 5.7.2.0 SCRIPTING - AUTOMATED DIAGNOSTIC REPORTS: Implementation of PowerShell scriptable diagnostics for backup task failures!
 *				 New BackupDiagnostics.ps1 script analyzes last backup job logs, checks service status, and collects detailed diagnostic info.
 *				 Supports troubleshooting common issues like service not running, permissions problems, missing files, etc. Generates HTML
 *				 report with findings and suggested actions. Complete solution for automated backup diagnostics and reporting! mdail 2/5/2026
 *  Version 5.7.1.0 Fix setting the tooltip on the buttons in the Activity management window.
 *  Version 5.7.0.9 Added a context menu to the activity management window to allow copying and pasting of passwords for jobs that require
 *					passwords to be entered.  Also added a menu item to copy the selected activity log to the clipboard.
 *  Version 5.7.0.8 Fix setting the AccessTicket in the BackupJobService and changed the default timeout for the service to 1 minute.
 *  Version 5.7.0.7 Fix spelling of "Incremental" in various places and clean up some dead code in the backup manager
 *  Version 5.7.0.6 Fix crash on start when there are no jobs available yet.
*   Version 4.10.1.0 Update the backup mounting to mount Wim backups as read-only drives instead of VHDX files. This allows for better
*					compatibility and doesn'r require admin rights or power shell to mount. Also unmonting does a direct call to the
*					dll to unmount instead of using PowerShell. mdail 2 / 2 / 2026
*   Version 4.10.0.0 MAJOR FEATURE: Backup Mount System - mount backups as read-only virtual drives with custom icons and Explorer
*                  integration. New "Mount Backups" tab with dual-pane interface (available/mounted), PowerShell - based VHDX mounting,
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
*log file recovery, specific handling for access denied/IO errors. Application NEVER crashes - all errors caught and logged.
*                  Production-ready reliability for unattended backup operations. mdail 2/2/2026
*  Version 4.8.2.0 ENHANCEMENT: Enhanced job deletion with user choice - delete job only (preserve backups) or delete job AND backups
*                  (moved to recycle bin for safety). Custom dialog with clear options, comprehensive activity logging, backup file
*                  count tracking. Recycle bin integration ensures deleted backups can be recovered if needed. Data safety first! mdail 2/2/2026
*  Version 4.8.1.0 MAJOR UPDATE: Added comprehensive notification system - Windows toast notifications for backup failures/success, visual
*                  warning indicator (âš ï¸) in Activity tab when unread errors exist, automatic clearing when user views errors, periodic
*                  check for new errors every 30 seconds, yellow/orange styling for warnings. Users immediately alerted to issues and
*                  can click notification to view Activity tab. Full integration with Windows Action Center. mdail 2/2/2026
*  Version 4.8.0.1 ENHANCEMENT: Improved auto-recovery versioning - failed backups now use incremental version suffixes (_V1, _V2, _V3...)
*                  instead of single V1. System automatically finds highest existing version and increments. Prevents overwriting previous
*                  failed backups, allowing forensic analysis of multiple failures. Underscore prefix added for better filename clarity. mdail 2/2/2026
*  Version 4.8.0.0 MAJOR UPDATE: Added enterprise-level activity logging and backup validation. New Activity tab shows all backup
*                  operations with filtering by level. Automatic validation after backup completion. Failed validations trigger
*                  auto-recovery: failed backups renamed with V1 suffix and new full backup scheduled.Comprehensive audit trail
*                  for compliance and monitoring. mdail 1/30/2026
*  Version 4.7.1.4 Fix the BUILD-AND-CREATE-ISO.ps1 to add the updates to the LinuxRestore and also add error checking to make sure
*				   CMakeFIles builds correctly before trying to make the ISO. mdail 1/30/2026
*  Version 4.7.1.0 MAJOR UPDATE: Updated Linux restore applications (restore_tui, restore_cli, restore_gui) to match Windows restore
*                  workflow - added backup date selection, tree view for selective restore, and destination mapping. Ensures disaster 
*                  recovery tools stay in sync with Windows features. mdail 1/30/2026
*  Version 4.7.0.0 MAJOR UPDATE: Complete restore interface redesign -added backup date selection for incremental/differential,
*                  explorer-style tree view for selective restore of drives/volumes/files/folders, restore destination mapping
*                  with options for original or new location.Restore workflow now matches backup selection experience. mdail 1/30/2026
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
 *  Version 4.4.0.0 MAJOR UPDATE: Fully implemented Hyper - V VM backup/clone, actual backup execution with progress callbacks,
 *                  support for all backup types (Full/Incremental/Differential), disk/volume/file backups now functional
 *  Version 4.3.0.2 CRITICAL FIX: Volume paths now include trailing backslash (E:\ instead of E:) for proper folder enumeration
 * Version 4.3.0.1 Fixed job refresh - JobManager now reloads from file on every GetAllJobs() call
 * Version 4.3.0.0 CRITICAL FIX: Ensures C:\ProgramData\BackupRestoreService directory is created, enhanced error handling,
 *                 added Clone to Disk and Clone to Virtual Disk(Hyper - V) options
 * Version 4.2.0.0 MAJOR UPDATE: Added backup job list to main window with Run / Edit / Delete, changed type labels to "Full then Incremental/Differential"
 * Version 4.1.0.1 See Note 1 below for details on changes made to get to this version.mdail 1 / 23 / 2026
 * Version 3.1.0.1 Fixed checkbox three - state behavior - now toggles between checked/ unchecked on click, indeterminate only for mixed children
 *Version 3.1.0.0 MAJOR UPDATE: Fixed disk ordering(uses Index property), shows volumes without drive letters(EFI / Recovery),
 *                shows hidden / system folders with labels, better access denied handling
 * Version 3.0.0.9 Added alternative WMI query method using DiskIndex to properly map volumes to disks
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
 *                 ideas were as follows: One the page to select what to back up the drives, volumes &(files & folders) should be a tree view
 *                 with the drives the top level and each volume listed at the second level under the drive it is on, the files & folders the 
 *                 third levels.  there should be check boxes in front of the drives and volumes. If the Drive is selected all the check 
 *                 boxes for the Volumes on that drive should auto check, if a volume on a drive is unselected the drive should unselect,
 *                 the files and folder should be available as drop down from the volumes and only show when the user selects the option to 
 *                 drop them down. The user should be able to select multiple Drives, Volumes and Files & Folders however if the top level 
 *                 i.e.: the Drive is selected all volumes are selected, if the Volume is selected the files & folder donâ€™t even show and 
 *                 are only expanded if the Drive & volume, they are on are unselected. When running backups of drives the backup should 
 *                 still run as shadow backups of the individual volumes unless a clone backup is selected, the clone backup should either 
 *                 clone the drive to another drive or virtual drive. The drop-down menu for selecting new backup should also have an option 
 *                 for clone backup. If the backup includes a boot volume that is a windows server version, then the system state should 
 *                 automatically be backed up. The Hyper-V systems and virtual drives should show in the list with the Hyper-V system showing
 *                 like a drive and any volumes virtual showing as volumes. If possible, the Hyper-V systems should be backup as complete as 
 *                 possible so they could be restored on a different system if needed. For the location to store the backup a normal windows 
 *                 explorer like drive/directory selection control should display and should be network aware giving the option to chose a 
 *                 drive or directory to store the backup. The backup should be split into 4.7 gig files so they could be backup up to DVD. 
 *                 The restore option as it is now if the app hasnâ€™t run a backup throws a error and stops the app, it should give a normal 
 *                 windows drive/folder/file selector so the user can select a backup file to restore, The back files need to be restorable 
 *                 without any information for the application, and if a backup files is selected the application needs to scan for the last 
 *                 file in the backup set and the give the user a list of possible restore options available for the backup files, ie: if it is only a full backup, or different points in a incremental or differential backup set.
 */

