using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Describes an OAuth 2.0 client that uses the authorization-code flow with PKCE.
    /// </summary>
    /// <param name="AuthorizationEndpoint">The authorization server's authorization endpoint.</param>
    /// <param name="TokenEndpoint">The authorization server's token endpoint.</param>
    /// <param name="ClientId">The OAuth client identifier.</param>
    /// <param name="ClientSecret">The client secret, or <see langword="null"/> for public clients.</param>
    /// <param name="RedirectPath">The loopback callback path. Defaults to <c>/oauth2/callback</c>.</param>
    /// <param name="PreferredPort">A fixed loopback port, or zero to search the default range.</param>
    public record OAuthClientConfig(
        string AuthorizationEndpoint,
        string TokenEndpoint,
        string ClientId,
        string? ClientSecret,
        string RedirectPath = "/oauth2/callback",
        int PreferredPort = 0);

    /// <summary>
    /// Contains the result of an OAuth authorization-code exchange.
    /// </summary>
    /// <param name="Tokens">The tokens returned by the authorization server.</param>
    /// <param name="RawResponseJson">The raw JSON token response.</param>
    public record OAuthAuthorizationResult(OAuthTokenSet Tokens, string RawResponseJson);

    /// <summary>
    /// Implements an OAuth 2.0 authorization-code flow with PKCE (S256) and a loopback redirect URI.
    /// </summary>
    public class OAuthPkceFlow
    {
        private const int FirstLoopbackPort = 53682;
        private const int LastLoopbackPort = 53692;
        private const string DefaultTokenType = "Bearer";

        private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(5);

        private readonly HttpClient _httpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="OAuthPkceFlow"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client used for token requests.</param>
        public OAuthPkceFlow(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Creates a cryptographically random PKCE code verifier.
        /// </summary>
        /// <returns>A base64url-encoded verifier between 43 and 128 characters long.</returns>
        public static string CreateCodeVerifier()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        /// <summary>
        /// Creates the S256 PKCE code challenge for a verifier.
        /// </summary>
        /// <param name="verifier">The code verifier.</param>
        /// <returns>The base64url-encoded SHA-256 hash of the verifier, without padding.</returns>
        public static string CreateCodeChallenge(string verifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(verifier);
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier), hash);
            return Base64UrlEncode(hash);
        }

        /// <summary>
        /// Builds an authorization URL for the authorization-code flow.
        /// </summary>
        /// <param name="config">The OAuth client configuration.</param>
        /// <param name="scopes">The scopes to request.</param>
        /// <param name="redirectUri">The loopback redirect URI.</param>
        /// <param name="state">The opaque state value used to validate the callback.</param>
        /// <param name="codeChallenge">The PKCE S256 code challenge.</param>
        /// <param name="extraParameters">Optional additional query parameters; values replace defaults with the same key.</param>
        /// <returns>The full authorization URL.</returns>
        public static string BuildAuthorizationUrl(
            OAuthClientConfig config,
            IEnumerable<string> scopes,
            string redirectUri,
            string state,
            string codeChallenge,
            IReadOnlyDictionary<string, string>? extraParameters = null)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(scopes);
            ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
            ArgumentException.ThrowIfNullOrWhiteSpace(state);
            ArgumentException.ThrowIfNullOrWhiteSpace(codeChallenge);

            var parameters = new List<KeyValuePair<string, string>>
            {
                new("response_type", "code"),
                new("client_id", config.ClientId),
                new("redirect_uri", redirectUri),
                new("scope", string.Join(" ", scopes.Where(static scope => !string.IsNullOrWhiteSpace(scope)))),
                new("state", state),
                new("code_challenge", codeChallenge),
                new("code_challenge_method", "S256"),
                new("access_type", "offline"),
                new("include_granted_scopes", "true"),
                new("prompt", "consent")
            };

            if (extraParameters is not null)
            {
                foreach (var parameter in extraParameters)
                {
                    parameters.RemoveAll(existing => string.Equals(existing.Key, parameter.Key, StringComparison.OrdinalIgnoreCase));
                    parameters.Add(new KeyValuePair<string, string>(parameter.Key, parameter.Value));
                }
            }

            var query = string.Join("&", parameters.Select(static parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
            var separator = config.AuthorizationEndpoint.IndexOf('?') >= 0 ? '&' : '?';
            return config.AuthorizationEndpoint + separator + query;
        }

        /// <summary>
        /// Runs the interactive authorization-code flow, opening the system browser and waiting for the loopback callback.
        /// </summary>
        /// <param name="config">The OAuth client configuration.</param>
        /// <param name="scopes">The scopes to request.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The tokens and raw JSON token response.</returns>
        public async Task<OAuthAuthorizationResult> AuthorizeAsync(
            OAuthClientConfig config,
            IEnumerable<string> scopes,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(scopes);
            ValidateConfig(config);
            cancellationToken.ThrowIfCancellationRequested();

            var requestedScopes = scopes.Where(static scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var (listener, port) = CreateLoopbackListener(config);
            try
            {
                var redirectPath = NormalizeRedirectPath(config.RedirectPath);
                var redirectUri = $"http://127.0.0.1:{port}{redirectPath}";
                var state = CreateRandomToken();
                var verifier = CreateCodeVerifier();
                var challenge = CreateCodeChallenge(verifier);
                var authorizationUrl = BuildAuthorizationUrl(config, requestedScopes, redirectUri, state, challenge);

                StartBrowser(authorizationUrl);

                var context = await WaitForCallbackAsync(listener, redirectPath, cancellationToken).ConfigureAwait(false);
                var parameters = ParseQuery(context.Request.Url?.Query);

                if (!parameters.TryGetValue("state", out var returnedState) ||
                    !string.Equals(returnedState, state, StringComparison.Ordinal))
                {
                    await TryWriteHtmlAsync(context, 400, "Sign-in failed: state validation failed.").ConfigureAwait(false);
                    throw new InvalidOperationException("OAuth state validation failed.");
                }

                if (parameters.TryGetValue("error", out var error))
                {
                    parameters.TryGetValue("error_description", out var description);
                    await TryWriteHtmlAsync(context, 400, "Sign-in failed. Return to the application.").ConfigureAwait(false);
                    var detail = string.IsNullOrWhiteSpace(description) ? error : $"{error} - {description}";
                    throw new InvalidOperationException($"OAuth authorization failed: {detail}");
                }

                if (!parameters.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                {
                    await TryWriteHtmlAsync(context, 400, "Sign-in failed: no authorization code was returned.").ConfigureAwait(false);
                    throw new InvalidOperationException("OAuth callback did not include an authorization code.");
                }

                try
                {
                    var result = await ExchangeCodeAsync(config, code, redirectUri, verifier, requestedScopes, cancellationToken).ConfigureAwait(false);
                    await TryWriteHtmlAsync(context, 200, "Sign-in complete. You can close this window.").ConfigureAwait(false);
                    return result;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await TryWriteHtmlAsync(context, 500, "Sign-in failed. Return to the application.").ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                listener.Close();
            }
        }

        /// <summary>
        /// Refreshes an access token using the refresh token in <paramref name="current"/>.
        /// </summary>
        /// <param name="config">The OAuth client configuration.</param>
        /// <param name="current">The current token set, which must contain a refresh token.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The refreshed token set.</returns>
        public async Task<OAuthTokenSet> RefreshAsync(
            OAuthClientConfig config,
            OAuthTokenSet current,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(current);
            ValidateConfig(config);

            if (string.IsNullOrWhiteSpace(current.RefreshToken))
            {
                throw new InvalidOperationException("Cannot refresh OAuth tokens because no refresh token is available.");
            }

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = config.ClientId,
                ["refresh_token"] = current.RefreshToken
            };
            if (!string.IsNullOrEmpty(config.ClientSecret))
            {
                form["client_secret"] = config.ClientSecret;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, config.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(form)
            };
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateTokenRequestException(response.StatusCode, rawResponse, "token refresh");
            }

            return ParseTokenResponse(rawResponse, current.Scopes, current.RefreshToken, current.Scopes);
        }

        private async Task<OAuthAuthorizationResult> ExchangeCodeAsync(
            OAuthClientConfig config,
            string code,
            string redirectUri,
            string verifier,
            IEnumerable<string> requestedScopes,
            CancellationToken cancellationToken)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = config.ClientId,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = verifier
            };
            if (!string.IsNullOrEmpty(config.ClientSecret))
            {
                form["client_secret"] = config.ClientSecret;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, config.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(form)
            };
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateTokenRequestException(response.StatusCode, rawResponse, "authorization code exchange");
            }

            var tokens = ParseTokenResponse(rawResponse, requestedScopes, previousRefreshToken: null, previousScopes: null);
            return new OAuthAuthorizationResult(tokens, rawResponse);
        }

        private static OAuthTokenSet ParseTokenResponse(
            string rawJson,
            IEnumerable<string>? fallbackScopes,
            string? previousRefreshToken,
            IEnumerable<string>? previousScopes)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(rawJson);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("The OAuth token response was not valid JSON.", ex);
            }

            using (document)
            {
                var root = document.RootElement;
                var accessToken = GetString(root, "access_token");
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    throw new InvalidOperationException("The OAuth token response did not include an access token.");
                }

                var refreshToken = GetString(root, "refresh_token");
                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    refreshToken = previousRefreshToken;
                }

                var expiresIn = GetInt64(root, "expires_in") ?? 3600;
                var scopes = ParseScopes(GetString(root, "scope"));
                if (scopes.Length == 0)
                {
                    scopes = (fallbackScopes ?? Array.Empty<string>()).ToArray();
                }
                else if (previousScopes is not null)
                {
                    scopes = MergeScopes(previousScopes, scopes);
                }

                var tokenType = GetString(root, "token_type");
                return new OAuthTokenSet
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
                    Scopes = scopes,
                    TokenType = string.IsNullOrWhiteSpace(tokenType) ? DefaultTokenType : tokenType
                };
            }
        }

        private static string? GetString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        }

        private static long? GetInt64(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element))
            {
                return null;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number))
            {
                return number;
            }

            if (element.ValueKind == JsonValueKind.String &&
                long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static string[] ParseScopes(string? scope)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                return Array.Empty<string>();
            }

            return scope.Split(
                    new[] { ' ', ',', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] MergeScopes(IEnumerable<string> existingScopes, IEnumerable<string> updatedScopes)
        {
            var merged = new List<string>();
            foreach (var scope in updatedScopes)
            {
                if (!string.IsNullOrWhiteSpace(scope) && !merged.Contains(scope, StringComparer.Ordinal))
                {
                    merged.Add(scope);
                }
            }

            foreach (var scope in existingScopes)
            {
                if (!string.IsNullOrWhiteSpace(scope) && !merged.Contains(scope, StringComparer.Ordinal))
                {
                    merged.Add(scope);
                }
            }

            return merged.ToArray();
        }

        private static (HttpListener Listener, int Port) CreateLoopbackListener(OAuthClientConfig config)
        {
            Exception? lastError = null;

            if (config.PreferredPort > 0)
            {
                var listener = TryStartListener(config.PreferredPort, out lastError);
                if (listener is not null)
                {
                    return (listener, config.PreferredPort);
                }

                throw new InvalidOperationException($"Unable to bind the OAuth callback port {config.PreferredPort}.", lastError);
            }

            for (var port = FirstLoopbackPort; port <= LastLoopbackPort; port++)
            {
                var listener = TryStartListener(port, out lastError);
                if (listener is not null)
                {
                    return (listener, port);
                }
            }

            throw new InvalidOperationException("Unable to bind a loopback port for the OAuth callback.", lastError);
        }

        private static HttpListener? TryStartListener(int port, out Exception? error)
        {
            error = null;
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                listener.Start();
                return listener;
            }
            catch (Exception ex) when (ex is HttpListenerException or PlatformNotSupportedException)
            {
                error = ex;
                listener.Close();
                return null;
            }
        }

        private static async Task<HttpListenerContext> WaitForCallbackAsync(
            HttpListener listener,
            string expectedPath,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CallbackTimeout);

            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Timed out waiting for the OAuth sign-in callback.");
                }

                if (string.Equals(context.Request.Url?.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return context;
                }

                await TryWriteHtmlAsync(context, 404, "Not found.").ConfigureAwait(false);
            }
        }

        private static Dictionary<string, string> ParseQuery(string? query)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(query))
            {
                return values;
            }

            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = part.IndexOf('=');
                var key = separatorIndex >= 0 ? part[..separatorIndex] : part;
                var value = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : string.Empty;
                values[Unescape(key)] = Unescape(value);
            }

            return values;
        }

        private static string Unescape(string value)
        {
            try
            {
                return Uri.UnescapeDataString(value.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                return value;
            }
        }

        private static string NormalizeRedirectPath(string redirectPath)
        {
            if (string.IsNullOrWhiteSpace(redirectPath))
            {
                return "/";
            }

            redirectPath = redirectPath.Trim();
            return redirectPath[0] == '/' ? redirectPath : "/" + redirectPath;
        }

        private static string CreateRandomToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static void StartBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
            {
                throw new InvalidOperationException("Unable to open the system browser for OAuth sign-in.", ex);
            }
        }

        private static async Task TryWriteHtmlAsync(HttpListenerContext context, int statusCode, string message)
        {
            try
            {
                var html = $"""
                    <!DOCTYPE html>
                    <html lang="en">
                    <head><meta charset="utf-8" /><title>AIA sign-in</title></head>
                    <body style="font-family: system-ui, sans-serif; text-align: center; margin-top: 4rem;">
                    <p>{WebUtility.HtmlEncode(message)}</p>
                    </body>
                    </html>
                    """;
                var buffer = Encoding.UTF8.GetBytes(html);
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
            {
                // The browser may have closed the tab; there is nothing else to do.
            }
        }

        private static InvalidOperationException CreateTokenRequestException(
            HttpStatusCode statusCode,
            string rawResponse,
            string operation)
        {
            var detail = string.Empty;
            try
            {
                using var document = JsonDocument.Parse(rawResponse);
                var root = document.RootElement;
                var error = GetString(root, "error");
                var description = GetString(root, "error_description");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    detail = string.IsNullOrWhiteSpace(description)
                        ? $": {error}"
                        : $": {error} - {description}";
                }
            }
            catch (JsonException)
            {
                // Keep the message free of tokens and client secrets when the body is not JSON.
            }

            return new InvalidOperationException(
                $"OAuth {operation} failed with HTTP {(int)statusCode} ({statusCode}){detail}.");
        }

        private static void ValidateConfig(OAuthClientConfig config)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(config.AuthorizationEndpoint);
            ArgumentException.ThrowIfNullOrWhiteSpace(config.TokenEndpoint);
            ArgumentException.ThrowIfNullOrWhiteSpace(config.ClientId);

            if (!Uri.TryCreate(config.AuthorizationEndpoint, UriKind.Absolute, out _))
            {
                throw new ArgumentException("The authorization endpoint must be an absolute URI.", nameof(config));
            }

            if (!Uri.TryCreate(config.TokenEndpoint, UriKind.Absolute, out _))
            {
                throw new ArgumentException("The token endpoint must be an absolute URI.", nameof(config));
            }
        }
    }
}
