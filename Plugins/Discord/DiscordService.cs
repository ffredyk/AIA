using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Discord
{
    #region Data Transfer Objects

    public class DiscordUserData
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string GlobalName { get; set; } = string.Empty;

        public string DisplayName => string.IsNullOrWhiteSpace(GlobalName) ? Username : GlobalName;
    }

    public class DiscordGuildData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class DiscordChannelData
    {
        public string Id { get; set; } = string.Empty;
        public string GuildId { get; set; } = string.Empty;
        public string GuildName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Type { get; set; }
        public int Position { get; set; }
        public string Topic { get; set; } = string.Empty;

        public string DisplayName => $"# {Name}";
        public string Subtitle => string.IsNullOrEmpty(Topic) ? GuildName : $"{GuildName} · {Topic}";
    }

    public class DiscordMessageData
    {
        public string Id { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool IsBot { get; set; }
        public bool HasAttachments { get; set; }
    }

    #endregion

    /// <summary>
    /// Minimal Discord REST API client using a bot token.
    /// </summary>
    public class DiscordService
    {
        private const string ApiBase = "https://discord.com/api/v10";
        private const int GuildTextChannel = 0;
        private const int GuildAnnouncementChannel = 5;

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private string _token = string.Empty;

        public DiscordService(IPluginContext context)
        {
            _context = context;
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("Discord");

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_token);

        public void Configure(string? token)
        {
            _token = token?.Trim() ?? string.Empty;
        }

        public async Task<DiscordUserData> GetCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/users/@me", null, cancellationToken);
            var root = doc.RootElement;

            return new DiscordUserData
            {
                Id = GetString(root, "id"),
                Username = GetString(root, "username"),
                GlobalName = GetString(root, "global_name")
            };
        }

        public async Task<List<DiscordGuildData>> GetGuildsAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/users/@me/guilds?limit=200", null, cancellationToken);
            var guilds = new List<DiscordGuildData>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                guilds.Add(new DiscordGuildData
                {
                    Id = GetString(item, "id"),
                    Name = GetString(item, "name")
                });
            }

            guilds.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return guilds;
        }

        public async Task<List<DiscordChannelData>> GetTextChannelsAsync(string guildId, string guildName, CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, $"/guilds/{guildId}/channels", null, cancellationToken);
            var channels = new List<DiscordChannelData>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var type = GetInt(item, "type");
                if (type != GuildTextChannel && type != GuildAnnouncementChannel)
                    continue;

                channels.Add(new DiscordChannelData
                {
                    Id = GetString(item, "id"),
                    GuildId = guildId,
                    GuildName = guildName,
                    Name = GetString(item, "name"),
                    Topic = GetString(item, "topic"),
                    Type = type,
                    Position = GetInt(item, "position")
                });
            }

            channels.Sort((a, b) => a.Position.CompareTo(b.Position));
            return channels;
        }

        public async Task<List<DiscordMessageData>> GetMessagesAsync(string channelId, int limit, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 100);
            using var doc = await SendAsync(HttpMethod.Get, $"/channels/{channelId}/messages?limit={limit}", null, cancellationToken);
            var messages = new List<DiscordMessageData>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                messages.Add(ParseMessage(item, channelId));
            }

            // Discord returns newest first.
            messages.Reverse();
            return messages;
        }

        public async Task<DiscordMessageData> SendMessageAsync(string channelId, string content, CancellationToken cancellationToken = default)
        {
            var payload = new { content };
            using var doc = await SendAsync(HttpMethod.Post, $"/channels/{channelId}/messages", payload, cancellationToken);
            return ParseMessage(doc.RootElement, channelId);
        }

        private static DiscordMessageData ParseMessage(JsonElement item, string channelId)
        {
            var authorName = "Unknown";
            var isBot = false;

            if (item.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object)
            {
                var globalName = GetString(author, "global_name");
                var username = GetString(author, "username");
                authorName = string.IsNullOrWhiteSpace(globalName) ? username : globalName;
                isBot = author.TryGetProperty("bot", out var bot) && bot.ValueKind == JsonValueKind.True;
            }

            var content = GetString(item, "content");
            var hasAttachments = item.TryGetProperty("attachments", out var attachments) &&
                                 attachments.ValueKind == JsonValueKind.Array &&
                                 attachments.GetArrayLength() > 0;

            if (string.IsNullOrWhiteSpace(content) && hasAttachments)
                content = "(attachment)";

            return new DiscordMessageData
            {
                Id = GetString(item, "id"),
                ChannelId = channelId,
                AuthorName = isBot ? $"{authorName} [bot]" : authorName,
                Content = content,
                Timestamp = ParseTimestamp(GetString(item, "timestamp")),
                IsBot = isBot,
                HasAttachments = hasAttachments
            };
        }

        private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
                throw new DiscordApiException("Discord bot token is not configured.");

            using var request = new HttpRequestMessage(method, ApiBase + path);
            request.Headers.TryAddWithoutValidation("Authorization", "Bot " + _token);
            request.Headers.TryAddWithoutValidation("User-Agent", "AIA-Discord-Plugin/1.0");

            if (body != null)
            {
                var json = JsonSerializer.Serialize(body);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new DiscordApiException(BuildErrorMessage(response, text));
            }

            return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }

        private static string BuildErrorMessage(HttpResponseMessage response, string body)
        {
            var reason = GetDiscordErrorMessage(body);

            return (int)response.StatusCode switch
            {
                401 => "Discord rejected the bot token (401 Unauthorized).",
                403 => reason ?? "Discord denied access (403). The bot needs View Channels, Read Message History and Send Messages permissions.",
                404 => reason ?? "Discord resource not found (404).",
                429 => reason ?? "Discord rate limit reached (429). Try again shortly.",
                _ => reason ?? $"Discord request failed ({(int)response.StatusCode} {response.ReasonPhrase})."
            };
        }

        private static string? GetDiscordErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    return message.GetString();
            }
            catch
            {
                // Fall through to the generic message.
            }

            return null;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static int GetInt(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : 0;
        }

        private static DateTime ParseTimestamp(string value)
        {
            return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToLocalTime()
                : DateTime.Now;
        }
    }

    public class DiscordApiException : Exception
    {
        public DiscordApiException(string message) : base(message)
        {
        }
    }
}
