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
using System.Windows.Media;
using System.Windows.Threading;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Notion
{
    [Plugin("AIA.Notion", "Notion Integration", IconSymbol = "DocumentText20", IsBuiltIn = true)]
    public class NotionPlugin : PluginBase
    {
        private NotionTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.Notion";
        public override string Name => "Notion Integration";
        public override string Description => "Searches Notion pages and databases, opens them, adds comments and links them to AIA tasks and reminders";
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
            LogInfo("Initializing Notion plugin...");
            await Context.Settings.LoadAsync();

            _tabViewModel = new NotionTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "notion",
                Title = "Notion",
                IconSymbol = "DocumentText20",
                Order = 95,
                ViewModel = _tabViewModel,
                BadgeColor = "#FF000000"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-notion",
                Text = "Notion",
                IconSymbol = "DocumentText20",
                Tooltip = "Open Notion in browser",
                Order = 95,
                OnClick = () => NotionTabViewModel.OpenNotionHome(Context.UI)
            });

            LogInfo("Notion plugin initialized");
        }

        protected override async Task OnStartAsync()
        {
            LogInfo("Starting Notion plugin...");

            var refreshMinutes = Math.Clamp(Context.Settings.Get("RefreshIntervalMinutes", 10), 1, 120);
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

            if (_tabViewModel != null)
            {
                await _tabViewModel.RefreshAsync();
            }

            LogInfo("Notion plugin started");
        }

        protected override Task OnStopAsync()
        {
            LogInfo("Stopping Notion plugin...");
            _refreshTimer?.Stop();
            _refreshTimer = null;
            LogInfo("Notion plugin stopped");
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Context.UI.UnregisterTab("notion");
            Context.UI.UnregisterToolbarButton("open-notion");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new NotionSettingsViewModel(Context.Settings);
        }
    }

    /// <summary>
    /// View model for the Notion tab.
    /// </summary>
    public class NotionTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private const string NotionHomeUrl = "https://www.notion.so";
        private const string SyncSettingsKey = "sync.records.notion";

        private readonly IPluginContext _context;
        private readonly NotionService _service;
        private readonly NotionSyncAdapter _adapter;
        private readonly SyncRecordStore _store;
        private readonly SyncCoordinator _coordinator;
        private readonly CancellationTokenSource _lifetimeCts = new();
        private readonly RelayCommand _saveConfigCommand;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _searchCommand;
        private readonly RelayCommand _openNotionCommand;
        private readonly RelayCommand _openItemCommand;
        private readonly RelayCommand _addCommentCommand;
        private readonly RelayCommand _createTaskCommand;
        private readonly RelayCommand _createReminderCommand;
        private DataTemplate? _cachedTemplate;
        private bool _isLoading;
        private bool _isConfigured;
        private bool _disposed;
        private string _integrationTokenInput = string.Empty;
        private string _searchText = string.Empty;
        private string _completedPropertyName = string.Empty;
        private string _completedPropertyType = "checkbox";
        private string _duePropertyName = string.Empty;
        private string _statusMessage = string.Empty;
        private string _commentText = string.Empty;
        private NotionItemViewModel? _selectedItem;
        private int _maxItems = 50;

        public ObservableCollection<NotionItemViewModel> Items { get; } = new();

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

        public string IntegrationTokenInput
        {
            get => _integrationTokenInput;
            set => SetProperty(ref _integrationTokenInput, value);
        }

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public string CompletedPropertyName
        {
            get => _completedPropertyName;
            set => SetProperty(ref _completedPropertyName, value);
        }

        public string CompletedPropertyType
        {
            get => _completedPropertyType;
            set => SetProperty(ref _completedPropertyType, value);
        }

        public string DuePropertyName
        {
            get => _duePropertyName;
            set => SetProperty(ref _duePropertyName, value);
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

        public NotionItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    _openItemCommand.RaiseCanExecuteChanged();
                    _addCommentCommand.RaiseCanExecuteChanged();
                    _createTaskCommand.RaiseCanExecuteChanged();
                    _createReminderCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public System.Windows.Input.ICommand SaveConfigCommand => _saveConfigCommand;
        public System.Windows.Input.ICommand RefreshCommand => _refreshCommand;
        public System.Windows.Input.ICommand SearchCommand => _searchCommand;
        public System.Windows.Input.ICommand OpenNotionCommand => _openNotionCommand;
        public System.Windows.Input.ICommand OpenItemCommand => _openItemCommand;
        public System.Windows.Input.ICommand AddCommentCommand => _addCommentCommand;
        public System.Windows.Input.ICommand CreateTaskCommand => _createTaskCommand;
        public System.Windows.Input.ICommand CreateReminderCommand => _createReminderCommand;

        public NotionTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new NotionService(context);
            _store = new SyncRecordStore(context.Settings, SyncSettingsKey);
            _adapter = new NotionSyncAdapter(_service);
            _coordinator = new SyncCoordinator(context, _adapter, _store);

            _saveConfigCommand = new RelayCommand(SaveConfigAsync);
            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading);
            _searchCommand = new RelayCommand(RefreshAsync, () => !IsLoading);
            _openNotionCommand = new RelayCommand(() => OpenNotionHome(_context.UI));
            _openItemCommand = new RelayCommand(OpenSelectedItem, () => SelectedItem != null);
            _addCommentCommand = new RelayCommand(
                AddCommentAsync,
                () => SelectedItem != null && !string.IsNullOrWhiteSpace(CommentText));
            _createTaskCommand = new RelayCommand(CreateTaskAsync, () => SelectedItem != null);
            _createReminderCommand = new RelayCommand(CreateReminderAsync, () => SelectedItem != null);
        }

        public async Task InitializeAsync()
        {
            IntegrationTokenInput = _context.Settings.Get("IntegrationToken", string.Empty) ?? string.Empty;
            CompletedPropertyName = _context.Settings.Get("CompletedPropertyName", string.Empty) ?? string.Empty;
            CompletedPropertyType = _context.Settings.Get("CompletedPropertyType", "checkbox") ?? "checkbox";
            DuePropertyName = _context.Settings.Get("DuePropertyName", string.Empty) ?? string.Empty;
            _maxItems = Math.Clamp(_context.Settings.Get("MaxItems", 50), 1, 100);

            _service.Configure(IntegrationTokenInput, BuildSyncConfig(out _));
            IsConfigured = _service.IsConfigured;

            await _store.LoadAsync(_lifetimeCts.Token).ConfigureAwait(false);
            _coordinator.Start();

            if (!IsConfigured)
            {
                StatusMessage = "Add a Notion integration token to get started.";
            }
            else if (_service.IsSyncConfigured)
            {
                StatusMessage = "Ready. Two-way sync is enabled for completion and due date.";
            }
            else
            {
                StatusMessage = "Ready. Tasks and reminders are created one-way.";
            }
        }

        public async Task RefreshAsync()
        {
            if (_disposed)
                return;

            if (!_service.IsConfigured)
            {
                PluginUiThread.Run(() =>
                    StatusMessage = "Add a Notion integration token to get started.");
                return;
            }

            if (IsLoading)
                return;

            PluginUiThread.Run(() =>
            {
                IsLoading = true;
                StatusMessage = "Searching Notion...";
                _refreshCommand.RaiseCanExecuteChanged();
                _searchCommand.RaiseCanExecuteChanged();
            });

            try
            {
                var token = _lifetimeCts.Token;
                var query = SearchText;
                var items = await _service.SearchAsync(query, _maxItems, token).ConfigureAwait(false);

                var selectedId = PluginUiThread.Get(() => SelectedItem?.Id);

                PluginUiThread.Run(() =>
                {
                    Items.Clear();
                    foreach (var item in items)
                    {
                        Items.Add(new NotionItemViewModel(item));
                    }

                    SelectedItem = Items.FirstOrDefault(i => i.Id == selectedId) ?? Items.FirstOrDefault();
                    IsConfigured = true;
                    StatusMessage = BuildRefreshStatus(items.Count);
                });

                await _coordinator.ReconcileAsync(BuildSyncStates(items), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Notion refresh failed: {ex.Message}", ex);
                PluginUiThread.Run(() => StatusMessage = ex.Message);
                _context.UI.ShowToast(ex.Message, ToastType.Error);
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

        private string BuildRefreshStatus(int count)
        {
            var suffix = _service.IsSyncConfigured
                ? "two-way sync on"
                : "one-way (set completed and due properties for two-way sync)";

            return count == 0
                ? "No items found. Share pages with the integration in Notion first."
                : $"{count} item(s) · {suffix}";
        }

        private async Task SaveConfigAsync()
        {
            var syncConfig = BuildSyncConfig(out var invalidType);

            if (invalidType)
            {
                PluginUiThread.Run(() =>
                    StatusMessage = "Property type must be \"checkbox\" or \"status\".");
                _context.UI.ShowToast(
                    "Notion property type must be \"checkbox\" or \"status\".",
                    ToastType.Warning);
                return;
            }

            _service.Configure(IntegrationTokenInput, syncConfig);

            _context.Settings.Set("IntegrationToken", IntegrationTokenInput?.Trim() ?? string.Empty);
            _context.Settings.Set("CompletedPropertyName", CompletedPropertyName?.Trim() ?? string.Empty);
            _context.Settings.Set("CompletedPropertyType",
                string.IsNullOrWhiteSpace(CompletedPropertyType) ? "checkbox" : CompletedPropertyType.Trim());
            _context.Settings.Set("DuePropertyName", DuePropertyName?.Trim() ?? string.Empty);

            try
            {
                await _context.Settings.SaveAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Notion settings save failed: {ex.Message}", ex);
                _context.UI.ShowToast($"Unable to save Notion settings: {ex.Message}", ToastType.Error);
            }

            var configured = _service.IsConfigured;
            PluginUiThread.Run(() =>
            {
                IsConfigured = configured;
                StatusMessage = configured
                    ? (_service.IsSyncConfigured
                        ? "Configuration saved. Two-way sync is enabled."
                        : "Configuration saved. Tasks and reminders are created one-way.")
                    : "Configuration cleared.";
                _refreshCommand.RaiseCanExecuteChanged();
                _searchCommand.RaiseCanExecuteChanged();
            });

            if (configured)
            {
                await RefreshAsync().ConfigureAwait(false);
            }
        }

        private NotionSyncPropertyConfig? BuildSyncConfig(out bool invalidType)
        {
            invalidType = false;

            var propertyName = CompletedPropertyName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(propertyName))
                return null;

            var config = new NotionSyncPropertyConfig
            {
                CompletedPropertyName = propertyName,
                CompletedPropertyType = string.IsNullOrWhiteSpace(CompletedPropertyType)
                    ? "checkbox"
                    : CompletedPropertyType.Trim(),
                DuePropertyName = DuePropertyName?.Trim() ?? string.Empty
            };

            if (!config.HasValidCompletedPropertyType)
            {
                invalidType = true;
                return null;
            }

            return config;
        }

        private async Task AddCommentAsync()
        {
            var item = SelectedItem;
            var comment = CommentText?.Trim();

            if (item == null || string.IsNullOrEmpty(comment))
                return;

            if (!string.Equals(item.ObjectType, "page", StringComparison.OrdinalIgnoreCase))
            {
                _context.UI.ShowToast(
                    "Comments can only be added to Notion pages, not databases.",
                    ToastType.Warning);
                return;
            }

            try
            {
                await _service.AddCommentAsync(item.Id, comment, _lifetimeCts.Token).ConfigureAwait(false);

                PluginUiThread.Run(() =>
                {
                    CommentText = string.Empty;
                    StatusMessage = $"Comment added to \"{item.TitleLine}\".";
                });
                _context.UI.ShowToast("Comment added to Notion.", ToastType.Success);
            }
            catch (OperationCanceledException)
            {
            }
            catch (NotionApiException ex)
            {
                _context.Logger.Warning($"Notion add comment failed: {ex.Message}");

                if (ex.StatusCode == 403)
                {
                    _context.UI.ShowToast(
                        "Notion integration cannot comment on this item. Enable the comment (insert content) capability for the integration in Notion, or add the comment directly in Notion.",
                        ToastType.Warning);
                }
                else
                {
                    _context.UI.ShowToast($"Unable to add Notion comment: {ex.Message}", ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Notion add comment failed: {ex.Message}", ex);
                _context.UI.ShowToast($"Unable to add Notion comment: {ex.Message}", ToastType.Error);
            }
        }

        private async Task CreateTaskAsync()
        {
            var item = SelectedItem;
            if (item == null)
                return;

            try
            {
                await PluginUiThread.RunAsync(() => _coordinator.LinkTaskAsync(
                    CreateState(item),
                    item.TitleLine,
                    BuildDescription(item),
                    _lifetimeCts.Token)).ConfigureAwait(false);

                _context.UI.ShowToast($"Created AIA task for \"{item.TitleLine}\".", ToastType.Success);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Notion create task failed: {ex.Message}", ex);
                _context.UI.ShowToast($"Unable to create task: {ex.Message}", ToastType.Error);
            }
        }

        private async Task CreateReminderAsync()
        {
            var item = SelectedItem;
            if (item == null)
                return;

            try
            {
                await PluginUiThread.RunAsync(() => _coordinator.LinkReminderAsync(
                    CreateState(item),
                    item.TitleLine,
                    _lifetimeCts.Token)).ConfigureAwait(false);

                _context.UI.ShowToast($"Created AIA reminder for \"{item.TitleLine}\".", ToastType.Success);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Notion create reminder failed: {ex.Message}", ex);
                _context.UI.ShowToast($"Unable to create reminder: {ex.Message}", ToastType.Error);
            }
        }

        private void OpenSelectedItem()
        {
            var item = SelectedItem;
            if (item == null)
                return;

            if (!TryOpenUrl(item.Url))
            {
                _context.UI.ShowToast("Unable to open the Notion item.", ToastType.Warning);
            }
        }

        public static void OpenNotionHome(IPluginUIService? ui)
        {
            if (!TryOpenUrl(NotionHomeUrl))
            {
                ui?.ShowToast("Unable to open Notion.", ToastType.Warning);
            }
        }

        private static bool TryOpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

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

        private static ExternalSyncState CreateState(NotionItemViewModel item)
        {
            return new ExternalSyncState
            {
                SourceId = item.Id,
                SourceUrl = item.Url,
                Title = item.TitleLine,
                IsCompleted = null,
                DueUtc = null
            };
        }

        private static List<ExternalSyncState> BuildSyncStates(IEnumerable<NotionItemData> items)
        {
            var states = new List<ExternalSyncState>();

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                    continue;

                states.Add(new ExternalSyncState
                {
                    SourceId = item.Id,
                    SourceUrl = item.Url,
                    Title = item.Title,
                    IsCompleted = null,
                    DueUtc = null
                });
            }

            return states;
        }

        private static string BuildDescription(NotionItemViewModel item)
        {
            var url = item.Url ?? string.Empty;
            return string.IsNullOrWhiteSpace(url)
                ? $"Notion {item.TypeLine}"
                : $"{url}{Environment.NewLine}Notion {item.TypeLine}";
        }

        public override DataTemplate GetDataTemplate()
        {
            _cachedTemplate ??= CreateDataTemplate();
            return _cachedTemplate;
        }

        public override void Refresh()
        {
            _ = RefreshAsync();
        }

        public override void OnActivated()
        {
            if (Items.Count == 0 && _service.IsConfigured)
            {
                _ = RefreshAsync();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _lifetimeCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _coordinator.Dispose();
            _lifetimeCts.Dispose();
        }

        #region Data template

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

            var search = CreateSearchBar();
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

            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var titlePanel = new FrameworkElementFactory(typeof(StackPanel));
            titlePanel.SetValue(Grid.ColumnProperty, 0);
            titlePanel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            titlePanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0));
            titlePanel.AppendChild(PluginUiFactory.CreateText(
                text: "Notion",
                fontSize: 16,
                fontWeight: FontWeights.SemiBold));
            titlePanel.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "StatusMessage",
                fontSize: 11,
                foreground: MutedBrush,
                wrap: true));
            grid.AppendChild(titlePanel);

            var refresh = PluginUiFactory.CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 1);
            refresh.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(refresh);

            var open = PluginUiFactory.CreateButton("Open Notion", "OpenNotionCommand", NeutralBrush);
            open.SetValue(Grid.ColumnProperty, 2);
            open.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            grid.AppendChild(open);

            return grid;
        }

        private FrameworkElementFactory CreateConfigPanel()
        {
            var panel = new FrameworkElementFactory(typeof(Grid));
            panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            panel.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));
            panel.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            panel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            panel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            panel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            panel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            panel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));

            panel.AppendChild(CreateFieldLabel("Integration token", 0));
            panel.AppendChild(CreateConfigTextBox(
                "IntegrationTokenInput",
                0,
                "Internal integration secret from https://www.notion.so/my-integrations"));

            panel.AppendChild(CreateFieldLabel("Completed property", 1));
            panel.AppendChild(CreateConfigTextBox(
                "CompletedPropertyName",
                1,
                "Optional. Notion database property that holds completion state"));

            panel.AppendChild(CreateFieldLabel("Property type", 2));
            panel.AppendChild(CreateConfigTextBox(
                "CompletedPropertyType",
                2,
                "\"checkbox\" or \"status\""));

            panel.AppendChild(CreateFieldLabel("Due date property", 3));
            panel.AppendChild(CreateConfigTextBox(
                "DuePropertyName",
                3,
                "Optional. Notion database property that holds the due date"));

            var actions = new FrameworkElementFactory(typeof(StackPanel));
            actions.SetValue(Grid.ColumnProperty, 1);
            actions.SetValue(Grid.RowProperty, 4);
            actions.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            actions.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            actions.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));

            var hint = PluginUiFactory.CreateText(
                text: "Completed + due properties enable two-way sync; without both, tasks and reminders are one-way.",
                fontSize: 10,
                foreground: MutedBrush,
                wrap: true);
            hint.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            hint.SetValue(FrameworkElement.MaxWidthProperty, 360.0);
            hint.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            actions.AppendChild(hint);

            actions.AppendChild(PluginUiFactory.CreateButton("Save & connect", "SaveConfigCommand", AccentBrush));
            panel.AppendChild(actions);

            return panel;
        }

        private static FrameworkElementFactory CreateFieldLabel(string text, int row)
        {
            var label = PluginUiFactory.CreateText(text: text, fontSize: 11, foreground: LabelBrush);
            label.SetValue(Grid.ColumnProperty, 0);
            label.SetValue(Grid.RowProperty, row);
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 3));
            return label;
        }

        private static FrameworkElementFactory CreateConfigTextBox(string bindingPath, int row, string tooltip)
        {
            var box = PluginUiFactory.CreateInput(bindingPath, tooltip);
            box.SetValue(Grid.ColumnProperty, 1);
            box.SetValue(Grid.RowProperty, row);
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 3));
            return box;
        }

        private static FrameworkElementFactory CreateSearchBar()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));

            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var box = PluginUiFactory.CreateInput(
                "SearchText",
                "Search pages and databases shared with the integration. Leave empty to list recent items.");
            box.SetValue(Grid.ColumnProperty, 0);
            grid.AppendChild(box);

            var search = PluginUiFactory.CreateButton("Search", "SearchCommand", AccentBrush);
            search.SetValue(Grid.ColumnProperty, 1);
            search.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            grid.AppendChild(search);

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

            var listHeader = PluginUiFactory.CreateSectionHeader("NOTION ITEMS");
            listHeader.SetValue(Grid.RowProperty, 0);
            listPanel.AppendChild(listHeader);

            var list = PluginUiFactory.CreateListBox(
                "Items",
                "SelectedItem",
                PluginUiFactory.CreateListItemTemplate("TitleLine", "SubtitleLine", "MetaLine"));
            list.SetValue(Grid.RowProperty, 1);
            listPanel.AppendChild(list);

            grid.AppendChild(listPanel);

            var details = CreateDetailsPanel();
            details.SetValue(Grid.ColumnProperty, 1);
            grid.AppendChild(details);

            return grid;
        }

        private static FrameworkElementFactory CreateDetailsPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));

            grid.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateRow(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            grid.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));

            var header = new FrameworkElementFactory(typeof(Grid));
            header.SetValue(Grid.RowProperty, 0);
            header.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
            header.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            header.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var headerInfo = new FrameworkElementFactory(typeof(StackPanel));
            headerInfo.SetValue(Grid.ColumnProperty, 0);
            headerInfo.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            headerInfo.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "SelectedItem.TitleLine",
                fontSize: 14,
                fontWeight: FontWeights.SemiBold,
                wrap: true));
            headerInfo.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "SelectedItem.SubtitleLine",
                fontSize: 11,
                foreground: MutedBrush,
                trim: true));
            header.AppendChild(headerInfo);

            var openItem = PluginUiFactory.CreateButton("Open item", "OpenItemCommand", NeutralBrush);
            openItem.SetValue(Grid.ColumnProperty, 1);
            openItem.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            header.AppendChild(openItem);

            grid.AppendChild(header);

            var info = new FrameworkElementFactory(typeof(Grid));
            info.SetValue(Grid.RowProperty, 1);
            info.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
            info.AppendChild(PluginUiFactory.CreateColumn(new GridLength(90)));
            info.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            info.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            info.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            info.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));

            info.AppendChild(CreateInfoRow("Type", "SelectedItem.TypeLine", 0));
            info.AppendChild(CreateInfoRow("Parent", "SelectedItem.ParentDisplay", 1));
            info.AppendChild(CreateInfoRow("Updated", "SelectedItem.UpdatedDisplay", 2));

            grid.AppendChild(info);

            var commentPanel = new FrameworkElementFactory(typeof(StackPanel));
            commentPanel.SetValue(Grid.RowProperty, 3);

            commentPanel.AppendChild(PluginUiFactory.CreateSectionHeader("COMMENT"));

            var commentRow = new FrameworkElementFactory(typeof(Grid));
            commentRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));
            commentRow.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            commentRow.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var commentBox = PluginUiFactory.CreateInput(
                "CommentText",
                "Add a comment to the selected Notion page");
            commentBox.SetValue(Grid.ColumnProperty, 0);
            commentRow.AppendChild(commentBox);

            var commentButton = PluginUiFactory.CreateButton("Add comment", "AddCommentCommand", AccentBrush);
            commentButton.SetValue(Grid.ColumnProperty, 1);
            commentButton.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            commentRow.AppendChild(commentButton);

            commentPanel.AppendChild(commentRow);
            grid.AppendChild(commentPanel);

            var buttons = new FrameworkElementFactory(typeof(StackPanel));
            buttons.SetValue(Grid.RowProperty, 4);
            buttons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            buttons.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);

            var createTask = PluginUiFactory.CreateButton("Create task", "CreateTaskCommand", NeutralBrush);
            createTask.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(createTask);
            buttons.AppendChild(PluginUiFactory.CreateButton("Create reminder", "CreateReminderCommand", AccentBrush));

            grid.AppendChild(buttons);

            return grid;
        }

        private static FrameworkElementFactory CreateInfoRow(string label, string bindingPath, int row)
        {
            var container = new FrameworkElementFactory(typeof(Grid));
            container.SetValue(Grid.RowProperty, row);
            container.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 3));
            container.AppendChild(PluginUiFactory.CreateColumn(new GridLength(90)));
            container.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));

            var labelBlock = PluginUiFactory.CreateText(text: label, fontSize: 11, foreground: LabelBrush);
            labelBlock.SetValue(Grid.ColumnProperty, 0);
            labelBlock.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            container.AppendChild(labelBlock);

            var valueBlock = PluginUiFactory.CreateText(
                bindingPath: bindingPath,
                fontSize: 11,
                foreground: MutedBrush,
                wrap: true);
            valueBlock.SetValue(Grid.ColumnProperty, 1);
            container.AppendChild(valueBlock);

            return container;
        }

        #endregion

        private static readonly Brush AccentBrush = PluginUiFactory.BrushFrom(0xFF, 0x22, 0x22, 0x22);
        private static readonly Brush NeutralBrush = PluginUiFactory.BrushFrom(0x33, 0xFF, 0xFF, 0xFF);
        private static readonly Brush MutedBrush = PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF);
        private static readonly Brush LabelBrush = PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF);
    }

    /// <summary>
    /// View model for a Notion page or database.
    /// </summary>
    public class NotionItemViewModel
    {
        public string Id { get; }
        public string ObjectType { get; }
        public string Title { get; }
        public string Url { get; }
        public string ParentType { get; }
        public string ParentId { get; }
        public DateTime? LastEditedTime { get; }

        public string TitleLine =>
            string.IsNullOrWhiteSpace(Title) ? $"Untitled {TypeLine}" : Title;

        public string TypeLine =>
            string.IsNullOrWhiteSpace(ObjectType) ? "item" : ObjectType;

        public string SubtitleLine =>
            string.IsNullOrWhiteSpace(ParentType) ? TypeLine : $"{TypeLine} · {ParentType}";

        public string MetaLine =>
            LastEditedTime.HasValue
                ? $"Edited {LastEditedTime.Value.ToLocalTime():MMM dd HH:mm}"
                : "No edit time";

        public string ParentDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ParentType))
                    return "unknown";

                var kind = ParentType.Replace("_id", string.Empty, StringComparison.OrdinalIgnoreCase);
                return string.IsNullOrWhiteSpace(ParentId) ? kind : $"{kind} · {ParentId}";
            }
        }

        public string UpdatedDisplay =>
            LastEditedTime.HasValue
                ? LastEditedTime.Value.ToLocalTime().ToString("MMM dd yyyy HH:mm")
                : "unknown";

        public NotionItemViewModel(NotionItemData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            Id = data.Id;
            ObjectType = data.ObjectType;
            Title = data.Title;
            Url = data.Url;
            ParentType = data.ParentType;
            ParentId = data.ParentId;
            LastEditedTime = data.LastEditedTime;
        }
    }

    /// <summary>
    /// Settings view model for the Notion plugin.
    /// </summary>
    public class NotionSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private string _integrationToken = string.Empty;
        private string _completedPropertyName = string.Empty;
        private string _completedPropertyType = "checkbox";
        private string _duePropertyName = string.Empty;
        private int _refreshIntervalMinutes = 10;
        private int _maxItems = 50;

        public override string SettingsTitle => "Notion Settings";
        public override string SettingsDescription =>
            "Configure the Notion internal integration and optional sync properties";

        public string IntegrationToken
        {
            get => _integrationToken;
            set => SetProperty(ref _integrationToken, value);
        }

        public string CompletedPropertyName
        {
            get => _completedPropertyName;
            set => SetProperty(ref _completedPropertyName, value);
        }

        public string CompletedPropertyType
        {
            get => _completedPropertyType;
            set => SetProperty(ref _completedPropertyType, value);
        }

        public string DuePropertyName
        {
            get => _duePropertyName;
            set => SetProperty(ref _duePropertyName, value);
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

        public NotionSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Load();
        }

        public override void Load()
        {
            IntegrationToken = _storage.Get("IntegrationToken", string.Empty) ?? string.Empty;
            CompletedPropertyName = _storage.Get("CompletedPropertyName", string.Empty) ?? string.Empty;
            CompletedPropertyType = _storage.Get("CompletedPropertyType", "checkbox") ?? "checkbox";
            DuePropertyName = _storage.Get("DuePropertyName", string.Empty) ?? string.Empty;
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 10);
            MaxItems = _storage.Get("MaxItems", 50);
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("IntegrationToken", IntegrationToken?.Trim() ?? string.Empty);
            _storage.Set("CompletedPropertyName", CompletedPropertyName?.Trim() ?? string.Empty);
            _storage.Set("CompletedPropertyType",
                string.IsNullOrWhiteSpace(CompletedPropertyType) ? "checkbox" : CompletedPropertyType.Trim());
            _storage.Set("DuePropertyName", DuePropertyName?.Trim() ?? string.Empty);
            _storage.Set("RefreshIntervalMinutes", Math.Clamp(RefreshIntervalMinutes, 1, 120));
            _storage.Set("MaxItems", Math.Clamp(MaxItems, 1, 100));
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            IntegrationToken = string.Empty;
            CompletedPropertyName = string.Empty;
            CompletedPropertyType = "checkbox";
            DuePropertyName = string.Empty;
            RefreshIntervalMinutes = 10;
            MaxItems = 50;
        }
    }
}
