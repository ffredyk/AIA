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

namespace AIA.Plugins.GoogleGmail
{
    #region Data Transfer Objects

    public class GmailMessageData
    {
        public string Id { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; }
        public bool IsUnread { get; set; }
        public string Url { get; set; } = string.Empty;
    }

    #endregion

    /// <summary>
    /// Minimal Gmail REST API client that uses tokens from the Google account plugin.
    /// </summary>
    public class GmailService
    {
        private const string ApiBase = "https://gmail.googleapis.com/gmail/v1";
        private const int MaxResultsLimit = 500;

        /// <summary>
        /// Scopes required to list messages and change their read state.
        /// </summary>
        public static readonly IReadOnlyList<string> RequiredScopes = new[]
        {
            "https://www.googleapis.com/auth/gmail.readonly",
            "https://www.googleapis.com/auth/gmail.modify"
        };

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private IOAuthTokenProvider? _tokenProvider;

        public GmailService(IPluginContext context)
        {
            _context = context;
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("GoogleGmail");

        public bool IsConfigured => _tokenProvider != null;

        public void Configure(IOAuthTokenProvider? provider)
        {
            _tokenProvider = provider;
        }

        public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            if (_tokenProvider == null)
                throw new GmailApiException("Google Account plugin is not available. Enable it and sign in.");

            var token = await _tokenProvider.GetAccessTokenAsync(RequiredScopes, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
                throw new GmailApiException("Google account is not signed in or Gmail scopes are not granted. Open the Google tab and enable Gmail.");

            return token;
        }

        public async Task<List<GmailMessageData>> GetUnreadMessagesAsync(int limit, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, MaxResultsLimit);

            using var listDoc = await SendAsync(
                HttpMethod.Get,
                $"/users/me/messages?q=is:unread&maxResults={limit}",
                null,
                cancellationToken).ConfigureAwait(false);

            var messages = new List<GmailMessageData>();

            if (!listDoc.RootElement.TryGetProperty("messages", out var items) || items.ValueKind != JsonValueKind.Array)
                return messages;

            foreach (var item in items.EnumerateArray())
            {
                var id = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                try
                {
                    messages.Add(await GetMessageAsync(id, cancellationToken).ConfigureAwait(false));
                }
                catch (GmailApiException ex)
                {
                    _context.Logger.Warning($"Gmail message {id} could not be loaded: {ex.Message}");
                }
                catch (HttpRequestException ex)
                {
                    _context.Logger.Warning($"Gmail message {id} could not be loaded: {ex.Message}");
                }
            }

            return messages;
        }

        public async Task MarkReadAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Gmail message id is required.", nameof(id));

            var payload = new { removeLabelIds = new[] { "UNREAD" } };

            using var _ = await SendAsync(
                HttpMethod.Post,
                $"/users/me/messages/{Uri.EscapeDataString(id)}/modify",
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task MarkUnreadAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Gmail message id is required.", nameof(id));

            var payload = new { addLabelIds = new[] { "UNREAD" } };

            using var _ = await SendAsync(
                HttpMethod.Post,
                $"/users/me/messages/{Uri.EscapeDataString(id)}/modify",
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<ExternalSyncState> GetSyncStateAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Gmail message id is required.", nameof(id));

            using var doc = await SendAsync(
                HttpMethod.Get,
                $"/users/me/messages/{Uri.EscapeDataString(id)}?format=minimal",
                null,
                cancellationToken).ConfigureAwait(false);

            var root = doc.RootElement;
            var isUnread = HasLabel(root, "UNREAD");
            var messageId = GetString(root, "id");
            if (string.IsNullOrWhiteSpace(messageId))
                messageId = id;

            return new ExternalSyncState
            {
                SourceId = messageId,
                SourceUrl = BuildMessageUrl(messageId),
                Title = GetString(root, "snippet"),
                IsCompleted = !isUnread,
                DueUtc = null
            };
        }

        private async Task<GmailMessageData> GetMessageAsync(string id, CancellationToken cancellationToken)
        {
            const string fields = "format=metadata&metadataHeaders=Subject&metadataHeaders=From&metadataHeaders=Date";

            using var doc = await SendAsync(
                HttpMethod.Get,
                $"/users/me/messages/{Uri.EscapeDataString(id)}?{fields}",
                null,
                cancellationToken).ConfigureAwait(false);

            var root = doc.RootElement;
            var messageId = GetString(root, "id");
            if (string.IsNullOrWhiteSpace(messageId))
                messageId = id;

            var subject = string.Empty;
            var from = string.Empty;
            var dateHeader = string.Empty;

            if (root.TryGetProperty("payload", out var payload) &&
                payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("headers", out var headers) &&
                headers.ValueKind == JsonValueKind.Array)
            {
                foreach (var header in headers.EnumerateArray())
                {
                    var name = GetString(header, "name");
                    var value = GetString(header, "value");

                    if (name.Equals("Subject", StringComparison.OrdinalIgnoreCase))
                        subject = value;
                    else if (name.Equals("From", StringComparison.OrdinalIgnoreCase))
                        from = value;
                    else if (name.Equals("Date", StringComparison.OrdinalIgnoreCase))
                        dateHeader = value;
                }
            }

            return new GmailMessageData
            {
                Id = messageId,
                ThreadId = GetString(root, "threadId"),
                Subject = subject,
                From = from,
                Snippet = GetString(root, "snippet"),
                ReceivedAt = ParseReceivedAt(root, dateHeader),
                IsUnread = HasLabel(root, "UNREAD"),
                Url = BuildMessageUrl(messageId)
            };
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

                if (response.IsSuccessStatusCode)
                    return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);

                if (attempt == 0 && ShouldRetry(response.StatusCode))
                {
                    await Task.Delay(GetRetryDelay(response), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new GmailApiException(BuildErrorMessage(response, text));
            }
        }

        private static bool ShouldRetry(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;

            if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
                return delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;

            if (retryAfter?.Date is DateTimeOffset date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                    return wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait;
            }

            return TimeSpan.FromSeconds(1);
        }

        private static string BuildErrorMessage(HttpResponseMessage response, string body)
        {
            var (message, status) = GetGoogleError(body);

            return (int)response.StatusCode switch
            {
                401 => "Gmail access token was rejected (401 Unauthorized). Open the Google tab and sign in again.",
                403 => message ?? "Gmail denied access (403 Forbidden). Make sure the Google account granted Gmail scopes.",
                404 => message ?? "Gmail message not found (404).",
                429 => message ?? "Gmail rate limit reached (429). Try again shortly.",
                _ => message ?? $"Gmail request failed ({(int)response.StatusCode} {response.ReasonPhrase}){(string.IsNullOrWhiteSpace(status) ? string.Empty : $": {status}")}."
            };
        }

        private static (string? Message, string? Status) GetGoogleError(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return (null, null);

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    var message = error.TryGetProperty("message", out var messageValue) && messageValue.ValueKind == JsonValueKind.String
                        ? messageValue.GetString()
                        : null;
                    var status = error.TryGetProperty("status", out var statusValue) && statusValue.ValueKind == JsonValueKind.String
                        ? statusValue.GetString()
                        : null;

                    return (message, status);
                }
            }
            catch (JsonException)
            {
                // Fall through to the generic message.
            }

            return (null, null);
        }

        private static bool HasLabel(JsonElement element, string label)
        {
            if (!element.TryGetProperty("labelIds", out var labels) || labels.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var item in labels.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String &&
                    string.Equals(item.GetString(), label, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static DateTime ParseReceivedAt(JsonElement element, string dateHeader)
        {
            var internalDate = GetString(element, "internalDate");
            if (long.TryParse(internalDate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Fall through to the Date header.
                }
            }

            if (DateTimeOffset.TryParse(dateHeader, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed.LocalDateTime;

            return DateTime.Now;
        }

        private static string BuildMessageUrl(string id)
        {
            return $"https://mail.google.com/mail/u/0/#inbox/{id}";
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
    }

    public class GmailApiException : Exception
    {
        public GmailApiException(string message) : base(message)
        {
        }
    }
}
