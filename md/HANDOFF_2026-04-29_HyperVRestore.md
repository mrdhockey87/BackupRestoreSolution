# Handoff - 2026-04-29

## Summary
Implemented the first increment of regular-backup restore support for Hyper-V targets.

Current version:
- `6.2.3.71`

Release note added:
- `Version 6.2.3.71 Added restore support to write regular full, incremental, differential, and file backups into Hyper-V VHDX targets and optionally attach the restored disk to an existing Hyper-V VM. mdail 4/29/2026`

## What was implemented
For regular backups, the restore UI now supports restoring into a Hyper-V virtual disk (`.vhdx`).

Implemented behavior:
- Restore regular backups into a Hyper-V `.vhdx` file
- Optionally attach the restored `.vhdx` to an existing Hyper-V VM
- Keep existing Hyper-V backup-point restore/import flow intact
- Keep existing disk/volume/file restore flows intact
- Added helper tests for the new managed decision logic

## Key implementation details
Approach used:
- Reused the existing restore engine by restoring into a mounted writable VHDX
- Did not add new native restore-engine exports for this first increment
- Used existing Hyper-V/PowerShell management commands for VHDX handling and VM disk attach

## Main files changed
- `BackupUI/Windows/RestoreWindowNew.xaml`
- `BackupUI/Windows/RestoreWindowNew.xaml.cs`
- `BackupUI/Services/BackupMountManager.cs`
- `BackupUI.Tests/RegularHyperVRestoreHelperTests.cs`
- `BackupUI/VersionClass.cs`
- `Directory.Build.props`

## Validation status
Build:
- `run_build` successful after changes

Tests run:
- `SecureServerBackup.Tests.HyperVRestorePointHelperTests`
- `SecureServerBackup.Tests.RegularHyperVRestoreHelperTests`
- Result: `11 passed, 0 failed`

## Important limitation
This is only the first Hyper-V restore increment for regular backups.

Implemented now:
- Restore regular backup contents into a `.vhdx`
- Optionally attach that restored disk to an existing Hyper-V VM

Not implemented yet:
- Automatic creation of a brand-new Hyper-V VM from a regular backup restore
- Generation-aware VM creation and boot-device selection
- Explicit IDE/SCSI controller selection for attached disks
- Full validation that a restored bootable Windows backup becomes a bootable guest without additional boot repair steps

## Likely next steps
Recommended next work:
1. Add creation of a brand-new Hyper-V VM from a regular backup restore target
2. Detect whether the restored backup is bootable and guide Generation 1 vs Generation 2 choice
3. Add controller selection and boot-disk selection when attaching to existing or new VMs
4. Add tests for the new restore-target selection logic and any VM-creation helper logic
5. Consider replacing PowerShell-based Hyper-V management with a more structured native or managed abstraction if this area grows

## Notes for resume
If resuming this work tomorrow, start in:
- `BackupUI/Windows/RestoreWindowNew.xaml.cs`

Focus areas:
- `RestoreTargetKind.HyperVVirtualDisk`
- `RestoreToHyperVVirtualDisk(...)`
- Hyper-V VM selection/attachment UI
- Existing Hyper-V VM import path vs regular-backup Hyper-V VHDX path
