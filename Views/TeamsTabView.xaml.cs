namespace AIA.Views
{
    /// <summary>
    /// Teams tab view - now handled by the Teams plugin
    /// This UserControl is kept for backward compatibility but 
    /// all functionality is provided by the plugin system.
    /// </summary>
    public partial class TeamsTabView : System.Windows.Controls.UserControl
    {
        public TeamsTabView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Event raised when a toast notification should be shown
        /// </summary>
        public event System.EventHandler<string>? ToastRequested;

        // Stub methods to satisfy XAML event handlers - these are no longer functional
        // as Teams integration is now handled by the plugin system
        private void BtnRefreshTeams(object sender, System.Windows.RoutedEventArgs e) { }
        private void BtnOpenTeams(object sender, System.Windows.RoutedEventArgs e) { }
        private void TeamsMeetingItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
        private void BtnJoinTeamsMeeting(object sender, System.Windows.RoutedEventArgs e) { }
        private void TeamsMessageItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
        private void BtnCompleteTeamsReminder(object sender, System.Windows.RoutedEventArgs e) { }
    }
}
