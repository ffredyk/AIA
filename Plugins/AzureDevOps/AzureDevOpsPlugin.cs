using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIA.Plugins.SDK;

using WpfApplication = System.Windows.Application;

namespace AIA.Plugins.AzureDevOps
{
    [Plugin("AIA.AzureDevOps", "Azure DevOps Integration", IconSymbol = "Cloud20", IsBuiltIn = true)]
    public class AzureDevOpsPlugin : PluginBase
    {
        private const string RefreshIntervalKey = "RefreshIntervalMinutes";

        private AzureDevOpsTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.AzureDevOps";
        public override string Name => "Azure DevOps Integration";
        public override string Description => "Shows assigned Azure DevOps work items and reviewer pull requests, adds comments, and links items to AIA tasks and reminders";
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
            LogInfo("Initializing Azure DevOps plugin...");
            await Context.Settings.LoadAsync();

            _tabViewModel = new AzureDevOpsTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "azure-devops",
                Title = "Azure DevOps",
                IconSymbol = "Cloud20",
                Order = 91,
                ViewModel = _tabViewModel,
                BadgeColor = "#FF0078D4"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-azure-devops",
                Text = "Azure DevOps",
                IconSymbol = "Cloud20",
                Tooltip = "Open Azure DevOps in browser",
                Order = 91,
                OnClick = () => _tabViewModel?.OpenAzureDevOps()
            });

            LogInfo("Azure DevOps plugin initialized");
        }

        protected override async Task OnStartAsync()
        {
            LogInfo("Starting Azure DevOps plugin...");

            var refreshMinutes = Math.Clamp(Context.Settings.Get(RefreshIntervalKey, 5), 1, 120);
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

            LogInfo("Azure DevOps plugin started");
        }

        protected override Task OnStopAsync()
        {
            LogInfo("Stopping Azure DevOps plugin...");
            _refreshTimer?.Stop();
            _refreshTimer = null;
            LogInfo("Azure DevOps plugin stopped");
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Context.UI.UnregisterTab("azure-devops");
            Context.UI.UnregisterToolbarButton("open-azure-devops");
            _refreshTimer?.Stop();
            _refreshTimer = null;
            _tabViewModel?.Dispose();
            _tabViewModel = null;
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new AzureDevOpsSettingsViewModel(Context.Settings);
        }
    }

    /// <summary>
    /// View model for the Azure DevOps tab.
    /// </summary>
    public class AzureDevOpsTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private const string SyncRecordsKey = "sync.records.azuredevops";

        private readonly IPluginContext _context;
        private readonly AzureDevOpsService _service;
        private readonly AzureDevOpsSyncAdapter _adapter;
        private readonly SyncRecordStore _store;
        private readonly SyncCoordinator _coordinator;
        private readonly CancellationTokenSource _cts = new();

        private readonly RelayCommand _saveConfigCommand;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _openAzureDevOpsCommand;
        private readonly RelayCommand _openItemCommand;
        private readonly RelayCommand _addCommentCommand;
        private readonly RelayCommand _createTaskCommand;
        private readonly RelayCommand _createReminderCommand;

        private DataTemplate? _cachedTemplate;
        private bool _isLoading;
        private bool _isConfigured;
        private bool _disposed;
        private string _organizationUrlInput = string.Empty;
        private string _patInput = string.Empty;
        private string _statusMessage = string.Empty;
        private string _commentText = string.Empty;
        private AzureItemViewModel? _selectedItem;
        private string _selectedItemKey = string.Empty;
        private int _maxItems = 50;

        public ObservableCollection<AzureItemViewModel> Items { get; } = new();

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

        public string OrganizationUrlInput
        {
            get => _organizationUrlInput;
            set
            {
                if (SetProperty(ref _organizationUrlInput, value))
                {
                    _saveConfigCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string PatInput
        {
            get => _patInput;
            set
            {
                if (SetProperty(ref _patInput, value))
                {
                    _saveConfigCommand.RaiseCanExecuteChanged();
                }
            }
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

        public AzureItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    _selectedItemKey = value?.Key ?? string.Empty;
                    _openItemCommand.RaiseCanExecuteChanged();
                    _addCommentCommand.RaiseCanExecuteChanged();
                    _createTaskCommand.RaiseCanExecuteChanged();
                    _createReminderCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand SaveConfigCommand => _saveConfigCommand;
        public ICommand RefreshCommand => _refreshCommand;
        public ICommand OpenAzureDevOpsCommand => _openAzureDevOpsCommand;
        public ICommand OpenItemCommand => _openItemCommand;
        public ICommand AddCommentCommand => _addCommentCommand;
        public ICommand CreateTaskCommand => _createTaskCommand;
        public ICommand CreateReminderCommand => _createReminderCommand;

        public AzureDevOpsTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new AzureDevOpsService(context);
            _adapter = new AzureDevOpsSyncAdapter(_service);
            _store = new SyncRecordStore(context.Settings, SyncRecordsKey);
            _coordinator = new SyncCoordinator(context, _adapter, _store);

            _saveConfigCommand = new RelayCommand(SaveConfigAsync);
            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading && _service.IsConfigured);
            _openAzureDevOpsCommand = new RelayCommand(OpenAzureDevOps);
            _openItemCommand = new RelayCommand(OpenSelectedItem, () => SelectedItem != null);
            _addCommentCommand = new RelayCommand(
                AddCommentAsync,
                () => SelectedItem is { IsWorkItem: true } && !string.IsNullOrWhiteSpace(CommentText));
            _createTaskCommand = new RelayCommand(CreateTaskAsync, () => SelectedItem != null);
            _createReminderCommand = new RelayCommand(CreateReminderAsync, () => SelectedItem != null);
        }

        public async Task InitializeAsync()
        {
            OrganizationUrlInput = AzureCredentialProtector.Unprotect(
                _context.Settings.Get(AzureDevOpsSettingsViewModel.OrganizationUrlKey, string.Empty) ?? string.Empty);
            PatInput = AzureCredentialProtector.Unprotect(
                _context.Settings.Get(AzureDevOpsSettingsViewModel.PatKey, string.Empty) ?? string.Empty);
            _maxItems = Math.Clamp(_context.Settings.Get(AzureDevOpsSettingsViewModel.MaxItemsKey, 50), 1, 200);

            _service.Configure(OrganizationUrlInput, PatInput);
            IsConfigured = _service.IsConfigured;

            if (!IsConfigured)
            {
                StatusMessage = "Add your Azure DevOps organization URL and personal access token to get started.";
            }

            try
            {
                await _store.LoadAsync(_cts.Token);
                _coordinator.Start();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"Azure DevOps sync could not be started: {ex.Message}");
            }
        }

        public async Task RefreshAsync()
        {
            if (!_service.IsConfigured)
            {
                RunOnUi(() => { StatusMessage = "Add your Azure DevOps organization URL and personal access token to get started."; });
                return;
            }

            if (IsLoading)
                return;

            var cancellationToken = _cts.Token;
            RunOnUi(() => { IsLoading = true; StatusMessage = "Connecting to Azure DevOps..."; });

            try
            {
                var user = await _service.TestConnectionAsync(cancellationToken);
                var workItems = await _service.GetAssignedWorkItemsAsync(_maxItems, cancellationToken);

                var pullRequests = new List<AzurePullRequestData>();
                try
                {
                    pullRequests = await _service.GetReviewerPullRequestsAsync(_maxItems, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _context.Logger.Warning($"Azure DevOps: unable to read reviewer pull requests: {ex.Message}");
                }

                var workItemViewModels = workItems.Select(AzureItemViewModel.FromWorkItem).ToList();
                var items = new List<AzureItemViewModel>(workItems.Count + pullRequests.Count);
                items.AddRange(workItemViewModels);
                items.AddRange(pullRequests.Select(AzureItemViewModel.FromPullRequest));

                RunOnUi(() =>
                {
                    Items.Clear();
                    foreach (var item in items)
                    {
                        Items.Add(item);
                    }

                    IsConfigured = true;
                    StatusMessage = $"Connected as {user.DisplayName} · {workItems.Count} work item(s) · {pullRequests.Count} review(s)";

                    SelectedItem = Items.FirstOrDefault(i => i.Key == _selectedItemKey) ?? Items.FirstOrDefault();
                });

                // Pull requests are one-way, so only work items take part in reconciliation.
                var states = workItemViewModels.Select(item => item.ToSyncState()).ToList();
                await AddLinkedWorkItemStatesAsync(states, cancellationToken);
                await _coordinator.ReconcileAsync(states, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The plugin is shutting down.
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Azure DevOps refresh failed: {ex.Message}", ex);
                RunOnUi(() =>
                {
                    StatusMessage = ex.Message;
                    _context.UI.ShowToast(ex.Message, ToastType.Error);
                });
            }
            finally
            {
                RunOnUi(() => { IsLoading = false; });
                _refreshCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Adds states for linked work items that the assigned-items query no longer returns,
        /// for example because they were closed in Azure DevOps. Without this the coordinator
        /// would drop the link instead of propagating the completion to the AIA item.
        /// </summary>
        private async Task AddLinkedWorkItemStatesAsync(List<ExternalSyncState> states, CancellationToken cancellationToken)
        {
            var knownIds = new HashSet<string>(states.Select(s => s.SourceId), StringComparer.Ordinal);

            var linkedIds = _store.Records
                .Where(r => string.Equals(r.SourceKey, _adapter.SourceKey, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.SourceId)
                .Where(id => !string.IsNullOrEmpty(id) && !knownIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var sourceId in linkedIds)
            {
                try
                {
                    var state = await _adapter.GetStateAsync(sourceId, cancellationToken);
                    if (state != null)
                    {
                        states.Add(state);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Keep the link: an error state makes the coordinator skip this record.
                    states.Add(new ExternalSyncState
                    {
                        SourceId = sourceId,
                        Error = ex.Message
                    });
                    _context.Logger.Warning($"Azure DevOps: unable to read linked work item {sourceId}: {ex.Message}");
                }
            }
        }

        private async Task SaveConfigAsync()
        {
            var organizationUrl = AzureDevOpsService.NormalizeOrganizationUrl(OrganizationUrlInput);
            var pat = PatInput?.Trim() ?? string.Empty;

            _context.Settings.Set(AzureDevOpsSettingsViewModel.OrganizationUrlKey, AzureCredentialProtector.Protect(organizationUrl));
            _context.Settings.Set(AzureDevOpsSettingsViewModel.PatKey, AzureCredentialProtector.Protect(pat));
            await _context.Settings.SaveAsync();

            _service.Configure(organizationUrl, pat);

            RunOnUi(() =>
            {
                OrganizationUrlInput = organizationUrl;
                IsConfigured = _service.IsConfigured;
                StatusMessage = IsConfigured ? "Configuration saved. Connecting..." : "Configuration cleared.";
            });

            _refreshCommand.RaiseCanExecuteChanged();

            if (IsConfigured)
            {
                await RefreshAsync();
            }
        }

        public void OpenAzureDevOps()
        {
            var url = string.IsNullOrWhiteSpace(_service.OrganizationUrl)
                ? "https://dev.azure.com"
                : _service.OrganizationUrl;

            if (!TryOpenUrl(url))
            {
                _context.UI.ShowToast("Unable to open Azure DevOps.", ToastType.Warning);
            }
        }

        private void OpenSelectedItem()
        {
            var item = SelectedItem;
            if (item == null)
                return;

            if (string.IsNullOrWhiteSpace(item.Url) || !TryOpenUrl(item.Url))
            {
                _context.UI.ShowToast("This item has no Azure DevOps link.", ToastType.Warning);
            }
        }

        private async Task AddCommentAsync()
        {
            var item = SelectedItem;
            var comment = CommentText?.Trim();

            if (item == null || !item.IsWorkItem || string.IsNullOrEmpty(comment))
                return;

            try
            {
                await _service.AddCommentAsync(item.Id, comment, _cts.Token);
                RunOnUi(() =>
                {
                    CommentText = string.Empty;
                    StatusMessage = $"Comment added to work item #{item.Id}";
                });
                _context.UI.ShowToast($"Comment added to work item #{item.Id}", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Azure DevOps add comment failed: {ex.Message}", ex);
                RunOnUi(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            }
        }

        private async Task CreateTaskAsync()
        {
            var item = SelectedItem;
            if (item == null)
                return;

            try
            {
                if (item.IsWorkItem)
                {
                    if (_store.FindBySource(_adapter.SourceKey, item.IdText).Any())
                    {
                        _context.UI.ShowToast($"Work item #{item.Id} is already linked to an AIA item.", ToastType.Warning);
                        return;
                    }

                    await _coordinator.LinkTaskAsync(item.ToSyncState(), item.Title, item.BuildDescription(), _cts.Token);
                }
                else
                {
                    await PluginUiThread.RunAsync(async () =>
                    {
                        var task = _context.Tasks.CreateTask(item.Title, item.BuildDescription());
                        task.Notes = AppendUrl(task.Notes, item.Url);
                        if (item.Due.HasValue)
                        {
                            task.DueDate = item.Due.Value.ToLocalTime();
                        }

                        await _context.Tasks.SaveAsync();
                    });
                }

                RunOnUi(() => { StatusMessage = $"Task created for {item.DisplayName}"; });
                _context.UI.ShowToast($"Task created for {item.DisplayName}", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Azure DevOps create task failed: {ex.Message}", ex);
                RunOnUi(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            }
        }

        private async Task CreateReminderAsync()
        {
            var item = SelectedItem;
            if (item == null)
                return;

            try
            {
                if (item.IsWorkItem)
                {
                    if (_store.FindBySource(_adapter.SourceKey, item.IdText).Any())
                    {
                        _context.UI.ShowToast($"Work item #{item.Id} is already linked to an AIA item.", ToastType.Warning);
                        return;
                    }

                    await _coordinator.LinkReminderAsync(item.ToSyncState(), item.Title, _cts.Token);
                }
                else
                {
                    // Reminder items have no notes field, so the Azure DevOps link goes in the title.
                    var title = string.IsNullOrWhiteSpace(item.Url)
                        ? item.Title
                        : $"{item.Title} ({item.Url})";

                    await PluginUiThread.RunAsync(async () =>
                    {
                        _context.Reminders.CreateReminder(title, item.Due?.ToLocalTime() ?? DateTime.Now.AddHours(1));
                        await _context.Reminders.SaveAsync();
                    });
                }

                RunOnUi(() => { StatusMessage = $"Reminder created for {item.DisplayName}"; });
                _context.UI.ShowToast($"Reminder created for {item.DisplayName}", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Azure DevOps create reminder failed: {ex.Message}", ex);
                RunOnUi(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            }
        }

        private static string AppendUrl(string? notes, string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return notes ?? string.Empty;

            return string.IsNullOrWhiteSpace(notes)
                ? url
                : notes.TrimEnd() + Environment.NewLine + url;
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
            _cts.Cancel();
            _coordinator.Dispose();
            _cts.Dispose();
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

        private static readonly Brush AccentBrush = PluginUiFactory.BrushFrom(0xFF, 0x00, 0x78, 0xD4);
        private static readonly Brush NeutralBrush = PluginUiFactory.BrushFrom(0x33, 0xFF, 0xFF, 0xFF);

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

            var titlePanel = new FrameworkElementFactory(typeof(StackPanel));
            titlePanel.SetValue(Grid.ColumnProperty, 1);
            titlePanel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            titlePanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0));
            titlePanel.AppendChild(PluginUiFactory.CreateText(text: "Azure DevOps", fontSize: 16, fontWeight: FontWeights.SemiBold));
            titlePanel.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "StatusMessage",
                fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));
            grid.AppendChild(titlePanel);

            var refresh = PluginUiFactory.CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 2);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(refresh);

            var open = PluginUiFactory.CreateButton("Open Azure DevOps", "OpenAzureDevOpsCommand", NeutralBrush);
            open.SetValue(Grid.ColumnProperty, 3);
            grid.AppendChild(open);

            return grid;
        }

        private FrameworkElementFactory CreateConfigPanel()
        {
            var panel = new FrameworkElementFactory(typeof(Grid));
            panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            panel.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));
            panel.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            panel.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            panel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            panel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));

            var urlLabel = PluginUiFactory.CreateText(text: "Organization URL", fontSize: 11, foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF));
            urlLabel.SetValue(Grid.ColumnProperty, 0);
            urlLabel.SetValue(Grid.RowProperty, 0);
            urlLabel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            urlLabel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 3));
            panel.AppendChild(urlLabel);

            var urlBox = PluginUiFactory.CreateInput("OrganizationUrlInput", "https://dev.azure.com/your-organization");
            urlBox.SetValue(Grid.ColumnProperty, 1);
            urlBox.SetValue(Grid.RowProperty, 0);
            urlBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 3));
            panel.AppendChild(urlBox);

            var save = PluginUiFactory.CreateButton("Save & Connect", "SaveConfigCommand", AccentBrush);
            save.SetValue(Grid.ColumnProperty, 2);
            save.SetValue(Grid.RowProperty, 0);
            save.SetValue(Grid.RowSpanProperty, 2);
            save.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            save.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            panel.AppendChild(save);

            var patLabel = PluginUiFactory.CreateText(text: "PAT", fontSize: 11, foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF));
            patLabel.SetValue(Grid.ColumnProperty, 0);
            patLabel.SetValue(Grid.RowProperty, 1);
            patLabel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            patLabel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            panel.AppendChild(patLabel);

            var patBox = PluginUiFactory.CreateInput(
                "PatInput",
                "Personal access token with Work Items (read & write) and Code (read) scopes. Stored encrypted with Windows DPAPI.");
            patBox.SetValue(Grid.ColumnProperty, 1);
            patBox.SetValue(Grid.RowProperty, 1);
            panel.AppendChild(patBox);

            return panel;
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

            var listHeader = PluginUiFactory.CreateSectionHeader("ASSIGNED ITEMS");
            listHeader.SetValue(Grid.RowProperty, 0);
            listPanel.AppendChild(listHeader);

            var list = PluginUiFactory.CreateListBox(
                "Items",
                "SelectedItem",
                PluginUiFactory.CreateListItemTemplate("Title", "Subtitle", "MetaLine"));
            list.SetValue(Grid.RowProperty, 1);
            listPanel.AppendChild(list);

            grid.AppendChild(listPanel);

            var detailsPanel = CreateDetailsPanel();
            detailsPanel.SetValue(Grid.ColumnProperty, 1);
            grid.AppendChild(detailsPanel);

            return grid;
        }

        private FrameworkElementFactory CreateDetailsPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));

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
            headerInfo.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedItem.Title", fontSize: 14, fontWeight: FontWeights.SemiBold, wrap: true));
            headerInfo.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "SelectedItem.Subtitle",
                fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF),
                trim: true));
            header.AppendChild(headerInfo);

            var openItem = PluginUiFactory.CreateButton("Open", "OpenItemCommand", NeutralBrush);
            openItem.SetValue(Grid.ColumnProperty, 1);
            openItem.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            header.AppendChild(openItem);

            grid.AppendChild(header);

            var details = new FrameworkElementFactory(typeof(StackPanel));
            details.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedItem.MetaLine", fontSize: 12));
            details.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "SelectedItem.DetailsLine",
                fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF),
                wrap: true,
                margin: new Thickness(0, 8, 0, 0)));
            details.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "SelectedItem.Url",
                fontSize: 10,
                foreground: PluginUiFactory.BrushFrom(0x88, 0xFF, 0xFF, 0xFF),
                wrap: true,
                margin: new Thickness(0, 8, 0, 0)));

            var scroll = PluginUiFactory.CreateScrollViewer(details);
            scroll.SetValue(Grid.RowProperty, 1);
            grid.AppendChild(scroll);

            var commentRow = new FrameworkElementFactory(typeof(Grid));
            commentRow.SetValue(Grid.RowProperty, 2);
            commentRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));
            commentRow.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            commentRow.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var commentBox = PluginUiFactory.CreateInput("CommentText", "Add a comment to the selected work item");
            commentBox.SetValue(Grid.ColumnProperty, 0);
            commentRow.AppendChild(commentBox);

            var commentButton = PluginUiFactory.CreateButton("Add comment", "AddCommentCommand", AccentBrush);
            commentButton.SetValue(Grid.ColumnProperty, 1);
            commentButton.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            commentRow.AppendChild(commentButton);

            grid.AppendChild(commentRow);

            var actions = new FrameworkElementFactory(typeof(StackPanel));
            actions.SetValue(Grid.RowProperty, 3);
            actions.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            actions.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            actions.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));

            var hint = PluginUiFactory.CreateText(
                text: "Work items are kept in sync with the linked AIA item.",
                fontSize: 10,
                foreground: PluginUiFactory.BrushFrom(0x88, 0xFF, 0xFF, 0xFF));
            hint.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            hint.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            actions.AppendChild(hint);

            var taskButton = PluginUiFactory.CreateButton("Create task", "CreateTaskCommand", NeutralBrush);
            taskButton.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            actions.AppendChild(taskButton);

            var reminderButton = PluginUiFactory.CreateButton("Create reminder", "CreateReminderCommand", NeutralBrush);
            actions.AppendChild(reminderButton);

            grid.AppendChild(actions);

            return grid;
        }

        #endregion
    }

    /// <summary>
    /// Kind of Azure DevOps item shown in the tab.
    /// </summary>
    public enum AzureItemKind
    {
        WorkItem,
        PullRequest
    }

    /// <summary>
    /// View model for an Azure DevOps work item or reviewer pull request.
    /// </summary>
    public class AzureItemViewModel
    {
        private AzureItemViewModel(
            AzureItemKind kind,
            int id,
            string title,
            string project,
            string state,
            string assigned,
            string repository,
            DateTime? changed,
            DateTime? due,
            string url)
        {
            Kind = kind;
            Id = id;
            Title = title;
            Project = project;
            State = state;
            Assigned = assigned;
            Repository = repository;
            Changed = changed;
            Due = due;
            Url = url;
        }

        public AzureItemKind Kind { get; }
        public int Id { get; }
        public string Title { get; }
        public string Project { get; }
        public string State { get; }
        public string Assigned { get; }
        public string Repository { get; }
        public DateTime? Changed { get; }
        public DateTime? Due { get; }
        public string Url { get; }

        public bool IsWorkItem => Kind == AzureItemKind.WorkItem;
        public string KindLabel => Kind == AzureItemKind.WorkItem ? "WorkItem" : "PR";
        public string IdText => Id.ToString(CultureInfo.InvariantCulture);
        public string DisplayName => IsWorkItem ? $"work item #{Id}" : $"PR #{Id}";
        public string Key => $"{KindLabel}:{Id}";

        public string Subtitle => string.IsNullOrWhiteSpace(Project)
            ? KindLabel
            : $"{KindLabel} · {Project}";

        public string MetaLine => BuildMetaLine();

        public string DetailsLine
        {
            get
            {
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(Project))
                    parts.Add($"Project: {Project}");
                if (!string.IsNullOrWhiteSpace(Repository))
                    parts.Add($"Repository: {Repository}");
                if (!string.IsNullOrWhiteSpace(Assigned))
                    parts.Add(IsWorkItem ? $"Assignee: {Assigned}" : $"Author: {Assigned}");
                if (Due.HasValue)
                    parts.Add($"Due: {Due.Value:yyyy-MM-dd}");

                return string.Join("   ", parts);
            }
        }

        public bool IsCompleted => IsWorkItem && AzureDevOpsService.IsCompletedState(State);

        public static AzureItemViewModel FromWorkItem(AzureWorkItemData data)
        {
            return new AzureItemViewModel(
                AzureItemKind.WorkItem,
                data.Id,
                data.Title,
                data.Project,
                data.State,
                data.AssignedTo,
                string.Empty,
                data.ChangedDate == DateTime.MinValue ? null : data.ChangedDate,
                data.DueDate,
                data.Url);
        }

        public static AzureItemViewModel FromPullRequest(AzurePullRequestData data)
        {
            return new AzureItemViewModel(
                AzureItemKind.PullRequest,
                data.Id,
                data.Title,
                data.Project,
                data.Status,
                data.CreatedBy,
                data.Repository,
                data.CreatedDate == DateTime.MinValue ? null : data.CreatedDate,
                null,
                data.Url);
        }

        /// <summary>
        /// Snapshot used for bidirectional sync. Pull requests are one-way and never use this.
        /// </summary>
        public ExternalSyncState ToSyncState()
        {
            return new ExternalSyncState
            {
                SourceId = IdText,
                SourceUrl = Url ?? string.Empty,
                Title = Title,
                IsCompleted = IsWorkItem ? AzureDevOpsService.IsCompletedState(State) : null,
                DueUtc = Due.HasValue
                    ? (Due.Value.Kind == DateTimeKind.Utc ? Due.Value : Due.Value.ToUniversalTime())
                    : null
            };
        }

        public string BuildDescription()
        {
            var parts = new List<string>
            {
                IsWorkItem ? $"Azure DevOps work item #{Id}" : $"Azure DevOps pull request #{Id}"
            };

            if (!string.IsNullOrWhiteSpace(Project))
                parts.Add(Project!);
            if (!string.IsNullOrWhiteSpace(State))
                parts.Add(State);
            if (!string.IsNullOrWhiteSpace(Url))
                parts.Add(Url);

            return string.Join(" · ", parts);
        }

        private string BuildMetaLine()
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(State))
                parts.Add(State);
            if (!string.IsNullOrWhiteSpace(Assigned))
                parts.Add(IsWorkItem ? $"assigned to {Assigned}" : $"by {Assigned}");
            if (Changed.HasValue)
                parts.Add($"changed {Changed.Value:MMM dd HH:mm}");

            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Settings view model for the Azure DevOps plugin.
    /// </summary>
    public class AzureDevOpsSettingsViewModel : PluginSettingsViewModelBase
    {
        internal const string OrganizationUrlKey = "OrganizationUrl";
        internal const string PatKey = "Pat";
        internal const string RefreshIntervalKey = "RefreshIntervalMinutes";
        internal const string MaxItemsKey = "MaxItems";

        private readonly IPluginSettingsStorage _storage;
        private string _organizationUrl = string.Empty;
        private string _pat = string.Empty;
        private int _refreshIntervalMinutes = 5;
        private int _maxItems = 50;

        public override string SettingsTitle => "Azure DevOps Settings";
        public override string SettingsDescription => "Configure the Azure DevOps organization connection";

        public string OrganizationUrl
        {
            get => _organizationUrl;
            set => SetProperty(ref _organizationUrl, value);
        }

        public string Pat
        {
            get => _pat;
            set => SetProperty(ref _pat, value);
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

        public AzureDevOpsSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Load();
        }

        public override void Load()
        {
            OrganizationUrl = AzureCredentialProtector.Unprotect(_storage.Get(OrganizationUrlKey, string.Empty) ?? string.Empty);
            Pat = AzureCredentialProtector.Unprotect(_storage.Get(PatKey, string.Empty) ?? string.Empty);
            RefreshIntervalMinutes = _storage.Get(RefreshIntervalKey, 5);
            MaxItems = _storage.Get(MaxItemsKey, 50);
            IsDirty = false;
        }

        public override void Save()
        {
            var organizationUrl = AzureDevOpsService.NormalizeOrganizationUrl(OrganizationUrl);
            var refreshMinutes = Math.Clamp(RefreshIntervalMinutes, 1, 120);
            var maxItems = Math.Clamp(MaxItems, 1, 200);

            _storage.Set(OrganizationUrlKey, AzureCredentialProtector.Protect(organizationUrl));
            _storage.Set(PatKey, AzureCredentialProtector.Protect(Pat?.Trim() ?? string.Empty));
            _storage.Set(RefreshIntervalKey, refreshMinutes);
            _storage.Set(MaxItemsKey, maxItems);
            _ = _storage.SaveAsync();

            OrganizationUrl = organizationUrl;
            RefreshIntervalMinutes = refreshMinutes;
            MaxItems = maxItems;
            IsDirty = false;
        }

        public override void Reset()
        {
            OrganizationUrl = string.Empty;
            Pat = string.Empty;
            RefreshIntervalMinutes = 5;
            MaxItems = 50;
        }
    }

    /// <summary>
    /// Protects the organization URL and personal access token at rest with Windows DPAPI.
    /// Values are prefixed with "enc:v1:" so any legacy plaintext value is migrated on save.
    /// </summary>
    internal static class AzureCredentialProtector
    {
        private const string Prefix = "enc:v1:";

        public static bool IsProtected(string? value) =>
            value != null && value.StartsWith(Prefix, StringComparison.Ordinal);

        public static string Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            if (IsProtected(plainText))
                return plainText;

            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainText),
                null,
                DataProtectionScope.CurrentUser);

            return Prefix + Convert.ToBase64String(encrypted);
        }

        public static string Unprotect(string? storedValue)
        {
            if (string.IsNullOrEmpty(storedValue))
                return string.Empty;

            if (!IsProtected(storedValue))
                return storedValue;

            try
            {
                var payload = Convert.FromBase64String(storedValue.Substring(Prefix.Length));
                var decrypted = ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                // Another Windows user or machine encrypted the value; treat it as unusable.
                return string.Empty;
            }
        }
    }
}
