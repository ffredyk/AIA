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
            var allSettings = await AppSettingsService.LoadPluginInstanceSettingsAsync();
            _settings = allSettings.FirstOrDefault(s => s.PluginId == _plugin.Id);

            if (_settings == null)
            {
                _settings = new PluginInstanceSettings
                {
                    PluginId = _plugin.Id,
                    IsEnabled = _plugin.IsEnabled
                };
            }

            // Apply settings to UI
            ChkUIAccess.IsChecked = _settings.HasUIAccess;
            ChkNotificationAccess.IsChecked = _settings.HasNotificationAccess;
            ChkNetworkAccess.IsChecked = _settings.HasNetworkAccess;
            ChkFileSystemAccess.IsChecked = _settings.HasFileSystemAccess;

            // Show requested permissions if available
            // This would need integration with the plugin's RequiredPermissions
            // For now, we show all permissions as configurable
        }

        private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            ChkUIAccess.IsChecked = true;
            ChkNotificationAccess.IsChecked = true;
            ChkNetworkAccess.IsChecked = false;
            ChkFileSystemAccess.IsChecked = false;
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;

            // Update settings from UI
            _settings.HasUIAccess = ChkUIAccess.IsChecked ?? true;
            _settings.HasNotificationAccess = ChkNotificationAccess.IsChecked ?? true;
            _settings.HasNetworkAccess = ChkNetworkAccess.IsChecked ?? false;
            _settings.HasFileSystemAccess = ChkFileSystemAccess.IsChecked ?? false;

            // Load all settings, update this one, and save
            var allSettings = await AppSettingsService.LoadPluginInstanceSettingsAsync();
            var existing = allSettings.FirstOrDefault(s => s.PluginId == _plugin.Id);
            
            if (existing != null)
            {
                allSettings.Remove(existing);
            }
            
            allSettings.Add(_settings);
            await AppSettingsService.SavePluginInstanceSettingsAsync(allSettings);

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
