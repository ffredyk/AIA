using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Interface for plugins that provide automation actions
    /// </summary>
    public interface IAutomationActionProvider
    {
        /// <summary>
        /// Gets the available action definitions from this plugin
        /// </summary>
        IEnumerable<PluginActionDefinition> GetActionDefinitions();

        /// <summary>
        /// Executes an action
        /// </summary>
        /// <param name="actionId">The action ID to execute</param>
        /// <param name="parameters">Parameters for the action</param>
        /// <param name="context">Context variables from the automation</param>
        /// <returns>Result of the action execution</returns>
        Task<PluginActionResult> ExecuteActionAsync(
            string actionId, 
            Dictionary<string, object> parameters,
            Dictionary<string, object?> context);
    }

    /// <summary>
    /// Defines an action that a plugin provides
    /// </summary>
    public class PluginActionDefinition
    {
        /// <summary>
        /// Unique ID within the plugin
        /// </summary>
        public string ActionId { get; set; } = string.Empty;

        /// <summary>
        /// Display name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of what this action does
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Icon symbol for UI
        /// </summary>
        public string IconSymbol { get; set; } = "Play20";

        /// <summary>
        /// Required permissions for this action
        /// </summary>
        public PluginPermissions RequiredPermissions { get; set; } = PluginPermissions.None;

        /// <summary>
        /// Parameters for this action
        /// </summary>
        public List<PluginActionParameter> Parameters { get; set; } = new();

        /// <summary>
        /// Whether this action can produce significant side effects (show confirmation warning)
        /// </summary>
        public bool HasSideEffects { get; set; }
    }

    /// <summary>
    /// A parameter for a plugin action
    /// </summary>
    public class PluginActionParameter
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
        /// Whether this parameter supports template variables
        /// </summary>
        public bool SupportsVariables { get; set; } = true;

        /// <summary>
        /// Allowed values for enum types
        /// </summary>
        public List<string>? AllowedValues { get; set; }
    }

    /// <summary>
    /// Result of executing a plugin action
    /// </summary>
    public class PluginActionResult
    {
        /// <summary>
        /// Whether the action succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Result message or data
        /// </summary>
        public string? Result { get; set; }

        /// <summary>
        /// Error message if failed
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Additional data from the action
        /// </summary>
        public Dictionary<string, object>? Data { get; set; }

        /// <summary>
        /// Creates a success result
        /// </summary>
        public static PluginActionResult Succeeded(string? result = null, Dictionary<string, object>? data = null)
        {
            return new PluginActionResult { Success = true, Result = result, Data = data };
        }

        /// <summary>
        /// Creates a failure result
        /// </summary>
        public static PluginActionResult Failed(string error)
        {
            return new PluginActionResult { Success = false, Error = error };
        }
    }
}
