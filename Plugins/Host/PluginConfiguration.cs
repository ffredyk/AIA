using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Host
{
    /// <summary>
    /// Configuration for a single plugin
    /// </summary>
    public class PluginConfiguration
    {
        public string PluginId { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public PluginPermissions GrantedPermissions { get; set; } = PluginPermissions.All;
        public Dictionary<string, object?> CustomSettings { get; set; } = new();
    }

    /// <summary>
    /// Stores plugin configurations
    /// </summary>
    public class PluginConfigurationStore
    {
        private readonly string _configFilePath;
        private readonly string _pluginsDirectory;
        private readonly Dictionary<string, PluginConfiguration> _configurations = new();

        public PluginConfigurationStore(string pluginsDirectory)
        {
            _pluginsDirectory = pluginsDirectory ?? throw new ArgumentNullException(nameof(pluginsDirectory));
            _configFilePath = Path.Combine(pluginsDirectory, "plugins.config.json");
        }

        public async Task LoadAsync()
        {
            if (File.Exists(_configFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_configFilePath);
                    var configs = JsonSerializer.Deserialize<List<PluginConfiguration>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _configurations.Clear();
                    if (configs != null)
                    {
                        foreach (var config in configs)
                        {
                            _configurations[config.PluginId] = config;
                        }
                    }
                }
                catch (Exception)
                {
                    // If config is corrupted, start fresh
                    _configurations.Clear();
                }
            }
        }

        public async Task SaveAsync()
        {
            var configs = new List<PluginConfiguration>(_configurations.Values);
            var json = JsonSerializer.Serialize(configs, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_configFilePath, json);
        }

        public PluginConfiguration GetPluginConfig(string pluginId)
        {
            if (!_configurations.TryGetValue(pluginId, out var config))
            {
                config = new PluginConfiguration
                {
                    PluginId = pluginId,
                    IsEnabled = true,
                    GrantedPermissions = PluginPermissions.All
                };
                _configurations[pluginId] = config;
            }

            return config;
        }

        public string GetPluginDataDirectory(string pluginId)
        {
            var directory = Path.Combine(_pluginsDirectory, "Data", pluginId);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return directory;
        }
    }

    /// <summary>
    /// Plugin settings storage implementation
    /// </summary>
    public class PluginSettingsStorage : IPluginSettingsStorage
    {
        private readonly string _settingsFilePath;
        private Dictionary<string, object?> _settings = new();

        public PluginSettingsStorage(string pluginDataDirectory, string pluginId)
        {
            _settingsFilePath = Path.Combine(pluginDataDirectory, $"{pluginId}.settings.json");
        }

        public T? Get<T>(string key, T? defaultValue = default)
        {
            if (_settings.TryGetValue(key, out var value))
            {
                if (value is JsonElement element)
                {
                    try
                    {
                        return element.Deserialize<T>();
                    }
                    catch
                    {
                        return defaultValue;
                    }
                }

                if (value is T typedValue)
                {
                    return typedValue;
                }
            }

            return defaultValue;
        }

        public void Set<T>(string key, T value)
        {
            _settings[key] = value;
        }

        public async Task SaveAsync()
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_settingsFilePath, json);
        }

        public async Task LoadAsync()
        {
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    _settings = JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new();
                }
                catch
                {
                    _settings = new();
                }
            }
        }
    }

    /// <summary>
    /// Plugin service registry for inter-plugin communication
    /// </summary>
    public class PluginServiceRegistry : IPluginServiceRegistry
    {
        private readonly Dictionary<string, object> _services = new();
        private readonly object _lock = new();

        public void Register<TService>(string serviceName, TService service) where TService : class
        {
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentNullException(nameof(serviceName));
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            lock (_lock)
            {
                _services[serviceName] = service;
            }
        }

        public TService? Get<TService>(string serviceName) where TService : class
        {
            lock (_lock)
            {
                if (_services.TryGetValue(serviceName, out var service))
                {
                    return service as TService;
                }
            }
            return null;
        }

        public bool IsRegistered(string serviceName)
        {
            lock (_lock)
            {
                return _services.ContainsKey(serviceName);
            }
        }

        public string[] GetRegisteredServices()
        {
            lock (_lock)
            {
                return new List<string>(_services.Keys).ToArray();
            }
        }
    }

    /// <summary>
    /// Plugin logger implementation
    /// </summary>
    public class PluginHostLogger : IPluginLogger
    {
        public void Debug(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[Plugin:DEBUG] {message}");
        }

        public void Info(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[Plugin:INFO] {message}");
        }

        public void Warning(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[Plugin:WARN] {message}");
        }

        public void Error(string message, Exception? exception = null)
        {
            var exMsg = exception != null ? $" - {exception.Message}" : "";
            System.Diagnostics.Debug.WriteLine($"[Plugin:ERROR] {message}{exMsg}");
        }
    }
}
