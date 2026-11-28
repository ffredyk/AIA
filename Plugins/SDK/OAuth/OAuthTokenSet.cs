using System;
using System.Text.Json.Serialization;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Represents a set of OAuth 2.0 tokens obtained from an authorization server.
    /// </summary>
    public class OAuthTokenSet
    {
        /// <summary>
        /// Gets or sets the access token used to authorize API requests.
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the refresh token, when the authorization server issued one.
        /// </summary>
        public string? RefreshToken { get; set; }

        /// <summary>
        /// Gets or sets the UTC date and time at which the access token expires.
        /// </summary>
        public DateTimeOffset ExpiresAtUtc { get; set; }

        /// <summary>
        /// Gets or sets the scopes granted to this token set.
        /// </summary>
        public string[] Scopes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the token type reported by the authorization server, typically "Bearer".
        /// </summary>
        public string TokenType { get; set; } = "Bearer";

        /// <summary>
        /// Gets a value indicating whether the access token is expired or will expire within two minutes.
        /// </summary>
        [JsonIgnore]
        public bool IsExpired => ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(2);

        /// <summary>
        /// Creates a deep copy of this token set.
        /// </summary>
        /// <returns>A new <see cref="OAuthTokenSet"/> instance with copied values.</returns>
        public OAuthTokenSet Clone()
        {
            return new OAuthTokenSet
            {
                AccessToken = AccessToken,
                RefreshToken = RefreshToken,
                ExpiresAtUtc = ExpiresAtUtc,
                Scopes = Scopes is null ? Array.Empty<string>() : (string[])Scopes.Clone(),
                TokenType = TokenType
            };
        }
    }
}
