using System;

namespace AIA.Domain.Models
{
    /// <summary>
    /// Base class for sync metadata tracking
    /// </summary>
    public interface ISyncable
    {
        Guid Id { get; set; }
        DateTime ModifiedDate { get; set; }
        DateTime? LastSyncedAt { get; set; }
        bool IsSyncPending { get; set; }
        string? SyncVersion { get; set; }
    }

    /// <summary>
    /// Represents a local change that needs to be synced
    /// </summary>
    public class SyncChange
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string EntityType { get; set; } = string.Empty; // "Task", "Reminder", "DataBankEntry", etc.
        public Guid EntityId { get; set; }
        public SyncChangeType ChangeType { get; set; }
        public string? EntityData { get; set; } // JSON serialized entity
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsSynced { get; set; }
        public int RetryCount { get; set; }
    }

    public enum SyncChangeType
    {
        Create,
        Update,
        Delete
    }

    /// <summary>
    /// Represents a sync conflict between local and server data
    /// </summary>
    public class SyncConflict
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string? LocalData { get; set; }
        public string? ServerData { get; set; }
        public DateTime LocalModifiedDate { get; set; }
        public DateTime ServerModifiedDate { get; set; }
        public SyncConflictResolution? Resolution { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }

    public enum SyncConflictResolution
    {
        PreferLocal,
        PreferServer,
        Merge,
        Manual
    }
}
