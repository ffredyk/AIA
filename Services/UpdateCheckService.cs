using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIA.Services
{
    /// <summary>
    /// Checks the public GitHub releases feed for a newer version. Report-only: nothing is
    /// downloaded or installed automatically.
    /// </summary>
    public static class UpdateCheckService
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/ffredyk/AIA/releases/latest";

        private static readonly HttpClient HttpClient = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AIA-Desktop-App");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var token = Environment.GetEnvironmentVariable("AIA_GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            }

            return client;
        }

        /// <summary>
        /// Returns the latest published tag (without a leading "v") or null when unavailable.
        /// </summary>
        public static async Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await HttpClient.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("tag_name", out var tagElement))
                    return null;

                var tag = tagElement.GetString();
                return string.IsNullOrWhiteSpace(tag) ? null : tag.TrimStart('v', 'V');
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
                return null;
            }
        }

        public static bool IsNewer(string? latestVersion, string currentVersion)
        {
            if (string.IsNullOrWhiteSpace(latestVersion))
                return false;

            if (!Version.TryParse(Normalize(latestVersion), out var latest))
                return false;

            if (!Version.TryParse(Normalize(currentVersion), out var current))
                return false;

            return latest > current;
        }

        private static string Normalize(string version)
        {
            var dash = version.IndexOfAny(new[] { '-', '+' });
            return dash > 0 ? version.Substring(0, dash) : version;
        }
    }
}
