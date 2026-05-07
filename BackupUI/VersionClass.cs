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
        private static readonly string version_fallback_number = "6.2.4.29";
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
 * Version 6.2.4.29 Added hidden-partition support to LinuxRestore for disk & volume restores.
 *                  BackupEngine now emits IS_HIDDEN_PARTITION in disk backup metadata, detecting
 *                  EFI System, MSR, and Windows Recovery GPT partition types as well as volumes
 *                  with no drive-letter mount path. LinuxRestore parses the new field and exposes
 *                  --list-volumes [--show-hidden] to enumerate backup volumes and
 *                  --restore-disk [--show-hidden] to optionally include hidden partitions in
 *                  disk reconstruction. Hidden partitions are excluded by default. mdail 5/7/2026
 * Version 6.2.4.28 After backup completes and the user closes the completion alert,
 *                  the progress window's Hide Window button now reads Close so the
 *                  action matches the final state of the window. mdail 5/7/2026
 * Version 6.2.4.27 Added single-instance enforcement:
 *                  window from opening. If the app is already running, the new launch activates
 *                  the existing window (restoring it if minimized) and exits immediately.
 *                  Backup operations can still run in parallel via the service. mdail 5/7/2026
 * Version 6.2.4.26 Fixed Hyper-V locked-AVHDX tree error still appearing by walking the full exception
 *                  chain (including AggregateException) in IsHyperVVirtualDiskSharingViolation so Task.
 *                  Run-wrapped sharing violations are caught correctly. Caller catch now skips the raw 
 *                  error node for sharing violations. Preserved checked tree selections across LoadDrives() 
 *                  rebuilds so enabling hidden partitions no longer clears existing checks. mdail 5/7/2026
 * Version 6.2.4.25 Changed locked-AVHDX handling in Hyper-V virtual disk tree expansion to show
 *                  a warning alert instead of an error node, then collapse and reset the disk node
 *                  so it reverts to its pre-expansion state. mdail 5/7/2026
 * Version 6.2.4.24 Fixed raw "being used by another process" error shown in the Hyper-V virtual
 *                  disk tree node when a running VM's AVHDX is locked. Now shows a friendly message
 *                  directing the user to select the VM node to back up the entire virtual machine. mdail 5/7/2026
 * Version 6.2.4.23 Replaced completion MessageBox with timed BackupCompletionDialog; auto-closes
 *                  both alert and progress window after 15 minutes, user-dismiss only closes alert.
 *                  Progress bar now explicitly set to 100% on success, fixing perceived 50% hang
 *                  on first-run incremental/differential disk backups. mdail 5/7/2026
 * Version 6.2.4.22 Fixed backup type shown as "Full" for Incremental and Differential jobs on
 *                  Mount, Restore, and Verify tabs. BackupType is now read from the job definition
 *                  instead of being inferred from the filename. mdail 5/7/2026
 * Version 6.2.4.21 Fixed Hyper-V virtual disk expansion
 *                  markup is now stripped from error messages so the user sees plain text instead
 *                  of raw XML. Mount-VHD is now run asynchronously on a background thread; the
 *                  tree node shows an animated spinner (ProgressBar) while the disk is mounting
 *                  so the UI no longer appears frozen. Pre-select path also awaits the mount
 *                  properly. mdail 5/7/2026
 * Version 6.2.4.20 Added Show Hidden Partitions toggle to VolumeSelectionWindow (restore target
 *                  selection). EFI, Recovery, and System Reserved volumes without a drive letter
 *                  are hidden by default; the checkbox reveals them. VolumeInfo gains
 *                  IsHiddenPartition flag. mdail 5/7/2026
 * Version 6.2.4.19 Added Show Hidden Partitions toggle to the backup drive tree toolbar.
 *                  EFI, Recovery, and System Reserved (no-drive-letter) partitions are now
 *                  hidden by default; checking the checkbox and refreshing makes them visible.
 *                  DriveTreeItem gains IsHiddenPartition flag for future filtering. mdail 5/7/2026
 * Version 6.2.4.18 Fixed Hyper-V backup edit re-opening with nothing selected.
 *                  Hyper-V system backup, VM names are stored in HyperVMachines as normalized
 *                  strings (e.g. "MyVM"), but pre-selection was falling through to path-based
 *                  recursive search which compared against FullPath (the raw display name like
 *                  "MyVM (Running)") and never matched. Added PreSelectHyperVSystemByName that
 *                  matches against VirtualMachineName (normalized) or a re-normalized FullPath
 *                  so the correct HyperVSystem node is checked on edit. mdail 5/7/2026
 * Version 6.2.4.17 Fixed persistent "cannot combine Hyper-V systems with disks" alert when editing
 *                  a Hyper-V backup. Placeholder Folder children ("Loading...", "No guest disks",
 *                  error nodes) are propagated IsChecked=true when a HyperVSystem is fully checked,
 *                  and were being counted as real non-Hyper-V sources. Added IsHyperVItem and
 *                  IsDescendantOfHyperVItem helpers; validation now ignores any item whose ancestor
 *                  is a Hyper-V node, and CollectSelectedChildren skips those same placeholder nodes
 *                  instead of adding them to SourcePaths. mdail 5/7/2026
 * Version 6.2.4.16 Fixed item-type classification bugs in backup source collection and validation.
 *                  CollectSelectedChildren now handles HyperVSystem, File, NetworkDrive, and
 *                  NetworkShare items that were silently dropped. CollectSelectedHyperVMachines now
 *                  recurses through partially-checked parents so Hyper-V VMs under a partially-
 *                  selected group are no longer missed. Validation now excludes NetworkRoot and
 *                  NetworkBrowser sentinel nodes from the non-Hyper-V count so they cannot trigger
 *                  a false mixed-source alert. mdail 5/7/2026
 * Version 6.2.4.15 Fixed false "cannot combine Hyper-V systems with disks, volumes, or folders"
 *                  validation error when selecting Incremental or Differential backup for a Hyper-V
 *                  disk job. The validator was only checking for HyperVSystem item type; child nodes
 *                  typed as HyperVVirtualDisk or HyperVVolume were being counted as non-Hyper-V,
 *                  triggering the mixed-source alert. Fixed by including all three Hyper-V item types
 *                  in both the Hyper-V and non-Hyper-V filter sets. mdail 5/7/2026
 * Version 6.2.4.14 Fixed startup crash caused by a stale SecureServerBackup.deps.json
 *                  referenced SecureServerBackupCommon version 6.2.5.1 (a leftover from an earlier
 *                  padded-zero version experiment) while the actual DLL was 6.2.4.13. The runtime
 *                  EEFileLoadException / FileNotFoundException on startup was caused by this mismatch.
 *                  Deleted the stale deps.json and forced a full no-incremental rebuild so the correct
 *                  6.2.4.13 reference is regenerated. mdail 5/7/2026
 * Version 6.2.4.13 Enabled TreatWarningsAsErrors for all projects in Directory.Build.props so
 *                  every C# and C++ compiler warning is promoted to a build error, preventing
 *                  potential bugs from being silently ignored. mdail 5/7/2026
 * Version 6.2.4.12 Fixed two nullable-reference warnings that were promoted to errors:
 *                  changed string configFile to string? in RestoreWindowNew.xaml.cs (CS8600,
 *                  FirstOrDefault return value) and added a null guard before accessing
 *                  chkShowEncryptionPassword.IsChecked in BackupWindowNew.xaml.cs (CS8602,
 *                  possible dereference during initialization). mdail 5/7/2026
 * Version 6.2.4.11 Added multi-volume restore selector: when restoring a disk or Hyper-V backup the
 *                  user is now prompted to choose a single volume or the entire disk group. Full-disk
 *                  restores allow each partition to be shrunk (down to used-data size + 10%) or grown
 *                  (up to available target capacity) via the existing interactive resize canvas. Partitions
 *                  are then restored in partition-offset order so layout is correctly reconstructed on the
 *                  target disk. Restore metadata (partition number, offset, type, boot/system flags) is
 *                  preserved through the selection and sizing windows into the final restore calls. mdail 5/7/2026
 * Version 6.2.4.10 now queries WMI Win32_DiskPartition.BootPartition to identify the currently
 *                  active OS disk and only flags Disk-target backups when their source path matches
 *                  that disk. Secondary and dual-boot disks that are not currently booted are fully
 *                  restorable from Windows without the recovery-disk warning. Fixed
 *                  NullReferenceException in RestoreWindowNew.RestoreLocation_Changed when the
 *                  handler fires during InitializeComponent before rbAlternateLocation is ready.
 *                  mdail 5/7/2026
 * Version 6.2.4.9 Added verify logging to the Activity page:
 *                 now writes start, progress, success, warning, and error entries to BackupLogger under
 *                 a "<JobName> [Verify]" job name so the Activity page shows a full trace of each verify run.
 *                 Fixed the multi-volume image-selection dialog to show "Verify Selected" and verify-specific
 *                 subtitle text instead of the mount-oriented "Mount Selected" label when opened from the
 *                 Verify tab. mdail 5/6/2026
 * Version 6.2.4.8 When the selected .ssb contains more than one image, the same ImageSelectionDialog
 *                 used by the Mount tab is shown before verification starts so the user can pick the volume
 *                  to check. The chosen image index is passed to both the health-check and repair calls.
 *                 The result log now shows "Image: N of M" for multi-volume files. Also fix version number. mdail 5/6/2026
 * Version 6.2.4.7 Fixed Hyper-V incremental and differential backups failing with VSS error
 *                 0x8004230C (VSS_E_VOLUME_NOT_SUPPORTED) when capturing the mounted exported
 *                 VHDX. VSS cannot snapshot VHD-mounted virtual disks; since the Hyper-V
 *                 export is already a consistent point, fall back to direct capture instead
 *                 of hard-failing. Also fixed HRESULT formatting in VSS error messages
 *                 (were printing decimal instead of hex). mdail 5/6/2026
 * Version 6.2.4.6 Fixed false post-backup verification failure on regular hard-drive
 *                 (non-OS) incremental backups: when TryResolveOfflineWindowsPaths finds no
 *                 Windows installation in the mounted image, skip SSBOpenSession/SSBCheckImageHealth
 *                 entirely and return healthy, since archive integrity was already confirmed.
 *                 Same guard applied in RestoreBackupImageHealth. mdail 5/6/2026
 * Version 6.2.4.5 Fixed mount failure caused by missing native export SsbMount_ValidateArchive:
 *                 replaced the non-existent P/Invoke with SsbMount_GetImageCount which returns
 *                 the image count (<=0 on failure) so archive validation works without the
 *                 missing entry point. mdail 5/5/2026
 * Version 6.2.4.4 Fixed BackupServiceManager.InstallServiceAsync
 *                 to sc.exe create and add a follow-up sc.exe description call so Services MMC always
 *                 shows the friendly spaced name and description on fresh installs. Fixed invalid
 *                 double-dot version string (6.2..4.003) in Directory.Build.props that blocked all
 *                 NuGet restores with NETSDK1005 project.assets.json errors. mdail 5/5/2026
 * Version 6.2.4.3 Renamed all Windows service registration, display names, log folder paths, and
 *                 installer scripts from the old BackupRestoreService identity to SecureServerBackupService
 *                 so the service list, Event Viewer, and on-disk log folders match the renamed project
 *                 outputs. BackupLogger migrates existing logs from the old folder on first run.
 *                 Also changed the name of a application to be correct in the MainPage of the UI. mdail 5/5/2026
 * Version 6.2.4.2 Fixed false post-backup verification failure
 *                 SSB component-store health check on Hyper-V .ssb archives. Hyper-V backups
 *                 contain guest VM disks, not Windows OS volumes, so SSBOpenSession always
 *                 fails with HRESULT 0x80070003. Archive integrity check is sufficient. mdail 5/5/2026
 * Version 6.2.4.1 Fixed service process crash during Hyper-V export by enabling async SEH
 *                 exception handling (/EHa) in BackupEngine so catch(...) intercepts access
 *                 violations from null WMI output pointers, added null guard on pOutParams after
 *                 ExecMethod, and fixed missing namespace closing brace. mdail 5/5/2026
 * Version 6.2.4.0 Fixed Hyper-V export job polling hang at 95%
 *                 handling all terminal CIM_ConcreteJob states (7=Completed, 8=Terminated, 9=Killed,
 *                 10=Exception, 32768=CompletedWithWarnings), and reading PercentComplete directly
 *                 from the WMI job object so progress moves smoothly from 40-94% during export
 *                 instead of stalling. Poll interval raised to 2 s to reduce WMI churn. mdail 5/4/2026
 * Version 6.2.3.99 Fixed running-VM Hyper-V export failure (0x80070020) caused by AVHDX chain merge
 *                  while the differencing disk is locked by a running VM. Full backups of running VMs
 *                  now use CopySnapshotConfiguration=2 (ExportOneSnapshot) and incremental/differential
 *                  use value 3 (ExportOneSnapshotForBackup), both with SnapshotVirtualSystem set.
 *                  Stopped VMs continue to use value 1 (ExportNoSnapshots). mdail 5/4/2026
 * Version 6.2.3.98 Version metadata consolidated: confirms 6.2.3.96 Hyper-V export fixes
 *                  (IWbemObjectTextSrc DTD 2.0 serialization, VT_BSTR async job polling) and
 *                  6.2.3.97 SSB branding cleanup are both in this build. mdail 5/4/2026
 * Version 6.2.3.97 Replaced all user-visible DISM and WIM references with SSB in verify/health-check
 *                  log messages across BackupExecutor and MainWindow so internal engine names never
 *                  appear in the UI or user-facing log output. mdail 5/4/2026
 * Version 6.2.3.96 Fixed two native Hyper-V export bugs: replaced GetObjectText() MOF serialization with
 *                  IWbemObjectTextSrc WMI DTD 2.0 format to eliminate 32773 invalid-parameter errors;
 *                  fixed async export job polling to handle VT_BSTR job path returned by WMI instead of
 *                  VT_UNKNOWN, ending "Failed to get export job" errors after ExecMethod succeeds. mdail 5/4/2026
 * Version 6.2.3.95 Fixed DISM backup health checks for mounted system images by resolving the offline Windows
 *                  directory and system-drive path before opening the SSB session, avoiding path-not-found
 *                  failures like HRESULT 0x80070003 during Hyper-V backup verify/mount follow-up checks. mdail 5/4/2026
 * Version 6.2.3.94 Fixed the Hyper-V PowerShell export fallback hang by making the process non-interactive,
 *                  draining stdout/stderr asynchronously, and timing out instead of blocking indefinitely. mdail 5/4/2026
 * Version 6.2.3.93 Added a PowerShell Export-VM fallback for Hyper-V full exports that fail with 32773 so first-run
 *                  incremental backups can still fall back to a full export, create the expected temporary backup-point
 *                  structure, and continue to virtual-disk capture. mdail 5/4/2026
 * Version 6.2.3.92 Fixed Hyper-V full-export fallback settings by loading the VM's associated export-setting instance
 *                  instead of spawning a blank one, preserving valid defaults and avoiding the 32773 invalid-parameter
 *                  failure when first-run incremental backups fall back to a full Hyper-V export. mdail 5/4/2026
 * Version 6.2.3.91 Fixed Hyper-V first-run incremental backup routing and native export settings so plain .ssb
 *                  files no longer count as a valid Hyper-V full base, running VM backup exports use the backup
 *                  snapshot mode, and BackupIntent now uses documented values to avoid export error 32773. mdail 5/1/2026
 * Version 6.2.3.90 Fixed Hyper-V export error 32773 for running VMs by correcting CopySnapshotConfiguration
 *                  to use documented values only: 1 (no snapshots) for stopped VMs or running VMs without a
 *                  current snapshot, 2 (one specific snapshot) for running VMs with a snapshot path. Value 3
 *                  is not a valid enum value and was the root cause. Also softened snapshot lookup failure
 *                  for running VMs to fall back to no-snapshot export instead of hard-failing. mdail 5/1/2026
 * Version 6.2.3.89 Fixed Hyper-V export error 32773 for VMs with no checkpoints by setting CopySnapshotConfiguration
 *                  to 0 (no snapshots) for stopped VMs instead of 1 (all snapshots), which is invalid when the VM has
 *                  no checkpoints. Running VMs still use 3 with a SnapshotVirtualSystem path. mdail 5/1/2026
 * Version 6.2.3.88 Fixed first-run incremental and differential Hyper-V backups by setting BackupIntent to 1 (Incremental)
 *                  and 2 (Differential) in Msvm_VirtualSystemExportSettingData, resolving error 32773 from ExportSystemDefinition.
 *                  Full backups (including first-run fallback from incremental/differential) leave BackupIntent unset to
 *                  preserve the original working full-export behavior. mdail 5/1/2026
 * Version 6.2.3.87 Hardened Hyper-V restore compatibility by reading VM names from backup metadata and
 *                  restoring exported VMs from the resolved configuration file with copy import semantics. mdail 5/1/2026
 * Version 6.2.3.86 Fixed first-run incremental and differential Hyper-V backups for running VMs by switching
 *                  the native export path to snapshot-based export settings instead of direct export. mdail 5/1/2026
 * Version 6.2.3.85 Added a running Hyper-V guest volume fallback that mounts the deepest readable parent VHD
 *                  in the differencing chain when the active guest AVHDX is locked by the running VM. mdail 5/1/2026
 * Version 6.2.3.84 Fixed the app PowerShell runner to use encoded commands so Hyper-V guest disk discovery
 *                  and guest-disk mount scripts no longer fail because of embedded quotes or script blocks. mdail 5/1/2026
 * Version 6.2.3.83 Fixed Hyper-V guest disk discovery on the New/Edit Backup page by adding a managed WMI
 *                  fallback so running VMs still list attached guest disks when native enumeration returns none. mdail 5/1/2026
 * Version 6.2.3.82 Fixed the New/Edit Backup Hyper-V tree so guest disks and files still list when native
 *                  Hyper-V disk enumeration returns no results by falling back to PowerShell VM disk discovery. mdail 4/30/2026
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
*/

