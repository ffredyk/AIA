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

namespace AIA.Plugins.AzureDevOps
{
    #region Data Transfer Objects

    public class AzureUserData
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
    }

    public class AzureWorkItemData
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string WorkItemType { get; set; } = string.Empty;
        public string AssignedTo { get; set; } = string.Empty;
        public string Project { get; set; } = string.Empty;
        public DateTime ChangedDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string Url { get; set; } = string.Empty;
        public int? Revisions { get; set; }
    }

    public class AzurePullRequestData
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Project { get; set; } = string.Empty;
        public string Repository { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
    }

    /// <summary>
    /// Synchronization-relevant snapshot of a work item.
    /// </summary>
    public class AzureWorkItemSyncState
    {
        public int Id { get; set; }
        public string State { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public DateTime? DueUtc { get; set; }
        public string Project { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    #endregion

    /// <summary>
    /// Minimal Azure DevOps REST API client using a personal access token (PAT).
    /// </summary>
    public class AzureDevOpsService
    {
        private const string ApiVersion = "api-version=7.0";
        private const string DueDateField = "Microsoft.VSTS.Scheduling.DueDate";

        private const string WorkItemFields =
            "System.Title,System.State,System.WorkItemType,System.AssignedTo,System.TeamProject," +
            DueDateField + ",System.ChangedDate";

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private string _organizationUrl = string.Empty;
        private string _pat = string.Empty;

        public AzureDevOpsService(IPluginContext context)
        {
            _context = context;
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("AzureDevOps");

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_organizationUrl) && !string.IsNullOrWhiteSpace(_pat);

        /// <summary>
        /// Normalized organization URL, for example https://dev.azure.com/contoso
        /// or https://contoso.visualstudio.com.
        /// </summary>
        public string OrganizationUrl => _organizationUrl;

        public void Configure(string? organizationUrl, string? pat)
        {
            _organizationUrl = NormalizeOrganizationUrl(organizationUrl);
            _pat = pat?.Trim() ?? string.Empty;
        }

        public static string NormalizeOrganizationUrl(string? url)
        {
            var value = url?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "https://" + value;
            }

            return value.TrimEnd('/');
        }

        public async Task<AzureUserData> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/_apis/connectionData?" + ApiVersion, null, cancellationToken);
            var root = doc.RootElement;

            var user = root;
            if (root.TryGetProperty("authenticatedUser", out var authenticated) && authenticated.ValueKind == JsonValueKind.Object)
                user = authenticated;

            var email = GetString(user, "emailAddress");
            if (string.IsNullOrWhiteSpace(email) &&
                user.TryGetProperty("properties", out var properties) &&
                properties.ValueKind == JsonValueKind.Object)
            {
                email = GetPropertyValue(properties, "Account");
            }

            return new AzureUserData
            {
                DisplayName = GetString(user, "displayName") is { Length: > 0 } displayName
                    ? displayName
                    : GetString(user, "providerDisplayName"),
                Email = email,
                Id = GetString(user, "id")
            };
        }

        public async Task<List<AzureWorkItemData>> GetAssignedWorkItemsAsync(int limit, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 200);

            var workItems = new List<AzureWorkItemData>();
            var ids = await QueryAssignedWorkItemIdsAsync(limit, cancellationToken);
            if (ids.Count == 0)
                return workItems;

            var fields = Uri.EscapeDataString(WorkItemFields);
            var path = "/_apis/wit/workitems?ids=" + string.Join(",", ids) + "&fields=" + fields + "&" + ApiVersion;

            using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken);

            if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
                return workItems;

            foreach (var item in value.EnumerateArray())
            {
                workItems.Add(ParseWorkItem(item));
            }

            return workItems;
        }

        public async Task<List<AzurePullRequestData>> GetReviewerPullRequestsAsync(int limit, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 200);

            var path = "/_apis/git/pullrequests?searchCriteria.reviewerId=@me" +
                       "&searchCriteria.status=active&$top=" + limit + "&" + ApiVersion;

            // The organization-level endpoint does not support reviewerId on every deployment.
            using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken, tolerateClientErrors: true);

            var pullRequests = new List<AzurePullRequestData>();
            if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
                return pullRequests;

            foreach (var item in value.EnumerateArray())
            {
                pullRequests.Add(ParsePullRequest(item));
            }

            return pullRequests;
        }

        public async Task AddCommentAsync(int workItemId, string comment, CancellationToken cancellationToken = default)
        {
            var path = "/_apis/wit/workItems/" + workItemId + "/comments?api-version=7.0-preview.3";
            using var _ = await SendAsync(HttpMethod.Post, path, new { text = comment ?? string.Empty }, cancellationToken);
        }

        public async Task SetStateAsync(int workItemId, string state, CancellationToken cancellationToken = default)
        {
            await SendJsonPatchAsync(workItemId, "/fields/System.State", state, cancellationToken);
        }

        /// <summary>
        /// Sets completion by mapping it to the Closed/Active states. Callers that need a different
        /// workflow state should call <see cref="SetStateAsync"/> directly.
        /// </summary>
        public async Task SetCompletedAsync(int id, bool completed, CancellationToken cancellationToken = default)
        {
            await SetStateAsync(id, completed ? "Closed" : "Active", cancellationToken);
        }

        public async Task SetDueDateAsync(int id, DateTime? dueUtc, CancellationToken cancellationToken = default)
        {
            var value = dueUtc.HasValue
                ? NormalizeUtc(dueUtc.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : string.Empty;

            await SendJsonPatchAsync(id, "/fields/" + DueDateField, value, cancellationToken);
        }

        public async Task<string> GetWorkItemStateAsync(int id, CancellationToken cancellationToken = default)
        {
            var path = "/_apis/wit/workitems/" + id + "?fields=System.State&" + ApiVersion;
            using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            return GetFieldString(doc.RootElement, "System.State");
        }

        /// <summary>
        /// Reads completion and due date in one request. Returns null when the work item no longer exists.
        /// </summary>
        public async Task<AzureWorkItemSyncState?> GetSyncStateAsync(int id, CancellationToken cancellationToken = default)
        {
            var fields = Uri.EscapeDataString("System.State,System.TeamProject," + DueDateField);
            var path = "/_apis/wit/workitems/" + id + "?fields=" + fields + "&" + ApiVersion;

            JsonDocument doc;
            try
            {
                doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            }
            catch (AzureDevOpsApiException ex) when (ex.StatusCode == 404)
            {
                return null;
            }

            using (doc)
            {
                var root = doc.RootElement;
                var state = GetFieldString(root, "System.State");
                var project = GetFieldString(root, "System.TeamProject");

                return new AzureWorkItemSyncState
                {
                    Id = id,
                    State = state,
                    IsCompleted = IsCompletedState(state),
                    DueUtc = GetFieldDate(root, DueDateField),
                    Project = project,
                    Url = BuildWorkItemUrl(project, id)
                };
            }
        }

        public static bool IsCompletedState(string? state)
        {
            return string.Equals(state, "Closed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "Done", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "Removed", StringComparison.OrdinalIgnoreCase);
        }

        public string BuildWorkItemUrl(string? project, int workItemId)
        {
            if (string.IsNullOrWhiteSpace(_organizationUrl))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(project))
                return _organizationUrl + "/_workitems/edit/" + workItemId;

            return _organizationUrl + "/" + Uri.EscapeDataString(project.Trim()) + "/_workitems/edit/" + workItemId;
        }

        private async Task<List<int>> QueryAssignedWorkItemIdsAsync(int limit, CancellationToken cancellationToken)
        {
            const string query =
                "SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = @Me " +
                "AND [System.State] <> 'Closed' AND [System.State] <> 'Removed' " +
                "ORDER BY [System.ChangedDate] DESC";

            var ids = new List<int>();
            using var doc = await SendAsync(HttpMethod.Post, "/_apis/wit/wiql?" + ApiVersion, new { query }, cancellationToken);

            if (!doc.RootElement.TryGetProperty("workItems", out var workItems) || workItems.ValueKind != JsonValueKind.Array)
                return ids;

            foreach (var item in workItems.EnumerateArray())
            {
                if (ids.Count >= limit)
                    break;

                if (item.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.Number && idValue.TryGetInt32(out var id))
                    ids.Add(id);
            }

            return ids;
        }

        private AzureWorkItemData ParseWorkItem(JsonElement item)
        {
            var workItem = new AzureWorkItemData
            {
                Id = GetInt(item, "id"),
                Title = GetFieldString(item, "System.Title"),
                State = GetFieldString(item, "System.State"),
                WorkItemType = GetFieldString(item, "System.WorkItemType"),
                Project = GetFieldString(item, "System.TeamProject"),
                ChangedDate = GetFieldDate(item, "System.ChangedDate")?.ToLocalTime() ?? DateTime.MinValue,
                DueDate = GetFieldDate(item, DueDateField),
                Revisions = GetFieldInt(item, "System.Rev")
            };

            if (item.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
            {
                workItem.AssignedTo = GetAssignedTo(fields);
            }

            workItem.Url = BuildWorkItemUrl(workItem.Project, workItem.Id);
            return workItem;
        }

        private AzurePullRequestData ParsePullRequest(JsonElement item)
        {
            var pullRequest = new AzurePullRequestData
            {
                Id = GetInt(item, "pullRequestId"),
                Title = GetString(item, "title"),
                Status = GetString(item, "status"),
                CreatedDate = ParseDate(GetString(item, "creationDate"))?.ToLocalTime() ?? DateTime.MinValue
            };

            if (item.TryGetProperty("createdBy", out var createdBy) && createdBy.ValueKind == JsonValueKind.Object)
                pullRequest.CreatedBy = GetString(createdBy, "displayName");

            var repositoryId = string.Empty;
            if (item.TryGetProperty("repository", out var repository) && repository.ValueKind == JsonValueKind.Object)
            {
                pullRequest.Repository = GetString(repository, "name");
                repositoryId = pullRequest.Repository;

                if (repository.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object)
                    pullRequest.Project = GetString(project, "name");
            }

            pullRequest.Url = BuildPullRequestUrl(pullRequest.Project, repositoryId, pullRequest.Id);
            return pullRequest;
        }

        private string BuildPullRequestUrl(string? project, string? repository, int pullRequestId)
        {
            if (string.IsNullOrWhiteSpace(_organizationUrl))
                return string.Empty;

            var builder = new StringBuilder(_organizationUrl);
            if (!string.IsNullOrWhiteSpace(project))
                builder.Append('/').Append(Uri.EscapeDataString(project.Trim()));
            if (!string.IsNullOrWhiteSpace(repository))
                builder.Append("/_git/").Append(Uri.EscapeDataString(repository.Trim()));

            return builder.Append("/pullrequest/").Append(pullRequestId).ToString();
        }

        private async Task SendJsonPatchAsync(int workItemId, string fieldPath, string? value, CancellationToken cancellationToken)
        {
            var operations = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["op"] = "add",
                    ["path"] = fieldPath,
                    ["value"] = value ?? string.Empty
                }
            };

            var path = "/_apis/wit/workitems/" + workItemId + "?" + ApiVersion;
            using var _ = await SendAsync(HttpMethod.Patch, path, operations, cancellationToken, "application/json-patch+json");
        }

        private async Task<JsonDocument> SendAsync(
            HttpMethod method,
            string path,
            object? body,
            CancellationToken cancellationToken,
            string? contentType = null,
            bool tolerateClientErrors = false)
        {
            EnsureConfigured();

            var payload = body == null ? null : JsonSerializer.Serialize(body);

            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, _organizationUrl + path);
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + _pat));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (payload != null)
                    request.Content = new StringContent(payload, Encoding.UTF8, contentType ?? "application/json");

                using var response = await HttpClient.SendAsync(request, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                var statusCode = (int)response.StatusCode;

                if (tolerateClientErrors && (statusCode == 400 || statusCode == 404))
                    return JsonDocument.Parse("{}");

                // Azure DevOps answers with a sign-in page when the PAT is rejected.
                if (statusCode == 203)
                    throw new AzureDevOpsApiException(BuildErrorMessage(response, text), statusCode);

                if ((statusCode == 429 || statusCode >= 500) && attempt == 0)
                {
                    await Task.Delay(GetRetryDelay(response), cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw new AzureDevOpsApiException(BuildErrorMessage(response, text), statusCode);

                return ParseJson(text);
            }
        }

        private static JsonDocument ParseJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return JsonDocument.Parse("{}");

            try
            {
                return JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                throw new AzureDevOpsApiException(
                    "Azure DevOps returned a response that is not JSON. Check the organization URL and personal access token.");
            }
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
                return delta > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delta;

            if (retryAfter?.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                    return wait > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : wait;
            }

            return TimeSpan.FromSeconds(2);
        }

        private void EnsureConfigured()
        {
            if (string.IsNullOrWhiteSpace(_organizationUrl))
                throw new AzureDevOpsApiException("Azure DevOps organization URL is not configured.");
            if (string.IsNullOrWhiteSpace(_pat))
                throw new AzureDevOpsApiException("Azure DevOps personal access token is not configured.");
        }

        private static string BuildErrorMessage(HttpResponseMessage response, string body)
        {
            var detail = GetAzureErrorMessage(body);

            return (int)response.StatusCode switch
            {
                203 => "Azure DevOps returned a sign-in page (203 Non-Authoritative). Check the personal access token scopes and expiry.",
                401 => "Azure DevOps rejected the personal access token (401 Unauthorized). Check the PAT scopes and expiry.",
                403 => detail ?? "Azure DevOps denied access (403). The personal access token may be missing the required scopes.",
                404 => detail ?? "Azure DevOps resource not found (404). Check the organization URL and project name.",
                429 => detail ?? "Azure DevOps rate limit reached (429). Try again shortly.",
                _ => detail ?? $"Azure DevOps request failed ({(int)response.StatusCode} {response.ReasonPhrase})."
            };
        }

        private static string? GetAzureErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                if (doc.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    var value = message.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
            catch
            {
                // Non-JSON error bodies fall through to the generic message.
            }

            return null;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        /// <summary>
        /// Reads a string property that Azure DevOps may wrap as { "$value": "..." }.
        /// </summary>
        private static string GetPropertyValue(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
                return string.Empty;

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? string.Empty;

            return value.ValueKind == JsonValueKind.Object ? GetString(value, "$value") : string.Empty;
        }

        private static int GetInt(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
                ? result
                : 0;
        }

        private static JsonElement GetFields(JsonElement item)
        {
            return item.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object
                ? fields
                : default;
        }

        private static string GetFieldString(JsonElement item, string fieldName)
        {
            var fields = GetFields(item);
            if (fields.ValueKind != JsonValueKind.Object ||
                !fields.TryGetProperty(fieldName, out var value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return value.GetString() ?? string.Empty;
        }

        private static int? GetFieldInt(JsonElement item, string fieldName)
        {
            var fields = GetFields(item);
            if (fields.ValueKind != JsonValueKind.Object ||
                !fields.TryGetProperty(fieldName, out var value) ||
                value.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            return value.TryGetInt32(out var result) ? result : null;
        }

        private static string GetAssignedTo(JsonElement fields)
        {
            if (!fields.TryGetProperty("System.AssignedTo", out var assignedTo))
                return string.Empty;

            if (assignedTo.ValueKind == JsonValueKind.String)
                return assignedTo.GetString() ?? string.Empty;

            if (assignedTo.ValueKind != JsonValueKind.Object)
                return string.Empty;

            var displayName = GetString(assignedTo, "displayName");
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;

            var uniqueName = GetString(assignedTo, "uniqueName");
            return string.IsNullOrWhiteSpace(uniqueName) ? GetString(assignedTo, "name") : uniqueName;
        }

        private static DateTime? GetFieldDate(JsonElement item, string fieldName)
        {
            var fields = GetFields(item);
            if (fields.ValueKind != JsonValueKind.Object ||
                !fields.TryGetProperty(fieldName, out var value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return ParseDate(value.GetString());
        }

        private static DateTime? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }
    }

    public class AzureDevOpsApiException : Exception
    {
        public AzureDevOpsApiException(string message, int? statusCode = null) : base(message)
        {
            StatusCode = statusCode;
        }

        /// <summary>HTTP status code of the failed response, when available.</summary>
        public int? StatusCode { get; }
    }
}
