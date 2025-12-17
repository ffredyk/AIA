namespace AIA.Views
{
    /// <summary>
    /// Outlook tab view - now handled by the Outlook plugin
    /// This UserControl is kept for backward compatibility but 
    /// all functionality is provided by the plugin system.
    /// </summary>
    public partial class OutlookTabView : System.Windows.Controls.UserControl
    {
        public OutlookTabView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Event raised when a toast notification should be shown
        /// </summary>
        public event System.EventHandler<string>? ToastRequested;

        // Stub methods to satisfy XAML event handlers - these are no longer functional
        // as Outlook integration is now handled by the plugin system
        private void BtnRefreshOutlook(object sender, System.Windows.RoutedEventArgs e) { }
        private void OutlookEmailItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
        private void BtnMarkEmailComplete(object sender, System.Windows.RoutedEventArgs e) { }
        private void BtnMarkSelectedEmailComplete(object sender, System.Windows.RoutedEventArgs e) { }
        private void BtnClearEmailFlag(object sender, System.Windows.RoutedEventArgs e) { }
        private void BtnOpenInOutlook(object sender, System.Windows.RoutedEventArgs e) { }
    }
}
