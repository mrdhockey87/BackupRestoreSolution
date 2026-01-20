using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BackupUI
{
	static class VersionClass
	{
		static public string version_word = "Version:";

		// Get version from assembly - this will always match the project file version
		static public string version_string = GetAssemblyVersion();

		static public string GetVersion()
		{
			return string.Format("{0} {1}", VersionClass.version_word, VersionClass.version_string);
		}

		static private string GetAssemblyVersion()
		{
			try
			{
				Assembly assembly = Assembly.GetExecutingAssembly();
				
				// Try to get the informational version first (this reads from the .csproj <Version> or <InformationalVersion>)
				var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
				if (infoVersionAttr != null && !string.IsNullOrEmpty(infoVersionAttr.InformationalVersion))
				{
					return infoVersionAttr.InformationalVersion;
				}
				
				// Fall back to file version
				var fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
				if (fileVersionAttr != null && !string.IsNullOrEmpty(fileVersionAttr.Version))
				{
					return fileVersionAttr.Version;
				}
				
				// Fall back to assembly version
				var version = assembly.GetName().Version;
				if (version != null)
				{
					return version.ToString();
				}
				
				// Last resort fallback
				return "3.0.0.0";
			}
			catch
			{
				// Fallback version if assembly version fails
				return "3.0.0.0";
			}
		}
	}
}

/*
 *  Version 3.0.0.0 Fix the dll not getting copied to the output directory. Need to fix the new backup
 *                  should auto select system state when the boot voulume or disk is selected, also the selection for 
 *                  the disk or volume should be a explorer style tree view. The selection for the what too back up should be
 *                  either check boxes or radio button group and should also include the Hyper-v virtual machines. (Maybe).
 *  Version 2.0.0.0 Fixed build errors that occurred after the AI built the first version.
 *  Version 1.0.0.0 added Version information for the application (This file) and had the AI write the app to run the backups
 *                  for windows servers and hyper-v virtual machines.
 * 
 * */