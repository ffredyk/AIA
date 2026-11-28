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

namespace AIA.Plugins.Notion
{
    #region Data Transfer Objects

    public class NotionUserData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Notion integration" : Name;
    }

    public class NotionItemData
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Notion object type, either "page" or "database".
        /// </summary>
        public string ObjectType { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime? LastEditedTime { get; set; }
        public string ParentType { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Maps AIA sync state onto Notion page properties of a database page.
    /// </summary>
    public class NotionSyncPropertyConfig
    {
        public string CompletedPropertyName { get; set; } = string.Empty;

        /// <summary>
        /// Property type of the completed flag, either "checkbox" or "status".
        /// </summary>
        public string CompletedPropertyType { get; set; } = "checkbox";

        public string DuePropertyName { get; set; } = string.Empty;

        public bool UsesStatusProperty =>
            string.Equals(CompletedPropertyType, "status", StringComparison.OrdinalIgnoreCase);

        public bool HasValidCompletedPropertyType =>
            string.IsNullOrWhiteSpace(CompletedPropertyType) ||
            UsesStatusProperty ||
            string.Equals(CompletedPropertyType, "checkbox", StringComparison.OrdinalIgnoreCase);

        public bool HasCompletedProperty =>
            !string.IsNullOrWhiteSpace(CompletedPropertyName) && HasValidCompletedPropertyType;

        public bool HasDueProperty => !string.IsNullOrWhiteSpace(DuePropertyName);

        /// <summary>
        /// True when completion and due date can both be read and written.
        /// </summary>
        public bool IsConfigured => HasCompletedProperty && HasDueProperty;
    }

    public class NotionSyncStateData
    {
        /// <summary>
        /// True when the page belongs to a Notion database.
        /// </summary>
        public bool IsDatabasePage { get; set; }

        /// <summary>
        /// Completion state read from the configured property, or null when it cannot be read.
        /// </summary>
        public bool? Completed { get; set; }

        /// <summary>
        /// Due date in UTC, or null when the page has no due date.
        /// </summary>
        public DateTime? Due { get; set; }

        public string Url { get; set; } = string.Empty;
    }

    #endregion

    /// <summary>
    /// Minimal Notion REST API client using an internal integration token.
    /// </summary>
    public class NotionService
    {
        private const string ApiBase = "https://api.notion.com/v1";
        private const string NotionVersion = "2022-06-28";
        private const int MaxAttempts = 2;

        private static readonly string[] CompletedStatusNames = { "Done", "Complete", "Closed", "Finished" };

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private string _token = string.Empty;
        private NotionSyncPropertyConfig? _syncConfig;

        public NotionService(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("Notion");

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_token);

        /// <summary>
        /// Property mapping used by the sync methods, or null when sync is not configured.
        /// </summary>
        public NotionSyncPropertyConfig? SyncConfig => _syncConfig;

        /// <summary>
        /// True when sync property names are configured well enough to read and write both fields.
        /// </summary>
        public bool IsSyncConfigured => _syncConfig != null && _syncConfig.IsConfigured;

        public void Configure(string? integrationToken, NotionSyncPropertyConfig? syncConfig = null)
        {
            _token = integrationToken?.Trim() ?? string.Empty;
            _syncConfig = syncConfig == null
                ? null
                : new NotionSyncPropertyConfig
                {
                    CompletedPropertyName = syncConfig.CompletedPropertyName?.Trim() ?? string.Empty,
                    CompletedPropertyType = string.IsNullOrWhiteSpace(syncConfig.CompletedPropertyType)
                        ? "checkbox"
                        : syncConfig.CompletedPropertyType.Trim(),
                    DuePropertyName = syncConfig.DuePropertyName?.Trim() ?? string.Empty
                };
        }

        public async Task<NotionUserData> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/users/me", null, cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            return new NotionUserData
            {
                Id = GetString(root, "id"),
                Name = GetString(root, "name"),
                Type = GetString(root, "type")
            };
        }

        /// <summary>
        /// Searches pages and databases that are shared with the integration.
        /// </summary>
        public async Task<List<NotionItemData>> SearchAsync(string? query, int limit, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 100);

            var payload = new Dictionary<string, object?>
            {
                ["query"] = query ?? string.Empty,
                ["page_size"] = limit,
                ["sort"] = new Dictionary<string, object?>
                {
                    ["direction"] = "descending",
                    ["timestamp"] = "last_edited_time"
                }
            };

            using var doc = await SendAsync(HttpMethod.Post, "/search", payload, cancellationToken).ConfigureAwait(false);

            var items = new List<NotionItemData>();
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return items;

            foreach (var item in results.EnumerateArray())
            {
                var objectType = GetString(item, "object");
                var isPage = string.Equals(objectType, "page", StringComparison.OrdinalIgnoreCase);
                var isDatabase = string.Equals(objectType, "database", StringComparison.OrdinalIgnoreCase);
                if (!isPage && !isDatabase)
                    continue;

                items.Add(new NotionItemData
                {
                    Id = GetString(item, "id"),
                    ObjectType = isPage ? "page" : "database",
                    Title = isPage ? GetPageTitle(item) : JoinPlainText(item, "title"),
                    Url = GetString(item, "url"),
                    LastEditedTime = ParseDate(GetString(item, "last_edited_time")),
                    ParentType = GetParentType(item),
                    ParentId = GetParentId(item)
                });
            }

            return items;
        }

        /// <summary>
        /// Adds a comment to a Notion page.
        /// </summary>
        /// <remarks>
        /// Throws <see cref="NotionApiException"/> when the integration cannot comment on the page;
        /// callers can hide the action or ignore the failure.
        /// </remarks>
        public async Task AddCommentAsync(string pageId, string comment, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

            var payload = new Dictionary<string, object?>
            {
                ["parent"] = new Dictionary<string, object?> { ["page_id"] = pageId },
                ["rich_text"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = "text",
                        ["text"] = new Dictionary<string, object?> { ["content"] = comment ?? string.Empty }
                    }
                }
            };

            using var doc = await SendAsync(HttpMethod.Post, "/comments", payload, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads completion and due date for a database page.
        /// </summary>
        public async Task<NotionSyncStateData> GetSyncStateAsync(string pageId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
            var config = RequireSyncConfig();

            using var doc = await SendAsync(
                HttpMethod.Get,
                $"/pages/{Uri.EscapeDataString(pageId)}",
                null,
                cancellationToken).ConfigureAwait(false);

            var root = doc.RootElement;
            var parentType = GetParentType(root);
            var isDatabasePage = string.Equals(parentType, "database_id", StringComparison.OrdinalIgnoreCase);

            bool? completed = null;
            DateTime? due = null;

            if (root.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
            {
                if (properties.TryGetProperty(config.CompletedPropertyName, out var completedProperty))
                {
                    completed = config.UsesStatusProperty
                        ? ReadStatusCompleted(completedProperty)
                        : ReadCheckbox(completedProperty);
                }

                if (properties.TryGetProperty(config.DuePropertyName, out var dueProperty))
                {
                    due = ReadDueDate(dueProperty);
                }
            }

            var url = GetString(root, "url");
            if (string.IsNullOrWhiteSpace(url))
                url = "https://www.notion.so/" + pageId.Replace("-", string.Empty);

            return new NotionSyncStateData
            {
                IsDatabasePage = isDatabasePage,
                Completed = completed,
                Due = due,
                Url = url
            };
        }

        public async Task SetCompletedAsync(string pageId, bool completed, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

            var config = _syncConfig;
            if (config == null || !config.HasCompletedProperty)
            {
                throw new NotionApiException(
                    "Notion sync is not configured for completion. Set the completed property name and type (\"checkbox\" or \"status\") in the Notion plugin settings.");
            }

            object propertyValue = config.UsesStatusProperty
                ? new Dictionary<string, object?>
                {
                    ["status"] = new Dictionary<string, object?> { ["name"] = completed ? "Done" : "Not started" }
                }
                : new Dictionary<string, object?> { ["checkbox"] = completed };

            var payload = new Dictionary<string, object?>
            {
                ["properties"] = new Dictionary<string, object?> { [config.CompletedPropertyName] = propertyValue }
            };

            using var doc = await SendAsync(
                HttpMethod.Patch,
                $"/pages/{Uri.EscapeDataString(pageId)}",
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task SetDueDateAsync(string pageId, DateTime? dueUtc, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

            var config = _syncConfig;
            if (config == null || !config.HasDueProperty)
            {
                throw new NotionApiException(
                    "Notion sync is not configured for due dates. Set the due date property name in the Notion plugin settings.");
            }

            var dateValue = dueUtc.HasValue
                ? new Dictionary<string, object?>
                {
                    ["start"] = dueUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                }
                : null;

            var payload = new Dictionary<string, object?>
            {
                ["properties"] = new Dictionary<string, object?>
                {
                    [config.DuePropertyName] = new Dictionary<string, object?> { ["date"] = dateValue }
                }
            };

            using var doc = await SendAsync(
                HttpMethod.Patch,
                $"/pages/{Uri.EscapeDataString(pageId)}",
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        private NotionSyncPropertyConfig RequireSyncConfig()
        {
            var config = _syncConfig;
            if (config == null || !config.IsConfigured)
            {
                throw new NotionApiException(
                    "Notion sync properties are not configured. Set the completed property (checkbox or status) and the due date property names in the Notion plugin settings.");
            }

            return config;
        }

        private async Task<JsonDocument> SendAsync(
            HttpMethod method,
            string path,
            object? body,
            CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                throw new NotionApiException(
                    "Notion integration token is not configured. Add an internal integration secret in the Notion plugin settings.");
            }

            var uri = ApiBase + path;
            var json = body == null ? null : JsonSerializer.Serialize(body);

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                using var request = new HttpRequestMessage(method, uri);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
                request.Headers.TryAddWithoutValidation("Notion-Version", NotionVersion);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Headers.TryAddWithoutValidation("User-Agent", "AIA-Notion-Plugin/1.0");

                if (json != null)
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);

                var isLastAttempt = attempt == MaxAttempts - 1;
                if (!isLastAttempt && IsRetryable(response.StatusCode))
                {
                    await Task.Delay(GetRetryDelay(response), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new NotionApiException(
                    BuildErrorMessage(response, text),
                    (int)response.StatusCode,
                    GetNotionErrorCode(text));
            }

            throw new NotionApiException("Notion request failed after retrying.");
        }

        private static bool IsRetryable(HttpStatusCode statusCode)
        {
            return (int)statusCode == 429 || (int)statusCode >= 500;
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;

            TimeSpan delay;
            if (retryAfter?.Delta is TimeSpan delta)
            {
                delay = delta;
            }
            else if (retryAfter?.Date is DateTimeOffset date)
            {
                delay = date - DateTimeOffset.UtcNow;
            }
            else
            {
                delay = TimeSpan.FromSeconds(1);
            }

            if (delay < TimeSpan.FromMilliseconds(250))
                return TimeSpan.FromMilliseconds(250);
            if (delay > TimeSpan.FromSeconds(30))
                return TimeSpan.FromSeconds(30);
            return delay;
        }

        private static string BuildErrorMessage(HttpResponseMessage response, string body)
        {
            var reason = GetNotionErrorMessage(body);
            var status = (int)response.StatusCode;

            return status switch
            {
                400 => reason ?? "Notion rejected the request (400 Bad Request). Check the configured property names and their types.",
                401 => "Notion rejected the integration token (401 Unauthorized). Verify the internal integration secret in the Notion plugin settings.",
                403 => reason ?? "Notion denied access (403 Forbidden). Share the page or database with the integration and allow update content in its capabilities.",
                404 => "Notion resource not found (404). Confirm the page still exists and is shared with the integration.",
                429 => reason ?? "Notion rate limit reached (429). Wait a few seconds and try again.",
                _ => reason ?? $"Notion request failed ({status} {response.ReasonPhrase})."
            };
        }

        private static string? GetNotionErrorMessage(string body)
        {
            var (_, message) = GetNotionError(body);
            return message;
        }

        private static string? GetNotionErrorCode(string body)
        {
            var (code, _) = GetNotionError(body);
            return code;
        }

        private static (string? Code, string? Message) GetNotionError(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return (null, null);

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return (null, null);

                var code = GetString(root, "code");
                var message = GetString(root, "message");

                return (
                    string.IsNullOrWhiteSpace(code) ? null : code,
                    string.IsNullOrWhiteSpace(message) ? null : message);
            }
            catch
            {
                // Fall through to the generic message.
            }

            return (null, null);
        }

        private static string GetPageTitle(JsonElement page)
        {
            if (!page.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
                return string.Empty;

            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                    continue;

                if (string.Equals(GetString(property.Value, "type"), "title", StringComparison.OrdinalIgnoreCase))
                    return JoinPlainText(property.Value, "title");
            }

            return string.Empty;
        }

        private static string JoinPlainText(JsonElement element, string arrayPropertyName)
        {
            if (!element.TryGetProperty(arrayPropertyName, out var parts) || parts.ValueKind != JsonValueKind.Array)
                return string.Empty;

            var builder = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object)
                    builder.Append(GetString(part, "plain_text"));
            }

            return builder.ToString().Trim();
        }

        private static string GetParentType(JsonElement element)
        {
            return element.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object
                ? GetString(parent, "type")
                : string.Empty;
        }

        private static string GetParentId(JsonElement element)
        {
            if (!element.TryGetProperty("parent", out var parent) || parent.ValueKind != JsonValueKind.Object)
                return string.Empty;

            var parentType = GetString(parent, "type");
            return parentType switch
            {
                "database_id" => GetString(parent, "database_id"),
                "page_id" => GetString(parent, "page_id"),
                "block_id" => GetString(parent, "block_id"),
                "workspace" => string.Empty,
                _ => GetString(parent, "id")
            };
        }

        private static bool? ReadCheckbox(JsonElement property)
        {
            if (!property.TryGetProperty("checkbox", out var checkbox))
                return null;

            return checkbox.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        private static bool? ReadStatusCompleted(JsonElement property)
        {
            if (!property.TryGetProperty("status", out var status))
                return null;

            // A status property without a selected value is not completed.
            if (status.ValueKind != JsonValueKind.Object)
                return false;

            var name = GetString(status, "name");
            if (string.IsNullOrWhiteSpace(name))
                return false;

            foreach (var completedName in CompletedStatusNames)
            {
                if (string.Equals(name, completedName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static DateTime? ReadDueDate(JsonElement property)
        {
            if (!property.TryGetProperty("date", out var date) || date.ValueKind != JsonValueKind.Object)
                return null;

            return ParseDate(GetString(date, "start"));
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return null;

            return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }
    }

    public class NotionApiException : Exception
    {
        public NotionApiException(string message) : base(message)
        {
        }

        public NotionApiException(string message, int statusCode, string? code = null) : base(message)
        {
            StatusCode = statusCode;
            Code = code;
        }

        /// <summary>
        /// HTTP status code returned by Notion, when the failure came from an API response.
        /// </summary>
        public int? StatusCode { get; }

        /// <summary>
        /// Notion error code, for example "validation_error" or "unauthorized".
        /// </summary>
        public string? Code { get; }
    }
}
