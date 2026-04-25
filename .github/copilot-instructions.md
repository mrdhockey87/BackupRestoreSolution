# Copilot Instructions

## General Guidelines
- When making phased code changes, continue through and fix build errors before pausing so the current implementation compiles cleanly.
- Always update `Directory.Build.props` and `BackupUI/VersionClass.cs` version numbers/notes for restore workflow changes, and keep the newest release note at the top of the notes block.

## Backup Password Management
- Use DPAPI LocalMachine for stored scheduled-backup passwords.
- On the New/Edit Backup page, show a masked password field with a non-persistent Show Password checkbox; only decrypt/display when checked.
- Make the password non-editable once initially saved.
- Include a Verify Password field only for the initial save.
- Add an Encrypted indicator in the Mount/Verify UI to show whether a password is required.

## Linux Restore Support
- Update the LinuxRestore project in the repo to support restoring encrypted backups by prompting for a password in the Linux recovery environment.

## Restore Process
- When restoring from the Restore tab, check whether the selected backup targets the currently booted disk/volume.
  - If it does, ask whether to restore to a non-boot disk/volume; if yes, preload the selected backup into `RestoreWindowNew` and allow choosing a non-boot destination there.
  - If the user opts not to restore to a non-boot disk/volume, alert that boot-drive restores must be done from the recovery disk.
- If the selected backup does not target the booted disk/volume, preload the selected backup into `RestoreWindowNew`.
- From `RestoreWindowNew`, allow target selection by restore type:
  - Disk restores should reconstruct the disk layout and account for user-modified partition layout and partition sizes, ensuring accurate metadata handling in both Windows and Linux.
  - Volume restores should format the target volume.
  - Both must warn that the target will be formatted and all data lost.
  - Restore progress should show a progress bar plus the current file/folder name being restored.
