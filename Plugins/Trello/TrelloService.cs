using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Trello
{
    #region Data Transfer Objects

    public class TrelloMemberData
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }

    public class TrelloCardData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime? Due { get; set; }
        public bool DueComplete { get; set; }
        public bool Closed { get; set; }
        public string BoardId { get; set; } = string.Empty;
        public string BoardName { get; set; } = string.Empty;
        public string ListId { get; set; } = string.Empty;
        public string ListName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Snapshot of a Trello card used for synchronization.
    /// </summary>
    public sealed record TrelloCardState(bool Completed, DateTime? DueUtc, string Url);

    #endregion

    /// <summary>
    /// Minimal Trello REST API client using an API key and token.
    /// </summary>
    public class TrelloService
    {
        private const string ApiBase = "https://api.trello.com/1";
        private const string CardUrlBase = "https://trello.com/c/";
        private const int MaxBoardListLookups = 20;
        private const int MaxAttempts = 2;

        private readonly IPluginContext _context;
        private HttpClient? _httpClient;
        private string _apiKey = string.Empty;
        private string _token = string.Empty;

        public TrelloService(IPluginContext context)
        {
            _context = context;
        }

        private HttpClient HttpClient => _httpClient ??= _context.HttpClientFactory.CreateClient("Trello");

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_token);

        public void Configure(string? apiKey, string? token)
        {
            _apiKey = apiKey?.Trim() ?? string.Empty;
            _token = token?.Trim() ?? string.Empty;
        }

        public async Task<TrelloMemberData> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            using var doc = await SendAsync(
                HttpMethod.Get,
                "/members/me",
                new Dictionary<string, string?> { ["fields"] = "id,username,fullname" },
                null,
                cancellationToken).ConfigureAwait(false);

            var root = doc.RootElement;

            return new TrelloMemberData
            {
                Id = GetString(root, "id"),
                Username = GetString(root, "username"),
                FullName = GetString(root, "fullname")
            };
        }

        public async Task<List<TrelloCardData>> GetMyCardsAsync(int limit, CancellationToken cancellationToken = default)
        {
            if (limit <= 0)
                return new List<TrelloCardData>();

            using var doc = await SendAsync(
                HttpMethod.Get,
                "/members/me/cards",
                new Dictionary<string, string?>
                {
                    ["fields"] = "name,desc,url,due,dueComplete,idBoard,idList,closed"
                },
                null,
                cancellationToken).ConfigureAwait(false);

            var cards = new List<TrelloCardData>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (GetBool(item, "closed"))
                    continue;

                cards.Add(new TrelloCardData
                {
                    Id = GetString(item, "id"),
                    Name = GetString(item, "name"),
                    Description = GetString(item, "desc"),
                    Url = GetString(item, "url"),
                    Due = ParseDue(GetString(item, "due")),
                    DueComplete = GetBool(item, "dueComplete"),
                    Closed = GetBool(item, "closed"),
                    BoardId = GetString(item, "idBoard"),
                    ListId = GetString(item, "idList")
                });
            }

            if (cards.Count == 0)
                return cards;

            var boardNames = await GetBoardNamesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var card in cards)
            {
                if (boardNames.TryGetValue(card.BoardId, out var boardName))
                    card.BoardName = boardName;
            }

            var distinctBoardIds = cards
                .Select(card => card.BoardId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Take(MaxBoardListLookups)
                .ToList();

            var listNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var boardId in distinctBoardIds)
            {
                await AddListNamesForBoardAsync(boardId, listNames, cancellationToken).ConfigureAwait(false);
            }

            foreach (var card in cards)
            {
                if (listNames.TryGetValue(card.ListId, out var listName))
                    card.ListName = listName;
            }

            return cards.Take(limit).ToList();
        }

        public async Task AddCommentAsync(string cardId, string comment, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                throw new ArgumentException("Card identifier is required.", nameof(cardId));

            using var doc = await SendAsync(
                HttpMethod.Post,
                $"/cards/{Uri.EscapeDataString(cardId)}/actions/comments",
                null,
                () => new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("text", comment ?? string.Empty)
                }),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task SetDueCompleteAsync(string cardId, bool completed, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                throw new ArgumentException("Card identifier is required.", nameof(cardId));

            using var doc = await SendAsync(
                HttpMethod.Put,
                $"/cards/{Uri.EscapeDataString(cardId)}",
                new Dictionary<string, string?>
                {
                    ["dueComplete"] = completed ? "true" : "false"
                },
                null,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task SetDueDateAsync(string cardId, DateTime? dueUtc, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                throw new ArgumentException("Card identifier is required.", nameof(cardId));

            // Trello clears the due date when "due" is sent as null.
            var dueValue = dueUtc.HasValue
                ? dueUtc.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
                : "null";

            using var doc = await SendAsync(
                HttpMethod.Put,
                $"/cards/{Uri.EscapeDataString(cardId)}",
                new Dictionary<string, string?> { ["due"] = dueValue },
                null,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<TrelloCardState> GetCardSyncStateAsync(string cardId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                throw new ArgumentException("Card identifier is required.", nameof(cardId));

            using var doc = await SendAsync(
                HttpMethod.Get,
                $"/cards/{Uri.EscapeDataString(cardId)}",
                new Dictionary<string, string?> { ["fields"] = "due,dueComplete,closed,url" },
                null,
                cancellationToken).ConfigureAwait(false);

            var root = doc.RootElement;
            var url = GetString(root, "url");
            if (string.IsNullOrWhiteSpace(url))
                url = CardUrlBase + cardId;

            var completed = GetBool(root, "dueComplete") || GetBool(root, "closed");
            return new TrelloCardState(completed, ParseDue(GetString(root, "due")), url);
        }

        private async Task<Dictionary<string, string>> GetBoardNamesAsync(CancellationToken cancellationToken)
        {
            var boardNames = new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                using var doc = await SendAsync(
                    HttpMethod.Get,
                    "/members/me/boards",
                    new Dictionary<string, string?> { ["fields"] = "name" },
                    null,
                    cancellationToken).ConfigureAwait(false);

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var id = GetString(item, "id");
                    if (!string.IsNullOrWhiteSpace(id))
                        boardNames[id] = GetString(item, "name");
                }
            }
            catch (TrelloApiException)
            {
                // Board names are cosmetic; cards remain usable without them.
            }
            catch (HttpRequestException)
            {
                // Same as above: tolerate transient network failures.
            }

            return boardNames;
        }

        private async Task AddListNamesForBoardAsync(string boardId, Dictionary<string, string> listNames, CancellationToken cancellationToken)
        {
            try
            {
                using var doc = await SendAsync(
                    HttpMethod.Get,
                    $"/boards/{Uri.EscapeDataString(boardId)}/lists",
                    new Dictionary<string, string?> { ["fields"] = "name" },
                    null,
                    cancellationToken).ConfigureAwait(false);

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var id = GetString(item, "id");
                    if (!string.IsNullOrWhiteSpace(id))
                        listNames[id] = GetString(item, "name");
                }
            }
            catch (TrelloApiException)
            {
                // List names are cosmetic; skip boards that cannot be read.
            }
            catch (HttpRequestException)
            {
                // Same as above: tolerate transient network failures.
            }
        }

        private string BuildUri(string path, IDictionary<string, string?>? query)
        {
            var builder = new StringBuilder(ApiBase);
            if (!path.StartsWith("/", StringComparison.Ordinal))
                builder.Append('/');
            builder.Append(path);

            var separator = path.Contains('?') ? '&' : '?';

            void Append(string key, string? value)
            {
                if (value == null)
                    return;

                builder.Append(separator)
                    .Append(Uri.EscapeDataString(key))
                    .Append('=')
                    .Append(Uri.EscapeDataString(value));
                separator = '&';
            }

            // Credentials travel as query parameters and are never logged.
            Append("key", _apiKey);
            Append("token", _token);

            if (query != null)
            {
                foreach (var pair in query)
                    Append(pair.Key, pair.Value);
            }

            return builder.ToString();
        }

        private async Task<JsonDocument> SendAsync(
            HttpMethod method,
            string path,
            IDictionary<string, string?>? query,
            Func<HttpContent>? contentFactory,
            CancellationToken cancellationToken)
        {
            if (!IsConfigured)
                throw new TrelloApiException("Trello API key and token are not configured.");

            var uri = BuildUri(path, query);

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                using var request = new HttpRequestMessage(method, uri);
                request.Headers.TryAddWithoutValidation("User-Agent", "AIA-Trello-Plugin/1.0");

                if (contentFactory != null)
                    request.Content = contentFactory();

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

                throw new TrelloApiException(BuildErrorMessage(response, text), (int)response.StatusCode);
            }

            throw new TrelloApiException("Trello request failed after retrying.");
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
            var reason = GetTrelloErrorMessage(body);
            var status = (int)response.StatusCode;

            return status switch
            {
                401 => "Trello rejected the API key or token (401 Unauthorized).",
                403 => reason ?? "Trello denied access (403 Forbidden). Check the token's permissions and board access.",
                404 => reason ?? "Trello resource not found (404).",
                429 => reason ?? "Trello rate limit reached (429). Try again shortly.",
                _ => reason ?? $"Trello request failed ({status} {response.ReasonPhrase})."
            };
        }

        private static string? GetTrelloErrorMessage(string body)
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

        private static bool GetBool(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
        }

        private static DateTime? ParseDue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToUniversalTime()
                : null;
        }
    }

    public class TrelloApiException : Exception
    {
        public TrelloApiException(string message) : base(message)
        {
        }

        public TrelloApiException(string message, int statusCode) : base(message)
        {
            StatusCode = statusCode;
        }

        /// <summary>
        /// HTTP status code returned by Trello, when the failure came from an API response.
        /// </summary>
        public int? StatusCode { get; }
    }
}
