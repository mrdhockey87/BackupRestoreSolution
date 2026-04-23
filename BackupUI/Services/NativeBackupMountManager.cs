using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using BackupCommon;

namespace BackupUI.Services
{
    /// <summary>
    /// Native WIM mounting manager (No PowerShell, No Admin required)
    /// </summary>
    public class NativeBackupMountManager
    {
        // Progress callback delegate matching C++ signature
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate void ProgressCallback(int percentage, [MarshalAs(UnmanagedType.LPWStr)] string message);

        // P/Invoke declarations for C++ WimMountManager
        [DllImport("BackupEngine.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool WimMount_MountWim(
            [MarshalAs(UnmanagedType.LPWStr)] string wimPath,
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

        [DllImport("BackupEngine.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool WimMount_UnmountWim(
            [MarshalAs(UnmanagedType.LPWStr)] string mountPath,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder errorMsg,
            int errorMsgSize,
            ProgressCallback? callback = null  // Optional progress callback
        );

        [DllImport("BackupEngine.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern void WimMount_UnmountAll();

        [DllImport("BackupEngine.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WimMount_GetMountedCount();

        [DllImport("BackupEngine.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool WimMount_GetMountedInfo(
            int index,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder wimPath,
            int wimPathSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder mountPath,
            int mountPathSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder backupName,
            int backupNameSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder backupType,
            int backupTypeSize,
            out SYSTEMTIME mountTime  // ? NEW: Get mount time from C++
        );

        [DllImport("BackupEngine.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool WimMount_ValidateWim(
            [MarshalAs(UnmanagedType.LPWStr)] string wimPath,
            out int imageCount,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder errorMsg,
            int errorMsgSize
        );

        [DllImport("BackupEngine.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WimMount_GetImageCount(
            [MarshalAs(UnmanagedType.LPWStr)] string wimPath,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder errorMsg,
            int errorMsgSize
        );

        [DllImport("BackupEngine.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool WimMount_GetImageInfo(
            [MarshalAs(UnmanagedType.LPWStr)] string wimPath,
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
            public string WimPath { get; set; } = "";
            public string MountPath { get; set; } = "";
            public string BackupName { get; set; } = "";
            public string BackupType { get; set; } = "";
            public DateTime MountTime { get; set; }
            public bool IsReadOnly => true; // Always read-only
        }

        /// <summary>
        /// Mount a WIM backup file (No admin required!)
        /// </summary>
        public static (bool Success, string MountPath, string Error) MountBackup(
            string wimPath,
            string backupName,
            string backupType,
            int imageIndex = 1)  // Default to first image
        {
            try
            {
                var mountPath = new StringBuilder(260);
                var errorMsg = new StringBuilder(512);

                bool success = WimMount_MountWim(
                    wimPath,
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
        /// Mount a WIM backup file asynchronously with progress reporting
        /// </summary>
        public static async Task<(bool Success, string MountPath, string Error)> MountBackupAsync(
            string wimPath,
            string backupName,
            string backupType,
            int imageIndex = 1,
            Action<int, string>? progressCallback = null,  // Progress callback
            string? tempPath = null)  // Optional temp path for WIM operations
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

                // Validate WIM file BEFORE attempting to mount
                var errorMsg = new StringBuilder(512);
                int imageCount;

                if (!WimMount_ValidateWim(wimPath, out imageCount, errorMsg, 512))
                {
                    string validationError = errorMsg.ToString();

                    BackupLogger.LogError("BackupMount",
                        $"WIM validation failed for {backupName}",
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
                        System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager] About to call WimMount_MountWim with tempPath: '{tempPath}'");
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

                        bool success = WimMount_MountWim(
                            wimPath,
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

                        System.Diagnostics.Debug.WriteLine($"[NativeBackupMountManager] WimMount_MountWim returned: {success}");
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
        }

        /// <summary>
        /// Unmount a backup by mount path
        /// </summary>
        public static (bool Success, string Error) UnmountBackup(string mountPath)
        {
            try
            {
                var errorMsg = new StringBuilder(512);

                bool success = WimMount_UnmountWim(mountPath, errorMsg, 512);

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

                        bool success = WimMount_UnmountWim(mountPath, errorMsg, 512, nativeCallback);

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
                WimMount_UnmountAll();

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
                int count = WimMount_GetMountedCount();

                for (int i = 0; i < count; i++)
                {
                    var wimPath = new StringBuilder(260);
                    var mountPath = new StringBuilder(260);
                    var backupName = new StringBuilder(256);
                    var backupType = new StringBuilder(64);
                    SYSTEMTIME mountTime;

                    if (WimMount_GetMountedInfo(i, wimPath, 260, mountPath, 260,
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
                            WimPath = wimPath.ToString(),
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
        /// Get number of images in a WIM backup file
        /// </summary>
        public static (bool Success, int ImageCount, string Error) GetImageCount(string wimPath)
        {
            try
            {
                var errorMsg = new StringBuilder(512);
                int imageCount = WimMount_GetImageCount(wimPath, errorMsg, 512);

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
        /// Get detailed information about all images in a WIM backup file
        /// </summary>
        public static (bool Success, List<Windows.BackupImageInfo> Images, string Error) GetImageInfo(string wimPath)
        {
            try
            {
                // First get image count
                var (success, imageCount, error) = GetImageCount(wimPath);
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

                    if (WimMount_GetImageInfo(wimPath, i, name, 256, description, 1024, errorMsg, 512))
                    {
                        // Parse metadata returned from C++
                        // Name format: "Disk 5 Volume 1 (Incremental) - 2026-04-22 13:45:10"
                        // Description format: job name (for newer backups)
                        string imageName = name.ToString();
                        string desc = description.ToString();
                        string type = "Full"; // Default
                        DateTime imageDate = DateTime.Now; // Default to now if can't parse

                        if (string.IsNullOrWhiteSpace(desc) || desc.Equals("No description", StringComparison.OrdinalIgnoreCase))
                        {
                            desc = Path.GetFileNameWithoutExtension(wimPath);
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
    }
}
