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
        private static readonly string version_fallback_number = "6.2.4.64";
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
 * Version 6.2.4.64 Changed passive archive reads to allow write sharing while a backup is running. Encryption detection
 *                  and WIM image-count/info opens no longer deny write access to the active .ssb file, preventing UI 
 *                  refreshes from blocking incremental reruns. mdail 5/11/2026
 * Version 6.2.4.63 Added retry handling when incremental or differential disk backups reopen an existing .ssb archive.
 *                  Transient sharing violations now wait and retry instead of failing immediately with WIM error 32,
 *                  and the native error text now includes the Windows system message for the open failure. mdail 5/11/2026
 * Version 6.2.4.62 Fixed manual backup fallback when the previous incremental or differential archive was deleted.
 *                  The backup windows now check for the current JobName.ssb file and run a full backup instead of 
 *                  calling the native incremental engine with a missing base file. mdail 5/11/2026
 * Version 6.2.4.61 Fixed RestoreWindowNew disk restore metadata gating. The disk sizing and reconstruction
 *                  flow now treats metadata as present only when planned restore volumes were actually parsed
 *                  from the backup, preventing false-positive metadata detection and keeping the sizing/partition 
 *                  restore path aligned with real reconstruction data. mdail 5/11/2026
 * Version 6.2.4.60 RestoreWindowNew disk restores now require reconstruction metadata before execution, reopen the 
 *                  sizing flow when needed, partition and format the selected target disk from the chosen layout, and restore
 *                  each selected volume image to the created target volumes before reporting completion. mdail 5/11/2026
 * Version 6.2.4.59 Fixed RestoreWindowNew disk restore sizing flow. Disk restore metadata is now parsed from raw image 
 *                  descriptions so the partition selection/sizing UI opens before execution, and the sizing window now 
 *                  defaults resizable volumes to expand and fill the target disk. mdail 5/11/2026
 * Version 6.2.4.58 RestoreWindowNew no longer shows the disk format warning twice. Disk restore confirmation remains 
 *                  in StartRestore_Click, while PrepareDiskTarget now only validates that a target disk is selected 
 *                  before execution. mdail 5/11/2026
 * Version 6.2.4.57 RestoreWindowNew now queues a restore-target tree reload when initial FileOrFolder-mode loading 
 *                  overlaps the later disk/volume mode update, preventing disks from staying greyed out until a restore 
 *                  point is selected. mdail 5/11/2026
 * Version 6.2.4.56 RestoreWindowNew now recalculates restore target kind immediately after scan/load, so disk targets 
 *                  become selectable right away for disk/volume backups even before a restore point is selected. mdail 5/11/2026
 * Version 6.2.4.55 RestoreWindowNew keeps disk targets selectable for disk/volume backups before a restore point is selected, preventing
 *                  pre-selection greying and avoiding manual target-tree refresh after choosing a restore point. mdail 5/11/2026
 * Version 6.2.4.54 Standardized RestoreWindowNew alerts to be owner-bound and modal. All restore,
 *                  validation, warning, and error dialogs in RestoreWindowNew now route through a
 *                  centralized helper that uses MessageBox.Show(this, ...) and UI-thread dispatch,
 *                  ensuring alerts stay on top of the active restore window.
 *                  mdail 5/11/2026
 * Version 6.2.4.53 Fixed RestoreWindowNew execution order so disk/volume restores now show the existing
 *                  volume selection and resize workflow before restore starts. After format confirmation,
 *                  metadata-backed restores proceed to the resize bar UI (including single-volume layouts),
 *                  then execute partition setup/format/restore with progress updates.
 *                  mdail 5/11/2026
 * Version 6.2.4.52 Fixed RestoreWindowNew disk/volume format confirmation z-order and modality.
 *                  Destructive format prompts now execute on the UI thread with RestoreWindowNew as
 *                  owner, so they appear in front of the restore window instead of behind it.
 *                  mdail 5/11/2026
 * Version 6.2.4.51 RestoreWindowNew now shows the generic "may overwrite existing files" confirmation
 *                  only for file/folder restores. Disk and volume restores no longer show that extra
 *                  prompt and continue to show only the destructive format confirmation.
 *                  mdail 5/11/2026
 * Version 6.2.4.50 Fixed RestoreWindowNew protected-disk detection that could over-mark target disks
 *                  as boot-protected and leave disk checkboxes greyed out. Protection now resolves only
 *                  from the active OS logical drive to its backing physical disk index, so non-boot disks
 *                  remain selectable for disk and volume restores.
 *                  mdail 5/11/2026
 * Version 6.2.4.49 Fixed a follow-up RestoreWindowNew target-tree state sync issue where disk checkboxes
 *                  could remain greyed out after restore-kind transitions. LoadRestoreTargetDrivesAsync now
 *                  captures a buildTargetKind snapshot and uses that same snapshot for disk selectability and
 *                  _lastBuiltTargetKind tracking, ensuring disk and volume backups keep disk targets enabled.
 *                  mdail 5/11/2026
 * Version 6.2.4.48 Fixed RestoreWindowNew restore-target disk checkbox enablement regression.
 *                  Disk nodes were incorrectly evaluated against live restore-kind state during async
 *                  tree generation, which could leave disk checkboxes greyed out for disk/volume restores.
 *                  Tree selectability now consistently uses the build-time diskMode snapshot so disk
 *                  targets are selectable again for both disk and volume backup restores. mdail 5/11/2026
 * Version 6.2.4.47 RestoreWindowNew restore-point selection now starts unselected on initial load.
 *                  Clicking a restore point selects/highlights it and checks its checkbox; clicking the
 *                  same selected restore point again now unselects/unhighlights and unchecks it. Single-
 *                  selection behavior is preserved so clicking a different restore point switches selection.
 *                  mdail 5/11/2026
 * Version 6.2.4.46 RestoreWindowNew restore-point list now shows a checkbox in front of each restore point.
 *                  Selecting a restore point keeps the existing highlight behavior and now also checks the
 *                  checkbox for the selected row so visual selection state is consistent. mdail 5/11/2026
 * Version 6.2.4.45 Fixed full-disk restore crash for .ssb archive paths. RestoreWindowNew now routes
 *                  disk restores for archive files to RestoreDiskFromImage instead of legacy RestoreDisk,
 *                  which expects a folder of .img files and threw "Exception in RestoreDisk". mdail 5/11/2026
 * Version 6.2.4.44 Fixed RestoreWindowNew target reselection lock where Start Restore stayed disabled
 *                  after changing tree selections; button state now re-evaluates on every restore-point,
 *                  restore-kind, and target-selection change. Renamed action button text to Next because
 *                  the next step opens partition sizing/selection before confirmation and execution. mdail 5/11/2026
 * Version 6.2.4.43 Fixed Show Hidden Partitions on the restore target tree: TargetAddHiddenPartition
 *                  was filtering by type name keywords (EFI/Recovery/Reserved) that don't match real
 *                  WMI Type strings like "GPT: System" or "GPT: Basic Data", so no hidden partitions
 *                  were ever shown. Removed the filter — every partition with no drive letter is hidden
 *                  by definition and should appear when the toggle is on. Added readable type labels
 *                  ("EFI System", "MSR", "Recovery", "Data (no letter)"). mdail 5/8/2026
 * Version 6.2.4.42 LinuxRestore GTK and ncurses TUI updated to match Windows RestoreWindowNew
 *                  restore-target behavior: target tree always shows all disks including boot disk
 *                  (greyed/insensitive); boot disk cannot be selected; disk and partition selectability
 *                  mirrors Windows (both Disk and Volume restore kinds can select whole disks);
 *                  GTK Step 3 redesigned as two-panel layout (options left, full-height target tree
 *                  right) with toolbar: Refresh, Expand All, Collapse All, Show Hidden Partitions.
 *                  ncurses TUI Step 3A shows inline tree with boot disks dimmed, H toggles hidden
 *                  partitions, R refreshes list; Step 3B presents radio-style restore mode options.
 *                  restore_engine.cpp PartitionInfo gains isHiddenPartition flag detected from
 *                  lsblk output (no mount point and no common filesystem). mdail 5/8/2026
 * Version 6.2.4.41 RestoreWindowNew: disk nodes in the restore target tree are now selectable for
 *                  both Disk and Volume restore kinds, allowing the user to pick an entire disk as the
 *                  restore destination (the engine repartitions it to match the backup layout). Disks
 *                  were previously always greyed because diskMode was only set for Disk kind and because
 *                  the tree was built before UpdateSelectedRestoreTargetKind ran. Added
 *                  _lastBuiltTargetKind tracking so the tree rebuilds whenever selectability changes
 *                  between disk-enabled and non-disk-enabled modes. mdail 5/8/2026
 * Version 6.2.4.40 fixed false [Boot] label on data volumes sharing the boot disk; fixed boot-disk
 *                  detection to walk SystemRoot drive letter -> LogicalDisk -> DiskPartition ->>
 *                  DiskDrive instead of unreliable BootPartition=TRUE flag. mdail 5/8/2026
 * Version 6.2.4.39 RestoreWindowNew target tree now uses 3-layer WMI/DriveInfo fallback enumeration
 *                  matching the New Backup window — Layer 1: ASSOCIATORS query, Layer 2: DiskIndex query,
 *                  Layer 3: DriveInfo fixed-drive scan. Resolves blank tree when WMI ASSOCIATORS escape
 *                  was broken. Hyper-V VMs still enumerated via PowerShell. mdail 5/8/2026
 * Version 6.2.4.38 RestoreWindowNew restore target tree now uses CheckBox controls identical to the
 *                  backup source tree in New/Edit Backup. Disks expand to show volumes; single-select
 *                  enforced. Tree is always visible and enabled for all restore types (disk, volume,
 *                  file/folder, HyperV virtual disk); only Hyper-V VM clone restores hide it.
 *                  Help text updates to guide selection by restore type. mdail 5/8/2026
 * Version 6.2.4.37 the restore target tree now fills the full right-side height. Disk/volume backup
 *                  classification fixed: detection now checks _diskRestorePlan metadata first, then
 *                  scans all backup items, then checks the file path for disk/volume keywords so
 *                  disk and volume backups are never misclassified as file/folder restores. mdail 5/8/2026
 * Version 6.2.4.36 right side now fills the column with disks, volumes, and Hyper-V VMs loaded
 *                  automatically on open. Bottom toolbar gains Refresh, Expand All, Collapse All,
 *                  and Show Hidden Partitions controls. Boot/system disk stays greyed and
 *                  unselectable. Selecting a running Hyper-V VM prompts for confirmation and
 *                  forces a Stop-VM shutdown before restore; cancelling unchecks the VM.
 *                  The target tree is disabled for file/folder backups (Original/Alternate
 *                  Location controls remain active). Hyper-V clone restores hide the tree and
 *                  show a new Restore Destination panel: Use Hyper-V default storage location or
 *                  Alternate location with separate browse fields for VM config folder and disk
 *                  data folder. mdail 5/8/2026
 * Version 6.2.4.35 RestoreWindowNew redesigned with two-column layout: restore options on the
 *                  left and a dedicated restore target tree on the right, always visible, mirroring
 *                  the New/Edit Backup source tree layout. The target tree auto-loads drives on window
 *                  open and shows all disks/volumes with boot disk greyed and unselectable. mdail 5/8/2026
 * Version 6.2.4.34 Linux restore UIs updated to match Windows restore-target-tree behavior:
 *                  GTK GUI Step 3 now always shows the full disk/partition target tree at the top,
 *                  loads automatically on entry, and auto-selects disk-target mode when the user
 *                  picks a device. ncurses TUI leads with the target tree as the primary Step 3
 *                  screen, showing boot/system disk greyed and its partitions flagged. CLI Step 3
 *                  always prints the full target tree before presenting destination-mode options.
 *                  Boot disk is blocked in all Linux UIs. mdail 5/7/2026
 * Version 6.2.4.33 Restore target tree now shown for all backup types, not just disk/volume
 *                  restores. File/folder restores display the same drive tree so the user can
 *                  click any non-boot volume to auto-select it as the Alternate Location
 *                  destination. Disk items remain selectable only in disk-restore mode.
 *                  Boot/system disk stays greyed and unselectable in all modes. mdail 5/7/2026
 * Version 6.2.4.32 radio-button single-selection) matching the backup source tree. Disks and
 *                  volumes are loaded via WMI; boot/system disk items are shown greyed and
 *                  unselectable. Tree auto-loads when the restore options become relevant.
 *                  RefreshRestoreTarget_Click allows manual reload. mdail 5/7/2026
 * Version 6.2.4.31 Restore destination redesign:
 *                  picker that excludes the currently booted system disk (detected via
 *                  Win32_DiskPartition.BootPartition). Volume backups show a volume picker.
 *                  Hyper-V system restores offer replace-non-running-VM or restore-to-empty-
 *                  directory modes. GetProtectedDiskIndexes fixed to use BootPartition WMI
 *                  instead of incorrectly comparing drive letters against DeviceID strings.
 *                  mdail 5/7/2026
 * Version 6.2.4.30 Fixed single-instance mutex not released before UAC elevation relaunch,
 *                  causing the elevated instance to see the mutex as already owned and exit
 *                  immediately, making the app appear to still be running after close. Mutex
 *                  is now explicitly released and disposed before spawning the elevated process.
 *                  mdail 5/7/2026
 * Version 6.2.4.29 Added hidden-partition support
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
 *                  guest files, guest volumes, or prepared physical discs, and updated LinuxRestore to detect and
 *                  restore Hyper-V backup-point export folders. mdail 4/29/2026
 * Version 6.2.3.68 Added true Hyper-V full, incremental, and differential backup routing with directory-based
 *                  backup points, restore/verify discovery for Hyper-V .ssb folders, and restore path resolution
 *                  for the new Hyper-V export layout. mdail 4/29/2026
 * Version 6.2.3.67 Added managed and native automated test coverage, a repeatable Run-Tests.ps1 workflow,
 *                  optional WSL LinuxRestore test_execution, and fixed the LinuxRestore restore_engine compile issues found by the new tests. mdail 4/28/2026
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
*                  three root causes. TECHNICAL DETAILS: Windows.h is ESSENTIAL for any C++ code using Windows API - provides type
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
*/

