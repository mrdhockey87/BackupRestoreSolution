// FolderPickerHelper.cs - TODO: Add from conversation
using Microsoft.Win32;

namespace BackupUI.Helpers
{
    public static class FolderPickerHelper
    {
        public static string? PickFolder(string title = "Select Folder", string? initialDirectory = null)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = title };
            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }
        public static string? PickFile(string title, string filter, string? initialDirectory = null)
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
        public static string? PickBackupLocation(string suggestedName) => PickFolder();
        public static string? PickBackupToRestore() => PickFolder("Select Backup");
    }
}
