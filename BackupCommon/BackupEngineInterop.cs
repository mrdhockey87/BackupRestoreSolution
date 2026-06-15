using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SecureServerBackupCommon
{
	public class BackupEngineInterop
	{
		private const string DllName = "SecureServerBackupEngine.dll";

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ProgressCallback(int percentage, [MarshalAs(UnmanagedType.LPWStr)] string message);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void LogCallback(
			int level,
			[MarshalAs(UnmanagedType.LPWStr)] string message,
			[MarshalAs(UnmanagedType.LPWStr)] string details);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int CreateVolumeSnapshot(
			string volume,
			StringBuilder snapshotPath,
			int pathSize);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int BackupFiles(
			string sourcePath,
			string destPath,
			[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? userExclusions,
			int userExclusionCount,
			ProgressCallback? callback,
			LogCallback? logCallback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int BackupFilesBySelections(
			string sourceRoot,
			string destPath,
			[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] includePaths,
			int includePathCount,
			[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? userExclusions,
			int userExclusionCount,
			ProgressCallback? callback,
			LogCallback? logCallback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int BackupHyperVVM(
			string vmName,
			string destPath,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int BackupHyperVVMIncremental(
			string vmName,
			string destPath,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int BackupHyperVVMDifferential(
			string vmName,
			string destPath,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int DeleteSnapshot(string snapshotId);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int RestoreFiles(
			string sourcePath,
			string destPath,
			bool overwriteExisting,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int RestoreHyperVVM(
			string backupPath,
			string vmName,
			string vmStoragePath,
			bool startAfterRestore,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int RestoreVolume(
			string backupPath,
			string targetVolume,
			bool restoreSystemState,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int RestoreVolumeFromImage(
			string archivePath,
			int imageIndex,
			string targetVolume,
			bool restoreSystemState,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int RestoreDisk(
			string backupPath,
			int targetDiskNumber,
			bool restoreSystemState,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int RestoreDiskFromImage(
			string archivePath,
			int imageIndex,
			int targetDiskNumber,
			bool restoreSystemState,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int VerifyBackupArchive(
			string archivePath,
			int expectedImageCount,
			StringBuilder errorMsg,
			int errorMsgSize,
			ProgressCallback? callback);

		public enum BackupImageHealthState
		{
			Healthy = 0,
			Repairable = 1,
			NonRepairable = 2
		}

		public enum HyperVVerifyStatus
		{
			Pass = 0,
			Fail = 1,
			Warning = 2,
			Skipped = 3
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
		public struct HyperVVerifyCheckResult
		{
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string CheckName;

			public HyperVVerifyStatus Status;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
			public string Detail;

			public ulong ElapsedMs;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
		public struct HyperVVerifyReport
		{
			[MarshalAs(UnmanagedType.I1)]
			public bool OverallPass;

			public int TotalChecks;
			public int PassCount;
			public int FailCount;
			public int WarnCount;

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
			public HyperVVerifyCheckResult[] Checks;

			public ulong SourceVhdxBytes;
			public ulong CloneVhdxBytes;

			[MarshalAs(UnmanagedType.I1)]
			public bool ChecksumMatch;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 65)]
			public string SourceChecksum;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 65)]
			public string CloneChecksum;

			[MarshalAs(UnmanagedType.I1)]
			public bool VmBootTestPerformed;

			[MarshalAs(UnmanagedType.I1)]
			public bool VmBootedCleanly;

			public uint VmBootTimeMs;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
			public string FirstFailureDetail;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
		public struct HyperVVerifyParams
		{
			[MarshalAs(UnmanagedType.LPWStr)]
			public string? SourceVmName;

			[MarshalAs(UnmanagedType.LPWStr)]
			public string CloneVmName;

			[MarshalAs(UnmanagedType.LPWStr)]
			public string? SourceVhdxPath;

			[MarshalAs(UnmanagedType.LPWStr)]
			public string CloneVhdxPath;

			[MarshalAs(UnmanagedType.LPWStr)]
			public string CloneExportPath;

			[MarshalAs(UnmanagedType.I1)]
			public bool PerformBootTest;

			[MarshalAs(UnmanagedType.I1)]
			public bool PerformChecksumVerify;

			public uint BootTestTimeoutSec;
		}

		[DllImport(DllName, EntryPoint = "CheckBackupImageHealth", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int CheckBackupImageStatus(
			string backupPath,
			int imageIndex,
			[MarshalAs(UnmanagedType.I1)] bool scanImage,
			StringBuilder healthMessage,
			int healthMessageSize,
			ProgressCallback? callback);

		[DllImport(DllName, EntryPoint = "RestoreBackupImageHealth", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int RepairBackupImageStatus(
			string backupPath,
			int imageIndex,
			[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? sourcePaths,
			int sourcePathCount,
			[MarshalAs(UnmanagedType.I1)] bool limitAccess,
			StringBuilder healthMessage,
			int healthMessageSize,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int RestoreSystemState(
			string backupPath,
			string targetVolume,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int ScheduleOfflineSystemSetupCl(
			string systemHivePath);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int RestoreArchiveToVhdxAsDisk(
			string archivePath,
			string vhdxPath,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int RestoreArchiveToVhdxAsVolume(
			string archivePath,
			string vhdxPath,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int ListBackupContents(
			string backupPath,
			StringBuilder buffer,
			int bufferSize);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int VerifyBackup(
			string backupPath,
			ProgressCallback? callback);

		[DllImport(DllName, EntryPoint = "HVE_VerifyClone", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
		private static extern int VerifyHyperVCloneNative(
			in HyperVVerifyParams parameters,
			out HyperVVerifyReport report);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern void GetLastErrorMessage(
			StringBuilder buffer,
			int bufferSize);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int BackupVolume(
			string volumePath,
			string destPath,
			bool includeSystemState,
			bool compress,
			[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? userExclusions,
			int userExclusionCount,
			ProgressCallback? callback,
			LogCallback? logCallback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int BackupDisk(
			int diskNumber,
			string destPath,
			bool includeSystemState,
			bool compress,
			[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? userExclusions,
			int userExclusionCount,
			ProgressCallback? callback,
			LogCallback? logCallback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int CreateIncrementalBackup(
			string sourcePath,
			string destPath,
			string baseBackupPath,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int CreateDifferentialBackup(
			string sourcePath,
			string destPath,
			string fullBackupPath,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int EnumerateVolumes(
			StringBuilder buffer,
			int bufferSize);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int EnumerateDisks(
			StringBuilder buffer,
			int bufferSize);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int EnumerateHyperVMachines(
			StringBuilder buffer,
			int bufferSize);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int EnumerateHyperVVirtualMachineDisks(
			StringBuilder buffer,
			int bufferSize);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int IsBootVolume(
			string volumePath,
			out bool isBootVolume);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int CreateRecoveryEnvironment(
			string usbDriveLetter,
			string programPath,
			ProgressCallback? callback);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int EnumerateBackupDates(
			string backupPath,
			StringBuilder buffer,
			int bufferSize);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern int RestoreWithManifest(
			string backupPath,
			string destPath,
			string manifest,
			bool overwriteExisting,
			bool restoreSystemState,
			bool preservePermissions,
			ProgressCallback? callback);

		public static int VerifyBackupArchive(
			string backupPath,
			ProgressCallback? callback)
		{
			return VerifyBackup(backupPath, callback);
		}

		public static int VerifyHyperVClone(
			HyperVVerifyParams parameters,
			out HyperVVerifyReport report)
		{
			parameters.CloneVmName = string.IsNullOrWhiteSpace(parameters.CloneVmName)
				? throw new ArgumentException("Clone VM name is required.", nameof(parameters))
				: parameters.CloneVmName;

			parameters.CloneVhdxPath = string.IsNullOrWhiteSpace(parameters.CloneVhdxPath)
				? throw new ArgumentException("Clone virtual disk path is required.", nameof(parameters))
				: parameters.CloneVhdxPath;

			parameters.CloneExportPath = string.IsNullOrWhiteSpace(parameters.CloneExportPath)
				? throw new ArgumentException("Clone export path is required.", nameof(parameters))
				: parameters.CloneExportPath;

			report = new HyperVVerifyReport
			{
				Checks = new HyperVVerifyCheckResult[32],
				SourceChecksum = string.Empty,
				CloneChecksum = string.Empty,
				FirstFailureDetail = string.Empty
			};

			return VerifyHyperVCloneNative(in parameters, out report);
		}

		public static int CheckBackupImageStatusWithProgress(
			string backupPath,
			int imageIndex,
			bool scanImage,
			StringBuilder healthMessage,
			int healthMessageSize,
			ProgressCallback? callback)
		{
			return CheckBackupImageStatus(backupPath, imageIndex, scanImage, healthMessage, healthMessageSize, callback);
		}

		public static int RepairBackupImageStatusWithProgress(
			string backupPath,
			int imageIndex,
			string[]? sourcePaths,
			int sourcePathCount,
			bool limitAccess,
			StringBuilder healthMessage,
			int healthMessageSize,
			ProgressCallback? callback)
		{
			return RepairBackupImageStatus(backupPath, imageIndex, sourcePaths, sourcePathCount, limitAccess, healthMessage, healthMessageSize, callback);
		}

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
		public static extern void SetCurrentJobName(string jobName);

		[DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
		public static extern void ClearCurrentJobName();
	}
}
