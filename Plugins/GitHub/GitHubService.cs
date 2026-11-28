using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.GitHub
{
    #region Data Transfer Objects

    public class GitHubUserData
    {
        public string Login { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class GitHubItemData
    {
        public string Id { get; set; } = string.Empty;
        public int Number { get; set; }
        public string Repository { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool IsPullRequest { get; set; }
        public bool IsNotification { get; set; }
        public string NotificationThreadId { get; set; } = string.Empty;
        public bool IsRead { get; set; } = true;
        public DateTime UpdatedAt { get; set; }
        public string Kind { get; set; } = string.Empty;
    }

    #endregion

    /// <summary>
    /// Minimal GitHub REST API client (api.github.com or a GitHub Enterprise server).
    /// The HTTP client is created lazily because network permissions may be denied.
    /// </summary>
    public class GitHubService
    {
        private const string DefaultApiBaseUrl = "https://api.github.com";
        private const string WebBaseUrl = "https://github.com";
        private const string ApiVersion = "2022-11-28";
        private const string UserAgent = "AIA-GitHub-Plugin/1.0";
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private string _token = string.Empty;
        private string _apiBaseUrl = DefaultApiBaseUrl;

        public GitHubService(IPluginContext context)
        {
            _context = context;
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("GitHub");

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_token);

        public string ApiBaseUrl => _apiBaseUrl;

        public void Configure(string? token, string? apiBaseUrl)
        {
            _token = token?.Trim() ?? string.Empty;
            _apiBaseUrl = NormalizeBaseUrl(apiBaseUrl);
        }

        /// <summary>
        /// Normalizes a user supplied base URL. The public API host is kept as is,
        /// GitHub Enterprise hosts get the "/api/v3" suffix appended once.
        /// </summary>
        public static string NormalizeBaseUrl(string? apiBaseUrl)
        {
            var value = apiBaseUrl?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                return DefaultApiBaseUrl;

            value = value.TrimEnd('/');
            if (!value.Contains("://", StringComparison.Ordinal))
                value = "https://" + value;
            value = value.TrimEnd('/');

            if (value.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase))
                return value;

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                (uri.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase) || string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)))
            {
                return value;
            }

            return value + "/api/v3";
        }

        public async Task<GitHubUserData> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/user", null, cancellationToken);
            var root = doc.RootElement;

            return new GitHubUserData
            {
                Login = GetString(root, "login"),
                Name = GetString(root, "name")
            };
        }

        public async Task<List<GitHubItemData>> GetAssignedIssuesAsync(int limit, CancellationToken cancellationToken = default)
        {
            limit = ClampLimit(limit);
            using var doc = await SendAsync(HttpMethod.Get, $"/issues?filter=assigned&state=open&per_page={limit}", null, cancellationToken);
            var items = new List<GitHubItemData>();

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return items;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("pull_request", out _))
                    continue;

                items.Add(ParseIssue(item, "Issue"));
            }

            return items;
        }

        public async Task<List<GitHubItemData>> GetReviewRequestedPullRequestsAsync(int limit, CancellationToken cancellationToken = default)
        {
            limit = ClampLimit(limit);
            const string Query = "is:pr is:open review-requested:@me";
            var path = $"/search/issues?q={Uri.EscapeDataString(Query)}&per_page={limit}";
            using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            var items = new List<GitHubItemData>();

            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("items", out var itemsElement) ||
                itemsElement.ValueKind != JsonValueKind.Array)
            {
                return items;
            }

            foreach (var item in itemsElement.EnumerateArray())
            {
                items.Add(ParseIssue(item, "PullRequest"));
            }

            return items;
        }

        public async Task<List<GitHubItemData>> GetUnreadNotificationsAsync(int limit, CancellationToken cancellationToken = default)
        {
            limit = ClampLimit(limit);
            using var doc = await SendAsync(HttpMethod.Get, $"/notifications?all=false&per_page={limit}", null, cancellationToken);
            var items = new List<GitHubItemData>();

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return items;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = GetId(item, "id");
                var repository = GetString(GetObject(item, "repository"), "full_name");
                var repositoryUrl = GetString(GetObject(item, "repository"), "html_url");

                var subject = GetObject(item, "subject");
                var kind = GetString(subject, "type");
                var subjectUrl = GetString(subject, "url");

                var unread = item.TryGetProperty("unread", out var unreadValue) &&
                             unreadValue.ValueKind == JsonValueKind.True;

                items.Add(new GitHubItemData
                {
                    Id = id,
                    Number = ParseTrailingNumber(subjectUrl),
                    Repository = repository,
                    Title = GetString(subject, "title"),
                    Body = string.Empty,
                    Url = DeriveWebUrl(subjectUrl, repositoryUrl),
                    IsPullRequest = string.Equals(kind, "PullRequest", StringComparison.OrdinalIgnoreCase),
                    IsNotification = true,
                    NotificationThreadId = id,
                    IsRead = !unread,
                    UpdatedAt = ParseUtc(GetString(item, "updated_at")),
                    Kind = kind
                });
            }

            return items;
        }

        public async Task AddCommentAsync(string repository, int number, string comment, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);

            var payload = new { body = comment ?? string.Empty };
            using var doc = await SendAsync(HttpMethod.Post, $"/repos/{repository.Trim()}/issues/{number}/comments", payload, cancellationToken);
        }

        public async Task MarkNotificationReadAsync(string threadId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

            using var doc = await SendAsync(HttpMethod.Patch, $"/notifications/threads/{threadId.Trim()}", null, cancellationToken);
        }

        public async Task SetIssueCompletedAsync(string repository, int number, bool completed, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);

            var payload = new { state = completed ? "closed" : "open" };
            using var doc = await SendAsync(HttpMethod.Patch, $"/repos/{repository.Trim()}/issues/{number}", payload, cancellationToken);
        }

        /// <summary>
        /// Returns true when the issue is closed, false when it is open and null when the
        /// payload does not contain a state.
        /// </summary>
        public async Task<bool?> GetIssueCompletedAsync(string repository, int number, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);

            using var doc = await SendAsync(HttpMethod.Get, $"/repos/{repository.Trim()}/issues/{number}", null, cancellationToken);
            var state = GetString(doc.RootElement, "state");

            if (string.IsNullOrWhiteSpace(state))
                return null;

            return string.Equals(state, "closed", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
                throw new GitHubApiException("GitHub token is not configured.");

            var json = body != null ? JsonSerializer.Serialize(body) : null;

            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, _apiBaseUrl + path);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
                request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                if (json != null)
                {
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using var response = await HttpClient.SendAsync(request, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
                }

                var statusCode = (int)response.StatusCode;
                var retryable = statusCode == 429 || statusCode >= 500;

                if (retryable && attempt == 0)
                {
                    await Task.Delay(GetRetryDelay(response), cancellationToken);
                    continue;
                }

                throw new GitHubApiException(BuildErrorMessage(response, text), statusCode);
            }
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                foreach (var value in values)
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                    {
                        return TimeSpan.FromSeconds(Math.Min(seconds, MaxRetryDelay.TotalSeconds));
                    }
                }
            }

            return TimeSpan.FromSeconds(1);
        }

        private static string BuildErrorMessage(HttpResponseMessage response, string body)
        {
            var reason = GetGitHubErrorMessage(body);

            switch ((int)response.StatusCode)
            {
                case 401:
                    return "GitHub token rejected (401 Unauthorized). Check that the personal access token is valid and has not expired.";
                case 403:
                    return BuildForbiddenMessage(response, reason);
                case 404:
                    return reason ?? "GitHub resource not found (404). Check the repository and item number.";
                case 422:
                    return reason != null
                        ? $"GitHub rejected the request (422 Validation failed): {reason}"
                        : "GitHub rejected the request (422 Validation failed).";
                case 429:
                    return reason ?? "GitHub rate limit reached (429). Try again shortly.";
                default:
                    return reason ?? $"GitHub request failed ({(int)response.StatusCode} {response.ReasonPhrase}).";
            }
        }

        private static string BuildForbiddenMessage(HttpResponseMessage response, string? reason)
        {
            var remaining = GetHeaderValue(response, "X-RateLimit-Remaining");
            if (!string.Equals(remaining, "0", StringComparison.Ordinal))
            {
                return reason ?? "GitHub denied access (403 Forbidden). The token may lack the required scopes.";
            }

            var reset = GetHeaderValue(response, "X-RateLimit-Reset");
            if (long.TryParse(reset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
            {
                var resetAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
                return $"GitHub API rate limit exceeded (403). Resets at {resetAt:yyyy-MM-dd HH:mm} local time.";
            }

            return "GitHub API rate limit exceeded (403). Try again later.";
        }

        private static string? GetHeaderValue(HttpResponseMessage response, string name)
        {
            return response.Headers.TryGetValues(name, out var values)
                ? values.FirstOrDefault()
                : null;
        }

        private static string? GetGitHubErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }
            }
            catch
            {
                // Fall through to the generic message.
            }

            return null;
        }

        private static GitHubItemData ParseIssue(JsonElement item, string kind)
        {
            var repository = GetString(GetObject(item, "repository"), "full_name");
            if (string.IsNullOrWhiteSpace(repository))
                repository = ParseRepositoryFromUrl(GetString(item, "repository_url"));

            return new GitHubItemData
            {
                Id = GetId(item, "id"),
                Number = GetInt(item, "number"),
                Repository = repository,
                Title = GetString(item, "title"),
                Body = GetString(item, "body"),
                Url = GetString(item, "html_url"),
                IsPullRequest = string.Equals(kind, "PullRequest", StringComparison.OrdinalIgnoreCase) ||
                                item.TryGetProperty("pull_request", out _),
                IsNotification = false,
                IsRead = true,
                UpdatedAt = ParseUtc(GetString(item, "updated_at")),
                Kind = kind
            };
        }

        private static string ParseRepositoryFromUrl(string repositoryUrl)
        {
            if (string.IsNullOrWhiteSpace(repositoryUrl))
                return string.Empty;

            const string Marker = "/repos/";
            var index = repositoryUrl.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? repositoryUrl.Substring(index + Marker.Length).Trim('/') : string.Empty;
        }

        private static string DeriveWebUrl(string subjectUrl, string repositoryHtmlUrl)
        {
            const string ApiPrefix = "https://api.github.com/";

            if (!string.IsNullOrWhiteSpace(subjectUrl))
            {
                if (subjectUrl.StartsWith(ApiPrefix, StringComparison.OrdinalIgnoreCase))
                    return WebBaseUrl + "/" + subjectUrl.Substring(ApiPrefix.Length);

                const string Marker = "/repos/";
                var index = subjectUrl.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                    return WebBaseUrl + "/" + subjectUrl.Substring(index + Marker.Length);
            }

            return repositoryHtmlUrl;
        }

        private static int ParseTrailingNumber(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return 0;

            var trimmed = url.TrimEnd('/');
            var lastSlash = trimmed.LastIndexOf('/');
            if (lastSlash < 0 || lastSlash == trimmed.Length - 1)
                return 0;

            return int.TryParse(trimmed.Substring(lastSlash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0;
        }

        private static int ClampLimit(int limit) => Math.Clamp(limit, 1, 100);

        private static JsonElement GetObject(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.Object
                ? value
                : default;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static string GetId(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
                return string.Empty;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.ToString(),
                _ => string.Empty
            };
        }

        private static int GetInt(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.Number &&
                   value.TryGetInt32(out var number)
                ? number
                : 0;
        }

        private static DateTime ParseUtc(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToUniversalTime()
                : DateTime.UtcNow;
        }
    }

    public class GitHubApiException : Exception
    {
        public GitHubApiException(string message, int? statusCode = null) : base(message)
        {
            StatusCode = statusCode;
        }

        /// <summary>
        /// HTTP status code returned by GitHub, or null when the failure happened locally.
        /// </summary>
        public int? StatusCode { get; }
    }
}
