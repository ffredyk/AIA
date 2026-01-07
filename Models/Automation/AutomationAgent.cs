using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace AIA.Models.Automation
{
    /// <summary>
    /// Represents an agent configuration for automation execution
    /// </summary>
    public class AutomationAgent : INotifyPropertyChanged
    {
        private Guid _id;
        private string _name = string.Empty;
        private string _description = string.Empty;
        private AgentType _agentType = AgentType.SimplePrompt;
        private string _systemPrompt = string.Empty;
        private string _userPromptTemplate = string.Empty;
        private Guid? _preferredProviderId;
        private double _temperature = 0.7;
        private int _maxTokens = 4096;
        private int _maxIterations = 10;
        private int _maxTotalTokens = 100000;
        private bool _enableToolUse = true;
        private ObservableCollection<string> _enabledTools = new();
        private SubAgentMode _subAgentMode = SubAgentMode.WaitForCompletion;
        private string _completionCriteria = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Unique identifier
        /// </summary>
        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        /// <summary>
        /// Agent name
        /// </summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        /// <summary>
        /// Agent description
        /// </summary>
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        /// <summary>
        /// Type of agent behavior
        /// </summary>
        public AgentType AgentType
        {
            get => _agentType;
            set 
            { 
                _agentType = value; 
                OnPropertyChanged(nameof(AgentType));
                OnPropertyChanged(nameof(SupportsIterations));
                OnPropertyChanged(nameof(SupportsSubAgents));
            }
        }

        /// <summary>
        /// System prompt to guide the agent
        /// </summary>
        public string SystemPrompt
        {
            get => _systemPrompt;
            set { _systemPrompt = value; OnPropertyChanged(nameof(SystemPrompt)); }
        }

        /// <summary>
        /// User prompt template with variable placeholders
        /// </summary>
        public string UserPromptTemplate
        {
            get => _userPromptTemplate;
            set { _userPromptTemplate = value; OnPropertyChanged(nameof(UserPromptTemplate)); }
        }

        /// <summary>
        /// Preferred AI provider ID (null = use auto-routing)
        /// </summary>
        public Guid? PreferredProviderId
        {
            get => _preferredProviderId;
            set { _preferredProviderId = value; OnPropertyChanged(nameof(PreferredProviderId)); }
        }

        /// <summary>
        /// Temperature for AI generation
        /// </summary>
        public double Temperature
        {
            get => _temperature;
            set { _temperature = value; OnPropertyChanged(nameof(Temperature)); }
        }

        /// <summary>
        /// Max tokens per response
        /// </summary>
        public int MaxTokens
        {
            get => _maxTokens;
            set { _maxTokens = value; OnPropertyChanged(nameof(MaxTokens)); }
        }

        /// <summary>
        /// Maximum iterations for multi-step agents
        /// </summary>
        public int MaxIterations
        {
            get => _maxIterations;
            set { _maxIterations = value; OnPropertyChanged(nameof(MaxIterations)); }
        }

        /// <summary>
        /// Maximum total tokens across all iterations
        /// </summary>
        public int MaxTotalTokens
        {
            get => _maxTotalTokens;
            set { _maxTotalTokens = value; OnPropertyChanged(nameof(MaxTotalTokens)); }
        }

        /// <summary>
        /// Whether to enable tool use for this agent
        /// </summary>
        public bool EnableToolUse
        {
            get => _enableToolUse;
            set { _enableToolUse = value; OnPropertyChanged(nameof(EnableToolUse)); }
        }

        /// <summary>
        /// List of enabled tool names (empty = all available tools)
        /// </summary>
        public ObservableCollection<string> EnabledTools
        {
            get => _enabledTools;
            set { _enabledTools = value; OnPropertyChanged(nameof(EnabledTools)); }
        }

        /// <summary>
        /// Mode for sub-agent execution
        /// </summary>
        public SubAgentMode SubAgentMode
        {
            get => _subAgentMode;
            set { _subAgentMode = value; OnPropertyChanged(nameof(SubAgentMode)); }
        }

        /// <summary>
        /// Description of completion criteria (for agent self-evaluation)
        /// </summary>
        public string CompletionCriteria
        {
            get => _completionCriteria;
            set { _completionCriteria = value; OnPropertyChanged(nameof(CompletionCriteria)); }
        }

        /// <summary>
        /// Whether this agent type supports iterations
        /// </summary>
        [JsonIgnore]
        public bool SupportsIterations => AgentType == AgentType.MultiStep || AgentType == AgentType.Orchestrator;

        /// <summary>
        /// Whether this agent type supports spawning sub-agents
        /// </summary>
        [JsonIgnore]
        public bool SupportsSubAgents => AgentType == AgentType.Orchestrator;

        public AutomationAgent()
        {
            Id = Guid.NewGuid();
        }

        /// <summary>
        /// Resolves the user prompt by replacing variables with context values
        /// </summary>
        public string ResolveUserPrompt(AutomationContext context)
        {
            var prompt = UserPromptTemplate;

            foreach (var variable in context.Variables)
            {
                prompt = prompt.Replace($"{{{{{variable.Key}}}}}", variable.Value?.ToString() ?? string.Empty);
            }

            return prompt;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Context passed to an automation when it executes
    /// </summary>
    public class AutomationContext
    {
        /// <summary>
        /// Variables available for template substitution
        /// </summary>
        public Dictionary<string, object?> Variables { get; set; } = new();

        /// <summary>
        /// The trigger that initiated this execution
        /// </summary>
        public AutomationTrigger? Trigger { get; set; }

        /// <summary>
        /// Trigger-specific data
        /// </summary>
        public TriggerData? TriggerData { get; set; }

        /// <summary>
        /// Parent execution context (for chained automations)
        /// </summary>
        public AutomationContext? ParentContext { get; set; }

        /// <summary>
        /// Sets a variable value
        /// </summary>
        public void SetVariable(string key, object? value)
        {
            Variables[key] = value;
        }

        /// <summary>
        /// Gets a variable value
        /// </summary>
        public T? GetVariable<T>(string key, T? defaultValue = default)
        {
            if (Variables.TryGetValue(key, out var value))
            {
                if (value is T typedValue)
                    return typedValue;

                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Creates a child context for sub-automations
        /// </summary>
        public AutomationContext CreateChildContext()
        {
            return new AutomationContext
            {
                Variables = new Dictionary<string, object?>(Variables),
                ParentContext = this
            };
        }
    }

    /// <summary>
    /// Base class for trigger-specific data
    /// </summary>
    public abstract class TriggerData
    {
        public DateTime TriggeredAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Data from a clipboard trigger
    /// </summary>
    public class ClipboardTriggerData : TriggerData
    {
        public ClipboardContentType ContentType { get; set; }
        public string? TextContent { get; set; }
        public byte[]? ImageData { get; set; }
        public List<string>? FilePaths { get; set; }
    }

    /// <summary>
    /// Data from a file change trigger
    /// </summary>
    public class FileChangeTriggerData : TriggerData
    {
        public FileChangeType ChangeType { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string? OldPath { get; set; }
        public string? FileName { get; set; }
        public string? Extension { get; set; }
    }

    /// <summary>
    /// Data from a window context trigger
    /// </summary>
    public class WindowContextTriggerData : TriggerData
    {
        public string WindowTitle { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public IntPtr WindowHandle { get; set; }
    }

    /// <summary>
    /// Data from a plugin trigger
    /// </summary>
    public class PluginTriggerData : TriggerData
    {
        public string PluginId { get; set; } = string.Empty;
        public string TriggerId { get; set; } = string.Empty;
        public Dictionary<string, object> Data { get; set; } = new();
    }

    /// <summary>
    /// Data from an automation chain trigger
    /// </summary>
    public class AutomationChainTriggerData : TriggerData
    {
        public Guid SourceAutomationId { get; set; }
        public Guid SourceExecutionId { get; set; }
        public bool SourceSuccess { get; set; }
        public string? SourceResult { get; set; }
    }
}
