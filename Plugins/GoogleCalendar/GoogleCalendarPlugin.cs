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

namespace AIA.Plugins.GoogleCalendar
{
    [Plugin("AIA.GoogleCalendar", "Google Calendar Integration", IconSymbol = "CalendarLtr20", IsBuiltIn = true)]
    public class GoogleCalendarPlugin : PluginBase
    {
        private GoogleCalendarTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.GoogleCalendar";
        public override string Name => "Google Calendar Integration";
        public override string Description => "Shows upcoming Google Calendar events and saves them as AIA tasks or reminders";
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
            _tabViewModel = new GoogleCalendarTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "google-calendar",
                Title = "Calendar",
                IconSymbol = "CalendarLtr20",
                Order = 102,
                ViewModel = _tabViewModel,
                BadgeColor = "#FF4285F4"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-google-calendar",
                Text = "Calendar",
                IconSymbol = "CalendarLtr20",
                Tooltip = "Open Google Calendar",
                Order = 102,
                OnClick = () => GoogleCalendarTabViewModel.OpenUrl("https://calendar.google.com")
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
            Context.UI.UnregisterTab("google-calendar");
            Context.UI.UnregisterToolbarButton("open-google-calendar");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new GoogleCalendarSettingsViewModel(Context.Settings);
        }
    }

    public class GoogleCalendarTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private const string OAuthServiceName = "AIA.GoogleOAuth";

        private readonly IPluginContext _context;
        private readonly GoogleCalendarService _service;
        private readonly GoogleCalendarSyncAdapter _adapter;
        private readonly SyncRecordStore _syncStore;
        private readonly SyncCoordinator _sync;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _openEventCommand;
        private readonly RelayCommand _createTaskCommand;
        private readonly RelayCommand _createReminderCommand;
        private IOAuthTokenProvider? _provider;
        private DataTemplate? _cachedTemplate;
        private bool _initialized;
        private bool _isLoading;
        private bool _isSignedIn;
        private string _statusMessage = string.Empty;
        private CalendarEventViewModel? _selectedEvent;
        private int _maxEvents = 25;

        public ObservableCollection<CalendarEventViewModel> Events { get; } = new();

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

        public CalendarEventViewModel? SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                if (SetProperty(ref _selectedEvent, value))
                {
                    _openEventCommand.RaiseCanExecuteChanged();
                    _createTaskCommand.RaiseCanExecuteChanged();
                    _createReminderCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand RefreshCommand => _refreshCommand;
        public ICommand OpenEventCommand => _openEventCommand;
        public ICommand CreateTaskCommand => _createTaskCommand;
        public ICommand CreateReminderCommand => _createReminderCommand;
        public ICommand OpenCalendarCommand { get; }

        public GoogleCalendarTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new GoogleCalendarService(context);
            _adapter = new GoogleCalendarSyncAdapter(_service);
            _syncStore = new SyncRecordStore(context.Settings, "sync.records.googlecalendar");
            _sync = new SyncCoordinator(context, _adapter, _syncStore);

            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading);
            _openEventCommand = new RelayCommand(OpenSelectedEvent, () => SelectedEvent != null);
            _createTaskCommand = new RelayCommand(CreateTaskAsync, () => SelectedEvent != null);
            _createReminderCommand = new RelayCommand(CreateReminderAsync, () => SelectedEvent != null);
            OpenCalendarCommand = new RelayCommand(() => OpenUrl("https://calendar.google.com"));
        }

        public async Task InitializeAsync()
        {
            _maxEvents = Math.Clamp(_context.Settings.Get("MaxEvents", 25), 1, 100);
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
                StatusMessage = "Sign in on the Google tab and enable Calendar.";
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
                PluginUiThread.Run(() => { StatusMessage = "Sign in on the Google tab and enable Calendar."; });
                return;
            }

            if (IsLoading)
                return;

            PluginUiThread.Run(() =>
            {
                IsLoading = true;
                StatusMessage = "Loading upcoming events...";
            });

            try
            {
                var events = await _service.GetUpcomingEventsAsync(_maxEvents);

                PluginUiThread.Run(() =>
                {
                    Events.Clear();
                    foreach (var item in events)
                    {
                        Events.Add(new CalendarEventViewModel(item));
                    }

                    IsSignedIn = true;
                    StatusMessage = $"{Events.Count} upcoming event(s)";
                });

                var states = events.Select(item => new ExternalSyncState
                {
                    SourceId = item.Id,
                    SourceUrl = item.HtmlLink,
                    Title = item.Summary,
                    IsCompleted = null,
                    DueUtc = item.Start?.ToUniversalTime()
                }).ToList();

                await _sync.ReconcileAsync(states, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Google Calendar refresh failed: {ex.Message}", ex);
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
                Events.Clear();
                SelectedEvent = null;
                IsSignedIn = false;
                StatusMessage = "Signed out of Google. Sign in on the Google tab.";
            });
        }

        private async Task CreateTaskAsync()
        {
            var item = SelectedEvent;
            if (item == null)
                return;

            try
            {
                await _sync.LinkTaskAsync(BuildState(item), item.Summary, BuildDescription(item), CancellationToken.None);
                _context.UI.ShowToast("AIA task created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Calendar task creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private async Task CreateReminderAsync()
        {
            var item = SelectedEvent;
            if (item == null)
                return;

            try
            {
                await _sync.LinkReminderAsync(BuildState(item), item.Summary, CancellationToken.None);
                _context.UI.ShowToast("AIA reminder created", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Calendar reminder creation failed: {ex.Message}", ex);
                ReportError(ex);
            }
        }

        private void OpenSelectedEvent()
        {
            if (SelectedEvent != null && OpenUrl(SelectedEvent.HtmlLink))
                return;

            _context.UI.ShowToast("Unable to open Google Calendar.", ToastType.Warning);
        }

        private static ExternalSyncState BuildState(CalendarEventViewModel item)
        {
            return new ExternalSyncState
            {
                SourceId = item.Id,
                SourceUrl = item.HtmlLink,
                Title = item.Summary,
                IsCompleted = null,
                DueUtc = item.Start?.ToUniversalTime()
            };
        }

        private static string BuildDescription(CalendarEventViewModel item)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.SubtitleLine))
                parts.Add(item.SubtitleLine);
            if (!string.IsNullOrWhiteSpace(item.Location))
                parts.Add(item.Location);
            if (!string.IsNullOrWhiteSpace(item.Description))
                parts.Add(item.Description);

            parts.Add(item.HtmlLink);
            return string.Join(Environment.NewLine, parts);
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
            if (_initialized && Events.Count == 0 && _provider?.IsSignedIn == true)
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
            info.AppendChild(PluginUiFactory.CreateText(text: "Google Calendar", fontSize: 16, fontWeight: FontWeights.SemiBold));
            info.AppendChild(PluginUiFactory.CreateText(bindingPath: "StatusMessage", fontSize: 11,
                foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));
            grid.AppendChild(info);

            var refresh = PluginUiFactory.CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 2);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(refresh);

            var open = PluginUiFactory.CreateButton("Open Calendar", "OpenCalendarCommand", NeutralBrush);
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

            var listHeader = PluginUiFactory.CreateSectionHeader("UPCOMING EVENTS");
            listHeader.SetValue(Grid.RowProperty, 0);
            listPanel.AppendChild(listHeader);

            var list = PluginUiFactory.CreateListBox("Events", "SelectedEvent",
                PluginUiFactory.CreateListItemTemplate("TitleLine", "SubtitleLine", "MetaLine"));
            list.SetValue(Grid.RowProperty, 1);
            listPanel.AppendChild(list);
            grid.AppendChild(listPanel);

            var details = new FrameworkElementFactory(typeof(StackPanel));
            details.SetValue(Grid.ColumnProperty, 1);

            details.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedEvent.Summary",
                fontSize: 15, fontWeight: FontWeights.SemiBold, wrap: true));
            details.AppendChild(PluginUiFactory.CreateText(bindingPath: "SelectedEvent.TimeLine",
                fontSize: 11, foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));

            var meta = PluginUiFactory.CreateText(bindingPath: "SelectedEvent.MetaLine",
                fontSize: 11, foreground: PluginUiFactory.BrushFrom(0x99, 0xFF, 0xFF, 0xFF));
            meta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 8));
            details.AppendChild(meta);

            var description = PluginUiFactory.CreateText(bindingPath: "SelectedEvent.Description",
                fontSize: 12, foreground: PluginUiFactory.BrushFrom(0xCC, 0xFF, 0xFF, 0xFF), wrap: true);
            description.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
            details.AppendChild(description);

            var buttons = new FrameworkElementFactory(typeof(StackPanel));
            buttons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var open = PluginUiFactory.CreateButton("Open", "OpenEventCommand", AccentBrush);
            open.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(open);

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

        private static readonly Brush AccentBrush = PluginUiFactory.BrushFrom(0xFF, 0x42, 0x85, 0xF4);
        private static readonly Brush NeutralBrush = PluginUiFactory.BrushFrom(0x33, 0xFF, 0xFF, 0xFF);
    }

    public class CalendarEventViewModel
    {
        public string Id { get; }
        public string Summary { get; }
        public string Description { get; }
        public string Location { get; }
        public DateTime? Start { get; }
        public DateTime? End { get; }
        public string HtmlLink { get; }
        public bool IsAllDay { get; }
        public string Organizer { get; }

        public string TitleLine => string.IsNullOrWhiteSpace(Summary) ? "(no title)" : Summary;

        public string TimeLine
        {
            get
            {
                if (IsAllDay)
                    return Start.HasValue ? $"{Start.Value:MMM dd} · All day" : "All day";
                if (Start.HasValue && End.HasValue)
                    return $"{Start.Value:MMM dd HH:mm} - {End.Value:HH:mm}";
                return Start.HasValue ? Start.Value.ToString("MMM dd HH:mm") : "No time";
            }
        }

        public string SubtitleLine => TimeLine;

        public string MetaLine
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Location))
                    parts.Add(Location);
                if (!string.IsNullOrWhiteSpace(Organizer))
                    parts.Add(Organizer);
                return string.Join(" · ", parts);
            }
        }

        public CalendarEventViewModel(GoogleCalendarEventData data)
        {
            Id = data.Id;
            Summary = data.Summary;
            Description = data.Description;
            Location = data.Location;
            Start = data.Start;
            End = data.End;
            HtmlLink = data.HtmlLink;
            IsAllDay = data.IsAllDay;
            Organizer = data.Organizer;
        }
    }

    public class GoogleCalendarSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private int _refreshIntervalMinutes = 5;
        private int _maxEvents = 25;

        public override string SettingsTitle => "Google Calendar Settings";
        public override string SettingsDescription => "Configure Google Calendar refresh behavior";

        public int RefreshIntervalMinutes
        {
            get => _refreshIntervalMinutes;
            set => SetProperty(ref _refreshIntervalMinutes, value);
        }

        public int MaxEvents
        {
            get => _maxEvents;
            set => SetProperty(ref _maxEvents, value);
        }

        public GoogleCalendarSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage;
            Load();
        }

        public override void Load()
        {
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 5);
            MaxEvents = _storage.Get("MaxEvents", 25);
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("MaxEvents", MaxEvents);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            RefreshIntervalMinutes = 5;
            MaxEvents = 25;
        }
    }
}
