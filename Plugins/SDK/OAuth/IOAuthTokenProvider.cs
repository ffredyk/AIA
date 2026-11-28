using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Provides access to OAuth tokens for a plugin, including cached tokens and interactive sign-in.
    /// </summary>
    public interface IOAuthTokenProvider
    {
        /// <summary>
        /// Gets a value indicating whether the provider currently has a usable token set.
        /// </summary>
        bool IsSignedIn { get; }

        /// <summary>
        /// Gets the scopes granted by the most recent authorization.
        /// </summary>
        IReadOnlyCollection<string> GrantedScopes { get; }

        /// <summary>
        /// Gets an access token that covers the required scopes, refreshing or prompting for sign-in when needed.
        /// </summary>
        /// <param name="requiredScopes">Scopes that the returned access token must cover.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The access token, or <see langword="null"/> when no token could be obtained.</returns>
        Task<string?> GetAccessTokenAsync(IEnumerable<string> requiredScopes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Occurs when the user signs out or the cached credentials are cleared.
        /// </summary>
        event EventHandler? SignedOut;
    }
}
