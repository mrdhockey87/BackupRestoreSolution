using System.Collections.Generic;
using SecureServerBackup.Models;
using SecureServerBackup.Windows;
using Xunit;

namespace SecureServerBackup.Tests;

public sealed class VolumeConfigurationWindowTests
{
	[Fact]
	public void CalculateDefaultLayout_WhenTargetIsSmaller_UsesDataBasedMinimumForResizableVolume()
	{
		List<VolumeInfo> sourceVolumes =
		[
			new VolumeInfo
			{
				Label = "Data",
				Size = 1000,
				UsedSpace = 400,
				FileSystem = "NTFS",
				IsSystemVolume = true,
				IsBootVolume = true
			}
		];

		VolumeConfigurationWindow.VolumeSizingLayout layout = VolumeConfigurationWindow.CalculateDefaultLayout(sourceVolumes, 500, 1, 1);

		Assert.Equal(440, layout.MinimumSizes[0]);
		Assert.Equal(500, layout.CurrentSizes[0]);
		Assert.Equal(500, layout.MaximumSizes[0]);
	}

	[Fact]
	public void CalculateDefaultLayout_WhenTargetIsLarger_DefaultsToUsingEntireTargetSpace()
	{
		List<VolumeInfo> sourceVolumes =
		[
			new VolumeInfo
			{
				Label = "Data",
				Size = 1000,
				UsedSpace = 400,
				FileSystem = "NTFS",
				IsSystemVolume = true,
				IsBootVolume = true
			}
		];

		VolumeConfigurationWindow.VolumeSizingLayout layout = VolumeConfigurationWindow.CalculateDefaultLayout(sourceVolumes, 1500, 1, 1);

		Assert.Equal(1500, layout.CurrentSizes[0]);
	}

	[Fact]
	public void CalculateDefaultLayout_WhenMultipleResizableVolumesExist_AssignsExtraSpaceToBootSystemVolume()
	{
		List<VolumeInfo> sourceVolumes =
		[
			new VolumeInfo
			{
				Label = "System",
				Size = 600,
				UsedSpace = 300,
				FileSystem = "NTFS",
				IsSystemVolume = true,
				IsBootVolume = true
			},
			new VolumeInfo
			{
				Label = "Apps",
				Size = 700,
				UsedSpace = 300,
				FileSystem = "NTFS"
			}
		];

		VolumeConfigurationWindow.VolumeSizingLayout layout = VolumeConfigurationWindow.CalculateDefaultLayout(sourceVolumes, 2000, 1, 1);

		Assert.Equal(2000, layout.CurrentSizes[0] + layout.CurrentSizes[1]);
		Assert.True(layout.CurrentSizes[0] > layout.CurrentSizes[1]);
		Assert.Equal(330, layout.MinimumSizes[0]);
		Assert.Equal(330, layout.MinimumSizes[1]);
	}
}
