using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIA.Models;
using AIA.Plugins.Host;
using AIA.Services;

// Resolve ambiguous references
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfColor = System.Windows.Media.Color;

namespace AIA
{
    public partial class SettingsWindow : Window, INotifyPropertyChanged
    {
        private AppSettings _appSettings = new();
        private PluginSettings _pluginSettings = new();
        private readonly PluginManager? _pluginManager;

        // Hotkey capture state
        private bool _isCapturingHotkey;
        private ModifierKeys _capturedModifiers = ModifierKeys.None;
        private Key _capturedKey = Key.None;
        private ModifierKeys _pendingModifiers = ModifierKeys.Windows;
        private Key _pendingKey = Key.Q;

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
            // Language selector
            var currentLang = LocalizationService.Instance.CurrentLanguageCode;
            foreach (ComboBoxItem item in CmbLanguage.Items)
            {
                if (item.Tag?.ToString() == currentLang)
                {
                    CmbLanguage.SelectedItem = item;
                    break;
                }
            }
            // Default to English if no match
            if (CmbLanguage.SelectedItem == null && CmbLanguage.Items.Count > 0)
            {
                CmbLanguage.SelectedIndex = 0;
            }

            // General tab
            ChkRunOnStartup.IsChecked = _appSettings.RunOnStartup;
            ChkMinimizeToTray.IsChecked = _appSettings.MinimizeToTrayOnClose;
            ChkCheckUpdates.IsChecked = _appSettings.CheckForUpdatesOnStartup;
            ChkAutoInstallUpdates.IsChecked = _appSettings.AutoInstallUpdates;

            // Parse and display the current hotkey
            var (modifiers, key) = HotkeyService.ParseHotkeyString(_appSettings.OverlayShortcut);
            _pendingModifiers = modifiers;
            _pendingKey = key;
            UpdateHotkeyDisplay();

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

        #region Hotkey Capture

        private void UpdateHotkeyDisplay()
        {
            if (_pendingKey == Key.None && _pendingModifiers == ModifierKeys.None)
            {
                TxtHotkeyDisplay.Visibility = Visibility.Collapsed;
                TxtHotkeyPlaceholder.Text = "No hotkey set";
                TxtHotkeyPlaceholder.Visibility = Visibility.Visible;
            }
            else
            {
                var displayText = HotkeyService.FormatHotkey(_pendingModifiers, _pendingKey);
                TxtHotkeyDisplay.Text = displayText;
                TxtHotkeyDisplay.Visibility = Visibility.Visible;
                TxtHotkeyPlaceholder.Visibility = Visibility.Collapsed;
            }
        }

        private void HotkeyCaptureBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Start capturing hotkey
            _isCapturingHotkey = true;
            _capturedModifiers = ModifierKeys.None;
            _capturedKey = Key.None;

            TxtHotkeyDisplay.Visibility = Visibility.Collapsed;
            TxtHotkeyPlaceholder.Text = LocalizationService.Instance.GetString("Settings_PressKeys");
            TxtHotkeyPlaceholder.Visibility = Visibility.Visible;
            
            HotkeyCaptureBorder.Focus();
            TxtShortcutStatus.Text = LocalizationService.Instance.GetString("Status_PressKeysCombination");
            TxtShortcutStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(153, 153, 153));
        }

        private void HotkeyCaptureBorder_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (!_isCapturingHotkey)
            {
                // If not capturing, start capturing on any key
                _isCapturingHotkey = true;
                _capturedModifiers = ModifierKeys.None;
                _capturedKey = Key.None;
            }

            e.Handled = true;

            // Get the actual key pressed (handle system key for Alt)
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Update modifiers
            _capturedModifiers = HotkeyService.GetModifiersFromKeyboard();

            // If it's a modifier key, just show current modifiers
            if (HotkeyService.IsModifierKey(key))
            {
                // Show modifiers being held
                if (_capturedModifiers != ModifierKeys.None)
                {
                    TxtHotkeyDisplay.Text = HotkeyService.FormatHotkey(_capturedModifiers, Key.None) + " + ...";
                    TxtHotkeyDisplay.Visibility = Visibility.Visible;
                    TxtHotkeyPlaceholder.Visibility = Visibility.Collapsed;
                }
                return;
            }

            // Non-modifier key pressed - complete the capture
            _capturedKey = key;
            
            // Finalize the hotkey
            _pendingModifiers = _capturedModifiers;
            _pendingKey = _capturedKey;
            _isCapturingHotkey = false;

            UpdateHotkeyDisplay();
            
            // Test if the hotkey can be registered
            var hotkeyService = App.Current.HotkeyService;
            if (hotkeyService != null && hotkeyService.TestHotkey(_pendingModifiers, _pendingKey))
            {
                TxtShortcutStatus.Text = LocalizationService.Instance.GetString("Status_HotkeyValid");
                TxtShortcutStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(30, 183, 95));
            }
            else
            {
                TxtShortcutStatus.Text = LocalizationService.Instance.GetString("Status_HotkeyConflict");
                TxtShortcutStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(255, 165, 0));
            }
        }

        private void HotkeyCaptureBorder_KeyUp(object sender, WpfKeyEventArgs e)
        {
            if (!_isCapturingHotkey)
                return;

            e.Handled = true;

            // Update current modifiers
            _capturedModifiers = HotkeyService.GetModifiersFromKeyboard();

            // If all keys are released and we have a valid key, complete capture
            if (_capturedKey != Key.None)
            {
                _isCapturingHotkey = false;
                UpdateHotkeyDisplay();
            }
            else if (_capturedModifiers == ModifierKeys.None)
            {
                // All modifiers released without pressing a non-modifier key
                TxtHotkeyDisplay.Visibility = Visibility.Collapsed;
                TxtHotkeyPlaceholder.Text = "Press keys...";
                TxtHotkeyPlaceholder.Visibility = Visibility.Visible;
            }
        }

        private void BtnClearHotkey_Click(object sender, RoutedEventArgs e)
        {
            _pendingModifiers = ModifierKeys.None;
            _pendingKey = Key.None;
            _isCapturingHotkey = false;
            
            UpdateHotkeyDisplay();
            TxtShortcutStatus.Text = LocalizationService.Instance.GetString("Status_HotkeyCleared");
            TxtShortcutStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(153, 153, 153));
        }

        #endregion

        #region Event Handlers

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLanguage.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string languageCode)
            {
                LocalizationService.Instance.SetLanguage(languageCode);
                _appSettings.Language = languageCode;
                
                // Note: Full UI refresh would require reopening the window or refreshing all bindings
                StatusText.Text = LocalizationService.Instance.GetString("Status_SettingsSaved");
            }
        }

        private void ChkRunOnStartup_Changed(object sender, RoutedEventArgs e)
        {
            var isChecked = ChkRunOnStartup.IsChecked ?? false;
            var success = AppSettingsService.SetRunOnStartup(isChecked);

            if (!success)
            {
                // Revert the checkbox if we couldn't change the setting
                ChkRunOnStartup.IsChecked = !isChecked;
                StatusText.Text = LocalizationService.Instance.GetString("Status_StartupFailed");
            }
            else
            {
                _appSettings.RunOnStartup = isChecked;
                StatusText.Text = isChecked 
                    ? LocalizationService.Instance.GetString("Status_AddedToStartup")
                    : LocalizationService.Instance.GetString("Status_RemovedFromStartup");
            }
        }

        private void BtnApplyShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingKey == Key.None)
            {
                TxtShortcutStatus.Text = LocalizationService.Instance.GetString("Status_SetValidHotkey");
                TxtShortcutStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(255, 102, 102));
                return;
            }

            // Format the hotkey string for storage
            var hotkeyString = HotkeyService.FormatHotkey(_pendingModifiers, _pendingKey);
            
            // Try to apply the hotkey immediately
            var success = App.Current.UpdateHotkey(hotkeyString);
            
            if (success)
            {
                _appSettings.OverlayShortcut = hotkeyString;
                TxtShortcutStatus.Text = $"{LocalizationService.Instance.GetString("Status_HotkeyApplied")} {hotkeyString}";
                TxtShortcutStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(30, 183, 95));
                StatusText.Text = LocalizationService.Instance.GetString("Status_HotkeyUpdated");
            }
            else
            {
                TxtShortcutStatus.Text = LocalizationService.Instance.GetString("Status_HotkeyRegisterFailed");
                TxtShortcutStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(255, 102, 102));
                StatusText.Text = LocalizationService.Instance.GetString("Status_HotkeyFailed");
            }
        }

        private void BtnCheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = LocalizationService.Instance.GetString("Status_CheckingUpdates");
            // TODO: Implement actual update check
            StatusText.Text = LocalizationService.Instance.GetString("Status_LatestVersion");
        }

        private void BtnRefreshPlugins_Click(object sender, RoutedEventArgs e)
        {
            RefreshPluginsList();
            StatusText.Text = LocalizationService.Instance.GetString("Status_FoundPlugins", Plugins.Count);
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
                StatusText.Text = $"{LocalizationService.Instance.GetString("Status_CouldNotOpenFolder")} {ex.Message}";
            }
        }

        private async void BtnBackupNow_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = LocalizationService.Instance.GetString("Status_CreatingBackup");
            
            var success = await AppSettingsService.CreateBackupAsync();
            
            if (success)
            {
                if (int.TryParse(TxtMaxBackups.Text, out var maxBackups))
                {
                    await AppSettingsService.CleanupOldBackupsAsync(maxBackups);
                }
                
                RefreshBackupsList();
                StatusText.Text = LocalizationService.Instance.GetString("Status_BackupCreated");
            }
            else
            {
                StatusText.Text = LocalizationService.Instance.GetString("Status_BackupFailed");
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
                StatusText.Text = $"{LocalizationService.Instance.GetString("Status_CouldNotOpenFolder")} {ex.Message}";
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
            // Collect settings from UI - hotkey is already stored in _appSettings when applied
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

            StatusText.Text = LocalizationService.Instance.GetString("Status_SettingsSaved");
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
