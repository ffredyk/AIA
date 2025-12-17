using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Service for plugin UI integration
    /// </summary>
    public interface IPluginUIService
    {
        /// <summary>
        /// Registers a new tab to be displayed in the main overlay
        /// </summary>
        /// <param name="tabDefinition">The tab definition</param>
        void RegisterTab(PluginTabDefinition tabDefinition);

        /// <summary>
        /// Unregisters a tab
        /// </summary>
        void UnregisterTab(string tabId);

        /// <summary>
        /// Registers a toolbar button
        /// </summary>
        void RegisterToolbarButton(PluginToolbarButton button);

        /// <summary>
        /// Unregisters a toolbar button
        /// </summary>
        void UnregisterToolbarButton(string buttonId);

        /// <summary>
        /// Shows a toast notification
        /// </summary>
        void ShowToast(string message, ToastType type = ToastType.Info);

        /// <summary>
        /// Shows a dialog with custom content
        /// </summary>
        bool? ShowDialog(string title, object content, DialogButtons buttons = DialogButtons.OkCancel);

        /// <summary>
        /// Refreshes plugin UI elements
        /// </summary>
        void RefreshUI();
    }

    /// <summary>
    /// Definition for a plugin tab
    /// </summary>
    public class PluginTabDefinition
    {
        /// <summary>
        /// Unique identifier for the tab
        /// </summary>
        public string TabId { get; set; } = string.Empty;

        /// <summary>
        /// Display title for the tab
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Icon symbol name (WPF UI SymbolRegular)
        /// </summary>
        public string IconSymbol { get; set; } = "Document20";

        /// <summary>
        /// Order priority for tab placement (lower = earlier)
        /// </summary>
        public int Order { get; set; } = 100;

        /// <summary>
        /// View model for the tab content
        /// </summary>
        public IPluginTabViewModel ViewModel { get; set; } = null!;

        /// <summary>
        /// Optional badge count (e.g., for notifications)
        /// </summary>
        public int BadgeCount { get; set; }

        /// <summary>
        /// Badge background color (hex)
        /// </summary>
        public string BadgeColor { get; set; } = "#33FF4444";
    }

    /// <summary>
    /// Base interface for plugin tab view models
    /// </summary>
    public interface IPluginTabViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// Gets the data template for rendering this view model
        /// </summary>
        DataTemplate GetDataTemplate();

        /// <summary>
        /// Called when the tab becomes active
        /// </summary>
        void OnActivated();

        /// <summary>
        /// Called when the tab becomes inactive
        /// </summary>
        void OnDeactivated();

        /// <summary>
        /// Refreshes the tab content
        /// </summary>
        void Refresh();
    }

    /// <summary>
    /// Definition for a plugin toolbar button
    /// </summary>
    public class PluginToolbarButton
    {
        /// <summary>
        /// Unique identifier for the button
        /// </summary>
        public string ButtonId { get; set; } = string.Empty;

        /// <summary>
        /// Display text for the button
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Icon symbol name
        /// </summary>
        public string IconSymbol { get; set; } = "Add20";

        /// <summary>
        /// Tooltip text
        /// </summary>
        public string? Tooltip { get; set; }

        /// <summary>
        /// Order priority for button placement
        /// </summary>
        public int Order { get; set; } = 100;

        /// <summary>
        /// Action to execute when clicked
        /// </summary>
        public Action OnClick { get; set; } = () => { };

        /// <summary>
        /// Function to determine if button is enabled
        /// </summary>
        public Func<bool>? IsEnabled { get; set; }
    }

    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error
    }

    [Flags]
    public enum DialogButtons
    {
        None = 0,
        Ok = 1,
        Cancel = 2,
        Yes = 4,
        No = 8,
        OkCancel = Ok | Cancel,
        YesNo = Yes | No,
        YesNoCancel = Yes | No | Cancel
    }
}
