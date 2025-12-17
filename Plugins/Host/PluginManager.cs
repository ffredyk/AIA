using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Host
{
    /// <summary>
    /// Central manager for all plugins in the application
    /// </summary>
    public class PluginManager : IDisposable
    {
        private readonly PluginLoader _loader;
        private readonly IPluginHostServices _hostServices;
        private readonly IPluginLogger _logger;
        private readonly PluginServiceRegistry _serviceRegistry;
        private readonly PluginConfigurationStore _configStore;
        private readonly Dictionary<string, PluginContext> _pluginContexts = new();
        private bool _disposed;

        public ObservableCollection<PluginInfo> Plugins { get; } = new();
        public IPluginServiceRegistry ServiceRegistry => _serviceRegistry;

        public event EventHandler<PluginEventArgs>? PluginLoaded;
        public event EventHandler<PluginEventArgs>? PluginStarted;
        public event EventHandler<PluginEventArgs>? PluginStopped;
        public event EventHandler<PluginEventArgs>? PluginUnloaded;
        public event EventHandler<PluginErrorEventArgs>? PluginError;

        public PluginManager(string pluginsDirectory, IPluginHostServices hostServices)
        {
            _hostServices = hostServices ?? throw new ArgumentNullException(nameof(hostServices));
            _logger = new PluginHostLogger();
            _loader = new PluginLoader(pluginsDirectory, _logger);
            _serviceRegistry = new PluginServiceRegistry();
            _configStore = new PluginConfigurationStore(pluginsDirectory);
        }

        /// <summary>
        /// Discovers and loads all plugins from the plugins directory
        /// </summary>
        public async Task LoadAllPluginsAsync()
        {
            _logger.Info("Loading all plugins...");

            // Load configuration
            await _configStore.LoadAsync();

            // Discover plugins
            var discovered = _loader.DiscoverPlugins();

            // Sort by dependencies
            var sorted = TopologicalSort(discovered);

            foreach (var pluginInfo in sorted)
            {
                var config = _configStore.GetPluginConfig(pluginInfo.Id);
                
                if (!config.IsEnabled)
                {
                    _logger.Info($"Skipping disabled plugin: {pluginInfo.Name}");
                    pluginInfo.IsEnabled = false;
                    pluginInfo.State = PluginState.Unloaded;
                    Plugins.Add(pluginInfo);
                    continue;
                }

                // Grant permissions based on configuration
                var grantedPermissions = config.GrantedPermissions;

                var loaded = await _loader.LoadPluginAsync(pluginInfo.AssemblyPath, grantedPermissions);
                if (loaded != null && loaded.Instance != null)
                {
                    loaded.IsEnabled = true;
                    loaded.GrantedPermissions = grantedPermissions;
                    Plugins.Add(loaded);
                    PluginLoaded?.Invoke(this, new PluginEventArgs(loaded));
                }
                else if (loaded != null)
                {
                    loaded.IsEnabled = true;
                    Plugins.Add(loaded);
                    PluginError?.Invoke(this, new PluginErrorEventArgs(loaded, loaded.ErrorMessage ?? "Unknown error"));
                }
            }

            _logger.Info($"Loaded {Plugins.Count(p => p.Instance != null)} plugin(s)");
        }

        /// <summary>
        /// Initializes all loaded plugins
        /// </summary>
        public async Task InitializeAllPluginsAsync()
        {
            _logger.Info("Initializing plugins...");

            foreach (var pluginInfo in Plugins.Where(p => p.Instance != null && p.State == PluginState.Loaded))
            {
                await InitializePluginAsync(pluginInfo);
            }
        }

        /// <summary>
        /// Starts all initialized plugins
        /// </summary>
        public async Task StartAllPluginsAsync()
        {
            _logger.Info("Starting plugins...");

            foreach (var pluginInfo in Plugins.Where(p => p.Instance != null && p.State == PluginState.Initialized))
            {
                await StartPluginAsync(pluginInfo);
            }
        }

        /// <summary>
        /// Initializes a specific plugin
        /// </summary>
        public async Task InitializePluginAsync(PluginInfo pluginInfo)
        {
            if (pluginInfo.Instance == null) return;

            try
            {
                _logger.Info($"Initializing plugin: {pluginInfo.Name}");

                // Create context for this plugin
                var context = new PluginContext(
                    pluginInfo,
                    _hostServices,
                    _serviceRegistry,
                    _logger,
                    _configStore.GetPluginDataDirectory(pluginInfo.Id)
                );

                _pluginContexts[pluginInfo.Id] = context;

                await pluginInfo.Instance.InitializeAsync(context);
                pluginInfo.State = PluginState.Initialized;

                _logger.Info($"Plugin initialized: {pluginInfo.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to initialize plugin {pluginInfo.Name}", ex);
                pluginInfo.State = PluginState.Error;
                pluginInfo.ErrorMessage = ex.Message;
                PluginError?.Invoke(this, new PluginErrorEventArgs(pluginInfo, ex.Message, ex));
            }
        }

        /// <summary>
        /// Starts a specific plugin
        /// </summary>
        public async Task StartPluginAsync(PluginInfo pluginInfo)
        {
            if (pluginInfo.Instance == null || pluginInfo.State != PluginState.Initialized) return;

            try
            {
                _logger.Info($"Starting plugin: {pluginInfo.Name}");

                await pluginInfo.Instance.StartAsync();
                pluginInfo.State = PluginState.Running;

                PluginStarted?.Invoke(this, new PluginEventArgs(pluginInfo));
                _logger.Info($"Plugin started: {pluginInfo.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to start plugin {pluginInfo.Name}", ex);
                pluginInfo.State = PluginState.Error;
                pluginInfo.ErrorMessage = ex.Message;
                PluginError?.Invoke(this, new PluginErrorEventArgs(pluginInfo, ex.Message, ex));
            }
        }

        /// <summary>
        /// Stops a specific plugin
        /// </summary>
        public async Task StopPluginAsync(PluginInfo pluginInfo)
        {
            if (pluginInfo.Instance == null || pluginInfo.State != PluginState.Running) return;

            try
            {
                _logger.Info($"Stopping plugin: {pluginInfo.Name}");

                await pluginInfo.Instance.StopAsync();
                pluginInfo.State = PluginState.Stopped;

                PluginStopped?.Invoke(this, new PluginEventArgs(pluginInfo));
                _logger.Info($"Plugin stopped: {pluginInfo.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to stop plugin {pluginInfo.Name}", ex);
                pluginInfo.State = PluginState.Error;
                pluginInfo.ErrorMessage = ex.Message;
            }
        }

        /// <summary>
        /// Enables a plugin (will take effect on next restart)
        /// </summary>
        public async Task EnablePluginAsync(string pluginId)
        {
            var pluginInfo = Plugins.FirstOrDefault(p => p.Id == pluginId);
            if (pluginInfo != null)
            {
                pluginInfo.IsEnabled = true;
                var config = _configStore.GetPluginConfig(pluginId);
                config.IsEnabled = true;
                await _configStore.SaveAsync();
            }
        }

        /// <summary>
        /// Disables a plugin (will take effect on next restart)
        /// </summary>
        public async Task DisablePluginAsync(string pluginId)
        {
            var pluginInfo = Plugins.FirstOrDefault(p => p.Id == pluginId);
            if (pluginInfo != null)
            {
                pluginInfo.IsEnabled = false;
                var config = _configStore.GetPluginConfig(pluginId);
                config.IsEnabled = false;
                await _configStore.SaveAsync();
            }
        }

        /// <summary>
        /// Updates permissions for a plugin (will take effect on next restart)
        /// </summary>
        public async Task UpdatePluginPermissionsAsync(string pluginId, PluginPermissions permissions)
        {
            var config = _configStore.GetPluginConfig(pluginId);
            config.GrantedPermissions = permissions;
            await _configStore.SaveAsync();
        }

        /// <summary>
        /// Gets the context for a specific plugin
        /// </summary>
        public PluginContext? GetPluginContext(string pluginId)
        {
            return _pluginContexts.TryGetValue(pluginId, out var context) ? context : null;
        }

        /// <summary>
        /// Stops and unloads all plugins
        /// </summary>
        public async Task ShutdownAsync()
        {
            _logger.Info("Shutting down plugin manager...");

            // Stop all running plugins in reverse order
            foreach (var pluginInfo in Plugins.Reverse())
            {
                if (pluginInfo.Instance != null && pluginInfo.State == PluginState.Running)
                {
                    await StopPluginAsync(pluginInfo);
                }
            }

            // Unload all plugins
            _loader.UnloadAll();

            // Clear contexts
            _pluginContexts.Clear();

            _logger.Info("Plugin manager shutdown complete");
        }

        private IReadOnlyList<PluginInfo> TopologicalSort(IReadOnlyList<PluginInfo> plugins)
        {
            var result = new List<PluginInfo>();
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();

            void Visit(PluginInfo plugin)
            {
                if (visited.Contains(plugin.Id)) return;
                if (visiting.Contains(plugin.Id))
                {
                    _logger.Warning($"Circular dependency detected involving plugin: {plugin.Id}");
                    return;
                }

                visiting.Add(plugin.Id);

                foreach (var depId in plugin.Dependencies)
                {
                    var dep = plugins.FirstOrDefault(p => p.Id == depId);
                    if (dep != null)
                    {
                        Visit(dep);
                    }
                }

                visiting.Remove(plugin.Id);
                visited.Add(plugin.Id);
                result.Add(plugin);
            }

            foreach (var plugin in plugins)
            {
                Visit(plugin);
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ShutdownAsync().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }
    }

    public class PluginEventArgs : EventArgs
    {
        public PluginInfo Plugin { get; }

        public PluginEventArgs(PluginInfo plugin)
        {
            Plugin = plugin;
        }
    }

    public class PluginErrorEventArgs : PluginEventArgs
    {
        public string Message { get; }
        public Exception? Exception { get; }

        public PluginErrorEventArgs(PluginInfo plugin, string message, Exception? exception = null)
            : base(plugin)
        {
            Message = message;
            Exception = exception;
        }
    }
}
