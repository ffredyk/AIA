using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using AIA.Models.AI;
using AIA.Services;
using AIA.Services.AI;

namespace AIA.Models
{
    public class OverlayViewModel : INotifyPropertyChanged
    {
        public static OverlayViewModel Singleton { get; } = new OverlayViewModel();

        private ChatSession _selectedChatSession;
        private string _messageInput;
        private TaskItem? _selectedTask;
        private bool _isAddingNewTask;
        private string _newTaskTitle = string.Empty;
        private ReminderItem? _selectedReminder;
        private bool _isAddingNewReminder;
        private string _newReminderTitle = string.Empty;
        private DispatcherTimer? _reminderRefreshTimer;
        private DispatcherTimer? _windowTrackingTimer;
        
        // Data Bank fields
        private DataBankCategory? _selectedCategory;
        private DataBankEntry? _selectedDataEntry;
        private bool _isAddingNewCategory;
        private string _newCategoryName = string.Empty;
        private bool _isAddingNewEntry;
        private string _newEntryTitle = string.Empty;

        // Outlook fields
        private OutlookEmail? _selectedOutlookEmail;
        private bool _isLoadingOutlookEmails;
        private bool _isOutlookAvailable;
        private string _outlookStatusMessage = string.Empty;
        private DispatcherTimer? _outlookRefreshTimer;

        // Teams fields
        private TeamsMeeting? _selectedTeamsMeeting;
        private TeamsMessage? _selectedTeamsMessage;
        private bool _isLoadingTeamsData;
        private bool _isTeamsAvailable;
        private string _teamsStatusMessage = string.Empty;
        private DispatcherTimer? _teamsRefreshTimer;

        // AI Orchestration
        private bool _isAiProcessing;
        private string _aiStatusMessage = string.Empty;
        private AIProvider? _selectedAiProvider;

        // Screen capture service
        private readonly ScreenCaptureService _screenCaptureService = new();

        // Reminder notification service
        private readonly ReminderNotificationService _reminderNotificationService = new();

        // AI Orchestration service
        private AIOrchestrationService? _aiOrchestrationService;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Event fired when a reminder notification should be displayed
        /// </summary>
        public event EventHandler<ReminderNotification>? ReminderNotificationTriggered;

        /// <summary>
        /// Settings for reminder notifications
        /// </summary>
        public ReminderNotificationSettings NotificationSettings => _reminderNotificationService.Settings;

        /// <summary>
        /// AI Orchestration service instance
        /// </summary>
        public AIOrchestrationService AIOrchestration => _aiOrchestrationService ??= new AIOrchestrationService(() => this);

        /// <summary>
        /// Whether AI is currently processing a request
        /// </summary>
        public bool IsAiProcessing
        {
            get => _isAiProcessing;
            set
            {
                if (_isAiProcessing != value)
                {
                    _isAiProcessing = value;
                    OnPropertyChanged(nameof(IsAiProcessing));
                }
            }
        }

        /// <summary>
        /// AI status message for display
        /// </summary>
        public string AiStatusMessage
        {
            get => _aiStatusMessage;
            set
            {
                if (_aiStatusMessage != value)
                {
                    _aiStatusMessage = value;
                    OnPropertyChanged(nameof(AiStatusMessage));
                }
            }
        }

        /// <summary>
        /// Currently selected AI provider for manual selection
        /// </summary>
        public AIProvider? SelectedAiProvider
        {
            get => _selectedAiProvider;
            set
            {
                if (_selectedAiProvider != value)
                {
                    _selectedAiProvider = value;
                    OnPropertyChanged(nameof(SelectedAiProvider));
                }
            }
        }

        public ObservableCollection<ChatSession> ActiveChats { get; set; } = new ObservableCollection<ChatSession>()
        {
            new ChatSession() 
            { 
                ChatTitle = "Chat 1", 
                ChatSummary = "Summary of Chat 1",
                Messages = 
                {
                    new ChatMessage { Role = "user", Content = "Hello! Can you help me with this task?" },
                    new ChatMessage { Role = "assistant", Content = "Of course! I'd be happy to help you. What do you need assistance with?" },
                    new ChatMessage { Role = "user", Content = "I need to implement a new feature in my application." },
                    new ChatMessage { Role = "assistant", Content = "That sounds great! Can you provide more details about the feature you want to implement? What should it do and what are the requirements?" }
                }
            },
            new ChatSession() 
            { 
                ChatTitle = "Chat 2", 
                ChatSummary = "Summary of Chat 2",
                Messages =
                {
                    new ChatMessage { Role = "user", Content = "What's the weather like?" },
                    new ChatMessage { Role = "assistant", Content = "I don't have access to real-time weather data, but I can help you with coding questions!" }
                }
            },
            new ChatSession() 
            { 
                ChatTitle = "Chat 3", 
                ChatSummary = "Summary of Chat 3",
                Messages =
                {
                    new ChatMessage { Role = "assistant", Content = "Welcome to a new chat session! How can I assist you today?" }
                }
            },
        };

        public ObservableCollection<TaskItem> Tasks { get; set; } = new ObservableCollection<TaskItem>();

        public ObservableCollection<ReminderItem> Reminders { get; set; } = new ObservableCollection<ReminderItem>();

        // Data Bank collections
        public ObservableCollection<DataBankCategory> DataBankCategories { get; set; } = new ObservableCollection<DataBankCategory>();
        public ObservableCollection<DataBankEntry> CurrentCategoryEntries { get; set; } = new ObservableCollection<DataBankEntry>();
        private List<DataBankEntry> _allEntries = new List<DataBankEntry>();

        // Current data assets collection
        public ObservableCollection<DataAsset> CurrentDataAssets { get; set; } = new ObservableCollection<DataAsset>();

        // Outlook collections
        public ObservableCollection<OutlookEmail> FlaggedEmails { get; set; } = new ObservableCollection<OutlookEmail>();

        // Teams collections
        public ObservableCollection<TeamsMeeting> TodaysMeetings { get; set; } = new ObservableCollection<TeamsMeeting>();
        public ObservableCollection<TeamsMessage> UnreadMessages { get; set; } = new ObservableCollection<TeamsMessage>();
        public ObservableCollection<TeamsReminder> TeamsReminders { get; set; } = new ObservableCollection<TeamsReminder>();

        public TaskItem? SelectedTask
        {
            get => _selectedTask;
            set
            {
                if (_selectedTask != value)
                {
                    _selectedTask = value;
                    OnPropertyChanged(nameof(SelectedTask));
                    OnPropertyChanged(nameof(HasSelectedTask));
                }
            }
        }

        public bool HasSelectedTask => SelectedTask != null;

        public bool IsAddingNewTask
        {
            get => _isAddingNewTask;
            set
            {
                if (_isAddingNewTask != value)
                {
                    _isAddingNewTask = value;
                    OnPropertyChanged(nameof(IsAddingNewTask));
                }
            }
        }

        public string NewTaskTitle
        {
            get => _newTaskTitle;
            set
            {
                if (_newTaskTitle != value)
                {
                    _newTaskTitle = value;
                    OnPropertyChanged(nameof(NewTaskTitle));
                }
            }
        }

        public ReminderItem? SelectedReminder
        {
            get => _selectedReminder;
            set
            {
                if (_selectedReminder != value)
                {
                    _selectedReminder = value;
                    OnPropertyChanged(nameof(SelectedReminder));
                    OnPropertyChanged(nameof(HasSelectedReminder));
                }
            }
        }

        public bool HasSelectedReminder => SelectedReminder != null;

        public bool IsAddingNewReminder
        {
            get => _isAddingNewReminder;
            set
            {
                if (_isAddingNewReminder != value)
                {
                    _isAddingNewReminder = value;
                    OnPropertyChanged(nameof(IsAddingNewReminder));
                }
            }
        }

        public string NewReminderTitle
        {
            get => _newReminderTitle;
            set
            {
                if (_newReminderTitle != value)
                {
                    _newReminderTitle = value;
                    OnPropertyChanged(nameof(NewReminderTitle));
                }
            }
        }

        // Data Bank properties
        public DataBankCategory? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory != value)
                {
                    _selectedCategory = value;
                    OnPropertyChanged(nameof(SelectedCategory));
                    OnPropertyChanged(nameof(HasSelectedCategory));
                    RefreshCurrentCategoryEntries();
                }
            }
        }

        public bool HasSelectedCategory => SelectedCategory != null;

        public DataBankEntry? SelectedDataEntry
        {
            get => _selectedDataEntry;
            set
            {
                if (_selectedDataEntry != value)
                {
                    _selectedDataEntry = value;
                    OnPropertyChanged(nameof(SelectedDataEntry));
                    OnPropertyChanged(nameof(HasSelectedDataEntry));
                }
            }
        }

        public bool HasSelectedDataEntry => SelectedDataEntry != null;

        public bool IsAddingNewCategory
        {
            get => _isAddingNewCategory;
            set
            {
                if (_isAddingNewCategory != value)
                {
                    _isAddingNewCategory = value;
                    OnPropertyChanged(nameof(IsAddingNewCategory));
                }
            }
        }

        public string NewCategoryName
        {
            get => _newCategoryName;
            set
            {
                if (_newCategoryName != value)
                {
                    _newCategoryName = value;
                    OnPropertyChanged(nameof(NewCategoryName));
                }
            }
        }

        public bool IsAddingNewEntry
        {
            get => _isAddingNewEntry;
            set
            {
                if (_isAddingNewEntry != value)
                {
                    _isAddingNewEntry = value;
                    OnPropertyChanged(nameof(IsAddingNewEntry));
                }
            }
        }

        public string NewEntryTitle
        {
            get => _newEntryTitle;
            set
            {
                if (_newEntryTitle != value)
                {
                    _newEntryTitle = value;
                    OnPropertyChanged(nameof(NewEntryTitle));
                }
            }
        }

        // Outlook properties
        public OutlookEmail? SelectedOutlookEmail
        {
            get => _selectedOutlookEmail;
            set
            {
                if (_selectedOutlookEmail != value)
                {
                    _selectedOutlookEmail = value;
                    OnPropertyChanged(nameof(SelectedOutlookEmail));
                    OnPropertyChanged(nameof(HasSelectedOutlookEmail));
                }
            }
        }

        public bool HasSelectedOutlookEmail => SelectedOutlookEmail != null;

        public bool IsLoadingOutlookEmails
        {
            get => _isLoadingOutlookEmails;
            set
            {
                if (_isLoadingOutlookEmails != value)
                {
                    _isLoadingOutlookEmails = value;
                    OnPropertyChanged(nameof(IsLoadingOutlookEmails));
                    OnPropertyChanged(nameof(ShowOutlookEmptyState));
                }
            }
        }

        public bool IsOutlookAvailable
        {
            get => _isOutlookAvailable;
            set
            {
                if (_isOutlookAvailable != value)
                {
                    _isOutlookAvailable = value;
                    OnPropertyChanged(nameof(IsOutlookAvailable));
                    OnPropertyChanged(nameof(ShowOutlookEmptyState));
                }
            }
        }

        public string OutlookStatusMessage
        {
            get => _outlookStatusMessage;
            set
            {
                if (_outlookStatusMessage != value)
                {
                    _outlookStatusMessage = value;
                    OnPropertyChanged(nameof(OutlookStatusMessage));
                }
            }
        }

        // Computed property for empty state visibility
        public bool ShowOutlookEmptyState => IsOutlookAvailable && !IsLoadingOutlookEmails && FlaggedEmails.Count == 0;

        // Teams properties
        public TeamsMeeting? SelectedTeamsMeeting
        {
            get => _selectedTeamsMeeting;
            set
            {
                if (_selectedTeamsMeeting != value)
                {
                    _selectedTeamsMeeting = value;
                    OnPropertyChanged(nameof(SelectedTeamsMeeting));
                    OnPropertyChanged(nameof(HasSelectedTeamsMeeting));
                }
            }
        }

        public bool HasSelectedTeamsMeeting => SelectedTeamsMeeting != null;

        public TeamsMessage? SelectedTeamsMessage
        {
            get => _selectedTeamsMessage;
            set
            {
                if (_selectedTeamsMessage != value)
                {
                    _selectedTeamsMessage = value;
                    OnPropertyChanged(nameof(SelectedTeamsMessage));
                    OnPropertyChanged(nameof(HasSelectedTeamsMessage));
                }
            }
        }

        public bool HasSelectedTeamsMessage => SelectedTeamsMessage != null;

        public bool IsLoadingTeamsData
        {
            get => _isLoadingTeamsData;
            set
            {
                if (_isLoadingTeamsData != value)
                {
                    _isLoadingTeamsData = value;
                    OnPropertyChanged(nameof(IsLoadingTeamsData));
                    OnPropertyChanged(nameof(ShowTeamsEmptyState));
                }
            }
        }

        public bool IsTeamsAvailable
        {
            get => _isTeamsAvailable;
            set
            {
                if (_isTeamsAvailable != value)
                {
                    _isTeamsAvailable = value;
                    OnPropertyChanged(nameof(IsTeamsAvailable));
                    OnPropertyChanged(nameof(ShowTeamsEmptyState));
                }
            }
        }

        public string TeamsStatusMessage
        {
            get => _teamsStatusMessage;
            set
            {
                if (_teamsStatusMessage != value)
                {
                    _teamsStatusMessage = value;
                    OnPropertyChanged(nameof(TeamsStatusMessage));
                }
            }
        }

        public bool ShowTeamsEmptyState => IsTeamsAvailable && !IsLoadingTeamsData && TodaysMeetings.Count == 0;

        public int TotalTeamsNotifications => UnreadMessages.Count + TeamsReminders.Count(r => !r.IsCompleted);

        public ChatSession SelectedChatSession
        {
            get => _selectedChatSession;
            set
            {
                if (_selectedChatSession != value)
                {
                    _selectedChatSession = value;
                    OnPropertyChanged(nameof(SelectedChatSession));
                }
            }
        }

        public string MessageInput
        {
            get => _messageInput;
            set
            {
                if (_messageInput != value)
                {
                    _messageInput = value;
                    OnPropertyChanged(nameof(MessageInput));
                }
            }
        }

        public Array TaskStatusValues => Enum.GetValues(typeof(TaskStatus));
        public Array TaskPriorityValues => Enum.GetValues(typeof(TaskPriority));
        public Array ReminderSeverityValues => Enum.GetValues(typeof(ReminderSeverity));
        public Array DataEntryTypeValues => Enum.GetValues(typeof(DataEntryType));

        public OverlayViewModel()
        {
            // Set the first chat as selected by default
            if (ActiveChats.Count > 0)
            {
                SelectedChatSession = ActiveChats[0];
            }

            // Load tasks and reminders from disk (or initialize with samples if first run)
            _ = LoadTasksAndRemindersAsync();

            // Start timer to refresh reminder time displays
            StartReminderRefreshTimer();

            // Start window tracking timer
            StartWindowTrackingTimer();

            // Initialize reminder notification service
            InitializeReminderNotificationService();

            // Load data banks
            _ = LoadDataBanksAsync();

            // Initialize Outlook
            _ = InitializeOutlookAsync();

            // Initialize Teams
            _ = InitializeTeamsAsync();
        }

        private void InitializeReminderNotificationService()
        {
            _reminderNotificationService.NotificationTriggered += (sender, notification) =>
            {
                // Forward the notification event
                ReminderNotificationTriggered?.Invoke(this, notification);
            };
            _reminderNotificationService.Start();
        }

        private void StartReminderRefreshTimer()
        {
            _reminderRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _reminderRefreshTimer.Tick += (s, e) =>
            {
                foreach (var reminder in Reminders)
                {
                    reminder.RefreshTimeLeft();
                }
                // Also refresh Teams meetings time displays
                foreach (var meeting in TodaysMeetings)
                {
                    meeting.RefreshTimeDisplays();
                }
                foreach (var teamsReminder in TeamsReminders)
                {
                    teamsReminder.RefreshTimeDisplays();
                }
            };
            _reminderRefreshTimer.Start();
        }

        private void StartWindowTrackingTimer()
        {
            _windowTrackingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _windowTrackingTimer.Tick += (s, e) =>
            {
                _screenCaptureService.TrackActiveWindow();
            };
            _windowTrackingTimer.Start();
        }

        /// <summary>
        /// Sets the overlay window handle to exclude from captures
        /// </summary>
        public void SetOverlayWindowHandle(IntPtr handle)
        {
            _screenCaptureService.SetOverlayWindowHandle(handle);
        }

        /// <summary>
        /// Pauses window tracking (call when overlay is shown)
        /// </summary>
        public void PauseWindowTracking()
        {
            _windowTrackingTimer?.Stop();
        }

        /// <summary>
        /// Resumes window tracking (call when overlay is hidden)
        /// </summary>
        public void ResumeWindowTracking()
        {
            _windowTrackingTimer?.Start();
        }

        /// <summary>
        /// Captures current screen and recent windows, updates CurrentDataAssets
        /// </summary>
        public void CaptureCurrentDataAssets()
        {
            CurrentDataAssets.Clear();

            var assets = _screenCaptureService.CaptureAllDataAssets();
            foreach (var asset in assets)
            {
                CurrentDataAssets.Add(asset);
            }
        }

        #region Teams Methods

        private async Task InitializeTeamsAsync()
        {
            IsTeamsAvailable = TeamsService.IsTeamsAvailable();

            if (IsTeamsAvailable)
            {
                TeamsStatusMessage = "Loading Teams data...";
                await RefreshTeamsDataAsync();
                StartTeamsRefreshTimer();
            }
            else
            {
                TeamsStatusMessage = "Teams/Outlook calendar not available.";
            }
        }

        private void StartTeamsRefreshTimer()
        {
            _teamsRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _teamsRefreshTimer.Tick += async (s, e) =>
            {
                await RefreshTeamsDataAsync();
            };
            _teamsRefreshTimer.Start();
        }

        public async Task RefreshTeamsDataAsync()
        {
            if (!IsTeamsAvailable)
            {
                TeamsService.ResetAvailabilityCheck();
                IsTeamsAvailable = TeamsService.IsTeamsAvailable();

                if (!IsTeamsAvailable)
                {
                    TeamsStatusMessage = "Teams not available. Click refresh to retry.";
                    return;
                }
            }

            IsLoadingTeamsData = true;
            TeamsStatusMessage = "Loading Teams data...";

            try
            {
                // Load meetings from calendar (uses Graph API if configured, otherwise Outlook)
                var (meetings, timedOut) = await TeamsService.GetTodaysMeetingsWithTimeoutAsync();

                if (timedOut)
                {
                    TeamsStatusMessage = "Timeout loading meetings. Calendar may be busy.";
                    IsLoadingTeamsData = false;
                    OnPropertyChanged(nameof(ShowTeamsEmptyState));
                    return;
                }

                TodaysMeetings.Clear();
                foreach (var meeting in meetings)
                {
                    TodaysMeetings.Add(meeting);
                }

                // Load unread messages (uses Graph API if configured, otherwise sample data)
                UnreadMessages.Clear();
                var messages = await TeamsService.GetUnreadMessagesAsync();
                foreach (var message in messages)
                {
                    UnreadMessages.Add(message);
                }

                // Load reminders/tasks (uses Graph API if configured, otherwise sample data)
                TeamsReminders.Clear();
                var reminders = await TeamsService.GetTeamsRemindersAsync();
                foreach (var reminder in reminders)
                {
                    TeamsReminders.Add(reminder);
                }

                var meetingCount = TodaysMeetings.Count;
                var messageCount = UnreadMessages.Count;
                var graphStatus = TeamsService.IsGraphApiConfigured ? " (Graph API)" : " (Sample data)";
                TeamsStatusMessage = $"{meetingCount} meeting(s) today, {messageCount} unread message(s){graphStatus}";
                OnPropertyChanged(nameof(TotalTeamsNotifications));
            }
            catch (Exception ex)
            {
                TeamsStatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoadingTeamsData = false;
                OnPropertyChanged(nameof(ShowTeamsEmptyState));
            }
        }

        /// <summary>
        /// Opens a Teams chat for the selected message
        /// </summary>
        public bool OpenTeamsChat(TeamsMessage message)
        {
            if (message == null) return false;
            return TeamsService.OpenTeamsChat(message.ChatId);
        }

        /// <summary>
        /// Joins a Teams meeting
        /// </summary>
        public bool JoinTeamsMeeting(TeamsMeeting meeting)
        {
            if (meeting == null || string.IsNullOrEmpty(meeting.JoinUrl))
                return false;

            return TeamsService.JoinMeeting(meeting.JoinUrl);
        }

        /// <summary>
        /// Opens Microsoft Teams application
        /// </summary>
        public bool OpenTeamsApp()
        {
            return TeamsService.OpenTeamsApp();
        }

        /// <summary>
        /// Marks a Teams message as read
        /// </summary>
        public void MarkTeamsMessageAsRead(TeamsMessage message)
        {
            if (message == null) return;
            message.IsRead = true;
            OnPropertyChanged(nameof(TotalTeamsNotifications));
        }

        /// <summary>
        /// Toggles completion of a Teams reminder
        /// </summary>
        public void CompleteTeamsReminder(TeamsReminder reminder)
        {
            if (reminder == null) return;
            reminder.IsCompleted = !reminder.IsCompleted;
            OnPropertyChanged(nameof(TotalTeamsNotifications));
        }

        #endregion

        #region Outlook Methods

        private async Task InitializeOutlookAsync()
        {
            IsOutlookAvailable = OutlookService.IsOutlookAvailable();
            
            if (IsOutlookAvailable)
            {
                OutlookStatusMessage = "Loading flagged emails...";
                await RefreshFlaggedEmailsAsync();
                StartOutlookRefreshTimer();
            }
            else
            {
                OutlookStatusMessage = "Outlook is not installed or not available.";
            }
        }

        private void StartOutlookRefreshTimer()
        {
            _outlookRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _outlookRefreshTimer.Tick += async (s, e) =>
            {
                await RefreshFlaggedEmailsAsync();
                foreach (var email in FlaggedEmails)
                {
                    email.RefreshTimeDisplays();
                }
            };
            _outlookRefreshTimer.Start();
        }

        public async Task RefreshFlaggedEmailsAsync()
        {
            if (!IsOutlookAvailable)
            {
                OutlookService.ResetAvailabilityCheck();
                IsOutlookAvailable = OutlookService.IsOutlookAvailable();
                
                if (!IsOutlookAvailable)
                {
                    OutlookStatusMessage = "Outlook is not available. Click refresh to retry.";
                    return;
                }
            }

            IsLoadingOutlookEmails = true;
            OutlookStatusMessage = "Loading flagged emails...";

            try
            {
                var (emails, timedOut) = await OutlookService.GetFlaggedEmailsWithTimeoutAsync();
                
                if (timedOut)
                {
                    OutlookStatusMessage = "Timeout loading emails. Outlook may be busy.";
                    IsLoadingOutlookEmails = false;
                    OnPropertyChanged(nameof(ShowOutlookEmptyState));
                    return;
                }
                
                FlaggedEmails.Clear();
                foreach (var email in emails)
                {
                    FlaggedEmails.Add(email);
                }

                if (FlaggedEmails.Count == 0)
                {
                    OutlookStatusMessage = "No flagged emails found.";
                }
                else
                {
                    OutlookStatusMessage = $"{FlaggedEmails.Count} flagged email(s)";
                }
            }
            catch (Exception ex)
            {
                OutlookStatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoadingOutlookEmails = false;
                OnPropertyChanged(nameof(ShowOutlookEmptyState));
            }
        }

        public async Task MarkEmailFlagCompleteAsync(OutlookEmail email)
        {
            if (email == null) return;

            var success = await OutlookService.MarkFlagCompleteAsync(email.EntryId);
            if (success)
            {
                email.FlagStatus = EmailFlagStatus.Complete;
                FlaggedEmails.Remove(email);
                
                if (SelectedOutlookEmail == email)
                {
                    SelectedOutlookEmail = null;
                }

                OutlookStatusMessage = $"{FlaggedEmails.Count} flagged email(s)";
            }
        }

        public async Task ClearEmailFlagAsync(OutlookEmail email)
        {
            if (email == null) return;

            var success = await OutlookService.ClearFlagAsync(email.EntryId);
            if (success)
            {
                email.FlagStatus = EmailFlagStatus.NotFlagged;
                FlaggedEmails.Remove(email);
                
                if (SelectedOutlookEmail == email)
                {
                    SelectedOutlookEmail = null;
                }

                OutlookStatusMessage = $"{FlaggedEmails.Count} flagged email(s)";
            }
        }

        public async Task OpenEmailInOutlookAsync(OutlookEmail email)
        {
            if (email == null) return;
            await OutlookService.OpenEmailAsync(email.EntryId);
        }

        #endregion

        #region Data Asset Save Methods

        public bool CopyDataAssetToClipboard(DataAsset asset)
        {
            return ScreenCaptureService.CopyToClipboard(asset);
        }

        public string? SaveDataAssetToFile(DataAsset asset)
        {
            return ScreenCaptureService.SaveToFile(asset);
        }

        public string? SaveDataAssetWithDialog(DataAsset asset)
        {
            return ScreenCaptureService.SaveToFileWithDialog(asset);
        }

        public async Task<bool> SaveDataAssetToDataBankAsync(DataAsset asset, DataBankCategory? category = null)
        {
            var targetCategory = category ?? SelectedCategory;
            
            if (targetCategory == null)
                return false;

            var entry = await ScreenCaptureService.CreateDataBankEntryAsync(asset, targetCategory.Id);
            if (entry == null)
                return false;

            _allEntries.Add(entry);
            targetCategory.EntryCount++;

            if (targetCategory == SelectedCategory)
            {
                CurrentCategoryEntries.Add(entry);
            }

            await SaveDataBanksAsync();
            return true;
        }

        #endregion

        private async Task LoadTasksAndRemindersAsync()
        {
            var tasks = await TaskReminderService.LoadTasksAsync();
            var reminders = await TaskReminderService.LoadRemindersAsync();

            if (tasks.Count == 0 && reminders.Count == 0)
            {
                // First run - initialize with sample data
                InitializeSampleTasks();
                InitializeSampleReminders();
                await SaveTasksAndRemindersAsync();
            }
            else
            {
                foreach (var task in tasks)
                {
                    Tasks.Add(task);
                }
                foreach (var reminder in reminders)
                {
                    Reminders.Add(reminder);
                }
            }
        }

        /// <summary>
        /// Saves all tasks and reminders to disk
        /// </summary>
        public async Task SaveTasksAndRemindersAsync()
        {
            await TaskReminderService.SaveTasksAsync(Tasks);
            await TaskReminderService.SaveRemindersAsync(Reminders);
        }

        private void InitializeSampleTasks()
        {
            var task1 = new TaskItem
            {
                Title = "Implement user authentication",
                Description = "Add login and registration functionality to the application",
                Status = TaskStatus.InProgress,
                Priority = TaskPriority.High,
                DueDate = DateTime.Now.AddDays(3),
                Notes = "Use OAuth 2.0 for social login options"
            };

            var subtask1 = new TaskItem
            {
                Title = "Design login UI",
                Description = "Create the login form layout",
                Status = TaskStatus.Completed,
                Priority = TaskPriority.Medium
            };
            task1.AddSubtask(subtask1);

            var subtask2 = new TaskItem
            {
                Title = "Implement backend API",
                Description = "Create authentication endpoints",
                Status = TaskStatus.InProgress,
                Priority = TaskPriority.High
            };
            task1.AddSubtask(subtask2);

            var subtask3 = new TaskItem
            {
                Title = "Add token validation",
                Description = "Implement JWT token validation",
                Status = TaskStatus.NotStarted,
                Priority = TaskPriority.Medium
            };
            task1.AddSubtask(subtask3);

            Tasks.Add(task1);

            var task2 = new TaskItem
            {
                Title = "Fix navigation bug",
                Description = "Users report that back button doesn't work correctly",
                Status = TaskStatus.NotStarted,
                Priority = TaskPriority.Critical,
                DueDate = DateTime.Now.AddDays(1),
                Notes = "Bug reported in version 2.1.0"
            };
            Tasks.Add(task2);

            var task3 = new TaskItem
            {
                Title = "Update documentation",
                Description = "Refresh the API documentation with new endpoints",
                Status = TaskStatus.Completed,
                Priority = TaskPriority.Low,
                DueDate = DateTime.Now.AddDays(-2)
            };
            Tasks.Add(task3);
        }

        private void InitializeSampleReminders()
        {
            var reminder1 = new ReminderItem
            {
                Title = "Team standup meeting",
                DueDate = DateTime.Now.AddMinutes(30),
                Severity = ReminderSeverity.High
            };
            Reminders.Add(reminder1);

            var reminder2 = new ReminderItem
            {
                Title = "Submit weekly report",
                DueDate = DateTime.Now.AddDays(2),
                Severity = ReminderSeverity.Medium
            };
            Reminders.Add(reminder2);

            var reminder3 = new ReminderItem
            {
                Title = "Review pull requests",
                DueDate = DateTime.Now.AddHours(4),
                Severity = ReminderSeverity.Low
            };
            Reminders.Add(reminder3);

            var reminder4 = new ReminderItem
            {
                Title = "Client demo preparation",
                DueDate = DateTime.Now.AddMinutes(-15),
                Severity = ReminderSeverity.Urgent
            };
            Reminders.Add(reminder4);
        }

        public void AddNewTask(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return;

            var task = new TaskItem
            {
                Title = title,
                Status = TaskStatus.NotStarted,
                Priority = TaskPriority.Medium
            };
            Tasks.Add(task);
            SelectedTask = task;
            NewTaskTitle = string.Empty;
            IsAddingNewTask = false;
            
            _ = SaveTasksAndRemindersAsync();
        }

        public void AddSubtask(TaskItem parentTask, string title)
        {
            if (parentTask == null || string.IsNullOrWhiteSpace(title)) return;

            var subtask = new TaskItem
            {
                Title = title,
                Status = TaskStatus.NotStarted,
                Priority = TaskPriority.Medium
            };
            parentTask.AddSubtask(subtask);
            
            _ = SaveTasksAndRemindersAsync();
        }

        public void DeleteTask(TaskItem task)
        {
            if (task == null) return;

            if (task.ParentTaskId.HasValue)
            {
                // It's a subtask, find parent and remove
                var parent = FindTaskById(task.ParentTaskId.Value);
                parent?.Subtasks.Remove(task);
                parent?.RefreshSubtaskProgress();
            }
            else
            {
                Tasks.Remove(task);
            }

            if (SelectedTask == task)
            {
                SelectedTask = null;
            }
            
            _ = SaveTasksAndRemindersAsync();
        }

        public TaskItem? FindTaskById(Guid id)
        {
            foreach (var task in Tasks)
            {
                if (task.Id == id) return task;
                foreach (var subtask in task.Subtasks)
                {
                    if (subtask.Id == id) return subtask;
                }
            }
            return null;
        }

        public void AddNewReminder(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return;

            var reminder = new ReminderItem
            {
                Title = title,
                DueDate = DateTime.Now.AddHours(1),
                Severity = ReminderSeverity.Medium
            };
            Reminders.Add(reminder);
            SelectedReminder = reminder;
            NewReminderTitle = string.Empty;
            IsAddingNewReminder = false;
            
            _ = SaveTasksAndRemindersAsync();
        }

        public void DeleteReminder(ReminderItem reminder)
        {
            if (reminder == null) return;

            Reminders.Remove(reminder);

            if (SelectedReminder == reminder)
            {
                SelectedReminder = null;
            }
            
            _ = SaveTasksAndRemindersAsync();
        }

        public void ToggleReminderComplete(ReminderItem reminder)
        {
            if (reminder == null) return;
            reminder.IsCompleted = !reminder.IsCompleted;
            
            _ = SaveTasksAndRemindersAsync();
        }

        /// <summary>
        /// Snoozes a reminder by the specified minutes (default 15)
        /// </summary>
        public void SnoozeReminder(ReminderItem reminder, int minutes = 15)
        {
            if (reminder == null) return;

            // If already overdue, snooze from now
            var baseTime = reminder.DueDate < DateTime.Now ? DateTime.Now : reminder.DueDate;
            reminder.DueDate = baseTime.AddMinutes(minutes);

            // Clear notification history so it can notify again
            _reminderNotificationService.ClearNotificationHistory(reminder.Id);

            reminder.RefreshTimeLeft();
            
            _ = SaveTasksAndRemindersAsync();
        }

        /// <summary>
        /// Forces a check of all reminders for notifications
        /// </summary>
        public void CheckReminderNotifications()
        {
            _reminderNotificationService.CheckReminders();
        }

        #region Data Bank Methods

        private async Task LoadDataBanksAsync()
        {
            var metadata = await DataBankService.LoadMetadataAsync();

            DataBankCategories.Clear();
            foreach (var category in metadata.Categories)
            {
                DataBankCategories.Add(category);
            }

            _allEntries = metadata.Entries;

            foreach (var category in DataBankCategories)
            {
                category.EntryCount = _allEntries.Count(e => e.CategoryId == category.Id);
            }

            if (DataBankCategories.Count > 0)
            {
                SelectedCategory = DataBankCategories[0];
            }
        }

        public async Task SaveDataBanksAsync()
        {
            var metadata = new DataBankMetadata
            {
                Categories = DataBankCategories.ToList(),
                Entries = _allEntries
            };

            await DataBankService.SaveMetadataAsync(metadata);
        }

        private void RefreshCurrentCategoryEntries()
        {
            CurrentCategoryEntries.Clear();

            if (SelectedCategory == null) return;

            foreach (var entry in _allEntries.Where(e => e.CategoryId == SelectedCategory.Id))
            {
                CurrentCategoryEntries.Add(entry);
            }
        }

        public async Task AddNewCategoryAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            var category = new DataBankCategory
            {
                Name = name,
                Color = GetNextCategoryColor()
            };

            DataBankCategories.Add(category);
            SelectedCategory = category;
            NewCategoryName = string.Empty;
            IsAddingNewCategory = false;

            await SaveDataBanksAsync();
        }

        private string GetNextCategoryColor()
        {
            string[] colors = { "#0078D4", "#1EB75F", "#FFA500", "#FF4444", "#9B59B6", "#3498DB", "#E74C3C", "#2ECC71" };
            return colors[DataBankCategories.Count % colors.Length];
        }

        public async Task DeleteCategoryAsync(DataBankCategory category)
        {
            if (category == null) return;

            // Delete all entries in this category and their files
            var entriesToDelete = _allEntries.Where(e => e.CategoryId == category.Id).ToList();
            foreach (var entry in entriesToDelete)
            {
                DataBankService.DeleteFile(entry.FilePath);
                _allEntries.Remove(entry);
            }

            DataBankCategories.Remove(category);

            if (SelectedCategory == category)
            {
                SelectedCategory = DataBankCategories.FirstOrDefault();
            }

            await SaveDataBanksAsync();
        }

        public async Task AddNewEntryAsync(string title, DataEntryType entryType = DataEntryType.Text)
        {
            if (SelectedCategory == null || string.IsNullOrWhiteSpace(title)) return;

            var entry = new DataBankEntry
            {
                Title = title,
                EntryType = entryType,
                CategoryId = SelectedCategory.Id
            };

            _allEntries.Add(entry);
            CurrentCategoryEntries.Add(entry);
            SelectedCategory.EntryCount++;
            SelectedDataEntry = entry;
            NewEntryTitle = string.Empty;
            IsAddingNewEntry = false;

            await SaveDataBanksAsync();
        }

        public async Task ImportFileAsync(string filePath)
        {
            if (SelectedCategory == null || string.IsNullOrEmpty(filePath)) return;

            var entryType = DataBankService.DetermineEntryType(filePath);
            var importedPath = await DataBankService.ImportFileAsync(filePath, entryType);
            var content = await DataBankService.ReadFileContentAsync(importedPath);
            var fileSize = DataBankService.GetFileSize(importedPath);

            var entry = new DataBankEntry
            {
                Title = System.IO.Path.GetFileNameWithoutExtension(filePath),
                Content = content,
                EntryType = entryType,
                FilePath = importedPath,
                OriginalFileName = System.IO.Path.GetFileName(filePath),
                FileSize = fileSize,
                CategoryId = SelectedCategory.Id
            };

            // Refresh preview to load image if it's an image entry
            entry.RefreshPreview();

            _allEntries.Add(entry);
            CurrentCategoryEntries.Add(entry);
            SelectedCategory.EntryCount++;
            SelectedDataEntry = entry;

            await SaveDataBanksAsync();
        }

        public async Task DeleteEntryAsync(DataBankEntry entry)
        {
            if (entry == null) return;

            // Delete associated file if exists
            DataBankService.DeleteFile(entry.FilePath);

            _allEntries.Remove(entry);
            CurrentCategoryEntries.Remove(entry);

            // Update category entry count
            var category = DataBankCategories.FirstOrDefault(c => c.Id == entry.CategoryId);
            if (category != null)
            {
                category.EntryCount--;
            }

            if (SelectedDataEntry == entry)
            {
                SelectedDataEntry = null;
            }

            await SaveDataBanksAsync();
        }

        public async Task UpdateEntryAsync()
        {
            await SaveDataBanksAsync();
        }

        #endregion

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
