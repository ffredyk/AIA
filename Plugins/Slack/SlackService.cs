using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Slack
{
    #region Data Transfer Objects

    public class SlackUserData
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string TeamId { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
    }

    public class SlackChannelData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsPrivate { get; set; }
        public bool IsMember { get; set; }
        public string Topic { get; set; } = string.Empty;
        public int MemberCount { get; set; }

        public string DisplayName => IsPrivate ? $"🔒 {Name}" : $"# {Name}";
        public string Subtitle => string.IsNullOrEmpty(Topic)
            ? $"{(IsPrivate ? "Private channel" : "Public channel")} · {MemberCount} member(s)"
            : Topic;
    }

    public class SlackMessageData
    {
        public string Id { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool IsBot { get; set; }
    }

    #endregion

    /// <summary>
    /// Minimal Slack Web API client using a bot token.
    /// </summary>
    public class SlackService
    {
        private const string ApiBase = "https://slack.com/api";

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private string _token = string.Empty;

        public SlackService(IPluginContext context)
        {
            _context = context;
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("Slack");

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_token);

        public void Configure(string? token)
        {
            _token = token?.Trim() ?? string.Empty;
        }

        public async Task<SlackUserData> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/auth.test", null, cancellationToken);
            var root = doc.RootElement;

            return new SlackUserData
            {
                UserId = GetString(root, "user_id"),
                UserName = GetString(root, "user"),
                TeamId = GetString(root, "team_id"),
                TeamName = GetString(root, "team")
            };
        }

        public async Task<List<SlackChannelData>> GetChannelsAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(
                HttpMethod.Get,
                "/conversations.list?types=public_channel,private_channel&exclude_archived=true&limit=200",
                null,
                cancellationToken);

            var channels = new List<SlackChannelData>();

            if (!doc.RootElement.TryGetProperty("channels", out var items) || items.ValueKind != JsonValueKind.Array)
                return channels;

            foreach (var item in items.EnumerateArray())
            {
                var topic = string.Empty;
                if (item.TryGetProperty("topic", out var topicElement) && topicElement.ValueKind == JsonValueKind.Object)
                    topic = GetString(topicElement, "value");

                channels.Add(new SlackChannelData
                {
                    Id = GetString(item, "id"),
                    Name = GetString(item, "name"),
                    IsPrivate = item.TryGetProperty("is_private", out var isPrivate) && isPrivate.ValueKind == JsonValueKind.True,
                    IsMember = item.TryGetProperty("is_member", out var isMember) && isMember.ValueKind == JsonValueKind.True,
                    Topic = topic,
                    MemberCount = GetInt(item, "num_members")
                });
            }

            channels.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return channels;
        }

        public async Task<Dictionary<string, string>> GetUserNamesAsync(CancellationToken cancellationToken = default)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using var doc = await SendAsync(HttpMethod.Get, "/users.list?limit=200", null, cancellationToken);

            if (!doc.RootElement.TryGetProperty("members", out var members) || members.ValueKind != JsonValueKind.Array)
                return names;

            foreach (var member in members.EnumerateArray())
            {
                var id = GetString(member, "id");
                if (string.IsNullOrEmpty(id))
                    continue;

                var name = string.Empty;
                if (member.TryGetProperty("profile", out var profile) && profile.ValueKind == JsonValueKind.Object)
                {
                    name = GetString(profile, "display_name");
                    if (string.IsNullOrWhiteSpace(name))
                        name = GetString(profile, "real_name");
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = GetString(member, "name");

                names[id] = string.IsNullOrWhiteSpace(name) ? id : name;
            }

            return names;
        }

        public async Task<List<SlackMessageData>> GetMessagesAsync(
            string channelId,
            int limit,
            IReadOnlyDictionary<string, string> userNames,
            CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 100);
            using var doc = await SendAsync(
                HttpMethod.Get,
                $"/conversations.history?channel={Uri.EscapeDataString(channelId)}&limit={limit}",
                null,
                cancellationToken);

            var messages = new List<SlackMessageData>();

            if (!doc.RootElement.TryGetProperty("messages", out var items) || items.ValueKind != JsonValueKind.Array)
                return messages;

            foreach (var item in items.EnumerateArray())
            {
                var userId = GetString(item, "user");
                var isBot = !string.IsNullOrEmpty(GetString(item, "bot_id"));
                var authorName = GetString(item, "username");

                if (string.IsNullOrWhiteSpace(authorName) && !string.IsNullOrWhiteSpace(userId) && userNames.TryGetValue(userId, out var name))
                    authorName = name;

                if (string.IsNullOrWhiteSpace(authorName))
                    authorName = string.IsNullOrEmpty(userId) ? "Slack" : userId;

                messages.Add(new SlackMessageData
                {
                    Id = GetString(item, "ts"),
                    AuthorName = isBot ? $"{authorName} [bot]" : authorName,
                    Text = GetString(item, "text"),
                    Timestamp = ParseSlackTimestamp(GetString(item, "ts")),
                    IsBot = isBot
                });
            }

            // Slack returns newest first.
            messages.Reverse();
            return messages;
        }

        public async Task<SlackMessageData> SendMessageAsync(
            string channelId,
            string text,
            IReadOnlyDictionary<string, string> userNames,
            CancellationToken cancellationToken = default)
        {
            var payload = new { channel = channelId, text };
            using var doc = await SendAsync(HttpMethod.Post, "/chat.postMessage", payload, cancellationToken);

            var authorName = "AIA";
            var userId = GetString(doc.RootElement, "message", "user");
            if (!string.IsNullOrEmpty(userId) && userNames.TryGetValue(userId, out var name))
                authorName = name;

            return new SlackMessageData
            {
                Id = GetString(doc.RootElement, "ts"),
                AuthorName = $"{authorName} [bot]",
                Text = text,
                Timestamp = DateTime.Now,
                IsBot = true
            };
        }

        private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
                throw new SlackApiException("Slack bot token is not configured.");

            using var request = new HttpRequestMessage(method, ApiBase + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            if (body != null)
            {
                var json = JsonSerializer.Serialize(body);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new SlackApiException($"Slack request failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
            }

            var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);

            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                var error = GetString(doc.RootElement, "error");
                doc.Dispose();
                throw new SlackApiException(DescribeError(error));
            }

            return doc;
        }

        private static string DescribeError(string error)
        {
            return error switch
            {
                "invalid_auth" => "Slack rejected the bot token (invalid_auth).",
                "not_authed" => "Slack bot token is missing or empty.",
                "account_inactive" => "The Slack bot account is inactive.",
                "missing_scope" => "The Slack bot token is missing a required scope. Needed: channels:read, channels:history, chat:write, users:read.",
                "not_in_channel" => "The bot is not a member of this channel. Invite the bot with /invite @YourBot.",
                "channel_not_found" => "Slack channel not found.",
                "is_archived" => "The Slack channel is archived.",
                "ratelimited" => "Slack rate limit reached. Try again shortly.",
                _ => string.IsNullOrWhiteSpace(error) ? "Slack request failed." : $"Slack error: {error}"
            };
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static string GetString(JsonElement element, string outerProperty, string innerProperty)
        {
            return element.TryGetProperty(outerProperty, out var outer) && outer.ValueKind == JsonValueKind.Object
                ? GetString(outer, innerProperty)
                : string.Empty;
        }

        private static int GetInt(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : 0;
        }

        private static DateTime ParseSlackTimestamp(string value)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)).ToLocalTime().DateTime;
                }
                catch
                {
                    // Fall through.
                }
            }

            return DateTime.Now;
        }
    }

    public class SlackApiException : Exception
    {
        public SlackApiException(string message) : base(message)
        {
        }
    }
}
