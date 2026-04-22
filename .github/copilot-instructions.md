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
