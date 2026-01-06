using System;
using System.Windows.Data;
using System.Windows.Markup;
using AIA.Services;

namespace AIA
{
    /// <summary>
    /// Markup extension for binding to localized strings in XAML
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class LocalizeExtension : MarkupExtension
    {
        /// <summary>
        /// Gets or sets the resource key
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a fallback value if the key is not found
        /// </summary>
        public string? Fallback { get; set; }

        public LocalizeExtension()
        {
        }

        public LocalizeExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key))
                return Fallback ?? string.Empty;

            var binding = new System.Windows.Data.Binding($"[{Key}]")
            {
                Source = LocalizationService.Instance,
                Mode = BindingMode.OneWay,
                FallbackValue = Fallback ?? Key
            };

            return binding.ProvideValue(serviceProvider);
        }
    }
}
