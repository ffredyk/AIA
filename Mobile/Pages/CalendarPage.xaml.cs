using System.Collections.ObjectModel;
using AIA.Mobile.Services;
using AIA.Sync.Contracts;

namespace AIA.Mobile.Pages
{
    public partial class CalendarPage : ContentPage
    {
        private readonly ObservableCollection<CalendarEventDto> _events = new();

        public CalendarPage()
        {
            InitializeComponent();
            EventsList.ItemsSource = _events;
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
                var events = await MobileApp.Store.GetCalendarEventsAsync();
                _events.Clear();
                foreach (var calendarEvent in events)
                    _events.Add(calendarEvent);

                StatusLabel.Text = $"{_events.Count} events" + (MobileApp.Sync.LastSyncUtc is { } last
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
            var title = await DisplayPromptAsync("New event", "Title", "Next", "Cancel", "Event title");
            if (string.IsNullOrWhiteSpace(title))
                return;

            var startText = await DisplayPromptAsync("New event", "Start (yyyy-MM-dd HH:mm)", "Next", "Cancel",
                DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm"));

            if (!DateTime.TryParse(startText, out var start))
                start = DateTime.Now.AddHours(1);

            var minutesText = await DisplayPromptAsync("New event", "Duration in minutes", "Create", "Cancel", "60",
                keyboard: Keyboard.Numeric);
            if (!int.TryParse(minutesText, out var minutes) || minutes <= 0)
                minutes = 60;

            var calendarEvent = new CalendarEventDto
            {
                Id = Guid.NewGuid(),
                Title = title.Trim(),
                Start = start,
                End = start.AddMinutes(minutes),
                ReminderMinutesBefore = 15,
                CreatedDate = DateTime.Now
            };

            await MobileApp.Store.SaveCalendarEventAsync(calendarEvent);
            await LoadAsync();
            await TrySyncAsync(silent: true);
        }

        private async void BtnDelete(object sender, EventArgs e)
        {
            if (sender is not Button { CommandParameter: CalendarEventDto calendarEvent })
                return;

            var confirm = await DisplayAlert("Delete event", calendarEvent.Title, "Delete", "Cancel");
            if (!confirm)
                return;

            await MobileApp.Store.DeleteAsync(SyncEntityType.CalendarEvent, calendarEvent.Id);
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
