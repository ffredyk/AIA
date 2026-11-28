using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AIA.Plugins.SDK;

using WpfApplication = System.Windows.Application;

namespace AIA.Plugins.GoogleAccount
{
    /// <summary>
    /// Google Account provider plugin. Signs in once with Google and exposes an
    /// <see cref="IOAuthTokenProvider"/> that other plugins can consume for Gmail,
    /// Calendar and Google Chat access.
    /// </summary>
    [Plugin("AIA.GoogleAccount", "Google Account", IconSymbol = "PersonKey20", IsBuiltIn = true)]
    public class GoogleAccountPlugin : PluginBase
    {
        /// <summary>
        /// Service name under which the Google OAuth provider is registered.
        /// </summary>
        public const string OAuthServiceName = "AIA.GoogleOAuth";

        private const string TabId = "google-account";
        private const string ToolbarButtonId = "open-google-account";

        private GoogleOAuthProvider? _provider;
        private GoogleAccountTabViewModel? _tabViewModel;

        public override string Id => "AIA.GoogleAccount";
        public override string Name => "Google Account";
        public override string Description => "Signs in to Google once and shares Gmail, Calendar and Google Chat access with other plugins";
        public override Version Version => new Version(1, 0, 0);
        public override string Author => "AIA";

        public override PluginPermissions RequiredPermissions =>
            PluginPermissions.Network |
            PluginPermissions.UI |
            PluginPermissions.PluginServices;

        protected override async Task OnInitializeAsync()
        {
            LogInfo("Initializing Google Account plugin...");
            await Context.Settings.LoadAsync();

            var tokenStore = new OAuthTokenStore(Context.Settings);
            _provider = new GoogleOAuthProvider(Context, tokenStore);

            // The provider is registered even while signed out so consumers can
            // discover it and surface their own setup state.
            Context.Services.Register<IOAuthTokenProvider>(OAuthServiceName, _provider);

            _tabViewModel = new GoogleAccountTabViewModel(Context, _provider);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = TabId,
                Title = "Google",
                IconSymbol = "PersonKey20",
                Order = 100,
                ViewModel = _tabViewModel,
                BadgeColor = "#FF4285F4"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = ToolbarButtonId,
                Text = "Google Account",
                IconSymbol = "PersonKey20",
                Tooltip = "Open Google Account",
                Order = 100,
                OnClick = () => Context.UI.RefreshUI()
            });

            LogInfo("Google Account plugin initialized");
        }

        protected override async Task OnStartAsync()
        {
            LogInfo("Starting Google Account plugin...");

            // No network calls here: only re-read the locally stored sign-in state.
            if (_tabViewModel != null)
            {
                await _tabViewModel.RefreshAsync();
            }

            LogInfo("Google Account plugin started");
        }

        protected override void OnDispose()
        {
            Context.UI.UnregisterTab(TabId);
            Context.UI.UnregisterToolbarButton(ToolbarButtonId);

            _tabViewModel?.Dispose();
            _tabViewModel = null;

            _provider?.Dispose();
            _provider = null;
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new GoogleAccountSettingsViewModel(Context.Settings);
        }
    }

    /// <summary>
    /// Scope constants and helpers for the Google OAuth provider.
    /// </summary>
    public static class GoogleAccountScopes
    {
        public const string OpenId = "openid";
        public const string Email = "email";
        public const string Profile = "profile";

        public const string GmailReadonly = "https://www.googleapis.com/auth/gmail.readonly";
        public const string GmailModify = "https://www.googleapis.com/auth/gmail.modify";

        public const string CalendarReadonly = "https://www.googleapis.com/auth/calendar.readonly";
        public const string CalendarEvents = "https://www.googleapis.com/auth/calendar.events";

        public const string ChatSpacesReadonly = "https://www.googleapis.com/auth/chat.spaces.readonly";
        public const string ChatMessagesReadonly = "https://www.googleapis.com/auth/chat.messages.readonly";
        public const string ChatMessagesCreate = "https://www.googleapis.com/auth/chat.messages.create";

        /// <summary>
        /// Builds the scope set for the enabled Google services. Sign-in scopes are always included.
        /// </summary>
        /// <param name="gmail">Whether Gmail scopes are requested.</param>
        /// <param name="calendar">Whether Calendar scopes are requested.</param>
        /// <param name="chat">Whether Google Chat scopes are requested.</param>
        /// <returns>The distinct scopes to request.</returns>
        public static string[] Build(bool gmail, bool calendar, bool chat)
        {
            var scopes = new List<string> { OpenId, Email, Profile };

            if (gmail)
            {
                scopes.Add(GmailReadonly);
                scopes.Add(GmailModify);
            }

            if (calendar)
            {
                scopes.Add(CalendarReadonly);
                scopes.Add(CalendarEvents);
            }

            if (chat)
            {
                scopes.Add(ChatSpacesReadonly);
                scopes.Add(ChatMessagesReadonly);
                scopes.Add(ChatMessagesCreate);
            }

            return scopes.Distinct(StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Produces a short human-readable label for a granted scope.
        /// </summary>
        /// <param name="scope">The scope identifier.</param>
        /// <returns>A friendly label, or the original scope when it is unknown.</returns>
        public static string Describe(string scope)
        {
            return scope switch
            {
                OpenId or Email or Profile => "Sign-in",
                GmailReadonly or GmailModify => "Gmail",
                CalendarReadonly or CalendarEvents => "Calendar",
                ChatSpacesReadonly or ChatMessagesReadonly or ChatMessagesCreate => "Google Chat",
                _ => scope
            };
        }
    }

    /// <summary>
    /// Google OAuth 2.0 token provider backed by <see cref="OAuthTokenStore"/>.
    /// </summary>
    public class GoogleOAuthProvider : IOAuthTokenProvider, IDisposable
    {
        public const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

        private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
        private const string GrantedScopesSettingsKey = "GrantedScopes";
        private const string AccountEmailSettingsKey = "AccountEmail";

        private readonly IPluginContext _context;
        private readonly OAuthTokenStore _store;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private OAuthTokenSet? _current;
        private string[] _storedGrantedScopes = Array.Empty<string>();
        private string? _accountEmail;
        private string? _accountName;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="GoogleOAuthProvider"/> class.
        /// </summary>
        /// <param name="context">The plugin context.</param>
        /// <param name="store">The encrypted token store.</param>
        public GoogleOAuthProvider(IPluginContext context, OAuthTokenStore store)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Gets or sets the OAuth client identifier configured by the user.
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the OAuth client secret configured by the user. Never logged.
        /// </summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the scopes implied by the services the user enabled.
        /// </summary>
        public string[] EnabledScopes { get; set; } = Array.Empty<string>();

        /// <inheritdoc />
        public bool IsSignedIn
        {
            get
            {
                var tokens = _current;
                return tokens != null &&
                       (!string.IsNullOrWhiteSpace(tokens.AccessToken) || !string.IsNullOrWhiteSpace(tokens.RefreshToken));
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> GrantedScopes
        {
            get
            {
                var scopes = new List<string>();
                var tokens = _current;
                if (tokens?.Scopes != null)
                {
                    foreach (var scope in tokens.Scopes)
                    {
                        if (!string.IsNullOrWhiteSpace(scope) && !scopes.Contains(scope, StringComparer.Ordinal))
                        {
                            scopes.Add(scope);
                        }
                    }
                }

                foreach (var scope in _storedGrantedScopes)
                {
                    if (!string.IsNullOrWhiteSpace(scope) && !scopes.Contains(scope, StringComparer.Ordinal))
                    {
                        scopes.Add(scope);
                    }
                }

                return scopes;
            }
        }

        /// <summary>
        /// Gets the email address of the signed-in Google account, when known.
        /// </summary>
        public string? AccountEmail => _accountEmail;

        /// <summary>
        /// Gets the display name of the signed-in Google account, when known.
        /// </summary>
        public string? AccountName => _accountName;

        /// <summary>
        /// Gets a value indicating whether an OAuth client ID has been configured.
        /// </summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

        /// <inheritdoc />
        public event EventHandler? SignedOut;

        /// <summary>
        /// Occurs after a successful interactive sign-in.
        /// </summary>
        public event EventHandler? SignedIn;

        /// <summary>
        /// Loads the stored token set and account metadata. Performs no network calls.
        /// </summary>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        public async Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _current = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
                _storedGrantedScopes = _context.Settings.Get<string[]?>(GrantedScopesSettingsKey) ?? Array.Empty<string>();
                _accountEmail = _context.Settings.Get<string?>(AccountEmailSettingsKey);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Runs the interactive Google sign-in flow, stores the tokens and reads the account profile.
        /// </summary>
        /// <param name="scopes">Scopes to request; falls back to <see cref="EnabledScopes"/> when empty.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        public async Task SignInAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scopes);

            if (string.IsNullOrWhiteSpace(ClientId))
            {
                throw new InvalidOperationException("A Google OAuth client ID is required before signing in.");
            }

            if (string.IsNullOrWhiteSpace(ClientSecret))
            {
                throw new InvalidOperationException("A Google OAuth client secret is required before signing in.");
            }

            var requestedScopes = scopes
                .Where(static scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (requestedScopes.Length == 0)
            {
                requestedScopes = EnabledScopes
                    .Where(static scope => !string.IsNullOrWhiteSpace(scope))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var httpClient = _context.HttpClientFactory.CreateClient();
                var flow = new OAuthPkceFlow(httpClient);
                var result = await flow.AuthorizeAsync(CreateClientConfig(), requestedScopes, cancellationToken).ConfigureAwait(false);

                var tokens = result.Tokens;
                if (tokens.Scopes.Length == 0)
                {
                    tokens.Scopes = requestedScopes;
                }

                await _store.SaveAsync(tokens, cancellationToken).ConfigureAwait(false);
                _current = tokens;
                _storedGrantedScopes = MergeScopes(_storedGrantedScopes, tokens.Scopes);
                _context.Settings.Set(GrantedScopesSettingsKey, _storedGrantedScopes);

                var profile = await TryFetchProfileAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(profile.Email))
                {
                    _accountEmail = profile.Email;
                    _context.Settings.Set(AccountEmailSettingsKey, profile.Email);
                }

                if (!string.IsNullOrWhiteSpace(profile.Name))
                {
                    _accountName = profile.Name;
                }

                await _context.Settings.SaveAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            _context.Logger.Info("Google Account signed in.");
            SignedIn?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Clears the stored tokens and notifies consumers that the account is gone.
        /// </summary>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        public async Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _store.ClearAsync(cancellationToken).ConfigureAwait(false);
                _current = null;
                _storedGrantedScopes = Array.Empty<string>();
                _accountEmail = null;
                _accountName = null;

                _context.Settings.Set(GrantedScopesSettingsKey, Array.Empty<string>());
                _context.Settings.Set(AccountEmailSettingsKey, string.Empty);
                await _context.Settings.SaveAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            _context.Logger.Info("Google Account signed out.");
            SignedOut?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc />
        public async Task<string?> GetAccessTokenAsync(IEnumerable<string> requiredScopes, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                return null;
            }

            var required = (requiredScopes ?? Array.Empty<string>())
                .Where(static scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var tokens = _current ?? await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
                _current = tokens;

                if (tokens == null ||
                    (string.IsNullOrWhiteSpace(tokens.AccessToken) && string.IsNullOrWhiteSpace(tokens.RefreshToken)))
                {
                    return null;
                }

                if (required.Length > 0)
                {
                    var granted = GrantedScopes;
                    var missing = required.FirstOrDefault(scope => !granted.Contains(scope, StringComparer.Ordinal));
                    if (missing != null)
                    {
                        _context.Logger.Warning($"Google OAuth token does not cover the required scope '{missing}'.");
                        return null;
                    }
                }

                if (tokens.IsExpired)
                {
                    if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
                    {
                        _context.Logger.Warning("Google OAuth access token expired and no refresh token is available.");
                        return null;
                    }

                    try
                    {
                        using var httpClient = _context.HttpClientFactory.CreateClient();
                        var flow = new OAuthPkceFlow(httpClient);
                        var refreshed = await flow.RefreshAsync(CreateClientConfig(), tokens, cancellationToken).ConfigureAwait(false);

                        await _store.SaveAsync(refreshed, cancellationToken).ConfigureAwait(false);
                        _current = refreshed;
                        _storedGrantedScopes = MergeScopes(_storedGrantedScopes, refreshed.Scopes);
                        tokens = refreshed;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Expected auth failure: report it through the log and let consumers show setup state.
                        _context.Logger.Warning($"Google OAuth token refresh failed: {ex.Message}");
                        return null;
                    }
                }

                return string.IsNullOrWhiteSpace(tokens.AccessToken) ? null : tokens.AccessToken;
            }
            finally
            {
                _gate.Release();
            }
        }

        private OAuthClientConfig CreateClientConfig()
        {
            return new OAuthClientConfig(
                AuthorizationEndpoint,
                TokenEndpoint,
                ClientId.Trim(),
                string.IsNullOrWhiteSpace(ClientSecret) ? null : ClientSecret.Trim(),
                RedirectPath: "/");
        }

        private async Task<(string? Email, string? Name)> TryFetchProfileAsync(string accessToken, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return (null, null);
            }

            try
            {
                using var httpClient = _context.HttpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _context.Logger.Warning($"Google user info request failed with status {(int)response.StatusCode}.");
                    return (null, null);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                var email = root.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null;
                var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                return (email, name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _context.Logger.Warning($"Unable to read the Google account profile: {ex.Message}");
                return (null, null);
            }
        }

        private static string[] MergeScopes(IEnumerable<string> existingScopes, IEnumerable<string>? updatedScopes)
        {
            var merged = new List<string>();

            if (updatedScopes != null)
            {
                foreach (var scope in updatedScopes)
                {
                    if (!string.IsNullOrWhiteSpace(scope) && !merged.Contains(scope, StringComparer.Ordinal))
                    {
                        merged.Add(scope);
                    }
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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _gate.Dispose();
        }
    }

    /// <summary>
    /// View model for the Google Account tab.
    /// </summary>
    public class GoogleAccountTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private const string ClientIdKey = "ClientId";
        private const string ClientSecretKey = "ClientSecret";
        private const string EnableGmailKey = "EnableGmail";
        private const string EnableCalendarKey = "EnableCalendar";
        private const string EnableChatKey = "EnableChat";

        private static readonly Brush AccentBrush = CreateBrush(0xFF, 0x42, 0x85, 0xF4);
        private static readonly Brush NeutralBrush = CreateBrush(0x33, 0xFF, 0xFF, 0xFF);
        private static readonly Brush SubtleTextBrush = CreateBrush(0x99, 0xFF, 0xFF, 0xFF);
        private static readonly Brush CardBrush = CreateBrush(0x14, 0x42, 0x85, 0xF4);

        private readonly IPluginContext _context;
        private readonly GoogleOAuthProvider _provider;
        private readonly RelayCommand _saveConfigCommand;
        private readonly RelayCommand _signInCommand;
        private readonly RelayCommand _signOutCommand;
        private readonly RelayCommand _refreshCommand;

        private DataTemplate? _cachedTemplate;
        private string _clientId = string.Empty;
        private string _clientSecret = string.Empty;
        private string _statusMessage = string.Empty;
        private string _grantedScopesText = string.Empty;
        private string _accountEmail = string.Empty;
        private bool _enableGmail = true;
        private bool _enableCalendar = true;
        private bool _enableChat = true;
        private bool _isBusy;
        private bool _isSignedIn;
        private bool _disposed;

        public GoogleAccountTabViewModel(IPluginContext context, GoogleOAuthProvider provider)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));

            _saveConfigCommand = new RelayCommand(SaveConfigAsync, () => !IsBusy);
            _signInCommand = new RelayCommand(SignInAsync, () => !IsBusy && CanSignIn);
            _signOutCommand = new RelayCommand(SignOutAsync, () => !IsBusy && _provider.IsSignedIn);
            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsBusy);

            _provider.SignedOut += OnProviderStateChanged;
            _provider.SignedIn += OnProviderStateChanged;
        }

        /// <summary>
        /// Gets or sets the OAuth client ID. The secret is never displayed after entry.
        /// </summary>
        public string ClientId
        {
            get => _clientId;
            set
            {
                if (SetProperty(ref _clientId, value))
                {
                    RefreshStatus();
                }
            }
        }

        /// <summary>
        /// Gets or sets the OAuth client secret. The value is stored in encrypted plugin settings and never logged.
        /// </summary>
        public string ClientSecret
        {
            get => _clientSecret;
            set
            {
                if (SetProperty(ref _clientSecret, value))
                {
                    RefreshStatus();
                }
            }
        }

        public bool EnableGmail
        {
            get => _enableGmail;
            set
            {
                if (SetProperty(ref _enableGmail, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        public bool EnableCalendar
        {
            get => _enableCalendar;
            set
            {
                if (SetProperty(ref _enableCalendar, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        public bool EnableChat
        {
            get => _enableChat;
            set
            {
                if (SetProperty(ref _enableChat, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        public bool IsSignedIn
        {
            get => _isSignedIn;
            private set => SetProperty(ref _isSignedIn, value);
        }

        public string AccountEmail
        {
            get => _accountEmail;
            private set => SetProperty(ref _accountEmail, value);
        }

        public string GrantedScopesText
        {
            get => _grantedScopesText;
            private set => SetProperty(ref _grantedScopesText, value);
        }

        public System.Windows.Input.ICommand SaveConfigCommand => _saveConfigCommand;
        public System.Windows.Input.ICommand SignInCommand => _signInCommand;
        public System.Windows.Input.ICommand SignOutCommand => _signOutCommand;
        public System.Windows.Input.ICommand RefreshCommand => _refreshCommand;

        private bool HasCredentials =>
            !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

        private bool HasAnyService => EnableGmail || EnableCalendar || EnableChat;

        private bool CanSignIn => HasCredentials && HasAnyService;

        /// <summary>
        /// Loads the persisted configuration and sign-in state.
        /// </summary>
        public async Task InitializeAsync()
        {
            ClientId = _context.Settings.Get(ClientIdKey, string.Empty) ?? string.Empty;
            ClientSecret = _context.Settings.Get(ClientSecretKey, string.Empty) ?? string.Empty;
            EnableGmail = _context.Settings.Get(EnableGmailKey, true);
            EnableCalendar = _context.Settings.Get(EnableCalendarKey, true);
            EnableChat = _context.Settings.Get(EnableChatKey, true);

            ApplyProviderConfiguration();

            try
            {
                await _provider.ReloadAsync();
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"Unable to read Google Account sign-in state: {ex.Message}");
            }

            RefreshStatus();
        }

        /// <inheritdoc />
        public override DataTemplate GetDataTemplate()
        {
            _cachedTemplate ??= CreateDataTemplate();
            return _cachedTemplate;
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            _ = RefreshAsync();
        }

        /// <inheritdoc />
        public override void OnActivated()
        {
            _ = RefreshAsync();
        }

        /// <summary>
        /// Reloads the sign-in state from storage and refreshes the status text.
        /// </summary>
        public async Task RefreshAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                ApplyProviderConfiguration();
                await _provider.ReloadAsync();
                RunOnUi(RefreshStatus);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Unable to refresh Google Account status: {ex.Message}", ex);
                RunOnUi(() => StatusMessage = ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _provider.SignedOut -= OnProviderStateChanged;
            _provider.SignedIn -= OnProviderStateChanged;
        }

        private void OnProviderStateChanged(object? sender, EventArgs e)
        {
            RunOnUi(() =>
            {
                RefreshStatus();
                RefreshCommandStates();
            });
        }

        private void ApplyProviderConfiguration()
        {
            _provider.ClientId = ClientId.Trim();
            _provider.ClientSecret = ClientSecret.Trim();
            _provider.EnabledScopes = GoogleAccountScopes.Build(EnableGmail, EnableCalendar, EnableChat);
        }

        private async Task SaveConfigurationAsync()
        {
            _context.Settings.Set(ClientIdKey, ClientId.Trim());
            _context.Settings.Set(ClientSecretKey, ClientSecret.Trim());
            _context.Settings.Set(EnableGmailKey, EnableGmail);
            _context.Settings.Set(EnableCalendarKey, EnableCalendar);
            _context.Settings.Set(EnableChatKey, EnableChat);
            await _context.Settings.SaveAsync();
            ApplyProviderConfiguration();
        }

        private async Task SaveConfigAsync()
        {
            IsBusy = true;
            try
            {
                await SaveConfigurationAsync();
                StatusMessage = HasCredentials
                    ? "Configuration saved."
                    : "Configuration saved. Add both the client ID and client secret to sign in.";
                _context.UI.ShowToast("Google Account configuration saved.", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Unable to save Google Account configuration: {ex.Message}", ex);
                StatusMessage = $"Unable to save configuration: {ex.Message}";
                _context.UI.ShowToast("Unable to save Google Account configuration.", ToastType.Error);
            }
            finally
            {
                IsBusy = false;
                RefreshCommandStates();
            }
        }

        private async Task SignInAsync()
        {
            if (!CanSignIn)
            {
                StatusMessage = "Add a client ID and client secret and enable at least one Google service.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Waiting for Google sign-in in the browser...";
            try
            {
                await SaveConfigurationAsync();

                var scopes = GoogleAccountScopes.Build(EnableGmail, EnableCalendar, EnableChat);
                await _provider.SignInAsync(scopes);

                RefreshStatus();
                _context.UI.ShowToast("Signed in to Google.", ToastType.Success);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Google sign-in was cancelled.";
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Google sign-in failed: {ex.Message}", ex);
                StatusMessage = $"Sign-in failed: {ex.Message}";
                _context.UI.ShowToast("Google sign-in failed.", ToastType.Error);
            }
            finally
            {
                IsBusy = false;
                RefreshCommandStates();
            }
        }

        private async Task SignOutAsync()
        {
            IsBusy = true;
            try
            {
                await _provider.SignOutAsync();
                await _provider.ReloadAsync();
                RefreshStatus();
                _context.UI.ShowToast("Signed out of Google.", ToastType.Info);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Google sign-out failed: {ex.Message}", ex);
                StatusMessage = $"Sign-out failed: {ex.Message}";
                _context.UI.ShowToast("Google sign-out failed.", ToastType.Error);
            }
            finally
            {
                IsBusy = false;
                RefreshCommandStates();
            }
        }

        private void RefreshStatus()
        {
            IsSignedIn = _provider.IsSignedIn;
            AccountEmail = _provider.AccountEmail ?? string.Empty;

            var granted = _provider.GrantedScopes
                .Select(GoogleAccountScopes.Describe)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            GrantedScopesText = granted.Length == 0
                ? "No Google scopes granted yet."
                : string.Join(", ", granted);

            if (!HasCredentials)
            {
                StatusMessage = "Add your Google OAuth client ID and client secret to get started.";
            }
            else if (!_provider.IsSignedIn)
            {
                StatusMessage = "Not signed in. Choose the Google services to enable, then sign in.";
            }
            else if (!string.IsNullOrWhiteSpace(AccountEmail))
            {
                StatusMessage = $"Signed in as {AccountEmail}";
            }
            else
            {
                StatusMessage = "Signed in to Google.";
            }

            RefreshCommandStates();
        }

        private void RefreshCommandStates()
        {
            _saveConfigCommand.RaiseCanExecuteChanged();
            _signInCommand.RaiseCanExecuteChanged();
            _signOutCommand.RaiseCanExecuteChanged();
            _refreshCommand.RaiseCanExecuteChanged();
        }

        private static void RunOnUi(Action action)
        {
            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }

        #region Data template

        private DataTemplate CreateDataTemplate()
        {
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(FrameworkElement.MarginProperty, new Thickness(12));

            root.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            root.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            root.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            root.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            root.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            root.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));

            var header = CreateHeader();
            header.SetValue(Grid.RowProperty, 0);
            root.AppendChild(header);

            var setup = CreateSetupCard();
            setup.SetValue(Grid.RowProperty, 1);
            root.AppendChild(setup);

            var config = CreateConfigPanel();
            config.SetValue(Grid.RowProperty, 2);
            root.AppendChild(config);

            var services = CreateServicesPanel();
            services.SetValue(Grid.RowProperty, 3);
            root.AppendChild(services);

            var actions = CreateActionsPanel();
            actions.SetValue(Grid.RowProperty, 4);
            root.AppendChild(actions);

            var scopes = CreateGrantedScopesPanel();
            scopes.SetValue(Grid.RowProperty, 5);
            root.AppendChild(scopes);

            var scroll = PluginUiFactory.CreateScrollViewer(root);
            return new DataTemplate { VisualTree = scroll };
        }

        private FrameworkElementFactory CreateHeader()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var titlePanel = new FrameworkElementFactory(typeof(StackPanel));
            titlePanel.SetValue(Grid.ColumnProperty, 0);
            titlePanel.AppendChild(PluginUiFactory.CreateText(text: "Google Account", fontSize: 16, fontWeight: FontWeights.SemiBold));
            titlePanel.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "StatusMessage",
                fontSize: 11,
                foreground: SubtleTextBrush,
                wrap: true,
                margin: new Thickness(0, 2, 0, 0)));
            grid.AppendChild(titlePanel);

            var refresh = PluginUiFactory.CreateButton("Refresh", "RefreshCommand", NeutralBrush);
            refresh.SetValue(Grid.ColumnProperty, 1);
            refresh.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            grid.AppendChild(refresh);

            return grid;
        }

        private FrameworkElementFactory CreateSetupCard()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, CardBrush);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.AppendChild(PluginUiFactory.CreateSectionHeader("SET UP GOOGLE SIGN-IN"));
            panel.AppendChild(PluginUiFactory.CreateText(
                text: "Create an OAuth client (Desktop app) in Google Cloud Console and paste its client ID and secret.",
                fontSize: 11,
                foreground: SubtleTextBrush,
                wrap: true,
                margin: new Thickness(2, 0, 2, 4)));
            panel.AppendChild(PluginUiFactory.CreateText(
                text: "Redirect URI: http://127.0.0.1 (loopback). AIA opens your browser and listens on a local port, so no hosted callback is needed.",
                fontSize: 11,
                foreground: SubtleTextBrush,
                wrap: true,
                margin: new Thickness(2, 0, 2, 4)));
            panel.AppendChild(PluginUiFactory.CreateText(
                text: "Scopes are requested only for the services enabled below: Gmail (read + modify), Calendar (read + events) and Google Chat (read spaces/messages + create messages).",
                fontSize: 11,
                foreground: SubtleTextBrush,
                wrap: true,
                margin: new Thickness(2, 0, 2, 4)));
            panel.AppendChild(PluginUiFactory.CreateText(
                text: "While the OAuth consent screen is in Testing mode, only accounts added as test users can sign in.",
                fontSize: 11,
                foreground: SubtleTextBrush,
                wrap: true,
                margin: new Thickness(2, 0, 2, 0)));

            border.AppendChild(panel);
            return border;
        }

        private FrameworkElementFactory CreateConfigPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            grid.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var clientIdLabel = CreateFieldLabel("Client ID");
            clientIdLabel.SetValue(Grid.RowProperty, 0);
            clientIdLabel.SetValue(Grid.ColumnProperty, 0);
            grid.AppendChild(clientIdLabel);

            var clientIdBox = PluginUiFactory.CreateInput(
                "ClientId",
                "OAuth client ID from Google Cloud Console > APIs & Services > Credentials.");
            clientIdBox.SetValue(Grid.RowProperty, 0);
            clientIdBox.SetValue(Grid.ColumnProperty, 1);
            clientIdBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
            grid.AppendChild(clientIdBox);

            var clientSecretLabel = CreateFieldLabel("Client secret");
            clientSecretLabel.SetValue(Grid.RowProperty, 1);
            clientSecretLabel.SetValue(Grid.ColumnProperty, 0);
            grid.AppendChild(clientSecretLabel);

            var clientSecretBox = PluginUiFactory.CreateInput(
                "ClientSecret",
                "OAuth client secret from Google Cloud Console. Stored in encrypted AIA settings and never logged.");
            clientSecretBox.SetValue(Grid.RowProperty, 1);
            clientSecretBox.SetValue(Grid.ColumnProperty, 1);
            grid.AppendChild(clientSecretBox);

            var save = PluginUiFactory.CreateButton("Save", "SaveConfigCommand", NeutralBrush);
            save.SetValue(Grid.RowProperty, 1);
            save.SetValue(Grid.ColumnProperty, 2);
            save.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            grid.AppendChild(save);

            return grid;
        }

        private FrameworkElementFactory CreateServicesPanel()
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
            panel.AppendChild(PluginUiFactory.CreateSectionHeader("SERVICES"));

            var wrap = new FrameworkElementFactory(typeof(WrapPanel));
            wrap.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
            wrap.AppendChild(CreateCheckBox(
                "Gmail",
                "EnableGmail",
                "Requests gmail.readonly and gmail.modify so other plugins can read mail and update labels."));
            wrap.AppendChild(CreateCheckBox(
                "Calendar",
                "EnableCalendar",
                "Requests calendar.readonly and calendar.events so other plugins can read events and create or update them."));
            wrap.AppendChild(CreateCheckBox(
                "Google Chat",
                "EnableChat",
                "Requests chat.spaces.readonly, chat.messages.readonly and chat.messages.create."));
            panel.AppendChild(wrap);

            return panel;
        }

        private FrameworkElementFactory CreateActionsPanel()
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            var signIn = PluginUiFactory.CreateButton("Sign in with Google", "SignInCommand", AccentBrush);
            signIn.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            panel.AppendChild(signIn);

            var signOut = PluginUiFactory.CreateButton("Sign out", "SignOutCommand", NeutralBrush);
            panel.AppendChild(signOut);

            return panel;
        }

        private FrameworkElementFactory CreateGrantedScopesPanel()
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.AppendChild(PluginUiFactory.CreateSectionHeader("GRANTED SCOPES"));
            panel.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "GrantedScopesText",
                fontSize: 11,
                foreground: SubtleTextBrush,
                wrap: true,
                margin: new Thickness(2, 0, 2, 0)));
            return panel;
        }

        private static FrameworkElementFactory CreateFieldLabel(string text)
        {
            var label = PluginUiFactory.CreateText(
                text: text,
                fontSize: 12,
                foreground: CreateBrush(0xCC, 0xFF, 0xFF, 0xFF));
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 6));
            return label;
        }

        private static FrameworkElementFactory CreateCheckBox(string text, string bindingPath, string tooltip)
        {
            var box = new FrameworkElementFactory(typeof(CheckBox));
            box.SetValue(ContentControl.ContentProperty, text);
            box.SetBinding(ToggleButton.IsCheckedProperty, PluginUiFactory.TwoWay(bindingPath));
            box.SetValue(Control.ForegroundProperty, Brushes.White);
            box.SetValue(Control.FontSizeProperty, 12.0);
            box.SetValue(FrameworkElement.ToolTipProperty, tooltip);
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 18, 6));
            return box;
        }

        private static Brush CreateBrush(byte alpha, byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        #endregion
    }

    /// <summary>
    /// Settings view model for the Google Account plugin.
    /// </summary>
    public class GoogleAccountSettingsViewModel : PluginSettingsViewModelBase
    {
        private const string ClientIdKey = "ClientId";
        private const string ClientSecretKey = "ClientSecret";
        private const string EnableGmailKey = "EnableGmail";
        private const string EnableCalendarKey = "EnableCalendar";
        private const string EnableChatKey = "EnableChat";
        private const string AccountEmailKey = "AccountEmail";

        private readonly IPluginSettingsStorage _storage;

        private string _clientId = string.Empty;
        private string _clientSecret = string.Empty;
        private string _accountEmail = string.Empty;
        private bool _enableGmail = true;
        private bool _enableCalendar = true;
        private bool _enableChat = true;

        public GoogleAccountSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Load();
        }

        public override string SettingsTitle => "Google Account Settings";

        public override string SettingsDescription =>
            "Configure the Google OAuth client and the services shared with other plugins";

        public string ClientId
        {
            get => _clientId;
            set => SetProperty(ref _clientId, value);
        }

        public string ClientSecret
        {
            get => _clientSecret;
            set => SetProperty(ref _clientSecret, value);
        }

        public bool EnableGmail
        {
            get => _enableGmail;
            set => SetProperty(ref _enableGmail, value);
        }

        public bool EnableCalendar
        {
            get => _enableCalendar;
            set => SetProperty(ref _enableCalendar, value);
        }

        public bool EnableChat
        {
            get => _enableChat;
            set => SetProperty(ref _enableChat, value);
        }

        public string AccountEmail
        {
            get => _accountEmail;
            private set => SetProperty(ref _accountEmail, value);
        }

        public override void Load()
        {
            ClientId = _storage.Get(ClientIdKey, string.Empty) ?? string.Empty;
            ClientSecret = _storage.Get(ClientSecretKey, string.Empty) ?? string.Empty;
            EnableGmail = _storage.Get(EnableGmailKey, true);
            EnableCalendar = _storage.Get(EnableCalendarKey, true);
            EnableChat = _storage.Get(EnableChatKey, true);
            AccountEmail = _storage.Get(AccountEmailKey, string.Empty) ?? string.Empty;
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set(ClientIdKey, ClientId.Trim());
            _storage.Set(ClientSecretKey, ClientSecret.Trim());
            _storage.Set(EnableGmailKey, EnableGmail);
            _storage.Set(EnableCalendarKey, EnableCalendar);
            _storage.Set(EnableChatKey, EnableChat);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            _storage.Set(ClientIdKey, string.Empty);
            _storage.Set(ClientSecretKey, string.Empty);
            _storage.Set(EnableGmailKey, true);
            _storage.Set(EnableCalendarKey, true);
            _storage.Set(EnableChatKey, true);
            _storage.Set(AccountEmailKey, string.Empty);
            _ = _storage.SaveAsync();
            Load();
        }
    }
}
