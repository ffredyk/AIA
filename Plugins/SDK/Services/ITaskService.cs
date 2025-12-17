using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Service for managing tasks
    /// </summary>
    public interface ITaskService
    {
        /// <summary>
        /// Gets all tasks (read permission required)
        /// </summary>
        IReadOnlyList<ITaskItem> GetAllTasks();

        /// <summary>
        /// Gets a task by ID
        /// </summary>
        ITaskItem? GetTaskById(Guid id);

        /// <summary>
        /// Creates a new task (write permission required)
        /// </summary>
        ITaskItem CreateTask(string title, string? description = null);

        /// <summary>
        /// Deletes a task (write permission required)
        /// </summary>
        bool DeleteTask(Guid id);

        /// <summary>
        /// Saves all tasks (write permission required)
        /// </summary>
        Task SaveAsync();

        /// <summary>
        /// Event fired when tasks change
        /// </summary>
        event EventHandler<TasksChangedEventArgs>? TasksChanged;
    }

    /// <summary>
    /// Read-only task item interface
    /// </summary>
    public interface ITaskItem
    {
        Guid Id { get; }
        string Title { get; set; }
        string Description { get; set; }
        string Notes { get; set; }
        TaskItemStatus Status { get; set; }
        TaskItemPriority Priority { get; set; }
        DateTime CreatedDate { get; }
        DateTime? DueDate { get; set; }
        DateTime? CompletedDate { get; }
        bool IsCompleted { get; }
        bool IsOverdue { get; }
        IReadOnlyList<ITaskItem> Subtasks { get; }
        
        /// <summary>
        /// Adds a subtask to this task
        /// </summary>
        ITaskItem AddSubtask(string title);
    }

    public enum TaskItemStatus
    {
        NotStarted,
        InProgress,
        OnHold,
        Completed,
        Cancelled
    }

    public enum TaskItemPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class TasksChangedEventArgs : EventArgs
    {
        public TaskChangeType ChangeType { get; }
        public ITaskItem? Task { get; }

        public TasksChangedEventArgs(TaskChangeType changeType, ITaskItem? task = null)
        {
            ChangeType = changeType;
            Task = task;
        }
    }

    public enum TaskChangeType
    {
        Added,
        Updated,
        Deleted,
        Reloaded
    }
}
