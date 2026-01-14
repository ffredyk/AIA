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
        private ModifierKeys _pendingModifiers = ModifierKeys.Alt;
        private Key _pendingKey = Key.Q;

        private const string DefaultHotkeyString = "Alt+Q";

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

            // Load chat templates list
            RefreshChatTemplatesList();

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
            
            // Overlay opacity
            SldrOverlayOpacity.Value = _appSettings.OverlayOpacity;
            UpdateOpacityDisplay();
            
            // Clipboard history settings
            ChkEnableClipboardHistory.IsChecked = _appSettings.EnableClipboardHistory;
            ChkTrackClipboardText.IsChecked = _appSettings.TrackClipboardText;
            ChkTrackClipboardImages.IsChecked = _appSettings.TrackClipboardImages;
            ChkTrackClipboardFiles.IsChecked = _appSettings.TrackClipboardFiles;
            TxtMaxClipboardItems.Text = _appSettings.MaxClipboardHistoryItems.ToString();
            TxtMaxClipboardItemSize.Text = _appSettings.MaxClipboardItemSizeKb.ToString();

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

        private void RefreshChatTemplatesList()
        {
            var templates = OverlayViewModel.Singleton.ChatMessageTemplates;
            ChatTemplatesControl.ItemsSource = templates;
            ChatTemplatesEmptyState.Visibility = templates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        #region Hotkey Capture

        private void UpdateHotkeyDisplay()
        {
            if (_pendingKey == Key.None && _pendingModifiers == ModifierKeys.None)
            {
                HotkeyTextBox.Text = LocalizationService.Instance.GetString("Settings_NoHotkeySet");
                HotkeyTextBox.FontStyle = FontStyles.Italic;
                HotkeyTextBox.Foreground = new SolidColorBrush(WpfColor.FromRgb(0x66, 0x66, 0x66));
            }
            else
            {
                var displayText = HotkeyService.FormatHotkey(_pendingModifiers, _pendingKey);
                HotkeyTextBox.Text = displayText;
                HotkeyTextBox.FontStyle = FontStyles.Normal;
                HotkeyTextBox.Foreground = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xFF, 0xFF));
            }
        }

        private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Start capturing hotkey
            _isCapturingHotkey = true;
            _capturedModifiers = ModifierKeys.None;
            _capturedKey = Key.None;

            // Temporarily unregister the global hotkey to prevent overlay from showing
            App.Current.HotkeyService?.UnregisterHotkey();

            // Change background to indicate capture mode
            HotkeyTextBox.Background = new SolidColorBrush(WpfColor.FromRgb(0x40, 0x40, 0x45));
            HotkeyTextBox.Text = LocalizationService.Instance.GetString("Settings_PressKeys");
            HotkeyTextBox.FontStyle = FontStyles.Italic;
            HotkeyTextBox.Foreground = new SolidColorBrush(WpfColor.FromRgb(0x66, 0x66, 0x66));
            
            UpdateHotkeyStatus(LocalizationService.Instance.GetString("Status_PressKeysCombination"), false);
        }

        private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _isCapturingHotkey = false;
            
            // Re-register the current hotkey (not the pending one)
            if (_appSettings.OverlayShortcut != null)
            {
                App.Current.HotkeyService?.RegisterHotkey(_appSettings.OverlayShortcut);
            }
            
            // Restore normal background
            HotkeyTextBox.Background = new SolidColorBrush(WpfColor.FromRgb(0x3E, 0x3E, 0x42));
            
            // Restore pending hotkey display
            UpdateHotkeyDisplay();
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (!_isCapturingHotkey) return;

            e.Handled = true;

            // Get the actual key pressed (handle system key for Alt)
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Update modifiers
            _capturedModifiers = HotkeyService.GetModifiersFromKeyboard();

            // If it's a modifier key, show current modifiers being held
            if (HotkeyService.IsModifierKey(key))
            {
                if (_capturedModifiers != ModifierKeys.None)
                {
                    HotkeyTextBox.Text = HotkeyService.FormatHotkey(_capturedModifiers, Key.None) + " + ...";
                    HotkeyTextBox.FontStyle = FontStyles.Normal;
                    HotkeyTextBox.Foreground = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xFF, 0xFF));
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
            
            // Show success message but don't apply yet
            var hotkeyString = HotkeyService.FormatHotkey(_pendingModifiers, _pendingKey);
            UpdateHotkeyStatus($"Hotkey set to {hotkeyString}. Click Save to apply.", true);
            
            // Remove focus to exit capture mode
            HotkeyTextBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void HotkeyTextBox_PreviewKeyUp(object sender, WpfKeyEventArgs e)
        {
            if (!_isCapturingHotkey) return;

            e.Handled = true;

            // Update current modifiers
            _capturedModifiers = HotkeyService.GetModifiersFromKeyboard();

            // If no key captured yet and all modifiers released, reset display
            if (_capturedKey == Key.None && _capturedModifiers == ModifierKeys.None)
            {
                HotkeyTextBox.Text = LocalizationService.Instance.GetString("Settings_PressKeys");
                HotkeyTextBox.FontStyle = FontStyles.Italic;
                HotkeyTextBox.Foreground = new SolidColorBrush(WpfColor.FromRgb(0x66, 0x66, 0x66));
            }
        }

        private void BtnClearHotkey_Click(object sender, RoutedEventArgs e)
        {
            _pendingModifiers = ModifierKeys.None;
            _pendingKey = Key.None;
            _isCapturingHotkey = false;
            
            UpdateHotkeyDisplay();
            UpdateHotkeyStatus(LocalizationService.Instance.GetString("Status_HotkeyCleared"), true);
        }

        private void BtnResetHotkey_Click(object sender, RoutedEventArgs e)
        {
            var (modifiers, key) = HotkeyService.ParseHotkeyString(DefaultHotkeyString);
            _pendingModifiers = modifiers;
            _pendingKey = key;
            _isCapturingHotkey = false;
            
            UpdateHotkeyDisplay();
            
            // Show message but don't apply yet
            UpdateHotkeyStatus($"Hotkey reset to {DefaultHotkeyString}. Click Save to apply.", true);
        }

        private void BtnApplyShortcut_Click(object sender, RoutedEventArgs e)
        {
            ApplyHotkey();
        }

        private void ApplyHotkey()
        {
            if (_pendingKey == Key.None)
            {
                UpdateHotkeyStatus(LocalizationService.Instance.GetString("Status_SetValidHotkey"), false, true);
                return;
            }

            // Format the hotkey string for storage
            var hotkeyString = HotkeyService.FormatHotkey(_pendingModifiers, _pendingKey);
            
            // Try to apply the hotkey
            var success = App.Current.UpdateHotkey(hotkeyString);
            
            if (success)
            {
                _appSettings.OverlayShortcut = hotkeyString;
                UpdateHotkeyStatus($"{LocalizationService.Instance.GetString("Status_HotkeyApplied")} {hotkeyString}", true);
            }
            else
            {
                UpdateHotkeyStatus(LocalizationService.Instance.GetString("Status_HotkeyRegisterFailed"), false, true);
            }
        }

        private void UpdateHotkeyStatus(string message, bool success, bool isError = false)
        {
            TxtShortcutStatus.Text = message;
            
            if (isError)
            {
                TxtShortcutStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(255, 102, 102));
            }
            else if (success)
            {
                TxtShortcutStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(30, 183, 95));
            }
            else
            {
                TxtShortcutStatus.Foreground = new SolidColorBrush(WpfColor.FromRgb(0, 120, 212));
            }
        }

        private void UpdateOpacityDisplay()
        {
            var opacityPercent = (int)(SldrOverlayOpacity.Value * 100);
            TxtOpacityValue.Text = $"{opacityPercent}%";
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

        private void SldrOverlayOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateOpacityDisplay();
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
            // Apply hotkey changes first
            ApplyHotkey();
            
            // Collect settings from UI - hotkey is already stored in _appSettings when applied
            _appSettings.MinimizeToTrayOnClose = ChkMinimizeToTray.IsChecked ?? true;
            _appSettings.CheckForUpdatesOnStartup = ChkCheckUpdates.IsChecked ?? true;
            _appSettings.AutoInstallUpdates = ChkAutoInstallUpdates.IsChecked ?? false;
            _appSettings.EnableAutoBackup = ChkAutoBackup.IsChecked ?? false;
            _appSettings.OverlayOpacity = SldrOverlayOpacity.Value;
            
            if (int.TryParse(TxtBackupInterval.Text, out var backupInterval))
                _appSettings.AutoBackupIntervalHours = backupInterval;
            
            if (int.TryParse(TxtMaxBackups.Text, out var maxBackups))
                _appSettings.MaxBackupCount = maxBackups;

            // Clipboard history settings
            _appSettings.EnableClipboardHistory = ChkEnableClipboardHistory.IsChecked ?? true;
            _appSettings.TrackClipboardText = ChkTrackClipboardText.IsChecked ?? true;
            _appSettings.TrackClipboardImages = ChkTrackClipboardImages.IsChecked ?? true;
            _appSettings.TrackClipboardFiles = ChkTrackClipboardFiles.IsChecked ?? true;
            
            if (int.TryParse(TxtMaxClipboardItems.Text, out var maxClipboardItems))
                _appSettings.MaxClipboardHistoryItems = maxClipboardItems;
            
            if (int.TryParse(TxtMaxClipboardItemSize.Text, out var maxClipboardSize))
                _appSettings.MaxClipboardItemSizeKb = maxClipboardSize;

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

            // Refresh the overlay opacity immediately if the main window is open
            if (Owner is MainWindow mainWindow)
            {
                mainWindow.RefreshOpacityFromSettings();
            }

            StatusText.Text = LocalizationService.Instance.GetString("Status_SettingsSaved");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region Chat Templates Management

        private void BtnAddTemplate_Click(object sender, RoutedEventArgs e)
        {
            var editorWindow = new ChatTemplateEditorWindow();
            editorWindow.Owner = this;
            
            if (editorWindow.ShowDialog() == true && editorWindow.Template != null)
            {
                OverlayViewModel.Singleton.AddChatTemplate(editorWindow.Template);
                RefreshChatTemplatesList();
                StatusText.Text = Services.LocalizationService.Instance.GetString("Status_TemplateAdded");
            }
        }

        private void BtnEditTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is ChatMessageTemplate template)
            {
                var editorWindow = new ChatTemplateEditorWindow(template);
                editorWindow.Owner = this;
                
                if (editorWindow.ShowDialog() == true)
                {
                    OverlayViewModel.Singleton.UpdateChatTemplate(template);
                    RefreshChatTemplatesList();
                    StatusText.Text = Services.LocalizationService.Instance.GetString("Status_TemplateUpdated");
                }
            }
        }

        private void BtnDeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is ChatMessageTemplate template)
            {
                var result = System.Windows.MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("ChatTemplate_DeleteConfirm", template.Title),
                    Services.LocalizationService.Instance.GetString("Common_Confirm"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    OverlayViewModel.Singleton.DeleteChatTemplate(template);
                    RefreshChatTemplatesList();
                    StatusText.Text = Services.LocalizationService.Instance.GetString("Status_TemplateDeleted");
                }
            }
        }

        private void BtnMoveTemplateUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is ChatMessageTemplate template)
            {
                var templates = OverlayViewModel.Singleton.ChatMessageTemplates;
                var index = templates.IndexOf(template);
                
                if (index > 0)
                {
                    OverlayViewModel.Singleton.ReorderChatTemplates(template, index - 1);
                    RefreshChatTemplatesList();
                }
            }
        }

        private void BtnMoveTemplateDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is ChatMessageTemplate template)
            {
                var templates = OverlayViewModel.Singleton.ChatMessageTemplates;
                var index = templates.IndexOf(template);
                
                if (index < templates.Count - 1)
                {
                    OverlayViewModel.Singleton.ReorderChatTemplates(template, index + 1);
                    RefreshChatTemplatesList();
                }
            }
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
