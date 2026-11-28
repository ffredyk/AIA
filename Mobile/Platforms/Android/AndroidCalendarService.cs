using Android.Content;
using Android.Provider;
using AIA.Mobile.Services;
using AIA.Sync.Contracts;

namespace AIA.Mobile.Platforms.Android
{
    /// <summary>
    /// Mirrors AIA events and due reminders into a dedicated local calendar and imports edits
    /// made in that calendar. External calendars are never modified.
    /// </summary>
    public sealed class AndroidCalendarService
    {
        private const string AccountName = "AIA";
        private const string AccountType = "LOCAL";
        private const string CalendarName = "AIA";
        private const string SyncDataColumn = "sync_data1";

        private readonly SqliteSyncStore _store;

        public AndroidCalendarService(SqliteSyncStore store)
        {
            _store = store;
        }

        public async Task SyncAsync(
            IReadOnlyList<CalendarEventDto> events,
            IReadOnlyList<ReminderDto> reminders,
            CancellationToken cancellationToken)
        {
            if (!HasPermissions())
                return;

            var resolver = global::Android.App.Application.Context.ContentResolver;
            if (resolver == null)
                return;

            var calendarId = EnsureCalendar(resolver);
            if (calendarId <= 0)
                return;

            var activeKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var calendarEvent in events)
            {
                var key = $"CalendarEvent:{calendarEvent.Id:N}";
                activeKeys.Add(key);
                await UpsertEventAsync(resolver, calendarId, key, "event", calendarEvent.Title,
                    calendarEvent.Description, calendarEvent.Location, calendarEvent.Start, calendarEvent.End,
                    calendarEvent.IsAllDay, calendarEvent.RecurrenceType, calendarEvent.RecurrenceInterval,
                    calendarEvent.ReminderMinutesBefore, calendarEvent.Id, cancellationToken).ConfigureAwait(false);
            }

            foreach (var reminder in reminders.Where(r => !r.IsCompleted))
            {
                var key = $"Reminder:{reminder.Id:N}";
                activeKeys.Add(key);
                var start = reminder.DueDate.Date;
                var end = start.AddDays(1);
                await UpsertEventAsync(resolver, calendarId, key, "reminder", "Reminder: " + reminder.Title,
                    string.Empty, string.Empty, start, end, true, SyncRecurrenceType.None, 1, 0,
                    reminder.Id, cancellationToken).ConfigureAwait(false);
            }

            await RemoveStaleMappingsAsync(resolver, activeKeys, cancellationToken).ConfigureAwait(false);
            await ImportAiaCalendarEditsAsync(resolver, calendarId, cancellationToken).ConfigureAwait(false);
        }

        private static bool HasPermissions()
        {
            var context = global::Android.App.Application.Context;
            return AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, global::Android.Manifest.Permission.ReadCalendar) == global::Android.Content.PM.Permission.Granted &&
                   AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, global::Android.Manifest.Permission.WriteCalendar) == global::Android.Content.PM.Permission.Granted;
        }

        private static long EnsureCalendar(ContentResolver resolver)
        {
            var projection = new[] { CalendarContract.Calendars.InterfaceConsts.Id };
            var selection = $"{CalendarContract.Calendars.InterfaceConsts.AccountName}=? AND {CalendarContract.Calendars.InterfaceConsts.AccountType}=?";
            using var cursor = resolver.Query(
                CalendarContract.Calendars.ContentUri,
                projection,
                selection,
                new[] { AccountName, AccountType },
                null);

            if (cursor != null && cursor.MoveToFirst())
                return cursor.GetLong(0);

            var values = new ContentValues();
            values.Put(CalendarContract.Calendars.InterfaceConsts.AccountName, AccountName);
            values.Put(CalendarContract.Calendars.InterfaceConsts.AccountType, AccountType);
            values.Put(CalendarContract.Calendars.Name, CalendarName);
            values.Put(CalendarContract.Calendars.InterfaceConsts.CalendarDisplayName, CalendarName);
            values.Put(CalendarContract.Calendars.InterfaceConsts.OwnerAccount, AccountName);
            values.Put(CalendarContract.Calendars.InterfaceConsts.CalendarColor, unchecked((int)0xFF0078D4));
            values.Put(CalendarContract.Calendars.InterfaceConsts.Visible, 1);
            values.Put(CalendarContract.Calendars.InterfaceConsts.SyncEvents, 1);
            values.Put(CalendarContract.Calendars.InterfaceConsts.CalendarAccessLevel, 700);

            var insertUri = CalendarContract.Calendars.ContentUri.BuildUpon()
                .AppendQueryParameter(CalendarContract.CallerIsSyncadapter, "true")
                .AppendQueryParameter(CalendarContract.Calendars.InterfaceConsts.AccountName, AccountName)
                .AppendQueryParameter(CalendarContract.Calendars.InterfaceConsts.AccountType, AccountType)
                .Build();

            try
            {
                var inserted = resolver.Insert(insertUri, values);
                return inserted?.LastPathSegment != null && long.TryParse(inserted.LastPathSegment, out var id) ? id : 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AIA calendar could not be created: {ex.Message}");
                return 0;
            }
        }

        private async Task UpsertEventAsync(
            ContentResolver resolver,
            long calendarId,
            string key,
            string kind,
            string title,
            string description,
            string location,
            DateTime start,
            DateTime end,
            bool isAllDay,
            SyncRecurrenceType recurrence,
            int recurrenceInterval,
            int reminderMinutes,
            Guid entityId,
            CancellationToken cancellationToken)
        {
            var mapping = await _store.GetCalendarMappingAsync(key).ConfigureAwait(false);
            var syncData = kind == "event" ? $"CalendarEvent:{entityId:N}" : $"Reminder:{entityId:N}";
            var values = BuildEventValues(calendarId, title, description, location, start, end, isAllDay,
                recurrence, recurrenceInterval, syncData);

            long eventId = 0;
            if (mapping != null && long.TryParse(mapping.DeviceEventId, out var existingId))
            {
                var uri = ContentUris.WithAppendedId(CalendarContract.Events.ContentUri, existingId);
                var updated = resolver.Update(uri, values, null, null);
                if (updated > 0)
                    eventId = existingId;
            }

            if (eventId == 0)
            {
                var inserted = resolver.Insert(CalendarContract.Events.ContentUri, values);
                if (inserted?.LastPathSegment != null && long.TryParse(inserted.LastPathSegment, out var newId))
                    eventId = newId;
            }

            if (eventId == 0)
                return;

            if (reminderMinutes > 0)
                ReplaceReminder(resolver, eventId, reminderMinutes);

            await _store.SaveCalendarMappingAsync(new CalendarMapRow
            {
                Key = key,
                Kind = kind,
                CalendarId = calendarId.ToString(),
                DeviceEventId = eventId.ToString()
            }).ConfigureAwait(false);
        }

        private static ContentValues BuildEventValues(
            long calendarId,
            string title,
            string description,
            string location,
            DateTime start,
            DateTime end,
            bool isAllDay,
            SyncRecurrenceType recurrence,
            int recurrenceInterval,
            string syncData)
        {
            var values = new ContentValues();
            values.Put(CalendarContract.Events.InterfaceConsts.CalendarId, calendarId);
            values.Put(CalendarContract.Events.InterfaceConsts.Title, title);
            values.Put(CalendarContract.Events.InterfaceConsts.Description, description);
            values.Put(CalendarContract.Events.InterfaceConsts.EventLocation, location);
            values.Put(CalendarContract.Events.InterfaceConsts.AllDay, isAllDay ? 1 : 0);
            values.Put(CalendarContract.Events.InterfaceConsts.EventTimezone, isAllDay ? "UTC" : Java.Util.TimeZone.Default!.ID);

            if (isAllDay)
            {
                values.Put(CalendarContract.Events.InterfaceConsts.Dtstart, ToUtcMidnightMillis(start));
                values.Put(CalendarContract.Events.InterfaceConsts.Dtend, ToUtcMidnightMillis(end.Date.AddDays(1)));
            }
            else
            {
                values.Put(CalendarContract.Events.InterfaceConsts.Dtstart, ToLocalMillis(start));
                values.Put(CalendarContract.Events.InterfaceConsts.Dtend, ToLocalMillis(end));
            }

            values.Put(SyncDataColumn, syncData);

            var rrule = BuildRRule(recurrence, recurrenceInterval);
            if (!string.IsNullOrEmpty(rrule))
                values.Put(CalendarContract.Events.InterfaceConsts.Rrule, rrule);

            return values;
        }

        private static string? BuildRRule(SyncRecurrenceType recurrence, int interval)
        {
            interval = Math.Max(1, interval);
            return recurrence switch
            {
                SyncRecurrenceType.Daily => $"FREQ=DAILY;INTERVAL={interval}",
                SyncRecurrenceType.Weekly => $"FREQ=WEEKLY;INTERVAL={interval}",
                SyncRecurrenceType.Monthly => $"FREQ=MONTHLY;INTERVAL={interval}",
                SyncRecurrenceType.Yearly => $"FREQ=YEARLY;INTERVAL={interval}",
                _ => null
            };
        }

        private static void ReplaceReminder(ContentResolver resolver, long eventId, int minutes)
        {
            resolver.Delete(CalendarContract.Reminders.ContentUri, $"{CalendarContract.Reminders.InterfaceConsts.EventId}=?",
                new string[] { eventId.ToString() ?? string.Empty });

            var values = new ContentValues();
            values.Put(CalendarContract.Reminders.InterfaceConsts.EventId, eventId);
            values.Put(CalendarContract.Reminders.InterfaceConsts.Minutes, minutes);
            values.Put(CalendarContract.Reminders.InterfaceConsts.Method, 1);
            resolver.Insert(CalendarContract.Reminders.ContentUri, values);
        }

        private async Task RemoveStaleMappingsAsync(
            ContentResolver resolver,
            HashSet<string> activeKeys,
            CancellationToken cancellationToken)
        {
            foreach (var kind in new[] { "event", "reminder" })
            {
                var mappings = await _store.GetCalendarMappingsAsync(kind).ConfigureAwait(false);
                foreach (var mapping in mappings)
                {
                    if (activeKeys.Contains(mapping.Key))
                        continue;

                    if (long.TryParse(mapping.DeviceEventId, out var eventId))
                    {
                        resolver.Delete(ContentUris.WithAppendedId(CalendarContract.Events.ContentUri, eventId), null, null);
                    }

                    await _store.DeleteCalendarMappingAsync(mapping.Key).ConfigureAwait(false);
                }
            }
        }

        private async Task ImportAiaCalendarEditsAsync(
            ContentResolver resolver,
            long calendarId,
            CancellationToken cancellationToken)
        {
            var projection = new[]
            {
                CalendarContract.Events.InterfaceConsts.Id,
                CalendarContract.Events.InterfaceConsts.Title,
                CalendarContract.Events.InterfaceConsts.Description,
                CalendarContract.Events.InterfaceConsts.EventLocation,
                CalendarContract.Events.InterfaceConsts.Dtstart,
                CalendarContract.Events.InterfaceConsts.Dtend,
                CalendarContract.Events.InterfaceConsts.AllDay,
                SyncDataColumn
            };

            using var cursor = resolver.Query(
                CalendarContract.Events.ContentUri,
                projection,
                $"{CalendarContract.Events.InterfaceConsts.CalendarId}=? AND {CalendarContract.Events.InterfaceConsts.Deleted}=0",
                new string[] { calendarId.ToString() },
                null);

            if (cursor == null)
                return;

            var localEvents = await _store.GetCalendarEventsAsync().ConfigureAwait(false);
            var localById = localEvents.ToDictionary(e => e.Id);
            var seenEventIds = new HashSet<string>(StringComparer.Ordinal);

            while (cursor.MoveToNext())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventId = cursor.GetLong(0);
                seenEventIds.Add(eventId.ToString());
                var title = cursor.GetString(1) ?? string.Empty;
                var description = cursor.GetString(2) ?? string.Empty;
                var location = cursor.GetString(3) ?? string.Empty;
                var allDay = cursor.GetInt(6) == 1;
                var start = FromMillis(cursor.GetLong(4), allDay);
                var end = FromMillis(cursor.GetLong(5), allDay);

                if (allDay && end > start)
                    end = end.AddDays(-1).Date.AddHours(23).AddMinutes(59);

                var syncData = cursor.IsNull(7) ? string.Empty : cursor.GetString(7) ?? string.Empty;

                if (syncData.StartsWith("Reminder:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (syncData.StartsWith("CalendarEvent:", StringComparison.OrdinalIgnoreCase))
                    syncData = syncData.Substring("CalendarEvent:".Length);

                if (string.IsNullOrWhiteSpace(syncData))
                {
                    // Event created directly in the AIA calendar on the phone.
                    var created = new CalendarEventDto
                    {
                        Id = Guid.NewGuid(),
                        Title = string.IsNullOrWhiteSpace(title) ? "Untitled event" : title,
                        Description = description,
                        Location = location,
                        Start = start,
                        End = end,
                        IsAllDay = allDay,
                        CreatedDate = DateTime.Now
                    };

                    await _store.SaveCalendarEventAsync(created).ConfigureAwait(false);
                    await _store.SaveCalendarMappingAsync(new CalendarMapRow
                    {
                        Key = $"CalendarEvent:{created.Id:N}",
                        Kind = "event",
                        CalendarId = calendarId.ToString(),
                        DeviceEventId = eventId.ToString()
                    }).ConfigureAwait(false);

                    var values = new ContentValues();
                    values.Put(SyncDataColumn, $"CalendarEvent:{created.Id:N}");
                    resolver.Update(ContentUris.WithAppendedId(CalendarContract.Events.ContentUri, eventId), values, null, null);
                    continue;
                }

                if (!Guid.TryParseExact(syncData, "N", out var localId) || !localById.TryGetValue(localId, out var localEvent))
                    continue;

                if (!EventDiffers(localEvent, title, description, location, start, end, allDay))
                    continue;

                localEvent.Title = string.IsNullOrWhiteSpace(title) ? localEvent.Title : title;
                localEvent.Description = description;
                localEvent.Location = location;
                localEvent.Start = start;
                localEvent.End = end;
                localEvent.IsAllDay = allDay;
                await _store.SaveCalendarEventAsync(localEvent).ConfigureAwait(false);
            }

            // A device-calendar delete should remove the AIA event too.
            var eventMappings = await _store.GetCalendarMappingsAsync("event").ConfigureAwait(false);
            foreach (var mapping in eventMappings)
            {
                if (seenEventIds.Contains(mapping.DeviceEventId))
                    continue;

                var idText = mapping.Key.StartsWith("CalendarEvent:", StringComparison.OrdinalIgnoreCase)
                    ? mapping.Key.Substring("CalendarEvent:".Length)
                    : string.Empty;

                if (Guid.TryParseExact(idText, "N", out var deletedId))
                    await _store.DeleteAsync(SyncEntityType.CalendarEvent, deletedId).ConfigureAwait(false);

                await _store.DeleteCalendarMappingAsync(mapping.Key).ConfigureAwait(false);
            }
        }

        private static bool EventDiffers(
            CalendarEventDto local,
            string title,
            string description,
            string location,
            DateTime start,
            DateTime end,
            bool allDay)
        {
            return !string.Equals(local.Title, title, StringComparison.Ordinal) ||
                   !string.Equals(local.Description, description, StringComparison.Ordinal) ||
                   !string.Equals(local.Location, location, StringComparison.Ordinal) ||
                   local.IsAllDay != allDay ||
                   Math.Abs((local.Start - start).TotalMinutes) >= 1 ||
                   Math.Abs((local.End - end).TotalMinutes) >= 1;
        }

        private static long ToLocalMillis(DateTime local)
        {
            var localOffset = DateTime.SpecifyKind(local, DateTimeKind.Local);
            return new DateTimeOffset(localOffset).ToUnixTimeMilliseconds();
        }

        private static long ToUtcMidnightMillis(DateTime date)
        {
            return new DateTimeOffset(date.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
        }

        private static DateTime FromMillis(long millis, bool allDay)
        {
            var utc = DateTimeOffset.FromUnixTimeMilliseconds(millis);
            return allDay ? utc.UtcDateTime.Date : utc.ToLocalTime().DateTime;
        }
    }
}
