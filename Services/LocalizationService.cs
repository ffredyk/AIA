using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace AIA.Services
{
    /// <summary>
    /// Service for managing application localization and translations
    /// </summary>
    public class LocalizationService : INotifyPropertyChanged
    {
        private static LocalizationService? _instance;
        private static readonly object _lock = new();
        private readonly ResourceManager _resourceManager;
        private CultureInfo _currentCulture;

        /// <summary>
        /// Supported languages with their culture codes
        /// </summary>
        public static readonly (string Code, string DisplayName)[] SupportedLanguages =
        [
            ("en", "English"),
            ("cs-CZ", "Èeština"),
            ("de-DE", "Deutsch")
        ];

        /// <summary>
        /// Gets the singleton instance of the LocalizationService
        /// </summary>
        public static LocalizationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new LocalizationService();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Event raised when the current culture changes
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Event raised when the language changes
        /// </summary>
        public event EventHandler? LanguageChanged;

        /// <summary>
        /// Gets or sets the current culture
        /// </summary>
        public CultureInfo CurrentCulture
        {
            get => _currentCulture;
            private set
            {
                if (_currentCulture?.Name != value?.Name)
                {
                    _currentCulture = value ?? CultureInfo.InvariantCulture;
                    Thread.CurrentThread.CurrentUICulture = _currentCulture;
                    Thread.CurrentThread.CurrentCulture = _currentCulture;
                    
                    OnPropertyChanged(nameof(CurrentCulture));
                    OnPropertyChanged(nameof(CurrentLanguageCode));
                    OnPropertyChanged("Item[]"); // Notify all indexer bindings to refresh
                    LanguageChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Gets the current language code (e.g., "en", "cs-CZ", "de-DE")
        /// </summary>
        public string CurrentLanguageCode => _currentCulture.Name switch
        {
            "cs-CZ" or "cs" => "cs-CZ",
            "de-DE" or "de" => "de-DE",
            _ => "en"
        };

        private LocalizationService()
        {
            _resourceManager = new ResourceManager("AIA.Resources.Strings", typeof(LocalizationService).Assembly);
            _currentCulture = CultureInfo.CurrentUICulture;
            
            // Initialize with system language if supported, otherwise fall back to English
            InitializeWithSystemLanguage();
        }

        /// <summary>
        /// Initialize the localization service with the system language
        /// </summary>
        private void InitializeWithSystemLanguage()
        {
            var systemCulture = CultureInfo.CurrentUICulture;
            var cultureName = systemCulture.Name;
            var twoLetterCode = systemCulture.TwoLetterISOLanguageName;
            
            // Check if the system language is supported (exact match or two-letter code)
            bool isSupported = false;
            string matchedCode = "en";
            
            foreach (var lang in SupportedLanguages)
            {
                if (lang.Code.Equals(cultureName, StringComparison.OrdinalIgnoreCase) ||
                    lang.Code.StartsWith(twoLetterCode, StringComparison.OrdinalIgnoreCase) ||
                    twoLetterCode.Equals(lang.Code.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                {
                    isSupported = true;
                    matchedCode = lang.Code;
                    break;
                }
            }
            
            if (isSupported)
            {
                _currentCulture = new CultureInfo(matchedCode);
            }
            else
            {
                // Fall back to English
                _currentCulture = new CultureInfo("en");
            }
            
            Thread.CurrentThread.CurrentUICulture = _currentCulture;
            Thread.CurrentThread.CurrentCulture = _currentCulture;
            
            System.Diagnostics.Debug.WriteLine($"Localization initialized with culture: {_currentCulture.Name} (System: {systemCulture.Name})");
        }

        /// <summary>
        /// Initialize with a saved language preference
        /// </summary>
        /// <param name="languageCode">The saved language code</param>
        public void InitializeWithSavedLanguage(string? languageCode)
        {
            if (!string.IsNullOrEmpty(languageCode))
            {
                SetLanguage(languageCode);
            }
        }

        /// <summary>
        /// Gets a localized string by key
        /// </summary>
        /// <param name="key">The resource key</param>
        /// <returns>The localized string, or the key if not found</returns>
        public string GetString(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;
                
            try
            {
                var value = _resourceManager.GetString(key, _currentCulture);
                return value ?? key;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Localization error for key '{key}': {ex.Message}");
                return key;
            }
        }

        /// <summary>
        /// Gets a localized string by key with format arguments
        /// </summary>
        /// <param name="key">The resource key</param>
        /// <param name="args">Format arguments</param>
        /// <returns>The formatted localized string</returns>
        public string GetString(string key, params object[] args)
        {
            var format = GetString(key);
            try
            {
                return string.Format(_currentCulture, format, args);
            }
            catch
            {
                return format;
            }
        }

        /// <summary>
        /// Indexer to get localized strings (used by XAML binding)
        /// </summary>
        public string this[string key] => GetString(key);

        /// <summary>
        /// Changes the current language
        /// </summary>
        /// <param name="languageCode">The language code (e.g., "en", "cs-CZ", "de-DE")</param>
        public void SetLanguage(string languageCode)
        {
            try
            {
                // Normalize the language code
                var normalizedCode = languageCode switch
                {
                    "cs" or "cs-CZ" => "cs-CZ",
                    "de" or "de-DE" => "de-DE",
                    _ => "en"
                };
                
                var culture = new CultureInfo(normalizedCode);
                CurrentCulture = culture;
                System.Diagnostics.Debug.WriteLine($"Language changed to: {culture.DisplayName} ({culture.Name})");
            }
            catch (CultureNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Culture not found: {languageCode}, falling back to English. Error: {ex.Message}");
                CurrentCulture = new CultureInfo("en");
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
