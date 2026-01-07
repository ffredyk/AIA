using System;
using System.Collections.Generic;
using System.Linq;
using AIA.Models.AI;
using AIA.Plugins.SDK;
using AIA.Services.AI;

namespace AIA.Plugins.Host.Services
{
    /// <summary>
    /// Host implementation of plugin AI service for tool registration
    /// </summary>
    public class HostPluginAIService : IPluginAIService
    {
        private readonly AIToolsService _toolsService;
        private readonly Dictionary<string, PluginAITool> _registeredTools = new();

        public HostPluginAIService(AIToolsService toolsService)
        {
            _toolsService = toolsService;
        }

        /// <inheritdoc/>
        public void RegisterTool(PluginAITool tool)
        {
            if (tool == null)
                throw new ArgumentNullException(nameof(tool));
            if (string.IsNullOrEmpty(tool.Name))
                throw new ArgumentException("Tool name is required", nameof(tool));
            if (string.IsNullOrEmpty(tool.PluginId))
                throw new ArgumentException("Plugin ID is required", nameof(tool));

            // Convert to internal AITool format
            var aiTool = new AITool
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = ConvertParameters(tool.Parameters),
                Handler = tool.Handler
            };

            // Register with the tools service
            _toolsService.RegisterCustomTool(aiTool);
            _registeredTools[tool.Name] = tool;
        }

        /// <inheritdoc/>
        public void UnregisterTool(string toolName)
        {
            if (_registeredTools.Remove(toolName))
            {
                _toolsService.UnregisterCustomTool(toolName);
            }
        }

        /// <inheritdoc/>
        public IEnumerable<PluginAITool> GetToolsByPlugin(string pluginId)
        {
            return _registeredTools.Values.Where(t => t.PluginId == pluginId);
        }

        /// <summary>
        /// Unregisters all tools from a specific plugin
        /// </summary>
        public void UnregisterAllToolsForPlugin(string pluginId)
        {
            var toolsToRemove = _registeredTools
                .Where(kvp => kvp.Value.PluginId == pluginId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var toolName in toolsToRemove)
            {
                UnregisterTool(toolName);
            }
        }

        private static Dictionary<string, AIToolParameter> ConvertParameters(Dictionary<string, PluginAIToolParameter> pluginParams)
        {
            var result = new Dictionary<string, AIToolParameter>();

            foreach (var kvp in pluginParams)
            {
                result[kvp.Key] = new AIToolParameter
                {
                    Type = kvp.Value.Type,
                    Description = kvp.Value.Description,
                    Required = kvp.Value.Required,
                    Enum = kvp.Value.AllowedValues
                };
            }

            return result;
        }
    }
}
