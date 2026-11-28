using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIA.Plugins.SDK;

namespace AIA.Plugins.ClickUp
{
    [Plugin("AIA.ClickUp", "ClickUp Integration", IconSymbol = "TaskListSquareLtr20", IsBuiltIn = true)]
    public class ClickUpPlugin : PluginBase
    {
        private const string IconSymbol = "TaskListSquareLtr20";

        private ClickUpTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.ClickUp";
        public override string Name => "ClickUp Integration";
        public override string Description => "Shows ClickUp tasks assigned to you and lets you comment, complete and link them to AIA tasks and reminders";
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
            LogInfo("Initializing ClickUp plugin...");
            await Context.Settings.LoadAsync();

            _tabViewModel = new ClickUpTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "clickup",
                Title = "ClickUp",
                IconSymbol = IconSymbol,
                Order = 94,
                ViewModel = _tabViewModel,
                BadgeColor = "#FF7B68EE"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-clickup",
                Text = "ClickUp",
                IconSymbol = IconSymbol,
                Tooltip = "Open ClickUp",
                Order = 94,
                OnClick = () => ClickUpTabViewModel.OpenClickUpApp()
            });

            LogInfo("ClickUp plugin initialized");
        }

        protected override async Task OnStartAsync()
        {
            LogInfo("Starting ClickUp plugin...");

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

            if (_tabViewModel != null)
            {
                await _tabViewModel.RefreshAsync();
            }

            LogInfo("ClickUp plugin started");
        }

        protected override Task OnStopAsync()
        {
            LogInfo("Stopping ClickUp plugin...");
            _refreshTimer?.Stop();
            _refreshTimer = null;
            LogInfo("ClickUp plugin stopped");
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Context.UI.UnregisterTab("clickup");
            Context.UI.UnregisterToolbarButton("open-clickup");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new ClickUpSettingsViewModel(Context.Settings);
        }
    }

    /// <summary>
    /// View model for the ClickUp tab.
    /// </summary>
    public class ClickUpTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private const string SyncRecordsKey = "sync.records.clickup";

        private static readonly Brush AccentBrush = PluginUiFactory.BrushFrom(0xFF, 0x7B, 0x68, 0xEE);
        private static readonly Brush NeutralBrush = PluginUiFactory.BrushFrom(0x33, 0xFF, 0xFF, 0xFF);
        private static readonly Brush MutedBrush = PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF);
        private static readonly Brush FaintBrush = PluginUiFactory.BrushFrom(0x88, 0xFF, 0xFF, 0xFF);
        private static readonly Brush BodyBrush = PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF);

        private readonly IPluginContext _context;
        private readonly ClickUpService _service;
        private readonly RelayCommand _saveConfigCommand;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _openTaskCommand;
        private readonly RelayCommand _addCommentCommand;
        private readonly RelayCommand _completeTaskCommand;
        private readonly RelayCommand _createTaskCommand;
        private readonly RelayCommand _createReminderCommand;
        private SyncRecordStore? _syncStore;
        private ClickUpSyncAdapter? _syncAdapter;
        private SyncCoordinator? _syncCoordinator;
        private DataTemplate? _cachedTemplate;
        private bool _isLoading;
        private bool _isConfigured;
        private string _tokenInput = string.Empty;
        private string _statusMessage = string.Empty;
        private string _commentText = string.Empty;
        private ClickUpTaskViewModel? _selectedTask;
        private string _selectedTaskId = string.Empty;
        private int _maxTasks = 50;

        public ObservableCollection<ClickUpTaskViewModel> Tasks { get; } = new();

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
            set
            {
                if (SetProperty(ref _tokenInput, value))
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

        public ClickUpTaskViewModel? SelectedTask
        {
            get => _selectedTask;
            set
            {
                if (SetProperty(ref _selectedTask, value))
                {
                    _selectedTaskId = value?.Id ?? string.Empty;
                    _openTaskCommand.RaiseCanExecuteChanged();
                    _addCommentCommand.RaiseCanExecuteChanged();
                    _completeTaskCommand.RaiseCanExecuteChanged();
                    _createTaskCommand.RaiseCanExecuteChanged();
                    _createReminderCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand SaveConfigCommand => _saveConfigCommand;
        public ICommand RefreshCommand => _refreshCommand;
        public ICommand OpenTaskCommand => _openTaskCommand;
        public ICommand AddCommentCommand => _addCommentCommand;
        public ICommand CompleteTaskCommand => _completeTaskCommand;
        public ICommand CreateTaskCommand => _createTaskCommand;
        public ICommand CreateReminderCommand => _createReminderCommand;
        public ICommand OpenClickUpCommand { get; }

        public ClickUpTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new ClickUpService(context);

            _saveConfigCommand = new RelayCommand(SaveConfigAsync);
            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading && _service.IsConfigured);
            _openTaskCommand = new RelayCommand(OpenSelectedTask, () => SelectedTask != null);
            _addCommentCommand = new RelayCommand(
                AddCommentAsync,
                () => SelectedTask != null && !string.IsNullOrWhiteSpace(CommentText));
            _completeTaskCommand = new RelayCommand(CompleteTaskAsync, () => SelectedTask != null);
            _createTaskCommand = new RelayCommand(CreateTaskAsync, () => SelectedTask != null);
            _createReminderCommand = new RelayCommand(CreateReminderAsync, () => SelectedTask != null);
            OpenClickUpCommand = new RelayCommand(OpenClickUp);
        }

        public async Task InitializeAsync()
        {
            var storedToken = _context.Settings.Get("Token", string.Empty) ?? string.Empty;
            TokenInput = ClickUpTokenProtector.Unprotect(storedToken, _context.Logger);
            _maxTasks = Math.Clamp(_context.Settings.Get("MaxTasks", 50), 1, 500);

            if (!string.IsNullOrEmpty(TokenInput) && !ClickUpTokenProtector.IsProtected(storedToken))
            {
                await TryPersistTokenAsync(TokenInput);
            }

            _service.Configure(TokenInput);
            IsConfigured = _service.IsConfigured;

            if (!IsConfigured)
            {
                StatusMessage = "Add your ClickUp personal API token to get started.";
            }

            await EnsureSyncAsync();
        }

        public async Task RefreshAsync()
        {
            if (!_service.IsConfigured)
            {
                PluginUiThread.Run(() => { StatusMessage = "Add your ClickUp personal API token to get started."; });
                return;
            }

            if (IsLoading)
                return;

            PluginUiThread.Run(() =>
            {
                IsLoading = true;
                StatusMessage = "Connecting to ClickUp...";
            });

            try
            {
                var user = await _service.TestConnectionAsync().ConfigureAwait(false);
                var tasks = await _service.GetMyTasksAsync(_maxTasks).ConfigureAwait(false);

                PluginUiThread.Run(() =>
                {
                    Tasks.Clear();
                    foreach (var task in tasks)
                    {
                        Tasks.Add(new ClickUpTaskViewModel(task));
                    }

                    IsConfigured = true;
                    var name = string.IsNullOrWhiteSpace(user.Username) ? "your account" : user.Username;
                    StatusMessage = $"Connected as {name} · {Tasks.Count} task(s)";
                });

                var target = PluginUiThread.Get(() =>
                    Tasks.FirstOrDefault(t => t.Id == _selectedTaskId) ?? Tasks.FirstOrDefault());

                PluginUiThread.Run(() => { SelectedTask = target; });

                await ReconcileAsync(tasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"ClickUp refresh failed: {ex.Message}", ex);
                PluginUiThread.Run(() =>
                {
                    StatusMessage = ex.Message;
                    _context.UI.ShowToast(ex.Message, ToastType.Error);
                });
            }
            finally
            {
                PluginUiThread.Run(() => { IsLoading = false; });
                _refreshCommand.RaiseCanExecuteChanged();
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
            if (Tasks.Count == 0 && _service.IsConfigured)
            {
                _ = RefreshAsync();
            }
        }

        public void Dispose()
        {
            _syncCoordinator?.Dispose();
            _syncCoordinator = null;
            _syncStore = null;
        }

        public static bool OpenClickUpApp()
        {
            return TryOpenUrl("https://app.clickup.com");
        }

        private async Task SaveConfigAsync()
        {
            var token = TokenInput?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(token))
            {
                _context.Settings.Set("Token", string.Empty);
                await _context.Settings.SaveAsync().ConfigureAwait(false);
            }
            else if (!await TryPersistTokenAsync(token).ConfigureAwait(false))
            {
                return;
            }

            _service.Configure(token);

            PluginUiThread.Run(() =>
            {
                IsConfigured = _service.IsConfigured;
                StatusMessage = IsConfigured
                    ? "Token saved. Connecting..."
                    : "ClickUp token cleared.";
            });

            _refreshCommand.RaiseCanExecuteChanged();

            if (IsConfigured)
            {
                await RefreshAsync();
            }
        }

        private async Task AddCommentAsync()
        {
            var task = SelectedTask;
            var comment = CommentText?.Trim();

            if (task == null || string.IsNullOrEmpty(comment))
                return;

            try
            {
                await _service.AddCommentAsync(task.Id, comment).ConfigureAwait(false);
                PluginUiThread.Run(() =>
                {
                    CommentText = string.Empty;
                    StatusMessage = $"Comment added to {task.Name}";
                });
                _context.UI.ShowToast($"Comment added to {task.Name}", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"ClickUp add comment failed: {ex.Message}", ex);
                PluginUiThread.Run(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            }
        }

        private async Task CompleteTaskAsync()
        {
            var task = SelectedTask;
            if (task == null)
                return;

            var completed = !task.Completed;

            try
            {
                await _service.SetCompletedAsync(task.Id, completed).ConfigureAwait(false);
                PluginUiThread.Run(() =>
                {
                    StatusMessage = completed
                        ? $"Completed {task.Name}"
                        : $"Reopened {task.Name}";
                });
                _context.UI.ShowToast(
                    completed ? $"Completed {task.Name}" : $"Reopened {task.Name}",
                    ToastType.Success);

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"ClickUp complete task failed: {ex.Message}", ex);
                PluginUiThread.Run(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            }
        }

        private async Task CreateTaskAsync()
        {
            var task = SelectedTask;
            if (task == null)
                return;

            if (_syncCoordinator == null)
            {
                _context.UI.ShowToast("ClickUp sync is not available.", ToastType.Warning);
                return;
            }

            if (IsLinked(task.Id))
            {
                _context.UI.ShowToast($"\"{task.Name}\" is already linked to an AIA item.", ToastType.Info);
                return;
            }

            try
            {
                await _syncCoordinator
                    .LinkTaskAsync(CreateSourceState(task), task.Name, BuildAiaDescription(task), CancellationToken.None)
                    .ConfigureAwait(false);

                PluginUiThread.Run(() => { StatusMessage = $"Added \"{task.Name}\" to AIA tasks"; });
                _context.UI.ShowToast($"Added \"{task.Name}\" to AIA tasks", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"ClickUp create AIA task failed: {ex.Message}", ex);
                PluginUiThread.Run(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            }
        }

        private async Task CreateReminderAsync()
        {
            var task = SelectedTask;
            if (task == null)
                return;

            if (_syncCoordinator == null)
            {
                _context.UI.ShowToast("ClickUp sync is not available.", ToastType.Warning);
                return;
            }

            if (IsLinked(task.Id))
            {
                _context.UI.ShowToast($"\"{task.Name}\" is already linked to an AIA item.", ToastType.Info);
                return;
            }

            try
            {
                await _syncCoordinator
                    .LinkReminderAsync(CreateSourceState(task), task.Name, CancellationToken.None)
                    .ConfigureAwait(false);

                PluginUiThread.Run(() => { StatusMessage = $"Added \"{task.Name}\" to AIA reminders"; });
                _context.UI.ShowToast($"Added \"{task.Name}\" to AIA reminders", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"ClickUp create AIA reminder failed: {ex.Message}", ex);
                PluginUiThread.Run(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            }
        }

        private void OpenSelectedTask()
        {
            var task = SelectedTask;
            if (task == null)
                return;

            if (TryOpenUrl(task.Url))
                return;

            _context.UI.ShowToast("Unable to open the task in ClickUp.", ToastType.Warning);
        }

        private void OpenClickUp()
        {
            if (!OpenClickUpApp())
            {
                _context.UI.ShowToast("Unable to open ClickUp.", ToastType.Warning);
            }
        }

        private async Task EnsureSyncAsync()
        {
            if (_syncCoordinator != null)
                return;

            try
            {
                _syncStore = new SyncRecordStore(_context.Settings, SyncRecordsKey);
                await _syncStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);

                _syncAdapter = new ClickUpSyncAdapter(_service);
                _syncCoordinator = new SyncCoordinator(_context, _syncAdapter, _syncStore);
                _syncCoordinator.Start();
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"ClickUp: sync initialization failed: {ex.Message}");
            }
        }

        private async Task ReconcileAsync(IReadOnlyList<ClickUpTaskData> tasks)
        {
            var coordinator = _syncCoordinator;
            if (coordinator == null)
                return;

            var states = tasks
                .Select(t => new ExternalSyncState
                {
                    SourceId = t.Id,
                    SourceUrl = t.Url,
                    Title = t.Name,
                    IsCompleted = t.IsCompleted,
                    DueUtc = t.DueDate?.ToUniversalTime()
                })
                .ToList();

            await AddLinkedStatesAsync(states, CancellationToken.None).ConfigureAwait(false);

            try
            {
                await coordinator.ReconcileAsync(states, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"ClickUp: sync reconcile failed: {ex.Message}");
            }
        }

        private async Task AddLinkedStatesAsync(List<ExternalSyncState> states, CancellationToken cancellationToken)
        {
            var adapter = _syncAdapter;
            var store = _syncStore;
            if (adapter == null || store == null)
                return;

            var known = new HashSet<string>(states.Select(s => s.SourceId), StringComparer.Ordinal);
            var linked = store.Records
                .Where(r => string.Equals(r.SourceKey, adapter.SourceKey, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.SourceId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var sourceId in linked)
            {
                if (string.IsNullOrEmpty(sourceId) || known.Contains(sourceId))
                    continue;

                try
                {
                    var state = await adapter.GetStateAsync(sourceId, cancellationToken).ConfigureAwait(false);
                    if (state != null)
                        states.Add(state);
                }
                catch (Exception ex)
                {
                    _context.Logger.Warning($"ClickUp: unable to read linked task state: {ex.Message}");
                    states.Add(new ExternalSyncState { SourceId = sourceId, Error = ex.Message });
                }
            }
        }

        private bool IsLinked(string sourceId)
        {
            return _syncStore?.FindBySource("clickup", sourceId).Any() == true;
        }

        private static ExternalSyncState CreateSourceState(ClickUpTaskViewModel task)
        {
            return new ExternalSyncState
            {
                SourceId = task.Id,
                SourceUrl = task.Url,
                Title = task.Name,
                IsCompleted = task.Completed,
                DueUtc = task.DueDate?.ToUniversalTime()
            };
        }

        private static string BuildAiaDescription(ClickUpTaskViewModel task)
        {
            var description = task.Description?.Trim() ?? string.Empty;
            var url = task.Url ?? string.Empty;

            if (string.IsNullOrEmpty(url))
                return description;

            return string.IsNullOrEmpty(description)
                ? url
                : description + Environment.NewLine + Environment.NewLine + url;
        }

        private async Task<bool> TryPersistTokenAsync(string token)
        {
            string protectedToken;

            try
            {
                protectedToken = ClickUpTokenProtector.Protect(token);
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"ClickUp: unable to encrypt the token at rest: {ex.GetType().Name}");
                _context.UI.ShowToast("Unable to store the ClickUp token securely.", ToastType.Error);
                return false;
            }

            _context.Settings.Set("Token", protectedToken);
            await _context.Settings.SaveAsync().ConfigureAwait(false);
            return true;
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
            titlePanel.AppendChild(PluginUiFactory.CreateText(text: "ClickUp", fontSize: 16, fontWeight: FontWeights.SemiBold));
            titlePanel.AppendChild(PluginUiFactory.CreateText(bindingPath: "StatusMessage", fontSize: 11, foreground: MutedBrush));
            grid.AppendChild(titlePanel);

            var refresh = PluginUiFactory.CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 2);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(refresh);

            var open = PluginUiFactory.CreateButton("Open ClickUp", "OpenClickUpCommand", NeutralBrush);
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

            var label = PluginUiFactory.CreateText(text: "Personal token", fontSize: 12, foreground: BodyBrush);
            label.SetValue(Grid.ColumnProperty, 0);
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            panel.AppendChild(label);

            var tokenBox = PluginUiFactory.CreateInput(
                "TokenInput",
                "ClickUp personal API token from https://app.clickup.com/settings/apps. Stored encrypted for the current Windows user.");
            tokenBox.SetValue(Grid.ColumnProperty, 1);
            panel.AppendChild(tokenBox);

            var save = PluginUiFactory.CreateButton("Save & Connect", "SaveConfigCommand", AccentBrush);
            save.SetValue(Grid.ColumnProperty, 2);
            save.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            panel.AppendChild(save);

            return panel;
        }

        private FrameworkElementFactory CreateContentPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(360)));
            grid.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));

            var tasksPanel = new FrameworkElementFactory(typeof(Grid));
            tasksPanel.SetValue(Grid.ColumnProperty, 0);
            tasksPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            tasksPanel.AppendChild(PluginUiFactory.CreateRow(GridLength.Auto));
            tasksPanel.AppendChild(PluginUiFactory.CreateRow(new GridLength(1, GridUnitType.Star)));

            var tasksHeader = PluginUiFactory.CreateSectionHeader("MY TASKS");
            tasksHeader.SetValue(Grid.RowProperty, 0);
            tasksPanel.AppendChild(tasksHeader);

            var list = PluginUiFactory.CreateListBox(
                "Tasks",
                "SelectedTask",
                PluginUiFactory.CreateListItemTemplate("Name", "SubtitleLine", "MetaLine"));
            list.SetValue(Grid.RowProperty, 1);
            tasksPanel.AppendChild(list);

            grid.AppendChild(tasksPanel);

            var details = CreateDetailsPanel();
            details.SetValue(Grid.ColumnProperty, 1);
            grid.AppendChild(details);

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
            headerInfo.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedTask.Name", fontSize: 15, fontWeight: FontWeights.SemiBold, wrap: true));
            headerInfo.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedTask.SubtitleLine", fontSize: 11, foreground: MutedBrush, trim: true));
            headerInfo.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedTask.MetaLine", fontSize: 11, foreground: FaintBrush, trim: true));
            header.AppendChild(headerInfo);

            var open = PluginUiFactory.CreateButton("Open in ClickUp", "OpenTaskCommand", NeutralBrush);
            open.SetValue(Grid.ColumnProperty, 1);
            open.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            header.AppendChild(open);

            grid.AppendChild(header);

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(Grid.RowProperty, 1);
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);

            var details = new FrameworkElementFactory(typeof(StackPanel));
            details.AppendChild(PluginUiFactory.CreateSectionHeader("DESCRIPTION"));
            details.AppendChild(PluginUiFactory.CreateText(
                bindingPath: "SelectedTask.Description",
                fontSize: 12,
                foreground: BodyBrush,
                wrap: true));
            scroll.AppendChild(details);
            grid.AppendChild(scroll);

            var commentRow = new FrameworkElementFactory(typeof(Grid));
            commentRow.SetValue(Grid.RowProperty, 2);
            commentRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));
            commentRow.AppendChild(PluginUiFactory.CreateColumn(new GridLength(1, GridUnitType.Star)));
            commentRow.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var commentBox = PluginUiFactory.CreateInput("CommentText", "Add a comment to the selected ClickUp task");
            commentBox.SetValue(Grid.ColumnProperty, 0);
            commentRow.AppendChild(commentBox);

            var comment = PluginUiFactory.CreateButton("Add comment", "AddCommentCommand", AccentBrush);
            comment.SetValue(Grid.ColumnProperty, 1);
            comment.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            commentRow.AppendChild(comment);

            grid.AppendChild(commentRow);

            var actions = new FrameworkElementFactory(typeof(StackPanel));
            actions.SetValue(Grid.RowProperty, 3);
            actions.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            actions.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            actions.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));

            var complete = PluginUiFactory.CreateButton("Complete / reopen", "CompleteTaskCommand", NeutralBrush);
            complete.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            actions.AppendChild(complete);

            var createTask = PluginUiFactory.CreateButton("Add to AIA tasks", "CreateTaskCommand", NeutralBrush);
            createTask.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            actions.AppendChild(createTask);

            actions.AppendChild(PluginUiFactory.CreateButton("Add reminder", "CreateReminderCommand", NeutralBrush));
            grid.AppendChild(actions);

            return grid;
        }

        #endregion
    }

    /// <summary>
    /// View model for a ClickUp task.
    /// </summary>
    public class ClickUpTaskViewModel
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Url { get; }
        public string Status { get; }
        public string StatusType { get; }
        public string ListId { get; }
        public string ListName { get; }
        public string FolderName { get; }
        public string SpaceId { get; }
        public string SpaceName { get; }
        public DateTime? DueDate { get; }
        public bool Completed { get; }

        public string TitleLine => string.IsNullOrWhiteSpace(Name) ? "(untitled task)" : Name;

        public string SubtitleLine => BuildSubtitleLine();

        public string MetaLine => BuildMetaLine();

        public ClickUpTaskViewModel(ClickUpTaskData data)
        {
            Id = data.Id;
            Name = data.Name;
            Description = data.Description;
            Url = data.Url;
            Status = data.Status;
            StatusType = data.StatusType;
            ListId = data.ListId;
            ListName = data.ListName;
            FolderName = data.FolderName;
            SpaceId = data.SpaceId;
            SpaceName = data.SpaceName;
            DueDate = data.DueDate;
            Completed = data.IsCompleted;
        }

        private string BuildSubtitleLine()
        {
            if (!string.IsNullOrWhiteSpace(SpaceName) && !string.IsNullOrWhiteSpace(ListName))
                return $"{SpaceName} · {ListName}";
            if (!string.IsNullOrWhiteSpace(ListName))
                return ListName;
            if (!string.IsNullOrWhiteSpace(SpaceName))
                return SpaceName;
            return FolderName ?? string.Empty;
        }

        private string BuildMetaLine()
        {
            var status = string.IsNullOrWhiteSpace(Status) ? "No status" : Status;

            if (!DueDate.HasValue)
                return status;

            return $"{status} · due {DueDate.Value.ToLocalTime():MMM dd}";
        }
    }

    /// <summary>
    /// Protects the ClickUp personal token at rest with Windows DPAPI (current user scope).
    /// Protected values are prefixed with "enc:v1:" so legacy plaintext tokens can be migrated.
    /// </summary>
    internal static class ClickUpTokenProtector
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

        public static string Unprotect(string? storedValue, IPluginLogger? logger = null)
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
            catch (Exception ex)
            {
                // A token protected by a different Windows user or machine cannot be recovered.
                logger?.Warning($"ClickUp: the stored token cannot be decrypted: {ex.GetType().Name}");
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Settings view model for the ClickUp plugin.
    /// </summary>
    public class ClickUpSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private string _token = string.Empty;
        private int _refreshIntervalMinutes = 5;
        private int _maxTasks = 50;

        public override string SettingsTitle => "ClickUp Settings";
        public override string SettingsDescription => "Configure the ClickUp personal token and refresh behaviour";

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

        public int MaxTasks
        {
            get => _maxTasks;
            set => SetProperty(ref _maxTasks, value);
        }

        public ClickUpSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage;
            Load();
        }

        public override void Load()
        {
            Token = ClickUpTokenProtector.Unprotect(_storage.Get("Token", string.Empty));
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 5);
            MaxTasks = _storage.Get("MaxTasks", 50);
            IsDirty = false;
        }

        public override void Save()
        {
            try
            {
                _storage.Set("Token", ClickUpTokenProtector.Protect(Token));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClickUp settings: failed to protect the token: {ex.Message}");
            }

            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("MaxTasks", MaxTasks);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            Token = string.Empty;
            RefreshIntervalMinutes = 5;
            MaxTasks = 50;
        }
    }
}
