using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace AIA.Models.Automation
{
    /// <summary>
    /// Represents a complete automation task with triggers, agent, and actions
    /// </summary>
    public class AutomationTask : INotifyPropertyChanged
    {
        private Guid _id;
        private string _name = string.Empty;
        private string _description = string.Empty;
        private AutomationStatus _status = AutomationStatus.Disabled;
        private bool _isOneTime;
        private AutomationTrigger? _trigger;
        private AutomationAgent _agent = new();
        private ObservableCollection<AutomationAction> _actions = new();
        private AutomationPermissions _permissions = new();
        private AutomationLimits _limits = new();
        private RetryStrategy _retryStrategy = RetryStrategy.None;
        private int _retryCount = 3;
        private int _retryDelaySeconds = 5;
        private DateTime _createdDate;
        private DateTime _modifiedDate;
        private DateTime? _lastExecutionDate;
        private int _totalExecutions;
        private int _successfulExecutions;
        private int _failedExecutions;
        private string _lastResult = string.Empty;
        private ObservableCollection<string> _tags = new();

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
        /// User-friendly name
        /// </summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        /// <summary>
        /// Description of what this automation does
        /// </summary>
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        /// <summary>
        /// Current status of the automation
        /// </summary>
        public AutomationStatus Status
        {
            get => _status;
            set 
            { 
                _status = value; 
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        /// <summary>
        /// Whether this is a one-time background task
        /// </summary>
        public bool IsOneTime
        {
            get => _isOneTime;
            set { _isOneTime = value; OnPropertyChanged(nameof(IsOneTime)); }
        }

        /// <summary>
        /// The trigger that activates this automation
        /// </summary>
        public AutomationTrigger? Trigger
        {
            get => _trigger;
            set { _trigger = value; OnPropertyChanged(nameof(Trigger)); }
        }

        /// <summary>
        /// The agent configuration for execution
        /// </summary>
        public AutomationAgent Agent
        {
            get => _agent;
            set { _agent = value; OnPropertyChanged(nameof(Agent)); }
        }

        /// <summary>
        /// Actions to perform after agent execution
        /// </summary>
        public ObservableCollection<AutomationAction> Actions
        {
            get => _actions;
            set { _actions = value; OnPropertyChanged(nameof(Actions)); }
        }

        /// <summary>
        /// Permissions for this automation
        /// </summary>
        public AutomationPermissions Permissions
        {
            get => _permissions;
            set { _permissions = value; OnPropertyChanged(nameof(Permissions)); }
        }

        /// <summary>
        /// Execution limits
        /// </summary>
        public AutomationLimits Limits
        {
            get => _limits;
            set { _limits = value; OnPropertyChanged(nameof(Limits)); }
        }

        /// <summary>
        /// Retry strategy for failed executions
        /// </summary>
        public RetryStrategy RetryStrategy
        {
            get => _retryStrategy;
            set { _retryStrategy = value; OnPropertyChanged(nameof(RetryStrategy)); }
        }

        /// <summary>
        /// Number of retry attempts
        /// </summary>
        public int RetryCount
        {
            get => _retryCount;
            set { _retryCount = value; OnPropertyChanged(nameof(RetryCount)); }
        }

        /// <summary>
        /// Delay between retries in seconds
        /// </summary>
        public int RetryDelaySeconds
        {
            get => _retryDelaySeconds;
            set { _retryDelaySeconds = value; OnPropertyChanged(nameof(RetryDelaySeconds)); }
        }

        /// <summary>
        /// When the automation was created
        /// </summary>
        public DateTime CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(nameof(CreatedDate)); }
        }

        /// <summary>
        /// When the automation was last modified
        /// </summary>
        public DateTime ModifiedDate
        {
            get => _modifiedDate;
            set { _modifiedDate = value; OnPropertyChanged(nameof(ModifiedDate)); }
        }

        /// <summary>
        /// When the automation last executed
        /// </summary>
        public DateTime? LastExecutionDate
        {
            get => _lastExecutionDate;
            set { _lastExecutionDate = value; OnPropertyChanged(nameof(LastExecutionDate)); }
        }

        /// <summary>
        /// Total number of executions
        /// </summary>
        public int TotalExecutions
        {
            get => _totalExecutions;
            set 
            { 
                _totalExecutions = value; 
                OnPropertyChanged(nameof(TotalExecutions));
                OnPropertyChanged(nameof(SuccessRate));
            }
        }

        /// <summary>
        /// Number of successful executions
        /// </summary>
        public int SuccessfulExecutions
        {
            get => _successfulExecutions;
            set 
            { 
                _successfulExecutions = value; 
                OnPropertyChanged(nameof(SuccessfulExecutions));
                OnPropertyChanged(nameof(SuccessRate));
            }
        }

        /// <summary>
        /// Number of failed executions
        /// </summary>
        public int FailedExecutions
        {
            get => _failedExecutions;
            set { _failedExecutions = value; OnPropertyChanged(nameof(FailedExecutions)); }
        }

        /// <summary>
        /// Last execution result summary
        /// </summary>
        public string LastResult
        {
            get => _lastResult;
            set { _lastResult = value; OnPropertyChanged(nameof(LastResult)); }
        }

        /// <summary>
        /// Tags for categorization
        /// </summary>
        public ObservableCollection<string> Tags
        {
            get => _tags;
            set { _tags = value; OnPropertyChanged(nameof(Tags)); }
        }

        // Computed properties

        [JsonIgnore]
        public bool IsActive => Status == AutomationStatus.Active || Status == AutomationStatus.Running;

        [JsonIgnore]
        public bool IsRunning => Status == AutomationStatus.Running;

        [JsonIgnore]
        public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions * 100 : 0;

        [JsonIgnore]
        public string StatusText => Status switch
        {
            AutomationStatus.Disabled => "Disabled",
            AutomationStatus.Active => "Active",
            AutomationStatus.Running => "Running",
            AutomationStatus.Paused => "Paused",
            AutomationStatus.Completed => "Completed",
            AutomationStatus.Failed => "Failed",
            AutomationStatus.Cancelled => "Cancelled",
            _ => "Unknown"
        };

        [JsonIgnore]
        public string StatusColor => Status switch
        {
            AutomationStatus.Disabled => "#666666",
            AutomationStatus.Active => "#1EB75F",
            AutomationStatus.Running => "#0078D4",
            AutomationStatus.Paused => "#FFA500",
            AutomationStatus.Completed => "#1EB75F",
            AutomationStatus.Failed => "#FF4444",
            AutomationStatus.Cancelled => "#999999",
            _ => "#666666"
        };

        public AutomationTask()
        {
            Id = Guid.NewGuid();
            CreatedDate = DateTime.Now;
            ModifiedDate = DateTime.Now;
        }

        /// <summary>
        /// Enables the automation
        /// </summary>
        public void Enable()
        {
            if (Status == AutomationStatus.Disabled || Status == AutomationStatus.Completed || 
                Status == AutomationStatus.Failed || Status == AutomationStatus.Cancelled)
            {
                Status = AutomationStatus.Active;
                ModifiedDate = DateTime.Now;
            }
        }

        /// <summary>
        /// Disables the automation
        /// </summary>
        public void Disable()
        {
            if (Status != AutomationStatus.Running)
            {
                Status = AutomationStatus.Disabled;
                ModifiedDate = DateTime.Now;
            }
        }

        /// <summary>
        /// Pauses a running automation (for one-time tasks)
        /// </summary>
        public void Pause()
        {
            if (Status == AutomationStatus.Running && IsOneTime)
            {
                Status = AutomationStatus.Paused;
            }
        }

        /// <summary>
        /// Resumes a paused automation
        /// </summary>
        public void Resume()
        {
            if (Status == AutomationStatus.Paused)
            {
                Status = AutomationStatus.Running;
            }
        }

        /// <summary>
        /// Records a successful execution
        /// </summary>
        public void RecordSuccess(string result)
        {
            TotalExecutions++;
            SuccessfulExecutions++;
            LastExecutionDate = DateTime.Now;
            LastResult = result;
            
            if (IsOneTime)
            {
                Status = AutomationStatus.Completed;
            }
        }

        /// <summary>
        /// Records a failed execution
        /// </summary>
        public void RecordFailure(string error)
        {
            TotalExecutions++;
            FailedExecutions++;
            LastExecutionDate = DateTime.Now;
            LastResult = $"Error: {error}";
            
            if (IsOneTime)
            {
                Status = AutomationStatus.Failed;
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Permissions that control what an automation can do
    /// </summary>
    public class AutomationPermissions : INotifyPropertyChanged
    {
        private bool _canCreateTasks = true;
        private bool _canCreateReminders = true;
        private bool _canSaveToDataBank = true;
        private bool _canShowNotifications = true;
        private bool _canAccessClipboard = true;
        private bool _canAccessFileSystem;
        private bool _canRunSubAutomations = true;
        private bool _canUsePluginActions = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool CanCreateTasks
        {
            get => _canCreateTasks;
            set { _canCreateTasks = value; OnPropertyChanged(nameof(CanCreateTasks)); }
        }

        public bool CanCreateReminders
        {
            get => _canCreateReminders;
            set { _canCreateReminders = value; OnPropertyChanged(nameof(CanCreateReminders)); }
        }

        public bool CanSaveToDataBank
        {
            get => _canSaveToDataBank;
            set { _canSaveToDataBank = value; OnPropertyChanged(nameof(CanSaveToDataBank)); }
        }

        public bool CanShowNotifications
        {
            get => _canShowNotifications;
            set { _canShowNotifications = value; OnPropertyChanged(nameof(CanShowNotifications)); }
        }

        public bool CanAccessClipboard
        {
            get => _canAccessClipboard;
            set { _canAccessClipboard = value; OnPropertyChanged(nameof(CanAccessClipboard)); }
        }

        public bool CanAccessFileSystem
        {
            get => _canAccessFileSystem;
            set { _canAccessFileSystem = value; OnPropertyChanged(nameof(CanAccessFileSystem)); }
        }

        public bool CanRunSubAutomations
        {
            get => _canRunSubAutomations;
            set { _canRunSubAutomations = value; OnPropertyChanged(nameof(CanRunSubAutomations)); }
        }

        public bool CanUsePluginActions
        {
            get => _canUsePluginActions;
            set { _canUsePluginActions = value; OnPropertyChanged(nameof(CanUsePluginActions)); }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Limits for automation execution
    /// </summary>
    public class AutomationLimits : INotifyPropertyChanged
    {
        private int _maxConcurrentExecutions = 1;
        private int _maxExecutionsPerHour = 60;
        private int _maxExecutionsPerDay = 1000;
        private int _maxActionsPerExecution = 10;
        private int _confirmationThreshold = 5;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Maximum concurrent executions of this automation
        /// </summary>
        public int MaxConcurrentExecutions
        {
            get => _maxConcurrentExecutions;
            set { _maxConcurrentExecutions = value; OnPropertyChanged(nameof(MaxConcurrentExecutions)); }
        }

        /// <summary>
        /// Maximum executions per hour
        /// </summary>
        public int MaxExecutionsPerHour
        {
            get => _maxExecutionsPerHour;
            set { _maxExecutionsPerHour = value; OnPropertyChanged(nameof(MaxExecutionsPerHour)); }
        }

        /// <summary>
        /// Maximum executions per day
        /// </summary>
        public int MaxExecutionsPerDay
        {
            get => _maxExecutionsPerDay;
            set { _maxExecutionsPerDay = value; OnPropertyChanged(nameof(MaxExecutionsPerDay)); }
        }

        /// <summary>
        /// Maximum actions per single execution
        /// </summary>
        public int MaxActionsPerExecution
        {
            get => _maxActionsPerExecution;
            set { _maxActionsPerExecution = value; OnPropertyChanged(nameof(MaxActionsPerExecution)); }
        }

        /// <summary>
        /// Threshold for requiring confirmation (e.g., creating more than N tasks)
        /// </summary>
        public int ConfirmationThreshold
        {
            get => _confirmationThreshold;
            set { _confirmationThreshold = value; OnPropertyChanged(nameof(ConfirmationThreshold)); }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
