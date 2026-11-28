using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AIA.Models;

namespace AIA.Services
{
    /// <summary>
    /// Persists calendar events to userdata/calendar-events.json.
    /// </summary>
    public static class CalendarEventService
    {
        private static string DataFolder => AppPaths.UserDataFolder;
        private static string EventsFile => Path.Combine(DataFolder, "calendar-events.json");

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(DataFolder))
                Directory.CreateDirectory(DataFolder);
        }

        public static async Task<List<CalendarEventItem>> LoadEventsAsync()
        {
            EnsureDirectoryExists();

            if (!File.Exists(EventsFile))
                return new List<CalendarEventItem>();

            try
            {
                var dtos = await JsonFileStore.ReadAsync<List<CalendarEventDto>>(EventsFile, GetJsonOptions());
                return dtos == null ? new List<CalendarEventItem>() : dtos.Select(FromDto).ToList();
            }
            catch (Exception ex)
            {
                throw new DataLoadException(EventsFile, JsonFileStore.Quarantine(EventsFile), ex);
            }
        }

        public static async Task SaveEventsAsync(IEnumerable<CalendarEventItem> events)
        {
            EnsureDirectoryExists();
            var dtos = events.Select(ToDto).ToList();
            await JsonFileStore.WriteAsync(EventsFile, dtos, GetJsonOptions()).ConfigureAwait(false);
        }

        public static bool HasStoredData => File.Exists(EventsFile);

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        private static CalendarEventItem FromDto(CalendarEventDto dto)
        {
            return new CalendarEventItem
            {
                Id = dto.Id,
                Title = dto.Title,
                Description = dto.Description,
                Location = dto.Location,
                Start = dto.Start,
                End = dto.End,
                IsAllDay = dto.IsAllDay,
                RecurrenceType = dto.RecurrenceType,
                RecurrenceInterval = dto.RecurrenceInterval,
                RecurrenceEndDate = dto.RecurrenceEndDate,
                ReminderMinutesBefore = dto.ReminderMinutesBefore,
                CreatedDate = dto.CreatedDate
            };
        }

        private static CalendarEventDto ToDto(CalendarEventItem item)
        {
            return new CalendarEventDto
            {
                Id = item.Id,
                Title = item.Title,
                Description = item.Description,
                Location = item.Location,
                Start = item.Start,
                End = item.End,
                IsAllDay = item.IsAllDay,
                RecurrenceType = item.RecurrenceType,
                RecurrenceInterval = item.RecurrenceInterval,
                RecurrenceEndDate = item.RecurrenceEndDate,
                ReminderMinutesBefore = item.ReminderMinutesBefore,
                CreatedDate = item.CreatedDate
            };
        }

        private class CalendarEventDto
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
            public bool IsAllDay { get; set; }
            public RecurrenceType RecurrenceType { get; set; }
            public int RecurrenceInterval { get; set; } = 1;
            public DateTime? RecurrenceEndDate { get; set; }
            public int ReminderMinutesBefore { get; set; } = 15;
            public DateTime CreatedDate { get; set; }
        }
    }
}
