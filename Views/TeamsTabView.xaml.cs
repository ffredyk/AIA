using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIA.Models;

namespace AIA.Views
{
    public partial class TeamsTabView
    {
        public TeamsTabView()
        {
            InitializeComponent();
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

        /// <summary>
        /// Event raised when a toast notification should be shown
        /// </summary>
        public event EventHandler<string>? ToastRequested;

        private async void BtnRefreshTeams(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            await ViewModel.RefreshTeamsDataAsync();
        }

        private void BtnOpenTeams(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            if (!ViewModel.OpenTeamsApp())
            {
                System.Windows.MessageBox.Show("Could not open Microsoft Teams.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TeamsMeetingItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is TeamsMeeting meeting)
            {
                ViewModel.SelectedTeamsMeeting = meeting;
                ViewModel.SelectedTeamsMessage = null;
            }
        }

        private void BtnJoinTeamsMeeting(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is TeamsMeeting meeting)
            {
                if (ViewModel.JoinTeamsMeeting(meeting))
                {
                    ToastRequested?.Invoke(this, "Opening Teams meeting...");
                }
                else
                {
                    System.Windows.MessageBox.Show("Could not join the meeting. No join URL available.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            e.Handled = true;
        }

        private void TeamsMessageItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is TeamsMessage message)
            {
                ViewModel.SelectedTeamsMessage = message;
                ViewModel.SelectedTeamsMeeting = null;
                ViewModel.MarkTeamsMessageAsRead(message);
            }
        }

        private void BtnCompleteTeamsReminder(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is TeamsReminder reminder)
            {
                reminder.IsCompleted = !reminder.IsCompleted;
                ViewModel.CompleteTeamsReminder(reminder);
            }
            e.Handled = true;
        }
    }
}
