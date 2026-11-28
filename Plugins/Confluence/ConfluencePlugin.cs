using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Confluence
{
    [Plugin("AIA.Confluence", "Confluence Integration", IconSymbol = "DocumentText20", IsBuiltIn = true)]
    public class ConfluencePlugin : PluginBase
    {
        private ConfluenceTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.Confluence";
        public override string Name => "Confluence Integration";
        public override string Description => "Searches Confluence pages and saves them as AIA tasks or reminders";
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
            _tabViewModel = new ConfluenceTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "confluence",
                Title = "Confluence",
                IconSymbol = "DocumentText20",
                Order = 96,
                ViewModel = _tabViewModel,
                BadgeColor = "#FF0052CC"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-confluence",
                Text = "Confluence",
                IconSymbol = "DocumentText20",
                Tooltip = "Open Confluence",
                Order = 96,
                OnClick = () => _tabViewModel?.OpenConfluence()
            });
        }

        protected override async Task OnStartAsync()
        {
            var refreshMinutes = Math.Clamp(Context.Settings.Get("RefreshIntervalMinutes", 10), 1, 120);
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
            Context.UI.UnregisterTab("confluence");
            Context.UI.UnregisterToolbarButton("open-confluence");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new ConfluenceSettingsViewModel(Context.Settings);
        }
    }

    public class ConfluenceTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private readonly IPluginContext _context;
        private readonly ConfluenceService _service;
        private readonly ConfluenceSyncAdapter _adapter;
        private readonly SyncRecordStore _syncStore;
        private readonly SyncCoordinator _sync;
        private readonly RelayCommand _saveConfigCommand;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _searchCommand;
        private readonly RelayCommand _openPageCommand;
        private readonly RelayCommand _addCommentCommand;
        private readonly RelayCommand _createTaskCommand;
        private readonly RelayCommand _createReminderCommand;
        private DataTemplate? _cachedTemplate;
        private bool _initialized;
        private bool _isLoading;
        private bool _isConfigured;
        private string _siteUrlInput = string.Empty;
        private string _emailInput = string.Empty;
        private string _apiTokenInput = string.Empty;
        private string _searchText = string.Empty;
        private string _statusMessage = string.Empty;
        private string _commentText = string.Empty;
        private ConfluencePageViewModel? _selectedPage;
        private int _maxPages = 50;

        public ObservableCollection<ConfluencePageViewModel> Pages { get; } = new();

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

        public string SiteUrlInput
        {
            get => _siteUrlInput;
            set => SetProperty(ref _siteUrlInput, value);
        }

        public string EmailInput
        {
            get => _emailInput;
            set => SetProperty(ref _emailInput, value);
        }

        public string ApiTokenInput
        {
            get => _apiTokenInput;
            set => SetProperty(ref _apiTokenInput, value);
        }

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
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

        public ConfluencePageViewModel? SelectedPage
        {
            get => _selectedPage;
            set
            {
                if (SetProperty(ref _selectedPage, value))
                {
                    _openPageCommand.RaiseCanExecuteChanged();
                    _addCommentCommand.RaiseCanExecuteChanged();
                    _createTaskCommand.RaiseCanExecuteChanged();
                    _createReminderCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand SaveConfigCommand => _saveConfigCommand;
        public ICommand RefreshCommand => _refreshCommand;
        public ICommand SearchCommand => _searchCommand;
        public ICommand OpenPageCommand => _openPageCommand;
        public ICommand AddCommentCommand => _addCommentCommand;
        public ICommand CreateTaskCommand => _createTaskCommand;
        public ICommand CreateReminderCommand => _createReminderCommand;
        public ICommand OpenConfluenceCommand { get; }

        public ConfluenceTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new ConfluenceService(context);
            _adapter = new ConfluenceSyncAdapter(_service);
            _syncStore = new SyncRecordStore(context.Settings, "sync.records.confluence");
            _sync = new SyncCoordinator(context, _adapter, _syncStore);

            _saveConfigCommand = new RelayCommand(SaveConfigAsync, () => !IsLoading);
            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading && _service.IsConfigured);
            _searchCommand = new RelayCommand(RefreshAsync, () => !IsLoading && _service.IsConfigured);
            _openPageCommand = new RelayCommand(OpenSelectedPage, () => SelectedPage != null);
            _addCommentCommand = new RelayCommand(AddCommentAsync,
                () => SelectedPage != null && !string.IsNullOrWhiteSpace(CommentText));
            _createTaskCommand = new RelayCommand(CreateTaskAsync, () => SelectedPage != null);
            _createReminderCommand = new RelayCommand(CreateReminderAsync, () => SelectedPage != null);
            OpenConfluenceCommand = new RelayCommand(OpenConfluence);
        }

        public async Task InitializeAsync()
        {
            SiteUrlInput = _context.Settings.Get("SiteUrl", string.Empty) ?? string.Empty;
            EmailInput = _context.Settings.Get("Email", string.Empty) ?? string.Empty;
            ApiTokenInput = _context.Settings.Get("ApiToken", string.Empty) ?? string.Empty;
            _maxPages = Math.Clamp(_context.Settings.Get("MaxPages", 50), 5, 200);

            _service.Configure(SiteUrlInput, EmailInput, ApiTokenInput);
            IsConfigured = _service.IsConfigured;

            await _syncStore.LoadAsync(CancellationToken.None);
            _sync.Start();
            _initialized = true;

            if (!IsConfigured)
            {
                StatusMessage = "Add your Confluence site, email and API token to get started.";
            }
        }

        public async Task RefreshAsync()
        {
            if (!_service.IsConfigured)
            {
                PluginUiThread.Run(() => { StatusMessage = "Add your Confluence site, email and API token to get started."; });
                return;
            }

            if (IsLoading)
                return;

            PluginUiThread.Run(() =>
            {
                IsLoading = true;
                StatusMessage = "Searching Confluence...";
            });

            try
            {
                var user = await _service.TestConnectionAsync();
                var pages = await _service.SearchAsync(SearchText, _maxPages);

                PluginUiThread.Run(() =>
                {
                    Pages.Clear();
                    foreach (var page in pages)
                    {
                        Pages.Add(new ConfluencePageViewModel(page));
                    }

                    IsConfigured = true;
                    StatusMessage = $"Connected as {user.DisplayName} · {Pages.Count} page(s)";
                });

                var states = new List<ExternalSyncState>();
                foreach (var page in pages)
                {
                    states.Add(new ExternalSyncState
                    {
                        SourceId = page.Id,
                        SourceUrl = page.Url,
                        Title = page.Title,
                        IsCompleted = null,
                        DueUtc = null
                    });
                }

                await _sync.ReconcileAsync(states, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Confluence search failed: {ex.Message}", ex);
                ReportError(ex);
            }
            finally
            {
                PluginUiThread.Run(() =>
                {
                    IsLoading = false;
                    _refreshCommand.RaiseCanExecuteChanged();
                    _searchCommand.RaiseCanExecuteChanged();
                });
            }
        }

        private async Task SaveConfigAsync()
        {
            _service.Configure(SiteUrlInput, EmailInput, ApiTokenInput);
            _context.Settings.Set("SiteUrl", ConfluenceService.NormalizeSiteUrl(SiteUrlInput));
            _context.Settings.Set("Email", EmailInput?.Trim() ?? string.Empty);
            _context.Settings.Set("ApiToken", ApiTokenInput?.Trim() ?? string.Empty);
            await _context.Settings.SaveAsync();

            PluginUiThread.Run(() =>
            {
                IsConfigured = _service.IsConfigured;
                StatusMessage = IsConfigured ? "Configuration saved. Connecting..." : "Configuration cleared.";
                _refreshCommand.RaiseCanExecuteChanged();
                _searchCommand.RaiseCanExecuteChanged();
            });

            if (IsConfigured)
            {
                await RefreshAsync();
            }
        }

        private async Task AddCommentAsync()
        {
            var page = SelectedPage;
            var text = CommentText?.Trim();
            if (page == null || string.IsNullOrEmpty(text))
                return;

            try
            {
                await _service.AddCommentAsync(page.Id, text);
                PluginUiThread.Run(() =>
                {
                    CommentText = string.Empty;
                    StatusMessage = $"Comment added to {page.Title}";
                });
                _context.UI.ShowToast("Comment added", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Confluence comment failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CreateTaskAsync()
        {
            var page = SelectedPage;
            if (page == null)
                return;

            try
            {
                await _sync.LinkTaskAsync(BuildState(page), page.Title, page.Url, CancellationToken.None);
                _context.UI.ShowToast("AIA task created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Confluence task creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CreateReminderAsync()
        {
            var page = SelectedPage;
            if (page == null)
                return;

            try
            {
                await _sync.LinkReminderAsync(BuildState(page), page.Title, CancellationToken.None);
                _context.UI.ShowToast("AIA reminder created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Confluence reminder creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private void OpenSelectedPage()
        {
            if (SelectedPage != null && OpenUrl(SelectedPage.Url))
                return;

            _context.UI.ShowToast("Unable to open Confluence.", ToastType.Warning);
        }

        public void OpenConfluence()
        {
            var url = string.IsNullOrWhiteSpace(_service.SiteUrl)
                ? "https://www.atlassian.com/software/confluence"
                : _service.SiteUrl;

            if (!OpenUrl(url))
            {
                _context.UI.ShowToast("Unable to open Confluence.", ToastType.Warning);
            }
        }

        private static ExternalSyncState BuildState(ConfluencePageViewModel page)
        {
            return new ExternalSyncState
            {
                SourceId = page.Id,
                SourceUrl = page.Url,
                Title = page.Title,
                IsCompleted = null,
                DueUtc = null
            };
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
            if (_initialized && Pages.Count == 0 && _service.IsConfigured)
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
            root.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            root.AppendChild(PluginUiFactory.CreateRow(new GridLength(1, GridUnitType.Star)));

            var header = CreateHeader();
            header.SetValue(Grid.RowProperty, 0);
            root.AppendChild(header);

            var config = CreateConfigPanel();
            config.SetValue(Grid.RowProperty, 1);
            root.AppendChild(config);

            var search = CreateSearchPanel();
            search.SetValue(Grid.RowProperty, 2);
            root.AppendChild(search);

            var content = CreateContentPanel();
            content.SetValue(Grid.RowProperty, 3);
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
            info.AppendChild(PluginUiFactory.CreateText(text: "Confluence", fontSize: 16, fontWeight: FontWeights.SemiBold));
            info.AppendChild(PluginUiFactory.CreateText(bindingPath: "StatusMessage", fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));
            grid.AppendChild(info);

            var refresh = PluginUiFactory.CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 2);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(refresh);

            var open = PluginUiFactory.CreateButton("Open Confluence", "OpenConfluenceCommand", NeutralBrush);
            open.SetValue(Grid.ColumnProperty, 3);
            grid.AppendChild(open);

            return grid;
        }

        private FrameworkElementFactory CreateConfigPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var label = PluginUiFactory.CreateText(text: "Site / email / token", fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF));
            label.SetValue(Grid.ColumnProperty, 0);
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            grid.AppendChild(label);

            var site = PluginUiFactory.CreateInput("SiteUrlInput",
                "Confluence site URL, for example https://your-domain.atlassian.net");
            site.SetValue(Grid.ColumnProperty, 1);
            site.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(site);

            var email = PluginUiFactory.CreateInput("EmailInput", "Atlassian account email");
            email.SetValue(Grid.ColumnProperty, 2);
            email.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(email);

            var token = PluginUiFactory.CreateInput("ApiTokenInput",
                "Atlassian API token from https://id.atlassian.com/manage-profile/security/api-tokens");
            token.SetValue(Grid.ColumnProperty, 3);
            token.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(token);

            var save = PluginUiFactory.CreateButton("Save & Connect", "SaveConfigCommand", AccentBrush);
            save.SetValue(Grid.ColumnProperty, 4);
            grid.AppendChild(save);

            return grid;
        }

        private FrameworkElementFactory CreateSearchPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var search = PluginUiFactory.CreateInput("SearchText",
                "Optional search text. Leave empty to list recently modified pages.");
            search.SetValue(Grid.ColumnProperty, 0);
            search.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            grid.AppendChild(search);

            var button = PluginUiFactory.CreateButton("Search", "SearchCommand", AccentBrush);
            button.SetValue(Grid.ColumnProperty, 1);
            grid.AppendChild(button);

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

            var listHeader = PluginUiFactory.CreateSectionHeader("PAGES");
            listHeader.SetValue(Grid.RowProperty, 0);
            listPanel.AppendChild(listHeader);

            var list = PluginUiFactory.CreateListBox("Pages", "SelectedPage",
                PluginUiFactory.CreateListItemTemplate("TitleLine", "SubtitleLine", "MetaLine"));
            list.SetValue(Grid.RowProperty, 1);
            listPanel.AppendChild(list);
            grid.AppendChild(listPanel);

            var details = new FrameworkElementFactory(typeof(StackPanel));
            details.SetValue(Grid.ColumnProperty, 1);

            details.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedPage.Title",
                fontSize: 15, fontWeight: FontWeights.SemiBold, wrap: true));
            details.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedPage.SubtitleLine",
                fontSize: 11, foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));

            var meta = PluginUiFactory.CreateText(bindingPath: "SelectedPage.MetaLine",
                fontSize: 11, foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF));
            meta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 10));
            details.AppendChild(meta);

            var buttons = new FrameworkElementFactory(typeof(StackPanel));
            buttons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            buttons.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            var open = PluginUiFactory.CreateButton("Open", "OpenPageCommand", AccentBrush);
            open.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(open);

            var task = PluginUiFactory.CreateButton("Create AIA task", "CreateTaskCommand", NeutralBrush);
            task.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(task);

            buttons.AppendChild(PluginUiFactory.CreateButton("Create reminder", "CreateReminderCommand", NeutralBrush));
            details.AppendChild(buttons);

            details.AppendChild(PluginUiFactory.CreateInput("CommentText", "Comment to add to the selected page"));

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

        private static readonly Brush AccentBrush = PluginUiFactory.BrushFrom(0xFF, 0x00, 0x52, 0xCC);
        private static readonly Brush NeutralBrush = PluginUiFactory.BrushFrom(0x33, 0xFF, 0xFF, 0xFF);
    }

    public class ConfluencePageViewModel
    {
        public string Id { get; }
        public string Title { get; }
        public string SpaceName { get; }
        public int Version { get; }
        public DateTime? Updated { get; }
        public string Url { get; }

        public string TitleLine => Title;
        public string SubtitleLine => string.IsNullOrWhiteSpace(SpaceName) ? "Confluence page" : SpaceName;
        public string MetaLine => Updated.HasValue
            ? $"v{Version} · {Updated.Value:MMM dd HH:mm}"
            : $"v{Version}";

        public ConfluencePageViewModel(ConfluencePageData data)
        {
            Id = data.Id;
            Title = data.Title;
            SpaceName = data.SpaceName;
            Version = data.Version;
            Updated = data.Updated;
            Url = data.Url;
        }
    }

    public class ConfluenceSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private string _siteUrl = string.Empty;
        private string _email = string.Empty;
        private string _apiToken = string.Empty;
        private int _refreshIntervalMinutes = 10;
        private int _maxPages = 50;

        public override string SettingsTitle => "Confluence Settings";
        public override string SettingsDescription => "Configure the Confluence connection";

        public string SiteUrl
        {
            get => _siteUrl;
            set => SetProperty(ref _siteUrl, value);
        }

        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value);
        }

        public string ApiToken
        {
            get => _apiToken;
            set => SetProperty(ref _apiToken, value);
        }

        public int RefreshIntervalMinutes
        {
            get => _refreshIntervalMinutes;
            set => SetProperty(ref _refreshIntervalMinutes, value);
        }

        public int MaxPages
        {
            get => _maxPages;
            set => SetProperty(ref _maxPages, value);
        }

        public ConfluenceSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage;
            Load();
        }

        public override void Load()
        {
            SiteUrl = _storage.Get("SiteUrl", string.Empty) ?? string.Empty;
            Email = _storage.Get("Email", string.Empty) ?? string.Empty;
            ApiToken = _storage.Get("ApiToken", string.Empty) ?? string.Empty;
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 10);
            MaxPages = _storage.Get("MaxPages", 50);
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("SiteUrl", ConfluenceService.NormalizeSiteUrl(SiteUrl));
            _storage.Set("Email", Email);
            _storage.Set("ApiToken", ApiToken);
            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("MaxPages", MaxPages);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            SiteUrl = string.Empty;
            Email = string.Empty;
            ApiToken = string.Empty;
            RefreshIntervalMinutes = 10;
            MaxPages = 50;
        }
    }
}
