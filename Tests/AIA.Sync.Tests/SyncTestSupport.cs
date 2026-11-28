using System.Text.Json;
using AIA.Sync.Contracts;
using AIA.Sync.Core;

namespace AIA.Sync.Tests
{
    internal sealed class InMemoryJournalStore : ISyncJournalStore
    {
        public SyncJournalState State { get; private set; } = new();

        public Task<SyncJournalState> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(State);

        public Task SaveAsync(SyncJournalState state, CancellationToken cancellationToken)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeBackend : ISyncBackend
    {
        public Dictionary<Guid, JsonElement> Tasks { get; } = new();
        public int ApplyCount { get; private set; }

        public Task<SyncSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            var snapshot = new SyncSnapshot();
            foreach (var task in Tasks)
            {
                snapshot.Entities.Add(new SyncSnapshotEntity
                {
                    EntityType = SyncEntityType.Task,
                    Id = task.Key,
                    Data = task.Value.Clone()
                });
            }

            return Task.FromResult(snapshot);
        }

        public Task ApplyChangesAsync(IReadOnlyList<SyncEntityChange> changes, CancellationToken cancellationToken)
        {
            ApplyCount++;
            foreach (var change in changes)
            {
                if (change.IsDeleted)
                {
                    Tasks.Remove(change.Id);
                    continue;
                }

                if (change.Data is { } data)
                    Tasks[change.Id] = data.Clone();
            }

            return Task.CompletedTask;
        }

        public void SetTask(TaskDto dto)
        {
            Tasks[dto.Id] = JsonSerializer.SerializeToElement(dto, SyncJson.Options);
        }
    }

    internal sealed class FakeClientStore : ISyncLocalStore
    {
        public long Revision { get; private set; }
        public List<SyncEntityChange> Pending { get; } = new();
        public List<SyncEntityEnvelope> Applied { get; } = new();
        public List<SyncAck> Accepted { get; } = new();
        public bool ResetApplied { get; private set; }

        public Task<long> GetServerRevisionAsync(CancellationToken cancellationToken) => Task.FromResult(Revision);

        public Task SetServerRevisionAsync(long revision, CancellationToken cancellationToken)
        {
            Revision = revision;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SyncEntityChange>> GetPendingChangesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SyncEntityChange>>(Pending.ToList());

        public Task ApplyServerChangesAsync(IReadOnlyList<SyncEntityEnvelope> changes, CancellationToken cancellationToken)
        {
            Applied.AddRange(changes);
            return Task.CompletedTask;
        }

        public Task ResetFromServerAsync(IReadOnlyList<SyncEntityEnvelope> snapshot, CancellationToken cancellationToken)
        {
            ResetApplied = true;
            Applied.AddRange(snapshot);
            return Task.CompletedTask;
        }

        public Task MarkAcceptedAsync(IReadOnlyList<SyncAck> accepted, CancellationToken cancellationToken)
        {
            Accepted.AddRange(accepted);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeTransport : ISyncTransport
    {
        public SyncRequest? LastRequest { get; private set; }
        public SyncResponse Response { get; set; } = new();

        public Task<SyncResponse> SendAsync(SyncRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Response);
        }
    }

    internal static class TestData
    {
        public static TaskDto Task(string title = "Test task")
        {
            return new TaskDto
            {
                Id = Guid.NewGuid(),
                Title = title,
                CreatedDate = new DateTime(2026, 1, 1, 9, 0, 0),
                Status = SyncTaskStatus.NotStarted,
                Priority = SyncTaskPriority.Medium
            };
        }

        public static SyncEntityChange Change(TaskDto dto, long baseRevision = 0)
        {
            return new SyncEntityChange
            {
                EntityType = SyncEntityType.Task,
                Id = dto.Id,
                BaseRevision = baseRevision,
                Data = JsonSerializer.SerializeToElement(dto, SyncJson.Options)
            };
        }
    }
}
