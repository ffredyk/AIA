using System;
using System.ComponentModel;

namespace AIA.Models.Automation
{
    /// <summary>
    /// Global settings for the automation system
    /// </summary>
    public class AutomationSettings : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        private int _maxConcurrentAutomations = 5;
        private int _maxConcurrentAgents = 3;
        private int _defaultMaxIterations = 10;
        private int _defaultMaxTotalTokens = 100000;
        private int _historyRetentionDays = 30;
        private int _maxHistoryEntries = 1000;
        private bool _enableLiveMonitoring = true;
        private bool _pauseOnOverlayVisible = true;
        private bool _showExecutionNotifications = true;
        private bool _logDebugTraces = false;
        private string _automationsFolder = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Whether the automation system is enabled globally
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        /// <summary>
        /// Maximum number of automations that can run concurrently
        /// </summary>
        public int MaxConcurrentAutomations
        {
            get => _maxConcurrentAutomations;
            set { _maxConcurrentAutomations = value; OnPropertyChanged(nameof(MaxConcurrentAutomations)); }
        }

        /// <summary>
        /// Maximum number of agents that can run concurrently across all automations
        /// </summary>
        public int MaxConcurrentAgents
        {
            get => _maxConcurrentAgents;
            set { _maxConcurrentAgents = value; OnPropertyChanged(nameof(MaxConcurrentAgents)); }
        }

        /// <summary>
        /// Default maximum iterations for new agents
        /// </summary>
        public int DefaultMaxIterations
        {
            get => _defaultMaxIterations;
            set { _defaultMaxIterations = value; OnPropertyChanged(nameof(DefaultMaxIterations)); }
        }

        /// <summary>
        /// Default maximum total tokens for new agents
        /// </summary>
        public int DefaultMaxTotalTokens
        {
            get => _defaultMaxTotalTokens;
            set { _defaultMaxTotalTokens = value; OnPropertyChanged(nameof(DefaultMaxTotalTokens)); }
        }

        /// <summary>
        /// Number of days to retain execution history
        /// </summary>
        public int HistoryRetentionDays
        {
            get => _historyRetentionDays;
            set { _historyRetentionDays = value; OnPropertyChanged(nameof(HistoryRetentionDays)); }
        }

        /// <summary>
        /// Maximum number of history entries to keep
        /// </summary>
        public int MaxHistoryEntries
        {
            get => _maxHistoryEntries;
            set { _maxHistoryEntries = value; OnPropertyChanged(nameof(MaxHistoryEntries)); }
        }

        /// <summary>
        /// Whether to enable live monitoring of executions
        /// </summary>
        public bool EnableLiveMonitoring
        {
            get => _enableLiveMonitoring;
            set { _enableLiveMonitoring = value; OnPropertyChanged(nameof(EnableLiveMonitoring)); }
        }

        /// <summary>
        /// Whether to pause triggers when overlay is visible
        /// </summary>
        public bool PauseOnOverlayVisible
        {
            get => _pauseOnOverlayVisible;
            set { _pauseOnOverlayVisible = value; OnPropertyChanged(nameof(PauseOnOverlayVisible)); }
        }

        /// <summary>
        /// Whether to show notifications for execution events
        /// </summary>
        public bool ShowExecutionNotifications
        {
            get => _showExecutionNotifications;
            set { _showExecutionNotifications = value; OnPropertyChanged(nameof(ShowExecutionNotifications)); }
        }

        /// <summary>
        /// Whether to log debug-level traces
        /// </summary>
        public bool LogDebugTraces
        {
            get => _logDebugTraces;
            set { _logDebugTraces = value; OnPropertyChanged(nameof(LogDebugTraces)); }
        }

        /// <summary>
        /// Custom folder for automation files (empty = default location)
        /// </summary>
        public string AutomationsFolder
        {
            get => _automationsFolder;
            set { _automationsFolder = value; OnPropertyChanged(nameof(AutomationsFolder)); }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
