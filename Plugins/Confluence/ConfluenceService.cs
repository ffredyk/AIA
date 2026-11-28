using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Confluence
{
    #region Data Transfer Objects

    public class ConfluenceUserData
    {
        public string AccountId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class ConfluencePageData
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SpaceKey { get; set; } = string.Empty;
        public string SpaceName { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTime? Updated { get; set; }
        public string Url { get; set; } = string.Empty;
    }

    #endregion

    /// <summary>
    /// Minimal Confluence Cloud REST API client using email + API token authentication.
    /// </summary>
    public class ConfluenceService
    {
        private const string SearchExpand = "space,version,history.lastUpdated";
        private const int MaxRetryDelaySeconds = 30;

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private string _siteUrl = string.Empty;
        private string _email = string.Empty;
        private string _apiToken = string.Empty;

        public ConfluenceService(IPluginContext context)
        {
            _context = context;
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("Confluence");

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_siteUrl) &&
            !string.IsNullOrWhiteSpace(_email) &&
            !string.IsNullOrWhiteSpace(_apiToken);

        public string SiteUrl => _siteUrl;

        public string RestBaseUrl => string.IsNullOrEmpty(_siteUrl) ? string.Empty : _siteUrl + "/wiki/rest/api";

        public void Configure(string? siteUrl, string? email, string? apiToken)
        {
            _siteUrl = NormalizeSiteUrl(siteUrl);
            _email = email?.Trim() ?? string.Empty;
            _apiToken = apiToken?.Trim() ?? string.Empty;
        }

        public static string NormalizeSiteUrl(string? url)
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

        public string BuildPageUrl(string pageId, string? spaceKey = null)
        {
            if (string.IsNullOrWhiteSpace(_siteUrl))
                return string.Empty;

            var escapedId = Uri.EscapeDataString(pageId);
            return string.IsNullOrWhiteSpace(spaceKey)
                ? $"{_siteUrl}/wiki/pages/{escapedId}"
                : $"{_siteUrl}/wiki/spaces/{Uri.EscapeDataString(spaceKey)}/pages/{escapedId}";
        }

        public async Task<ConfluenceUserData> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(HttpMethod.Get, "/user/current", null, cancellationToken);
            var root = doc.RootElement;

            var email = GetString(root, "email");
            if (string.IsNullOrEmpty(email))
                email = GetString(root, "emailAddress");

            return new ConfluenceUserData
            {
                AccountId = GetString(root, "accountId"),
                DisplayName = GetString(root, "displayName"),
                Email = email
            };
        }

        public async Task<List<ConfluencePageData>> SearchAsync(string? text, int limit, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 100);

            var cql = BuildCql(text);
            var path = $"/content/search?cql={Uri.EscapeDataString(cql)}&limit={limit}&expand={Uri.EscapeDataString(SearchExpand)}";

            using var doc = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            var pages = new List<ConfluencePageData>();

            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return pages;

            foreach (var item in results.EnumerateArray())
            {
                pages.Add(ParsePage(item));
            }

            return pages;
        }

        public async Task AddCommentAsync(string pageId, string comment, CancellationToken cancellationToken = default)
        {
            var path = $"/content/{Uri.EscapeDataString(pageId)}/child/comment";

            var body = new
            {
                type = "comment",
                container = new { id = pageId, type = "page" },
                body = new
                {
                    storage = new
                    {
                        value = "<p>" + WebUtility.HtmlEncode(comment ?? string.Empty) + "</p>",
                        representation = "storage"
                    }
                }
            };

            using var _ = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        }

        internal static string BuildCql(string? text)
        {
            const string baseQuery = "type=page";

            if (string.IsNullOrWhiteSpace(text))
                return baseQuery + " order by lastmodified desc";

            var escapedQuotes = text.Trim().Replace("\"", "\\\"");
            return $"{baseQuery} and text ~ \"{escapedQuotes}\" order by lastmodified desc";
        }

        private ConfluencePageData ParsePage(JsonElement item)
        {
            var page = new ConfluencePageData
            {
                Id = GetString(item, "id"),
                Title = GetString(item, "title")
            };

            if (item.TryGetProperty("space", out var space) && space.ValueKind == JsonValueKind.Object)
            {
                page.SpaceKey = GetString(space, "key");
                page.SpaceName = GetString(space, "name");
            }

            string? versionWhen = null;
            if (item.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Object)
            {
                page.Version = GetInt(version, "number");
                versionWhen = GetString(version, "when");
            }

            string? lastUpdatedWhen = null;
            if (item.TryGetProperty("history", out var history) && history.ValueKind == JsonValueKind.Object &&
                history.TryGetProperty("lastUpdated", out var lastUpdated) && lastUpdated.ValueKind == JsonValueKind.Object)
            {
                lastUpdatedWhen = GetString(lastUpdated, "when");
            }

            page.Updated = ParseDate(lastUpdatedWhen) ?? ParseDate(versionWhen);
            page.Url = BuildPageUrl(page.Id, page.SpaceKey);

            return page;
        }

        private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_siteUrl))
                throw new ConfluenceApiException("Confluence site URL is not configured.");
            if (string.IsNullOrWhiteSpace(_email) || string.IsNullOrWhiteSpace(_apiToken))
                throw new ConfluenceApiException("Confluence email and API token are required.");

            var url = RestBaseUrl + path;
            var serializedBody = body == null ? null : JsonSerializer.Serialize(body);

            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, url);
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_email}:{_apiToken}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (serializedBody != null)
                    request.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(request, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                    return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);

                if (attempt == 0 && ShouldRetry(response.StatusCode))
                {
                    await Task.Delay(GetRetryDelay(response), cancellationToken);
                    continue;
                }

                throw new ConfluenceApiException(BuildErrorMessage(response, text));
            }
        }

        private static bool ShouldRetry(HttpStatusCode statusCode)
        {
            return (int)statusCode == 429 || (int)statusCode >= 500;
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter?.Delta is TimeSpan delta)
                return ClampDelay(delta);

            if (retryAfter?.Date is DateTimeOffset date)
                return ClampDelay(date - DateTimeOffset.UtcNow);

            return TimeSpan.FromSeconds(1);
        }

        private static TimeSpan ClampDelay(TimeSpan delay)
        {
            if (delay < TimeSpan.Zero)
                return TimeSpan.Zero;
            if (delay > TimeSpan.FromSeconds(MaxRetryDelaySeconds))
                return TimeSpan.FromSeconds(MaxRetryDelaySeconds);
            return delay;
        }

        private static string BuildErrorMessage(HttpResponseMessage response, string body)
        {
            var detail = GetConfluenceErrorMessage(body);
            var retryHint = response.Headers.RetryAfter != null
                ? $" Retry after {response.Headers.RetryAfter}."
                : string.Empty;

            return (int)response.StatusCode switch
            {
                401 => "Confluence rejected the credentials (401). Check the email and API token.",
                403 => detail ?? "Confluence denied access (403). Check the account permissions.",
                404 => detail ?? "Confluence site or resource not found (404). Check the site URL and the page id.",
                429 => (detail ?? "Confluence rate limit reached (429). Wait and try again.") + retryHint,
                _ => detail ?? $"Confluence request failed ({(int)response.StatusCode} {response.ReasonPhrase})."
            };
        }

        private static string? GetConfluenceErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                {
                    var values = errors.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.Object
                            ? GetString(e, "message")
                            : e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .ToList();

                    if (values.Count > 0)
                        return string.Join("; ", values);
                }

                if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    var value = message.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
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

        private static int GetInt(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
                return 0;

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 0
            };
        }

        private static DateTime? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                return parsed.ToLocalTime().DateTime;

            return null;
        }
    }

    public class ConfluenceApiException : Exception
    {
        public ConfluenceApiException(string message) : base(message)
        {
        }
    }
}
