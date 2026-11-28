using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AIA.Sync.Contracts;
using AIA.Sync.Core;

namespace AIA.Mobile.Services
{
    public sealed class HttpSyncTransport : ISyncTransport
    {
        private static readonly HttpClient Client = new()
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        private readonly string _baseUrl;
        private readonly string _token;

        public HttpSyncTransport(string baseUrl, string token)
        {
            _baseUrl = NormalizeBaseUrl(baseUrl);
            _token = token ?? string.Empty;
        }

        public async Task<SyncResponse> SendAsync(SyncRequest request, CancellationToken cancellationToken)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/v1/sync")
            {
                Content = new StringContent(JsonSerializer.Serialize(request, SyncJson.Options), Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            using var response = await Client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(DescribeError(response.StatusCode, body));

            var result = JsonSerializer.Deserialize<SyncResponse>(body, SyncJson.Options);
            return result ?? throw new InvalidOperationException("Server returned an empty sync response.");
        }

        public static async Task<PairResponse> PairAsync(string baseUrl, string code, string deviceName, CancellationToken cancellationToken = default)
        {
            var normalized = NormalizeBaseUrl(baseUrl);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{normalized}/api/v1/pair")
            {
                Content = JsonContent.Create(new PairRequest
                {
                    Code = code,
                    DeviceName = deviceName
                }, options: SyncJson.Options)
            };

            using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(DescribeError(response.StatusCode, body));

            var result = JsonSerializer.Deserialize<PairResponse>(body, SyncJson.Options);
            return result ?? throw new InvalidOperationException("Server returned an empty pairing response.");
        }

        public static string NormalizeBaseUrl(string? value)
        {
            var url = (value ?? string.Empty).Trim().TrimEnd('/');
            if (url.Length == 0)
                return url;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !IsPrivateOrLoopback(uri.Host))
            {
                throw new InvalidOperationException("Use HTTPS for a public server URL.");
            }

            return url;
        }

        private static bool IsPrivateOrLoopback(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!System.Net.IPAddress.TryParse(host, out var address))
                return false;

            if (System.Net.IPAddress.IsLoopback(address))
                return true;

            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4)
                return address.IsIPv6LinkLocal;

            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        private static string DescribeError(System.Net.HttpStatusCode statusCode, string body)
        {
            try
            {
                var error = JsonSerializer.Deserialize<ApiError>(body, SyncJson.Options);
                if (!string.IsNullOrWhiteSpace(error?.Error))
                    return $"{(int)statusCode}: {error.Error}";
            }
            catch
            {
                // Fall back to a short body below.
            }

            if (statusCode == System.Net.HttpStatusCode.Unauthorized)
                return "Unauthorized. Pair the phone again; the token may be revoked.";

            return $"{(int)statusCode}: {body}";
        }
    }
}
