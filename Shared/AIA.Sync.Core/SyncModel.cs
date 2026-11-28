using System.Text.Json;
using AIA.Sync.Contracts;

namespace AIA.Sync.Core
{
    public sealed class SyncSnapshotEntity
    {
        public SyncEntityType EntityType { get; set; }
        public Guid Id { get; set; }
        public JsonElement Data { get; set; }
    }

    public sealed class SyncSnapshot
    {
        public List<SyncSnapshotEntity> Entities { get; set; } = new();
    }

    /// <summary>
    /// Bridge between the revision engine and the desktop live data.
    /// Implementations must apply all changes atomically from the caller's point of view.
    /// </summary>
    public interface ISyncBackend
    {
        Task<SyncSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
        Task ApplyChangesAsync(IReadOnlyList<SyncEntityChange> changes, CancellationToken cancellationToken);
    }

    public interface ISyncJournalStore
    {
        Task<SyncJournalState> LoadAsync(CancellationToken cancellationToken);
        Task SaveAsync(SyncJournalState state, CancellationToken cancellationToken);
    }

    public sealed class SyncJournalState
    {
        public long Revision { get; set; }
        public long ResetFloor { get; set; }
        public List<SyncJournalEntry> Entries { get; set; } = new();
    }

    public sealed class SyncJournalEntry
    {
        public SyncEntityType EntityType { get; set; }
        public Guid Id { get; set; }
        public long Revision { get; set; }
        public string Hash { get; set; } = string.Empty;
        public DateTimeOffset ModifiedUtc { get; set; }
        public bool IsDeleted { get; set; }
        public JsonElement? Data { get; set; }
    }

    public sealed class SyncJournalOptions
    {
        public int TombstoneRetentionDays { get; set; } = 90;
        public int MaxChangesPerRequest { get; set; } = 500;
    }
}
