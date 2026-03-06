using System;
using System.Threading.Tasks;
using System.Windows;

namespace BackupUI.Windows
{
    public partial class MountProgressWindow : Window
    {
        private bool _isClosed = false;

        public MountProgressWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Update backup name
        /// </summary>
        public void SetBackupName(string name)
        {
            if (!_isClosed && txtBackupName != null)
            {
                Dispatcher.Invoke(() => txtBackupName.Text = $"Backup: {name}");
            }
        }

        /// <summary>
        /// Update status message
        /// </summary>
        public void SetStatus(string status)
        {
            if (!_isClosed && txtStatus != null)
            {
                Dispatcher.Invoke(() => txtStatus.Text = status);
            }
        }

        /// <summary>
        /// Set progress percentage (0-100), or -1 for indeterminate
        /// </summary>
        public void SetProgress(int percentage)
        {
            if (!_isClosed && progressBar != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (percentage < 0)
                    {
                        progressBar.IsIndeterminate = true;
                    }
                    else
                    {
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = percentage;
                        progressBar.Maximum = 100;
                    }
                });
            }
        }

        /// <summary>
        /// Close the progress window
        /// </summary>
        public void CloseProgress()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        Close();
                    }
                    catch
                    {
                        // Window may already be closed
                    }
                });
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            base.OnClosed(e);
        }
    }
}
