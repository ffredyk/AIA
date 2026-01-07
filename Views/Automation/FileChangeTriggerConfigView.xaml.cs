using System.Windows;
using System.Windows.Forms;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace AIA.Views.Automation
{
    /// <summary>
    /// Configuration view for File Change trigger
    /// </summary>
    public partial class FileChangeTriggerConfigView : WpfUserControl
    {
        public FileChangeTriggerConfigView()
        {
            InitializeComponent();
        }

        private void BtnBrowseFolder(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select folder to watch",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (DataContext is Models.Automation.FileChangeTrigger trigger && 
                !string.IsNullOrEmpty(trigger.WatchPath) && 
                System.IO.Directory.Exists(trigger.WatchPath))
            {
                dialog.SelectedPath = trigger.WatchPath;
            }

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (DataContext is Models.Automation.FileChangeTrigger t)
                {
                    t.WatchPath = dialog.SelectedPath;
                }
            }
        }
    }
}
