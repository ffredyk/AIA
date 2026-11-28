using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIA.Plugins.SDK;

namespace AIA.Plugins.GoogleChat
{
    /// <summary>
    /// Google Chat integration plugin. Reads spaces and messages through the Google Chat API and
    /// reuses the OAuth token provider registered by the Google Account plugin.
    /// </summary>
    [Plugin("AIA.GoogleChat", "Google Chat Integration", IconSymbol = "Chat20", IsBuiltIn = true)]
    public class GoogleChatPlugin : PluginBase
    {
        private const string OAuthServiceName = "AIA.GoogleOAuth";

        private GoogleChatTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.GoogleChat";
        public override string Name => "Google Chat Integration";
        public override string Description => "Browses Google Chat spaces, reads recent messages, sends replies and creates AIA tasks or reminders from conversations";
        public override Version Version => new Version(1, 0, 0);
        public override string Author => "AIA";

        public override IReadOnlyList<string> Dependencies => new[] { "AIA.GoogleAccount" };

        public override PluginPermissions RequiredPermissions =>
            PluginPermissions.Network |
            PluginPermissions.UI |
            PluginPermissions.PluginServices |
            PluginPermissions.ReadTasks |
            PluginPermissions.WriteTasks |
            PluginPermissions.ReadReminders |
            PluginPermissions.WriteReminders;

        protected override async Task OnInitializeAsync()
        {
            LogInfo("Initializing Google Chat plugin...");
            await Context.Settings.LoadAsync();

            var provider = Context.Services.Get<IOAuthTokenProvider>(OAuthServiceName);
            if (provider == null)
            {
                LogWarning("Google Chat: the Google Account plugin did not register an OAuth token provider.");
            }

            _tabViewModel = new GoogleChatTabViewModel(Context, provider);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "google-chat",
                Title = "Chat",
                IconSymbol = "Chat20",
                Order = 103,
                ViewModel = _tabViewModel,
                BadgeColor = "#FF00AC47"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-google-chat",
                Text = "Google Chat",
                IconSymbol = "Chat20",
                Tooltip = "Open Google Chat",
                Order = 103,
                OnClick = () => GoogleChatTabViewModel.OpenChatApp()
            });

            LogInfo("Google Chat plugin initialized");
        }

        protected override async Task OnStartAsync()
        {
            LogInfo("Starting Google Chat plugin...");

            var refreshMinutes = Math.Clamp(Context.Settings.Get("RefreshIntervalMinutes", 5), 1, 120);
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(refreshMinutes)
            };
            _refreshTimer.Tick += async (s, e) =>
            {
                if (_tabViewModel != null)
                {
                    await _tabViewModel.RefreshAsync();
                }
            };
            _refreshTimer.Start();

            if (_tabViewModel != null && _tabViewModel.IsSignedIn)
            {
                await _tabViewModel.RefreshAsync();
            }

            LogInfo("Google Chat plugin started");
        }

        protected override Task OnStopAsync()
        {
            LogInfo("Stopping Google Chat plugin...");
            _refreshTimer?.Stop();
            _refreshTimer = null;
            LogInfo("Google Chat plugin stopped");
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Context.UI.UnregisterTab("google-chat");
            Context.UI.UnregisterToolbarButton("open-google-chat");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new GoogleChatSettingsViewModel(Context.Settings);
        }
    }

    /// <summary>
    /// View model for the Google Chat tab.
    /// </summary>
    public class GoogleChatTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private const string OAuthServiceName = "AIA.GoogleOAuth";
        private const string SyncRecordsKey = "sync.records.googlechat";
        private const string LastSpaceNameKey = "LastSpaceName";

        private static readonly Brush AccentBrush = PluginUiFactory.BrushFrom(0xFF, 0x00, 0xAC, 0x47);
        private static readonly Brush NeutralBrush = PluginUiFactory.BrushFrom(0x33, 0xFF, 0xFF, 0xFF);
        private static readonly Brush SubtleTextBrush = PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF);
        private static readonly Brush CardBrush = PluginUiFactory.BrushFrom(0x14, 0x00, 0xAC, 0x47);

        private readonly IPluginContext _context;
        private readonly GoogleChatService _service;
        private readonly GoogleChatSyncAdapter _syncAdapter;
        private readonly SyncRecordStore _syncStore;
        private readonly SyncCoordinator _coordinator;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _openChatCommand;
        private readonly RelayCommand _openSpaceCommand;
        private readonly RelayCommand _sendMessageCommand;
        private readonly RelayCommand _createTaskCommand;
        private readonly RelayCommand _createReminderCommand;
        private readonly RelayCommand _refreshMessagesCommand;

        private IOAuthTokenProvider? _provider;
        private CancellationTokenSource? _messageLoadCts;
        private DataTemplate? _cachedTemplate;
        private GoogleChatSpaceViewModel? _selectedSpace;
        private GoogleChatMessageViewModel? _selectedMessage;
        private bool _isLoading;
        private bool _isSignedIn;
        private bool _disposed;
        private string _statusMessage = string.Empty;
        private string _googleStatusText = string.Empty;
        private string _messageText = string.Empty;
        private int _maxSpaces = 50;
        private int _maxMessages = 25;

        /// <summary>
        /// Google Chat spaces visible to the signed-in account.
        /// </summary>
        public ObservableCollection<GoogleChatSpaceViewModel> Spaces { get; } = new();

        /// <summary>
        /// Creates the tab view model and hooks it to the shared Google Chat service.
        /// </summary>
        /// <param name="context">Plugin context.</param>
        /// <param name="provider">OAuth token provider registered by the Google Account plugin, when available.</param>
        public GoogleChatTabViewModel(IPluginContext context, IOAuthTokenProvider? provider)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new GoogleChatService(context);
            _syncAdapter = new GoogleChatSyncAdapter(_service);
            _syncStore = new SyncRecordStore(context.Settings, SyncRecordsKey);
            _coordinator = new SyncCoordinator(context, _syncAdapter, _syncStore);

            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading);
            _openChatCommand = new RelayCommand(() => OpenSelectedChat(), () => IsSignedIn);
            _openSpaceCommand = new RelayCommand(() => OpenSelectedSpace(), () => SelectedSpace != null);
            _sendMessageCommand = new RelayCommand(SendMessageAsync, () => SelectedSpace != null && !string.IsNullOrWhiteSpace(MessageText));
            _createTaskCommand = new RelayCommand(CreateTaskAsync, () => SelectedSpace != null);
            _createReminderCommand = new RelayCommand(CreateReminderAsync, () => SelectedSpace != null);
            _refreshMessagesCommand = new RelayCommand(RefreshMessagesAsync, () => SelectedSpace != null);

            AttachProvider(provider);
        }

        /// <summary>
        /// Gets a value indicating whether the tab is busy loading data.
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the Google Account plugin currently has a token set.
        /// </summary>
        public bool IsSignedIn
        {
            get => _isSignedIn;
            private set
            {
                if (SetProperty(ref _isSignedIn, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        /// <summary>
        /// Gets a short status line shown in the tab header.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Gets the Google sign-in state description shown in the status panel.
        /// </summary>
        public string GoogleStatusText
        {
            get => _googleStatusText;
            private set => SetProperty(ref _googleStatusText, value);
        }

        /// <summary>
        /// Gets or sets the text typed into the composer.
        /// </summary>
        public string MessageText
        {
            get => _messageText;
            set
            {
                if (SetProperty(ref _messageText, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected space. Selecting a space loads its recent messages.
        /// </summary>
        public GoogleChatSpaceViewModel? SelectedSpace
        {
            get => _selectedSpace;
            set
            {
                if (!SetProperty(ref _selectedSpace, value))
                {
                    return;
                }

                SelectedMessage = null;
                RaiseCommandStates();

                if (value != null)
                {
                    _ = RememberSpaceAsync(value.Name);
                    _ = LoadMessagesAsync(value);
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected message, used when creating a task or reminder from a specific message.
        /// </summary>
        public GoogleChatMessageViewModel? SelectedMessage
        {
            get => _selectedMessage;
            set
            {
                if (SetProperty(ref _selectedMessage, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public ICommand RefreshCommand => _refreshCommand;
        public ICommand OpenChatCommand => _openChatCommand;
        public ICommand OpenSpaceCommand => _openSpaceCommand;
        public ICommand SendMessageCommand => _sendMessageCommand;
        public ICommand CreateTaskCommand => _createTaskCommand;
        public ICommand CreateReminderCommand => _createReminderCommand;
        public ICommand RefreshMessagesCommand => _refreshMessagesCommand;

        /// <summary>
        /// Loads settings, resolves the OAuth provider and starts the sync coordinator.
        /// </summary>
        public async Task InitializeAsync()
        {
            _maxSpaces = Math.Clamp(_context.Settings.Get("MaxSpaces", 50), 1, 1000);
            _maxMessages = Math.Clamp(_context.Settings.Get("MaxMessages", 25), 1, 1000);

            ResolveProvider();
            UpdateGoogleStatus();

            await _syncStore.LoadAsync(CancellationToken.None);
            _coordinator.Start();
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
            if (Spaces.Count == 0 && IsSignedIn)
            {
                _ = RefreshAsync();
            }
        }

        /// <summary>
        /// Re-reads the token provider from the service registry and reloads spaces and messages.
        /// </summary>
        public async Task RefreshAsync()
        {
            if (_disposed)
            {
                return;
            }

            ResolveProvider();
            UpdateGoogleStatus();

            if (!_service.IsConfigured)
            {
                StatusMessage = "Google Account plugin not available. Install it and sign in to use Google Chat.";
                return;
            }

            if (!IsSignedIn)
            {
                StatusMessage = "Not signed in. Open the Google Account tab, enable Google Chat and sign in.";
                return;
            }

            if (IsLoading)
            {
                return;
            }

            IsLoading = true;
            StatusMessage = "Loading Google Chat spaces...";

            try
            {
                var spaces = await _service.GetSpacesAsync(_maxSpaces);
                var lastSpaceName = _context.Settings.Get(LastSpaceNameKey, string.Empty) ?? string.Empty;

                RunOnUi(() =>
                {
                    Spaces.Clear();
                    foreach (var space in spaces)
                    {
                        Spaces.Add(new GoogleChatSpaceViewModel(space));
                    }
                });

                if (Spaces.Count == 0)
                {
                    StatusMessage = "No Google Chat spaces were returned. Google Chat requires a Google Workspace account with the Chat API enabled.";
                    return;
                }

                var target = GetOnUi(() =>
                    Spaces.FirstOrDefault(s => string.Equals(s.Name, lastSpaceName, StringComparison.OrdinalIgnoreCase))
                    ?? Spaces.FirstOrDefault());

                if (target == null)
                {
                    StatusMessage = "No Google Chat spaces available.";
                    return;
                }

                var alreadySelected = GetOnUi(() => ReferenceEquals(SelectedSpace, target));
                RunOnUi(() => { SelectedSpace = target; });

                if (alreadySelected)
                {
                    await LoadMessagesAsync(target);
                }
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Google Chat refresh failed: {ex.Message}", ex);
                RunOnUi(() =>
                {
                    StatusMessage = ex.Message;
                    _context.UI.ShowToast(ex.Message, ToastType.Error);
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Loads the most recent messages of the selected space.
        /// </summary>
        public async Task RefreshMessagesAsync()
        {
            var space = SelectedSpace;
            if (space == null)
            {
                return;
            }

            await LoadMessagesAsync(space);
        }

        private async Task LoadMessagesAsync(GoogleChatSpaceViewModel space)
        {
            if (_disposed)
            {
                return;
            }

            _messageLoadCts?.Cancel();
            _messageLoadCts?.Dispose();
            var cts = new CancellationTokenSource();
            _messageLoadCts = cts;

            try
            {
                var messages = await _service.GetMessagesAsync(space.Name, _maxMessages, cts.Token);
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                RunOnUi(() =>
                {
                    space.Messages.Clear();
                    foreach (var message in messages)
                    {
                        space.Messages.Add(new GoogleChatMessageViewModel(message));
                    }

                    if (ReferenceEquals(SelectedSpace, space))
                    {
                        SelectedMessage = null;
                        StatusMessage = $"{space.DisplayName} · {space.Messages.Count} recent message(s)";
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Another space was selected before this load completed.
            }
            catch (Exception ex)
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                _context.Logger.Warning($"Google Chat: unable to load messages for '{space.DisplayName}': {ex.Message}");
                RunOnUi(() =>
                {
                    if (ReferenceEquals(SelectedSpace, space))
                    {
                        StatusMessage = ex.Message;
                    }
                });
            }
        }

        private async Task RememberSpaceAsync(string spaceName)
        {
            try
            {
                _context.Settings.Set(LastSpaceNameKey, spaceName);
                await _context.Settings.SaveAsync();
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"Google Chat: unable to save the selected space: {ex.Message}");
            }
        }

        private async Task SendMessageAsync()
        {
            var space = SelectedSpace;
            var text = MessageText?.Trim();

            if (space == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                var sent = await _service.SendMessageAsync(space.Name, text);
                RunOnUi(() =>
                {
                    space.Messages.Add(new GoogleChatMessageViewModel(sent));
                    MessageText = string.Empty;
                    StatusMessage = $"Message sent to {space.DisplayName}";
                });

                _context.UI.ShowToast($"Message sent to {space.DisplayName}", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Google Chat send failed: {ex.Message}", ex);
                RunOnUi(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            }
        }

        private async Task CreateTaskAsync()
        {
            var space = SelectedSpace;
            var source = BuildSourceState(space);
            if (space == null || source == null)
            {
                return;
            }

            var message = SelectedMessage;
            var title = BuildFollowUpTitle(space, message);
            var description = BuildTaskDescription(space, message, source.SourceUrl);

            try
            {
                await PluginUiThread.RunAsync(() =>
                    _coordinator.LinkTaskAsync(source, title, description, CancellationToken.None));

                RunOnUi(() => { StatusMessage = $"Task created: {title}"; });
                _context.UI.ShowToast("Task created from Google Chat.", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Google Chat: unable to create a task: {ex.Message}", ex);
                RunOnUi(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast("Unable to create the task.", ToastType.Error);
            }
        }

        private async Task CreateReminderAsync()
        {
            var space = SelectedSpace;
            var source = BuildSourceState(space);
            if (space == null || source == null)
            {
                return;
            }

            var message = SelectedMessage;
            var title = BuildFollowUpTitle(space, message);
            if (!string.IsNullOrWhiteSpace(source.SourceUrl))
            {
                title = $"{title} — {source.SourceUrl}";
            }

            try
            {
                await PluginUiThread.RunAsync(() =>
                    _coordinator.LinkReminderAsync(source, title, CancellationToken.None));

                RunOnUi(() => { StatusMessage = $"Reminder created: {title}"; });
                _context.UI.ShowToast("Reminder created from Google Chat.", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Google Chat: unable to create a reminder: {ex.Message}", ex);
                RunOnUi(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast("Unable to create the reminder.", ToastType.Error);
            }
        }

        private ExternalSyncState? BuildSourceState(GoogleChatSpaceViewModel? space)
        {
            if (space == null)
            {
                return null;
            }

            var message = SelectedMessage;
            if (message != null && !string.IsNullOrWhiteSpace(message.Name))
            {
                return new ExternalSyncState
                {
                    SourceId = message.Name,
                    SourceUrl = string.IsNullOrWhiteSpace(message.Url) ? space.Url : message.Url,
                    Title = $"{space.DisplayName} · {message.AuthorName}"
                };
            }

            return new ExternalSyncState
            {
                SourceId = space.Name,
                SourceUrl = space.Url,
                Title = space.DisplayName
            };
        }

        private static string BuildFollowUpTitle(
            GoogleChatSpaceViewModel space,
            GoogleChatMessageViewModel? message)
        {
            return message == null
                ? $"Follow up in Google Chat: {space.DisplayName}"
                : $"Follow up on a message in {space.DisplayName}";
        }

        private static string BuildTaskDescription(
            GoogleChatSpaceViewModel space,
            GoogleChatMessageViewModel? message,
            string url)
        {
            var lines = new List<string>();

            if (message != null)
            {
                lines.Add($"Google Chat message in {space.DisplayName} from {message.AuthorName} ({message.TimestampText}).");
                lines.Add(message.Text);
            }
            else
            {
                lines.Add($"Google Chat space {space.DisplayName}.");
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                lines.Add(url);
            }

            return string.Join(Environment.NewLine, lines);
        }

        private void OpenSelectedChat()
        {
            if (OpenChatApp())
            {
                return;
            }

            _context.UI.ShowToast("Unable to open Google Chat.", ToastType.Warning);
        }

        private void OpenSelectedSpace()
        {
            var url = SelectedSpace?.Url;
            if (string.IsNullOrWhiteSpace(url) || !TryOpenUrl(url))
            {
                _context.UI.ShowToast("Unable to open the selected space.", ToastType.Warning);
            }
        }

        /// <summary>
        /// Opens Google Chat in the default browser.
        /// </summary>
        public static bool OpenChatApp()
        {
            return TryOpenUrl("https://mail.google.com/chat");
        }

        private static bool TryOpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _messageLoadCts?.Cancel();
            _messageLoadCts?.Dispose();
            _messageLoadCts = null;

            if (_provider != null)
            {
                _provider.SignedOut -= OnProviderSignedOut;
                _provider = null;
            }

            _coordinator.Dispose();
        }

        private void ResolveProvider()
        {
            IOAuthTokenProvider? provider;
            try
            {
                provider = _context.Services.Get<IOAuthTokenProvider>(OAuthServiceName);
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"Google Chat: unable to resolve the Google OAuth provider: {ex.Message}");
                provider = null;
            }

            AttachProvider(provider);
            _service.Configure(provider);
        }

        private void AttachProvider(IOAuthTokenProvider? provider)
        {
            if (ReferenceEquals(_provider, provider))
            {
                return;
            }

            if (_provider != null)
            {
                _provider.SignedOut -= OnProviderSignedOut;
            }

            _provider = provider;

            if (_provider != null)
            {
                _provider.SignedOut += OnProviderSignedOut;
            }
        }

        private void OnProviderSignedOut(object? sender, EventArgs e)
        {
            RunOnUi(() =>
            {
                IsSignedIn = false;
                UpdateGoogleStatus();
                StatusMessage = "Signed out of Google. Sign in with the Google Account tab.";
                Spaces.Clear();
                SelectedSpace = null;
            });
        }

        private void UpdateGoogleStatus()
        {
            IsSignedIn = _provider?.IsSignedIn == true;

            if (_provider == null)
            {
                GoogleStatusText = "Google Account plugin not available. Install and enable the Google Account plugin, then sign in.";
            }
            else if (!_provider.IsSignedIn)
            {
                GoogleStatusText = "Not signed in. Open the Google Account tab, enable Google Chat and sign in.";
            }
            else
            {
                GoogleStatusText = "Signed in to Google. Google Chat requires a Google Workspace account with the Chat API enabled.";
            }
        }

        private void RaiseCommandStates()
        {
            _refreshCommand.RaiseCanExecuteChanged();
            _openChatCommand.RaiseCanExecuteChanged();
            _openSpaceCommand.RaiseCanExecuteChanged();
            _sendMessageCommand.RaiseCanExecuteChanged();
            _createTaskCommand.RaiseCanExecuteChanged();
            _createReminderCommand.RaiseCanExecuteChanged();
            _refreshMessagesCommand.RaiseCanExecuteChanged();
        }

        private static void RunOnUi(Action action)
        {
            PluginUiThread.Run(action);
        }

        private static T GetOnUi<T>(Func<T> func)
        {
            return PluginUiThread.Get(func);
        }

        #region Data template

        private DataTemplate CreateDataTemplate()
        {
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(FrameworkElement.MarginProperty, new Thickness(12));

            root.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            root.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            root.AppendChild(PluginUiFactory.CreateRow(new GridLength(1, GridUnitType.Star)));

            var header = CreateHeader();
            header.SetValue(Grid.RowProperty, 0);
            root.AppendChild(header);

            var status = CreateGoogleStatusPanel();
            status.SetValue(Grid.RowProperty, 1);
            root.AppendChild(status);

            var content = CreateContentPanel();
            content.SetValue(Grid.RowProperty, 2);
            root.AppendChild(content);

            return new DataTemplate { VisualTree = root };
        }

        private FrameworkElementFactory CreateHeader()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));

            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var titlePanel = new FrameworkElementFactory(typeof(StackPanel));
            titlePanel.SetValue(Grid.ColumnProperty, 0);
            titlePanel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            titlePanel.AppendChild(PluginUiFactory.CreateText(text: "Google Chat", fontSize: 16, fontWeight: FontWeights.SemiBold));
            titlePanel.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "StatusMessage",
                fontSize: 11,
                foreground: SubtleTextBrush,
                wrap: true,
                margin: new Thickness(0, 2, 0, 0)));
            grid.AppendChild(titlePanel);

            var refresh = PluginUiFactory.CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 1);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 6, 0));
            refresh.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            grid.AppendChild(refresh);

            var open = PluginUiFactory.CreateButton("Open Google Chat", "OpenChatCommand", NeutralBrush);
            open.SetValue(Grid.ColumnProperty, 2);
            open.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            grid.AppendChild(open);

            return grid;
        }

        private FrameworkElementFactory CreateGoogleStatusPanel()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, CardBrush);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.AppendChild(PluginUiFactory.CreateSectionHeader("GOOGLE ACCOUNT"));
            panel.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "GoogleStatusText",
                fontSize: 11,
                foreground: SubtleTextBrush,
                wrap: true,
                margin: new Thickness(2, 0, 2, 4)));
            panel.AppendChild(PluginUiFactory.CreateText(
                text: "Google Chat scopes are requested by the Google Account plugin: chat.spaces.readonly, chat.messages.readonly and chat.messages.create.",
                fontSize: 10,
                foreground: SubtleTextBrush,
                wrap: true,
                margin: new Thickness(2, 0, 2, 0)));

            border.AppendChild(panel);
            return border;
        }

        private FrameworkElementFactory CreateContentPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(280)));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));

            var spacesPanel = new FrameworkElementFactory(typeof(Grid));
            spacesPanel.SetValue(Grid.ColumnProperty, 0);
            spacesPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            spacesPanel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            spacesPanel.AppendChild(PluginUiFactory.CreateRow(new GridLength(1, GridUnitType.Star)));

            var spacesHeader = PluginUiFactory.CreateSectionHeader("SPACES");
            spacesHeader.SetValue(Grid.RowProperty, 0);
            spacesPanel.AppendChild(spacesHeader);

            var spacesList = PluginUiFactory.CreateListBox("Spaces", "SelectedSpace", CreateSpaceItemTemplate());
            spacesList.SetValue(Grid.RowProperty, 1);
            spacesPanel.AppendChild(spacesList);

            grid.AppendChild(spacesPanel);

            var messagesPanel = new FrameworkElementFactory(typeof(Grid));
            messagesPanel.SetValue(Grid.ColumnProperty, 1);
            messagesPanel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            messagesPanel.AppendChild(PluginUiFactory.CreateRow(new GridLength(1, GridUnitType.Star)));
            messagesPanel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            messagesPanel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));

            var selectedHeader = new FrameworkElementFactory(typeof(Grid));
            selectedHeader.SetValue(Grid.RowProperty, 0);
            selectedHeader.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
            selectedHeader.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            selectedHeader.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));
            selectedHeader.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var selectedInfo = new FrameworkElementFactory(typeof(StackPanel));
            selectedInfo.SetValue(Grid.ColumnProperty, 0);
            selectedInfo.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedSpace.TitleLine", fontSize: 14, fontWeight: FontWeights.SemiBold));
            selectedInfo.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedSpace.SubtitleLine", fontSize: 11, foreground: SubtleTextBrush));
            selectedHeader.AppendChild(selectedInfo);

            var refreshMessages = PluginUiFactory.CreateButton("Refresh messages", "RefreshMessagesCommand", NeutralBrush);
            refreshMessages.SetValue(Grid.ColumnProperty, 1);
            refreshMessages.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            refreshMessages.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 6, 0));
            selectedHeader.AppendChild(refreshMessages);

            var openSpace = PluginUiFactory.CreateButton("Open space", "OpenSpaceCommand", NeutralBrush);
            openSpace.SetValue(Grid.ColumnProperty, 2);
            openSpace.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            selectedHeader.AppendChild(openSpace);

            messagesPanel.AppendChild(selectedHeader);

            var messagesList = PluginUiFactory.CreateListBox("SelectedSpace.Messages", "SelectedMessage", CreateMessageItemTemplate());
            messagesList.SetValue(Grid.RowProperty, 1);
            messagesPanel.AppendChild(messagesList);

            var composer = new FrameworkElementFactory(typeof(Grid));
            composer.SetValue(Grid.RowProperty, 2);
            composer.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));
            composer.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            composer.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var messageBox = PluginUiFactory.CreateInput(
                "MessageText",
                "Type a message and press Send.",
                minHeight: 32);
            messageBox.SetValue(Grid.ColumnProperty, 0);
            composer.AppendChild(messageBox);

            var send = PluginUiFactory.CreateButton("Send", "SendMessageCommand", AccentBrush);
            send.SetValue(Grid.ColumnProperty, 1);
            send.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            composer.AppendChild(send);

            messagesPanel.AppendChild(composer);

            var actions = new FrameworkElementFactory(typeof(StackPanel));
            actions.SetValue(Grid.RowProperty, 3);
            actions.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            actions.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));

            var createTask = PluginUiFactory.CreateButton("Create task", "CreateTaskCommand", NeutralBrush);
            createTask.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            actions.AppendChild(createTask);

            var createReminder = PluginUiFactory.CreateButton("Create reminder", "CreateReminderCommand", NeutralBrush);
            createReminder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            actions.AppendChild(createReminder);

            var hint = PluginUiFactory.CreateText(
                text: "Uses the selected message, or the selected space when no message is selected.",
                fontSize: 10,
                foreground: SubtleTextBrush,
                margin: new Thickness(2, 0, 0, 0));
            hint.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            actions.AppendChild(hint);

            messagesPanel.AppendChild(actions);

            grid.AppendChild(messagesPanel);
            return grid;
        }

        private static DataTemplate CreateSpaceItemTemplate()
        {
            return PluginUiFactory.CreateListItemTemplate("TitleLine", "SubtitleLine");
        }

        private static DataTemplate CreateMessageItemTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));
            border.SetValue(Border.MarginProperty, new Thickness(0, 2, 0, 2));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.BackgroundProperty, PluginUiFactory.BrushFrom(0x14, 0xFF, 0xFF, 0xFF));

            var panel = new FrameworkElementFactory(typeof(StackPanel));

            var header = new FrameworkElementFactory(typeof(StackPanel));
            header.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            header.AppendChild(PluginUiFactory.CreateText(bindingPath: "AuthorName", fontSize: 12, fontWeight: FontWeights.SemiBold));
            header.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "TimestampText",
                fontSize: 10,
                foreground: PluginUiFactory.BrushFrom(0x88, 0xFF, 0xFF, 0xFF),
                margin: new Thickness(8, 0, 0, 0)));

            panel.AppendChild(header);

            panel.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "Text",
                fontSize: 12,
                foreground: PluginUiFactory.BrushFrom(0xDD, 0xFF, 0xFF, 0xFF),
                wrap: true,
                margin: new Thickness(0, 2, 0, 0)));

            border.AppendChild(panel);
            return new DataTemplate { VisualTree = border };
        }

        #endregion
    }

    /// <summary>
    /// View model for a Google Chat space.
    /// </summary>
    public class GoogleChatSpaceViewModel
    {
        /// <summary>
        /// Resource name of the space, for example <c>spaces/AAAA</c>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Display name of the space.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Space type such as <c>SPACE</c>, <c>GROUP_CHAT</c> or <c>DIRECT_MESSAGE</c>.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Web link that opens the space in Google Chat.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// Primary line shown in the space list.
        /// </summary>
        public string TitleLine => DisplayName;

        /// <summary>
        /// Secondary line shown in the space list.
        /// </summary>
        public string SubtitleLine => Type;

        /// <summary>
        /// Messages loaded for the space.
        /// </summary>
        public ObservableCollection<GoogleChatMessageViewModel> Messages { get; } = new();

        /// <summary>
        /// Creates a space view model from service data.
        /// </summary>
        /// <param name="data">Space data returned by the Google Chat service.</param>
        public GoogleChatSpaceViewModel(GoogleChatSpaceData data)
        {
            ArgumentNullException.ThrowIfNull(data);

            Name = data.Name;
            DisplayName = data.DisplayName;
            Type = data.Type;
            Url = data.Url;
        }
    }

    /// <summary>
    /// View model for a Google Chat message.
    /// </summary>
    public class GoogleChatMessageViewModel
    {
        /// <summary>
        /// Resource name of the message, for example <c>spaces/AAAA/messages/BBBB</c>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Resource name of the containing space.
        /// </summary>
        public string SpaceName { get; }

        /// <summary>
        /// Display name of the sender.
        /// </summary>
        public string AuthorName { get; }

        /// <summary>
        /// Plain text of the message.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Short local time label.
        /// </summary>
        public string TimestampText { get; }

        /// <summary>
        /// Web link that opens the containing space in Google Chat.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// Creates a message view model from service data.
        /// </summary>
        /// <param name="data">Message data returned by the Google Chat service.</param>
        public GoogleChatMessageViewModel(GoogleChatMessageData data)
        {
            ArgumentNullException.ThrowIfNull(data);

            Name = data.Name;
            SpaceName = data.SpaceName;
            AuthorName = data.SenderName;
            Text = data.Text;
            TimestampText = data.CreateTime.ToString("MMM dd HH:mm");
            Url = data.Url;
        }
    }

    /// <summary>
    /// Settings view model for the Google Chat plugin.
    /// </summary>
    public class GoogleChatSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private int _refreshIntervalMinutes = 5;
        private int _maxSpaces = 50;
        private int _maxMessages = 25;

        public override string SettingsTitle => "Google Chat Settings";
        public override string SettingsDescription => "Configure Google Chat refresh behaviour and request limits";

        public int RefreshIntervalMinutes
        {
            get => _refreshIntervalMinutes;
            set => SetProperty(ref _refreshIntervalMinutes, value);
        }

        public int MaxSpaces
        {
            get => _maxSpaces;
            set => SetProperty(ref _maxSpaces, value);
        }

        public int MaxMessages
        {
            get => _maxMessages;
            set => SetProperty(ref _maxMessages, value);
        }

        public GoogleChatSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Load();
        }

        public override void Load()
        {
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 5);
            MaxSpaces = _storage.Get("MaxSpaces", 50);
            MaxMessages = _storage.Get("MaxMessages", 25);
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("MaxSpaces", MaxSpaces);
            _storage.Set("MaxMessages", MaxMessages);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            RefreshIntervalMinutes = 5;
            MaxSpaces = 50;
            MaxMessages = 25;
        }
    }
}
