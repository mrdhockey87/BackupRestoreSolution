# Copilot Instructions

## Azure Guidelines
- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool, ask the user to enable it.

## Versioning Rules
- When updating versions, always update all three together:
  - `Directory.Build.props` ProductVersion/current version text
  - `BackupUI\VersionClass.cs` version_fallback_number
  - `VersionClass.cs` release note entry using the older concise format used around version 6.1.3.7 and below.
- Ensure that the notes in `BackupUI\VersionClass.cs` are included in the last version entries, maintaining proper note details while still using the older concise release-note style.
- Ensure the `VersionClass` fallback number always matches the current `ProductVersion` in `Directory.Build.props` and that the UI-fix release note is present in `VersionClass.cs`.

## Backup Password Management
- Use DPAPI LocalMachine for stored scheduled-backup passwords.
- On the New/Edit Backup page, show a masked password field with a non-persistent Show Password checkbox; only decrypt/display when checked.
- Make the password non-editable once initially saved.
- Include a Verify Password field only for the initial save.
- Add an Encrypted indicator in the Mount/Verify UI to show whether a password is required.

## Linux Restore Support
- Update the LinuxRestore project in the repo to support restoring encrypted backups by prompting for a password in the Linux recovery environment.
