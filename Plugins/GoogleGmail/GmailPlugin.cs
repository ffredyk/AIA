using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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

namespace AIA.Plugins.GoogleGmail
{
    [Plugin("AIA.Gmail", "Gmail Integration", IconSymbol = "Mail20", IsBuiltIn = true)]
    public class GmailPlugin : PluginBase
    {
        private GmailTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.Gmail";
        public override string Name => "Gmail Integration";
        public override string Description => "Shows unread Gmail messages and saves them as AIA tasks or reminders";
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
            await Context.Settings.LoadAsync();
            _tabViewModel = new GmailTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "gmail",
                Title = "Gmail",
                IconSymbol = "Mail20",
                Order = 101,
                ViewModel = _tabViewModel,
                BadgeColor = "#FFEA4335"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-gmail",
                Text = "Gmail",
                IconSymbol = "Mail20",
                Tooltip = "Open Gmail",
                Order = 101,
                OnClick = () => GmailTabViewModel.OpenUrl("https://mail.google.com")
            });
        }

        protected override async Task OnStartAsync()
        {
            var refreshMinutes = Math.Clamp(Context.Settings.Get("RefreshIntervalMinutes", 5), 1, 120);
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(refreshMinutes) };
            _refreshTimer.Tick += async (s, e) =>
            {
                if (_tabViewModel != null)
                {
                    await _tabViewModel.RefreshAsync();
                }
            };
            _refreshTimer.Start();

            if (_tabViewModel != null)
            {
                await _tabViewModel.RefreshAsync();
            }
        }

        protected override Task OnStopAsync()
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Context.UI.UnregisterTab("gmail");
            Context.UI.UnregisterToolbarButton("open-gmail");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new GmailSettingsViewModel(Context.Settings);
        }
    }

    public class GmailTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private const string OAuthServiceName = "AIA.GoogleOAuth";

        private readonly IPluginContext _context;
        private readonly GmailService _service;
        private readonly GmailSyncAdapter _adapter;
        private readonly SyncRecordStore _syncStore;
        private readonly SyncCoordinator _sync;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _openMessageCommand;
        private readonly RelayCommand _markReadCommand;
        private readonly RelayCommand _createTaskCommand;
        private readonly RelayCommand _createReminderCommand;
        private IOAuthTokenProvider? _provider;
        private DataTemplate? _cachedTemplate;
        private bool _initialized;
        private bool _isLoading;
        private bool _isSignedIn;
        private string _statusMessage = string.Empty;
        private GmailMessageViewModel? _selectedMessage;
        private int _maxMessages = 25;

        public ObservableCollection<GmailMessageViewModel> Messages { get; } = new();

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public bool IsSignedIn
        {
            get => _isSignedIn;
            private set => SetProperty(ref _isSignedIn, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public GmailMessageViewModel? SelectedMessage
        {
            get => _selectedMessage;
            set
            {
                if (SetProperty(ref _selectedMessage, value))
                {
                    _openMessageCommand.RaiseCanExecuteChanged();
                    _markReadCommand.RaiseCanExecuteChanged();
                    _createTaskCommand.RaiseCanExecuteChanged();
                    _createReminderCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand RefreshCommand => _refreshCommand;
        public ICommand OpenMessageCommand => _openMessageCommand;
        public ICommand MarkReadCommand => _markReadCommand;
        public ICommand CreateTaskCommand => _createTaskCommand;
        public ICommand CreateReminderCommand => _createReminderCommand;
        public ICommand OpenGmailCommand { get; }

        public GmailTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new GmailService(context);
            _adapter = new GmailSyncAdapter(_service);
            _syncStore = new SyncRecordStore(context.Settings, "sync.records.gmail");
            _sync = new SyncCoordinator(context, _adapter, _syncStore);

            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading);
            _openMessageCommand = new RelayCommand(OpenSelectedMessage, () => SelectedMessage != null);
            _markReadCommand = new RelayCommand(MarkReadAsync, () => SelectedMessage != null);
            _createTaskCommand = new RelayCommand(CreateTaskAsync, () => SelectedMessage != null);
            _createReminderCommand = new RelayCommand(CreateReminderAsync, () => SelectedMessage != null);
            OpenGmailCommand = new RelayCommand(() => OpenUrl("https://mail.google.com"));
        }

        public async Task InitializeAsync()
        {
            _maxMessages = Math.Clamp(_context.Settings.Get("MaxMessages", 25), 1, 100);
            await _syncStore.LoadAsync(CancellationToken.None);
            _sync.Start();
            ResolveProvider();
            _initialized = true;

            if (_provider == null)
            {
                StatusMessage = "Enable the Google Account plugin and sign in on the Google tab.";
            }
            else if (!_provider.IsSignedIn)
            {
                StatusMessage = "Sign in on the Google tab and enable Gmail.";
            }
        }

        public async Task RefreshAsync()
        {
            ResolveProvider();

            if (_provider == null)
            {
                PluginUiThread.Run(() => { StatusMessage = "Enable the Google Account plugin and sign in on the Google tab."; });
                return;
            }

            if (!_provider.IsSignedIn)
            {
                PluginUiThread.Run(() => { StatusMessage = "Sign in on the Google tab and enable Gmail."; });
                return;
            }

            if (IsLoading)
                return;

            PluginUiThread.Run(() =>
            {
                IsLoading = true;
                StatusMessage = "Loading unread Gmail messages...";
            });

            try
            {
                var messages = await _service.GetUnreadMessagesAsync(_maxMessages);

                PluginUiThread.Run(() =>
                {
                    Messages.Clear();
                    foreach (var message in messages)
                    {
                        Messages.Add(new GmailMessageViewModel(message));
                    }

                    IsSignedIn = true;
                    StatusMessage = $"{Messages.Count} unread message(s)";
                });

                var states = messages.Select(message => new ExternalSyncState
                {
                    SourceId = message.Id,
                    SourceUrl = message.Url,
                    Title = message.Subject,
                    IsCompleted = !message.IsUnread,
                    DueUtc = null
                }).ToList();

                await AddLinkedStatesAsync(states, CancellationToken.None);
                await _sync.ReconcileAsync(states, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Gmail refresh failed: {ex.Message}", ex);
                ReportError(ex);
            }
            finally
            {
                PluginUiThread.Run(() =>
                {
                    IsLoading = false;
                    _refreshCommand.RaiseCanExecuteChanged();
                });
            }
        }

        private async Task AddLinkedStatesAsync(List<ExternalSyncState> states, CancellationToken cancellationToken)
        {
            var known = new HashSet<string>(states.Select(s => s.SourceId), StringComparer.Ordinal);
            var linked = _syncStore.Records
                .Where(r => string.Equals(r.SourceKey, _adapter.SourceKey, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.SourceId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var sourceId in linked)
            {
                if (string.IsNullOrEmpty(sourceId) || known.Contains(sourceId))
                    continue;

                try
                {
                    var state = await _adapter.GetStateAsync(sourceId, cancellationToken);
                    if (state != null)
                        states.Add(state);
                }
                catch (Exception ex)
                {
                    _context.Logger.Warning($"Gmail: unable to read linked message state: {ex.Message}");
                    states.Add(new ExternalSyncState { SourceId = sourceId, Error = ex.Message });
                }
            }
        }

        private void ResolveProvider()
        {
            var provider = _context.Services.Get<IOAuthTokenProvider>(OAuthServiceName);
            if (ReferenceEquals(provider, _provider))
                return;

            if (_provider != null)
            {
                _provider.SignedOut -= OnProviderSignedOut;
            }

            _provider = provider;
            _service.Configure(provider);
            IsSignedIn = provider?.IsSignedIn ?? false;

            if (_provider != null)
            {
                _provider.SignedOut += OnProviderSignedOut;
            }
        }

        private void OnProviderSignedOut(object? sender, EventArgs e)
        {
            PluginUiThread.Run(() =>
            {
                Messages.Clear();
                SelectedMessage = null;
                IsSignedIn = false;
                StatusMessage = "Signed out of Google. Sign in on the Google tab.";
            });
        }

        private async Task MarkReadAsync()
        {
            var message = SelectedMessage;
            if (message == null)
                return;

            try
            {
                await _service.MarkReadAsync(message.Id);
                PluginUiThread.Run(() =>
                {
                    Messages.Remove(message);
                    SelectedMessage = null;
                    StatusMessage = "Message marked read";
                });
                _context.UI.ShowToast("Message marked read", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Gmail mark read failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CreateTaskAsync()
        {
            var message = SelectedMessage;
            if (message == null)
                return;

            try
            {
                await _sync.LinkTaskAsync(BuildState(message), message.Subject, BuildDescription(message), CancellationToken.None);
                _context.UI.ShowToast("AIA task created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Gmail task creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CreateReminderAsync()
        {
            var message = SelectedMessage;
            if (message == null)
                return;

            try
            {
                await _sync.LinkReminderAsync(BuildState(message), message.Subject, CancellationToken.None);
                _context.UI.ShowToast("AIA reminder created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Gmail reminder creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private void OpenSelectedMessage()
        {
            if (SelectedMessage != null && OpenUrl(SelectedMessage.Url))
                return;

            _context.UI.ShowToast("Unable to open Gmail.", ToastType.Warning);
        }

        private static ExternalSyncState BuildState(GmailMessageViewModel message)
        {
            return new ExternalSyncState
            {
                SourceId = message.Id,
                SourceUrl = message.Url,
                Title = message.Subject,
                IsCompleted = !message.IsUnread,
                DueUtc = null
            };
        }

        private static string BuildDescription(GmailMessageViewModel message)
        {
            return $"From: {message.From}{Environment.NewLine}{message.Snippet}{Environment.NewLine}{Environment.NewLine}{message.Url}";
        }

        private void ReportError(Exception ex)
        {
            PluginUiThread.Run(() =>
            {
                StatusMessage = ex.Message;
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            });
        }

        public static bool OpenUrl(string url)
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

        public override DataTemplate GetDataTemplate()
        {
            _cachedTemplate ??= CreateDataTemplate();
            return _cachedTemplate;
        }

        public override void Refresh() => _ = RefreshAsync();

        public override void OnActivated()
        {
            if (_initialized && Messages.Count == 0 && _provider?.IsSignedIn == true)
            {
                _ = RefreshAsync();
            }
        }

        public void Dispose()
        {
            if (_provider != null)
            {
                _provider.SignedOut -= OnProviderSignedOut;
            }

            _sync.Dispose();
        }

        private DataTemplate CreateDataTemplate()
        {
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(FrameworkElement.MarginProperty, new Thickness(12));
            root.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            root.AppendChild(PluginUiFactory.CreateRow(new GridLength(1, GridUnitType.Star)));

            var header = CreateHeader();
            header.SetValue(Grid.RowProperty, 0);
            root.AppendChild(header);

            var content = CreateContentPanel();
            content.SetValue(Grid.RowProperty, 1);
            root.AppendChild(content);

            return new DataTemplate { VisualTree = root };
        }

        private FrameworkElementFactory CreateHeader()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var info = new FrameworkElementFactory(typeof(StackPanel));
            info.SetValue(Grid.ColumnProperty, 1);
            info.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            info.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0));
            info.AppendChild(PluginUiFactory.CreateText(text: "Gmail", fontSize: 16, fontWeight: FontWeights.SemiBold));
            info.AppendChild(PluginUiFactory.CreateText(bindingPath: "StatusMessage", fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));
            grid.AppendChild(info);

            var refresh = PluginUiFactory.CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 2);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(refresh);

            var open = PluginUiFactory.CreateButton("Open Gmail", "OpenGmailCommand", NeutralBrush);
            open.SetValue(Grid.ColumnProperty, 3);
            grid.AppendChild(open);

            return grid;
        }

        private FrameworkElementFactory CreateContentPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(360)));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));

            var listPanel = new FrameworkElementFactory(typeof(Grid));
            listPanel.SetValue(Grid.ColumnProperty, 0);
            listPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            listPanel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            listPanel.AppendChild(PluginUiFactory.CreateRow(new GridLength(1, GridUnitType.Star)));

            var listHeader = PluginUiFactory.CreateSectionHeader("UNREAD MESSAGES");
            listHeader.SetValue(Grid.RowProperty, 0);
            listPanel.AppendChild(listHeader);

            var list = PluginUiFactory.CreateListBox("Messages", "SelectedMessage",
                PluginUiFactory.CreateListItemTemplate("TitleLine", "SubtitleLine", "MetaLine"));
            list.SetValue(Grid.RowProperty, 1);
            listPanel.AppendChild(list);
            grid.AppendChild(listPanel);

            var details = new FrameworkElementFactory(typeof(StackPanel));
            details.SetValue(Grid.ColumnProperty, 1);

            details.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedMessage.Subject",
                fontSize: 15, fontWeight: FontWeights.SemiBold, wrap: true));
            details.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedMessage.SubtitleLine",
                fontSize: 11, foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));

            var meta = PluginUiFactory.CreateText(bindingPath: "SelectedMessage.MetaLine",
                fontSize: 11, foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF));
            meta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 8));
            details.AppendChild(meta);

            var snippet = PluginUiFactory.CreateText(bindingPath: "SelectedMessage.Snippet",
                fontSize: 12, foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF), wrap: true);
            snippet.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
            details.AppendChild(snippet);

            var buttons = new FrameworkElementFactory(typeof(StackPanel));
            buttons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var open = PluginUiFactory.CreateButton("Open", "OpenMessageCommand", AccentBrush);
            open.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(open);

            var read = PluginUiFactory.CreateButton("Mark read", "MarkReadCommand", NeutralBrush);
            read.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(read);

            var task = PluginUiFactory.CreateButton("Create AIA task", "CreateTaskCommand", NeutralBrush);
            task.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(task);

            buttons.AppendChild(PluginUiFactory.CreateButton("Create reminder", "CreateReminderCommand", NeutralBrush));
            details.AppendChild(buttons);

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.AppendChild(details);
            scroll.SetValue(Grid.ColumnProperty, 1);
            grid.AppendChild(scroll);

            return grid;
        }

        private static readonly Brush AccentBrush = PluginUiFactory.BrushFrom(0xFF, 0xEA, 0x43, 0x35);
        private static readonly Brush NeutralBrush = PluginUiFactory.BrushFrom(0x33, 0xFF, 0xFF, 0xFF);
    }

    public class GmailMessageViewModel
    {
        public string Id { get; }
        public string Subject { get; }
        public string From { get; }
        public string Snippet { get; }
        public DateTime ReceivedAt { get; }
        public bool IsUnread { get; }
        public string Url { get; }

        public string TitleLine => string.IsNullOrWhiteSpace(Subject) ? "(no subject)" : Subject;
        public string SubtitleLine => From;
        public string MetaLine => ReceivedAt.ToString("MMM dd HH:mm");

        public GmailMessageViewModel(GmailMessageData data)
        {
            Id = data.Id;
            Subject = data.Subject;
            From = data.From;
            Snippet = data.Snippet;
            ReceivedAt = data.ReceivedAt;
            IsUnread = data.IsUnread;
            Url = data.Url;
        }
    }

    public class GmailSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private int _refreshIntervalMinutes = 5;
        private int _maxMessages = 25;

        public override string SettingsTitle => "Gmail Settings";
        public override string SettingsDescription => "Configure Gmail refresh behavior";

        public int RefreshIntervalMinutes
        {
            get => _refreshIntervalMinutes;
            set => SetProperty(ref _refreshIntervalMinutes, value);
        }

        public int MaxMessages
        {
            get => _maxMessages;
            set => SetProperty(ref _maxMessages, value);
        }

        public GmailSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage;
            Load();
        }

        public override void Load()
        {
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 5);
            MaxMessages = _storage.Get("MaxMessages", 25);
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("MaxMessages", MaxMessages);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            RefreshIntervalMinutes = 5;
            MaxMessages = 25;
        }
    }
}
