using System.Windows;
using System.Windows.Controls;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace AIA.Views.Automation
{
    /// <summary>
    /// Configuration view for Plugin trigger
    /// </summary>
    public partial class PluginTriggerConfigView : WpfUserControl
    {
        public PluginTriggerConfigView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Check if there are any plugins available
            UpdatePluginAvailability();
        }

        private void PluginCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Refresh triggers list when plugin changes
            UpdatePluginAvailability();
        }

        private void UpdatePluginAvailability()
        {
            var hasPlugins = PluginCombo.Items.Count > 0;
            NoPluginsMessage.Visibility = hasPlugins ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
