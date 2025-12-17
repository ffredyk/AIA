using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIA.Models;

namespace AIA.Views
{
    public partial class RemindersTabView
    {
        public RemindersTabView()
        {
            InitializeComponent();
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

        public void FocusNewReminderInput()
        {
            NewReminderTitleInput?.Focus();
        }

        public void UpdateTimeComboBoxes(ReminderItem reminder)
        {
            ReminderHourCombo.SelectedIndex = reminder.DueDate.Hour;
            int minuteIndex = reminder.DueDate.Minute / 5;
            if (minuteIndex >= 0 && minuteIndex < ReminderMinuteCombo.Items.Count)
            {
                ReminderMinuteCombo.SelectedIndex = minuteIndex;
            }
        }

        #region New Reminder Input

        private void BtnConfirmNewReminder(object sender, RoutedEventArgs e)
        {
            ViewModel?.AddNewReminder(ViewModel.NewReminderTitle);
        }

        private void BtnCancelNewReminder(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            ViewModel.NewReminderTitle = string.Empty;
            ViewModel.IsAddingNewReminder = false;
        }

        private void NewReminderTitleInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmNewReminder(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelNewReminder(sender, e);
                e.Handled = true;
            }
        }

        #endregion

        #region Reminder List Events

        private void ReminderItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is ReminderItem reminder)
            {
                ViewModel.SelectedReminder = reminder;
                UpdateTimeComboBoxes(reminder);
            }
        }

        private void BtnToggleReminderComplete(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is ReminderItem reminder)
            {
                ViewModel.ToggleReminderComplete(reminder);
            }
            e.Handled = true;
        }

        #endregion

        #region Reminder Details Events

        private void BtnDeleteReminder(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedReminder == null) return;
            ViewModel.DeleteReminder(ViewModel.SelectedReminder);
        }

        private void ReminderTimeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel?.SelectedReminder == null) return;

            if (ReminderHourCombo.SelectedIndex < 0 || ReminderMinuteCombo.SelectedIndex < 0)
                return;

            int hour = ReminderHourCombo.SelectedIndex;
            int minute = ReminderMinuteCombo.SelectedIndex * 5;

            var currentDate = ViewModel.SelectedReminder.DueDate;
            ViewModel.SelectedReminder.DueDate = new DateTime(
                currentDate.Year,
                currentDate.Month,
                currentDate.Day,
                hour,
                minute,
                0);
        }

        #endregion
    }
}
