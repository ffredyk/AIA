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
using AIA.Services.Automation;
using AIA.Plugins.Host.Services;
using AIA.Plugins.SDK;

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

        // AI Orchestration
        private bool _isAiProcessing;
        private string _aiStatusMessage = string.Empty;
        private AIProvider? _selectedAiProvider;

        // Chat management fields
        private bool _isRenamingChat;
        private string _renameChatTitle = string.Empty;

        // Chat message templates
        private ObservableCollection<ChatMessageTemplate> _chatMessageTemplates = new ObservableCollection<ChatMessageTemplate>();

        // Screen capture service
        private readonly ScreenCaptureService _screenCaptureService = new();

        // Reminder notification service
        private readonly ReminderNotificationService _reminderNotificationService = new();

        // AI Orchestration service
        private AIOrchestrationService? _aiOrchestrationService;

        // Automation service
        private AutomationService? _automationService;

        // Plugin UI service
        private HostPluginUIService? _pluginUIService;

        // Clipboard history service
        private ClipboardHistoryService? _clipboardHistoryService;
        private AppSettings? _appSettings;

        public event PropertyChangedEventHandler? PropertyChanged;

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
        /// Automation service instance
        /// </summary>
        public AutomationService AutomationService => _automationService ??= new AutomationService(() => this);

        /// <summary>
        /// Plugin UI service for accessing plugin tabs and toolbar buttons
        /// </summary>
        public HostPluginUIService? PluginUIService => _pluginUIService;

        /// <summary>
        /// Application settings
        /// </summary>
        public AppSettings AppSettings => _appSettings ??= new AppSettings();

        /// <summary>
        /// Clipboard history collection
        /// </summary>
        public ObservableCollection<DataAsset> ClipboardHistory => 
            _clipboardHistoryService?.ClipboardHistory ?? new ObservableCollection<DataAsset>();

        /// <summary>
        /// Collection of plugin tabs for UI binding
        /// </summary>
        public ObservableCollection<PluginTabDefinition>? PluginTabs => _pluginUIService?.Tabs;

        /// <summary>
        /// Collection of plugin toolbar buttons for UI binding
        /// </summary>
        public ObservableCollection<PluginToolbarButton>? PluginToolbarButtons => _pluginUIService?.ToolbarButtons;

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

        public ObservableCollection<ChatSession> ActiveChats { get; set; } = new ObservableCollection<ChatSession>();

        public ObservableCollection<TaskItem> Tasks { get; set; } = new ObservableCollection<TaskItem>();

        public ObservableCollection<ReminderItem> Reminders { get; set; } = new ObservableCollection<ReminderItem>();

        // Chat message templates collection
        public ObservableCollection<ChatMessageTemplate> ChatMessageTemplates
        {
            get => _chatMessageTemplates;
            set
            {
                if (_chatMessageTemplates != value)
                {
                    _chatMessageTemplates = value;
                    OnPropertyChanged(nameof(ChatMessageTemplates));
                }
            }
        }

        // Data Bank collections
        public ObservableCollection<DataBankCategory> DataBankCategories { get; set; } = new ObservableCollection<DataBankCategory>();
        public ObservableCollection<DataBankEntry> CurrentCategoryEntries { get; set; } = new ObservableCollection<DataBankEntry>();
        private List<DataBankEntry> _allEntries = new List<DataBankEntry>();

        // Current data assets collection
        public ObservableCollection<DataAsset> CurrentDataAssets { get; set; } = new ObservableCollection<DataAsset>();

        // ...existing task properties...
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

        public ChatSession SelectedChatSession
        {
            get => _selectedChatSession;
            set
            {
                if (_selectedChatSession != value)
                {
                    _selectedChatSession = value;
                    OnPropertyChanged(nameof(SelectedChatSession));
                    OnPropertyChanged(nameof(HasSelectedChatSession));
                }
            }
        }

        public bool HasSelectedChatSession => SelectedChatSession != null;

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

        // Chat management properties
        public bool IsRenamingChat
        {
            get => _isRenamingChat;
            set
            {
                if (_isRenamingChat != value)
                {
                    _isRenamingChat = value;
                    OnPropertyChanged(nameof(IsRenamingChat));
                }
            }
        }

        public string RenameChatTitle
        {
            get => _renameChatTitle;
            set
            {
                if (_renameChatTitle != value)
                {
                    _renameChatTitle = value;
                    OnPropertyChanged(nameof(RenameChatTitle));
                }
            }
        }

        public Array TaskStatusValues => Enum.GetValues(typeof(TaskStatus));
        public Array TaskPriorityValues => Enum.GetValues(typeof(TaskPriority));
        public Array ReminderSeverityValues => Enum.GetValues(typeof(ReminderSeverity));
        public Array DataEntryTypeValues => Enum.GetValues(typeof(DataEntryType));

        public OverlayViewModel()
        {
            // Load chat sessions from disk (or create empty one if first run)
            _ = LoadChatsAsync();

            // Load tasks and reminders from disk (or initialize with samples if first run)
            _ = LoadTasksAndRemindersAsync();

            // Load chat message templates
            _ = LoadChatTemplatesAsync();

            // Start timer to refresh reminder time displays
            StartReminderRefreshTimer();

            // Start window tracking timer
            StartWindowTrackingTimer();

            // Initialize reminder notification service
            InitializeReminderNotificationService();

            // Load data banks
            _ = LoadDataBanksAsync();

            // Initialize clipboard history service
            _ = InitializeClipboardHistoryAsync();
        }

        /// <summary>
        /// Initializes the clipboard history service
        /// </summary>
        private async Task InitializeClipboardHistoryAsync()
        {
            _appSettings = await AppSettingsService.LoadAppSettingsAsync();
            _clipboardHistoryService = new ClipboardHistoryService(() => _appSettings);
            
            OnPropertyChanged(nameof(ClipboardHistory));
            OnPropertyChanged(nameof(AppSettings));
            
            // Start clipboard monitoring (will be paused when overlay is visible)
            _clipboardHistoryService.Start();
        }

        /// <summary>
        /// Sets the plugin UI service for plugin integration
        /// </summary>
        public void SetPluginUIService(HostPluginUIService pluginUIService)
        {
            _pluginUIService = pluginUIService;
            
            // Wire up toast events
            _pluginUIService.ToastRequested += OnPluginToastRequested;
            
            OnPropertyChanged(nameof(PluginUIService));
            OnPropertyChanged(nameof(PluginTabs));
            OnPropertyChanged(nameof(PluginToolbarButtons));
        }

        private void OnPluginToastRequested(object? sender, ToastEventArgs e)
        {
            // Forward to main window via event
            // The MainWindow will handle this
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
            _clipboardHistoryService?.Pause();
        }

        /// <summary>
        /// Resumes window tracking (call when overlay is hidden)
        /// </summary>
        public void ResumeWindowTracking()
        {
            _windowTrackingTimer?.Start();
            _clipboardHistoryService?.Resume();
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

        #region Chat Session Management

        private async Task LoadChatsAsync()
        {
            var chats = await ChatSessionService.LoadChatsAsync();

            ActiveChats.Clear();
            foreach (var chat in chats)
            {
                ActiveChats.Add(chat);
            }

            // If no chats exist, create an empty one
            if (ActiveChats.Count == 0)
            {
                CreateNewChatSession();
            }
            else
            {
                SelectedChatSession = ActiveChats[0];
            }
        }

        /// <summary>
        /// Saves all chat sessions to disk
        /// </summary>
        public async Task SaveChatsAsync()
        {
            await ChatSessionService.SaveChatsAsync(ActiveChats);
        }

        /// <summary>
        /// Creates a new chat session and selects it
        /// </summary>
        public ChatSession CreateNewChatSession(string? title = null)
        {
            var chatNumber = ActiveChats.Count + 1;
            var chatSession = new ChatSession
            {
                ChatTitle = title ?? $"Chat {chatNumber}",
                ChatSummary = "New conversation",
                IsRenaming = true // Start in rename mode
            };

            ActiveChats.Add(chatSession);
            SelectedChatSession = chatSession;

            _ = SaveChatsAsync();

            return chatSession;
        }

        /// <summary>
        /// Deletes the specified chat session
        /// </summary>
        public void DeleteChatSession(ChatSession chatSession)
        {
            if (chatSession == null) return;

            var index = ActiveChats.IndexOf(chatSession);
            ActiveChats.Remove(chatSession);

            // Select another chat if the deleted one was selected
            if (SelectedChatSession == chatSession || SelectedChatSession == null)
            {
                if (ActiveChats.Count > 0)
                {
                    // Select the previous chat, or the first one if we deleted the first
                    var newIndex = Math.Max(0, index - 1);
                    SelectedChatSession = ActiveChats[Math.Min(newIndex, ActiveChats.Count - 1)];
                }
                else
                {
                    // No chats left, create a new one
                    CreateNewChatSession();
                    return; // CreateNewChatSession already saves
                }
            }

            _ = SaveChatsAsync();
        }

        /// <summary>
        /// Deletes the currently selected chat session
        /// </summary>
        public void DeleteSelectedChatSession()
        {
            if (SelectedChatSession != null)
            {
                DeleteChatSession(SelectedChatSession);
            }
        }

        /// <summary>
        /// Renames the specified chat session
        /// </summary>
        public void RenameChatSession(ChatSession chatSession, string newTitle)
        {
            if (chatSession == null || string.IsNullOrWhiteSpace(newTitle)) return;
            
            chatSession.ChatTitle = newTitle.Trim();
            chatSession.IsRenaming = false;
        }

        /// <summary>
        /// Starts renaming the currently selected chat session
        /// </summary>
        public void StartRenamingSelectedChat()
        {
            if (SelectedChatSession == null) return;
            
            RenameChatTitle = SelectedChatSession.ChatTitle;
            IsRenamingChat = true;
        }

        /// <summary>
        /// Confirms the rename operation for the selected chat
        /// </summary>
        public void ConfirmRenameChatSession()
        {
            if (SelectedChatSession != null && !string.IsNullOrWhiteSpace(RenameChatTitle))
            {
                SelectedChatSession.ChatTitle = RenameChatTitle.Trim();
            }
            IsRenamingChat = false;
            RenameChatTitle = string.Empty;

            _ = SaveChatsAsync();
        }

        /// <summary>
        /// Cancels the rename operation
        /// </summary>
        public void CancelRenameChatSession()
        {
            IsRenamingChat = false;
            RenameChatTitle = string.Empty;
        }

        /// <summary>
        /// Clears all messages in the specified chat session
        /// </summary>
        public void ClearChatSession(ChatSession chatSession)
        {
            if (chatSession == null) return;

            chatSession.Messages.Clear();

            _ = SaveChatsAsync();
        }

        /// <summary>
        /// Clears all messages in the currently selected chat session
        /// </summary>
        public void ClearSelectedChatSession()
        {
            if (SelectedChatSession != null)
            {
                ClearChatSession(SelectedChatSession);
            }
        }

        /// <summary>
        /// Deletes all chat sessions and creates a new empty one
        /// </summary>
        public void DeleteAllChatSessions()
        {
            ActiveChats.Clear();
            CreateNewChatSession();
        }

        /// <summary>
        /// Deletes all chat sessions except the currently selected one
        /// </summary>
        public void DeleteAllChatSessionsExceptCurrent()
        {
            if (SelectedChatSession == null) return;

            var currentChat = SelectedChatSession;
            var chatsToRemove = ActiveChats.Where(c => c != currentChat).ToList();
            
            foreach (var chat in chatsToRemove)
            {
                ActiveChats.Remove(chat);
            }

            _ = SaveChatsAsync();
        }

        /// <summary>
        /// Generates a title for the chat using AI based on the first user message
        /// </summary>
        public async Task<bool> AutoNameChatSessionAsync(ChatSession? chatSession = null)
        {
            var session = chatSession ?? SelectedChatSession;
            if (session == null)
            {
                System.Diagnostics.Debug.WriteLine("AutoNameChatSessionAsync: No session provided");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"AutoNameChatSessionAsync: Session has {session.Messages.Count} messages");

            // Find first user message
            var firstUserMessage = session.Messages.FirstOrDefault(m => m.Role == "user");
            if (firstUserMessage == null || string.IsNullOrWhiteSpace(firstUserMessage.Content))
            {
                System.Diagnostics.Debug.WriteLine("AutoNameChatSessionAsync: No user message found");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"AutoNameChatSessionAsync: Found first user message, calling GenerateChatTitleAsync");

            try
            {
                var title = await AIOrchestration.GenerateChatTitleAsync(firstUserMessage.Content);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    System.Diagnostics.Debug.WriteLine($"AutoNameChatSessionAsync: Setting chat title to: {title}");
                    
                    // Update the title directly - ChatSession.ChatTitle already has PropertyChanged notification
                    session.ChatTitle = title;
                    System.Diagnostics.Debug.WriteLine($"AutoNameChatSessionAsync: Chat title updated");
                    
                    await SaveChatsAsync();
                    System.Diagnostics.Debug.WriteLine("AutoNameChatSessionAsync: Chats saved");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("AutoNameChatSessionAsync: GenerateChatTitleAsync returned null or empty");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AutoNameChatSessionAsync: Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"AutoNameChatSessionAsync: Exception StackTrace: {ex.StackTrace}");
                // Silently fail - will keep existing name
            }

            return false;
        }

        #endregion

        #region Chat Message Templates

        private async Task LoadChatTemplatesAsync()
        {
            var templates = await ChatTemplateService.LoadTemplatesAsync();

            ChatMessageTemplates.Clear();
            foreach (var template in templates.Where(t => t.IsEnabled).OrderBy(t => t.Order))
            {
                ChatMessageTemplates.Add(template);
            }
        }

        /// <summary>
        /// Saves all chat message templates to disk
        /// </summary>
        public async Task SaveChatTemplatesAsync()
        {
            // Include both enabled and disabled templates when saving
            var allTemplates = ChatMessageTemplates.ToList();
            await ChatTemplateService.SaveTemplatesAsync(allTemplates);
        }

        /// <summary>
        /// Adds a new chat message template
        /// </summary>
        public void AddChatTemplate(ChatMessageTemplate template)
        {
            if (template == null) return;

            // Set order to be last
            template.Order = ChatMessageTemplates.Count > 0 
                ? ChatMessageTemplates.Max(t => t.Order) + 1 
                : 0;

            ChatMessageTemplates.Add(template);
            _ = SaveChatTemplatesAsync();
        }

        /// <summary>
        /// Updates an existing chat message template
        /// </summary>
        public void UpdateChatTemplate(ChatMessageTemplate template)
        {
            _ = SaveChatTemplatesAsync();
        }

        /// <summary>
        /// Deletes a chat message template
        /// </summary>
        public void DeleteChatTemplate(ChatMessageTemplate template)
        {
            if (template == null) return;

            ChatMessageTemplates.Remove(template);
            _ = SaveChatTemplatesAsync();
        }

        /// <summary>
        /// Reorders chat templates
        /// </summary>
        public void ReorderChatTemplates(ChatMessageTemplate template, int newIndex)
        {
            if (template == null || newIndex < 0 || newIndex >= ChatMessageTemplates.Count)
                return;

            var oldIndex = ChatMessageTemplates.IndexOf(template);
            if (oldIndex == newIndex) return;

            ChatMessageTemplates.Move(oldIndex, newIndex);

            // Update order values
            for (int i = 0; i < ChatMessageTemplates.Count; i++)
            {
                ChatMessageTemplates[i].Order = i;
            }

            _ = SaveChatTemplatesAsync();
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

        #region Tasks and Reminders

        private TaskFilter _currentTaskFilter = TaskFilter.CreateDefault();
        private bool _isBulkSelectionMode;
        private ObservableCollection<TaskTemplate> _taskTemplates = new ObservableCollection<TaskTemplate>();

        public TaskFilter CurrentTaskFilter
        {
            get => _currentTaskFilter;
            set
            {
                _currentTaskFilter = value;
                OnPropertyChanged(nameof(CurrentTaskFilter));
                ApplyTaskFilter();
            }
        }

        public bool IsBulkSelectionMode
        {
            get => _isBulkSelectionMode;
            set
            {
                _isBulkSelectionMode = value;
                OnPropertyChanged(nameof(IsBulkSelectionMode));
                OnPropertyChanged(nameof(SelectedTasksCount));
                if (!value)
                {
                    // Clear all selections when exiting bulk mode
                    foreach (var task in Tasks)
                    {
                        task.IsSelected = false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the count of currently selected tasks
        /// </summary>
        public int SelectedTasksCount => Tasks.Count(t => t.IsSelected);

        public ObservableCollection<TaskTemplate> TaskTemplates
        {
            get => _taskTemplates;
            set
            {
                _taskTemplates = value;
                OnPropertyChanged(nameof(TaskTemplates));
            }
        }

        public ObservableCollection<TaskItem> FilteredTasks { get; set; } = new ObservableCollection<TaskItem>();

        private async Task LoadTasksAndRemindersAsync()
        {
            var tasks = await TaskReminderService.LoadTasksAsync();
            var reminders = await TaskReminderService.LoadRemindersAsync();

            // Load task templates
            var templates = await TaskTemplateService.LoadTemplatesAsync();
            foreach (var template in templates)
            {
                TaskTemplates.Add(template);
            }

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
                
                // Process recurring tasks
                await ProcessRecurringTasksAsync();
            }

            ApplyTaskFilter();
        }

        /// <summary>
        /// Saves all tasks and reminders to disk
        /// </summary>
        public async Task SaveTasksAndRemindersAsync()
        {
            await TaskReminderService.SaveTasksAsync(Tasks);
            await TaskReminderService.SaveRemindersAsync(Reminders);
        }

        /// <summary>
        /// Saves task templates to disk
        /// </summary>
        public async Task SaveTaskTemplatesAsync()
        {
            await TaskTemplateService.SaveTemplatesAsync(TaskTemplates.ToList());
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
            
            ApplyTaskFilter();
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
            
            ApplyTaskFilter();
            _ = SaveTasksAndRemindersAsync();
        }

        /// <summary>
        /// Duplicates a task (creates a copy with new ID)
        /// </summary>
        public TaskItem? DuplicateTask(TaskItem task, bool resetStatus = true, bool includeSubtasks = true)
        {
            if (task == null) return null;

            var duplicate = task.Clone(resetStatus, includeSubtasks);
            
            if (task.ParentTaskId.HasValue)
            {
                // If it's a subtask, add to the same parent
                var parent = FindTaskById(task.ParentTaskId.Value);
                parent?.AddSubtask(duplicate);
            }
            else
            {
                // Add to main tasks list
                Tasks.Add(duplicate);
                SelectedTask = duplicate;
            }

            ApplyTaskFilter();
            _ = SaveTasksAndRemindersAsync();
            
            return duplicate;
        }

        /// <summary>
        /// Archives a task (hides from normal view but keeps in storage)
        /// </summary>
        public void ArchiveTask(TaskItem task)
        {
            if (task == null) return;

            task.IsArchived = true;
            if (SelectedTask == task)
            {
                SelectedTask = null;
            }
            
            ApplyTaskFilter();
            _ = SaveTasksAndRemindersAsync();
        }

        /// <summary>
        /// Unarchives a task
        /// </summary>
        public void UnarchiveTask(TaskItem task)
        {
            if (task == null) return;

            task.IsArchived = false;
            ApplyTaskFilter();
            _ = SaveTasksAndRemindersAsync();
        }

        /// <summary>
        /// Creates a task from a template
        /// </summary>
        public TaskItem? CreateTaskFromTemplate(TaskTemplate template)
        {
            if (template == null) return null;

            var task = template.CreateTask();
            Tasks.Add(task);
            SelectedTask = task;

            ApplyTaskFilter();
            _ = SaveTasksAndRemindersAsync();
            _ = SaveTaskTemplatesAsync(); // Save updated usage stats

            return task;
        }

        /// <summary>
        /// Creates a template from an existing task
        /// </summary>
        public async Task<TaskTemplate?> SaveTaskAsTemplateAsync(TaskItem task, string templateName, string templateDescription = "")
        {
            if (task == null || string.IsNullOrWhiteSpace(templateName)) return null;

            var template = await TaskTemplateService.CreateTemplateFromTaskAsync(task, templateName, templateDescription);
            TaskTemplates.Add(template);

            return template;
        }

        /// <summary>
        /// Deletes a task template
        /// </summary>
        public async Task<bool> DeleteTaskTemplateAsync(TaskTemplate template)
        {
            if (template == null) return false;

            var result = await TaskTemplateService.DeleteTemplateAsync(template.Id);
            if (result)
            {
                TaskTemplates.Remove(template);
            }
            return result;
        }

        /// <summary>
        /// Applies the current filter to the tasks list
        /// </summary>
        public void ApplyTaskFilter()
        {
            FilteredTasks.Clear();
            
            var filtered = CurrentTaskFilter.Apply(Tasks);
            foreach (var task in filtered)
            {
                FilteredTasks.Add(task);
            }
        }

        /// <summary>
        /// Adds a tag to a task
        /// </summary>
        public void AddTagToTask(TaskItem task, string tag)
        {
            if (task == null || string.IsNullOrWhiteSpace(tag)) return;

            tag = tag.Trim();
            if (!task.Tags.Contains(tag))
            {
                task.Tags.Add(tag);
                _ = SaveTasksAndRemindersAsync();
            }
        }

        /// <summary>
        /// Removes a tag from a task
        /// </summary>
        public void RemoveTagFromTask(TaskItem task, string tag)
        {
            if (task == null || string.IsNullOrWhiteSpace(tag)) return;

            task.Tags.Remove(tag);
            _ = SaveTasksAndRemindersAsync();
        }

        /// <summary>
        /// Gets all unique tags across all tasks
        /// </summary>
        public List<string> GetAllTags()
        {
            var tags = new HashSet<string>();
            foreach (var task in Tasks)
            {
                foreach (var tag in task.Tags)
                {
                    tags.Add(tag);
                }
            }
            return tags.OrderBy(t => t).ToList();
        }

        /// <summary>
        /// Adds a dependency between tasks
        /// </summary>
        public void AddTaskDependency(TaskItem task, TaskItem dependsOn)
        {
            if (task == null || dependsOn == null) return;
            if (task.Id == dependsOn.Id) return; // Can't depend on itself

            if (!task.DependencyTaskIds.Contains(dependsOn.Id))
            {
                task.DependencyTaskIds.Add(dependsOn.Id);
                _ = SaveTasksAndRemindersAsync();
            }
        }

        /// <summary>
        /// Removes a dependency between tasks
        /// </summary>
        public void RemoveTaskDependency(TaskItem task, Guid dependencyId)
        {
            if (task == null) return;

            task.DependencyTaskIds.Remove(dependencyId);
            _ = SaveTasksAndRemindersAsync();
        }

        /// <summary>
        /// Checks if a task can be started based on dependencies
        /// </summary>
        public bool CanStartTask(TaskItem task)
        {
            if (task == null || task.DependencyTaskIds.Count == 0) return true;

            foreach (var depId in task.DependencyTaskIds)
            {
                var depTask = FindTaskById(depId);
                if (depTask == null || !depTask.IsCompleted)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Gets tasks that depend on the specified task
        /// </summary>
        public List<TaskItem> GetDependentTasks(TaskItem task)
        {
            if (task == null) return new List<TaskItem>();

            return Tasks.Where(t => t.DependencyTaskIds.Contains(task.Id)).ToList();
        }

        /// <summary>
        /// Processes recurring tasks and creates new instances if needed
        /// </summary>
        public async Task ProcessRecurringTasksAsync()
        {
            var recurringTasks = Tasks.Where(t => t.IsRecurring && t.IsCompleted && !t.IsArchived).ToList();

            foreach (var task in recurringTasks)
            {
                if (ShouldCreateNextRecurrence(task))
                {
                    var nextTask = CreateNextRecurrence(task);
                    if (nextTask != null)
                    {
                        Tasks.Add(nextTask);
                        // Archive the completed recurring task
                        task.IsArchived = true;
                    }
                }
            }

            if (recurringTasks.Count > 0)
            {
                ApplyTaskFilter();
                await SaveTasksAndRemindersAsync();
            }
        }

        private bool ShouldCreateNextRecurrence(TaskItem task)
        {
            if (!task.IsRecurring || !task.IsCompleted) return false;
            if (task.RecurrenceEndDate.HasValue && DateTime.Now > task.RecurrenceEndDate.Value) return false;

            // Check if next instance already exists
            var nextDueDate = CalculateNextDueDate(task);
            if (!nextDueDate.HasValue) return false;

            // Check if we already have a task with similar title and due date
            var similar = Tasks.Any(t => 
                t.Title == task.Title && 
                t.DueDate.HasValue && 
                Math.Abs((t.DueDate.Value - nextDueDate.Value).TotalDays) < 1);

            return !similar;
        }

        private TaskItem? CreateNextRecurrence(TaskItem task)
        {
            var nextDueDate = CalculateNextDueDate(task);
            if (!nextDueDate.HasValue) return null;

            var nextTask = task.Clone(resetStatus: true, includeSubtasks: true);
            nextTask.DueDate = nextDueDate;

            return nextTask;
        }

        private DateTime? CalculateNextDueDate(TaskItem task)
        {
            if (!task.DueDate.HasValue) return null;

            return task.RecurrenceType switch
            {
                RecurrenceType.Daily => task.DueDate.Value.AddDays(task.RecurrenceInterval),
                RecurrenceType.Weekly => task.DueDate.Value.AddDays(7 * task.RecurrenceInterval),
                RecurrenceType.Monthly => task.DueDate.Value.AddMonths(task.RecurrenceInterval),
                RecurrenceType.Yearly => task.DueDate.Value.AddYears(task.RecurrenceInterval),
                _ => null
            };
        }

        #region Bulk Operations

        /// <summary>
        /// Gets all selected tasks
        /// </summary>
        public List<TaskItem> GetSelectedTasks()
        {
            return Tasks.Where(t => t.IsSelected).ToList();
        }

        /// <summary>
        /// Selects all tasks
        /// </summary>
        public void SelectAllTasks()
        {
            foreach (var task in FilteredTasks)
            {
                task.IsSelected = true;
            }
        }

        /// <summary>
        /// Deselects all tasks
        /// </summary>
        public void DeselectAllTasks()
        {
            foreach (var task in Tasks)
            {
                task.IsSelected = false;
            }
        }

        /// <summary>
        /// Changes status for all selected tasks
        /// </summary>
        public void BulkChangeStatus(TaskStatus status)
        {
            var selectedTasks = GetSelectedTasks();
            foreach (var task in selectedTasks)
            {
                task.Status = status;
            }
            
            if (selectedTasks.Count > 0)
            {
                _ = SaveTasksAndRemindersAsync();
            }
        }

        /// <summary>
        /// Changes priority for all selected tasks
        /// </summary>
        public void BulkChangePriority(TaskPriority priority)
        {
            var selectedTasks = GetSelectedTasks();
            foreach (var task in selectedTasks)
            {
                task.Priority = priority;
            }
            
            if (selectedTasks.Count > 0)
            {
                _ = SaveTasksAndRemindersAsync();
            }
        }

        /// <summary>
        /// Archives all selected tasks
        /// </summary>
        public void BulkArchiveTasks()
        {
            var selectedTasks = GetSelectedTasks();
            foreach (var task in selectedTasks)
            {
                task.IsArchived = true;
                task.IsSelected = false;
            }
            
            if (selectedTasks.Count > 0)
            {
                ApplyTaskFilter();
                _ = SaveTasksAndRemindersAsync();
            }
        }

        /// <summary>
        /// Deletes all selected tasks
        /// </summary>
        public void BulkDeleteTasks()
        {
            var selectedTasks = GetSelectedTasks().ToList();
            foreach (var task in selectedTasks)
            {
                if (!task.ParentTaskId.HasValue) // Only delete top-level tasks
                {
                    Tasks.Remove(task);
                }
            }
            
            if (selectedTasks.Count > 0)
            {
                ApplyTaskFilter();
                _ = SaveTasksAndRemindersAsync();
            }
        }

        /// <summary>
        /// Adds a tag to all selected tasks
        /// </summary>
        public void BulkAddTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;

            var selectedTasks = GetSelectedTasks();
            foreach (var task in selectedTasks)
            {
                AddTagToTask(task, tag);
            }
        }

        #endregion

        #region Import/Export

        /// <summary>
        /// Exports tasks to JSON file
        /// </summary>
        public async Task<bool> ExportTasksAsync(string filePath, bool includeArchived = false)
        {
            try
            {
                var tasksToExport = includeArchived ? Tasks.ToList() : Tasks.Where(t => !t.IsArchived).ToList();
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var json = System.Text.Json.JsonSerializer.Serialize(tasksToExport, options);
                await System.IO.File.WriteAllTextAsync(filePath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Imports tasks from JSON file
        /// </summary>
        public async Task<int> ImportTasksAsync(string filePath, bool replaceExisting = false)
        {
            try
            {
                if (!System.IO.File.Exists(filePath)) return 0;

                var json = await System.IO.File.ReadAllTextAsync(filePath);
                var importedTasks = System.Text.Json.JsonSerializer.Deserialize<List<TaskItem>>(json);
                
                if (importedTasks == null) return 0;

                if (replaceExisting)
                {
                    Tasks.Clear();
                }

                foreach (var task in importedTasks)
                {
                    // Generate new IDs to avoid conflicts
                    task.Id = Guid.NewGuid();
                    foreach (var subtask in task.Subtasks)
                    {
                        subtask.Id = Guid.NewGuid();
                    }
                    Tasks.Add(task);
                }

                ApplyTaskFilter();
                await SaveTasksAndRemindersAsync();
                
                return importedTasks.Count;
            }
            catch
            {
                return 0;
            }
        }

        #endregion

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

        #endregion

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

        #region Clipboard History Methods

        /// <summary>
        /// Restores a clipboard history item to the system clipboard
        /// </summary>
        public bool RestoreClipboardItem(DataAsset item)
        {
            return _clipboardHistoryService?.RestoreToClipboard(item) ?? false;
        }

        /// <summary>
        /// Clears all clipboard history
        /// </summary>
        public void ClearClipboardHistory()
        {
            _clipboardHistoryService?.ClearHistory();
        }

        /// <summary>
        /// Saves app settings
        /// </summary>
        public async Task SaveAppSettingsAsync()
        {
            if (_appSettings != null)
            {
                await AppSettingsService.SaveAppSettingsAsync(_appSettings);
            }
        }

        #endregion

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
