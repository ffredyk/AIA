using System;
using AIA.Models;
using AIA.Plugins.Host.Services;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Host
{
    /// <summary>
    /// Implementation of IPluginHostServices that bridges to the application
    /// </summary>
    public class PluginHostServices : IPluginHostServices
    {
        private readonly Func<OverlayViewModel> _viewModelProvider;
        private HostTaskService? _tasks;
        private HostReminderService? _reminders;
        private HostDataBankService? _dataBanks;
        private HostDataAssetService? _dataAssets;
        private HostChatService? _chats;
        private HostPluginUIService? _ui;

        public PluginHostServices(Func<OverlayViewModel> viewModelProvider)
        {
            _viewModelProvider = viewModelProvider ?? throw new ArgumentNullException(nameof(viewModelProvider));
        }

        public ITaskService Tasks => _tasks ??= new HostTaskService(_viewModelProvider);
        public IReminderService Reminders => _reminders ??= new HostReminderService(_viewModelProvider);
        public IDataBankService DataBanks => _dataBanks ??= new HostDataBankService(_viewModelProvider);
        public IDataAssetService DataAssets => _dataAssets ??= new HostDataAssetService(_viewModelProvider);
        public IChatService Chats => _chats ??= new HostChatService(_viewModelProvider);
        public IPluginUIService UI => _ui ??= new HostPluginUIService();

        /// <summary>
        /// Gets the host UI service for direct access
        /// </summary>
        public HostPluginUIService UIService => (HostPluginUIService)UI;
    }
}
