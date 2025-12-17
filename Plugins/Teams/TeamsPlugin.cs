using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AIA.Plugins.SDK;

// Alias to resolve ambiguous reference
using WpfApplication = System.Windows.Application;

namespace AIA.Plugins.Teams
{
    [Plugin("AIA.Teams", "Teams Integration", IconSymbol = "PeopleTeam20", IsBuiltIn = true)]
    public class TeamsPlugin : PluginBase
    {
        private TeamsTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.Teams";
        public override string Name => "Teams Integration";
        public override string Description => "Integrates with Microsoft Teams to display meetings, messages, and reminders";
        public override Version Version => new Version(1, 0, 0);
        public override string Author => "AIA";

        public override PluginPermissions RequiredPermissions =>
            PluginPermissions.ComAutomation |
            PluginPermissions.Network |
            PluginPermissions.ReadReminders |
            PluginPermissions.WriteReminders;

        protected override async Task OnInitializeAsync()
        {
            LogInfo("Initializing Teams plugin...");

            _tabViewModel = new TeamsTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            // Register the tab
            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "teams",
                Title = "Teams",
                IconSymbol = "PeopleTeam20",
                Order = 50,
                ViewModel = _tabViewModel,
                BadgeColor = "#335B5FC7"
            });

            // Register toolbar button for quick Teams access
            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-teams",
                Text = "Teams",
                IconSymbol = "PeopleTeam20",
                Tooltip = "Open Microsoft Teams",
                Order = 50,
                OnClick = () => _tabViewModel?.OpenTeamsApp()
            });

            LogInfo("Teams plugin initialized");
        }

        protected override async Task OnStartAsync()
        {
            LogInfo("Starting Teams plugin...");

            // Start refresh timer
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _refreshTimer.Tick += async (s, e) =>
            {
                if (_tabViewModel != null)
                {
                    await _tabViewModel.RefreshAsync();
                }
            };
            _refreshTimer.Start();

            // Initial refresh
            if (_tabViewModel != null)
            {
                await _tabViewModel.RefreshAsync();
            }

            LogInfo("Teams plugin started");
        }

        protected override Task OnStopAsync()
        {
            LogInfo("Stopping Teams plugin...");
            _refreshTimer?.Stop();
            _refreshTimer = null;
            LogInfo("Teams plugin stopped");
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Context.UI.UnregisterTab("teams");
            Context.UI.UnregisterToolbarButton("open-teams");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new TeamsSettingsViewModel(Context.Settings);
        }
    }

    /// <summary>
    /// View model for the Teams tab
    /// </summary>
    public class TeamsTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private readonly IPluginContext _context;
        private readonly TeamsService _teamsService;
        private bool _isLoading;
        private bool _isAvailable;
        private string _statusMessage = string.Empty;
        private TeamsMeetingViewModel? _selectedMeeting;
        private TeamsMessageViewModel? _selectedMessage;

        public ObservableCollection<TeamsMeetingViewModel> TodaysMeetings { get; } = new();
        public ObservableCollection<TeamsMessageViewModel> UnreadMessages { get; } = new();
        public ObservableCollection<TeamsReminderViewModel> Reminders { get; } = new();

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsAvailable
        {
            get => _isAvailable;
            set => SetProperty(ref _isAvailable, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public TeamsMeetingViewModel? SelectedMeeting
        {
            get => _selectedMeeting;
            set
            {
                if (SetProperty(ref _selectedMeeting, value))
                {
                    if (value != null) SelectedMessage = null;
                    OnPropertyChanged(nameof(HasSelectedMeeting));
                }
            }
        }

        public TeamsMessageViewModel? SelectedMessage
        {
            get => _selectedMessage;
            set
            {
                if (SetProperty(ref _selectedMessage, value))
                {
                    if (value != null) SelectedMeeting = null;
                    OnPropertyChanged(nameof(HasSelectedMessage));
                }
            }
        }

        public bool HasSelectedMeeting => SelectedMeeting != null;
        public bool HasSelectedMessage => SelectedMessage != null;

        public int TotalNotifications => UnreadMessages.Count + GetActiveRemindersCount();

        private int GetActiveRemindersCount()
        {
            int count = 0;
            foreach (var r in Reminders)
                if (!r.IsCompleted) count++;
            return count;
        }

        public TeamsTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _teamsService = new TeamsService();
        }

        public async Task InitializeAsync()
        {
            IsAvailable = _teamsService.IsTeamsAvailable();
            if (!IsAvailable)
            {
                StatusMessage = "Teams/Outlook calendar not available.";
            }
        }

        public async Task RefreshAsync()
        {
            if (!IsAvailable)
            {
                _teamsService.ResetAvailabilityCheck();
                IsAvailable = _teamsService.IsTeamsAvailable();
                if (!IsAvailable)
                {
                    StatusMessage = "Teams not available. Click refresh to retry.";
                    return;
                }
            }

            IsLoading = true;
            StatusMessage = "Loading Teams data...";

            try
            {
                // Load meetings
                var (meetings, timedOut) = await _teamsService.GetTodaysMeetingsWithTimeoutAsync();

                if (timedOut)
                {
                    StatusMessage = "Timeout loading meetings. Calendar may be busy.";
                    IsLoading = false;
                    return;
                }

                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    TodaysMeetings.Clear();
                    foreach (var meeting in meetings)
                    {
                        TodaysMeetings.Add(new TeamsMeetingViewModel(meeting, _teamsService, this));
                    }
                });

                // Load messages
                var messages = await _teamsService.GetUnreadMessagesAsync();
                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    UnreadMessages.Clear();
                    foreach (var message in messages)
                    {
                        UnreadMessages.Add(new TeamsMessageViewModel(message, _teamsService, this));
                    }
                });

                // Load reminders
                var reminders = await _teamsService.GetTeamsRemindersAsync();
                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    Reminders.Clear();
                    foreach (var reminder in reminders)
                    {
                        Reminders.Add(new TeamsReminderViewModel(reminder, this));
                    }
                });

                var graphStatus = _teamsService.IsGraphApiConfigured ? " (Graph API)" : " (Sample data)";
                StatusMessage = $"{TodaysMeetings.Count} meeting(s) today, {UnreadMessages.Count} unread message(s){graphStatus}";
                OnPropertyChanged(nameof(TotalNotifications));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _context.Logger.Error($"Failed to refresh Teams data: {ex.Message}", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public bool JoinMeeting(TeamsMeetingViewModel meeting)
        {
            if (string.IsNullOrEmpty(meeting.JoinUrl))
            {
                _context.UI.ShowToast("No join URL available for this meeting", ToastType.Warning);
                return false;
            }

            var result = _teamsService.JoinMeeting(meeting.JoinUrl);
            if (result)
            {
                _context.UI.ShowToast("Opening Teams meeting...", ToastType.Info);
            }
            return result;
        }

        public bool OpenTeamsChat(string chatId)
        {
            return _teamsService.OpenTeamsChat(chatId);
        }

        public bool OpenTeamsApp()
        {
            return _teamsService.OpenTeamsApp();
        }

        public void MarkMessageAsRead(TeamsMessageViewModel message)
        {
            message.MarkAsRead();
            OnPropertyChanged(nameof(TotalNotifications));
        }

        public void ToggleReminderComplete(TeamsReminderViewModel reminder)
        {
            reminder.ToggleComplete();
            OnPropertyChanged(nameof(TotalNotifications));
        }

        public override DataTemplate GetDataTemplate()
        {
            return (DataTemplate)WpfApplication.Current.FindResource("TeamsTabTemplate");
        }

        public override void Refresh()
        {
            _ = RefreshAsync();
        }

        public void Dispose()
        {
            // Cleanup
        }
    }

    /// <summary>
    /// View model for Teams meetings
    /// </summary>
    public class TeamsMeetingViewModel : INotifyPropertyChanged
    {
        private readonly TeamsService _service;
        private readonly TeamsTabViewModel _parent;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }
        public string Subject { get; }
        public string Organizer { get; }
        public string OrganizerEmail { get; }
        public DateTime StartTime { get; }
        public DateTime EndTime { get; }
        public string Location { get; }
        public bool IsAllDay { get; }
        public bool IsOnlineMeeting { get; }
        public string JoinUrl { get; }
        public string BodyPreview { get; }
        public MeetingStatus Status { get; private set; }

        public string TimeRange => IsAllDay ? "All Day" : $"{StartTime:HH:mm} - {EndTime:HH:mm}";
        public string TimeUntilStart => GetTimeUntilStart();
        public bool IsInProgress => Status == MeetingStatus.InProgress;
        public bool CanJoin => IsOnlineMeeting && !string.IsNullOrEmpty(JoinUrl);

        public TeamsMeetingViewModel(TeamsMeetingData data, TeamsService service, TeamsTabViewModel parent)
        {
            _service = service;
            _parent = parent;

            Id = data.Id;
            Subject = data.Subject;
            Organizer = data.Organizer;
            OrganizerEmail = data.OrganizerEmail;
            StartTime = data.StartTime;
            EndTime = data.EndTime;
            Location = data.Location;
            IsAllDay = data.IsAllDay;
            IsOnlineMeeting = data.IsOnlineMeeting;
            JoinUrl = data.JoinUrl;
            BodyPreview = data.BodyPreview;
            Status = data.Status;
        }

        public void JoinMeeting() => _parent.JoinMeeting(this);

        public void RefreshTimeDisplays()
        {
            // Update status based on current time
            if (DateTime.Now >= StartTime && DateTime.Now <= EndTime)
                Status = MeetingStatus.InProgress;
            else if (DateTime.Now > EndTime)
                Status = MeetingStatus.Completed;
            else
                Status = MeetingStatus.Scheduled;

            OnPropertyChanged(nameof(TimeUntilStart));
            OnPropertyChanged(nameof(IsInProgress));
        }

        private string GetTimeUntilStart()
        {
            if (Status == MeetingStatus.InProgress) return "In Progress";
            if (Status == MeetingStatus.Completed) return "Completed";

            var diff = StartTime - DateTime.Now;
            if (diff.TotalMinutes < 0) return "Started";
            if (diff.TotalMinutes < 60) return $"Starts in {(int)diff.TotalMinutes}m";
            if (diff.TotalHours < 24) return $"Starts in {(int)diff.TotalHours}h";
            return StartTime.ToString("MMM dd");
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// View model for Teams messages
    /// </summary>
    public class TeamsMessageViewModel : INotifyPropertyChanged
    {
        private readonly TeamsService _service;
        private readonly TeamsTabViewModel _parent;
        private bool _isRead;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }
        public string SenderName { get; }
        public string SenderEmail { get; }
        public string ChatId { get; }
        public string ChatName { get; }
        public string TeamName { get; }
        public string ChannelName { get; }
        public string Content { get; }
        public string ContentPreview { get; }
        public DateTime ReceivedDate { get; }
        public bool IsMention { get; }
        public TeamsMessageType MessageType { get; }

        public bool IsRead
        {
            get => _isRead;
            private set
            {
                if (_isRead != value)
                {
                    _isRead = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ReceivedDateText => GetRelativeDateText(ReceivedDate);
        public string SourceName => MessageType == TeamsMessageType.Channel ? $"{TeamName} > {ChannelName}" : ChatName;

        public TeamsMessageViewModel(TeamsMessageData data, TeamsService service, TeamsTabViewModel parent)
        {
            _service = service;
            _parent = parent;

            Id = data.Id;
            SenderName = data.SenderName;
            SenderEmail = data.SenderEmail;
            ChatId = data.ChatId;
            ChatName = data.ChatName;
            TeamName = data.TeamName;
            ChannelName = data.ChannelName;
            Content = data.Content;
            ContentPreview = data.ContentPreview;
            ReceivedDate = data.ReceivedDate;
            IsMention = data.IsMention;
            MessageType = data.MessageType;
            IsRead = data.IsRead;
        }

        public void MarkAsRead() => IsRead = true;
        public void OpenChat() => _parent.OpenTeamsChat(ChatId);

        private static string GetRelativeDateText(DateTime date)
        {
            var diff = DateTime.Now - date;
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return date.ToString("MMM dd");
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// View model for Teams reminders
    /// </summary>
    public class TeamsReminderViewModel : INotifyPropertyChanged
    {
        private readonly TeamsTabViewModel _parent;
        private bool _isCompleted;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public DateTime DueDate { get; }
        public string Source { get; }

        public bool IsCompleted
        {
            get => _isCompleted;
            private set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DueDateText => GetRelativeDateText(DueDate);
        public bool IsOverdue => DueDate < DateTime.Now && !IsCompleted;

        public TeamsReminderViewModel(TeamsReminderData data, TeamsTabViewModel parent)
        {
            _parent = parent;

            Id = data.Id;
            Title = data.Title;
            Description = data.Description;
            DueDate = data.DueDate;
            Source = data.Source;
            IsCompleted = data.IsCompleted;
        }

        public void ToggleComplete() => IsCompleted = !IsCompleted;

        private static string GetRelativeDateText(DateTime date)
        {
            var diff = date - DateTime.Now;
            if (diff.TotalMinutes < 0)
            {
                var overdue = DateTime.Now - date;
                if (overdue.TotalMinutes < 60) return $"{(int)overdue.TotalMinutes}m overdue";
                if (overdue.TotalHours < 24) return $"{(int)overdue.TotalHours}h overdue";
                return $"{(int)overdue.TotalDays}d overdue";
            }
            if (diff.TotalMinutes < 60) return $"In {(int)diff.TotalMinutes}m";
            if (diff.TotalHours < 24) return $"In {(int)diff.TotalHours}h";
            return date.ToString("MMM dd");
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Settings view model for Teams plugin
    /// </summary>
    public class TeamsSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private int _refreshIntervalMinutes = 5;
        private string _clientId = string.Empty;
        private string _tenantId = string.Empty;

        public override string SettingsTitle => "Teams Settings";
        public override string SettingsDescription => "Configure Teams integration behavior and Microsoft Graph API";

        public int RefreshIntervalMinutes
        {
            get => _refreshIntervalMinutes;
            set => SetProperty(ref _refreshIntervalMinutes, value);
        }

        public string ClientId
        {
            get => _clientId;
            set => SetProperty(ref _clientId, value);
        }

        public string TenantId
        {
            get => _tenantId;
            set => SetProperty(ref _tenantId, value);
        }

        public TeamsSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage;
            Load();
        }

        public override void Load()
        {
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 5);
            ClientId = _storage.Get("ClientId", string.Empty) ?? string.Empty;
            TenantId = _storage.Get("TenantId", string.Empty) ?? string.Empty;
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("ClientId", ClientId);
            _storage.Set("TenantId", TenantId);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            RefreshIntervalMinutes = 5;
            ClientId = string.Empty;
            TenantId = string.Empty;
        }
    }
}
