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

namespace AIA.Plugins.GoogleChat
{
    #region Data Transfer Objects

    /// <summary>
    /// A Google Chat space (named room, group chat or direct message).
    /// </summary>
    public class GoogleChatSpaceData
    {
        /// <summary>
        /// Resource name of the space, for example <c>spaces/AAAA</c>.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Human readable space name, or "Direct message" when Google omits it.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Space type: <c>SPACE</c>, <c>GROUP_CHAT</c> or <c>DIRECT_MESSAGE</c>.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Web link that opens the space in Google Chat.
        /// </summary>
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// A message in a Google Chat space.
    /// </summary>
    public class GoogleChatMessageData
    {
        /// <summary>
        /// Resource name of the message, for example <c>spaces/AAAA/messages/BBBB</c>.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Resource name of the containing space.
        /// </summary>
        public string SpaceName { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the message sender.
        /// </summary>
        public string SenderName { get; set; } = string.Empty;

        /// <summary>
        /// Plain text of the message.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Time the message was created, in local time.
        /// </summary>
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// Web link that opens the containing space in Google Chat.
        /// </summary>
        public string Url { get; set; } = string.Empty;
    }

    #endregion

    /// <summary>
    /// Minimal Google Chat REST API client that consumes OAuth access tokens from an
    /// <see cref="IOAuthTokenProvider"/>.
    /// </summary>
    /// <remarks>
    /// Google Chat API requires a Google Workspace account and the Chat API enabled in the Google
    /// Cloud project that issued the OAuth client. Personal @gmail.com accounts cannot use it.
    /// </remarks>
    public class GoogleChatService
    {
        private const string ApiBase = "https://chat.googleapis.com/v1";
        private const string DirectMessageFallbackName = "Direct message";
        private const int MaxPageSize = 1000;

        /// <summary>
        /// Scopes that a token must cover for the service to read spaces and messages and to send messages.
        /// </summary>
        public static IReadOnlyList<string> RequiredScopes { get; } = new[]
        {
            "https://www.googleapis.com/auth/chat.spaces.readonly",
            "https://www.googleapis.com/auth/chat.messages.readonly",
            "https://www.googleapis.com/auth/chat.messages.create"
        };

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private IOAuthTokenProvider? _tokenProvider;

        /// <summary>
        /// Creates a service that uses the plugin context to create HTTP clients.
        /// </summary>
        /// <param name="context">Plugin context that provides the HTTP client factory.</param>
        public GoogleChatService(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("GoogleChat");

        /// <summary>
        /// Gets a value indicating whether an OAuth token provider has been configured.
        /// </summary>
        public bool IsConfigured => _tokenProvider != null;

        /// <summary>
        /// Sets the OAuth token provider used to obtain Google access tokens, or <see langword="null"/> to disconnect.
        /// </summary>
        /// <param name="provider">Token provider, typically backed by the Google Account plugin.</param>
        public void Configure(IOAuthTokenProvider? provider)
        {
            _tokenProvider = provider;
        }

        /// <summary>
        /// Lists Google Chat spaces visible to the signed-in account, ordered as Google returns them.
        /// </summary>
        /// <param name="limit">Maximum number of spaces to request; clamped to 1..1000.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The spaces, or an empty list when Google returns none.</returns>
        public async Task<List<GoogleChatSpaceData>> GetSpacesAsync(int limit, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, MaxPageSize);

            using var doc = await SendAsync(HttpMethod.Get, $"/spaces?pageSize={limit}", null, cancellationToken)
                .ConfigureAwait(false);

            var spaces = new List<GoogleChatSpaceData>();
            if (TryGetArray(doc.RootElement, "spaces", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    spaces.Add(ParseSpace(item));
                }
            }

            return spaces;
        }

        /// <summary>
        /// Lists recent messages in a space, newest first.
        /// </summary>
        /// <param name="spaceName">Space resource name such as <c>spaces/AAAA</c>, or a bare space id.</param>
        /// <param name="limit">Maximum number of messages to request; clamped to 1..1000.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The messages, or an empty list when the space has none.</returns>
        public async Task<List<GoogleChatMessageData>> GetMessagesAsync(
            string spaceName,
            int limit,
            CancellationToken cancellationToken = default)
        {
            var normalizedSpace = GoogleChatUrl.NormalizeSpaceName(spaceName);
            limit = Math.Clamp(limit, 1, MaxPageSize);

            using var doc = await SendAsync(
                    HttpMethod.Get,
                    $"/{normalizedSpace}/messages?pageSize={limit}&orderBy=createTime%20desc",
                    null,
                    cancellationToken)
                .ConfigureAwait(false);

            var messages = new List<GoogleChatMessageData>();
            if (TryGetArray(doc.RootElement, "messages", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    messages.Add(ParseMessage(item, normalizedSpace));
                }
            }

            return messages;
        }

        /// <summary>
        /// Sends a text message to a space.
        /// </summary>
        /// <param name="spaceName">Space resource name such as <c>spaces/AAAA</c>, or a bare space id.</param>
        /// <param name="text">Message text.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The message as created by Google Chat.</returns>
        public async Task<GoogleChatMessageData> SendMessageAsync(
            string spaceName,
            string text,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);

            var normalizedSpace = GoogleChatUrl.NormalizeSpaceName(spaceName);
            var payload = new { text };

            using var doc = await SendAsync(
                    HttpMethod.Post,
                    $"/{normalizedSpace}/messages",
                    payload,
                    cancellationToken)
                .ConfigureAwait(false);

            return ParseMessage(doc.RootElement, normalizedSpace);
        }

        private async Task<JsonDocument> SendAsync(
            HttpMethod method,
            string path,
            object? body,
            CancellationToken cancellationToken)
        {
            var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            var retried = false;

            while (true)
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
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!retried && IsTransient(response.StatusCode))
                {
                    retried = true;
                    await Task.Delay(GetRetryDelay(response), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new GoogleChatApiException(BuildErrorMessage(response, responseText));
                }

                return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseText) ? "{}" : responseText);
            }
        }

        private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            var provider = _tokenProvider;
            if (provider == null)
            {
                throw new GoogleChatApiException(
                    "Google Chat is not connected. Configure an OAuth token provider (for example the Google Account plugin) and sign in before using Google Chat.");
            }

            string? token;
            try
            {
                token = await provider.GetAccessTokenAsync(RequiredScopes, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GoogleChatApiException(
                    "Could not obtain a Google access token for Google Chat. Sign in again with the Google Account plugin. " + ex.Message,
                    ex);
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new GoogleChatApiException(
                    "Google did not return an access token for Google Chat. Sign in to the Google Account plugin and grant these Chat scopes: "
                    + string.Join(", ", RequiredScopes) + ".");
            }

            return token;
        }

        private static bool IsTransient(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return code == 429 || code >= 500;
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return CapRetryDelay(delta);
            }

            if (retryAfter?.Date is { } date)
            {
                var delay = date - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    return CapRetryDelay(delay);
                }
            }

            return TimeSpan.FromSeconds(1);
        }

        private static TimeSpan CapRetryDelay(TimeSpan delay)
            => delay > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : delay;

        private static string BuildErrorMessage(HttpResponseMessage response, string body)
        {
            var reason = GetGoogleErrorMessage(body);
            var code = (int)response.StatusCode;

            return code switch
            {
                401 => WithReason(
                    "Google rejected the Google Chat access token (401 Unauthorized). Sign in again and grant the Chat scopes.",
                    reason),
                403 => WithReason(
                    "Google denied access to Google Chat (403 Forbidden). Confirm the account is a Google Workspace account, the Chat API is enabled, and the Chat read/write scopes were granted.",
                    reason),
                404 => WithReason(
                    "Google Chat resource not found (404). The space may no longer exist or the account cannot access it.",
                    reason),
                429 => WithReason(
                    "Google Chat rate limit reached (429). Try again shortly.",
                    reason),
                _ => WithReason(
                    $"Google Chat request failed ({code} {response.ReasonPhrase}).",
                    reason)
            };
        }

        private static string WithReason(string message, string? reason)
            => string.IsNullOrWhiteSpace(reason) ? message : $"{message} Google said: {reason}";

        private static string? GetGoogleErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("error", out var error) ||
                    error.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var message = GetString(error, "message");
                var status = GetString(error, "status");

                if (!string.IsNullOrWhiteSpace(message) && !string.IsNullOrWhiteSpace(status))
                {
                    return $"{message} ({status})";
                }

                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }

                return string.IsNullOrWhiteSpace(status) ? null : status;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static GoogleChatSpaceData ParseSpace(JsonElement item)
        {
            var name = GetString(item, "name");
            var displayName = GetString(item, "displayName");
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = DirectMessageFallbackName;
            }

            var type = GetString(item, "spaceType");
            if (string.IsNullOrWhiteSpace(type))
            {
                type = GetString(item, "type");
            }

            return new GoogleChatSpaceData
            {
                Name = name,
                DisplayName = displayName,
                Type = type,
                Url = GoogleChatUrl.ForSpace(name)
            };
        }

        private static GoogleChatMessageData ParseMessage(JsonElement item, string spaceName)
        {
            var senderName = "Unknown";
            if (item.TryGetProperty("sender", out var sender) && sender.ValueKind == JsonValueKind.Object)
            {
                var displayName = GetString(sender, "displayName");
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    senderName = displayName;
                }
            }

            return new GoogleChatMessageData
            {
                Name = GetString(item, "name"),
                SpaceName = spaceName,
                SenderName = senderName,
                Text = GetString(item, "text"),
                CreateTime = ParseTimestamp(GetString(item, "createTime")),
                Url = GoogleChatUrl.ForSpace(spaceName)
            };
        }

        private static bool TryGetArray(JsonElement root, string propertyName, out JsonElement array)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
                return true;
            }

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.Array)
            {
                array = value;
                return true;
            }

            array = default;
            return false;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static DateTime ParseTimestamp(string value)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc).ToLocalTime()
                    : parsed.ToLocalTime();
            }

            return DateTime.Now;
        }
    }

    /// <summary>
    /// Builds Google Chat resource names and web links.
    /// </summary>
    internal static class GoogleChatUrl
    {
        private const string SpacePrefix = "spaces/";

        /// <summary>
        /// Normalizes a space resource name, accepting a bare space id.
        /// </summary>
        internal static string NormalizeSpaceName(string spaceName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(spaceName);

            var value = spaceName.Trim().TrimStart('/');
            return value.StartsWith(SpacePrefix, StringComparison.OrdinalIgnoreCase)
                ? value
                : SpacePrefix + value;
        }

        /// <summary>
        /// Gets the bare space id from a space resource name.
        /// </summary>
        internal static string GetSpaceId(string? spaceName)
        {
            if (string.IsNullOrWhiteSpace(spaceName))
            {
                return string.Empty;
            }

            var value = spaceName.Trim().TrimStart('/');
            if (value.StartsWith(SpacePrefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(SpacePrefix.Length);
            }

            var memberIndex = value.IndexOf("/members/", StringComparison.OrdinalIgnoreCase);
            if (memberIndex >= 0)
            {
                value = value.Substring(0, memberIndex);
            }

            return value;
        }

        /// <summary>
        /// Gets the Google Chat web link for a space resource name.
        /// </summary>
        internal static string ForSpace(string? spaceName)
        {
            var id = GetSpaceId(spaceName);
            return string.IsNullOrEmpty(id)
                ? string.Empty
                : $"https://mail.google.com/chat/u/0/#chat/space/{id}";
        }

        /// <summary>
        /// Gets the Google Chat web link for a space or message resource name.
        /// </summary>
        internal static string ForSpaceOrMessage(string? resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
            {
                return string.Empty;
            }

            var value = resourceName.Trim().TrimStart('/');
            if (!value.StartsWith(SpacePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var rest = value.Substring(SpacePrefix.Length);
            var messageIndex = rest.IndexOf("/messages/", StringComparison.OrdinalIgnoreCase);
            if (messageIndex >= 0)
            {
                rest = rest.Substring(0, messageIndex);
            }

            return ForSpace(SpacePrefix + rest);
        }
    }

    /// <summary>
    /// Exception thrown when a Google Chat request fails or the account is not connected.
    /// </summary>
    public class GoogleChatApiException : Exception
    {
        /// <summary>
        /// Creates an exception with a user-facing message.
        /// </summary>
        public GoogleChatApiException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates an exception with a user-facing message and an inner exception.
        /// </summary>
        public GoogleChatApiException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
