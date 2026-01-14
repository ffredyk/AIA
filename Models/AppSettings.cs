using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AIA.Models
{
    /// <summary>
    /// Application-wide settings
    /// </summary>
    public class AppSettings : INotifyPropertyChanged
    {
        private bool _runOnStartup = false;
        private string _overlayShortcut = "Win+Q";
        private bool _minimizeToTrayOnClose = true;
        private bool _showInTaskbar = false;
        private bool _checkForUpdatesOnStartup = true;
        private bool _autoInstallUpdates = false;
        private string _dataStoragePath = string.Empty;
        private bool _enableAutoBackup = false;
        private int _autoBackupIntervalHours = 24;
        private int _maxBackupCount = 5;
        private string _language = string.Empty; // Empty = use system language
        private double _overlayOpacity = 1.0; // Default: fully opaque
        
        // Clipboard history settings
        private bool _enableClipboardHistory = true;
        private int _maxClipboardHistoryItems = 50;
        private int _maxClipboardItemSizeKb = 1024; // 1MB default
        private bool _trackClipboardText = true;
        private bool _trackClipboardImages = true;
        private bool _trackClipboardFiles = true;

        // Chat attachment settings
        private int _chatImageThumbnailSize = 80;

        // Screenshot history settings
        private bool _enableAllDisplayCapture = false;
        private int _maxScreenshotHistoryItems = 50;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Whether to start the application when Windows starts
        /// </summary>
        public bool RunOnStartup
        {
            get => _runOnStartup;
            set { _runOnStartup = value; OnPropertyChanged(nameof(RunOnStartup)); }
        }

        /// <summary>
        /// Keyboard shortcut to open the overlay (e.g., "Win+Q", "Ctrl+Shift+A")
        /// </summary>
        public string OverlayShortcut
        {
            get => _overlayShortcut;
            set { _overlayShortcut = value; OnPropertyChanged(nameof(OverlayShortcut)); }
        }

        /// <summary>
        /// Whether to minimize to system tray instead of closing
        /// </summary>
        public bool MinimizeToTrayOnClose
        {
            get => _minimizeToTrayOnClose;
            set { _minimizeToTrayOnClose = value; OnPropertyChanged(nameof(MinimizeToTrayOnClose)); }
        }

        /// <summary>
        /// Whether to show the app in the taskbar
        /// </summary>
        public bool ShowInTaskbar
        {
            get => _showInTaskbar;
            set { _showInTaskbar = value; OnPropertyChanged(nameof(ShowInTaskbar)); }
        }

        /// <summary>
        /// Whether to check for updates on startup
        /// </summary>
        public bool CheckForUpdatesOnStartup
        {
            get => _checkForUpdatesOnStartup;
            set { _checkForUpdatesOnStartup = value; OnPropertyChanged(nameof(CheckForUpdatesOnStartup)); }
        }

        /// <summary>
        /// Whether to automatically install updates
        /// </summary>
        public bool AutoInstallUpdates
        {
            get => _autoInstallUpdates;
            set { _autoInstallUpdates = value; OnPropertyChanged(nameof(AutoInstallUpdates)); }
        }

        /// <summary>
        /// Custom data storage path (empty = default location)
        /// </summary>
        public string DataStoragePath
        {
            get => _dataStoragePath;
            set { _dataStoragePath = value; OnPropertyChanged(nameof(DataStoragePath)); }
        }

        /// <summary>
        /// Whether to enable automatic backups
        /// </summary>
        public bool EnableAutoBackup
        {
            get => _enableAutoBackup;
            set { _enableAutoBackup = value; OnPropertyChanged(nameof(EnableAutoBackup)); }
        }

        /// <summary>
        /// Interval between automatic backups in hours
        /// </summary>
        public int AutoBackupIntervalHours
        {
            get => _autoBackupIntervalHours;
            set { _autoBackupIntervalHours = value; OnPropertyChanged(nameof(AutoBackupIntervalHours)); }
        }

        /// <summary>
        /// Maximum number of backups to keep
        /// </summary>
        public int MaxBackupCount
        {
            get => _maxBackupCount;
            set { _maxBackupCount = value; OnPropertyChanged(nameof(MaxBackupCount)); }
        }

        /// <summary>
        /// Language code for the UI (empty = use system language)
        /// </summary>
        public string Language
        {
            get => _language;
            set { _language = value; OnPropertyChanged(nameof(Language)); }
        }

        /// <summary>
        /// Overlay window opacity (0.0 to 1.0)
        /// </summary>
        public double OverlayOpacity
        {
            get => _overlayOpacity;
            set { _overlayOpacity = Math.Max(0.1, Math.Min(1.0, value)); OnPropertyChanged(nameof(OverlayOpacity)); }
        }

        #region Clipboard History Settings

        /// <summary>
        /// Whether to enable clipboard history tracking
        /// </summary>
        public bool EnableClipboardHistory
        {
            get => _enableClipboardHistory;
            set { _enableClipboardHistory = value; OnPropertyChanged(nameof(EnableClipboardHistory)); }
        }

        /// <summary>
        /// Maximum number of clipboard history items to keep
        /// </summary>
        public int MaxClipboardHistoryItems
        {
            get => _maxClipboardHistoryItems;
            set { _maxClipboardHistoryItems = Math.Max(1, Math.Min(200, value)); OnPropertyChanged(nameof(MaxClipboardHistoryItems)); }
        }

        /// <summary>
        /// Maximum size per clipboard item in KB (to prevent memory issues)
        /// </summary>
        public int MaxClipboardItemSizeKb
        {
            get => _maxClipboardItemSizeKb;
            set { _maxClipboardItemSizeKb = Math.Max(1, value); OnPropertyChanged(nameof(MaxClipboardItemSizeKb)); }
        }

        /// <summary>
        /// Whether to track text content in clipboard
        /// </summary>
        public bool TrackClipboardText
        {
            get => _trackClipboardText;
            set { _trackClipboardText = value; OnPropertyChanged(nameof(TrackClipboardText)); }
        }

        /// <summary>
        /// Whether to track images in clipboard
        /// </summary>
        public bool TrackClipboardImages
        {
            get => _trackClipboardImages;
            set { _trackClipboardImages = value; OnPropertyChanged(nameof(TrackClipboardImages)); }
        }

        /// <summary>
        /// Whether to track file paths in clipboard
        /// </summary>
        public bool TrackClipboardFiles
        {
            get => _trackClipboardFiles;
            set { _trackClipboardFiles = value; OnPropertyChanged(nameof(TrackClipboardFiles)); }
        }

        #endregion

        #region Chat Attachment Settings

        /// <summary>
        /// Size of image thumbnails in chat (in pixels)
        /// </summary>
        public int ChatImageThumbnailSize
        {
            get => _chatImageThumbnailSize;
            set { _chatImageThumbnailSize = Math.Max(40, Math.Min(200, value)); OnPropertyChanged(nameof(ChatImageThumbnailSize)); }
        }

        #endregion

        #region Screenshot History Settings

        /// <summary>
        /// Whether to enable capturing of all display screens
        /// </summary>
        public bool EnableAllDisplayCapture
        {
            get => _enableAllDisplayCapture;
            set { _enableAllDisplayCapture = value; OnPropertyChanged(nameof(EnableAllDisplayCapture)); }
        }

        /// <summary>
        /// Maximum number of screenshots to keep in history
        /// </summary>
        public int MaxScreenshotHistoryItems
        {
            get => _maxScreenshotHistoryItems;
            set { _maxScreenshotHistoryItems = Math.Max(1, Math.Min(200, value)); OnPropertyChanged(nameof(MaxScreenshotHistoryItems)); }
        }

        #endregion

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Plugin-specific settings
    /// </summary>
    public class PluginSettings : INotifyPropertyChanged
    {
        private bool _enablePlugins = true;
        private bool _checkForPluginUpdates = true;
        private bool _autoUpdatePlugins = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Whether the plugin system is enabled
        /// </summary>
        public bool EnablePlugins
        {
            get => _enablePlugins;
            set { _enablePlugins = value; OnPropertyChanged(nameof(EnablePlugins)); }
        }

        /// <summary>
        /// Whether to check for plugin updates
        /// </summary>
        public bool CheckForPluginUpdates
        {
            get => _checkForPluginUpdates;
            set { _checkForPluginUpdates = value; OnPropertyChanged(nameof(CheckForPluginUpdates)); }
        }

        /// <summary>
        /// Whether to automatically update plugins
        /// </summary>
        public bool AutoUpdatePlugins
        {
            get => _autoUpdatePlugins;
            set { _autoUpdatePlugins = value; OnPropertyChanged(nameof(AutoUpdatePlugins)); }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Settings for an individual plugin
    /// </summary>
    public class PluginInstanceSettings : INotifyPropertyChanged
    {
        private string _pluginId = string.Empty;
        private bool _isEnabled = true;
        private bool _hasFileSystemAccess = false;
        private bool _hasNetworkAccess = false;
        private bool _hasNotificationAccess = true;
        private bool _hasUIAccess = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// The plugin's unique identifier
        /// </summary>
        public string PluginId
        {
            get => _pluginId;
            set { _pluginId = value; OnPropertyChanged(nameof(PluginId)); }
        }

        /// <summary>
        /// Whether this plugin is enabled
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        /// <summary>
        /// Whether the plugin has file system access
        /// </summary>
        public bool HasFileSystemAccess
        {
            get => _hasFileSystemAccess;
            set { _hasFileSystemAccess = value; OnPropertyChanged(nameof(HasFileSystemAccess)); }
        }

        /// <summary>
        /// Whether the plugin has network access
        /// </summary>
        public bool HasNetworkAccess
        {
            get => _hasNetworkAccess;
            set { _hasNetworkAccess = value; OnPropertyChanged(nameof(HasNetworkAccess)); }
        }

        /// <summary>
        /// Whether the plugin can show notifications
        /// </summary>
        public bool HasNotificationAccess
        {
            get => _hasNotificationAccess;
            set { _hasNotificationAccess = value; OnPropertyChanged(nameof(HasNotificationAccess)); }
        }

        /// <summary>
        /// Whether the plugin can add UI elements
        /// </summary>
        public bool HasUIAccess
        {
            get => _hasUIAccess;
            set { _hasUIAccess = value; OnPropertyChanged(nameof(HasUIAccess)); }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
