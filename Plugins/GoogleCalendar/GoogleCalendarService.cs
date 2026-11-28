using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.GoogleCalendar
{
    #region Data Transfer Objects

    public class GoogleCalendarEventData
    {
        public string Id { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }
        public string HtmlLink { get; set; } = string.Empty;
        public bool IsAllDay { get; set; }
        public string Organizer { get; set; } = string.Empty;
    }

    #endregion

    /// <summary>
    /// Minimal Google Calendar REST API v3 client using OAuth access tokens.
    /// </summary>
    public class GoogleCalendarService
    {
        private const string ApiBase = "https://www.googleapis.com/calendar/v3";
        private const string CalendarEventsPath = "/calendars/primary/events";

        private static readonly string[] RequiredScopeList =
        {
            "https://www.googleapis.com/auth/calendar.readonly",
            "https://www.googleapis.com/auth/calendar.events"
        };

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private IOAuthTokenProvider? _tokenProvider;

        public GoogleCalendarService(IPluginContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Scopes that a token must cover to use this service.
        /// </summary>
        public static IReadOnlyList<string> RequiredScopes => RequiredScopeList;

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("GoogleCalendar");

        public bool IsConfigured => _tokenProvider != null;

        public void Configure(IOAuthTokenProvider? provider)
        {
            _tokenProvider = provider;
        }

        public async Task<List<GoogleCalendarEventData>> GetUpcomingEventsAsync(int limit, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 2500);
            var timeMin = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            var path = $"{CalendarEventsPath}?timeMin={timeMin}&maxResults={limit}&singleEvents=true&orderBy=startTime";

            using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            var events = new List<GoogleCalendarEventData>();

            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    events.Add(ParseEvent(item));
                }
            }

            return events;
        }

        public async Task<(DateTime? startUtc, string htmlLink)> GetEventSyncStateAsync(string eventId, CancellationToken cancellationToken = default)
        {
            var path = $"{CalendarEventsPath}/{Uri.EscapeDataString(eventId)}";
            using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            var root = doc.RootElement;

            return (ReadStart(root), GetString(root, "htmlLink"));
        }

        public async Task SetEventStartAsync(string eventId, DateTime? startUtc, CancellationToken cancellationToken = default)
        {
            if (startUtc is null)
                throw new NotSupportedException("A Google Calendar event cannot have its start cleared. Move the event instead.");

            var path = $"{CalendarEventsPath}/{Uri.EscapeDataString(eventId)}";
            using var current = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            var root = current.RootElement;

            var duration = TimeSpan.FromMinutes(30);
            var existingStart = ReadStart(root);
            var existingEnd = ReadEnd(root);
            if (existingStart.HasValue && existingEnd.HasValue)
            {
                var existingDuration = existingEnd.Value - existingStart.Value;
                if (existingDuration > TimeSpan.Zero)
                    duration = existingDuration;
            }

            var newStart = DateTime.SpecifyKind(startUtc.Value, DateTimeKind.Utc);
            var newEnd = newStart + duration;
            var payload = new
            {
                start = new { dateTime = FormatUtc(newStart) },
                end = new { dateTime = FormatUtc(newEnd) }
            };

            using var _ = await SendAsync(HttpMethod.Patch, path, payload, cancellationToken);
        }

        private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            var provider = _tokenProvider;
            if (provider == null)
            {
                throw new InvalidOperationException(
                    "Google Calendar is not connected. Configure an OAuth token provider that grants the " +
                    "'https://www.googleapis.com/auth/calendar.readonly' and 'https://www.googleapis.com/auth/calendar.events' scopes.");
            }

            var token = await provider.GetAccessTokenAsync(RequiredScopeList, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException(
                    "Google Calendar could not obtain an access token. Sign in again and grant the " +
                    "'https://www.googleapis.com/auth/calendar.readonly' and 'https://www.googleapis.com/auth/calendar.events' scopes.");
            }

            return token;
        }

        private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);

            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, ApiBase + path);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");

                if (body != null)
                {
                    var json = JsonSerializer.Serialize(body);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == 0 && IsTransient(response.StatusCode))
                    {
                        await Task.Delay(GetRetryDelay(response), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new GoogleCalendarApiException(BuildErrorMessage(response, text), response.StatusCode);
                }

                return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            }
        }

        private static bool IsTransient(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter != null)
            {
                TimeSpan delay;
                if (retryAfter.Delta.HasValue)
                    delay = retryAfter.Delta.Value;
                else if (retryAfter.Date.HasValue)
                    delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                else
                    delay = TimeSpan.FromSeconds(1);

                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.Zero;

                return delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;
            }

            return TimeSpan.FromSeconds(1);
        }

        private static string BuildErrorMessage(HttpResponseMessage response, string body)
        {
            var reason = GetGoogleErrorMessage(body);
            var suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Google said: {reason}";

            return (int)response.StatusCode switch
            {
                401 => "Google Calendar rejected the access token (401 Unauthorized). Sign in again and grant the calendar.readonly and calendar.events scopes." + suffix,
                403 => "Google Calendar denied access (403 Forbidden). Check that the calendar.readonly and calendar.events scopes are granted and that the calendar is accessible." + suffix,
                404 => "Google Calendar item not found (404). The event may have been deleted or is not in the primary calendar." + suffix,
                429 => "Google Calendar rate limit reached (429 Too Many Requests). Try again shortly." + suffix,
                _ when (int)response.StatusCode >= 500 => $"Google Calendar is temporarily unavailable ({(int)response.StatusCode} {response.ReasonPhrase}). Try again later." + suffix,
                _ => $"Google Calendar request failed ({(int)response.StatusCode} {response.ReasonPhrase})." + suffix
            };
        }

        private static string? GetGoogleErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    var message = GetString(error, "message");
                    if (!string.IsNullOrWhiteSpace(message))
                        return message;
                }
            }
            catch
            {
                // Fall through to the generic message.
            }

            return null;
        }

        private static GoogleCalendarEventData ParseEvent(JsonElement item)
        {
            var isAllDay = false;
            var start = ReadStart(item, ref isAllDay);

            return new GoogleCalendarEventData
            {
                Id = GetString(item, "id"),
                Summary = GetString(item, "summary"),
                Description = GetString(item, "description"),
                Location = GetString(item, "location"),
                Start = start,
                End = ReadEnd(item),
                HtmlLink = GetString(item, "htmlLink"),
                IsAllDay = isAllDay,
                Organizer = ReadOrganizer(item)
            };
        }

        private static DateTime? ReadStart(JsonElement item)
        {
            var ignored = false;
            return ReadStart(item, ref ignored);
        }

        private static DateTime? ReadStart(JsonElement item, ref bool isAllDay)
        {
            if (!item.TryGetProperty("start", out var start) || start.ValueKind != JsonValueKind.Object)
                return null;

            var dateTime = GetString(start, "dateTime");
            if (!string.IsNullOrWhiteSpace(dateTime))
                return ParseDateTime(dateTime);

            var date = GetString(start, "date");
            if (!string.IsNullOrWhiteSpace(date))
            {
                isAllDay = true;
                return ParseDate(date);
            }

            return null;
        }

        private static DateTime? ReadEnd(JsonElement item)
        {
            if (!item.TryGetProperty("end", out var end) || end.ValueKind != JsonValueKind.Object)
                return null;

            var dateTime = GetString(end, "dateTime");
            if (!string.IsNullOrWhiteSpace(dateTime))
                return ParseDateTime(dateTime);

            var date = GetString(end, "date");
            return string.IsNullOrWhiteSpace(date) ? null : ParseDate(date);
        }

        private static string ReadOrganizer(JsonElement item)
        {
            return item.TryGetProperty("organizer", out var organizer) && organizer.ValueKind == JsonValueKind.Object
                ? GetString(organizer, "displayName")
                : string.Empty;
        }

        private static DateTime? ParseDateTime(string value)
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.UtcDateTime
                : null;
        }

        private static DateTime? ParseDate(string value)
        {
            // All-day events use a date without a zone; anchor it to local midnight.
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return DateTime.SpecifyKind(parsed, DateTimeKind.Local).ToUniversalTime();

            return null;
        }

        private static string FormatUtc(DateTime value)
        {
            return value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
    }

    public class GoogleCalendarApiException : Exception
    {
        public GoogleCalendarApiException(string message) : base(message)
        {
        }

        public GoogleCalendarApiException(string message, HttpStatusCode statusCode) : base(message)
        {
            StatusCode = statusCode;
        }

        public HttpStatusCode? StatusCode { get; }
    }
}
