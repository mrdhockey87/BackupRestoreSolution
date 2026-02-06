using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BackupUI.Services
{
    /// <summary>
    /// Native WIM mounting manager (No PowerShell, No Admin required)
    /// </summary>
    public class NativeBackupMountManager
    {
        // P/Invoke declarations for C++ WimMountManager
        [DllImport("BackupEngine.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool WimMount_MountWim(
            [MarshalAs(UnmanagedType.LPWStr)] string wimPath,
            [MarshalAs(UnmanagedType.LPWStr)] string backupName,
            [MarshalAs(UnmanagedType.LPWStr)] string backupType,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder mountPath,
            int mountPathSize,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder errorMsg,
            int errorMsgSize
        );

        [DllImport("BackupEngine.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool WimMount_UnmountWim(
            [MarshalAs(UnmanagedType.LPWStr)] string mountPath,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder errorMsg,
            int errorMsgSize
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
            string backupType)
        {
            try
            {
                var mountPath = new StringBuilder(260);
                var errorMsg = new StringBuilder(512);

                bool success = WimMount_MountWim(
                    wimPath,
                    backupName,
                    backupType,
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
    }
}
