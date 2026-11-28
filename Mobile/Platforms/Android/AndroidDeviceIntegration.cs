using AIA.Mobile.Services;

namespace AIA.Mobile.Platforms.Android
{
    public sealed class AndroidDeviceIntegration : IDeviceIntegration
    {
        private readonly SqliteSyncStore _store;

        public AndroidDeviceIntegration(SqliteSyncStore store)
        {
            _store = store;
        }

        public async Task RefreshAsync(SqliteSyncStore store, CancellationToken cancellationToken)
        {
            var reminders = await store.GetRemindersAsync().ConfigureAwait(false);
            var events = await store.GetCalendarEventsAsync().ConfigureAwait(false);

            if (MobileSettingsService.CalendarMirrorEnabled)
            {
                try
                {
                    var calendar = new AndroidCalendarService(store);
                    await calendar.SyncAsync(events, reminders, cancellationToken).ConfigureAwait(false);
                    events = await store.GetCalendarEventsAsync().ConfigureAwait(false);
                    reminders = await store.GetRemindersAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Device calendar sync failed: {ex}");
                }
            }

            if (MobileSettingsService.NotificationsEnabled)
            {
                try
                {
                    await AndroidNotificationScheduler.RescheduleAsync(reminders, events, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Notification scheduling failed: {ex}");
                }
            }
        }
    }
}
