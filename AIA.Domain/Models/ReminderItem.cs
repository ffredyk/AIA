using System;

namespace AIA.Domain.Models
{
    /// <summary>
    /// Represents a reminder item in the productivity system
    /// </summary>
    public class ReminderItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public DateTime DueDate { get; set; } = DateTime.Now.AddHours(1);
        public ReminderSeverity Severity { get; set; } = ReminderSeverity.Medium;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime ModifiedDate { get; set; } = DateTime.Now;
        public bool IsCompleted { get; set; }
        
        // Sync metadata
        public DateTime? LastSyncedAt { get; set; }
        public bool IsSyncPending { get; set; }
        public string? SyncVersion { get; set; }
    }

    public enum ReminderSeverity
    {
        Low,
        Medium,
        High,
        Urgent
    }
}
