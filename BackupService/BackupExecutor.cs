using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SecureServerBackupCommon;

namespace SecureServerBackupService
{
    public class BackupExecutor
    {
        private const string DllName = "SecureServerBackupEngine.dll";
        private static readonly SemaphoreSlim NativeExecutionLock = new(1, 1);

        public static string NormalizeHyperVVirtualMachineName(string displayText)
        {
            if (string.IsNullOrWhiteSpace(displayText))
            {
                return string.Empty;
            }

            int stateIndex = displayText.LastIndexOf(" (", StringComparison.Ordinal);
            return stateIndex > 0 ? displayText[..stateIndex].Trim() : displayText.Trim();
        }

        public static string GetHyperVBackupMode(BackupType backupType, bool hasExistingFullBackup, bool hasAnyExistingBackup)
        {
            return backupType switch
            {
                BackupType.Incremental when hasAnyExistingBackup => "Incremental",
                BackupType.Differential when hasExistingFullBackup => "Differential",
                _ => "Full"
            };
        }

        public static bool HasAnyHyperVBackupPoint(string destinationPath)
        {
            return HasHyperVBackupArchive(destinationPath, requireLegacyFullPoint: false);
        }

        public static bool HasFullHyperVBackupPoint(string destinationPath)
        {
            return HasHyperVBackupArchive(destinationPath, requireLegacyFullPoint: true);
        }

        public static bool ShouldReplaceExistingFullFileArchive(BackupJob job, bool isHyperVBackup, bool isSelectedFilesHistoryBackup)
        {
            ArgumentNullException.ThrowIfNull(job);

            return !isHyperVBackup &&
                   job.Target == BackupTarget.FilesAndFolders &&
                   (job.Type == BackupType.Full || job.Type == BackupType.SelectedFilesAndFolders);
        }

        private static bool HasHyperVBackupArchive(string destinationPath, bool requireLegacyFullPoint)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return false;
            }

            if (File.Exists(destinationPath))
            {
                return !requireLegacyFullPoint;
            }

            if (!Directory.Exists(destinationPath))
            {
                return false;
            }

            if (string.Equals(Path.GetExtension(destinationPath), ".ssb", StringComparison.OrdinalIgnoreCase))
            {
                return !requireLegacyFullPoint || Path.GetFileName(destinationPath).StartsWith("Full_", StringComparison.OrdinalIgnoreCase);
            }

            if (Directory.EnumerateFiles(destinationPath, "*.ssb", SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }

            var legacyBackupPointDirectories = Directory.EnumerateDirectories(destinationPath, "*.ssb", SearchOption.TopDirectoryOnly);
            return requireLegacyFullPoint
                ? legacyBackupPointDirectories.Any(path => Path.GetFileName(path).StartsWith("Full_", StringComparison.OrdinalIgnoreCase))
                : legacyBackupPointDirectories.Any();
        }

        private static string GetHyperVArchivePath(BackupJob job, string normalizedVmName)
        {
            string fileName = job.HyperVMachines.Count > 1
                ? $"{job.Name}_{SanitizeFileName(normalizedVmName)}.ssb"
                : $"{job.Name}.ssb";

            return Path.Combine(job.DestinationPath, fileName);
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "HyperV";
            }

            var invalidCharacters = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalidCharacters.Contains(ch) ? '_' : ch).ToArray());
        }

        public static string GetVirtualDiskClonePathForTest(BackupJob job)
        {
            return job.GetVirtualDiskClonePath();
        }

        public static bool ShouldCloneToVirtualDiskAsDiskForTest(BackupJob job)
        {
            return job.ShouldCloneToVirtualDiskAsDisk();
        }

        private static string EscapePowerShellSingleQuotedString(string value)
        {
            return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
        }

        private static bool IsSelectedFilesHistoryBackup(BackupJob job)
        {
            ArgumentNullException.ThrowIfNull(job);

            return job.Type == BackupType.SelectedFilesAndFolders && job.Target == BackupTarget.FilesAndFolders;
        }

        private static bool ShouldUseSelectedFileBatching(BackupJob job)
        {
            ArgumentNullException.ThrowIfNull(job);

            return IsSelectedFilesHistoryBackup(job);
        }

        private static bool ShouldUseSelectedFileSelections(BackupJob job)
        {
            ArgumentNullException.ThrowIfNull(job);

            return job.Type == BackupType.SelectedFilesAndFolders && job.Target == BackupTarget.FilesAndFolders;
        }

        private static string GetSelectedFilesHistoryArchivePath(BackupJob job, DateTime timestamp)
        {
            ArgumentNullException.ThrowIfNull(job);

            return Path.Combine(job.DestinationPath, $"{job.Name}.ssb");
        }

        public static string GetSelectedFilesHistoryArchivePathForTest(BackupJob job, DateTime timestamp)
        {
            return GetSelectedFilesHistoryArchivePath(job, timestamp);
        }

        private static IReadOnlyList<string> GetSelectedFilesHistoryArchives(string destinationPath, string jobName)
        {
            if (string.IsNullOrWhiteSpace(destinationPath) || string.IsNullOrWhiteSpace(jobName) || !Directory.Exists(destinationPath))
            {
                return Array.Empty<string>();
            }

            string pattern = $"{jobName}_SelectedFiles_*.ssb";
            return Directory.GetFiles(destinationPath, pattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IReadOnlyList<string> ResolveSourcePaths(BackupJob job)
        {
            ArgumentNullException.ThrowIfNull(job);

            if (!ShouldUseSelectedFileSelections(job))
            {
                return job.SourcePaths;
            }

            List<string> persistedPaths = SelectedFileListStore.Load(job.Name);
            return persistedPaths.Count > 0 ? persistedPaths : job.SourcePaths;
        }

        private static bool SelectionPathExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        private sealed record FileSelectionResolution(IReadOnlyList<string> ResolvedPaths, IReadOnlyList<string> MissingPaths);

        private static FileSelectionResolution ResolveSelectedFilePathsForExecution(IReadOnlyList<string> selectedPaths)
        {
            ArgumentNullException.ThrowIfNull(selectedPaths);

            string[] includePaths = selectedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string[] resolvedIncludePaths = includePaths
                .Where(SelectionPathExists)
                .ToArray();

            string[] missingIncludePaths = includePaths
                .Except(resolvedIncludePaths, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new FileSelectionResolution(resolvedIncludePaths, missingIncludePaths);
        }

        private static bool HasAnyRuntimeSelections(BackupJob job, IReadOnlyList<string> sourcePaths)
        {
            ArgumentNullException.ThrowIfNull(job);
            ArgumentNullException.ThrowIfNull(sourcePaths);

            if (job.IsHyperVBackup || job.Target == BackupTarget.HyperV)
            {
                return job.HyperVMachines.Count > 0;
            }

            if (!ShouldUseSelectedFileSelections(job))
            {
                return job.SourcePaths.Any(path => !string.IsNullOrWhiteSpace(path));
            }

            return sourcePaths.Any(path => !string.IsNullOrWhiteSpace(path));
        }

        public static IReadOnlyList<FileBackupBatch> BuildFileBackupBatches(IEnumerable<string> sourcePaths)
        {
            ArgumentNullException.ThrowIfNull(sourcePaths);

            Dictionary<string, HashSet<string>> groupedSelections = new(StringComparer.OrdinalIgnoreCase);

            foreach (string rawPath in sourcePaths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                string normalizedPath = NormalizeBatchPath(rawPath);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                string sourceRoot = DetermineFileBackupSourceRoot(normalizedPath);
                if (string.IsNullOrWhiteSpace(sourceRoot))
                {
                    sourceRoot = normalizedPath;
                }

                if (!groupedSelections.TryGetValue(sourceRoot, out HashSet<string>? selections))
                {
                    selections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    groupedSelections[sourceRoot] = selections;
                }

                selections.Add(normalizedPath);
            }

            return groupedSelections
                .Select(group => new FileBackupBatch(group.Key, group.Value.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()))
                .OrderBy(batch => batch.SourceRoot, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IReadOnlyList<FileBackupBatch> BuildSelectedFileBackupBatches(
            IEnumerable<string> sourceRoots,
            IEnumerable<string> selectedPaths)
        {
            ArgumentNullException.ThrowIfNull(sourceRoots);
            ArgumentNullException.ThrowIfNull(selectedPaths);

            string[] normalizedRoots = sourceRoots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeBatchPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(path => path.Length)
                .ToArray();

            if (normalizedRoots.Length == 0)
            {
                return Array.Empty<FileBackupBatch>();
            }

            Dictionary<string, HashSet<string>> groupedSelections = new(StringComparer.OrdinalIgnoreCase);

            foreach (string rawPath in selectedPaths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                string normalizedPath = NormalizeBatchPath(rawPath);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                string? sourceRoot = FindBestSelectedFileSourceRoot(normalizedPath, normalizedRoots);
                if (string.IsNullOrWhiteSpace(sourceRoot))
                {
                    continue;
                }

                if (!groupedSelections.TryGetValue(sourceRoot, out HashSet<string>? selections))
                {
                    selections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    groupedSelections[sourceRoot] = selections;
                }

                selections.Add(normalizedPath);
            }

            return groupedSelections
                .Select(group => new FileBackupBatch(group.Key, group.Value.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()))
                .OrderBy(batch => batch.SourceRoot, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string NormalizeBatchPath(string path)
        {
            string trimmed = path.Trim();
            string normalized = trimmed.Replace('/', '\\');

            while (normalized.Length > 3 && normalized.EndsWith("\\", StringComparison.Ordinal))
            {
                normalized = normalized[..^1];
            }

            return normalized;
        }

        private static string DetermineFileBackupSourceRoot(string normalizedPath)
        {
            if (HyperVGuestSelectionPath.TryParse(normalizedPath, out _))
            {
                return normalizedPath;
            }

            if (Directory.Exists(normalizedPath))
            {
                return normalizedPath;
            }

            string? directoryName = Path.GetDirectoryName(normalizedPath);
            return string.IsNullOrWhiteSpace(directoryName)
                ? normalizedPath
                : NormalizeBatchPath(directoryName);
        }

        private static string? FindBestSelectedFileSourceRoot(string normalizedPath, IReadOnlyList<string> normalizedRoots)
        {
            foreach (string sourceRoot in normalizedRoots)
            {
                if (IsSelectedPathUnderSourceRoot(normalizedPath, sourceRoot))
                {
                    return sourceRoot;
                }
            }

            return null;
        }

        private static bool IsSelectedPathUnderSourceRoot(string normalizedPath, string sourceRoot)
        {
            HyperVGuestSelectionInfo? parsedSelectedGuestPath;
            HyperVGuestSelectionInfo? parsedSourceGuestRoot;
            bool selectedGuestParsed = HyperVGuestSelectionPath.TryParse(normalizedPath, out parsedSelectedGuestPath);
            bool sourceGuestParsed = HyperVGuestSelectionPath.TryParse(sourceRoot, out parsedSourceGuestRoot);

            if (selectedGuestParsed && sourceGuestParsed)
            {
                if (parsedSelectedGuestPath == null || parsedSourceGuestRoot == null)
                {
                    return false;
                }

                HyperVGuestSelectionInfo selectedGuest = parsedSelectedGuestPath;
                HyperVGuestSelectionInfo sourceGuest = parsedSourceGuestRoot;

                if (!string.Equals(selectedGuest.VirtualMachineName, sourceGuest.VirtualMachineName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(selectedGuest.VirtualDiskPath, sourceGuest.VirtualDiskPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return sourceGuest.Kind switch
                {
                    HyperVGuestSelectionKind.VirtualDisk => true,
                    HyperVGuestSelectionKind.Volume => selectedGuest.PartitionNumber == sourceGuest.PartitionNumber,
                    _ => false
                };
            }

            if (HyperVGuestSelectionPath.IsEncodedPath(normalizedPath) || HyperVGuestSelectionPath.IsEncodedPath(sourceRoot))
            {
                return false;
            }

            if (string.Equals(normalizedPath, sourceRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string sourceRootWithSeparator = sourceRoot.EndsWith("\\", StringComparison.Ordinal)
                ? sourceRoot
                : sourceRoot + "\\";

            return normalizedPath.StartsWith(sourceRootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private static void CleanupSelectedFilesHistoryArchives(BackupJob job, string latestArchivePath, Action<string>? logger)
        {
            ArgumentNullException.ThrowIfNull(job);

            int retentionCount = Math.Clamp(job.SelectedFilesRetentionCount, 1, 30);
            IReadOnlyList<string> historyArchives = GetSelectedFilesHistoryArchives(job.DestinationPath, job.Name);

            // Collect archives excluding the latest, sort by write time descending (newest first)
            var matchingArchives = historyArchives
                .Where(archivePath => !string.Equals(archivePath, latestArchivePath, StringComparison.OrdinalIgnoreCase))
                .Select(archivePath => new { Path = archivePath, WriteTime = File.GetLastWriteTimeUtc(archivePath) })
                .OrderByDescending(x => x.WriteTime)
                .ToList();

            // Keep only the most recent N history points (exclude latest which is already excluded)
            var archivesToDelete = matchingArchives.Skip(retentionCount).ToList();

            foreach (var archiveInfo in archivesToDelete)
            {
                try
                {
                    File.Delete(archiveInfo.Path);
                    logger?.Invoke($"Removed old Selected Files history point (keeping last {retentionCount} versions): {Path.GetFileName(archiveInfo.Path)}");
                }
                catch (Exception cleanupEx)
                {
                    logger?.Invoke($"Warning: Failed to remove old Selected Files history point '{Path.GetFileName(archiveInfo.Path)}': {cleanupEx.Message}");
                }
            }
        }

        public static void CleanupOldClones(BackupJob job, string latestClonePath, Action<string>? logger)
        {
            ArgumentNullException.ThrowIfNull(job);

            if (string.IsNullOrWhiteSpace(job.DestinationPath) || !Directory.Exists(job.DestinationPath))
            {
                return;
            }

            int retentionCount = Math.Clamp(job.CloneRetentionCount, 1, 30);

            try
            {
                // For CloneHyperVSystem: directories are created with job name or renamed VM name
                // For CloneToVirtualDisk: VHDX files are created with job name
                string[] subdirectories = Directory.GetDirectories(job.DestinationPath);

                // Collect directories matching the job name pattern, excluding the latest clone
                var matchingDirs = subdirectories
                    .Where(dir => !string.Equals(dir, latestClonePath, StringComparison.OrdinalIgnoreCase))
                    .Where(dir => Path.GetFileName(dir).StartsWith(job.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(dir => new { Path = dir, WriteTime = Directory.GetLastWriteTimeUtc(dir) })
                    .OrderByDescending(x => x.WriteTime)
                    .ToList();

                // Keep only the most recent N clones (exclude latest which is already excluded)
                var dirsToDelete = matchingDirs.Skip(retentionCount).ToList();

                foreach (var dirInfo in dirsToDelete)
                {
                    try
                    {
                        Directory.Delete(dirInfo.Path, recursive: true);
                        logger?.Invoke($"Removed old clone directory (keeping last {retentionCount} clones): {Path.GetFileName(dirInfo.Path)}");
                    }
                    catch (Exception cleanupEx)
                    {
                        logger?.Invoke($"Warning: Failed to remove old clone directory '{Path.GetFileName(dirInfo.Path)}': {cleanupEx.Message}");
                    }
                }

                // Also cleanup old VHDX files for CloneToVirtualDisk
                if (job.Type == BackupType.CloneToVirtualDisk)
                {
                    string[] vhdxFiles = Directory.GetFiles(job.DestinationPath, "*.vhdx");

                    // Collect VHDX files matching the job name pattern, excluding the latest clone
                    var matchingVhdx = vhdxFiles
                        .Where(vhdx => !string.Equals(vhdx, latestClonePath, StringComparison.OrdinalIgnoreCase))
                        .Where(vhdx => Path.GetFileNameWithoutExtension(vhdx).StartsWith(job.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(vhdx => new { Path = vhdx, WriteTime = File.GetLastWriteTimeUtc(vhdx) })
                        .OrderByDescending(x => x.WriteTime)
                        .ToList();

                    // Keep only the most recent N clones (exclude latest which is already excluded)
                    var vhdxToDelete = matchingVhdx.Skip(retentionCount).ToList();

                    foreach (var vhdxInfo in vhdxToDelete)
                    {
                        try
                        {
                            File.Delete(vhdxInfo.Path);
                            logger?.Invoke($"Removed old clone VHDX (keeping last {retentionCount} clones): {Path.GetFileName(vhdxInfo.Path)}");
                        }
                        catch (Exception cleanupEx)
                        {
                            logger?.Invoke($"Warning: Failed to remove old clone VHDX '{Path.GetFileName(vhdxInfo.Path)}': {cleanupEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Warning: Error during clone retention cleanup: {ex.Message}");
            }
        }

        private static string RunPowerShell(string script)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(script);

            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var output = new StringBuilder();
            var errors = new StringBuilder();
            object syncRoot = new();

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    return;
                }

                lock (syncRoot)
                {
                    output.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    return;
                }

                lock (syncRoot)
                {
                    errors.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit((int)TimeSpan.FromHours(2).TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                throw new TimeoutException("The PowerShell command timed out after waiting 2 hours.");
            }

            process.WaitForExit();

            string outputText;
            string errorText;
            lock (syncRoot)
            {
                outputText = output.ToString();
                errorText = errors.ToString();
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorText) ? "The PowerShell command failed." : errorText.Trim());
            }

            return outputText;
        }

        private static string CreateHyperVExportPoint(string exportRootPath, string backupType, string vmName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(exportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(backupType);
            ArgumentException.ThrowIfNullOrWhiteSpace(vmName);

            string pointId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string backupPointPath = Path.Combine(exportRootPath, $"{backupType}_{pointId}.ssb");
            string exportPath = Path.Combine(backupPointPath, "Export");

            Directory.CreateDirectory(exportPath);

            string metadataPath = Path.Combine(backupPointPath, "hyperv_backup_info.txt");
            string metadata = string.Join(
                Environment.NewLine,
                $"Type={backupType}",
                $"PointId={pointId}",
                $"VmName={vmName}",
                $"ExportPath={exportPath}");

            File.WriteAllText(metadataPath, metadata + Environment.NewLine);
            return backupPointPath;
        }

        private static string ExportHyperVVmWithPowerShell(string vmName, string exportRootPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(vmName);
            ArgumentException.ThrowIfNullOrWhiteSpace(exportRootPath);

            string backupPointPath = CreateHyperVExportPoint(exportRootPath, "Full", vmName);
            string exportPath = Path.Combine(backupPointPath, "Export");
            string escapedVmName = EscapePowerShellSingleQuotedString(vmName);
            string escapedExportPath = EscapePowerShellSingleQuotedString(exportPath);

            string script =
                "$ProgressPreference = 'SilentlyContinue'; $VerbosePreference = 'SilentlyContinue'; $WarningPreference = 'Continue'; " +
                $"Import-Module Hyper-V -ErrorAction Stop; " +
                $"$vmName = '{escapedVmName}'; " +
                $"$exportPath = '{escapedExportPath}'; " +
                "try { Export-VM -Name $vmName -Path $exportPath -CaptureLiveState CaptureDataConsistentState -ErrorAction Stop | Out-Null } " +
                "catch { Export-VM -Name $vmName -Path $exportPath -ErrorAction Stop | Out-Null }";

            RunPowerShell(script);
            return backupPointPath;
        }

        private static string FindNewestHyperVExportPoint(string exportRootPath)
        {
            if (!Directory.Exists(exportRootPath))
            {
                return string.Empty;
            }

            return Directory.EnumerateDirectories(exportRootPath, "*.ssb", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? string.Empty;
        }

        private static string ResolveHyperVExportPath(string exportPointPath)
        {
            if (string.IsNullOrWhiteSpace(exportPointPath) || !Directory.Exists(exportPointPath))
            {
                return string.Empty;
            }

            string exportPath = Path.Combine(exportPointPath, "Export");
            return Directory.Exists(exportPath) ? exportPath : string.Empty;
        }

        private static string FindPrimaryHyperVVirtualDisk(string exportPointPath)
        {
            string exportPath = ResolveHyperVExportPath(exportPointPath);
            if (string.IsNullOrWhiteSpace(exportPath))
            {
                return string.Empty;
            }

            return Directory.EnumerateFiles(exportPath, "*.vhd*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.Length)
                .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(file => file.FullName)
                .FirstOrDefault() ?? string.Empty;
        }

        private static int MountVirtualDiskAndGetDiskNumber(string virtualDiskPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(virtualDiskPath);

            string escapedPath = EscapePowerShellSingleQuotedString(virtualDiskPath);
            string script = $"Mount-DiskImage -ImagePath '{escapedPath}' -Access ReadOnly -ErrorAction Stop | Out-Null; Start-Sleep -Milliseconds 500; (Get-DiskImage -ImagePath '{escapedPath}' -ErrorAction Stop | Get-Disk -ErrorAction Stop | Select-Object -ExpandProperty Number)";

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            string output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
            string errors = process?.StandardError.ReadToEnd() ?? string.Empty;
            process?.WaitForExit();

            if (process == null || process.ExitCode != 0 ||
                !int.TryParse(output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault(), out int diskNumber))
            {
                throw new InvalidOperationException($"Failed to mount the exported Hyper-V virtual disk. {errors}".Trim());
            }

            return diskNumber;
        }

        private static void UnmountVirtualDisk(string virtualDiskPath)
        {
            if (string.IsNullOrWhiteSpace(virtualDiskPath))
            {
                return;
            }

            string escapedPath = EscapePowerShellSingleQuotedString(virtualDiskPath);
            string script = $"Dismount-DiskImage -ImagePath '{escapedPath}' -ErrorAction Stop";

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            string errors = process?.StandardError.ReadToEnd() ?? string.Empty;
            process?.WaitForExit();

            if (process == null || process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to unmount the exported Hyper-V virtual disk. {errors}".Trim());
            }
        }

        private sealed record MountedHyperVGuestPartition(int PartitionNumber, string MountPath, bool CreatedMountDirectory);

        private sealed class MountedHyperVGuestDisk : IDisposable
        {
            private readonly List<MountedHyperVGuestPartition> _partitions;

            public MountedHyperVGuestDisk(string virtualDiskPath, List<MountedHyperVGuestPartition> partitions)
            {
                VirtualDiskPath = virtualDiskPath;
                _partitions = partitions;
            }

            public string VirtualDiskPath { get; }

            public IReadOnlyList<MountedHyperVGuestPartition> Partitions => _partitions;

            public void Dispose()
            {
                try
                {
                    UnmountVirtualDisk(VirtualDiskPath);
                }
                catch
                {
                }

                foreach (MountedHyperVGuestPartition partition in _partitions.Where(partition => partition.CreatedMountDirectory))
                {
                    try
                    {
                        if (Directory.Exists(partition.MountPath))
                        {
                            Directory.Delete(partition.MountPath, recursive: true);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static MountedHyperVGuestDisk MountHyperVGuestDiskReadOnly(string vmName, string virtualDiskPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(vmName);
            ArgumentException.ThrowIfNullOrWhiteSpace(virtualDiskPath);

            string mountRoot = Path.Combine(Path.GetTempPath(), "SecureServerBackup", "HyperVGuestMounts", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(mountRoot);

            string escapedPath = EscapePowerShellSingleQuotedString(virtualDiskPath);
            string escapedRoot = EscapePowerShellSingleQuotedString(mountRoot);
            string script = $"$image = Mount-DiskImage -ImagePath '{escapedPath}' -Access ReadOnly -PassThru -ErrorAction Stop; $disk = $image | Get-Disk -ErrorAction Stop; Get-Partition -DiskNumber $disk.Number -ErrorAction Stop | Sort-Object PartitionNumber | ForEach-Object {{ $partition = $_; $mountPath = $null; $created = $false; if ($partition.AccessPaths) {{ $mountPath = @($partition.AccessPaths | Where-Object {{ $_ }}) | Select-Object -First 1; }} if ([string]::IsNullOrWhiteSpace($mountPath)) {{ $folder = Join-Path '{escapedRoot}' ('Partition' + $partition.PartitionNumber); New-Item -ItemType Directory -Path $folder -Force | Out-Null; Add-PartitionAccessPath -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber -AccessPath $folder -ErrorAction Stop | Out-Null; $mountPath = $folder; $created = $true; }} [Console]::WriteLine(($partition.PartitionNumber.ToString() + \"`t\" + $mountPath + \"`t\" + $created.ToString())); }}";

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            string output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
            string errors = process?.StandardError.ReadToEnd() ?? string.Empty;
            process?.WaitForExit();

            if (process == null || process.ExitCode != 0)
            {
                try
                {
                    Directory.Delete(mountRoot, recursive: true);
                }
                catch
                {
                }

                throw new InvalidOperationException($"Failed to mount Hyper-V guest disk '{vmName}'. {errors}".Trim());
            }

            var partitions = new List<MountedHyperVGuestPartition>();
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 3 ||
                    !int.TryParse(parts[0], out int partitionNumber) ||
                    string.IsNullOrWhiteSpace(parts[1]))
                {
                    continue;
                }

                partitions.Add(new MountedHyperVGuestPartition(
                    partitionNumber,
                    parts[1].Trim(),
                    bool.TryParse(parts[2], out bool createdMountDirectory) && createdMountDirectory));
            }

            return new MountedHyperVGuestDisk(virtualDiskPath, partitions);
        }

        private static IReadOnlyList<string> ResolveHyperVGuestSourcePaths(HyperVGuestSelectionInfo selection, MountedHyperVGuestDisk mountedDisk)
        {
            ArgumentNullException.ThrowIfNull(mountedDisk);

            return HyperVGuestSelectionPath.GetCandidateSourcePaths(
                    selection,
                    mountedDisk.Partitions.Select(partition => new HyperVGuestMountedPartition(partition.PartitionNumber, partition.MountPath)))
                .Where(Directory.Exists)
                .ToArray();
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ProgressCallback(int percentage, [MarshalAs(UnmanagedType.LPWStr)] string message);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void LogCallback(
            int level,
            [MarshalAs(UnmanagedType.LPWStr)] string message,
            [MarshalAs(UnmanagedType.LPWStr)] string details);

        private static void EncryptBackupFileIfNeeded(BackupJob job, string backupPath, Action<int, string>? progressCallback, Action<string>? logger)
        {
            if (!job.EncryptBackup)
            {
                return;
            }

            progressCallback?.Invoke(88, "Encrypting backup archive with AES-128...");
            logger?.Invoke("Encrypting backup archive with AES-128...");
            string password = BackupEncryptionService.UnprotectPassword(job.ProtectedEncryptionPassword);
            BackupEncryptionService.EncryptFile(backupPath, backupPath, password);
            logger?.Invoke("Backup archive encrypted successfully.");
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupFiles(string sourcePath, string destPath,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount, ProgressCallback? callback, LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupFilesBySelections(
            string sourceRoot,
            string destPath,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] includePaths,
            int includePathCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount,
            ProgressCallback? callback,
            LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupVolume(string volumePath, string destPath,
            [MarshalAs(UnmanagedType.I1)] bool includeSystemState,
            [MarshalAs(UnmanagedType.I1)] bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount,
            ProgressCallback? callback, LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupDisk(int diskNumber, string destPath,
            [MarshalAs(UnmanagedType.I1)] bool includeSystemState,
            [MarshalAs(UnmanagedType.I1)] bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount,
            ProgressCallback? callback, LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupDiskIncremental(int diskNumber, string destPath,
            [MarshalAs(UnmanagedType.I1)] bool includeSystemState,
            [MarshalAs(UnmanagedType.I1)] bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount,
            ProgressCallback? callback, LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupDiskDifferential(int diskNumber, string destPath,
            [MarshalAs(UnmanagedType.I1)] bool includeSystemState,
            [MarshalAs(UnmanagedType.I1)] bool compress,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] userExclusions,
            int userExclusionCount,
            ProgressCallback? callback, LogCallback? logCallback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupHyperVVM(string vmName, string destPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupHyperVVMIncremental(string vmName, string destPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int BackupHyperVVMDifferential(string vmName, string destPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int CreateIncrementalBackup(string sourcePath, string destPath, 
            string baseBackupPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int CreateDifferentialBackup(string sourcePath, string destPath, 
            string fullBackupPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int VerifyBackup(string backupPath, ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int VerifyWimArchive(
            string archivePath, 
            int expectedImageCount, 
            StringBuilder errorMsg, 
            int errorMsgSize, 
            ProgressCallback? callback);

        public enum DismImageHealthState
        {
            Healthy = 0,
            Repairable = 1,
            NonRepairable = 2
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int CheckBackupImageHealth(
            string backupPath,
            int imageIndex,
            [MarshalAs(UnmanagedType.I1)] bool scanImage,
            StringBuilder healthMessage,
            int healthMessageSize,
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int RestoreBackupImageHealth(
            string backupPath,
            int imageIndex,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? sourcePaths,
            int sourcePathCount,
            [MarshalAs(UnmanagedType.I1)] bool limitAccess,
            StringBuilder healthMessage,
            int healthMessageSize,
            ProgressCallback? callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern void GetLastErrorMessage(StringBuilder buffer, int bufferSize);

        // Job context functions - tells C++ engine which job is running for logging
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern void SetCurrentJobName(string jobName);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ClearCurrentJobName();

        public async Task<bool> ExecuteBackupJobWithProgress(
            BackupJob job,
            Action<int, string>? progressCallback,
            CancellationToken cancellationToken,
            Action<string>? logger = null)
        {
            var originalPriority = ProcessPriorityClass.Normal;

            await NativeExecutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        try
                        {
                            originalPriority = Process.GetCurrentProcess().PriorityClass;
                            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
                            logger?.Invoke($"Process priority set to BelowNormal for backup operation (was {originalPriority})");
                        }
                        catch (Exception prioEx)
                        {
                            logger?.Invoke($"Warning: Could not set process priority: {prioEx.Message}");
                        }

                        try
                        {
                            SetCurrentJobName(job.Name);
                            logger?.Invoke($"C++ engine logging context set to: {job.Name}");
                        }
                        catch (Exception jobNameEx)
                        {
                            logger?.Invoke($"Warning: Could not set C++ job name context: {jobNameEx.Message}");
                        }

                        logger?.Invoke($"Starting backup job: {job.Name}");
                        progressCallback?.Invoke(0, "Initializing backup...");

                        if (cancellationToken.IsCancellationRequested)
                        {
                            logger?.Invoke("Backup cancelled by user");
                            return false;
                        }

                        BackupType originalType = job.Type;
                        if (job.ForceFullBackupOnNextRun && (job.Type == BackupType.Incremental || job.Type == BackupType.Differential))
                        {
                            logger?.Invoke($"AUTO-RECOVERY MODE: Previous {originalType} backup failed verification");
                            logger?.Invoke("Forcing FULL backup to rebuild backup chain");
                            job.Type = BackupType.Full;
                            job.ForceFullBackupOnNextRun = false;

                            try
                            {
                                var jobManager = new JobManager();
                                jobManager.UpdateJob(job);
                                logger?.Invoke("ForceFullBackupOnNextRun flag cleared");
                            }
                            catch (Exception saveEx)
                            {
                                logger?.Invoke($"Warning: Failed to clear ForceFullBackupOnNextRun flag: {saveEx.Message}");
                            }
                        }

                        ProgressCallback? nativeCallback = null;
                        if (progressCallback != null)
                        {
                            nativeCallback = (percentage, message) =>
                            {
                                progressCallback(percentage, message ?? $"Progress: {percentage}%");
                            };
                        }

                        LogCallback nativeLogCallback = (level, message, details) =>
                        {
                            LogFromEngine(job.Name, level, message, details);
                        };

                        bool isHyperVBackup = job.IsHyperVBackup || job.Target == BackupTarget.HyperV;
                        bool shouldUseSelectedFileBatching = ShouldUseSelectedFileBatching(job);
                        IReadOnlyList<string> sourcePaths = ResolveSourcePaths(job);

                        if (!HasAnyRuntimeSelections(job, sourcePaths))
                        {
                            logger?.Invoke(isHyperVBackup
                                ? "[ERROR] Backup failed because the saved Hyper-V selection is missing. Edit the backup and select a Hyper-V system to back up."
                                : "[ERROR] Backup failed because the saved selection is missing. Edit the backup and select something to back up.");
                            return false;
                        }

                        // Clone jobs (CloneToVirtualDisk and CloneHyperVSystem) now execute through shared helpers
                        if (job.Type == BackupType.CloneToVirtualDisk)
                        {
                            logger?.Invoke($"[CLONE] Executing CloneToVirtualDisk job '{job.Name}'...");
                            logger?.Invoke($"[CLONE] Retention policy: keep last {job.CloneRetentionCount} clones.");
                            logger?.Invoke($"[CLONE] Clone destination: {job.GetVirtualDiskClonePath()}");

                            try
                            {
                                SecureServerBackupCommon.BackupEngineInterop.ProgressCallback commonCallback = (percentage, message) =>
                                {
                                    nativeCallback?.Invoke(percentage, message);
                                    progressCallback?.Invoke(percentage, message);
                                };

                                bool cloneSuccess = CloneExecutionHelper.ExecuteCloneToVirtualDiskJob(job, commonCallback);

                                if (cloneSuccess)
                                {
                                    logger?.Invoke("[CLONE] Clone to Virtual Disk completed successfully");
                                    string latestClonePath = job.GetVirtualDiskClonePath();
                                    CleanupOldClones(job, latestClonePath, logger);
                                    progressCallback?.Invoke(100, "Clone completed successfully!");
                                }
                                else
                                {
                                    logger?.Invoke("[ERROR] Clone to Virtual Disk failed");
                                }

                                return cloneSuccess;
                            }
                            catch (Exception cloneEx)
                            {
                                logger?.Invoke($"[ERROR] Clone to Virtual Disk failed: {cloneEx.Message}");
                                return false;
                            }
                        }

                        if (job.Type == BackupType.CloneHyperVSystem)
                        {
                            logger?.Invoke($"[CLONE] Executing CloneHyperVSystem job '{job.Name}'...");
                            logger?.Invoke($"[CLONE] Retention policy: keep last {job.CloneRetentionCount} clones.");
                            logger?.Invoke($"[CLONE] Clone destination: {Path.Combine(job.DestinationPath, job.Name)}");

                            try
                            {
                                SecureServerBackupCommon.BackupEngineInterop.ProgressCallback commonCallback = (percentage, message) =>
                                {
                                    nativeCallback?.Invoke(percentage, message);
                                    progressCallback?.Invoke(percentage, message);
                                };

                                CloneExecutionHelper.ExecuteCloneHyperVSystemJob(job, commonCallback);
                                logger?.Invoke("[CLONE] Clone Hyper-V System completed successfully");
                                string latestClonePath = Path.Combine(job.DestinationPath, job.Name);
                                CleanupOldClones(job, latestClonePath, logger);
                                progressCallback?.Invoke(100, "Clone completed successfully!");
                                return true;
                            }
                            catch (Exception cloneEx)
                            {
                                logger?.Invoke($"[ERROR] Clone Hyper-V System failed: {cloneEx.Message}");
                                return false;
                            }
                        }

                        string? newBackupPath = shouldUseSelectedFileBatching
                            ? GetSelectedFilesHistoryArchivePath(job, DateTime.Now)
                            : Path.Combine(job.DestinationPath, $"{job.Name}.ssb");
                        if (!isHyperVBackup && (job.Type == BackupType.Incremental || job.Type == BackupType.Differential))
                        {
                            if (!File.Exists(newBackupPath))
                            {
                                logger?.Invoke($"No base backup exists. Automatically switching from {job.Type} to Full backup: {job.Name}.ssb");
                                job.Type = BackupType.Full;
                            }
                        }

                        Directory.CreateDirectory(job.DestinationPath);

                        bool shouldReplaceExistingFileArchive = ShouldReplaceExistingFullFileArchive(job, isHyperVBackup, shouldUseSelectedFileBatching);

                        if (shouldReplaceExistingFileArchive && File.Exists(newBackupPath))
                        {
                            logger?.Invoke($"Removing previous file backup archive before creating a new full backup: {Path.GetFileName(newBackupPath)}");
                            File.Delete(newBackupPath);
                        }

                        logger?.Invoke($"Creating backup file: {Path.GetFileName(newBackupPath)}");

                        if (shouldUseSelectedFileBatching)
                        {
                            IReadOnlyList<FileBackupBatch> fileBackupBatches = BuildSelectedFileBackupBatches(job.SelectedFilesSourceRoots, sourcePaths);

                            if (fileBackupBatches.Count == 0)
                            {
                                logger?.Invoke("[ERROR] Selected Files backup could not start because the saved source roots are missing or no selected files and folders still map to those roots.");
                                return false;
                            }

                            foreach (FileBackupBatch batch in fileBackupBatches)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    logger?.Invoke("Backup cancelled by user");
                                    return false;
                                }

                                int result;
                                string sourcePath = batch.SourceRoot;
                                if (HyperVGuestSelectionPath.TryParse(sourcePath, out HyperVGuestSelectionInfo? guestSelection) && guestSelection != null)
                                {
                                    logger?.Invoke($"Mounting Hyper-V guest disk selection from VM '{guestSelection.VirtualMachineName}': {guestSelection.VirtualDiskPath}");

                                    using var mountedDisk = MountHyperVGuestDiskReadOnly(guestSelection.VirtualMachineName, guestSelection.VirtualDiskPath);
                                    IReadOnlyList<string> resolvedSourcePaths = ResolveHyperVGuestSourcePaths(guestSelection, mountedDisk);
                                    if (resolvedSourcePaths.Count == 0)
                                    {
                                        logger?.Invoke($"[ERROR] Hyper-V guest selection could not be resolved: {sourcePath}");
                                        return false;
                                    }

                                    result = 0;
                                    foreach (string resolvedSourcePath in resolvedSourcePaths)
                                    {
                                        logger?.Invoke($"Backing up mounted Hyper-V guest path: {resolvedSourcePath}");
                                        result = ExecuteFileSelectionBackup(job, batch.SelectedPaths, resolvedSourcePath, newBackupPath, nativeCallback, nativeLogCallback, logger);
                                        if (result != 0)
                                        {
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    result = ExecuteFileSelectionBackup(job, batch.SelectedPaths, sourcePath, newBackupPath, nativeCallback, nativeLogCallback, logger);
                                }

                                if (result != 0)
                                {
                                    var error = new StringBuilder(1024);
                                    GetLastErrorMessage(error, error.Capacity);

                                    string errorMessage = error.ToString();
                                    logger?.Invoke($"[ERROR] Backup failed with code {result}");
                                    logger?.Invoke($"[ERROR] Error message: {(string.IsNullOrEmpty(errorMessage) ? "(empty - C++ didn't set error message)" : errorMessage)}");
                                    logger?.Invoke($"[ERROR] Source path: {sourcePath}");
                                    logger?.Invoke($"[ERROR] Destination path: {newBackupPath}");
                                    logger?.Invoke($"[DEBUG] Failed backup file preserved for analysis: {Path.GetFileName(newBackupPath)}");
                                    return false;
                                }
                            }
                        }
                        else if (!isHyperVBackup)
                        {
                            foreach (string sourcePath in sourcePaths)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    logger?.Invoke("Backup cancelled by user");
                                    return false;
                                }

                                int result = ExecuteBackup(job, sourcePath, newBackupPath, nativeCallback, nativeLogCallback, logger);
                                if (result != 0)
                                {
                                    var error = new StringBuilder(1024);
                                    GetLastErrorMessage(error, error.Capacity);

                                    string errorMessage = error.ToString();
                                    logger?.Invoke($"[ERROR] Backup failed with code {result}");
                                    logger?.Invoke($"[ERROR] Error message: {(string.IsNullOrEmpty(errorMessage) ? "(empty - C++ didn't set error message)" : errorMessage)}");
                                    logger?.Invoke($"[ERROR] Source path: {sourcePath}");
                                    logger?.Invoke($"[ERROR] Destination path: {newBackupPath}");
                                    logger?.Invoke($"[DEBUG] Failed backup file preserved for analysis: {Path.GetFileName(newBackupPath)}");
                                    return false;
                                }
                            }
                        }

                        if (isHyperVBackup)
                        {
                            foreach (var vm in job.HyperVMachines)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    logger?.Invoke("Backup cancelled by user");
                                    return false;
                                }

                                string normalizedVmName = NormalizeHyperVVirtualMachineName(vm);
                                if (string.IsNullOrWhiteSpace(normalizedVmName))
                                {
                                    logger?.Invoke("Hyper-V backup failed: the selected virtual machine name is empty after normalization.");
                                    return false;
                                }

                                newBackupPath = GetHyperVArchivePath(job, normalizedVmName);

                                logger?.Invoke($"Creating Hyper-V backup archive: {Path.GetFileName(newBackupPath)}");
                                progressCallback?.Invoke(0, $"Backing up Hyper-V VM: {normalizedVmName}...");

                                bool hasAnyExistingHyperVPoint = HasAnyHyperVBackupPoint(newBackupPath);
                                bool hasExistingFullHyperVPoint = HasFullHyperVBackupPoint(newBackupPath);

                                if ((job.Type == BackupType.Incremental || job.Type == BackupType.Differential) && !hasAnyExistingHyperVPoint)
                                {
                                    logger?.Invoke($"No Hyper-V base backup archive exists. Automatically switching from {job.Type} to Full backup for VM: {normalizedVmName}");
                                }

                                string hyperVBackupMode = GetHyperVBackupMode(job.Type, hasExistingFullHyperVPoint, hasAnyExistingHyperVPoint);

                                string temporaryExportRoot = Path.Combine(Path.GetTempPath(), "SecureServerBackup", "HyperV", Guid.NewGuid().ToString("N"));
                                Directory.CreateDirectory(temporaryExportRoot);

                                int result;
                                string exportPointPath = string.Empty;
                                string virtualDiskPath = string.Empty;
                                int mountedDiskNumber = -1;

                                try
                                {
                                    result = hyperVBackupMode switch
                                    {
                                        "Incremental" => BackupHyperVVMIncremental(normalizedVmName, temporaryExportRoot, nativeCallback),
                                        "Differential" => BackupHyperVVMDifferential(normalizedVmName, temporaryExportRoot, nativeCallback),
                                        _ => BackupHyperVVM(normalizedVmName, temporaryExportRoot, nativeCallback)
                                    };

                                    if (result != 0)
                                    {
                                        var exportError = new StringBuilder(1024);
                                        GetLastErrorMessage(exportError, exportError.Capacity);
                                        string exportErrorMessage = exportError.ToString();
                                        if (string.Equals(hyperVBackupMode, "Full", StringComparison.OrdinalIgnoreCase) &&
                                            exportErrorMessage.Contains("32773", StringComparison.Ordinal))
                                        {
                                            logger?.Invoke("Native Hyper-V full export returned 32773. Trying PowerShell Export-VM fallback.");

                                            try
                                            {
                                                logger?.Invoke($"Starting PowerShell Hyper-V export fallback for VM: {normalizedVmName}");
                                                progressCallback?.Invoke(35, $"Retrying Hyper-V export for {normalizedVmName} using PowerShell...");
                                                exportPointPath = ExportHyperVVmWithPowerShell(normalizedVmName, temporaryExportRoot);
                                                logger?.Invoke($"PowerShell Hyper-V export fallback completed for VM: {normalizedVmName}");
                                                result = 0;
                                            }
                                            catch (Exception fallbackEx)
                                            {
                                                logger?.Invoke($"Hyper-V export fallback failed: {fallbackEx.Message}");
                                                logger?.Invoke($"Hyper-V export failed: {exportErrorMessage}");
                                                return false;
                                            }
                                        }
                                        else
                                        {
                                            logger?.Invoke($"Hyper-V export failed: {exportErrorMessage}");
                                            return false;
                                        }
                                    }

                                    if (string.IsNullOrWhiteSpace(exportPointPath))
                                    {
                                        exportPointPath = FindNewestHyperVExportPoint(temporaryExportRoot);
                                    }

                                    if (string.IsNullOrWhiteSpace(exportPointPath))
                                    {
                                        logger?.Invoke("Hyper-V backup failed: the temporary export did not create a backup point folder.");
                                        return false;
                                    }

                                    virtualDiskPath = FindPrimaryHyperVVirtualDisk(exportPointPath);
                                    if (string.IsNullOrWhiteSpace(virtualDiskPath))
                                    {
                                        logger?.Invoke("Hyper-V backup failed: the temporary export did not contain a VHD or VHDX disk to capture.");
                                        return false;
                                    }

                                    logger?.Invoke($"Capturing Hyper-V virtual disk into backup archive: {Path.GetFileName(newBackupPath)}");
                                    mountedDiskNumber = MountVirtualDiskAndGetDiskNumber(virtualDiskPath);

                                    result = hyperVBackupMode switch
                                    {
                                        "Incremental" => BackupDiskIncremental(mountedDiskNumber, newBackupPath, includeSystemState: false, compress: true, Array.Empty<string>(), 0, nativeCallback, nativeLogCallback),
                                        "Differential" => BackupDiskDifferential(mountedDiskNumber, newBackupPath, includeSystemState: false, compress: true, Array.Empty<string>(), 0, nativeCallback, nativeLogCallback),
                                        _ => BackupDisk(mountedDiskNumber, newBackupPath, includeSystemState: false, compress: true, Array.Empty<string>(), 0, nativeCallback, nativeLogCallback)
                                    };
                                }
                                finally
                                {
                                    if (!string.IsNullOrWhiteSpace(virtualDiskPath))
                                    {
                                        try
                                        {
                                            UnmountVirtualDisk(virtualDiskPath);
                                        }
                                        catch (Exception unmountEx)
                                        {
                                            logger?.Invoke($"Warning: Failed to unmount the temporary Hyper-V export disk: {unmountEx.Message}");
                                        }
                                    }

                                    try
                                    {
                                        if (Directory.Exists(temporaryExportRoot))
                                        {
                                            Directory.Delete(temporaryExportRoot, recursive: true);
                                        }
                                    }
                                    catch (Exception cleanupEx)
                                    {
                                        logger?.Invoke($"Warning: Failed to delete the temporary Hyper-V export folder: {cleanupEx.Message}");
                                    }
                                }

                                if (result != 0)
                                {
                                    var error = new StringBuilder(1024);
                                    GetLastErrorMessage(error, error.Capacity);
                                    logger?.Invoke($"Hyper-V backup failed: {error}");
                                    return false;
                                }
                            }
                        }

                        if (newBackupPath != null && job.Target != BackupTarget.HyperV)
                        {
                            EncryptBackupFileIfNeeded(job, newBackupPath, progressCallback, logger);
                        }

                        if (shouldUseSelectedFileBatching && newBackupPath != null)
                        {
                            CleanupSelectedFilesHistoryArchives(job, newBackupPath, logger);
                        }

                        if (originalType != job.Type)
                        {
                            job.Type = originalType;
                            try
                            {
                                var jobManager = new JobManager();
                                jobManager.UpdateJob(job);
                                logger?.Invoke($"AUTO-RECOVERY COMPLETE: Job type restored to {originalType} for next run");
                            }
                            catch (Exception saveEx)
                            {
                                logger?.Invoke($"Warning: Failed to restore job type: {saveEx.Message}");
                            }
                        }

                        progressCallback?.Invoke(100, "Backup completed successfully!");
                        logger?.Invoke($"Backup job completed successfully: {job.Name}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke($"Backup job failed with exception: {ex.Message}");
                        return false;
                    }
                    finally
                    {
                        try
                        {
                            ClearCurrentJobName();
                        }
                        catch
                        {
                        }

                        try
                        {
                            Process.GetCurrentProcess().PriorityClass = originalPriority;
                            logger?.Invoke($"Process priority restored to {originalPriority}");
                        }
                        catch (Exception prioEx)
                        {
                            logger?.Invoke($"Warning: Could not restore process priority: {prioEx.Message}");
                        }
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                NativeExecutionLock.Release();
            }
        }

        public async Task<bool> ExecuteBackupJob(BackupJob job, Action<string>? logger = null)
        {
            return await ExecuteBackupJobWithProgress(job, null, CancellationToken.None, logger);
        }

        /// <summary>
        /// Executes verification on a completed backup with progress tracking
        /// </summary>
        public async Task<bool> VerifyBackupWithProgress(
            BackupJob job,
            Action<int, string>? progressCallback,
            CancellationToken cancellationToken,
            Action<string>? logger = null)
        {
            await NativeExecutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        logger?.Invoke($"Starting backup verification for: {job.Name}");
                        progressCallback?.Invoke(0, "Initializing verification...");

                        if (cancellationToken.IsCancellationRequested)
                        {
                            logger?.Invoke("Verification cancelled by user");
                            return false;
                        }

                        string backupPath;

                        // Hyper-V clone jobs use directory structures instead of .ssb files
                        if (job.Type == BackupType.CloneHyperVSystem || job.Type == BackupType.CloneToVirtualDisk)
                        {
                            // For clone jobs, the backup path is the job folder containing the VHDX/VM files
                            string cloneFolderName = job.RenameHyperVSystem && !string.IsNullOrWhiteSpace(job.RenameHyperVSystemName)
                                ? job.RenameHyperVSystemName.Trim()
                                : job.Name;
                            backupPath = Path.Combine(job.DestinationPath, cloneFolderName);

                            if (!Directory.Exists(backupPath))
                            {
                                logger?.Invoke($"[ERROR] Clone directory not found: {backupPath}");
                                return false;
                            }

                            // Verify the VHDX file exists
                            string vhdxPath = Path.Combine(backupPath, $"{cloneFolderName}.vhdx");
                            if (!File.Exists(vhdxPath))
                            {
                                logger?.Invoke($"[ERROR] Clone VHDX file not found: {vhdxPath}");
                                return false;
                            }

                            logger?.Invoke($"Clone verification PASSED: Found clone directory and VHDX file");
                            logger?.Invoke($"Clone path: {backupPath}");
                            logger?.Invoke($"VHDX file: {Path.GetFileName(vhdxPath)}");
                            progressCallback?.Invoke(100, "Clone verification completed successfully");
                            return true;
                        }
                        else
                        {
                            // Standard backups use .ssb archive files
                            backupPath = Path.Combine(job.DestinationPath, $"{job.Name}.ssb");
                            if (!File.Exists(backupPath))
                            {
                                logger?.Invoke($"[ERROR] Backup file not found: {backupPath}");
                                return false;
                            }
                        }

                        logger?.Invoke($"Verifying backup file: {Path.GetFileName(backupPath)}");
                        progressCallback?.Invoke(10, "Verifying SSB archive integrity...");

                        ProgressCallback? nativeCallback = null;
                        if (progressCallback != null)
                        {
                            nativeCallback = (percentage, message) =>
                            {
                                int mappedPercentage = 10 + (int)(percentage * 0.8);
                                progressCallback(mappedPercentage, message ?? $"Verifying: {percentage}%");
                            };
                        }

                        int expectedImageCount = -1;
                        if (job.Target == BackupTarget.Disk && job.SourcePaths?.Count > 0)
                        {
                            int diskNumber = ExtractDiskNumber(job.SourcePaths[0]);
                            if (diskNumber >= 0)
                            {
                                expectedImageCount = -1;
                            }
                        }
                        else if (job.Target == BackupTarget.HyperV)
                        {
                            expectedImageCount = -1;
                        }

                        var errorMsg = new StringBuilder(1024);
                        int verifyResult = VerifyWimArchive(
                            backupPath,
                            expectedImageCount,
                            errorMsg,
                            errorMsg.Capacity,
                            nativeCallback);

                        if (verifyResult != 0)
                        {
                            logger?.Invoke($"[VERIFICATION FAILED] Result code: {verifyResult}");
                            logger?.Invoke($"[VERIFICATION FAILED] Error: {errorMsg}");
                            return false;
                        }

                        logger?.Invoke($"Archive verification PASSED: {errorMsg}");

                        // SSB component-store health checks only apply to Windows OS volume backups.
                        // Hyper-V backups contain guest VM disks, not a Windows installation, so
                        // SSBOpenSession would fail with 0x80070003. Archive integrity is sufficient.
                        if (job.Target != BackupTarget.HyperV)
                        {
                            progressCallback?.Invoke(90, "Checking image health...");

                            var healthMsg = new StringBuilder(1024);
                            int healthState = CheckBackupImageHealth(
                                backupPath,
                                1,
                                true,
                                healthMsg,
                                healthMsg.Capacity,
                                nativeCallback);

                            if (healthState < 0)
                            {
                                logger?.Invoke($"[SSB VERIFY FAILED] Result code: {healthState}");
                                logger?.Invoke($"[SSB VERIFY FAILED] {healthMsg}");
                                return false;
                            }

                            if (healthState == (int)DismImageHealthState.Repairable)
                            {
                                logger?.Invoke($"[SSB] Image is repairable. Attempting RestoreHealth: {healthMsg}");
                                progressCallback?.Invoke(95, "Repairing image...");

                                var repairMsg = new StringBuilder(1024);
                                int repairResult = RestoreBackupImageHealth(
                                    backupPath,
                                    1,
                                    null,
                                    0,
                                    false,
                                    repairMsg,
                                    repairMsg.Capacity,
                                    nativeCallback);

                                if (repairResult != 0)
                                {
                                    logger?.Invoke($"[SSB REPAIR FAILED] Result code: {repairResult}");
                                    logger?.Invoke($"[SSB REPAIR FAILED] {repairMsg}");
                                    return false;
                                }

                                logger?.Invoke($"[SSB] Repair completed: {repairMsg}");
                            }
                            else if (healthState == (int)DismImageHealthState.NonRepairable)
                            {
                                logger?.Invoke($"[SSB VERIFY FAILED] Image is non-repairable: {healthMsg}");
                                return false;
                            }
                            else
                            {
                                logger?.Invoke($"[SSB VERIFY PASSED] {healthMsg}");
                            }
                        }

                        progressCallback?.Invoke(100, "Verification completed successfully!");
                        logger?.Invoke($"Backup verification completed successfully: {job.Name}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke($"Verification failed with exception: {ex.Message}");
                        return false;
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                NativeExecutionLock.Release();
            }
        }

        private static void LogFromEngine(string jobName, int level, string message, string details)
        {
            switch (level)
            {
                case 0: // Info
                    BackupLogger.LogInfo(jobName, message, details);
                    break;
                case 1: // Success
                    BackupLogger.LogSuccess(jobName, message, details);
                    break;
                case 2: // Warning
                    BackupLogger.LogWarning(jobName, message, details);
                    break;
                case 3: // Error
                    BackupLogger.LogError(jobName, message, details);
                    break;
                default:
                    BackupLogger.LogInfo(jobName, message, details);
                    break;
            }
        }

        private int ExecuteBackup(BackupJob job, string sourcePath, string destPath,
            ProgressCallback? progressCallback, LogCallback logCallback, Action<string>? logger)
        {
            int result;

            // Convert user exclusions to array for P/Invoke (empty array if null)
            string[] exclusionsArray = job.UserExclusions?.ToArray() ?? Array.Empty<string>();
            int exclusionCount = exclusionsArray.Length;

            if (exclusionCount > 0)
            {
                logger?.Invoke($"Applying {exclusionCount} user-defined exclusion(s) to backup");
            }

            // DEFENSIVE FIX: Auto-detect if sourcePath is actually a device path but job.Target is wrong
            // This handles cases where jobs were created before the fix or with incorrect settings
            // Only log correction message if we're actually CHANGING the target (not when already correct)
            if (sourcePath.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
            {
                // Physical drive path detected - should be Disk backup
                if (job.Target != BackupTarget.Disk)
                {
                    logger?.Invoke($"AUTO-CORRECT: Detected device path (PHYSICALDRIVE) - changing from {job.Target} to Disk backup");
                    job.Target = BackupTarget.Disk;
                }
            }
            else if (sourcePath.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
            {
                // Volume GUID path detected - should be Volume backup
                if (job.Target != BackupTarget.Volume)
                {
                    logger?.Invoke($"AUTO-CORRECT: Detected device path (Volume GUID) - changing from {job.Target} to Volume backup");
                    job.Target = BackupTarget.Volume;
                }
            }

            if (job.Target == BackupTarget.HyperV)
            {
                logger?.Invoke($"Backing up Hyper-V virtual machine: {sourcePath}");
                return BackupHyperVVM(sourcePath, destPath, progressCallback);
            }

            switch (job.Type)
            {
                case BackupType.Full:
                case BackupType.SelectedFilesAndFolders:
                    if (job.Target == BackupTarget.Disk)
                    {
                        // Extract disk number from device path (e.g., \\.\PHYSICALDRIVE5 -> 5)
                        int diskNumber = ExtractDiskNumber(sourcePath);
                        if (diskNumber < 0)
                        {
                            logger?.Invoke($"ERROR: Invalid disk path format: {sourcePath}");
                            return -11;
                        }
                        logger?.Invoke($"Backing up disk: {diskNumber} ({sourcePath})");

                        // DIAGNOSTIC: Log right before calling C++ function
                        logger?.Invoke($"[DIAGNOSTIC] About to call BackupDisk({diskNumber}, {destPath}, {job.IncludeSystemState}, {job.CompressData}, exclusions: {exclusionCount})");

                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, 
                                          exclusionsArray, exclusionCount, progressCallback, logCallback);

                        // DIAGNOSTIC: Log result code immediately
                        logger?.Invoke($"[DIAGNOSTIC] BackupDisk returned: {result}");

                        if (result != 0)
                        {
                            // DIAGNOSTIC: Log that we're getting error message
                            logger?.Invoke($"[DIAGNOSTIC] BackupDisk failed with code {result}, getting error message...");
                        }
                    }
                    else if (job.Target == BackupTarget.Volume)
                    {
                        logger?.Invoke($"Backing up volume: {sourcePath}");
                        result = BackupVolume(sourcePath, destPath, job.IncludeSystemState, job.CompressData,
                                            exclusionsArray, exclusionCount, progressCallback, logCallback);
                    }
                    else
                    {
                        logger?.Invoke($"Backing up files: {sourcePath}");
                        result = BackupFiles(sourcePath, destPath, exclusionsArray, exclusionCount, progressCallback, logCallback);
                    }
                    break;

            case BackupType.Incremental:
                // DISK BACKUPS: Now supports true incremental using WIM_FLAG_REFERENCE!
                if (job.Target == BackupTarget.Disk)
                {
                    int diskNumber = ExtractDiskNumber(sourcePath);
                    if (diskNumber < 0)
                    {
                        logger?.Invoke($"ERROR: Invalid disk path format: {sourcePath}");
                        return -11;
                    }

                    // Check if base backup exists
                    if (File.Exists(destPath))
                    {
                        logger?.Invoke($"Creating incremental disk backup (SSB referential): {diskNumber}");
                        result = BackupDiskIncremental(diskNumber, destPath, job.IncludeSystemState, job.CompressData,
                            exclusionsArray, exclusionCount, progressCallback, logCallback);

                        if (result != 0)
                        {
                            logger?.Invoke($"Disk incremental backup failed with code {result}");
                        }
                    }
                    else
                    {
                        logger?.Invoke($"No base backup found. Creating initial full backup: {diskNumber}");
                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, 
                                          exclusionsArray, exclusionCount, progressCallback, logCallback);

                        if (result != 0)
                        {
                            logger?.Invoke($"Disk full backup (fallback) failed with code {result}");
                        }
                        else
                        {
                            logger?.Invoke($"Initial full backup completed successfully (fallback from incremental)");
                        }
                    }
                }
                else
                {
                    // FILE/FOLDER/VOLUME BACKUPS: Support true incremental backups
                    var fullBackupBase = FindFullBackup(job.DestinationPath, job.Name);
                    if (string.IsNullOrEmpty(fullBackupBase))
                    {
                        logger?.Invoke($"No full backup found. Creating initial full backup instead of incremental.");
                        // Do a full backup if no previous full backup exists
                        if (job.Target == BackupTarget.Volume)
                        {
                            result = BackupVolume(sourcePath, destPath, job.IncludeSystemState, job.CompressData,
                                                exclusionsArray, exclusionCount, progressCallback, logCallback);
                        }
                        else
                        {
                            result = BackupFiles(sourcePath, destPath, exclusionsArray, exclusionCount, progressCallback, logCallback);
                        }
                    }
                    else
                    {
                        // Find the most recent backup (could be full, incremental, or differential) to base the incremental on
                        var lastBackup = FindLastBackup(job.DestinationPath, job.Name) ?? fullBackupBase;
                        logger?.Invoke($"Creating incremental backup from: {lastBackup}");
                        result = CreateIncrementalBackup(sourcePath, destPath, lastBackup, progressCallback);
                    }
                }
                break;

            case BackupType.Differential:
                // DISK BACKUPS: Now supports true differential using WIM_FLAG_REFERENCE!
                if (job.Target == BackupTarget.Disk)
                {
                    int diskNumber = ExtractDiskNumber(sourcePath);
                    if (diskNumber < 0)
                    {
                        logger?.Invoke($"ERROR: Invalid disk path format: {sourcePath}");
                        return -11;
                    }

                    // Check if base backup exists
                    if (File.Exists(destPath))
                    {
                        logger?.Invoke($"Creating differential disk backup (SSB referential): {diskNumber}");
                        result = BackupDiskDifferential(diskNumber, destPath, job.IncludeSystemState, job.CompressData,
                            exclusionsArray, exclusionCount, progressCallback, logCallback);

                        if (result != 0)
                        {
                            logger?.Invoke($"Disk differential backup failed with code {result}");
                        }
                    }
                    else
                    {
                        logger?.Invoke($"No base backup found. Creating initial full backup: {diskNumber}");
                        result = BackupDisk(diskNumber, destPath, job.IncludeSystemState, job.CompressData, 
                                          exclusionsArray, exclusionCount, progressCallback, logCallback);

                        if (result != 0)
                        {
                            logger?.Invoke($"Disk full backup (fallback) failed with code {result}");
                        }
                        else
                        {
                            logger?.Invoke($"Initial full backup completed successfully (fallback from differential)");
                        }
                    }
                }
                else
                {
                    // FILE/FOLDER/VOLUME BACKUPS: Support true differential backups
                    var fullBackup = FindFullBackup(job.DestinationPath, job.Name);
                    if (string.IsNullOrEmpty(fullBackup))
                    {
                        logger?.Invoke($"No full backup found. Creating initial full backup instead of differential.");
                        // Do a full backup if no base full backup exists
                        if (job.Target == BackupTarget.Volume)
                        {
                            result = BackupVolume(sourcePath, destPath, job.IncludeSystemState, job.CompressData,
                                                exclusionsArray, exclusionCount, progressCallback, logCallback);
                        }
                        else
                        {
                            result = BackupFiles(sourcePath, destPath, exclusionsArray, exclusionCount, progressCallback, logCallback);
                        }
                    }
                    else
                    {
                        logger?.Invoke($"Creating differential backup from: {fullBackup}");
                        result = CreateDifferentialBackup(sourcePath, destPath, fullBackup, progressCallback);
                    }
                }
                break;

                default:
                    result = -1;
                    break;
            }

            return result;
        }

        private int ExecuteFileSelectionBackup(
            BackupJob job,
            IReadOnlyList<string> selectedPaths,
            string sourceRoot,
            string destPath,
            ProgressCallback? progressCallback,
            LogCallback logCallback,
            Action<string>? logger)
        {
            ArgumentNullException.ThrowIfNull(job);
            ArgumentNullException.ThrowIfNull(selectedPaths);

            string[] exclusionsArray = job.UserExclusions?.ToArray() ?? Array.Empty<string>();
            FileSelectionResolution selectionResolution = ResolveSelectedFilePathsForExecution(selectedPaths);
            string[] resolvedIncludePaths = selectionResolution.ResolvedPaths.ToArray();
            IReadOnlyList<string> missingIncludePaths = selectionResolution.MissingPaths;

            if (missingIncludePaths.Count > 0)
            {
                string missingSummary = string.Join(", ", missingIncludePaths.Take(3));
                if (missingIncludePaths.Count > 3)
                {
                    missingSummary += ", ...";
                }

                if (resolvedIncludePaths.Length > 0)
                {
                    logger?.Invoke($"[WARNING] Skipping {missingIncludePaths.Count} missing selected file(s) or folder(s): {missingSummary}");
                }
                else
                {
                    logger?.Invoke($"[ERROR] All selected files or folders are missing for source root '{sourceRoot}': {missingSummary}");
                }
            }

            if (resolvedIncludePaths.Length == 0)
            {
                logger?.Invoke($"[ERROR] No selected files or folders were found for source root: {sourceRoot}");
                return -12;
            }

            logger?.Invoke($"Backing up selected files from root: {sourceRoot} ({resolvedIncludePaths.Length} item(s))");
            return BackupFilesBySelections(
                sourceRoot,
                destPath,
                resolvedIncludePaths,
                resolvedIncludePaths.Length,
                exclusionsArray,
                exclusionsArray.Length,
                progressCallback,
                logCallback);
        }

        public sealed record FileBackupBatch(string SourceRoot, IReadOnlyList<string> SelectedPaths);

        private string? FindLastBackup(string destPath, string jobName)
        {
            try
            {
                if (!Directory.Exists(destPath))
                    return null;

                // SIMPLIFIED: Look for the single backup file (no suffixes)
                // With new architecture, there's only ONE file: JobName.ssb
                string backupFile = Path.Combine(destPath, $"{jobName}.ssb");

                if (File.Exists(backupFile))
                    return backupFile;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private string? FindFullBackup(string destPath, string jobName)
        {
            try
            {
                if (!Directory.Exists(destPath))
                    return null;

                // SIMPLIFIED: Look for the single backup file (no suffixes)
                // With new architecture, full/incremental/differential all use same file: JobName.ssb
                string backupFile = Path.Combine(destPath, $"{jobName}.ssb");

                if (File.Exists(backupFile))
                    return backupFile;

                return null;
            }
            catch
            {
                return null;
            }
        }

        // REMOVED: GetExistingFullBackups - no longer needed with single-file approach
        // REMOVED: RenameBackupAsPending - overwrite existing files directly  
        // REMOVED: RestoreRenamedBackup - no backup renaming needed
        // REMOVED: CleanupOldBackups - single file per backup type, no cleanup needed

        // Retention logic simplified: Each backup type (Full/Incremental/Differential) 
        // overwrites its own file. For multiple versions, implement versioning later.

        /// <summary>
        /// Extracts disk number from physical drive device path
        /// </summary>
        /// <param name="devicePath">Device path like \\.\PHYSICALDRIVE5</param>
        /// <returns>Disk number or -1 if invalid format</returns>
        private int ExtractDiskNumber(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
                return -1;

            // Expected format: \\.\PHYSICALDRIVE5 or \\.\PhysicalDrive5
            const string prefix = "\\\\.\\PHYSICALDRIVE";

            if (devicePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string numberPart = devicePath.Substring(prefix.Length);
                if (int.TryParse(numberPart, out int diskNumber))
                {
                    return diskNumber;
                }
            }

            return -1;
        }

        // REMOVED: CleanupOldBackups - not needed with single-file approach
        // Each backup type overwrites its own file
    }
}

