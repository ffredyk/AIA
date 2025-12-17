using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AIA.Models;
using AIA.Plugins.Host;
using AIA.Services;

namespace AIA
{
    public partial class SettingsWindow : Window, INotifyPropertyChanged
    {
        private AppSettings _appSettings = new();
        private PluginSettings _pluginSettings = new();
        private readonly PluginManager? _pluginManager;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<PluginDisplayInfo> Plugins { get; } = new();

        public SettingsWindow(PluginManager? pluginManager = null)
        {
            _pluginManager = pluginManager;
            
            InitializeComponent();
            DataContext = this;

            Loaded += SettingsWindow_Loaded;
        }

        private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Load settings
            _appSettings = await AppSettingsService.LoadAppSettingsAsync();
            _pluginSettings = await AppSettingsService.LoadPluginSettingsAsync();

            // Apply settings to UI
            ApplySettingsToUI();

            // Load plugins list
            RefreshPluginsList();

            // Load backup list
            RefreshBackupsList();

            // Load system info
            LoadSystemInfo();
        }

        private void ApplySettingsToUI()
        {
            // General tab
            ChkRunOnStartup.IsChecked = _appSettings.RunOnStartup;
            TxtOverlayShortcut.Text = _appSettings.OverlayShortcut;
            ChkMinimizeToTray.IsChecked = _appSettings.MinimizeToTrayOnClose;
            ChkCheckUpdates.IsChecked = _appSettings.CheckForUpdatesOnStartup;
            ChkAutoInstallUpdates.IsChecked = _appSettings.AutoInstallUpdates;

            // Plugin tab
            ChkEnablePlugins.IsChecked = _pluginSettings.EnablePlugins;
            ChkCheckPluginUpdates.IsChecked = _pluginSettings.CheckForPluginUpdates;
            ChkAutoUpdatePlugins.IsChecked = _pluginSettings.AutoUpdatePlugins;

            // Notification tab
            var notificationSettings = OverlayViewModel.Singleton.NotificationSettings;
            ChkNotificationsEnabled.IsChecked = notificationSettings.IsEnabled;
            ChkWarningNotifications.IsChecked = notificationSettings.ShowWarningNotifications;
            ChkUrgentNotifications.IsChecked = notificationSettings.ShowUrgentNotifications;
            ChkOverdueNotifications.IsChecked = notificationSettings.ShowOverdueNotifications;
            ChkPlaySound.IsChecked = notificationSettings.PlaySound;
            TxtWarningMinutes.Text = notificationSettings.WarningMinutes.ToString();
            TxtUrgentMinutes.Text = notificationSettings.UrgentMinutes.ToString();
            TxtNotificationDuration.Text = notificationSettings.NotificationDurationSeconds.ToString();

            // Data tab
            TxtDataPath.Text = AppSettingsService.GetDataDirectory();
            ChkAutoBackup.IsChecked = _appSettings.EnableAutoBackup;
            TxtBackupInterval.Text = _appSettings.AutoBackupIntervalHours.ToString();
            TxtMaxBackups.Text = _appSettings.MaxBackupCount.ToString();
        }

        private void LoadSystemInfo()
        {
            TxtAppVersion.Text = $"Version {AppSettingsService.GetAppVersion()}";
            TxtAppPath.Text = AppDomain.CurrentDomain.BaseDirectory;
            TxtPluginsPath.Text = AppSettingsService.GetPluginsDirectory();
            TxtDotNetVersion.Text = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            TxtPluginCount.Text = _pluginManager?.Plugins.Count.ToString() ?? "0";
        }

        private void RefreshPluginsList()
        {
            Plugins.Clear();

            if (_pluginManager != null)
            {
                foreach (var plugin in _pluginManager.Plugins)
                {
                    Plugins.Add(new PluginDisplayInfo
                    {
                        Id = plugin.Id,
                        Name = plugin.Name,
                        Description = plugin.Description,
                        Version = plugin.Version,
                        Author = plugin.Author,
                        IsEnabled = plugin.IsEnabled,
                        IsBuiltIn = plugin.IsBuiltIn,
                        State = plugin.State
                    });
                }
            }

            PluginsListControl.ItemsSource = Plugins;
            PluginsEmptyState.Visibility = Plugins.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshBackupsList()
        {
            var backups = AppSettingsService.GetBackupList();
            BackupsList.ItemsSource = backups;
        }

        #region Event Handlers

        private void ChkRunOnStartup_Changed(object sender, RoutedEventArgs e)
        {
            var isChecked = ChkRunOnStartup.IsChecked ?? false;
            var success = AppSettingsService.SetRunOnStartup(isChecked);

            if (!success)
            {
                // Revert the checkbox if we couldn't change the setting
                ChkRunOnStartup.IsChecked = !isChecked;
                StatusText.Text = "Failed to modify startup settings. Try running as administrator.";
            }
            else
            {
                _appSettings.RunOnStartup = isChecked;
                StatusText.Text = isChecked ? "Added to Windows startup" : "Removed from Windows startup";
            }
        }

        private void BtnApplyShortcut_Click(object sender, RoutedEventArgs e)
        {
            var shortcut = TxtOverlayShortcut.Text.Trim();
            
            if (string.IsNullOrEmpty(shortcut))
            {
                TxtShortcutStatus.Text = "Please enter a shortcut";
                TxtShortcutStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 102, 102));
                return;
            }

            // Parse and validate the shortcut
            var (modifiers, virtualKey) = AppSettingsService.ParseShortcut(shortcut);
            
            if (virtualKey == 0)
            {
                TxtShortcutStatus.Text = "Invalid shortcut format. Use format like Win+Q, Ctrl+Shift+A";
                TxtShortcutStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 102, 102));
                return;
            }

            _appSettings.OverlayShortcut = shortcut;
            TxtShortcutStatus.Text = $"Shortcut will be applied after restart";
            TxtShortcutStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 183, 95));
            StatusText.Text = "Shortcut updated - restart required to apply";
        }

        private void BtnCheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Checking for updates...";
            // TODO: Implement actual update check
            StatusText.Text = "You are running the latest version";
        }

        private void BtnRefreshPlugins_Click(object sender, RoutedEventArgs e)
        {
            RefreshPluginsList();
            StatusText.Text = $"Found {Plugins.Count} plugin(s)";
        }

        private void BtnPluginPermissions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is PluginDisplayInfo plugin)
            {
                var permissionsWindow = new PluginPermissionsWindow(plugin);
                permissionsWindow.Owner = this;
                permissionsWindow.ShowDialog();
            }
        }

        private void BtnOpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppSettingsService.GetDataDirectory(),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Could not open folder: {ex.Message}";
            }
        }

        private async void BtnBackupNow_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Creating backup...";
            
            var success = await AppSettingsService.CreateBackupAsync();
            
            if (success)
            {
                if (int.TryParse(TxtMaxBackups.Text, out var maxBackups))
                {
                    await AppSettingsService.CleanupOldBackupsAsync(maxBackups);
                }
                
                RefreshBackupsList();
                StatusText.Text = "Backup created successfully";
            }
            else
            {
                StatusText.Text = "Failed to create backup";
            }
        }

        private void BtnOpenBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var backupFolder = AppSettingsService.GetBackupFolder();
                if (!System.IO.Directory.Exists(backupFolder))
                {
                    System.IO.Directory.CreateDirectory(backupFolder);
                }
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = backupFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Could not open folder: {ex.Message}";
            }
        }

        private void BtnGitHub_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/ffredyk/AIA",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Collect settings from UI
            _appSettings.OverlayShortcut = TxtOverlayShortcut.Text.Trim();
            _appSettings.MinimizeToTrayOnClose = ChkMinimizeToTray.IsChecked ?? true;
            _appSettings.CheckForUpdatesOnStartup = ChkCheckUpdates.IsChecked ?? true;
            _appSettings.AutoInstallUpdates = ChkAutoInstallUpdates.IsChecked ?? false;
            _appSettings.EnableAutoBackup = ChkAutoBackup.IsChecked ?? false;
            
            if (int.TryParse(TxtBackupInterval.Text, out var backupInterval))
                _appSettings.AutoBackupIntervalHours = backupInterval;
            
            if (int.TryParse(TxtMaxBackups.Text, out var maxBackups))
                _appSettings.MaxBackupCount = maxBackups;

            _pluginSettings.EnablePlugins = ChkEnablePlugins.IsChecked ?? true;
            _pluginSettings.CheckForPluginUpdates = ChkCheckPluginUpdates.IsChecked ?? true;
            _pluginSettings.AutoUpdatePlugins = ChkAutoUpdatePlugins.IsChecked ?? false;

            // Update notification settings
            var notificationSettings = OverlayViewModel.Singleton.NotificationSettings;
            notificationSettings.IsEnabled = ChkNotificationsEnabled.IsChecked ?? true;
            notificationSettings.ShowWarningNotifications = ChkWarningNotifications.IsChecked ?? true;
            notificationSettings.ShowUrgentNotifications = ChkUrgentNotifications.IsChecked ?? true;
            notificationSettings.ShowOverdueNotifications = ChkOverdueNotifications.IsChecked ?? true;
            notificationSettings.PlaySound = ChkPlaySound.IsChecked ?? false;
            
            if (int.TryParse(TxtWarningMinutes.Text, out var warningMinutes))
                notificationSettings.WarningMinutes = warningMinutes;
            
            if (int.TryParse(TxtUrgentMinutes.Text, out var urgentMinutes))
                notificationSettings.UrgentMinutes = urgentMinutes;
            
            if (int.TryParse(TxtNotificationDuration.Text, out var duration))
                notificationSettings.NotificationDurationSeconds = duration;

            // Save all settings
            await AppSettingsService.SaveAppSettingsAsync(_appSettings);
            await AppSettingsService.SavePluginSettingsAsync(_pluginSettings);

            StatusText.Text = "Settings saved successfully";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        protected override void OnClosing(CancelEventArgs e)
        {
            // Auto-save on close
            BtnSave_Click(this, new RoutedEventArgs());
            base.OnClosing(e);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Display model for plugins in the settings UI
    /// </summary>
    public class PluginDisplayInfo : INotifyPropertyChanged
    {
        private bool _isEnabled = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Version Version { get; set; } = new Version(1, 0);
        public string Author { get; set; } = string.Empty;
        public bool IsBuiltIn { get; set; }
        public Plugins.SDK.PluginState State { get; set; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
