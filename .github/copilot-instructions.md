# Copilot Instructions

## General Guidelines
- When making phased code changes, continue through and fix build errors before pausing so the current implementation compiles cleanly.
- Always apply the Version Tracker skill for version updates and follow its exact output/update requirements, including updating `Directory.Build.props` and `BackupUI/VersionClass.cs` version numbers/notes for restore workflow changes. Keep the newest release note at the top of the notes block. After every code update in this repo, update the version number and release note entries, including those in `BackupUI/VersionClass.cs` and version properties such as those in `Directory.Build.props`. 
  - Version numbers must never have leading zeros (use 6.2.4.13, not 06.02.04.13). When bumping a version, always read the last version entry in `VersionClass.cs` release notes to determine the current version and increment the last segment by 1 from there. Only bump a higher segment when the lower one reaches 99 or when the change is significant enough to warrant it.
- When a plan is created, continue executing the planned investigation or fix instead of stopping after announcing the first step.
- Always treat compiler warnings as errors — they point to possible bugs. Enable `TreatWarningsAsErrors` in the build configuration for all projects.

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
- When reporting a restore-flow fix, verify it actually reaches the expected UI step and execution sequence instead of assuming a metadata-path change solved the issue.
