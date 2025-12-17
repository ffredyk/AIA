using System;
using System.Net.Http;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Host
{
    /// <summary>
    /// Plugin context implementation that provides access to host services
    /// </summary>
    public class PluginContext : IPluginContext
    {
        private readonly PluginInfo _pluginInfo;
        private readonly IPluginHostServices _hostServices;
        private readonly PluginServiceRegistry _serviceRegistry;
        private readonly PluginSettingsStorage _settingsStorage;

        public ITaskService Tasks { get; }
        public IReminderService Reminders { get; }
        public IDataBankService DataBanks { get; }
        public IDataAssetService DataAssets { get; }
        public IChatService Chats { get; }
        public IPluginUIService UI { get; }
        public IPluginServiceRegistry Services => _serviceRegistry;
        public IHttpClientFactory HttpClientFactory { get; }
        public IPluginLogger Logger { get; }
        public IPluginSettingsStorage Settings => _settingsStorage;
        public PluginPermissions GrantedPermissions => _pluginInfo.GrantedPermissions;
        public string PluginDataDirectory { get; }

        public PluginContext(
            PluginInfo pluginInfo,
            IPluginHostServices hostServices,
            PluginServiceRegistry serviceRegistry,
            IPluginLogger logger,
            string pluginDataDirectory)
        {
            _pluginInfo = pluginInfo ?? throw new ArgumentNullException(nameof(pluginInfo));
            _hostServices = hostServices ?? throw new ArgumentNullException(nameof(hostServices));
            _serviceRegistry = serviceRegistry ?? throw new ArgumentNullException(nameof(serviceRegistry));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            PluginDataDirectory = pluginDataDirectory ?? throw new ArgumentNullException(nameof(pluginDataDirectory));

            // Create permission-guarded services
            Tasks = new GuardedTaskService(_hostServices.Tasks, this);
            Reminders = new GuardedReminderService(_hostServices.Reminders, this);
            DataBanks = new GuardedDataBankService(_hostServices.DataBanks, this);
            DataAssets = new GuardedDataAssetService(_hostServices.DataAssets, this);
            Chats = new GuardedChatService(_hostServices.Chats, this);
            UI = _hostServices.UI;
            HttpClientFactory = new GuardedHttpClientFactory(this);
            
            _settingsStorage = new PluginSettingsStorage(pluginDataDirectory, pluginInfo.Id);
        }

        public bool HasPermission(PluginPermissions permission)
        {
            return (GrantedPermissions & permission) == permission;
        }
    }

    /// <summary>
    /// HTTP client factory with permission guard
    /// </summary>
    internal class GuardedHttpClientFactory : IHttpClientFactory
    {
        private readonly PluginContext _context;
        private static readonly HttpClient _sharedClient = new();

        public GuardedHttpClientFactory(PluginContext context)
        {
            _context = context;
        }

        public HttpClient CreateClient(string? name = null)
        {
            if (!_context.HasPermission(PluginPermissions.Network))
            {
                throw new PluginPermissionException(_context.PluginDataDirectory, PluginPermissions.Network);
            }

            // In a real implementation, you might want to return named clients
            // For simplicity, we return a shared client
            return _sharedClient;
        }
    }

    /// <summary>
    /// Task service with permission guards
    /// </summary>
    internal class GuardedTaskService : ITaskService
    {
        private readonly ITaskService _inner;
        private readonly PluginContext _context;

        public event EventHandler<TasksChangedEventArgs>? TasksChanged
        {
            add => _inner.TasksChanged += value;
            remove => _inner.TasksChanged -= value;
        }

        public GuardedTaskService(ITaskService inner, PluginContext context)
        {
            _inner = inner;
            _context = context;
        }

        public IReadOnlyList<ITaskItem> GetAllTasks()
        {
            RequirePermission(PluginPermissions.ReadTasks);
            return _inner.GetAllTasks();
        }

        public ITaskItem? GetTaskById(Guid id)
        {
            RequirePermission(PluginPermissions.ReadTasks);
            return _inner.GetTaskById(id);
        }

        public ITaskItem CreateTask(string title, string? description = null)
        {
            RequirePermission(PluginPermissions.WriteTasks);
            return _inner.CreateTask(title, description);
        }

        public bool DeleteTask(Guid id)
        {
            RequirePermission(PluginPermissions.WriteTasks);
            return _inner.DeleteTask(id);
        }

        public Task SaveAsync()
        {
            RequirePermission(PluginPermissions.WriteTasks);
            return _inner.SaveAsync();
        }

        private void RequirePermission(PluginPermissions permission)
        {
            if (!_context.HasPermission(permission))
            {
                throw new PluginPermissionException(_context.PluginDataDirectory, permission);
            }
        }
    }

    /// <summary>
    /// Reminder service with permission guards
    /// </summary>
    internal class GuardedReminderService : IReminderService
    {
        private readonly IReminderService _inner;
        private readonly PluginContext _context;

        public event EventHandler<RemindersChangedEventArgs>? RemindersChanged
        {
            add => _inner.RemindersChanged += value;
            remove => _inner.RemindersChanged -= value;
        }

        public GuardedReminderService(IReminderService inner, PluginContext context)
        {
            _inner = inner;
            _context = context;
        }

        public IReadOnlyList<IReminderItem> GetAllReminders()
        {
            RequirePermission(PluginPermissions.ReadReminders);
            return _inner.GetAllReminders();
        }

        public IReminderItem? GetReminderById(Guid id)
        {
            RequirePermission(PluginPermissions.ReadReminders);
            return _inner.GetReminderById(id);
        }

        public IReminderItem CreateReminder(string title, DateTime dueDate, ReminderItemSeverity severity = ReminderItemSeverity.Medium)
        {
            RequirePermission(PluginPermissions.WriteReminders);
            return _inner.CreateReminder(title, dueDate, severity);
        }

        public bool DeleteReminder(Guid id)
        {
            RequirePermission(PluginPermissions.WriteReminders);
            return _inner.DeleteReminder(id);
        }

        public void SnoozeReminder(Guid id, int minutes = 15)
        {
            RequirePermission(PluginPermissions.WriteReminders);
            _inner.SnoozeReminder(id, minutes);
        }

        public void ToggleComplete(Guid id)
        {
            RequirePermission(PluginPermissions.WriteReminders);
            _inner.ToggleComplete(id);
        }

        public Task SaveAsync()
        {
            RequirePermission(PluginPermissions.WriteReminders);
            return _inner.SaveAsync();
        }

        private void RequirePermission(PluginPermissions permission)
        {
            if (!_context.HasPermission(permission))
            {
                throw new PluginPermissionException(_context.PluginDataDirectory, permission);
            }
        }
    }

    /// <summary>
    /// Data bank service with permission guards
    /// </summary>
    internal class GuardedDataBankService : IDataBankService
    {
        private readonly IDataBankService _inner;
        private readonly PluginContext _context;

        public event EventHandler<DataBankChangedEventArgs>? DataBankChanged
        {
            add => _inner.DataBankChanged += value;
            remove => _inner.DataBankChanged -= value;
        }

        public GuardedDataBankService(IDataBankService inner, PluginContext context)
        {
            _inner = inner;
            _context = context;
        }

        public IReadOnlyList<IDataBankCategory> GetAllCategories()
        {
            RequirePermission(PluginPermissions.ReadDataBanks);
            return _inner.GetAllCategories();
        }

        public IDataBankCategory? GetCategoryById(Guid id)
        {
            RequirePermission(PluginPermissions.ReadDataBanks);
            return _inner.GetCategoryById(id);
        }

        public IReadOnlyList<IDataBankEntry> GetEntriesByCategory(Guid categoryId)
        {
            RequirePermission(PluginPermissions.ReadDataBanks);
            return _inner.GetEntriesByCategory(categoryId);
        }

        public IDataBankEntry? GetEntryById(Guid id)
        {
            RequirePermission(PluginPermissions.ReadDataBanks);
            return _inner.GetEntryById(id);
        }

        public IDataBankCategory CreateCategory(string name, string? color = null)
        {
            RequirePermission(PluginPermissions.WriteDataBanks);
            return _inner.CreateCategory(name, color);
        }

        public IDataBankEntry CreateEntry(Guid categoryId, string title, DataBankEntryType entryType = DataBankEntryType.Text)
        {
            RequirePermission(PluginPermissions.WriteDataBanks);
            return _inner.CreateEntry(categoryId, title, entryType);
        }

        public Task<bool> DeleteCategoryAsync(Guid id)
        {
            RequirePermission(PluginPermissions.WriteDataBanks);
            return _inner.DeleteCategoryAsync(id);
        }

        public Task<bool> DeleteEntryAsync(Guid id)
        {
            RequirePermission(PluginPermissions.WriteDataBanks);
            return _inner.DeleteEntryAsync(id);
        }

        public Task<IDataBankEntry?> ImportFileAsync(Guid categoryId, string filePath)
        {
            RequirePermission(PluginPermissions.WriteDataBanks);
            RequirePermission(PluginPermissions.FileSystem);
            return _inner.ImportFileAsync(categoryId, filePath);
        }

        public Task SaveAsync()
        {
            RequirePermission(PluginPermissions.WriteDataBanks);
            return _inner.SaveAsync();
        }

        private void RequirePermission(PluginPermissions permission)
        {
            if (!_context.HasPermission(permission))
            {
                throw new PluginPermissionException(_context.PluginDataDirectory, permission);
            }
        }
    }

    /// <summary>
    /// Data asset service with permission guards
    /// </summary>
    internal class GuardedDataAssetService : IDataAssetService
    {
        private readonly IDataAssetService _inner;
        private readonly PluginContext _context;

        public event EventHandler<DataAssetsChangedEventArgs>? DataAssetsChanged
        {
            add => _inner.DataAssetsChanged += value;
            remove => _inner.DataAssetsChanged -= value;
        }

        public GuardedDataAssetService(IDataAssetService inner, PluginContext context)
        {
            _inner = inner;
            _context = context;
        }

        public IReadOnlyList<IDataAsset> GetCurrentAssets()
        {
            RequirePermission(PluginPermissions.ReadDataAssets);
            return _inner.GetCurrentAssets();
        }

        public void CaptureCurrentAssets()
        {
            RequirePermission(PluginPermissions.WriteDataAssets);
            _inner.CaptureCurrentAssets();
        }

        public bool CopyToClipboard(IDataAsset asset)
        {
            RequirePermission(PluginPermissions.ReadDataAssets);
            return _inner.CopyToClipboard(asset);
        }

        public string? SaveToFile(IDataAsset asset)
        {
            RequirePermission(PluginPermissions.ReadDataAssets);
            RequirePermission(PluginPermissions.FileSystem);
            return _inner.SaveToFile(asset);
        }

        public string? SaveToFileWithDialog(IDataAsset asset)
        {
            RequirePermission(PluginPermissions.ReadDataAssets);
            RequirePermission(PluginPermissions.FileSystem);
            return _inner.SaveToFileWithDialog(asset);
        }

        public Task<bool> SaveToDataBankAsync(IDataAsset asset, Guid categoryId)
        {
            RequirePermission(PluginPermissions.WriteDataAssets);
            RequirePermission(PluginPermissions.WriteDataBanks);
            return _inner.SaveToDataBankAsync(asset, categoryId);
        }

        private void RequirePermission(PluginPermissions permission)
        {
            if (!_context.HasPermission(permission))
            {
                throw new PluginPermissionException(_context.PluginDataDirectory, permission);
            }
        }
    }

    /// <summary>
    /// Chat service with permission guards
    /// </summary>
    internal class GuardedChatService : IChatService
    {
        private readonly IChatService _inner;
        private readonly PluginContext _context;

        public event EventHandler<ChatChangedEventArgs>? ChatChanged
        {
            add => _inner.ChatChanged += value;
            remove => _inner.ChatChanged -= value;
        }

        public GuardedChatService(IChatService inner, PluginContext context)
        {
            _inner = inner;
            _context = context;
        }

        public IReadOnlyList<IChatSession> GetAllSessions()
        {
            RequirePermission(PluginPermissions.ReadChats);
            return _inner.GetAllSessions();
        }

        public IChatSession? GetSelectedSession()
        {
            RequirePermission(PluginPermissions.ReadChats);
            return _inner.GetSelectedSession();
        }

        public IChatSession? GetSessionById(Guid id)
        {
            RequirePermission(PluginPermissions.ReadChats);
            return _inner.GetSessionById(id);
        }

        public IChatSession CreateSession(string? title = null)
        {
            RequirePermission(PluginPermissions.WriteChats);
            return _inner.CreateSession(title);
        }

        public bool DeleteSession(Guid id)
        {
            RequirePermission(PluginPermissions.WriteChats);
            return _inner.DeleteSession(id);
        }

        public IChatMessage AddMessage(Guid sessionId, string content, ChatMessageRole role)
        {
            RequirePermission(PluginPermissions.WriteChats);
            return _inner.AddMessage(sessionId, content, role);
        }

        public Task SaveAsync()
        {
            RequirePermission(PluginPermissions.WriteChats);
            return _inner.SaveAsync();
        }

        private void RequirePermission(PluginPermissions permission)
        {
            if (!_context.HasPermission(permission))
            {
                throw new PluginPermissionException(_context.PluginDataDirectory, permission);
            }
        }
    }
}
