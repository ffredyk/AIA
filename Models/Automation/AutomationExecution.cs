using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AIA.Models.Automation
{
    /// <summary>
    /// Represents a single execution of an automation
    /// </summary>
    public class AutomationExecution : INotifyPropertyChanged
    {
        private Guid _id;
        private Guid _automationId;
        private string _automationName = string.Empty;
        private AutomationStatus _status = AutomationStatus.Running;
        private DateTime _startedAt;
        private DateTime? _completedAt;
        private int _currentIteration;
        private int _totalTokensUsed;
        private string _triggerDescription = string.Empty;
        private string _result = string.Empty;
        private string? _error;
        private ObservableCollection<AutomationTraceEntry> _traceLog = new();
        private Dictionary<string, object?> _contextSnapshot = new();
        private Guid? _parentExecutionId;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Unique execution ID
        /// </summary>
        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        /// <summary>
        /// ID of the automation being executed
        /// </summary>
        public Guid AutomationId
        {
            get => _automationId;
            set { _automationId = value; OnPropertyChanged(nameof(AutomationId)); }
        }

        /// <summary>
        /// Name of the automation (cached for history display)
        /// </summary>
        public string AutomationName
        {
            get => _automationName;
            set { _automationName = value; OnPropertyChanged(nameof(AutomationName)); }
        }

        /// <summary>
        /// Current status of this execution
        /// </summary>
        public AutomationStatus Status
        {
            get => _status;
            set 
            { 
                _status = value; 
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsSuccess));
                OnPropertyChanged(nameof(Duration));
            }
        }

        /// <summary>
        /// When execution started
        /// </summary>
        public DateTime StartedAt
        {
            get => _startedAt;
            set { _startedAt = value; OnPropertyChanged(nameof(StartedAt)); }
        }

        /// <summary>
        /// When execution completed
        /// </summary>
        public DateTime? CompletedAt
        {
            get => _completedAt;
            set 
            { 
                _completedAt = value; 
                OnPropertyChanged(nameof(CompletedAt));
                OnPropertyChanged(nameof(Duration));
            }
        }

        /// <summary>
        /// Current iteration number (for multi-step agents)
        /// </summary>
        public int CurrentIteration
        {
            get => _currentIteration;
            set { _currentIteration = value; OnPropertyChanged(nameof(CurrentIteration)); }
        }

        /// <summary>
        /// Total tokens used in this execution
        /// </summary>
        public int TotalTokensUsed
        {
            get => _totalTokensUsed;
            set { _totalTokensUsed = value; OnPropertyChanged(nameof(TotalTokensUsed)); }
        }

        /// <summary>
        /// Description of what triggered this execution
        /// </summary>
        public string TriggerDescription
        {
            get => _triggerDescription;
            set { _triggerDescription = value; OnPropertyChanged(nameof(TriggerDescription)); }
        }

        /// <summary>
        /// Final result of the execution
        /// </summary>
        public string Result
        {
            get => _result;
            set { _result = value; OnPropertyChanged(nameof(Result)); }
        }

        /// <summary>
        /// Error message if execution failed
        /// </summary>
        public string? Error
        {
            get => _error;
            set { _error = value; OnPropertyChanged(nameof(Error)); }
        }

        /// <summary>
        /// Complete trace log of the execution
        /// </summary>
        public ObservableCollection<AutomationTraceEntry> TraceLog
        {
            get => _traceLog;
            set { _traceLog = value; OnPropertyChanged(nameof(TraceLog)); }
        }

        /// <summary>
        /// Snapshot of context variables at execution time
        /// </summary>
        public Dictionary<string, object?> ContextSnapshot
        {
            get => _contextSnapshot;
            set { _contextSnapshot = value; OnPropertyChanged(nameof(ContextSnapshot)); }
        }

        /// <summary>
        /// Parent execution ID (for sub-automations)
        /// </summary>
        public Guid? ParentExecutionId
        {
            get => _parentExecutionId;
            set { _parentExecutionId = value; OnPropertyChanged(nameof(ParentExecutionId)); }
        }

        // Computed properties

        [JsonIgnore]
        public bool IsCompleted => Status == AutomationStatus.Completed || 
                                    Status == AutomationStatus.Failed || 
                                    Status == AutomationStatus.Cancelled;

        [JsonIgnore]
        public bool IsSuccess => Status == AutomationStatus.Completed;

        [JsonIgnore]
        public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;

        [JsonIgnore]
        public string DurationText
        {
            get
            {
                if (!Duration.HasValue)
                    return Status == AutomationStatus.Running ? "Running..." : "-";

                var d = Duration.Value;
                if (d.TotalSeconds < 1)
                    return $"{d.TotalMilliseconds:F0}ms";
                if (d.TotalMinutes < 1)
                    return $"{d.TotalSeconds:F1}s";
                if (d.TotalHours < 1)
                    return $"{d.Minutes}m {d.Seconds}s";
                return $"{d.Hours}h {d.Minutes}m";
            }
        }

        public AutomationExecution()
        {
            Id = Guid.NewGuid();
            StartedAt = DateTime.Now;
        }

        /// <summary>
        /// Adds a trace entry to the log
        /// </summary>
        public void AddTrace(TraceLevel level, string message, string? details = null)
        {
            TraceLog.Add(new AutomationTraceEntry
            {
                Level = level,
                Message = message,
                Details = details,
                Timestamp = DateTime.Now,
                Iteration = CurrentIteration
            });
        }

        /// <summary>
        /// Marks execution as completed successfully
        /// </summary>
        public void Complete(string result)
        {
            Result = result;
            Status = AutomationStatus.Completed;
            CompletedAt = DateTime.Now;
            AddTrace(TraceLevel.Info, "Execution completed successfully", result);
        }

        /// <summary>
        /// Marks execution as failed
        /// </summary>
        public void Fail(string error)
        {
            Error = error;
            Status = AutomationStatus.Failed;
            CompletedAt = DateTime.Now;
            AddTrace(TraceLevel.Error, "Execution failed", error);
        }

        /// <summary>
        /// Marks execution as cancelled
        /// </summary>
        public void Cancel()
        {
            Status = AutomationStatus.Cancelled;
            CompletedAt = DateTime.Now;
            AddTrace(TraceLevel.Warning, "Execution cancelled");
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// A single entry in the execution trace log
    /// </summary>
    public class AutomationTraceEntry : INotifyPropertyChanged
    {
        private DateTime _timestamp;
        private TraceLevel _level;
        private string _message = string.Empty;
        private string? _details;
        private int _iteration;
        private string? _agentName;
        private string? _toolName;
        private int? _tokensUsed;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// When this trace entry was created
        /// </summary>
        public DateTime Timestamp
        {
            get => _timestamp;
            set { _timestamp = value; OnPropertyChanged(nameof(Timestamp)); }
        }

        /// <summary>
        /// Severity level
        /// </summary>
        public TraceLevel Level
        {
            get => _level;
            set 
            { 
                _level = value; 
                OnPropertyChanged(nameof(Level));
                OnPropertyChanged(nameof(LevelColor));
                OnPropertyChanged(nameof(LevelIcon));
            }
        }

        /// <summary>
        /// Log message
        /// </summary>
        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(nameof(Message)); }
        }

        /// <summary>
        /// Additional details (can be long text)
        /// </summary>
        public string? Details
        {
            get => _details;
            set { _details = value; OnPropertyChanged(nameof(Details)); }
        }

        /// <summary>
        /// Iteration number when this was logged
        /// </summary>
        public int Iteration
        {
            get => _iteration;
            set { _iteration = value; OnPropertyChanged(nameof(Iteration)); }
        }

        /// <summary>
        /// Name of the agent that generated this entry
        /// </summary>
        public string? AgentName
        {
            get => _agentName;
            set { _agentName = value; OnPropertyChanged(nameof(AgentName)); }
        }

        /// <summary>
        /// Name of the tool that was called (if applicable)
        /// </summary>
        public string? ToolName
        {
            get => _toolName;
            set { _toolName = value; OnPropertyChanged(nameof(ToolName)); }
        }

        /// <summary>
        /// Tokens used for this operation
        /// </summary>
        public int? TokensUsed
        {
            get => _tokensUsed;
            set { _tokensUsed = value; OnPropertyChanged(nameof(TokensUsed)); }
        }

        [JsonIgnore]
        public string LevelColor => Level switch
        {
            TraceLevel.Debug => "#666666",
            TraceLevel.Info => "#0078D4",
            TraceLevel.Warning => "#FFA500",
            TraceLevel.Error => "#FF4444",
            TraceLevel.Critical => "#FF0000",
            _ => "#999999"
        };

        [JsonIgnore]
        public string LevelIcon => Level switch
        {
            TraceLevel.Debug => "Bug20",
            TraceLevel.Info => "Info20",
            TraceLevel.Warning => "Warning20",
            TraceLevel.Error => "ErrorCircle20",
            TraceLevel.Critical => "DismissCircle20",
            _ => "Circle20"
        };

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
