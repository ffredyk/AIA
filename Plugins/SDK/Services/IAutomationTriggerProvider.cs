using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Interface for plugins that provide automation triggers
    /// </summary>
    public interface IAutomationTriggerProvider
    {
        /// <summary>
        /// Gets the available trigger definitions from this plugin
        /// </summary>
        IEnumerable<PluginTriggerDefinition> GetTriggerDefinitions();

        /// <summary>
        /// Called when a trigger is registered/enabled
        /// </summary>
        Task OnTriggerRegisteredAsync(string triggerId, Dictionary<string, object> configuration);

        /// <summary>
        /// Called when a trigger is unregistered/disabled
        /// </summary>
        Task OnTriggerUnregisteredAsync(string triggerId);

        /// <summary>
        /// Event raised when a trigger fires
        /// </summary>
        event EventHandler<PluginTriggerEventArgs>? TriggerFired;
    }

    /// <summary>
    /// Defines a trigger that a plugin provides
    /// </summary>
    public class PluginTriggerDefinition
    {
        /// <summary>
        /// Unique ID within the plugin
        /// </summary>
        public string TriggerId { get; set; } = string.Empty;

        /// <summary>
        /// Display name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of what triggers this
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Icon symbol for UI
        /// </summary>
        public string IconSymbol { get; set; } = "Lightning20";

        /// <summary>
        /// Configuration parameters for this trigger
        /// </summary>
        public List<PluginTriggerParameter> Parameters { get; set; } = new();

        /// <summary>
        /// Variables that will be provided in the trigger context
        /// </summary>
        public List<PluginTriggerVariable> ProvidedVariables { get; set; } = new();
    }

    /// <summary>
    /// A configuration parameter for a plugin trigger
    /// </summary>
    public class PluginTriggerParameter
    {
        /// <summary>
        /// Parameter key
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Display name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Parameter type (string, int, bool, enum)
        /// </summary>
        public string Type { get; set; } = "string";

        /// <summary>
        /// Whether this parameter is required
        /// </summary>
        public bool IsRequired { get; set; }

        /// <summary>
        /// Default value
        /// </summary>
        public object? DefaultValue { get; set; }

        /// <summary>
        /// Allowed values for enum types
        /// </summary>
        public List<string>? AllowedValues { get; set; }
    }

    /// <summary>
    /// A variable that a trigger provides
    /// </summary>
    public class PluginTriggerVariable
    {
        /// <summary>
        /// Variable name (used in templates as {{variable_name}})
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of what this variable contains
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Type of the variable value
        /// </summary>
        public string Type { get; set; } = "string";
    }

    /// <summary>
    /// Event args for when a plugin trigger fires
    /// </summary>
    public class PluginTriggerEventArgs : EventArgs
    {
        /// <summary>
        /// The trigger ID that fired
        /// </summary>
        public string TriggerId { get; set; } = string.Empty;

        /// <summary>
        /// Data provided by the trigger
        /// </summary>
        public Dictionary<string, object> Data { get; set; } = new();

        /// <summary>
        /// When the trigger fired
        /// </summary>
        public DateTime FiredAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Optional description of what triggered this
        /// </summary>
        public string? Description { get; set; }
    }
}
