using System.Windows;
using BackupUI.Windows;

namespace BackupUI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            LoadVersion();
        }

        private void LoadVersion()
        {
            txtVersion.Text = VersionClass.GetVersion();
        }
        
        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        
        private void NewBackup_Click(object sender, RoutedEventArgs e)
        {
            var window = new BackupWindowNew();
            window.ShowDialog();
        }
        
        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            var window = new RestoreWindowNew();
            window.ShowDialog();
        }
        
        private void ManageSchedules_Click(object sender, RoutedEventArgs e) => new ScheduleManagementWindow().ShowDialog();
        private void ServiceManagement_Click(object sender, RoutedEventArgs e) => new ServiceManagementWindow().ShowDialog();
        private void RecoveryEnvironmentCreator_Click(object sender, RoutedEventArgs e) => new RecoveryEnvironmentWindow().ShowDialog();
        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"Backup & Restore Solution\n{VersionClass.GetVersion()}\n\nEnterprise backup with scheduling and disaster recovery", 
                "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
