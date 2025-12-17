using System;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Attribute to mark a class as an AIA plugin
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class PluginAttribute : Attribute
    {
        /// <summary>
        /// Unique identifier for the plugin
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Display name of the plugin
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Optional icon symbol name (WPF UI symbol)
        /// </summary>
        public string? IconSymbol { get; set; }

        /// <summary>
        /// Whether this is a built-in plugin that ships with the application
        /// </summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>
        /// Creates a new plugin attribute
        /// </summary>
        /// <param name="id">Unique identifier for the plugin</param>
        /// <param name="name">Display name of the plugin</param>
        public PluginAttribute(string id, string name)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }

    /// <summary>
    /// Attribute to mark a method as a plugin service that can be consumed by other plugins
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false)]
    public class PluginServiceAttribute : Attribute
    {
        /// <summary>
        /// Name of the service
        /// </summary>
        public string ServiceName { get; }

        /// <summary>
        /// Description of the service
        /// </summary>
        public string? Description { get; set; }

        public PluginServiceAttribute(string serviceName)
        {
            ServiceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        }
    }
}
