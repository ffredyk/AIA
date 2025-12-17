using System.ComponentModel;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Base interface for plugin settings view models
    /// </summary>
    public interface IPluginSettingsViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// Display name for the settings section
        /// </summary>
        string SettingsTitle { get; }

        /// <summary>
        /// Description of the settings
        /// </summary>
        string SettingsDescription { get; }

        /// <summary>
        /// Loads settings from storage
        /// </summary>
        void Load();

        /// <summary>
        /// Saves settings to storage
        /// </summary>
        void Save();

        /// <summary>
        /// Resets settings to defaults
        /// </summary>
        void Reset();

        /// <summary>
        /// Whether settings have been modified
        /// </summary>
        bool IsDirty { get; }
    }
}
