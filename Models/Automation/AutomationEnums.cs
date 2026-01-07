using System;

namespace AIA.Models.Automation
{
    /// <summary>
    /// The type of trigger that starts an automation
    /// </summary>
    public enum TriggerType
    {
        /// <summary>
        /// Manual trigger (user-initiated)
        /// </summary>
        Manual,

        /// <summary>
        /// Triggered when clipboard content changes
        /// </summary>
        Clipboard,

        /// <summary>
        /// Triggered when a specific hotkey is pressed
        /// </summary>
        Hotkey,

        /// <summary>
        /// Triggered when files change in a watched folder
        /// </summary>
        FileChange,

        /// <summary>
        /// Triggered when active window changes
        /// </summary>
        WindowContext,

        /// <summary>
        /// Triggered by a plugin
        /// </summary>
        Plugin,

        /// <summary>
        /// Triggered by another automation completing
        /// </summary>
        AutomationChain,

        /// <summary>
        /// Scheduled trigger (time-based)
        /// </summary>
        Schedule
    }

    /// <summary>
    /// Content type filter for clipboard triggers
    /// </summary>
    [Flags]
    public enum ClipboardContentType
    {
        None = 0,
        Text = 1,
        Image = 2,
        FilePaths = 4,
        Html = 8,
        All = Text | Image | FilePaths | Html
    }

    /// <summary>
    /// File change types to monitor
    /// </summary>
    [Flags]
    public enum FileChangeType
    {
        None = 0,
        Created = 1,
        Modified = 2,
        Deleted = 4,
        Renamed = 8,
        All = Created | Modified | Deleted | Renamed
    }

    /// <summary>
    /// The current status of an automation task
    /// </summary>
    public enum AutomationStatus
    {
        /// <summary>
        /// Automation is inactive/disabled
        /// </summary>
        Disabled,

        /// <summary>
        /// Automation is enabled and waiting for trigger
        /// </summary>
        Active,

        /// <summary>
        /// Automation is currently running
        /// </summary>
        Running,

        /// <summary>
        /// Automation is paused (for one-time tasks)
        /// </summary>
        Paused,

        /// <summary>
        /// Automation completed successfully
        /// </summary>
        Completed,

        /// <summary>
        /// Automation failed
        /// </summary>
        Failed,

        /// <summary>
        /// Automation was cancelled
        /// </summary>
        Cancelled
    }

    /// <summary>
    /// Type of agent for automation execution
    /// </summary>
    public enum AgentType
    {
        /// <summary>
        /// Simple single-prompt agent
        /// </summary>
        SimplePrompt,

        /// <summary>
        /// Multi-step agent that can iterate
        /// </summary>
        MultiStep,

        /// <summary>
        /// Orchestrator agent that can spawn sub-agents
        /// </summary>
        Orchestrator
    }

    /// <summary>
    /// Type of action to perform when automation completes
    /// </summary>
    public enum ActionType
    {
        /// <summary>
        /// Show a notification
        /// </summary>
        Notification,

        /// <summary>
        /// Create a task
        /// </summary>
        CreateTask,

        /// <summary>
        /// Create a reminder
        /// </summary>
        CreateReminder,

        /// <summary>
        /// Save to data bank
        /// </summary>
        SaveToDataBank,

        /// <summary>
        /// Store result in automation
        /// </summary>
        StoreResult,

        /// <summary>
        /// Run another automation
        /// </summary>
        RunAutomation,

        /// <summary>
        /// Execute a plugin action
        /// </summary>
        PluginAction,

        /// <summary>
        /// Copy to clipboard
        /// </summary>
        CopyToClipboard,

        /// <summary>
        /// Save to file
        /// </summary>
        SaveToFile
    }

    /// <summary>
    /// Execution mode for sub-agents
    /// </summary>
    public enum SubAgentMode
    {
        /// <summary>
        /// Wait for sub-agent to complete and receive results
        /// </summary>
        WaitForCompletion,

        /// <summary>
        /// Fire and forget - don't wait for results
        /// </summary>
        FireAndForget
    }

    /// <summary>
    /// Log level for automation execution traces
    /// </summary>
    public enum TraceLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Retry strategy for failed executions
    /// </summary>
    public enum RetryStrategy
    {
        /// <summary>
        /// No retry
        /// </summary>
        None,

        /// <summary>
        /// Retry immediately
        /// </summary>
        Immediate,

        /// <summary>
        /// Retry with delay
        /// </summary>
        WithDelay,

        /// <summary>
        /// Retry with exponential backoff
        /// </summary>
        ExponentialBackoff,

        /// <summary>
        /// Manual retry only
        /// </summary>
        Manual
    }

    /// <summary>
    /// Recurrence type for scheduled triggers
    /// </summary>
    public enum RecurrenceType
    {
        /// <summary>
        /// No recurrence (one-time)
        /// </summary>
        None,

        /// <summary>
        /// Repeat daily
        /// </summary>
        Daily,

        /// <summary>
        /// Repeat weekly
        /// </summary>
        Weekly,

        /// <summary>
        /// Repeat monthly
        /// </summary>
        Monthly
    }
}
