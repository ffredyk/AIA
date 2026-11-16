using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AIA.Models;
using AIA.Services;

namespace AIA
{
    public partial class PluginPermissionsWindow : Window
    {
        private readonly PluginDisplayInfo _plugin;
        private PluginInstanceSettings? _settings;

        public PluginPermissionsWindow(PluginDisplayInfo plugin)
        {
            _plugin = plugin;
            InitializeComponent();
            
            Loaded += PluginPermissionsWindow_Loaded;
        }

        private async void PluginPermissionsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TxtPluginName.Text = _plugin.Name;

            // Load existing settings for this plugin
            var manager = App.Current.PluginManager;
            if (manager != null)
            {
                _settings = manager.GetInstanceSettings(_plugin.Id);
            }
            else
            {
                var allSettings = await AppSettingsService.LoadPluginInstanceSettingsAsync();
                _settings = allSettings.FirstOrDefault(s => s.PluginId == _plugin.Id)
                            ?? new PluginInstanceSettings { PluginId = _plugin.Id, IsEnabled = _plugin.IsEnabled };
            }

            // Apply settings to UI
            ChkUIAccess.IsChecked = _settings.HasUIAccess;
            ChkNotificationAccess.IsChecked = _settings.HasNotificationAccess;
            ChkNetworkAccess.IsChecked = _settings.HasNetworkAccess;
            ChkFileSystemAccess.IsChecked = _settings.HasFileSystemAccess;
            ChkComAccess.IsChecked = _settings.HasComAccess;

            // Show permissions the plugin declares in its manifest
            if (_plugin.RequiredPermissions != Plugins.SDK.PluginPermissions.None)
            {
                RequestedPermissionsPanel.Visibility = Visibility.Visible;
                TxtRequestedPermissions.Text = _plugin.RequiredPermissions.ToString();
            }
        }

        private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            ChkUIAccess.IsChecked = true;
            ChkNotificationAccess.IsChecked = true;
            ChkNetworkAccess.IsChecked = false;
            ChkFileSystemAccess.IsChecked = false;
            ChkComAccess.IsChecked = false;
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;

            // Update settings from UI
            _settings.HasUIAccess = ChkUIAccess.IsChecked ?? true;
            _settings.HasNotificationAccess = ChkNotificationAccess.IsChecked ?? true;
            _settings.HasNetworkAccess = ChkNetworkAccess.IsChecked ?? false;
            _settings.HasFileSystemAccess = ChkFileSystemAccess.IsChecked ?? false;
            _settings.HasComAccess = ChkComAccess.IsChecked ?? false;

            try
            {
                var manager = App.Current.PluginManager;
                if (manager != null)
                {
                    await manager.SaveInstanceSettingsAsync(_settings);
                }
                else
                {
                    var allSettings = await AppSettingsService.LoadPluginInstanceSettingsAsync();
                    allSettings.RemoveAll(s => s.PluginId == _plugin.Id);
                    allSettings.Add(_settings);
                    await AppSettingsService.SavePluginInstanceSettingsAsync(allSettings);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    string.Format(Services.LocalizationService.Instance.GetString("Status_SettingsSaveFailed"), ex.Message),
                    Services.LocalizationService.Instance.GetString("Common_Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
