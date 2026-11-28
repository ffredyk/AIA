using System.Text.Json;

namespace AIA.Sync.Contracts
{
    public enum SyncEntityType
    {
        Task,
        Reminder,
        CalendarEvent
    }

    public enum SyncTaskStatus
    {
        NotStarted,
        InProgress,
        OnHold,
        Completed,
        Cancelled
    }

    public enum SyncTaskPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum SyncRecurrenceType
    {
        None,
        Daily,
        Weekly,
        Monthly,
        Yearly
    }

    public enum SyncReminderSeverity
    {
        Low,
        Medium,
        High,
        Urgent
    }

    public class TaskDto
    {
        public Guid Id { get; set; }
        public Guid? ParentTaskId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public SyncTaskStatus Status { get; set; }
        public SyncTaskPriority Priority { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public bool IsArchived { get; set; }
        public SyncRecurrenceType RecurrenceType { get; set; }
        public int RecurrenceInterval { get; set; } = 1;
        public DateTime? RecurrenceEndDate { get; set; }
        public List<string> Tags { get; set; } = new();
        public List<Guid> DependencyTaskIds { get; set; } = new();
    }

    public class ReminderDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public SyncReminderSeverity Severity { get; set; }
        public DateTime CreatedDate { get; set; }
        public bool IsCompleted { get; set; }
    }

    public class CalendarEventDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool IsAllDay { get; set; }
        public SyncRecurrenceType RecurrenceType { get; set; }
        public int RecurrenceInterval { get; set; } = 1;
        public DateTime? RecurrenceEndDate { get; set; }
        public int ReminderMinutesBefore { get; set; } = 15;
        public DateTime CreatedDate { get; set; }
    }

    public sealed class SyncEntityChange
    {
        public SyncEntityType EntityType { get; set; }
        public Guid Id { get; set; }
        public long BaseRevision { get; set; }
        public bool IsDeleted { get; set; }
        public JsonElement? Data { get; set; }
    }

    public sealed class SyncEntityEnvelope
    {
        public SyncEntityType EntityType { get; set; }
        public Guid Id { get; set; }
        public long Revision { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset ModifiedUtc { get; set; }
        public JsonElement? Data { get; set; }
    }

    public sealed class SyncConflict
    {
        public SyncEntityType EntityType { get; set; }
        public Guid Id { get; set; }
        public string Reason { get; set; } = string.Empty;
        public SyncEntityEnvelope? ServerVersion { get; set; }
    }

    public sealed class SyncAck
    {
        public SyncEntityType EntityType { get; set; }
        public Guid Id { get; set; }
        public long Revision { get; set; }
    }

    public sealed class SyncRequest
    {
        public long SinceRevision { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public List<SyncEntityChange> Changes { get; set; } = new();
    }

    public sealed class SyncResponse
    {
        public long ServerRevision { get; set; }
        public bool ResetRequired { get; set; }
        public List<SyncEntityEnvelope> Changes { get; set; } = new();
        public List<SyncAck> Accepted { get; set; } = new();
        public List<SyncConflict> Conflicts { get; set; } = new();
    }

    public sealed class PairRequest
    {
        public string Code { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
    }

    public sealed class PairResponse
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string ServerName { get; set; } = "AIA";
    }

    public sealed class HealthResponse
    {
        public string Status { get; set; } = "ok";
        public string Version { get; set; } = string.Empty;
        public long ServerRevision { get; set; }
    }

    public sealed class ApiError
    {
        public string Error { get; set; } = string.Empty;
    }
}
