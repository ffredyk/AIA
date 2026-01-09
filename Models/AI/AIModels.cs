using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AIA.Models.AI
{
    /// <summary>
    /// Represents a tool that the AI can call to access application data
    /// </summary>
    public class AITool
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, AIToolParameter> Parameters { get; set; } = new();
        public Func<Dictionary<string, object>, string>? Handler { get; set; }
    }

    /// <summary>
    /// Represents a parameter for an AI tool
    /// </summary>
    public class AIToolParameter
    {
        public string Type { get; set; } = "string";
        public string Description { get; set; } = string.Empty;
        public bool Required { get; set; } = false;
        public string[]? Enum { get; set; }
    }

    /// <summary>
    /// Represents a tool call made by the AI
    /// </summary>
    public class AIToolCall
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, object> Arguments { get; set; } = new();
    }

    /// <summary>
    /// Represents the result of a tool call
    /// </summary>
    public class AIToolResult
    {
        public string ToolCallId { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public bool Success { get; set; } = true;
        public string? Error { get; set; }
    }

    /// <summary>
    /// Represents a message in the AI conversation
    /// </summary>
    public class AIMessage
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
        public List<AIToolCall>? ToolCalls { get; set; }
        public string? ToolCallId { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>
    /// Request configuration for AI generation
    /// </summary>
    public class AIRequest
    {
        public List<AIMessage> Messages { get; set; } = new();
        public List<AITool>? Tools { get; set; }
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 4096;
        public string? SystemPrompt { get; set; }
        public bool StreamResponse { get; set; } = false;
    }

    /// <summary>
    /// Response from AI generation
    /// </summary>
    public class AIResponse
    {
        public string Content { get; set; } = string.Empty;
        public List<AIToolCall>? ToolCalls { get; set; }
        public bool RequiresToolExecution => ToolCalls != null && ToolCalls.Count > 0;
        public string? FinishReason { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public string? Error { get; set; }
        public bool Success => string.IsNullOrEmpty(Error);
        public AIProvider? UsedProvider { get; set; }
    }

    /// <summary>
    /// Orchestration settings for AI routing
    /// </summary>
    public class AIOrchestrationSettings : INotifyPropertyChanged
    {
        private bool _enableAutoRouting = true;
        private bool _enableToolUse = true;
        private bool _includeTasksContext = true;
        private bool _includeRemindersContext = true;
        private bool _includeDataBankContext = true;
        private bool _includeScreenshotsContext = false;
        private int _maxContextItems = 10;
        private double _defaultTemperature = 0.7;
        private int _defaultMaxTokens = 4096;
        
        // Auto-naming settings
        private bool _enableAutoNaming = true;
        private Guid? _autoNamingProviderId;
        private double _autoNamingTemperature = 0.3;
        private int _autoNamingMaxTokens = 30;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Whether to automatically route prompts to the best AI provider
        /// </summary>
        public bool EnableAutoRouting
        {
            get => _enableAutoRouting;
            set { _enableAutoRouting = value; OnPropertyChanged(nameof(EnableAutoRouting)); }
        }

        /// <summary>
        /// Whether to enable tool use (MCP-like functionality)
        /// </summary>
        public bool EnableToolUse
        {
            get => _enableToolUse;
            set { _enableToolUse = value; OnPropertyChanged(nameof(EnableToolUse)); }
        }

        /// <summary>
        /// Whether to include tasks in the AI context
        /// </summary>
        public bool IncludeTasksContext
        {
            get => _includeTasksContext;
            set { _includeTasksContext = value; OnPropertyChanged(nameof(IncludeTasksContext)); }
        }

        /// <summary>
        /// Whether to include reminders in the AI context
        /// </summary>
        public bool IncludeRemindersContext
        {
            get => _includeRemindersContext;
            set { _includeRemindersContext = value; OnPropertyChanged(nameof(IncludeRemindersContext)); }
        }

        /// <summary>
        /// Whether to include data bank entries in the AI context
        /// </summary>
        public bool IncludeDataBankContext
        {
            get => _includeDataBankContext;
            set { _includeDataBankContext = value; OnPropertyChanged(nameof(IncludeDataBankContext)); }
        }

        /// <summary>
        /// Whether to include current screenshots in the AI context
        /// </summary>
        public bool IncludeScreenshotsContext
        {
            get => _includeScreenshotsContext;
            set { _includeScreenshotsContext = value; OnPropertyChanged(nameof(IncludeScreenshotsContext)); }
        }

        /// <summary>
        /// Maximum number of context items to include per category
        /// </summary>
        public int MaxContextItems
        {
            get => _maxContextItems;
            set { _maxContextItems = value; OnPropertyChanged(nameof(MaxContextItems)); }
        }

        /// <summary>
        /// Default temperature for AI generation
        /// </summary>
        public double DefaultTemperature
        {
            get => _defaultTemperature;
            set { _defaultTemperature = value; OnPropertyChanged(nameof(DefaultTemperature)); }
        }

        /// <summary>
        /// Default max tokens for AI generation
        /// </summary>
        public int DefaultMaxTokens
        {
            get => _defaultMaxTokens;
            set { _defaultMaxTokens = value; OnPropertyChanged(nameof(DefaultMaxTokens)); }
        }

        /// <summary>
        /// Whether to automatically generate chat titles after first message
        /// </summary>
        public bool EnableAutoNaming
        {
            get => _enableAutoNaming;
            set { _enableAutoNaming = value; OnPropertyChanged(nameof(EnableAutoNaming)); }
        }

        /// <summary>
        /// Provider ID to use for auto-naming (null = use first available)
        /// </summary>
        public Guid? AutoNamingProviderId
        {
            get => _autoNamingProviderId;
            set { _autoNamingProviderId = value; OnPropertyChanged(nameof(AutoNamingProviderId)); }
        }

        /// <summary>
        /// Temperature for auto-naming (lower = more consistent names)
        /// </summary>
        public double AutoNamingTemperature
        {
            get => _autoNamingTemperature;
            set { _autoNamingTemperature = value; OnPropertyChanged(nameof(AutoNamingTemperature)); }
        }

        /// <summary>
        /// Max tokens for auto-naming responses
        /// </summary>
        public int AutoNamingMaxTokens
        {
            get => _autoNamingMaxTokens;
            set { _autoNamingMaxTokens = value; OnPropertyChanged(nameof(AutoNamingMaxTokens)); }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Categories for automatic routing
    /// </summary>
    public static class AIRouterCategories
    {
        public const string Coding = "coding";
        public const string Creative = "creative";
        public const string Analysis = "analysis";
        public const string Math = "math";
        public const string Research = "research";
        public const string Conversation = "conversation";
        public const string TaskManagement = "task-management";
        public const string Summarization = "summarization";
    }
}
