using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SecureServerBackupCommon
{
	public static class SelectedFileListStore
	{
		private static readonly string JobDataDirectory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
			"SecureServerBackupService");

		public static string GetListFilePath(string jobName)
		{
			if (string.IsNullOrWhiteSpace(jobName))
			{
				throw new ArgumentException("A job name is required.", nameof(jobName));
			}

			string sanitizedJobName = SanitizeFileName(jobName.Trim());
			return Path.Combine(JobDataDirectory, $"{sanitizedJobName}list.json");
		}

		public static List<string> Load(string jobName)
		{
			string listFilePath = GetListFilePath(jobName);
			if (!File.Exists(listFilePath))
			{
				return new List<string>();
			}

			string json = File.ReadAllText(listFilePath);
			return JsonSerializer.Deserialize<List<string>>(json)
				?.Where(path => !string.IsNullOrWhiteSpace(path))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList() ?? new List<string>();
		}

		public static void Save(string jobName, IEnumerable<string> selectedPaths)
		{
			ArgumentNullException.ThrowIfNull(selectedPaths);

			Directory.CreateDirectory(JobDataDirectory);

			List<string> normalizedPaths = selectedPaths
				.Where(path => !string.IsNullOrWhiteSpace(path))
				.Select(path => path.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			string listFilePath = GetListFilePath(jobName);
			string json = JsonSerializer.Serialize(normalizedPaths, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(listFilePath, json);
		}

		public static void Delete(string jobName)
		{
			string listFilePath = GetListFilePath(jobName);
			if (File.Exists(listFilePath))
			{
				File.Delete(listFilePath);
			}
		}

		private static string SanitizeFileName(string value)
		{
			char[] invalidCharacters = Path.GetInvalidFileNameChars();
			return new string(value.Select(ch => invalidCharacters.Contains(ch) ? '_' : ch).ToArray());
		}
	}
}