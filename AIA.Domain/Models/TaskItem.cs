using System;
using System.Collections.Generic;

namespace AIA.Domain.Models
{
    /// <summary>
    /// Represents a task item in the productivity system
    /// </summary>
    public class TaskItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public TaskStatus Status { get; set; } = TaskStatus.NotStarted;
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime ModifiedDate { get; set; } = DateTime.Now;
        public DateTime? DueDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public Guid? ParentTaskId { get; set; }
        public bool IsArchived { get; set; }
        
        // Recurrence
        public bool IsRecurring { get; set; }
        public RecurrenceType RecurrenceType { get; set; } = RecurrenceType.None;
        public int RecurrenceInterval { get; set; } = 1;
        public DateTime? RecurrenceEndDate { get; set; }
        
        // Tags and Dependencies
        public List<string> Tags { get; set; } = new();
        public List<Guid> DependencyTaskIds { get; set; } = new();
        
        // Subtasks (for hierarchical tasks)
        public List<TaskItem> Subtasks { get; set; } = new();
        
        // Sync metadata
        public DateTime? LastSyncedAt { get; set; }
        public bool IsSyncPending { get; set; }
        public string? SyncVersion { get; set; }
    }

    public enum TaskStatus
    {
        NotStarted,
        InProgress,
        OnHold,
        Completed,
        Cancelled
    }

    public enum TaskPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum RecurrenceType
    {
        None,
        Daily,
        Weekly,
        Monthly,
        Yearly
    }
}
