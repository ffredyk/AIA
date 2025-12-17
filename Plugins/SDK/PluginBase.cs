using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Base class for plugins providing common functionality
    /// </summary>
    public abstract class PluginBase : IPlugin
    {
        private PluginState _state = PluginState.Unloaded;

        protected IPluginContext Context { get; private set; } = null!;

        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract Version Version { get; }
        public virtual string Author => "Unknown";
        public virtual IReadOnlyList<string> Dependencies => Array.Empty<string>();
        public abstract PluginPermissions RequiredPermissions { get; }

        public PluginState State
        {
            get => _state;
            protected set
            {
                if (_state != value)
                {
                    _state = value;
                    OnStateChanged(value);
                }
            }
        }

        public virtual async Task InitializeAsync(IPluginContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            State = PluginState.Initialized;
            await OnInitializeAsync();
        }

        public virtual async Task StartAsync()
        {
            State = PluginState.Running;
            await OnStartAsync();
        }

        public virtual async Task StopAsync()
        {
            State = PluginState.Stopped;
            await OnStopAsync();
        }

        public virtual IPluginSettingsViewModel? GetSettingsViewModel() => null;

        public virtual void Dispose()
        {
            State = PluginState.Unloaded;
            OnDispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Override to perform custom initialization
        /// </summary>
        protected virtual Task OnInitializeAsync() => Task.CompletedTask;

        /// <summary>
        /// Override to perform custom start logic
        /// </summary>
        protected virtual Task OnStartAsync() => Task.CompletedTask;

        /// <summary>
        /// Override to perform custom stop logic
        /// </summary>
        protected virtual Task OnStopAsync() => Task.CompletedTask;

        /// <summary>
        /// Override to perform custom cleanup
        /// </summary>
        protected virtual void OnDispose() { }

        /// <summary>
        /// Called when state changes
        /// </summary>
        protected virtual void OnStateChanged(PluginState newState) { }

        /// <summary>
        /// Logs a debug message
        /// </summary>
        protected void LogDebug(string message) => Context?.Logger.Debug(message);

        /// <summary>
        /// Logs an info message
        /// </summary>
        protected void LogInfo(string message) => Context?.Logger.Info(message);

        /// <summary>
        /// Logs a warning message
        /// </summary>
        protected void LogWarning(string message) => Context?.Logger.Warning(message);

        /// <summary>
        /// Logs an error message
        /// </summary>
        protected void LogError(string message, Exception? ex = null) => Context?.Logger.Error(message, ex);

        /// <summary>
        /// Checks if the plugin has the specified permission
        /// </summary>
        protected bool HasPermission(PluginPermissions permission) => Context?.HasPermission(permission) ?? false;

        /// <summary>
        /// Throws if the plugin doesn't have the specified permission
        /// </summary>
        protected void RequirePermission(PluginPermissions permission)
        {
            if (!HasPermission(permission))
            {
                throw new PluginPermissionException(Id, permission);
            }
        }
    }

    /// <summary>
    /// Base class for plugin tab view models
    /// </summary>
    public abstract class PluginTabViewModelBase : IPluginTabViewModel
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public abstract DataTemplate GetDataTemplate();

        public virtual void OnActivated() { }

        public virtual void OnDeactivated() { }

        public virtual void Refresh() { }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    /// <summary>
    /// Base class for plugin settings view models
    /// </summary>
    public abstract class PluginSettingsViewModelBase : IPluginSettingsViewModel
    {
        private bool _isDirty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public abstract string SettingsTitle { get; }
        public virtual string SettingsDescription => string.Empty;

        public bool IsDirty
        {
            get => _isDirty;
            protected set => SetProperty(ref _isDirty, value);
        }

        public abstract void Load();
        public abstract void Save();
        public abstract void Reset();

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            
            if (propertyName != nameof(IsDirty))
                IsDirty = true;
            
            return true;
        }
    }

    /// <summary>
    /// Exception thrown when a plugin attempts an operation without required permissions
    /// </summary>
    public class PluginPermissionException : Exception
    {
        public string PluginId { get; }
        public PluginPermissions RequiredPermission { get; }

        public PluginPermissionException(string pluginId, PluginPermissions requiredPermission)
            : base($"Plugin '{pluginId}' does not have required permission: {requiredPermission}")
        {
            PluginId = pluginId;
            RequiredPermission = requiredPermission;
        }
    }

    /// <summary>
    /// Exception thrown when plugin initialization fails
    /// </summary>
    public class PluginInitializationException : Exception
    {
        public string PluginId { get; }

        public PluginInitializationException(string pluginId, string message, Exception? innerException = null)
            : base($"Plugin '{pluginId}' failed to initialize: {message}", innerException)
        {
            PluginId = pluginId;
        }
    }
}
