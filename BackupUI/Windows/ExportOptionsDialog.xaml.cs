using System.Windows;

namespace SecureServerBackup.Windows
{
    public partial class ExportOptionsDialog : Window
    {
        public string ExportFormat { get; private set; } = "CSV";

        public ExportOptionsDialog()
        {
            InitializeComponent();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ExportFormat = rbCSV.IsChecked == true ? "CSV" : "Text";
            DialogResult = true;
            Close();
        }
    }
}
