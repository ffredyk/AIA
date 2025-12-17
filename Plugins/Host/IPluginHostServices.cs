using AIA.Plugins.SDK;

namespace AIA.Plugins.Host
{
    /// <summary>
    /// Interface for host services provided to plugins
    /// </summary>
    public interface IPluginHostServices
    {
        /// <summary>
        /// Task service implementation
        /// </summary>
        ITaskService Tasks { get; }

        /// <summary>
        /// Reminder service implementation
        /// </summary>
        IReminderService Reminders { get; }

        /// <summary>
        /// Data bank service implementation
        /// </summary>
        IDataBankService DataBanks { get; }

        /// <summary>
        /// Data asset service implementation
        /// </summary>
        IDataAssetService DataAssets { get; }

        /// <summary>
        /// Chat service implementation
        /// </summary>
        IChatService Chats { get; }

        /// <summary>
        /// UI service implementation
        /// </summary>
        IPluginUIService UI { get; }
    }
}
