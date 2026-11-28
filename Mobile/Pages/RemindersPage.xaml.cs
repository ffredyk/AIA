using System.Collections.ObjectModel;
using AIA.Mobile.Services;
using AIA.Sync.Contracts;

namespace AIA.Mobile.Pages
{
    public partial class RemindersPage : ContentPage
    {
        private readonly ObservableCollection<ReminderDto> _reminders = new();

        public RemindersPage()
        {
            InitializeComponent();
            RemindersList.ItemsSource = _reminders;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadAsync();
            await TrySyncAsync(silent: true);
        }

        private async Task LoadAsync()
        {
            try
            {
                await MobileApp.InitializeAsync();
                var reminders = await MobileApp.Store.GetRemindersAsync();
                _reminders.Clear();
                foreach (var reminder in reminders)
                    _reminders.Add(reminder);

                StatusLabel.Text = $"{_reminders.Count} reminders" + (MobileApp.Sync.LastSyncUtc is { } last
                    ? $" · synced {last.ToLocalTime():HH:mm}"
                    : string.Empty);
            }
            catch (Exception ex)
            {
                StatusLabel.Text = ex.Message;
            }
        }

        private async void BtnNew(object sender, EventArgs e)
        {
            var title = await DisplayPromptAsync("New reminder", "Title", "Next", "Cancel", "Reminder title");
            if (string.IsNullOrWhiteSpace(title))
                return;

            var minutesText = await DisplayPromptAsync("New reminder", "Due in minutes", "Create", "Cancel", "60", keyboard: Keyboard.Numeric);
            if (!int.TryParse(minutesText, out var minutes) || minutes <= 0)
                minutes = 60;

            var reminder = new ReminderDto
            {
                Id = Guid.NewGuid(),
                Title = title.Trim(),
                DueDate = DateTime.Now.AddMinutes(minutes),
                CreatedDate = DateTime.Now,
                Severity = SyncReminderSeverity.Medium
            };

            await MobileApp.Store.SaveReminderAsync(reminder);
            await LoadAsync();
            await TrySyncAsync(silent: true);
        }

        private async void BtnComplete(object sender, EventArgs e)
        {
            if (sender is not Button { CommandParameter: ReminderDto reminder })
                return;

            reminder.IsCompleted = !reminder.IsCompleted;
            await MobileApp.Store.SaveReminderAsync(reminder);
            await LoadAsync();
            await TrySyncAsync(silent: true);
        }

        private async void BtnDelete(object sender, EventArgs e)
        {
            if (sender is not Button { CommandParameter: ReminderDto reminder })
                return;

            var confirm = await DisplayAlert("Delete reminder", reminder.Title, "Delete", "Cancel");
            if (!confirm)
                return;

            await MobileApp.Store.DeleteAsync(SyncEntityType.Reminder, reminder.Id);
            await LoadAsync();
            await TrySyncAsync(silent: true);
        }

        private async void BtnSync(object sender, EventArgs e)
        {
            await TrySyncAsync(silent: false);
            await LoadAsync();
        }

        private async void OnRefreshing(object sender, EventArgs e)
        {
            await TrySyncAsync(silent: true);
            await LoadAsync();
            Refresh.IsRefreshing = false;
        }

        private async Task TrySyncAsync(bool silent)
        {
            try
            {
                await MobileApp.Sync.SyncNowAsync();
                if (!silent)
                    StatusLabel.Text = "Sync complete.";
            }
            catch (Exception ex)
            {
                if (!silent || MobileSettingsService.ServerUrl.Length > 0)
                    StatusLabel.Text = ex.Message;
            }
        }
    }
}
