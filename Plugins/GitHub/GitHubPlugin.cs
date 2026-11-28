using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIA.Plugins.SDK;

namespace AIA.Plugins.GitHub
{
    [Plugin("AIA.GitHub", "GitHub Integration", IconSymbol = "Code20", IsBuiltIn = true)]
    public class GitHubPlugin : PluginBase
    {
        private GitHubTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.GitHub";
        public override string Name => "GitHub Integration";
        public override string Description => "Shows assigned issues, review requests and notifications from GitHub";
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
            LogInfo("Initializing GitHub plugin...");
            await Context.Settings.LoadAsync();

            _tabViewModel = new GitHubTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "github",
                Title = "GitHub",
                IconSymbol = "Code20",
                Order = 90,
                ViewModel = _tabViewModel,
                BadgeColor = "#FF24292F"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-github",
                Text = "GitHub",
                IconSymbol = "Code20",
                Tooltip = "Open GitHub",
                Order = 90,
                OnClick = () => GitHubTabViewModel.OpenUrl("https://github.com")
            });

            LogInfo("GitHub plugin initialized");
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
            Context.UI.UnregisterTab("github");
            Context.UI.UnregisterToolbarButton("open-github");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new GitHubSettingsViewModel(Context.Settings);
        }
    }

    public class GitHubTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private readonly IPluginContext _context;
        private readonly GitHubService _service;
        private readonly GitHubSyncAdapter _adapter;
        private readonly SyncRecordStore _syncStore;
        private readonly SyncCoordinator _sync;
        private readonly RelayCommand _saveConfigCommand;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _openItemCommand;
        private readonly RelayCommand _addCommentCommand;
        private readonly RelayCommand _markReadCommand;
        private readonly RelayCommand _createTaskCommand;
        private readonly RelayCommand _createReminderCommand;
        private DataTemplate? _cachedTemplate;
        private bool _isLoading;
        private bool _isConfigured;
        private bool _initialized;
        private string _tokenInput = string.Empty;
        private string _apiBaseUrlInput = string.Empty;
        private string _statusMessage = string.Empty;
        private string _commentText = string.Empty;
        private GitHubItemViewModel? _selectedItem;
        private int _maxItems = 30;

        public ObservableCollection<GitHubItemViewModel> Items { get; } = new();

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

        public string TokenInput
        {
            get => _tokenInput;
            set => SetProperty(ref _tokenInput, value);
        }

        public string ApiBaseUrlInput
        {
            get => _apiBaseUrlInput;
            set => SetProperty(ref _apiBaseUrlInput, value);
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

        public GitHubItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    _openItemCommand.RaiseCanExecuteChanged();
                    _addCommentCommand.RaiseCanExecuteChanged();
                    _markReadCommand.RaiseCanExecuteChanged();
                    _createTaskCommand.RaiseCanExecuteChanged();
                    _createReminderCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand SaveConfigCommand => _saveConfigCommand;
        public ICommand RefreshCommand => _refreshCommand;
        public ICommand OpenItemCommand => _openItemCommand;
        public ICommand AddCommentCommand => _addCommentCommand;
        public ICommand MarkNotificationReadCommand => _markReadCommand;
        public ICommand CreateTaskCommand => _createTaskCommand;
        public ICommand CreateReminderCommand => _createReminderCommand;
        public ICommand OpenGitHubCommand { get; }

        public GitHubTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new GitHubService(context);
            _adapter = new GitHubSyncAdapter(_service);
            _syncStore = new SyncRecordStore(context.Settings, "sync.records.github");
            _sync = new SyncCoordinator(context, _adapter, _syncStore);

            _saveConfigCommand = new RelayCommand(SaveConfigAsync, () => !IsLoading);
            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading && _service.IsConfigured);
            _openItemCommand = new RelayCommand(OpenSelectedItem, () => SelectedItem != null);
            _addCommentCommand = new RelayCommand(AddCommentAsync, () => CanComment());
            _markReadCommand = new RelayCommand(MarkNotificationReadAsync, () => SelectedItem?.IsNotification == true);
            _createTaskCommand = new RelayCommand(CreateTaskAsync, () => SelectedItem != null);
            _createReminderCommand = new RelayCommand(CreateReminderAsync, () => SelectedItem != null);
            OpenGitHubCommand = new RelayCommand(() => OpenUrl("https://github.com"));
        }

        public async Task InitializeAsync()
        {
            TokenInput = _context.Settings.Get("Token", string.Empty) ?? string.Empty;
            ApiBaseUrlInput = _context.Settings.Get("ApiBaseUrl", string.Empty) ?? string.Empty;
            _maxItems = Math.Clamp(_context.Settings.Get("MaxItems", 30), 5, 100);

            _service.Configure(TokenInput, ApiBaseUrlInput);
            IsConfigured = _service.IsConfigured;

            await _syncStore.LoadAsync(CancellationToken.None);
            _sync.Start();
            _initialized = true;

            if (!IsConfigured)
            {
                StatusMessage = "Add a GitHub token to get started.";
            }
        }

        public async Task RefreshAsync()
        {
            if (!_service.IsConfigured)
            {
                PluginUiThread.Run(() => { StatusMessage = "Add a GitHub token to get started."; });
                return;
            }

            if (IsLoading)
                return;

            PluginUiThread.Run(() =>
            {
                IsLoading = true;
                StatusMessage = "Connecting to GitHub...";
            });

            try
            {
                var user = await _service.TestConnectionAsync();
                var issues = await _service.GetAssignedIssuesAsync(_maxItems);
                var pullRequests = await _service.GetReviewRequestedPullRequestsAsync(_maxItems);
                var notifications = await _service.GetUnreadNotificationsAsync(_maxItems);

                PluginUiThread.Run(() =>
                {
                    Items.Clear();
                    foreach (var item in notifications)
                    {
                        Items.Add(new GitHubItemViewModel(item));
                    }
                    foreach (var item in pullRequests)
                    {
                        Items.Add(new GitHubItemViewModel(item));
                    }
                    foreach (var item in issues)
                    {
                        Items.Add(new GitHubItemViewModel(item));
                    }

                    IsConfigured = true;
                    StatusMessage = $"Connected as {user.Login} · {Items.Count} item(s)";
                });

                var states = issues
                    .Where(issue => !issue.IsPullRequest && issue.Number > 0 && !string.IsNullOrWhiteSpace(issue.Repository))
                    .Select(issue => new ExternalSyncState
                    {
                        SourceId = GitHubSyncAdapter.GetSourceId(issue.Repository, issue.Number),
                        SourceUrl = issue.Url,
                        Title = issue.Title,
                        IsCompleted = false,
                        DueUtc = null
                    })
                    .ToList();

                await AddLinkedStatesAsync(states, CancellationToken.None);
                await _sync.ReconcileAsync(states, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"GitHub refresh failed: {ex.Message}", ex);
                PluginUiThread.Run(() =>
                {
                    StatusMessage = ex.Message;
                    _context.UI.ShowToast(ex.Message, ToastType.Error);
                });
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
                    _context.Logger.Warning($"GitHub: unable to read linked issue state: {ex.Message}");
                    states.Add(new ExternalSyncState { SourceId = sourceId, Error = ex.Message });
                }
            }
        }

        private async Task SaveConfigAsync()
        {
            _service.Configure(TokenInput, ApiBaseUrlInput);

            _context.Settings.Set("Token", TokenInput?.Trim() ?? string.Empty);
            _context.Settings.Set("ApiBaseUrl", GitHubService.NormalizeBaseUrl(ApiBaseUrlInput));
            await _context.Settings.SaveAsync();

            PluginUiThread.Run(() =>
            {
                IsConfigured = _service.IsConfigured;
                StatusMessage = IsConfigured ? "Configuration saved. Connecting..." : "Token cleared.";
                _refreshCommand.RaiseCanExecuteChanged();
            });

            if (IsConfigured)
            {
                await RefreshAsync();
            }
        }

        private bool CanComment()
        {
            return SelectedItem != null
                && !SelectedItem.IsNotification
                && SelectedItem.Number > 0
                && !string.IsNullOrWhiteSpace(SelectedItem.Repository)
                && !string.IsNullOrWhiteSpace(CommentText);
        }

        private async Task AddCommentAsync()
        {
            var item = SelectedItem;
            var comment = CommentText?.Trim();
            if (item == null || string.IsNullOrEmpty(comment))
                return;

            try
            {
                await _service.AddCommentAsync(item.Repository, item.Number, comment);
                PluginUiThread.Run(() =>
                {
                    CommentText = string.Empty;
                    StatusMessage = $"Comment added to {item.Repository}#{item.Number}";
                });
                _context.UI.ShowToast("Comment added", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"GitHub comment failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task MarkNotificationReadAsync()
        {
            var item = SelectedItem;
            if (item == null || !item.IsNotification)
                return;

            try
            {
                await _service.MarkNotificationReadAsync(item.NotificationThreadId);
                PluginUiThread.Run(() =>
                {
                    Items.Remove(item);
                    SelectedItem = null;
                    StatusMessage = "Notification marked read";
                });
                _context.UI.ShowToast("Notification marked read", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"GitHub notification update failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CreateTaskAsync()
        {
            var item = SelectedItem;
            if (item == null)
                return;

            try
            {
                if (item.CanSync)
                {
                    await _sync.LinkTaskAsync(BuildSyncState(item), item.Title, BuildDescription(item), CancellationToken.None);
                }
                else
                {
                    var task = _context.Tasks.CreateTask(item.Title, BuildDescription(item));
                    task.Notes = item.Url;
                    await _context.Tasks.SaveAsync();
                }

                _context.UI.ShowToast("AIA task created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"GitHub task creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CreateReminderAsync()
        {
            var item = SelectedItem;
            if (item == null)
                return;

            try
            {
                if (item.CanSync)
                {
                    await _sync.LinkReminderAsync(BuildSyncState(item), item.Title, CancellationToken.None);
                }
                else
                {
                    _context.Reminders.CreateReminder(item.Title, DateTime.Now.AddHours(1));
                    await _context.Reminders.SaveAsync();
                }

                _context.UI.ShowToast("AIA reminder created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"GitHub reminder creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private void OpenSelectedItem()
        {
            if (SelectedItem == null)
                return;

            if (!OpenUrl(SelectedItem.Url))
            {
                _context.UI.ShowToast("Unable to open the browser.", ToastType.Warning);
            }
        }

        private static ExternalSyncState BuildSyncState(GitHubItemViewModel item)
        {
            return new ExternalSyncState
            {
                SourceId = GitHubSyncAdapter.GetSourceId(item.Repository, item.Number),
                SourceUrl = item.Url,
                Title = item.Title,
                IsCompleted = false,
                DueUtc = null
            };
        }

        private static string BuildDescription(GitHubItemViewModel item)
        {
            var body = item.Body?.Trim() ?? string.Empty;
            if (body.Length > 500)
                body = body.Substring(0, 500) + "...";

            return string.IsNullOrEmpty(body)
                ? item.Url
                : body + Environment.NewLine + Environment.NewLine + item.Url;
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
            if (_initialized && Items.Count == 0 && _service.IsConfigured)
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
            info.AppendChild(PluginUiFactory.CreateText(text: "GitHub", fontSize: 16, fontWeight: FontWeights.SemiBold));
            info.AppendChild(PluginUiFactory.CreateText(bindingPath: "StatusMessage", fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));
            grid.AppendChild(info);

            var refresh = PluginUiFactory.CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 2);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(refresh);

            var open = PluginUiFactory.CreateButton("Open GitHub", "OpenGitHubCommand", NeutralBrush);
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

            var tokenLabel = PluginUiFactory.CreateText(text: "Token", fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF));
            tokenLabel.SetValue(Grid.ColumnProperty, 0);
            tokenLabel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tokenLabel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            grid.AppendChild(tokenLabel);

            var tokenBox = PluginUiFactory.CreateInput("TokenInput",
                "GitHub PAT. Classic token needs repo and notifications; fine-grained needs Issues and Notifications read/write.");
            tokenBox.SetValue(Grid.ColumnProperty, 1);
            tokenBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            grid.AppendChild(tokenBox);

            var baseBox = PluginUiFactory.CreateInput("ApiBaseUrlInput",
                "Optional GitHub Enterprise API URL, for example https://github.company.com/api/v3. Leave empty for github.com.");
            baseBox.SetValue(Grid.ColumnProperty, 2);
            baseBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            grid.AppendChild(baseBox);

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

            var listHeader = PluginUiFactory.CreateSectionHeader("NOTIFICATIONS · REVIEW REQUESTS · ISSUES");
            listHeader.SetValue(Grid.RowProperty, 0);
            listPanel.AppendChild(listHeader);

            var list = PluginUiFactory.CreateListBox("Items", "SelectedItem",
                PluginUiFactory.CreateListItemTemplate("TitleLine", "SubtitleLine", "MetaLine"));
            list.SetValue(Grid.RowProperty, 1);
            listPanel.AppendChild(list);
            grid.AppendChild(listPanel);

            var details = new FrameworkElementFactory(typeof(StackPanel));
            details.SetValue(Grid.ColumnProperty, 1);

            var selectedTitle = PluginUiFactory.CreateText(bindingPath: "SelectedItem.TitleLine",
                fontSize: 15, fontWeight: FontWeights.SemiBold, wrap: true);
            details.AppendChild(selectedTitle);

            var selectedMeta = PluginUiFactory.CreateText(bindingPath: "SelectedItem.MetaLine",
                fontSize: 11, foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF));
            selectedMeta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 8));
            details.AppendChild(selectedMeta);

            var body = PluginUiFactory.CreateText(bindingPath: "SelectedItem.BodyPreview",
                fontSize: 12, foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF), wrap: true);
            body.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
            details.AppendChild(body);

            var buttons = new FrameworkElementFactory(typeof(StackPanel));
            buttons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            buttons.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            var open = PluginUiFactory.CreateButton("Open", "OpenItemCommand", AccentBrush);
            open.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(open);

            var markRead = PluginUiFactory.CreateButton("Mark read", "MarkNotificationReadCommand", NeutralBrush);
            markRead.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(markRead);

            var task = PluginUiFactory.CreateButton("Create AIA task", "CreateTaskCommand", NeutralBrush);
            task.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(task);

            buttons.AppendChild(PluginUiFactory.CreateButton("Create reminder", "CreateReminderCommand", NeutralBrush));
            details.AppendChild(buttons);

            var comment = PluginUiFactory.CreateInput("CommentText", "Comment to add to the selected issue or pull request");
            details.AppendChild(comment);

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

        private static readonly Brush AccentBrush = PluginUiFactory.BrushFrom(0xFF, 0x2D, 0xA4, 0x4E);
        private static readonly Brush NeutralBrush = PluginUiFactory.BrushFrom(0x33, 0xFF, 0xFF, 0xFF);
    }

    public class GitHubItemViewModel
    {
        public string Id { get; }
        public int Number { get; }
        public string Repository { get; }
        public string Title { get; }
        public string Body { get; }
        public string Url { get; }
        public bool IsPullRequest { get; }
        public bool IsNotification { get; }
        public string NotificationThreadId { get; }
        public string Kind { get; }

        public bool CanSync => !IsNotification && !IsPullRequest && Number > 0 && !string.IsNullOrWhiteSpace(Repository);

        public string TitleLine => IsNotification
            ? $"{Repository} · {Title}"
            : $"{Repository} #{Number} · {Title}";

        public string SubtitleLine => IsNotification
            ? "Notification"
            : IsPullRequest ? "Pull request" : "Issue";

        public string MetaLine => Kind;

        public string BodyPreview => string.IsNullOrWhiteSpace(Body) ? "(no description)" : Body;

        public GitHubItemViewModel(GitHubItemData data)
        {
            Id = data.Id;
            Number = data.Number;
            Repository = data.Repository;
            Title = data.Title;
            Body = data.Body;
            Url = data.Url;
            IsPullRequest = data.IsPullRequest;
            IsNotification = data.IsNotification;
            NotificationThreadId = data.NotificationThreadId;
            Kind = data.Kind;
        }
    }

    public class GitHubSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private string _token = string.Empty;
        private string _apiBaseUrl = string.Empty;
        private int _refreshIntervalMinutes = 5;
        private int _maxItems = 30;

        public override string SettingsTitle => "GitHub Settings";
        public override string SettingsDescription => "Configure the GitHub connection";

        public string Token
        {
            get => _token;
            set => SetProperty(ref _token, value);
        }

        public string ApiBaseUrl
        {
            get => _apiBaseUrl;
            set => SetProperty(ref _apiBaseUrl, value);
        }

        public int RefreshIntervalMinutes
        {
            get => _refreshIntervalMinutes;
            set => SetProperty(ref _refreshIntervalMinutes, value);
        }

        public int MaxItems
        {
            get => _maxItems;
            set => SetProperty(ref _maxItems, value);
        }

        public GitHubSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage;
            Load();
        }

        public override void Load()
        {
            Token = _storage.Get("Token", string.Empty) ?? string.Empty;
            ApiBaseUrl = _storage.Get("ApiBaseUrl", string.Empty) ?? string.Empty;
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 5);
            MaxItems = _storage.Get("MaxItems", 30);
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("Token", Token);
            _storage.Set("ApiBaseUrl", GitHubService.NormalizeBaseUrl(ApiBaseUrl));
            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("MaxItems", MaxItems);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            Token = string.Empty;
            ApiBaseUrl = string.Empty;
            RefreshIntervalMinutes = 5;
            MaxItems = 30;
        }
    }
}
