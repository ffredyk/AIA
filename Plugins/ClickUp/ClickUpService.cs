using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.ClickUp
{
    #region Data Transfer Objects

    public class ClickUpUserData
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class ClickUpTeamData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class ClickUpTaskData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusType { get; set; } = string.Empty;
        public DateTime? DueDate { get; set; }
        public string ListId { get; set; } = string.Empty;
        public string ListName { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string SpaceId { get; set; } = string.Empty;
        public string SpaceName { get; set; } = string.Empty;
        public List<string> Assignees { get; set; } = new List<string>();

        public bool IsCompleted =>
            string.Equals(StatusType, "closed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(StatusType, "done", StringComparison.OrdinalIgnoreCase);

        public string Location => string.IsNullOrWhiteSpace(ListName) ? SpaceName : ListName;
    }

    #endregion

    /// <summary>
    /// Minimal ClickUp REST API v2 client using a personal API token.
    /// </summary>
    public class ClickUpService
    {
        private const string ApiBase = "https://api.clickup.com/api/v2";
        private const string UserAgent = "AIA-ClickUp-Plugin/1.0";
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private string _token = string.Empty;

        public ClickUpService(IPluginContext context)
        {
            _context = context;
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("ClickUp");

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_token);

        public void Configure(string? personalToken)
        {
            _token = personalToken?.Trim() ?? string.Empty;
        }

        public async Task<ClickUpUserData> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/user", null, cancellationToken);
            var root = doc.RootElement;

            var user = root.TryGetProperty("user", out var userElement) && userElement.ValueKind == JsonValueKind.Object
                ? userElement
                : root;

            return ParseUser(user);
        }

        public async Task<List<ClickUpTeamData>> GetTeamsAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/team", null, cancellationToken);
            var teams = new List<ClickUpTeamData>();

            if (doc.RootElement.TryGetProperty("teams", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    teams.Add(new ClickUpTeamData
                    {
                        Id = GetString(item, "id"),
                        Name = GetString(item, "name")
                    });
                }
            }

            teams.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return teams;
        }

        /// <summary>
        /// Returns tasks assigned to the token owner across all accessible teams.
        /// Teams that fail (for example due to permissions) are skipped.
        /// </summary>
        public async Task<List<ClickUpTaskData>> GetMyTasksAsync(int limit, CancellationToken cancellationToken = default)
        {
            limit = Math.Max(limit, 1);

            var user = await TestConnectionAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(user.Id))
                throw new ClickUpApiException("ClickUp did not return a user id for the configured token.");

            var teams = await GetTeamsAsync(cancellationToken);
            var tasks = new List<ClickUpTaskData>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var team in teams)
            {
                if (string.IsNullOrWhiteSpace(team.Id))
                    continue;

                var path = $"/team/{team.Id}/task?assignees[]={Uri.EscapeDataString(user.Id)}&include_closed=false&subtasks=false&page=0";

                try
                {
                    using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
                    if (!doc.RootElement.TryGetProperty("tasks", out var array) || array.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var item in array.EnumerateArray())
                    {
                        var task = ParseTask(item);
                        if (string.IsNullOrWhiteSpace(task.Id) || !seenIds.Add(task.Id))
                            continue;

                        tasks.Add(task);
                    }
                }
                catch (ClickUpApiException)
                {
                    // One inaccessible team must not fail the whole list.
                }
                catch (HttpRequestException)
                {
                    // Transient transport failure for a single team.
                }
            }

            tasks.Sort(CompareTasks);
            return tasks.Take(limit).ToList();
        }

        public async Task AddCommentAsync(string taskId, string comment, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
            ArgumentNullException.ThrowIfNull(comment);

            var payload = new { comment_text = comment, notify_all = false };
            using var _ = await SendAsync(HttpMethod.Post, $"/task/{taskId}/comment", payload, cancellationToken);
        }

        /// <summary>
        /// Completes or reopens a task by picking a status with a matching type from its list.
        /// </summary>
        public async Task SetCompletedAsync(string taskId, bool completed, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

            string listId;
            string currentStatusType;

            using (var taskDoc = await SendAsync(HttpMethod.Get, $"/task/{taskId}", null, cancellationToken))
            {
                var task = taskDoc.RootElement;
                listId = task.TryGetProperty("list", out var list) ? GetString(list, "id") : string.Empty;
                currentStatusType = task.TryGetProperty("status", out var status) ? GetString(status, "type") : string.Empty;
            }

            if (string.IsNullOrWhiteSpace(listId))
                throw new ClickUpApiException($"ClickUp task {taskId} does not belong to a list, so its status cannot be changed.");

            if (completed == IsClosedStatusType(currentStatusType))
                return;

            using (var listDoc = await SendAsync(HttpMethod.Get, $"/list/{listId}", null, cancellationToken))
            {
                var chosen = ChooseStatus(listDoc.RootElement, completed);

                if (string.IsNullOrWhiteSpace(chosen))
                {
                    var target = completed ? "a closed or done" : "an open or custom";
                    var action = completed ? "completed" : "reopened";
                    throw new ClickUpApiException(
                        $"ClickUp list {listId} has no {target} status, so task {taskId} cannot be {action}.");
                }

                var payload = new { status = chosen };
                using var _ = await SendAsync(HttpMethod.Put, $"/task/{taskId}", payload, cancellationToken);
            }
        }

        /// <summary>
        /// Sets or clears the due date of a task.
        /// </summary>
        public async Task SetDueDateAsync(string taskId, DateTime? dueUtc, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

            var payload = new Dictionary<string, object?>
            {
                ["due_date"] = dueUtc.HasValue ? ToEpochMilliseconds(dueUtc.Value) : null,
                ["due_date_time"] = true
            };

            using var _ = await SendAsync(HttpMethod.Put, $"/task/{taskId}", payload, cancellationToken);
        }

        /// <summary>
        /// Reads the sync-relevant state of a task: completion, due date and web link.
        /// </summary>
        public async Task<(bool completed, DateTime? due, string url)> GetTaskSyncStateAsync(
            string taskId,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

            using var doc = await SendAsync(HttpMethod.Get, $"/task/{taskId}", null, cancellationToken);
            var task = doc.RootElement;

            var statusType = task.TryGetProperty("status", out var status) ? GetString(status, "type") : string.Empty;

            return (
                IsClosedStatusType(statusType),
                ParseDueDate(task),
                GetString(task, "url"));
        }

        #region Parsing helpers

        private static ClickUpUserData ParseUser(JsonElement user)
        {
            return new ClickUpUserData
            {
                Id = GetRawString(user, "id"),
                Username = GetString(user, "username"),
                Email = GetString(user, "email")
            };
        }

        private static ClickUpTaskData ParseTask(JsonElement item)
        {
            var task = new ClickUpTaskData
            {
                Id = GetString(item, "id"),
                Name = GetString(item, "name"),
                Description = FirstNonEmpty(GetString(item, "description"), GetString(item, "text_content")),
                Url = GetString(item, "url"),
                DueDate = ParseDueDate(item)
            };

            if (item.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object)
            {
                task.Status = GetString(status, "status");
                task.StatusType = GetString(status, "type");
            }

            if (item.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Object)
            {
                task.ListId = GetString(list, "id");
                task.ListName = GetString(list, "name");
            }

            if (item.TryGetProperty("folder", out var folder) && folder.ValueKind == JsonValueKind.Object)
            {
                task.FolderName = GetString(folder, "name");
            }

            if (item.TryGetProperty("space", out var space) && space.ValueKind == JsonValueKind.Object)
            {
                task.SpaceId = GetString(space, "id");
                task.SpaceName = FirstNonEmpty(GetString(space, "name"), task.SpaceId);
            }

            if (item.TryGetProperty("assignees", out var assignees) && assignees.ValueKind == JsonValueKind.Array)
            {
                foreach (var assignee in assignees.EnumerateArray())
                {
                    var name = FirstNonEmpty(GetString(assignee, "username"), GetString(assignee, "email"));
                    if (!string.IsNullOrWhiteSpace(name))
                        task.Assignees.Add(name);
                }
            }

            return task;
        }

        private static DateTime? ParseDueDate(JsonElement item)
        {
            if (!item.TryGetProperty("due_date", out var due))
                return null;

            long milliseconds;
            switch (due.ValueKind)
            {
                case JsonValueKind.String:
                    if (!long.TryParse(due.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds))
                        return null;
                    break;
                case JsonValueKind.Number:
                    if (!due.TryGetInt64(out milliseconds))
                        return null;
                    break;
                default:
                    return null;
            }

            if (milliseconds <= 0)
                return null;

            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
        }

        private static string? ChooseStatus(JsonElement listRoot, bool completed)
        {
            if (!listRoot.TryGetProperty("statuses", out var array) || array.ValueKind != JsonValueKind.Array)
                return null;

            var candidates = new List<string>();

            foreach (var status in array.EnumerateArray())
            {
                if (status.ValueKind != JsonValueKind.Object)
                    continue;

                var name = GetString(status, "status");
                var type = GetString(status, "type");

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
                    continue;

                var matches = completed ? IsClosedStatusType(type) : IsOpenStatusType(type);
                if (matches)
                    candidates.Add(name);
            }

            if (candidates.Count == 0)
                return null;

            if (!completed)
            {
                var toDo = candidates.FirstOrDefault(c => NormalizeStatusName(c) == "todo");
                if (toDo != null)
                    return toDo;
            }

            return candidates[0];
        }

        private static string NormalizeStatusName(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    builder.Append(char.ToLowerInvariant(ch));
            }

            return builder.ToString();
        }

        private static bool IsClosedStatusType(string? type)
        {
            return string.Equals(type, "closed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "done", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOpenStatusType(string? type)
        {
            return string.Equals(type, "open", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "custom", StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareTasks(ClickUpTaskData a, ClickUpTaskData b)
        {
            if (a.DueDate.HasValue != b.DueDate.HasValue)
                return a.DueDate.HasValue ? -1 : 1;

            if (a.DueDate.HasValue && b.DueDate.HasValue)
            {
                var byDueDate = a.DueDate.Value.CompareTo(b.DueDate.Value);
                if (byDueDate != 0)
                    return byDueDate;
            }

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static long ToEpochMilliseconds(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return string.IsNullOrWhiteSpace(first) ? second : first;
        }

        #endregion

        #region HTTP plumbing

        private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
                throw new ClickUpApiException("ClickUp personal token is not configured.");

            var json = body != null ? JsonSerializer.Serialize(body) : null;

            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, ApiBase + path);

                // ClickUp personal tokens are sent raw; a token already given as "Bearer ..." is kept verbatim.
                request.Headers.TryAddWithoutValidation("Authorization", _token);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                if (json != null)
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(request, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                    return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);

                var statusCode = (int)response.StatusCode;
                var retryable = statusCode == 429 || statusCode >= 500;

                if (retryable && attempt == 0)
                {
                    await Task.Delay(GetRetryDelay(response), cancellationToken);
                    continue;
                }

                throw new ClickUpApiException(BuildErrorMessage(response, text), statusCode);
            }
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                foreach (var value in values)
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                        return TimeSpan.FromSeconds(Math.Min(seconds, MaxRetryDelay.TotalSeconds));

                    if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                    {
                        var delay = at - DateTimeOffset.UtcNow;
                        if (delay > TimeSpan.Zero)
                            return delay > MaxRetryDelay ? MaxRetryDelay : delay;
                    }
                }
            }

            return TimeSpan.FromSeconds(1);
        }

        private static string BuildErrorMessage(HttpResponseMessage response, string body)
        {
            var reason = GetClickUpErrorMessage(body);
            var statusCode = (int)response.StatusCode;

            switch (statusCode)
            {
                case 401:
                    return "ClickUp rejected the personal token (401 Unauthorized). Check that the token is valid and has not been revoked.";
                case 403:
                    return reason != null
                        ? $"ClickUp denied access (403 Forbidden): {reason}"
                        : "ClickUp denied access (403 Forbidden). The token may lack access to this workspace, space or list.";
                case 404:
                    return reason != null
                        ? $"ClickUp resource not found (404): {reason}"
                        : "ClickUp resource not found (404). Check that the task, list or team still exists.";
                case 429:
                    return reason != null
                        ? $"ClickUp rate limit reached (429): {reason}"
                        : "ClickUp rate limit reached (429). Try again shortly.";
                default:
                    return reason ?? $"ClickUp request failed ({statusCode} {response.ReasonPhrase}).";
            }
        }

        private static string? GetClickUpErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                    return null;

                var err = GetString(root, "err");
                var code = GetString(root, "ECODE");

                if (!string.IsNullOrWhiteSpace(err))
                    return string.IsNullOrWhiteSpace(code) ? err : $"{err} ({code})";

                if (!string.IsNullOrWhiteSpace(code))
                    return $"ClickUp error {code}.";
            }
            catch (JsonException)
            {
                // Non-JSON error body: fall back to the generic message.
            }

            return null;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static string GetRawString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
                return string.Empty;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                _ => string.Empty
            };
        }

        #endregion
    }

    public class ClickUpApiException : Exception
    {
        public ClickUpApiException(string message) : base(message)
        {
        }

        public ClickUpApiException(string message, int statusCode) : base(message)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; }
    }
}
