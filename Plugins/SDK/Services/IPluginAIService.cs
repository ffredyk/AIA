using System;
using System.Collections.Generic;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Service for plugins to register AI tools
    /// </summary>
    public interface IPluginAIService
    {
        /// <summary>
        /// Registers a custom AI tool from a plugin
        /// </summary>
        /// <param name="tool">The tool definition</param>
        void RegisterTool(PluginAITool tool);

        /// <summary>
        /// Unregisters a previously registered tool
        /// </summary>
        /// <param name="toolName">The name of the tool to unregister</param>
        void UnregisterTool(string toolName);

        /// <summary>
        /// Gets all tools registered by a specific plugin
        /// </summary>
        /// <param name="pluginId">The plugin identifier</param>
        IEnumerable<PluginAITool> GetToolsByPlugin(string pluginId);
    }

    /// <summary>
    /// Definition for a plugin-provided AI tool
    /// </summary>
    public class PluginAITool
    {
        /// <summary>
        /// Unique name for the tool (should be prefixed with plugin name, e.g., "outlook_get_emails")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable description of what the tool does
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The plugin ID that registered this tool
        /// </summary>
        public string PluginId { get; set; } = string.Empty;

        /// <summary>
        /// Tool parameters
        /// </summary>
        public Dictionary<string, PluginAIToolParameter> Parameters { get; set; } = new();

        /// <summary>
        /// Handler function that executes the tool and returns a JSON result
        /// </summary>
        public Func<Dictionary<string, object>, string>? Handler { get; set; }
    }

    /// <summary>
    /// Parameter definition for a plugin AI tool
    /// </summary>
    public class PluginAIToolParameter
    {
        /// <summary>
        /// Parameter type: "string", "integer", "boolean", "number"
        /// </summary>
        public string Type { get; set; } = "string";

        /// <summary>
        /// Description of the parameter
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Whether this parameter is required
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Optional list of allowed values (for enum-like parameters)
        /// </summary>
        public string[]? AllowedValues { get; set; }
    }
}
