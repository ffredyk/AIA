using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Jira
{
    #region Data Transfer Objects

    public class JiraUserData
    {
        public string AccountId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class JiraIssueData
    {
        public string Key { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusCategory { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string IssueType { get; set; } = string.Empty;
        public string ProjectKey { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string Assignee { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime? Updated { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class JiraCreatedIssueData
    {
        public string Id { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string SelfUrl { get; set; } = string.Empty;
    }

    #endregion

    /// <summary>
    /// Minimal Jira Cloud REST API client using email + API token authentication.
    /// </summary>
    public class JiraService
    {
        private const string IssueFields = "summary,status,priority,updated,issuetype,project,description,assignee,duedate,reporter";

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private string _baseUrl = string.Empty;
        private string _email = string.Empty;
        private string _apiToken = string.Empty;

        public JiraService(IPluginContext context)
        {
            _context = context;
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("Jira");

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_baseUrl) &&
            !string.IsNullOrWhiteSpace(_email) &&
            !string.IsNullOrWhiteSpace(_apiToken);

        public string BaseUrl => _baseUrl;

        public void Configure(string? baseUrl, string? email, string? apiToken)
        {
            _baseUrl = NormalizeBaseUrl(baseUrl);
            _email = email?.Trim() ?? string.Empty;
            _apiToken = apiToken?.Trim() ?? string.Empty;
        }

        public static string NormalizeBaseUrl(string? url)
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

        public async Task<JiraUserData> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/rest/api/2/myself", null, cancellationToken);
            var root = doc.RootElement;

            return new JiraUserData
            {
                AccountId = GetString(root, "accountId"),
                DisplayName = GetString(root, "displayName"),
                Email = GetString(root, "emailAddress")
            };
        }

        public async Task<List<JiraIssueData>> GetMyOpenIssuesAsync(int maxResults, CancellationToken cancellationToken = default)
        {
            maxResults = Math.Clamp(maxResults, 1, 200);

            const string jql = "assignee = currentUser() AND statusCategory != Done ORDER BY updated DESC";
            var path = $"/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&maxResults={maxResults}&fields={Uri.EscapeDataString(IssueFields)}";

            using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            var issues = new List<JiraIssueData>();

            if (!doc.RootElement.TryGetProperty("issues", out var items) || items.ValueKind != JsonValueKind.Array)
                return issues;

            foreach (var item in items.EnumerateArray())
            {
                issues.Add(ParseIssue(item));
            }

            return issues;
        }

        public async Task<JiraCreatedIssueData> CreateIssueAsync(
            string projectKey,
            string summary,
            string description,
            string issueType,
            CancellationToken cancellationToken = default)
        {
            var fields = new Dictionary<string, object?>
            {
                ["project"] = new { key = projectKey },
                ["summary"] = summary,
                ["issuetype"] = new { name = string.IsNullOrWhiteSpace(issueType) ? "Task" : issueType }
            };

            if (!string.IsNullOrWhiteSpace(description))
                fields["description"] = description;

            using var doc = await SendAsync(HttpMethod.Post, "/rest/api/2/issue", new { fields }, cancellationToken);
            var root = doc.RootElement;

            return new JiraCreatedIssueData
            {
                Id = GetString(root, "id"),
                Key = GetString(root, "key"),
                SelfUrl = GetString(root, "self")
            };
        }

        public async Task AddCommentAsync(string issueKey, string comment, CancellationToken cancellationToken = default)
        {
            var path = $"/rest/api/2/issue/{Uri.EscapeDataString(issueKey)}/comment";
            using var _ = await SendAsync(HttpMethod.Post, path, new { body = comment }, cancellationToken);
        }

        private static JiraIssueData ParseIssue(JsonElement item)
        {
            var issue = new JiraIssueData
            {
                Key = GetString(item, "key")
            };

            if (!item.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
                return issue;

            issue.Summary = GetString(fields, "summary");
            issue.Description = GetString(fields, "description");
            issue.Updated = ParseDate(GetString(fields, "updated"));
            issue.DueDate = ParseDate(GetString(fields, "duedate"));

            if (fields.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object)
            {
                issue.Status = GetString(status, "name");
                if (status.TryGetProperty("statusCategory", out var category) && category.ValueKind == JsonValueKind.Object)
                    issue.StatusCategory = GetString(category, "name");
            }

            if (fields.TryGetProperty("priority", out var priority) && priority.ValueKind == JsonValueKind.Object)
                issue.Priority = GetString(priority, "name");

            if (fields.TryGetProperty("issuetype", out var issueType) && issueType.ValueKind == JsonValueKind.Object)
                issue.IssueType = GetString(issueType, "name");

            if (fields.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object)
            {
                issue.ProjectKey = GetString(project, "key");
                issue.ProjectName = GetString(project, "name");
            }

            if (fields.TryGetProperty("assignee", out var assignee) && assignee.ValueKind == JsonValueKind.Object)
                issue.Assignee = GetString(assignee, "displayName");

            return issue;
        }

        private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
                throw new JiraApiException("Jira site URL is not configured.");
            if (string.IsNullOrWhiteSpace(_email) || string.IsNullOrWhiteSpace(_apiToken))
                throw new JiraApiException("Jira email and API token are required.");

            using var request = new HttpRequestMessage(method, _baseUrl + path);
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_email}:{_apiToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (body != null)
            {
                var json = JsonSerializer.Serialize(body);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new JiraApiException(BuildErrorMessage(response, text));
            }

            return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }

        private static string BuildErrorMessage(HttpResponseMessage response, string body)
        {
            var detail = GetJiraErrorMessage(body);

            return (int)response.StatusCode switch
            {
                401 => "Jira rejected the credentials (401). Check the email and API token.",
                403 => detail ?? "Jira denied access (403). Check the account permissions.",
                404 => detail ?? "Jira site or resource not found (404). Check the site URL.",
                400 => detail ?? "Jira rejected the request (400).",
                _ => detail ?? $"Jira request failed ({(int)response.StatusCode} {response.ReasonPhrase})."
            };
        }

        private static string? GetJiraErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);

                if (doc.RootElement.TryGetProperty("errorMessages", out var messages) &&
                    messages.ValueKind == JsonValueKind.Array)
                {
                    var values = messages.EnumerateArray()
                        .Where(m => m.ValueKind == JsonValueKind.String)
                        .Select(m => m.GetString())
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .ToList();

                    if (values.Count > 0)
                        return string.Join(" ", values);
                }

                if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var values = errors.EnumerateObject()
                        .Select(p => $"{p.Name}: {p.Value.GetString()}")
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .ToList();

                    if (values.Count > 0)
                        return string.Join("; ", values);
                }

                if (doc.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    return message.GetString();
            }
            catch
            {
                // Fall through.
            }

            return null;
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

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                return parsed.ToLocalTime().DateTime;

            return null;
        }
    }

    public class JiraApiException : Exception
    {
        public JiraApiException(string message) : base(message)
        {
        }
    }
}
