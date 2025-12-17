using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Host
{
    /// <summary>
    /// Information about a loaded plugin
    /// </summary>
    public class PluginInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Version Version { get; set; } = new Version(1, 0);
        public string Author { get; set; } = string.Empty;
        public string AssemblyPath { get; set; } = string.Empty;
        public bool IsBuiltIn { get; set; }
        public bool IsEnabled { get; set; } = true;
        public PluginPermissions RequiredPermissions { get; set; }
        public PluginPermissions GrantedPermissions { get; set; }
        public PluginState State { get; set; }
        public IReadOnlyList<string> Dependencies { get; set; } = Array.Empty<string>();
        public string? IconSymbol { get; set; }
        public string? ErrorMessage { get; set; }
        
        internal IPlugin? Instance { get; set; }
        internal PluginLoadContext? LoadContext { get; set; }
    }

    /// <summary>
    /// Custom assembly load context for plugin isolation
    /// </summary>
    internal class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // First try to resolve from plugin's dependencies
            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            // Fall back to default context for shared assemblies
            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Loads and manages plugins
    /// </summary>
    public class PluginLoader
    {
        private readonly string _pluginsDirectory;
        private readonly Dictionary<string, PluginInfo> _loadedPlugins = new();
        private readonly IPluginLogger _logger;

        public IReadOnlyDictionary<string, PluginInfo> LoadedPlugins => _loadedPlugins;

        public PluginLoader(string pluginsDirectory, IPluginLogger logger)
        {
            _pluginsDirectory = pluginsDirectory ?? throw new ArgumentNullException(nameof(pluginsDirectory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (!Directory.Exists(_pluginsDirectory))
            {
                Directory.CreateDirectory(_pluginsDirectory);
            }
        }

        /// <summary>
        /// Discovers all plugins in the plugins directory
        /// </summary>
        public IReadOnlyList<PluginInfo> DiscoverPlugins()
        {
            var plugins = new List<PluginInfo>();

            _logger.Info($"Discovering plugins in: {_pluginsDirectory}");

            // Look for plugin DLLs in subdirectories
            foreach (var pluginDir in Directory.GetDirectories(_pluginsDirectory))
            {
                var dirName = Path.GetFileName(pluginDir);
                var pluginDll = Path.Combine(pluginDir, $"{dirName}.dll");

                if (File.Exists(pluginDll))
                {
                    var info = DiscoverPlugin(pluginDll);
                    if (info != null)
                    {
                        plugins.Add(info);
                    }
                }
            }

            // Also look for DLLs directly in the plugins folder
            foreach (var dllPath in Directory.GetFiles(_pluginsDirectory, "*.dll"))
            {
                var info = DiscoverPlugin(dllPath);
                if (info != null)
                {
                    plugins.Add(info);
                }
            }

            _logger.Info($"Discovered {plugins.Count} plugin(s)");
            return plugins;
        }

        private PluginInfo? DiscoverPlugin(string assemblyPath)
        {
            try
            {
                // Load assembly temporarily to inspect it
                var loadContext = new PluginLoadContext(assemblyPath);
                
                try
                {
                    var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

                    foreach (var type in assembly.GetTypes())
                    {
                        var pluginAttr = type.GetCustomAttribute<PluginAttribute>();
                        if (pluginAttr != null && typeof(IPlugin).IsAssignableFrom(type))
                        {
                            var info = new PluginInfo
                            {
                                Id = pluginAttr.Id,
                                Name = pluginAttr.Name,
                                AssemblyPath = assemblyPath,
                                IsBuiltIn = pluginAttr.IsBuiltIn,
                                IconSymbol = pluginAttr.IconSymbol,
                                State = PluginState.Unloaded
                            };

                            // Unload the temporary context
                            loadContext.Unload();
                            
                            return info;
                        }
                    }
                }
                finally
                {
                    loadContext.Unload();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to discover plugin at {assemblyPath}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Loads a plugin from the specified path
        /// </summary>
        public async Task<PluginInfo?> LoadPluginAsync(string assemblyPath, PluginPermissions grantedPermissions)
        {
            try
            {
                _logger.Info($"Loading plugin from: {assemblyPath}");

                var loadContext = new PluginLoadContext(assemblyPath);
                var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

                foreach (var type in assembly.GetTypes())
                {
                    var pluginAttr = type.GetCustomAttribute<PluginAttribute>();
                    if (pluginAttr != null && typeof(IPlugin).IsAssignableFrom(type))
                    {
                        var instance = Activator.CreateInstance(type) as IPlugin;
                        if (instance == null)
                        {
                            _logger.Error($"Failed to create instance of plugin: {type.FullName}");
                            continue;
                        }

                        var info = new PluginInfo
                        {
                            Id = instance.Id,
                            Name = instance.Name,
                            Description = instance.Description,
                            Version = instance.Version,
                            Author = instance.Author,
                            AssemblyPath = assemblyPath,
                            IsBuiltIn = pluginAttr.IsBuiltIn,
                            IconSymbol = pluginAttr.IconSymbol,
                            RequiredPermissions = instance.RequiredPermissions,
                            GrantedPermissions = grantedPermissions,
                            Dependencies = instance.Dependencies,
                            State = PluginState.Loaded,
                            Instance = instance,
                            LoadContext = loadContext
                        };

                        _loadedPlugins[info.Id] = info;
                        _logger.Info($"Plugin loaded: {info.Name} v{info.Version}");
                        return info;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load plugin from {assemblyPath}", ex);
                return new PluginInfo
                {
                    AssemblyPath = assemblyPath,
                    State = PluginState.Error,
                    ErrorMessage = ex.Message
                };
            }

            return null;
        }

        /// <summary>
        /// Unloads a plugin
        /// </summary>
        public void UnloadPlugin(string pluginId)
        {
            if (_loadedPlugins.TryGetValue(pluginId, out var info))
            {
                try
                {
                    info.Instance?.Dispose();
                    info.LoadContext?.Unload();
                    _loadedPlugins.Remove(pluginId);
                    _logger.Info($"Plugin unloaded: {info.Name}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to unload plugin {pluginId}", ex);
                }
            }
        }

        /// <summary>
        /// Unloads all plugins
        /// </summary>
        public void UnloadAll()
        {
            foreach (var pluginId in _loadedPlugins.Keys.ToList())
            {
                UnloadPlugin(pluginId);
            }
        }
    }
}
