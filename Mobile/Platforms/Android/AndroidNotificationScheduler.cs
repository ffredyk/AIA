using Android.App;
using Android.Content;
using AIA.Sync.Contracts;
using Microsoft.Maui.Storage;

namespace AIA.Mobile.Platforms.Android
{
    public static class AndroidNotificationScheduler
    {
        private const string ScheduledIdsKey = "aia_scheduled_alarm_ids";

        public static Task RescheduleAsync(
            IReadOnlyList<ReminderDto> reminders,
            IReadOnlyList<CalendarEventDto> events,
            CancellationToken cancellationToken)
        {
            var context = global::Android.App.Application.Context;
            var alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
            if (alarmManager == null)
                return Task.CompletedTask;

            CancelPrevious(context, alarmManager);

            var scheduled = new List<string>();
            var now = DateTimeOffset.Now;

            foreach (var reminder in reminders.Where(r => !r.IsCompleted))
            {
                var trigger = new DateTimeOffset(reminder.DueDate);
                if (trigger <= now)
                    trigger = now.AddSeconds(5);

                Schedule(context, alarmManager, reminder.Id, "Reminder: " + reminder.Title,
                    reminder.DueDate.ToString("g"), trigger, scheduled);
            }

            foreach (var calendarEvent in events)
            {
                var trigger = new DateTimeOffset(calendarEvent.Start).AddMinutes(-calendarEvent.ReminderMinutesBefore);
                if (trigger <= now || calendarEvent.End <= DateTime.Now)
                    continue;

                Schedule(context, alarmManager, calendarEvent.Id, "Event: " + calendarEvent.Title,
                    calendarEvent.Start.ToString("g"), trigger, scheduled);
            }

            Preferences.Default.Set(ScheduledIdsKey, string.Join(';', scheduled));
            return Task.CompletedTask;
        }

        private static void Schedule(
            Context context,
            AlarmManager alarmManager,
            Guid id,
            string title,
            string message,
            DateTimeOffset trigger,
            List<string> scheduled)
        {
            var requestCode = RequestCode(id);
            var intent = new Intent(context, typeof(NotificationReceiver));
            intent.PutExtra("id", requestCode);
            intent.PutExtra("title", title);
            intent.PutExtra("message", message);

            var pending = PendingIntent.GetBroadcast(
                context,
                requestCode,
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            if (pending == null)
                return;

            try
            {
                alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, trigger.ToUnixTimeMilliseconds(), pending);
                scheduled.Add(requestCode.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Alarm could not be scheduled: {ex.Message}");
            }
        }

        private static void CancelPrevious(Context context, AlarmManager alarmManager)
        {
            var previous = Preferences.Default.Get(ScheduledIdsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(previous))
                return;

            foreach (var value in previous.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(value, out var requestCode))
                    continue;

                var intent = new Intent(context, typeof(NotificationReceiver));
                var pending = PendingIntent.GetBroadcast(
                    context,
                    requestCode,
                    intent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

                if (pending != null)
                {
                    alarmManager.Cancel(pending);
                    pending.Cancel();
                }
            }
        }

        private static int RequestCode(Guid id)
        {
            var bytes = id.ToByteArray();
            return BitConverter.ToInt32(bytes, 0) & 0x7fffffff;
        }
    }
}
