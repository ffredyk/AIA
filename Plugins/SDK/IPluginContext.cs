using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Context provided to plugins for accessing host application services
    /// </summary>
    public interface IPluginContext
    {
        /// <summary>
        /// Access to task management operations
        /// </summary>
        ITaskService Tasks { get; }

        /// <summary>
        /// Access to reminder management operations
        /// </summary>
        IReminderService Reminders { get; }

        /// <summary>
        /// Access to data bank operations
        /// </summary>
        IDataBankService DataBanks { get; }

        /// <summary>
        /// Access to data asset (screenshot) operations
        /// </summary>
        IDataAssetService DataAssets { get; }

        /// <summary>
        /// Access to chat session operations
        /// </summary>
        IChatService Chats { get; }

        /// <summary>
        /// UI integration service for adding tabs, toolbar buttons, etc.
        /// </summary>
        IPluginUIService UI { get; }

        /// <summary>
        /// Service for registering and consuming plugin services
        /// </summary>
        IPluginServiceRegistry Services { get; }

        /// <summary>
        /// HTTP client factory for network operations
        /// </summary>
        IHttpClientFactory HttpClientFactory { get; }

        /// <summary>
        /// Logger for the plugin
        /// </summary>
        IPluginLogger Logger { get; }

        /// <summary>
        /// Settings storage for the plugin
        /// </summary>
        IPluginSettingsStorage Settings { get; }

        /// <summary>
        /// Gets the permissions granted to this plugin
        /// </summary>
        PluginPermissions GrantedPermissions { get; }

        /// <summary>
        /// Checks if the plugin has a specific permission
        /// </summary>
        bool HasPermission(PluginPermissions permission);

        /// <summary>
        /// Gets the plugin's data directory for file storage
        /// </summary>
        string PluginDataDirectory { get; }
    }

    /// <summary>
    /// HTTP client factory interface
    /// </summary>
    public interface IHttpClientFactory
    {
        /// <summary>
        /// Creates a new HttpClient instance
        /// </summary>
        HttpClient CreateClient(string? name = null);
    }

    /// <summary>
    /// Plugin logger interface
    /// </summary>
    public interface IPluginLogger
    {
        void Debug(string message);
        void Info(string message);
        void Warning(string message);
        void Error(string message, Exception? exception = null);
    }

    /// <summary>
    /// Plugin settings storage interface
    /// </summary>
    public interface IPluginSettingsStorage
    {
        /// <summary>
        /// Gets a setting value
        /// </summary>
        T? Get<T>(string key, T? defaultValue = default);

        /// <summary>
        /// Sets a setting value
        /// </summary>
        void Set<T>(string key, T value);

        /// <summary>
        /// Saves all settings to disk
        /// </summary>
        Task SaveAsync();

        /// <summary>
        /// Loads settings from disk
        /// </summary>
        Task LoadAsync();
    }

    /// <summary>
    /// Service registry for inter-plugin communication
    /// </summary>
    public interface IPluginServiceRegistry
    {
        /// <summary>
        /// Registers a service that other plugins can consume
        /// </summary>
        void Register<TService>(string serviceName, TService service) where TService : class;

        /// <summary>
        /// Gets a registered service by name
        /// </summary>
        TService? Get<TService>(string serviceName) where TService : class;

        /// <summary>
        /// Checks if a service is registered
        /// </summary>
        bool IsRegistered(string serviceName);

        /// <summary>
        /// Gets all registered service names
        /// </summary>
        string[] GetRegisteredServices();
    }
}
