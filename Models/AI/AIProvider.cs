using System;
using System.ComponentModel;

namespace AIA.Models.AI
{
    /// <summary>
    /// Supported AI provider types
    /// </summary>
    public enum AIProviderType
    {
        OpenAI,
        AzureOpenAI,
        Google,
        Anthropic
    }

    /// <summary>
    /// Represents an AI provider configuration
    /// </summary>
    public class AIProvider : INotifyPropertyChanged
    {
        private Guid _id = Guid.NewGuid();
        private string _name = string.Empty;
        private AIProviderType _providerType;
        private string _apiKey = string.Empty;
        private string _endpoint = string.Empty;
        private string _modelId = string.Empty;
        private string _deploymentName = string.Empty;
        private bool _isEnabled = true;
        private bool _isDefault;
        private int _priority = 50;
        private string[] _strengths = Array.Empty<string>();
        private double _costPerMillionTokens;
        private int _maxContextTokens = 128000;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public AIProviderType ProviderType
        {
            get => _providerType;
            set 
            { 
                _providerType = value; 
                OnPropertyChanged(nameof(ProviderType));
                OnPropertyChanged(nameof(ProviderIcon));
                OnPropertyChanged(nameof(ProviderColor));
            }
        }

        public string ApiKey
        {
            get => _apiKey;
            set { _apiKey = value; OnPropertyChanged(nameof(ApiKey)); }
        }

        public string Endpoint
        {
            get => _endpoint;
            set { _endpoint = value; OnPropertyChanged(nameof(Endpoint)); }
        }

        public string ModelId
        {
            get => _modelId;
            set { _modelId = value; OnPropertyChanged(nameof(ModelId)); }
        }

        /// <summary>
        /// Azure-specific deployment name
        /// </summary>
        public string DeploymentName
        {
            get => _deploymentName;
            set { _deploymentName = value; OnPropertyChanged(nameof(DeploymentName)); }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        public bool IsDefault
        {
            get => _isDefault;
            set { _isDefault = value; OnPropertyChanged(nameof(IsDefault)); }
        }

        /// <summary>
        /// Priority for routing (higher = preferred)
        /// </summary>
        public int Priority
        {
            get => _priority;
            set { _priority = value; OnPropertyChanged(nameof(Priority)); }
        }

        /// <summary>
        /// Categories this provider excels at (e.g., "coding", "creative", "analysis", "math")
        /// </summary>
        public string[] Strengths
        {
            get => _strengths;
            set { _strengths = value; OnPropertyChanged(nameof(Strengths)); OnPropertyChanged(nameof(StrengthsText)); }
        }

        public string StrengthsText
        {
            get => string.Join(", ", _strengths);
            set { _strengths = value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>(); OnPropertyChanged(nameof(Strengths)); OnPropertyChanged(nameof(StrengthsText)); }
        }

        public double CostPerMillionTokens
        {
            get => _costPerMillionTokens;
            set { _costPerMillionTokens = value; OnPropertyChanged(nameof(CostPerMillionTokens)); }
        }

        public int MaxContextTokens
        {
            get => _maxContextTokens;
            set { _maxContextTokens = value; OnPropertyChanged(nameof(MaxContextTokens)); }
        }

        public string ProviderIcon => ProviderType switch
        {
            AIProviderType.OpenAI => "Bot20",
            AIProviderType.AzureOpenAI => "Cloud20",
            AIProviderType.Google => "Globe20",
            AIProviderType.Anthropic => "Brain20",
            _ => "Bot20"
        };

        public string ProviderColor => ProviderType switch
        {
            AIProviderType.OpenAI => "#10A37F",
            AIProviderType.AzureOpenAI => "#0078D4",
            AIProviderType.Google => "#4285F4",
            AIProviderType.Anthropic => "#D97706",
            _ => "#808080"
        };

        public bool RequiresEndpoint => ProviderType == AIProviderType.AzureOpenAI;

        public bool RequiresDeploymentName => ProviderType == AIProviderType.AzureOpenAI;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
