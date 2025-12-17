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

        // Screen capture service
        private readonly ScreenCaptureService _screenCaptureService = new();

        // Reminder notification service
        private readonly ReminderNotificationService _reminderNotificationService = new();

        // AI Orchestration service
        private AIOrchestrationService? _aiOrchestrationService;

        // Plugin UI service
        private HostPluginUIService? _pluginUIService;

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
        /// Plugin UI service for accessing plugin tabs and toolbar buttons
        /// </summary>
        public HostPluginUIService? PluginUIService => _pluginUIService;

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

            // Start timer to refresh reminder time displays
            StartReminderRefreshTimer();

            // Start window tracking timer
            StartWindowTrackingTimer();

            // Initialize reminder notification service
            InitializeReminderNotificationService();

            // Load data banks
            _ = LoadDataBanksAsync();
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
            // Forward to main window via event or direct call
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
                ChatSummary = "New conversation"
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

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
