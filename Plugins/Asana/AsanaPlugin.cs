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

namespace AIA.Plugins.Asana
{
    [Plugin("AIA.Asana", "Asana Integration", IconSymbol = "TaskListSquareLtr20", IsBuiltIn = true)]
    public class AsanaPlugin : PluginBase
    {
        private AsanaTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.Asana";
        public override string Name => "Asana Integration";
        public override string Description => "Shows Asana tasks assigned to you with due dates and comments";
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
            _tabViewModel = new AsanaTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "asana",
                Title = "Asana",
                IconSymbol = "TaskListSquareLtr20",
                Order = 93,
                ViewModel = _tabViewModel,
                BadgeColor = "#FFF06A6A"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-asana",
                Text = "Asana",
                IconSymbol = "TaskListSquareLtr20",
                Tooltip = "Open Asana",
                Order = 93,
                OnClick = () => AsanaTabViewModel.OpenUrl("https://app.asana.com")
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
            Context.UI.UnregisterTab("asana");
            Context.UI.UnregisterToolbarButton("open-asana");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new AsanaSettingsViewModel(Context.Settings);
        }
    }

    public class AsanaTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private readonly IPluginContext _context;
        private readonly AsanaService _service;
        private readonly AsanaSyncAdapter _adapter;
        private readonly SyncRecordStore _syncStore;
        private readonly SyncCoordinator _sync;
        private readonly RelayCommand _saveConfigCommand;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _openTaskCommand;
        private readonly RelayCommand _addCommentCommand;
        private readonly RelayCommand _completeCommand;
        private readonly RelayCommand _createTaskCommand;
        private readonly RelayCommand _createReminderCommand;
        private DataTemplate? _cachedTemplate;
        private bool _initialized;
        private bool _isLoading;
        private bool _isConfigured;
        private string _patInput = string.Empty;
        private string _statusMessage = string.Empty;
        private string _commentText = string.Empty;
        private AsanaTaskViewModel? _selectedTask;
        private int _maxTasks = 50;

        public ObservableCollection<AsanaTaskViewModel> Tasks { get; } = new();

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

        public string PatInput
        {
            get => _patInput;
            set => SetProperty(ref _patInput, value);
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

        public AsanaTaskViewModel? SelectedTask
        {
            get => _selectedTask;
            set
            {
                if (SetProperty(ref _selectedTask, value))
                {
                    _openTaskCommand.RaiseCanExecuteChanged();
                    _completeCommand.RaiseCanExecuteChanged();
                    _createTaskCommand.RaiseCanExecuteChanged();
                    _createReminderCommand.RaiseCanExecuteChanged();
                    _addCommentCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand SaveConfigCommand => _saveConfigCommand;
        public ICommand RefreshCommand => _refreshCommand;
        public ICommand OpenTaskCommand => _openTaskCommand;
        public ICommand AddCommentCommand => _addCommentCommand;
        public ICommand CompleteTaskCommand => _completeCommand;
        public ICommand CreateTaskCommand => _createTaskCommand;
        public ICommand CreateReminderCommand => _createReminderCommand;
        public ICommand OpenAsanaCommand { get; }

        public AsanaTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new AsanaService(context);
            _adapter = new AsanaSyncAdapter(_service);
            _syncStore = new SyncRecordStore(context.Settings, "sync.records.asana");
            _sync = new SyncCoordinator(context, _adapter, _syncStore);

            _saveConfigCommand = new RelayCommand(SaveConfigAsync, () => !IsLoading);
            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading && _service.IsConfigured);
            _openTaskCommand = new RelayCommand(OpenSelectedTask, () => SelectedTask != null);
            _addCommentCommand = new RelayCommand(AddCommentAsync,
                () => SelectedTask != null && !string.IsNullOrWhiteSpace(CommentText));
            _completeCommand = new RelayCommand(CompleteAsync, () => SelectedTask != null);
            _createTaskCommand = new RelayCommand(CreateTaskAsync, () => SelectedTask != null);
            _createReminderCommand = new RelayCommand(CreateReminderAsync, () => SelectedTask != null);
            OpenAsanaCommand = new RelayCommand(() => OpenUrl("https://app.asana.com"));
        }

        public async Task InitializeAsync()
        {
            PatInput = _context.Settings.Get("Pat", string.Empty) ?? string.Empty;
            _maxTasks = Math.Clamp(_context.Settings.Get("MaxTasks", 50), 5, 200);
            _service.Configure(PatInput);
            IsConfigured = _service.IsConfigured;

            await _syncStore.LoadAsync(CancellationToken.None);
            _sync.Start();
            _initialized = true;

            if (!IsConfigured)
            {
                StatusMessage = "Add an Asana personal access token to get started.";
            }
        }

        public async Task RefreshAsync()
        {
            if (!_service.IsConfigured)
            {
                PluginUiThread.Run(() => { StatusMessage = "Add an Asana personal access token to get started."; });
                return;
            }

            if (IsLoading)
                return;

            PluginUiThread.Run(() =>
            {
                IsLoading = true;
                StatusMessage = "Loading Asana tasks...";
            });

            try
            {
                var user = await _service.TestConnectionAsync();
                var tasks = await _service.GetMyTasksAsync(_maxTasks);

                PluginUiThread.Run(() =>
                {
                    Tasks.Clear();
                    foreach (var task in tasks)
                    {
                        Tasks.Add(new AsanaTaskViewModel(task));
                    }

                    IsConfigured = true;
                    StatusMessage = $"Connected as {user.Name} · {Tasks.Count} task(s)";
                });

                var states = tasks.Select(task => new ExternalSyncState
                {
                    SourceId = task.Gid,
                    SourceUrl = task.Url,
                    Title = task.Name,
                    IsCompleted = task.Completed,
                    DueUtc = task.Due?.ToUniversalTime()
                }).ToList();

                await AddLinkedStatesAsync(states, CancellationToken.None);
                await _sync.ReconcileAsync(states, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Asana refresh failed: {ex.Message}", ex);
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
                    _context.Logger.Warning($"Asana: unable to read linked task state: {ex.Message}");
                    states.Add(new ExternalSyncState { SourceId = sourceId, Error = ex.Message });
                }
            }
        }

        private async Task SaveConfigAsync()
        {
            _service.Configure(PatInput);
            _context.Settings.Set("Pat", PatInput?.Trim() ?? string.Empty);
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

        private async Task AddCommentAsync()
        {
            var task = SelectedTask;
            var text = CommentText?.Trim();
            if (task == null || string.IsNullOrEmpty(text))
                return;

            try
            {
                await _service.AddCommentAsync(task.Gid, text);
                PluginUiThread.Run(() =>
                {
                    CommentText = string.Empty;
                    StatusMessage = $"Comment added to {task.Name}";
                });
                _context.UI.ShowToast("Comment added", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Asana comment failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CompleteAsync()
        {
            var task = SelectedTask;
            if (task == null)
                return;

            try
            {
                await _service.SetCompletedAsync(task.Gid, !task.Completed);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Asana complete failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CreateTaskAsync()
        {
            var task = SelectedTask;
            if (task == null)
                return;

            try
            {
                await _sync.LinkTaskAsync(BuildState(task), task.Name, BuildDescription(task), CancellationToken.None);
                _context.UI.ShowToast("AIA task created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Asana task creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CreateReminderAsync()
        {
            var task = SelectedTask;
            if (task == null)
                return;

            try
            {
                await _sync.LinkReminderAsync(BuildState(task), task.Name, CancellationToken.None);
                _context.UI.ShowToast("AIA reminder created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Asana reminder creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private void OpenSelectedTask()
        {
            if (SelectedTask != null && OpenUrl(SelectedTask.Url))
                return;

            _context.UI.ShowToast("Unable to open Asana.", ToastType.Warning);
        }

        private static ExternalSyncState BuildState(AsanaTaskViewModel task)
        {
            return new ExternalSyncState
            {
                SourceId = task.Gid,
                SourceUrl = task.Url,
                Title = task.Name,
                IsCompleted = task.Completed,
                DueUtc = task.Due?.ToUniversalTime()
            };
        }

        private static string BuildDescription(AsanaTaskViewModel task)
        {
            var notes = task.Notes?.Trim() ?? string.Empty;
            if (notes.Length > 500)
                notes = notes.Substring(0, 500) + "...";

            return string.IsNullOrEmpty(notes)
                ? task.Url
                : notes + Environment.NewLine + Environment.NewLine + task.Url;
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
            if (_initialized && Tasks.Count == 0 && _service.IsConfigured)
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
            info.AppendChild(PluginUiFactory.CreateText(text: "Asana", fontSize: 16, fontWeight: FontWeights.SemiBold));
            info.AppendChild(PluginUiFactory.CreateText(bindingPath: "StatusMessage", fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));
            grid.AppendChild(info);

            var refresh = PluginUiFactory.CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 2);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(refresh);

            var open = PluginUiFactory.CreateButton("Open Asana", "OpenAsanaCommand", NeutralBrush);
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
            grid.AppendChild(PluginUiFactory.CreateColumn(GridLength.Auto));

            var label = PluginUiFactory.CreateText(text: "Personal token", fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF));
            label.SetValue(Grid.ColumnProperty, 0);
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            grid.AppendChild(label);

            var input = PluginUiFactory.CreateInput("PatInput",
                "Asana personal access token from https://app.asana.com/0/my-apps");
            input.SetValue(Grid.ColumnProperty, 1);
            input.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            grid.AppendChild(input);

            var save = PluginUiFactory.CreateButton("Save & Connect", "SaveConfigCommand", AccentBrush);
            save.SetValue(Grid.ColumnProperty, 2);
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

            var listHeader = PluginUiFactory.CreateSectionHeader("MY TASKS");
            listHeader.SetValue(Grid.RowProperty, 0);
            listPanel.AppendChild(listHeader);

            var list = PluginUiFactory.CreateListBox("Tasks", "SelectedTask",
                PluginUiFactory.CreateListItemTemplate("TitleLine", "SubtitleLine", "MetaLine"));
            list.SetValue(Grid.RowProperty, 1);
            listPanel.AppendChild(list);
            grid.AppendChild(listPanel);

            var details = new FrameworkElementFactory(typeof(StackPanel));
            details.SetValue(Grid.ColumnProperty, 1);

            details.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedTask.Name",
                fontSize: 15, fontWeight: FontWeights.SemiBold, wrap: true));
            details.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedTask.SubtitleLine",
                fontSize: 11, foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));

            var meta = PluginUiFactory.CreateText(bindingPath: "SelectedTask.MetaLine",
                fontSize: 11, foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF));
            meta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 8));
            details.AppendChild(meta);

            var notes = PluginUiFactory.CreateText(bindingPath: "SelectedTask.Notes",
                fontSize: 12, foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF), wrap: true);
            notes.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
            details.AppendChild(notes);

            var buttons = new FrameworkElementFactory(typeof(StackPanel));
            buttons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            buttons.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            var open = PluginUiFactory.CreateButton("Open", "OpenTaskCommand", AccentBrush);
            open.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(open);

            var complete = PluginUiFactory.CreateButton("Toggle complete", "CompleteTaskCommand", NeutralBrush);
            complete.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(complete);

            var task = PluginUiFactory.CreateButton("Create AIA task", "CreateTaskCommand", NeutralBrush);
            task.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(task);

            buttons.AppendChild(PluginUiFactory.CreateButton("Create reminder", "CreateReminderCommand", NeutralBrush));
            details.AppendChild(buttons);

            details.AppendChild(PluginUiFactory.CreateInput("CommentText", "Comment to add to the selected task"));

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

        private static readonly Brush AccentBrush = PluginUiFactory.BrushFrom(0xFF, 0xF0, 0x6A, 0x6A);
        private static readonly Brush NeutralBrush = PluginUiFactory.BrushFrom(0x33, 0xFF, 0xFF, 0xFF);
    }

    public class AsanaTaskViewModel
    {
        public string Gid { get; }
        public string Name { get; }
        public string Notes { get; }
        public bool Completed { get; }
        public DateTime? Due { get; }
        public string Url { get; }
        public string ProjectNames { get; }

        public string TitleLine => Name;
        public string SubtitleLine => string.IsNullOrWhiteSpace(ProjectNames) ? "Asana task" : ProjectNames;
        public string MetaLine
        {
            get
            {
                var parts = new List<string>();
                if (Due.HasValue) parts.Add($"Due {Due.Value:MMM dd HH:mm}");
                parts.Add(Completed ? "Complete" : "Open");
                return string.Join(" · ", parts);
            }
        }

        public AsanaTaskViewModel(AsanaTaskData data)
        {
            Gid = data.Gid;
            Name = data.Name;
            Notes = data.Notes;
            Completed = data.Completed;
            Due = data.Due;
            Url = data.Url;
            ProjectNames = string.Join(", ", data.ProjectNames);
        }
    }

    public class AsanaSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private string _pat = string.Empty;
        private int _refreshIntervalMinutes = 5;
        private int _maxTasks = 50;

        public override string SettingsTitle => "Asana Settings";
        public override string SettingsDescription => "Configure the Asana connection";

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

        public int MaxTasks
        {
            get => _maxTasks;
            set => SetProperty(ref _maxTasks, value);
        }

        public AsanaSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage;
            Load();
        }

        public override void Load()
        {
            Pat = _storage.Get("Pat", string.Empty) ?? string.Empty;
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 5);
            MaxTasks = _storage.Get("MaxTasks", 50);
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("Pat", Pat);
            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("MaxTasks", MaxTasks);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            Pat = string.Empty;
            RefreshIntervalMinutes = 5;
            MaxTasks = 50;
        }
    }
}
