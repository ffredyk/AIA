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

namespace AIA.Plugins.Asana
{
    #region Data Transfer Objects

    public class AsanaUserData
    {
        public string Gid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class AsanaWorkspaceData
    {
        public string Gid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class AsanaTaskData
    {
        public string Gid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public bool Completed { get; set; }
        public DateTime? DueOn { get; set; }
        public DateTime? DueAt { get; set; }
        public string Url { get; set; } = string.Empty;
        public List<string> ProjectNames { get; set; } = new List<string>();
        public string AssigneeName { get; set; } = string.Empty;

        public DateTime? Due => DueAt ?? DueOn;
    }

    #endregion

    /// <summary>
    /// Minimal Asana REST API client using a personal access token (PAT).
    /// </summary>
    public class AsanaService
    {
        private const string ApiBase = "https://app.asana.com/api/1.0";
        private const int MaxWorkspacesForTasks = 5;
        private const string TaskFields = "name,notes,completed,due_on,due_at,permalink_url,projects.name,assignee.name";

        private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private string _pat = string.Empty;

        public AsanaService(IPluginContext context)
        {
            _context = context;
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("Asana");

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_pat);

        public void Configure(string? pat)
        {
            _pat = pat?.Trim() ?? string.Empty;
        }

        public async Task<AsanaUserData> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/users/me?opt_fields=name,email", null, cancellationToken);
            var root = Unwrap(doc.RootElement);

            return new AsanaUserData
            {
                Gid = GetString(root, "gid"),
                Name = GetString(root, "name"),
                Email = GetString(root, "email")
            };
        }

        public async Task<List<AsanaWorkspaceData>> GetWorkspacesAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/workspaces?opt_fields=name", null, cancellationToken);

            var workspaces = new List<AsanaWorkspaceData>();
            foreach (var item in DataArray(doc.RootElement))
            {
                workspaces.Add(new AsanaWorkspaceData
                {
                    Gid = GetString(item, "gid"),
                    Name = GetString(item, "name")
                });
            }

            workspaces.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return workspaces;
        }

        /// <summary>
        /// Returns incomplete tasks assigned to the token owner across the first few workspaces,
        /// merged and sorted by due date, then name.
        /// </summary>
        public async Task<List<AsanaTaskData>> GetMyTasksAsync(int limit, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 100);

            var workspaces = await GetWorkspacesAsync(cancellationToken);
            var selected = new List<AsanaWorkspaceData>();
            foreach (var workspace in workspaces)
            {
                if (selected.Count >= MaxWorkspacesForTasks)
                    break;
                if (!string.IsNullOrWhiteSpace(workspace.Gid))
                    selected.Add(workspace);
            }

            var tasks = new Dictionary<string, AsanaTaskData>(StringComparer.Ordinal);
            if (selected.Count == 0)
                return new List<AsanaTaskData>();

            var perWorkspace = Math.Clamp((int)Math.Ceiling(limit / (double)selected.Count), 1, 100);

            foreach (var workspace in selected)
            {
                var path = "/tasks?assignee=me" +
                           "&workspace=" + Uri.EscapeDataString(workspace.Gid) +
                           "&completed_since=now" +
                           "&limit=" + perWorkspace.ToString(CultureInfo.InvariantCulture) +
                           "&opt_fields=" + TaskFields;

                using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
                foreach (var item in DataArray(doc.RootElement))
                {
                    var task = ParseTask(item);
                    if (string.IsNullOrEmpty(task.Gid))
                        continue;

                    // A task can surface in more than one workspace query; keep the first occurrence.
                    tasks[task.Gid] = task;
                }
            }

            var result = new List<AsanaTaskData>(tasks.Values);
            result.Sort(CompareTasks);

            if (result.Count > limit)
                result.RemoveRange(limit, result.Count - limit);

            return result;
        }

        public async Task AddCommentAsync(string taskGid, string text, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskGid);

            var payload = new { data = new { text = text ?? string.Empty } };
            using var doc = await SendAsync(HttpMethod.Post, $"/tasks/{Uri.EscapeDataString(taskGid)}/stories", payload, cancellationToken);
        }

        public async Task SetCompletedAsync(string taskGid, bool completed, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskGid);

            var payload = new { data = new { completed } };
            using var doc = await SendAsync(HttpMethod.Put, $"/tasks/{Uri.EscapeDataString(taskGid)}", payload, cancellationToken);
        }

        /// <summary>
        /// Sets the Asana due date. A null value clears the due date (best effort; Asana may ignore it).
        /// </summary>
        public async Task SetDueDateAsync(string taskGid, DateTime? dueUtc, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskGid);

            var dueOn = dueUtc?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var payload = new { data = new { due_on = dueOn } };
            using var doc = await SendAsync(HttpMethod.Put, $"/tasks/{Uri.EscapeDataString(taskGid)}", payload, cancellationToken);
        }

        public async Task<(bool completed, DateTime? due, string url)> GetTaskSyncStateAsync(string taskGid, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskGid);

            var path = $"/tasks/{Uri.EscapeDataString(taskGid)}?opt_fields=completed,due_on,due_at,permalink_url";
            using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            var root = Unwrap(doc.RootElement);

            return (GetBool(root, "completed"), GetDue(root), GetString(root, "permalink_url"));
        }

        private static AsanaTaskData ParseTask(JsonElement item)
        {
            var task = new AsanaTaskData
            {
                Gid = GetString(item, "gid"),
                Name = GetString(item, "name"),
                Notes = GetString(item, "notes"),
                Completed = GetBool(item, "completed"),
                DueOn = ParseDate(GetString(item, "due_on")),
                DueAt = ParseDateTime(GetString(item, "due_at")),
                Url = GetString(item, "permalink_url")
            };

            if (item.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
            {
                foreach (var project in projects.EnumerateArray())
                {
                    var projectName = GetString(project, "name");
                    if (!string.IsNullOrWhiteSpace(projectName))
                        task.ProjectNames.Add(projectName);
                }
            }

            if (item.TryGetProperty("assignee", out var assignee) && assignee.ValueKind == JsonValueKind.Object)
                task.AssigneeName = GetString(assignee, "name");

            return task;
        }

        private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
                throw new AsanaApiException("Asana personal access token is not configured.", HttpStatusCode.Unauthorized);

            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, ApiBase + path);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _pat);
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

                var status = (int)response.StatusCode;
                var retryable = status == 429 || status >= 500;
                if (retryable && attempt == 0)
                {
                    await Task.Delay(GetRetryDelay(response), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new AsanaApiException(BuildErrorMessage(response, text), response.StatusCode);
            }
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            var delay = retryAfter?.Delta;

            if ((!delay.HasValue || delay.Value <= TimeSpan.Zero) && retryAfter?.Date is DateTimeOffset date)
                delay = date - DateTimeOffset.UtcNow;

            if (!delay.HasValue || delay.Value <= TimeSpan.Zero)
                return DefaultRetryDelay;

            return delay.Value > MaxRetryDelay ? MaxRetryDelay : delay.Value;
        }

        private static string BuildErrorMessage(HttpResponseMessage response, string body)
        {
            var reason = GetAsanaErrorMessage(body);
            var detail = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason})";

            return (int)response.StatusCode switch
            {
                401 => $"Asana rejected the personal access token (401 Unauthorized){detail}. Check the token in the Asana plugin settings.",
                403 => $"Asana denied access (403 Forbidden){detail}. The token owner may lack access to this workspace, project or task.",
                404 => $"Asana resource not found (404){detail}. The task may have been deleted or moved to an inaccessible workspace.",
                429 => $"Asana rate limit reached (429){detail}. Wait a moment and try again.",
                _ => string.IsNullOrWhiteSpace(reason)
                        ? $"Asana request failed ({(int)response.StatusCode} {response.ReasonPhrase})."
                        : $"Asana request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {reason}"
            };
        }

        private static string? GetAsanaErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                {
                    var messages = new List<string>();
                    foreach (var error in errors.EnumerateArray())
                    {
                        if (error.ValueKind != JsonValueKind.Object)
                            continue;

                        var message = GetString(error, "message");
                        var help = GetString(error, "help");
                        if (!string.IsNullOrWhiteSpace(message))
                            messages.Add(string.IsNullOrWhiteSpace(help) ? message : $"{message} See {help}");
                    }

                    if (messages.Count > 0)
                        return string.Join("; ", messages);
                }

                var single = GetString(root, "error");
                if (!string.IsNullOrWhiteSpace(single))
                    return single;
            }
            catch
            {
                // Fall through to the generic message.
            }

            return null;
        }

        private static JsonElement Unwrap(JsonElement root)
        {
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("data", out var data) &&
                   data.ValueKind == JsonValueKind.Object
                ? data
                : root;
        }

        private static IEnumerable<JsonElement> DataArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data))
                root = data;

            if (root.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var item in root.EnumerateArray())
                yield return item;
        }

        private static int CompareTasks(AsanaTaskData a, AsanaTaskData b)
        {
            var aDue = a.Due;
            var bDue = b.Due;

            if (aDue.HasValue != bDue.HasValue)
                return aDue.HasValue ? -1 : 1;

            if (aDue.HasValue && bDue.HasValue)
            {
                var dueComparison = aDue.Value.CompareTo(bDue.Value);
                if (dueComparison != 0)
                    return dueComparison;
            }

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime? GetDue(JsonElement element)
        {
            var dueAt = ParseDateTime(GetString(element, "due_at"));
            if (dueAt.HasValue)
                return dueAt.Value.ToUniversalTime();

            var dueOn = ParseDate(GetString(element, "due_on"));
            return dueOn.HasValue ? DateTime.SpecifyKind(dueOn.Value.Date, DateTimeKind.Utc) : null;
        }

        private static DateTime? ParseDateTime(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToLocalTime()
                : null;
        }

        private static DateTime? ParseDate(string value)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool GetBool(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
        }
    }

    public class AsanaApiException : Exception
    {
        public AsanaApiException(string message, HttpStatusCode statusCode) : base(message)
        {
            StatusCode = statusCode;
        }

        public HttpStatusCode StatusCode { get; }
    }
}
