using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SecureServerBackupCommon;

namespace SecureServerBackup.Services
{
        /// <summary>
        /// Native SSB mounting manager (No PowerShell, No Admin required)
        /// </summary>
    public class NativeBackupMountManager
    {
        private const string NativeDllName = "SecureServerBackupEngine.dll";

        public sealed class ImageRestoreMetadata
        {
            public int SourceDiskNumber { get; set; }
            public ulong SourceDiskSizeBytes { get; set; }
            public string SourceVolumeGuidPath { get; set; } = string.Empty;
            public string SourceVolumeMountPath { get; set; } = string.Empty;
            public string SourceVolumeLabel { get; set; } = string.Empty;
            public string SourceFileSystem { get; set; } = string.Empty;
            public string PartitionStyle { get; set; } = string.Empty;
            public uint PartitionNumber { get; set; }
            public ulong PartitionOffsetBytes { get; set; }
            public ulong PartitionLengthBytes { get; set; }
            public string PartitionType { get; set; } = string.Empty;
            public bool IsBootVolume { get; set; }
            public bool IsSystemVolume { get; set; }
            public int VolumeIndex { get; set; }
        }

        public sealed class SsbImageInfoResult
        {
            public int ImageIndex { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public ImageRestoreMetadata? RestoreMetadata { get; set; }
        }

        public sealed class RestoreDiskPlan
        {
            public int SourceDiskNumber { get; set; }
            public int ImageIndex { get; set; }
            public string ImageName { get; set; } = string.Empty;
            public string ImageDescription { get; set; } = string.Empty;
            public List<RestoreVolumePlan> Volumes { get; set; } = new();
            public bool HasMetadata => Volumes.Count > 0 || SourceDiskNumber >= 0;
        }

        public sealed class RestoreVolumePlan
        {
            public int VolumeIndex { get; set; }
            public string SourceVolumeGuidPath { get; set; } = string.Empty;
            public string SourceVolumeMountPath { get; set; } = string.Empty;
            public string SourceVolumeLabel { get; set; } = string.Empty;
            public string SourceFileSystem { get; set; } = string.Empty;
            public string PartitionStyle { get; set; } = string.Empty;
            public uint PartitionNumber { get; set; }
            public ulong PartitionOffsetBytes { get; set; }
            public ulong PartitionLengthBytes { get; set; }
            public string PartitionType { get; set; } = string.Empty;
            public bool IsBootVolume { get; set; }
            public bool IsSystemVolume { get; set; }
        }

        private sealed class MountPathWatcher : IDisposable
        {
            private readonly Action<int, string>? _progressCallback;
            private readonly string _rootPath;
            private readonly object _syncLock = new();
            private FileSystemWatcher? _watcher;
            private Timer? _debounceTimer;
            private string? _pendingPath;
            private string? _lastReportedPath;
            private bool _disposed;

            public MountPathWatcher(string rootPath, Action<int, string>? progressCallback)
            {
                _rootPath = rootPath;
                _progressCallback = progressCallback;
            }

            public void Start()
            {
                if (_progressCallback == null || string.IsNullOrWhiteSpace(_rootPath) || !Directory.Exists(_rootPath))
                {
                    return;
                }

                _debounceTimer = new Timer(FlushPendingPath, null, Timeout.Infinite, Timeout.Infinite);
                _watcher = new FileSystemWatcher(_rootPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnPathChanged;
                _watcher.Changed += OnPathChanged;
                _watcher.Renamed += OnPathRenamed;
            }

            private void OnPathChanged(object sender, FileSystemEventArgs e)
            {
                QueuePath(e.FullPath);
            }

            private void OnPathRenamed(object sender, RenamedEventArgs e)
            {
                QueuePath(e.FullPath);
            }

            private void QueuePath(string fullPath)
            {
                if (_disposed || string.IsNullOrWhiteSpace(fullPath))
                {
                    return;
                }

                if (Directory.Exists(fullPath) || File.Exists(fullPath))
                {
                    string relativePath;
                    try
                    {
                        relativePath = Path.GetRelativePath(_rootPath, fullPath);
                    }
                    catch
                    {
                        relativePath = Path.GetFileName(fullPath);
                    }

                    lock (_syncLock)
                    {
                        _pendingPath = relativePath;
                        _debounceTimer?.Change(150, Timeout.Infinite);
                    }
                }
            }

            private void FlushPendingPath(object? state)
            {
                string? pathToReport;

                lock (_syncLock)
                {
                    pathToReport = _pendingPath;
                    _pendingPath = null;
                }

                if (string.IsNullOrWhiteSpace(pathToReport) || string.Equals(pathToReport, _lastReportedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _lastReportedPath = pathToReport;
                _progressCallback?.Invoke(65, $"Processing: {pathToReport}");
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Created -= OnPathChanged;
                    _watcher.Changed -= OnPathChanged;
                    _watcher.Renamed -= OnPathRenamed;
                    _watcher.Dispose();
                    _watcher = null;
                }

                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
        }

        private static string? TryPredictMountPath(string backupName, int imageIndex, string? tempPath)
        {
            try
            {
                string rootPath = string.IsNullOrWhiteSpace(tempPath)
                    ? Path.Combine(Path.GetTempPath(), "BackupMounts")
                    : Path.Combine(tempPath, "BackupMounts");

                string sanitizedName = string.IsNullOrWhiteSpace(backupName)
                    ? "Backup"
                    : new string(backupName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());

                return rootPath;
            }
            catch
            {
                return null;
            }
        }

        // Progress callback delegate matching C++ signature
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate void ProgressCallback(int percentage, [MarshalAs(UnmanagedType.LPWStr)] string message);

        // P/Invoke declarations for the native mount manager
        [DllImport(NativeDllName, EntryPoint = "SsbMount_MountArchive", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SsbMount_MountArchive(
            [MarshalAs(UnmanagedType.LPWStr)] string ssbPath,
            [MarshalAs(UnmanagedType.LPWStr)] string backupName,
            [MarshalAs(UnmanagedType.LPWStr)] string backupType,
            int imageIndex,  // Image index to mount (1-based, use 1 for first image)
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder mountPath,
            int mountPathSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder errorMsg,
            int errorMsgSize,
            ProgressCallback? callback = null,  // Optional progress callback
            [MarshalAs(UnmanagedType.LPWStr)] string? tempPath = null  // Optional temp path
        );

        [DllImport(NativeDllName, EntryPoint = "SsbMount_UnmountArchive", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SsbMount_UnmountArchive(
            [MarshalAs(UnmanagedType.LPWStr)] string mountPath,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder errorMsg,
            int errorMsgSize,
            ProgressCallback? callback = null  // Optional progress callback
        );

        [DllImport(NativeDllName, EntryPoint = "SsbMount_UnmountAll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SsbMount_UnmountAll();

        [DllImport(NativeDllName, EntryPoint = "SsbMount_GetMountedCount", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SsbMount_GetMountedCount();

        [DllImport(NativeDllName, EntryPoint = "SsbMount_GetMountedInfo", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SsbMount_GetMountedInfo(
            int index,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder ssbPath,
            int ssbPathSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder mountPath,
            int mountPathSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder backupName,
            int backupNameSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder backupType,
            int backupTypeSize,
            out SYSTEMTIME mountTime  // ? NEW: Get mount time from C++
        );

        [DllImport(NativeDllName, EntryPoint = "SsbMount_GetImageCount", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SsbMount_GetImageCount(
            [MarshalAs(UnmanagedType.LPWStr)] string ssbPath,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder errorMsg,
            int errorMsgSize
        );

        [DllImport(NativeDllName, EntryPoint = "SsbMount_GetImageInfo", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SsbMount_GetImageInfo(
            [MarshalAs(UnmanagedType.LPWStr)] string ssbPath,
            int imageIndex,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,
            int nameSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder description,
            int descSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder errorMsg,
            int errorMsgSize
        );

        // SYSTEMTIME structure for interop
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear;
            public ushort wMonth;
            public ushort wDayOfWeek;
            public ushort wDay;
            public ushort wHour;
            public ushort wMinute;
            public ushort wSecond;
            public ushort wMilliseconds;
        }

        /// <summary>
        /// Mounted backup information
        /// </summary>
        public class MountedBackup
        {
            public string SsbPath { get; set; } = "";
            public string MountPath { get; set; } = "";
            public string BackupName { get; set; } = "";
            public string BackupType { get; set; } = "";
            public DateTime MountTime { get; set; }
            public bool IsReadOnly => true; // Always read-only
        }

        /// <summary>
        /// Mount an SSB backup file (No admin required!)
        /// </summary>
        public static (bool Success, string MountPath, string Error) MountBackup(
            string ssbPath,
            string backupName,
            string backupType,
            int imageIndex = 1)  // Default to first image
        {
            try
            {
                var mountPath = new StringBuilder(260);
                var errorMsg = new StringBuilder(512);

                bool success = SsbMount_MountArchive(
                    ssbPath,
                    backupName,
                    backupType,
                    imageIndex,  // Pass image index to C++
                    mountPath,
                    260,
                    errorMsg,
                    512
                );

                if (success)
                {
                    BackupLogger.LogSuccess("BackupMount",
                        $"Backup mounted successfully: {backupName}",
                        $"Path: {mountPath}");

                    return (true, mountPath.ToString(), "");
                }
                else
                {
                    BackupLogger.LogError("BackupMount",
                        $"Failed to mount backup: {backupName}",
                        errorMsg.ToString());

                    return (false, "", errorMsg.ToString());
                }
            }
            catch (Exception ex)
            {
                BackupLogger.LogError("BackupMount",
                    "Exception mounting backup",
                    ex.Message);

                return (false, "", ex.Message);
            }
        }

        /// <summary>
        /// Mount an SSB backup file asynchronously with progress reporting
        /// </summary>
        public static async Task<(bool Success, string MountPath, string Error)> MountBackupAsync(
            string ssbPath,
            string backupName,
            string backupType,
            int imageIndex = 1,
            Action<int, string>? progressCallback = null,  // Progress callback
            string? tempPath = null)  // Optional temp path for archive operations
        {
            // Ensure mount operations run at Normal priority (not Efficiency mode)
            var originalPriority = System.Diagnostics.ProcessPriorityClass.Normal;
            try
            {
                originalPriority = System.Diagnostics.Process.GetCurrentProcess().PriorityClass;
                if (originalPriority != System.Diagnostics.ProcessPriorityClass.Normal)
                {
                    System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal;
                    System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager.MountBackupAsync] Priority raised from {originalPriority} to Normal for mount operation");
                }
            }
            catch (Exception prioEx)
            {
                System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager.MountBackupAsync] Warning: Could not set priority: {prioEx.Message}");
            }

            try
            {
                progressCallback?.Invoke(0, "Validating backup file...");

                // Validate backup archive BEFORE attempting to mount
                var errorMsg = new StringBuilder(512);
                int imageCount = SsbMount_GetImageCount(ssbPath, errorMsg, 512);

                if (imageCount <= 0)
                {
                    string validationError = errorMsg.Length > 0
                        ? errorMsg.ToString()
                        : "Archive contains no images or could not be read.";

                    BackupLogger.LogError("BackupMount",
                        $"Archive validation failed for {backupName}",
                        validationError);

                    return (false, "", validationError);
                }

                progressCallback?.Invoke(10, $"Validation successful - {imageCount} image(s) found");

                // Set temp path if provided
                if (!string.IsNullOrEmpty(tempPath))
                {
                    progressCallback?.Invoke(15, $"Using temp path: {tempPath}");
                }

                progressCallback?.Invoke(20, "Opening SSB archive...");

                // Run the synchronous mount operation on a background thread
                return await Task.Run(() =>
                {
                    try
                    {
                        progressCallback?.Invoke(30, "Loading image from SSB archive...");

                        var mountPath = new StringBuilder(260);
                        errorMsg.Clear();

                        // DIAGNOSTIC: Log the temp path we're about to pass to C++
                        System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager] About to call SsbMount_MountArchive with tempPath: '{tempPath}'");
                        System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager] tempPath is null: {tempPath == null}");
                        System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager] tempPath is empty: {string.IsNullOrEmpty(tempPath)}");
                        if (!string.IsNullOrEmpty(tempPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager] tempPath string length: {tempPath.Length}");
                            System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager] tempPath first 10 chars: '{tempPath.Substring(0, Math.Min(10, tempPath.Length))}'");
                        }

                        // Create native callback that wraps our C# callback
                        ProgressCallback? nativeCallback = null;
                        if (progressCallback != null)
                        {
                            nativeCallback = (percentage, message) =>
                            {
                                progressCallback?.Invoke(percentage, message ?? "Processing...");
                            };
                        }

                        string? predictedMountPath = TryPredictMountPath(backupName, imageIndex, tempPath);
                        using var mountWatcher = !string.IsNullOrWhiteSpace(predictedMountPath)
                            ? new MountPathWatcher(predictedMountPath, progressCallback)
                            : null;
                        mountWatcher?.Start();

                        bool success = SsbMount_MountArchive(
                            ssbPath,
                            backupName,
                            backupType,
                            imageIndex,
                            mountPath,
                            260,
                            errorMsg,
                            512,
                            nativeCallback,  // Pass the callback to C++
                            tempPath  // Pass temp path to C++
                        );

                        System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager] SsbMount_MountArchive returned: {success}");
                        if (!success)
                        {
                            System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager] Error message: {errorMsg}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager] Mount path returned: {mountPath}");
                        }

                        if (success)
                        {
                            progressCallback?.Invoke(100, "Mount completed successfully!");

                            BackupLogger.LogSuccess("BackupMount",
                                $"Backup mounted successfully: {backupName}",
                                $"Path: {mountPath}");

                            return (true, mountPath.ToString(), "");
                        }
                        else
                        {
                            BackupLogger.LogError("BackupMount",
                                $"Failed to mount backup: {backupName}",
                                errorMsg.ToString());

                            return (false, "", errorMsg.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        BackupLogger.LogError("BackupMount",
                            "Exception mounting backup",
                            ex.Message);

                        return (false, "", ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                BackupLogger.LogError("BackupMount",
                    "Exception in async mount operation",
                    ex.Message);

                return (false, "", ex.Message);
            }
            finally
            {
                try
                {
                    if (System.Diagnostics.Process.GetCurrentProcess().PriorityClass != originalPriority)
                    {
                        System.Diagnostics.Process.GetCurrentProcess().PriorityClass = originalPriority;
                        System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager.MountBackupAsync] Priority restored to {originalPriority} after mount");
                    }
                }
                catch (Exception prioEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager.MountBackupAsync] Warning: Could not restore priority: {prioEx.Message}");
                }
            }
        }

        /// <summary>
        /// Unmount a backup by mount path
        /// </summary>
        public static (bool Success, string Error) UnmountBackup(string mountPath)
        {
            try
            {
                var errorMsg = new StringBuilder(512);

                bool success = SsbMount_UnmountArchive(mountPath, errorMsg, 512);

                if (success)
                {
                    BackupLogger.LogSuccess("BackupMount",
                        "Backup unmounted successfully",
                        mountPath);

                    return (true, "");
                }
                else
                {
                    BackupLogger.LogError("BackupMount",
                        "Failed to unmount backup",
                        errorMsg.ToString());

                    return (false, errorMsg.ToString());
                }
            }
            catch (Exception ex)
            {
                BackupLogger.LogError("BackupMount",
                    "Exception unmounting backup",
                    ex.Message);

                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Unmount a backup asynchronously with progress reporting
        /// </summary>
        public static async Task<(bool Success, string Error)> UnmountBackupAsync(
            string mountPath,
            Action<int, string>? progressCallback = null)
        {
            // Ensure unmount operations run at Normal priority (not Efficiency mode)
            var originalPriority = System.Diagnostics.ProcessPriorityClass.Normal;
            try
            {
                originalPriority = System.Diagnostics.Process.GetCurrentProcess().PriorityClass;
                if (originalPriority != System.Diagnostics.ProcessPriorityClass.Normal)
                {
                    System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal;
                    System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager.UnmountBackupAsync] Priority raised from {originalPriority} to Normal for unmount operation");
                }
            }
            catch (Exception prioEx)
            {
                System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager.UnmountBackupAsync] Warning: Could not set priority: {prioEx.Message}");
            }

            try
            {
                progressCallback?.Invoke(0, "Starting unmount operation...");

                // Run the synchronous unmount operation on a background thread
                return await Task.Run(() =>
                {
                    try
                    {
                        var errorMsg = new StringBuilder(512);

                        // Create native callback that wraps our C# callback
                        ProgressCallback? nativeCallback = null;
                        if (progressCallback != null)
                        {
                            nativeCallback = (percentage, message) =>
                            {
                                progressCallback?.Invoke(percentage, message ?? "Processing...");
                            };
                        }

                bool success = SsbMount_UnmountArchive(mountPath, errorMsg, 512, nativeCallback);

                        if (success)
                        {
                            progressCallback?.Invoke(100, "Unmount completed successfully!");

                            BackupLogger.LogSuccess("BackupMount",
                                "Backup unmounted successfully",
                                mountPath);

                            return (true, "");
                        }
                        else
                        {
                            string error = errorMsg.ToString();

                            BackupLogger.LogError("BackupMount",
                                "Failed to unmount backup",
                                error);

                            return (false, error);
                        }
                    }
                    catch (Exception ex)
                    {
                        BackupLogger.LogError("BackupMount",
                            "Exception unmounting backup",
                            ex.Message);

                        return (false, ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                BackupLogger.LogError("BackupMount",
                    "Exception in async unmount operation",
                    ex.Message);

                return (false, ex.Message);
            }
            finally
            {
                // Restore original process priority after unmount completes
                try
                {
                    if (System.Diagnostics.Process.GetCurrentProcess().PriorityClass != originalPriority)
                    {
                        System.Diagnostics.Process.GetCurrentProcess().PriorityClass = originalPriority;
                        System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager.UnmountBackupAsync] Priority restored to {originalPriority} after unmount");
                    }
                }
                catch (Exception prioEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager.UnmountBackupAsync] Warning: Could not restore priority: {prioEx.Message}");
                }
            }
        }

        /// <summary>
        /// Unmount all mounted backups
        /// </summary>
        public static void UnmountAll()
        {
            try
            {
                SsbMount_UnmountAll();

                BackupLogger.LogSuccess("BackupMount",
                    "All backups unmounted",
                    "");
            }
            catch (Exception ex)
            {
                BackupLogger.LogError("BackupMount",
                    "Exception unmounting all backups",
                    ex.Message);
            }
        }

        /// <summary>
        /// Get list of currently mounted backups
        /// </summary>
        public static List<MountedBackup> GetMountedBackups()
        {
            var result = new List<MountedBackup>();

            try
            {
                int count = SsbMount_GetMountedCount();

                for (int i = 0; i < count; i++)
                {
                    var ssbPath = new StringBuilder(260);
                    var mountPath = new StringBuilder(260);
                    var backupName = new StringBuilder(256);
                    var backupType = new StringBuilder(64);
                    SYSTEMTIME mountTime;

                    if (SsbMount_GetMountedInfo(i, ssbPath, 260, mountPath, 260,
                                               backupName, 256, backupType, 64, out mountTime))
                    {
                        // Convert SYSTEMTIME to DateTime
                        DateTime mountDateTime;
                        try
                        {
                            mountDateTime = new DateTime(
                                mountTime.wYear,
                                mountTime.wMonth,
                                mountTime.wDay,
                                mountTime.wHour,
                                mountTime.wMinute,
                                mountTime.wSecond,
                                mountTime.wMilliseconds,
                                DateTimeKind.Utc  // SYSTEMTIME from GetSystemTime is UTC
                            ).ToLocalTime();  // Convert to local time for display
                        }
                        catch
                        {
                            // If conversion fails, use current time as fallback
                            mountDateTime = DateTime.Now;
                        }

                        result.Add(new MountedBackup
                        {
                            SsbPath = ssbPath.ToString(),
                            MountPath = mountPath.ToString(),
                            BackupName = backupName.ToString(),
                            BackupType = backupType.ToString(),
                            MountTime = mountDateTime  // ? Now using actual mount time from C++!
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                BackupLogger.LogError("BackupMount",
                    "Exception getting mounted backups",
                    ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Get number of images in an SSB backup file
        /// </summary>
        public static (bool Success, int ImageCount, string Error) GetImageCount(string ssbPath)
        {
            try
            {
                var errorMsg = new StringBuilder(512);
                int imageCount = SsbMount_GetImageCount(ssbPath, errorMsg, 512);

                if (imageCount > 0)
                {
                    return (true, imageCount, "");
                }
                else if (imageCount == 0)
                {
                    return (false, 0, "No images found in backup file");
                }
                else
                {
                    return (false, 0, errorMsg.ToString());
                }
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        }

        /// <summary>
        /// Get detailed information about all images in an SSB backup file
        /// </summary>
        public static (bool Success, List<Windows.BackupImageInfo> Images, string Error) GetImageInfo(string ssbPath)
        {
            try
            {
                // First get image count
                var (success, imageCount, error) = GetImageCount(ssbPath);
                if (!success)
                {
                    return (false, new List<Windows.BackupImageInfo>(), error);
                }

                var images = new List<Windows.BackupImageInfo>();

                // Get info for each image (1-based indexing)
                for (int i = 1; i <= imageCount; i++)
                {
                    var name = new StringBuilder(256);
                    var description = new StringBuilder(1024);
                    var errorMsg = new StringBuilder(512);

                    if (SsbMount_GetImageInfo(ssbPath, i, name, 256, description, 1024, errorMsg, 512))
                    {
                        string imageName = name.ToString();
                        string desc = description.ToString();

                        if (desc.Contains("|BACKUPRESTOREMETADATA|", StringComparison.Ordinal))
                        {
                            desc = desc.Split(new[] { "|BACKUPRESTOREMETADATA|" }, StringSplitOptions.None)[0];
                        }

                        // Parse metadata returned from C++
                        // Name format: "Disk 5 Volume 1 (Incremental) - 2026-04-22 13:45:10"
                        // Description format: job name (for newer backups)
                        string type = "Full"; // Default
                        DateTime imageDate = DateTime.Now; // Default to now if can't parse

                        if (string.IsNullOrWhiteSpace(desc) || desc.Equals("No description", StringComparison.OrdinalIgnoreCase))
                        {
                            desc = Path.GetFileNameWithoutExtension(ssbPath);
                        }

                        string nameWithoutTimestamp = imageName;

                        // Extract date from the image name suffix
                        int dashIndex = imageName.LastIndexOf(" - ", StringComparison.Ordinal);
                        if (dashIndex > 0 && dashIndex + 3 < imageName.Length)
                        {
                            string dateStr = imageName.Substring(dashIndex + 3).Trim();
                            if (DateTime.TryParse(dateStr, out DateTime parsed))
                            {
                                imageDate = parsed;
                            }

                            nameWithoutTimestamp = imageName.Substring(0, dashIndex).TrimEnd();
                        }

                        // Extract backup type from the base image name (text in parentheses)
                        if (nameWithoutTimestamp.Contains("(") && nameWithoutTimestamp.Contains(")"))
                        {
                            int start = nameWithoutTimestamp.LastIndexOf('(') + 1;
                            int end = nameWithoutTimestamp.LastIndexOf(')');
                            if (end > start)
                            {
                                type = nameWithoutTimestamp.Substring(start, end - start).Trim();
                            }
                        }

                        images.Add(new Windows.BackupImageInfo
                        {
                            ImageIndex = i,
                            ImageDate = imageDate,
                            Name = imageName,
                            ImageType = type,
                            Description = desc
                        });
                    }
                }

                return (true, images, "");
            }
            catch (Exception ex)
            {
                return (false, new List<Windows.BackupImageInfo>(), ex.Message);
            }
        }

        public static (bool Success, List<SsbImageInfoResult> Images, string Error) GetImageInfoWithRestoreMetadata(string ssbPath)
        {
            var (success, images, error) = GetImageInfo(ssbPath);
            if (!success)
            {
                return (false, new List<SsbImageInfoResult>(), error);
            }

            var results = new List<SsbImageInfoResult>();
            foreach (var image in images)
            {
                var result = new SsbImageInfoResult
                {
                    ImageIndex = image.ImageIndex,
                    Name = image.Name,
                    Description = image.Description,
                    RestoreMetadata = ParseRestoreMetadata(image.Description)
                };

                if (result.RestoreMetadata != null && result.Description.Contains("|BACKUPRESTOREMETADATA|", StringComparison.Ordinal))
                {
                    result.Description = result.Description.Split(new[] { "|BACKUPRESTOREMETADATA|" }, StringSplitOptions.None)[0];
                }

                results.Add(result);
            }

            return (true, results, string.Empty);
        }

        private static ImageRestoreMetadata? ParseRestoreMetadata(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            const string marker = "|BACKUPRESTOREMETADATA|";
            int markerIndex = description.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return null;
            }

            string metadataBlob = description[(markerIndex + marker.Length)..];

            string Read(string tag)
            {
                string open = $"<{tag}>";
                string close = $"</{tag}>";
                int start = metadataBlob.IndexOf(open, StringComparison.OrdinalIgnoreCase);
                if (start < 0) return string.Empty;
                start += open.Length;
                int end = metadataBlob.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
                return end > start ? metadataBlob[start..end] : string.Empty;
            }

            var metadata = new ImageRestoreMetadata();
            _ = int.TryParse(Read("SOURCE_DISK_NUMBER"), out var diskNumber);
            metadata.SourceDiskNumber = diskNumber;
            _ = ulong.TryParse(Read("SOURCE_DISK_SIZE_BYTES"), out var diskSize);
            metadata.SourceDiskSizeBytes = diskSize;
            metadata.SourceVolumeGuidPath = Read("SOURCE_VOLUME_GUID_PATH");
            metadata.SourceVolumeMountPath = Read("SOURCE_VOLUME_MOUNT_PATH");
            metadata.SourceVolumeLabel = Read("SOURCE_VOLUME_LABEL");
            metadata.SourceFileSystem = Read("SOURCE_FILESYSTEM");
            metadata.PartitionStyle = Read("PARTITION_STYLE");
            _ = uint.TryParse(Read("PARTITION_NUMBER"), out var partitionNumber);
            metadata.PartitionNumber = partitionNumber;
            _ = ulong.TryParse(Read("PARTITION_OFFSET_BYTES"), out var partitionOffset);
            metadata.PartitionOffsetBytes = partitionOffset;
            _ = ulong.TryParse(Read("PARTITION_LENGTH_BYTES"), out var partitionLength);
            metadata.PartitionLengthBytes = partitionLength;
            metadata.PartitionType = Read("PARTITION_TYPE");
            metadata.IsBootVolume = string.Equals(Read("IS_BOOT_VOLUME"), "true", StringComparison.OrdinalIgnoreCase);
            metadata.IsSystemVolume = string.Equals(Read("IS_SYSTEM_VOLUME"), "true", StringComparison.OrdinalIgnoreCase);
            _ = int.TryParse(Read("VOLUME_INDEX"), out var volumeIndex);
            metadata.VolumeIndex = volumeIndex;
            return metadata;
        }

        public static (bool Success, RestoreDiskPlan Plan, string Error) BuildDiskRestorePlan(string ssbPath)
        {
            var plan = new RestoreDiskPlan();

            var (success, images, error) = GetImageInfoWithRestoreMetadata(ssbPath);
            if (!success)
            {
                return (false, plan, error);
            }

            foreach (var image in images)
            {
                if (image.RestoreMetadata == null)
                {
                    continue;
                }

                plan.SourceDiskNumber = image.RestoreMetadata.SourceDiskNumber;
                plan.ImageIndex = image.ImageIndex;
                plan.ImageName = image.Name;
                plan.ImageDescription = image.Description;

                plan.Volumes.Add(new RestoreVolumePlan
                {
                    VolumeIndex = image.RestoreMetadata.VolumeIndex,
                    SourceVolumeGuidPath = image.RestoreMetadata.SourceVolumeGuidPath,
                    SourceVolumeMountPath = image.RestoreMetadata.SourceVolumeMountPath,
                    SourceVolumeLabel = image.RestoreMetadata.SourceVolumeLabel,
                    SourceFileSystem = image.RestoreMetadata.SourceFileSystem,
                    PartitionStyle = image.RestoreMetadata.PartitionStyle,
                    PartitionNumber = image.RestoreMetadata.PartitionNumber,
                    PartitionOffsetBytes = image.RestoreMetadata.PartitionOffsetBytes,
                    PartitionLengthBytes = image.RestoreMetadata.PartitionLengthBytes,
                    PartitionType = image.RestoreMetadata.PartitionType,
                    IsBootVolume = image.RestoreMetadata.IsBootVolume,
                    IsSystemVolume = image.RestoreMetadata.IsSystemVolume
                });
            }

            if (plan.Volumes.Count == 0)
            {
                return (false, plan, "No disk reconstruction metadata found in the backup.");
            }

            return (true, plan, string.Empty);
        }
    }
}
