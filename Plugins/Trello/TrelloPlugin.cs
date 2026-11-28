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

namespace AIA.Plugins.Trello
{
    [Plugin("AIA.Trello", "Trello Integration", IconSymbol = "Board20", IsBuiltIn = true)]
    public class TrelloPlugin : PluginBase
    {
        private TrelloTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.Trello";
        public override string Name => "Trello Integration";
        public override string Description => "Shows Trello cards assigned to you with due dates and comments";
        public override Version Version => new Version(1, 0, 0);
        public override string Author => "AIA";

        public override PluginPermissions RequiredPermissions =>
            PluginPermissions.Network |
            PluginPermissions.UI |
            PluginPermissions.ReadTasks |
            PluginPermissions.WriteTasks |
            PluginPermissions.ReadReminders |
            PluginPermissions.WriteReminders;

        protected override async Task OnInitializeAsync()
        {
            await Context.Settings.LoadAsync();
            _tabViewModel = new TrelloTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "trello",
                Title = "Trello",
                IconSymbol = "Board20",
                Order = 92,
                ViewModel = _tabViewModel,
                BadgeColor = "#FF0079BF"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-trello",
                Text = "Trello",
                IconSymbol = "Board20",
                Tooltip = "Open Trello",
                Order = 92,
                OnClick = () => TrelloTabViewModel.OpenUrl("https://trello.com")
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
            Context.UI.UnregisterTab("trello");
            Context.UI.UnregisterToolbarButton("open-trello");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new TrelloSettingsViewModel(Context.Settings);
        }
    }

    public class TrelloTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private readonly IPluginContext _context;
        private readonly TrelloService _service;
        private readonly TrelloSyncAdapter _adapter;
        private readonly SyncRecordStore _syncStore;
        private readonly SyncCoordinator _sync;
        private readonly RelayCommand _saveConfigCommand;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _openCardCommand;
        private readonly RelayCommand _addCommentCommand;
        private readonly RelayCommand _toggleCompleteCommand;
        private readonly RelayCommand _createTaskCommand;
        private readonly RelayCommand _createReminderCommand;
        private DataTemplate? _cachedTemplate;
        private bool _initialized;
        private bool _isLoading;
        private bool _isConfigured;
        private string _apiKeyInput = string.Empty;
        private string _tokenInput = string.Empty;
        private string _statusMessage = string.Empty;
        private string _commentText = string.Empty;
        private TrelloCardViewModel? _selectedCard;
        private int _maxCards = 50;

        public ObservableCollection<TrelloCardViewModel> Cards { get; } = new();

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public bool IsConfigured
        {
            get => _isConfigured;
            private set => SetProperty(ref _isConfigured, value);
        }

        public string ApiKeyInput
        {
            get => _apiKeyInput;
            set => SetProperty(ref _apiKeyInput, value);
        }

        public string TokenInput
        {
            get => _tokenInput;
            set => SetProperty(ref _tokenInput, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public string CommentText
        {
            get => _commentText;
            set
            {
                if (SetProperty(ref _commentText, value))
                {
                    _addCommentCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public TrelloCardViewModel? SelectedCard
        {
            get => _selectedCard;
            set
            {
                if (SetProperty(ref _selectedCard, value))
                {
                    _openCardCommand.RaiseCanExecuteChanged();
                    _toggleCompleteCommand.RaiseCanExecuteChanged();
                    _createTaskCommand.RaiseCanExecuteChanged();
                    _createReminderCommand.RaiseCanExecuteChanged();
                    _addCommentCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand SaveConfigCommand => _saveConfigCommand;
        public ICommand RefreshCommand => _refreshCommand;
        public ICommand OpenCardCommand => _openCardCommand;
        public ICommand AddCommentCommand => _addCommentCommand;
        public ICommand ToggleCompleteCommand => _toggleCompleteCommand;
        public ICommand CreateTaskCommand => _createTaskCommand;
        public ICommand CreateReminderCommand => _createReminderCommand;
        public ICommand OpenTrelloCommand { get; }

        public TrelloTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new TrelloService(context);
            _adapter = new TrelloSyncAdapter(_service);
            _syncStore = new SyncRecordStore(context.Settings, "sync.records.trello");
            _sync = new SyncCoordinator(context, _adapter, _syncStore);

            _saveConfigCommand = new RelayCommand(SaveConfigAsync, () => !IsLoading);
            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading && _service.IsConfigured);
            _openCardCommand = new RelayCommand(OpenSelectedCard, () => SelectedCard != null);
            _addCommentCommand = new RelayCommand(AddCommentAsync,
                () => SelectedCard != null && !string.IsNullOrWhiteSpace(CommentText));
            _toggleCompleteCommand = new RelayCommand(ToggleCompleteAsync, () => SelectedCard != null);
            _createTaskCommand = new RelayCommand(CreateTaskAsync, () => SelectedCard != null);
            _createReminderCommand = new RelayCommand(CreateReminderAsync, () => SelectedCard != null);
            OpenTrelloCommand = new RelayCommand(() => OpenUrl("https://trello.com"));
        }

        public async Task InitializeAsync()
        {
            ApiKeyInput = _context.Settings.Get("ApiKey", string.Empty) ?? string.Empty;
            TokenInput = _context.Settings.Get("Token", string.Empty) ?? string.Empty;
            _maxCards = Math.Clamp(_context.Settings.Get("MaxCards", 50), 5, 200);

            _service.Configure(ApiKeyInput, TokenInput);
            IsConfigured = _service.IsConfigured;

            await _syncStore.LoadAsync(CancellationToken.None);
            _sync.Start();
            _initialized = true;

            if (!IsConfigured)
            {
                StatusMessage = "Add a Trello API key and token to get started.";
            }
        }

        public async Task RefreshAsync()
        {
            if (!_service.IsConfigured)
            {
                PluginUiThread.Run(() => { StatusMessage = "Add a Trello API key and token to get started."; });
                return;
            }

            if (IsLoading)
                return;

            PluginUiThread.Run(() =>
            {
                IsLoading = true;
                StatusMessage = "Loading Trello cards...";
            });

            try
            {
                var member = await _service.TestConnectionAsync();
                var cards = await _service.GetMyCardsAsync(_maxCards);

                PluginUiThread.Run(() =>
                {
                    Cards.Clear();
                    foreach (var card in cards)
                    {
                        Cards.Add(new TrelloCardViewModel(card));
                    }

                    IsConfigured = true;
                    StatusMessage = $"Connected as {member.Username} · {Cards.Count} card(s)";
                });

                var states = cards.Select(card => new ExternalSyncState
                {
                    SourceId = card.Id,
                    SourceUrl = card.Url,
                    Title = card.Name,
                    IsCompleted = card.DueComplete || card.Closed,
                    DueUtc = card.Due?.ToUniversalTime()
                }).ToList();

                await AddLinkedStatesAsync(states, CancellationToken.None);
                await _sync.ReconcileAsync(states, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Trello refresh failed: {ex.Message}", ex);
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
                    _context.Logger.Warning($"Trello: unable to read linked card state: {ex.Message}");
                    states.Add(new ExternalSyncState { SourceId = sourceId, Error = ex.Message });
                }
            }
        }

        private async Task SaveConfigAsync()
        {
            _service.Configure(ApiKeyInput, TokenInput);
            _context.Settings.Set("ApiKey", ApiKeyInput?.Trim() ?? string.Empty);
            _context.Settings.Set("Token", TokenInput?.Trim() ?? string.Empty);
            await _context.Settings.SaveAsync();

            PluginUiThread.Run(() =>
            {
                IsConfigured = _service.IsConfigured;
                StatusMessage = IsConfigured ? "Configuration saved. Connecting..." : "Credentials cleared.";
                _refreshCommand.RaiseCanExecuteChanged();
            });

            if (IsConfigured)
            {
                await RefreshAsync();
            }
        }

        private async Task AddCommentAsync()
        {
            var card = SelectedCard;
            var text = CommentText?.Trim();
            if (card == null || string.IsNullOrEmpty(text))
                return;

            try
            {
                await _service.AddCommentAsync(card.Id, text);
                PluginUiThread.Run(() =>
                {
                    CommentText = string.Empty;
                    StatusMessage = $"Comment added to {card.Name}";
                });
                _context.UI.ShowToast("Comment added", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Trello comment failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task ToggleCompleteAsync()
        {
            var card = SelectedCard;
            if (card == null)
                return;

            try
            {
                await _service.SetDueCompleteAsync(card.Id, !card.DueComplete);
                PluginUiThread.Run(() => { StatusMessage = "Card updated"; });
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Trello complete toggle failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CreateTaskAsync()
        {
            var card = SelectedCard;
            if (card == null)
                return;

            try
            {
                await _sync.LinkTaskAsync(BuildState(card), card.Name, BuildDescription(card), CancellationToken.None);
                _context.UI.ShowToast("AIA task created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Trello task creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CreateReminderAsync()
        {
            var card = SelectedCard;
            if (card == null)
                return;

            try
            {
                await _sync.LinkReminderAsync(BuildState(card), card.Name, CancellationToken.None);
                _context.UI.ShowToast("AIA reminder created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Trello reminder creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private void OpenSelectedCard()
        {
            if (SelectedCard != null && OpenUrl(SelectedCard.Url))
                return;

            _context.UI.ShowToast("Unable to open Trello.", ToastType.Warning);
        }

        private static ExternalSyncState BuildState(TrelloCardViewModel card)
        {
            return new ExternalSyncState
            {
                SourceId = card.Id,
                SourceUrl = card.Url,
                Title = card.Name,
                IsCompleted = card.DueComplete || card.Closed,
                DueUtc = card.Due?.ToUniversalTime()
            };
        }

        private static string BuildDescription(TrelloCardViewModel card)
        {
            var description = card.Description?.Trim() ?? string.Empty;
            if (description.Length > 500)
                description = description.Substring(0, 500) + "...";

            return string.IsNullOrEmpty(description)
                ? card.Url
                : description + Environment.NewLine + Environment.NewLine + card.Url;
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
            if (_initialized && Cards.Count == 0 && _service.IsConfigured)
            {
                _ = RefreshAsync();
            }
        }

        public void Dispose()
        {
            _sync.Dispose();
        }

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

            var config = CreateConfigPanel();
            config.SetValue(Grid.RowProperty, 1);
            root.AppendChild(config);

            var content = CreateContentPanel();
            content.SetValue(Grid.RowProperty, 2);
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
            info.AppendChild(PluginUiFactory.CreateText(text: "Trello", fontSize: 16, fontWeight: FontWeights.SemiBold));
            info.AppendChild(PluginUiFactory.CreateText(bindingPath: "StatusMessage", fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));
            grid.AppendChild(info);

            var refresh = PluginUiFactory.CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 2);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(refresh);

            var open = PluginUiFactory.CreateButton("Open Trello", "OpenTrelloCommand", NeutralBrush);
            open.SetValue(Grid.ColumnProperty, 3);
            grid.AppendChild(open);

            return grid;
        }

        private FrameworkElementFactory CreateConfigPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var label = PluginUiFactory.CreateText(text: "API key / token", fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF));
            label.SetValue(Grid.ColumnProperty, 0);
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            grid.AppendChild(label);

            var keyBox = PluginUiFactory.CreateInput("ApiKeyInput",
                "Trello API key from https://trello.com/app-key");
            keyBox.SetValue(Grid.ColumnProperty, 1);
            keyBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            grid.AppendChild(keyBox);

            var tokenBox = PluginUiFactory.CreateInput("TokenInput",
                "Trello read/write token generated from the API key page");
            tokenBox.SetValue(Grid.ColumnProperty, 2);
            tokenBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            grid.AppendChild(tokenBox);

            var save = PluginUiFactory.CreateButton("Save & Connect", "SaveConfigCommand", AccentBrush);
            save.SetValue(Grid.ColumnProperty, 3);
            grid.AppendChild(save);

            return grid;
        }

        private FrameworkElementFactory CreateContentPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(340)));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));

            var listPanel = new FrameworkElementFactory(typeof(Grid));
            listPanel.SetValue(Grid.ColumnProperty, 0);
            listPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            listPanel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            listPanel.AppendChild(PluginUiFactory.CreateRow(new GridLength(1, GridUnitType.Star)));

            var listHeader = PluginUiFactory.CreateSectionHeader("MY CARDS");
            listHeader.SetValue(Grid.RowProperty, 0);
            listPanel.AppendChild(listHeader);

            var list = PluginUiFactory.CreateListBox("Cards", "SelectedCard",
                PluginUiFactory.CreateListItemTemplate("TitleLine", "SubtitleLine", "MetaLine"));
            list.SetValue(Grid.RowProperty, 1);
            listPanel.AppendChild(list);
            grid.AppendChild(listPanel);

            var details = new FrameworkElementFactory(typeof(StackPanel));
            details.SetValue(Grid.ColumnProperty, 1);

            details.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedCard.Name",
                fontSize: 15, fontWeight: FontWeights.SemiBold, wrap: true));
            details.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedCard.SubtitleLine",
                fontSize: 11, foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));

            var meta = PluginUiFactory.CreateText(bindingPath: "SelectedCard.MetaLine",
                fontSize: 11, foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF));
            meta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 8));
            details.AppendChild(meta);

            var description = PluginUiFactory.CreateText(bindingPath: "SelectedCard.Description",
                fontSize: 12, foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF), wrap: true);
            description.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
            details.AppendChild(description);

            var buttons = new FrameworkElementFactory(typeof(StackPanel));
            buttons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            buttons.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            var open = PluginUiFactory.CreateButton("Open", "OpenCardCommand", AccentBrush);
            open.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(open);

            var toggle = PluginUiFactory.CreateButton("Toggle due complete", "ToggleCompleteCommand", NeutralBrush);
            toggle.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(toggle);

            var task = PluginUiFactory.CreateButton("Create AIA task", "CreateTaskCommand", NeutralBrush);
            task.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(task);

            buttons.AppendChild(PluginUiFactory.CreateButton("Create reminder", "CreateReminderCommand", NeutralBrush));
            details.AppendChild(buttons);

            details.AppendChild(PluginUiFactory.CreateInput("CommentText", "Comment to add to the selected card"));

            var addComment = PluginUiFactory.CreateButton("Add comment", "AddCommentCommand", AccentBrush);
            addComment.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 0));
            addComment.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            details.AppendChild(addComment);

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.AppendChild(details);
            scroll.SetValue(Grid.ColumnProperty, 1);
            grid.AppendChild(scroll);

            return grid;
        }

        private static readonly Brush AccentBrush = PluginUiFactory.BrushFrom(0xFF, 0x00, 0x79, 0xBF);
        private static readonly Brush NeutralBrush = PluginUiFactory.BrushFrom(0x33, 0xFF, 0xFF, 0xFF);
    }

    public class TrelloCardViewModel
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Url { get; }
        public DateTime? Due { get; }
        public bool DueComplete { get; }
        public bool Closed { get; }
        public string BoardName { get; }
        public string ListName { get; }

        public string TitleLine => Name;
        public string SubtitleLine => $"{BoardName} · {ListName}".Trim(' ', '·');
        public string MetaLine
        {
            get
            {
                var parts = new List<string>();
                if (Due.HasValue) parts.Add($"Due {Due.Value:MMM dd HH:mm}");
                parts.Add(DueComplete || Closed ? "Complete" : "Open");
                return string.Join(" · ", parts);
            }
        }

        public TrelloCardViewModel(TrelloCardData data)
        {
            Id = data.Id;
            Name = data.Name;
            Description = data.Description;
            Url = data.Url;
            Due = data.Due;
            DueComplete = data.DueComplete;
            Closed = data.Closed;
            BoardName = data.BoardName;
            ListName = data.ListName;
        }
    }

    public class TrelloSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private string _apiKey = string.Empty;
        private string _token = string.Empty;
        private int _refreshIntervalMinutes = 5;
        private int _maxCards = 50;

        public override string SettingsTitle => "Trello Settings";
        public override string SettingsDescription => "Configure the Trello connection";

        public string ApiKey
        {
            get => _apiKey;
            set => SetProperty(ref _apiKey, value);
        }

        public string Token
        {
            get => _token;
            set => SetProperty(ref _token, value);
        }

        public int RefreshIntervalMinutes
        {
            get => _refreshIntervalMinutes;
            set => SetProperty(ref _refreshIntervalMinutes, value);
        }

        public int MaxCards
        {
            get => _maxCards;
            set => SetProperty(ref _maxCards, value);
        }

        public TrelloSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage;
            Load();
        }

        public override void Load()
        {
            ApiKey = _storage.Get("ApiKey", string.Empty) ?? string.Empty;
            Token = _storage.Get("Token", string.Empty) ?? string.Empty;
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 5);
            MaxCards = _storage.Get("MaxCards", 50);
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("ApiKey", ApiKey);
            _storage.Set("Token", Token);
            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("MaxCards", MaxCards);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            ApiKey = string.Empty;
            Token = string.Empty;
            RefreshIntervalMinutes = 5;
            MaxCards = 50;
        }
    }
}
