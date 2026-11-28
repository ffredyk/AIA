using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Persists OAuth tokens in plugin settings.
    /// </summary>
    public class OAuthTokenStore
    {
        private const string DefaultSettingsKey = "oauth.tokens";

        private readonly IPluginSettingsStorage _storage;
        private readonly string _settingsKey;

        /// <summary>
        /// Initializes a new instance of the <see cref="OAuthTokenStore"/> class.
        /// </summary>
        /// <param name="storage">Plugin settings storage used to persist tokens.</param>
        /// <param name="settingsKey">Settings key under which the token JSON is stored.</param>
        public OAuthTokenStore(IPluginSettingsStorage storage, string settingsKey = DefaultSettingsKey)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _settingsKey = string.IsNullOrWhiteSpace(settingsKey) ? DefaultSettingsKey : settingsKey;
        }

        /// <summary>
        /// Gets the most recently loaded or saved token set.
        /// </summary>
        public OAuthTokenSet? Current { get; private set; }

        /// <summary>
        /// Loads the stored token set.
        /// </summary>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>The stored token set, or <see langword="null"/> when none is stored or the stored value is invalid.</returns>
        public Task<OAuthTokenSet?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var json = _storage.Get<string?>(_settingsKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                Current = null;
                return Task.FromResult<OAuthTokenSet?>(null);
            }

            try
            {
                var tokens = JsonSerializer.Deserialize<OAuthTokenSet>(json);
                Current = tokens;
                return Task.FromResult(tokens);
            }
            catch (JsonException)
            {
                Current = null;
                return Task.FromResult<OAuthTokenSet?>(null);
            }
        }

        /// <summary>
        /// Saves a token set to plugin settings.
        /// </summary>
        /// <param name="tokens">The token set to save.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        public async Task SaveAsync(OAuthTokenSet tokens, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(tokens);
            cancellationToken.ThrowIfCancellationRequested();

            var json = JsonSerializer.Serialize(tokens);
            _storage.Set(_settingsKey, json);
            await _storage.SaveAsync().ConfigureAwait(false);
            Current = tokens;
        }

        /// <summary>
        /// Clears the stored token set.
        /// </summary>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _storage.Set<string>(_settingsKey, string.Empty);
            await _storage.SaveAsync().ConfigureAwait(false);
            Current = null;
        }
    }
}
