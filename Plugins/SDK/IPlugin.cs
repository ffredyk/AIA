using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Base interface for all AIA plugins. Plugins must implement this interface
    /// and be decorated with <see cref="PluginAttribute"/> to be discovered.
    /// </summary>
    public interface IPlugin : IDisposable
    {
        /// <summary>
        /// Unique identifier for the plugin
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Display name of the plugin
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of what the plugin does
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Version of the plugin
        /// </summary>
        Version Version { get; }

        /// <summary>
        /// Author of the plugin
        /// </summary>
        string Author { get; }

        /// <summary>
        /// Plugin dependencies (other plugin IDs this plugin requires)
        /// </summary>
        IReadOnlyList<string> Dependencies { get; }

        /// <summary>
        /// Permissions required by this plugin
        /// </summary>
        PluginPermissions RequiredPermissions { get; }

        /// <summary>
        /// Current state of the plugin
        /// </summary>
        PluginState State { get; }

        /// <summary>
        /// Called when the plugin is loaded. Initialize resources here.
        /// </summary>
        /// <param name="context">The plugin context providing access to host services</param>
        Task InitializeAsync(IPluginContext context);

        /// <summary>
        /// Called when the plugin should start operating
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// Called when the plugin should stop operating
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Gets the plugin's configuration view model, if it has settings
        /// </summary>
        IPluginSettingsViewModel? GetSettingsViewModel();
    }

    /// <summary>
    /// Plugin state enumeration
    /// </summary>
    public enum PluginState
    {
        /// <summary>Plugin is not loaded</summary>
        Unloaded,
        /// <summary>Plugin is loaded but not initialized</summary>
        Loaded,
        /// <summary>Plugin is initialized and ready</summary>
        Initialized,
        /// <summary>Plugin is running</summary>
        Running,
        /// <summary>Plugin is stopped</summary>
        Stopped,
        /// <summary>Plugin encountered an error</summary>
        Error
    }

    /// <summary>
    /// Permissions that plugins can request
    /// </summary>
    [Flags]
    public enum PluginPermissions
    {
        /// <summary>No permissions required</summary>
        None = 0,

        /// <summary>Read access to tasks</summary>
        ReadTasks = 1 << 0,
        /// <summary>Write access to tasks</summary>
        WriteTasks = 1 << 1,

        /// <summary>Read access to reminders</summary>
        ReadReminders = 1 << 2,
        /// <summary>Write access to reminders</summary>
        WriteReminders = 1 << 3,

        /// <summary>Read access to data banks</summary>
        ReadDataBanks = 1 << 4,
        /// <summary>Write access to data banks</summary>
        WriteDataBanks = 1 << 5,

        /// <summary>Read access to screenshots/data assets</summary>
        ReadDataAssets = 1 << 6,
        /// <summary>Write access to screenshots/data assets</summary>
        WriteDataAssets = 1 << 7,

        /// <summary>Read access to chat sessions</summary>
        ReadChats = 1 << 8,
        /// <summary>Write access to chat sessions</summary>
        WriteChats = 1 << 9,

        /// <summary>Access to network/HTTP operations</summary>
        Network = 1 << 10,

        /// <summary>Access to file system operations</summary>
        FileSystem = 1 << 11,

        /// <summary>Access to COM automation (e.g., Outlook)</summary>
        ComAutomation = 1 << 12,

        /// <summary>Access to register and consume plugin services</summary>
        PluginServices = 1 << 13,

        /// <summary>All task permissions</summary>
        AllTasks = ReadTasks | WriteTasks,
        /// <summary>All reminder permissions</summary>
        AllReminders = ReadReminders | WriteReminders,
        /// <summary>All data bank permissions</summary>
        AllDataBanks = ReadDataBanks | WriteDataBanks,
        /// <summary>All data asset permissions</summary>
        AllDataAssets = ReadDataAssets | WriteDataAssets,
        /// <summary>All chat permissions</summary>
        AllChats = ReadChats | WriteChats,
        /// <summary>All permissions</summary>
        All = AllTasks | AllReminders | AllDataBanks | AllDataAssets | AllChats | Network | FileSystem | ComAutomation | PluginServices
    }
}
