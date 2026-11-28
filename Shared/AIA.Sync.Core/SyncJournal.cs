using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIA.Sync.Contracts;

namespace AIA.Sync.Core
{
    /// <summary>
    /// Tracks the server revision of every synced entity and remembers deletions as tombstones.
    /// A revision only increases. Content hashes detect desktop edits without touching every
    /// model setter.
    /// </summary>
    public sealed class SyncJournal
    {
        private readonly Dictionary<string, SyncJournalEntry> _entries = new(StringComparer.Ordinal);
        private readonly TimeProvider _timeProvider;
        private readonly SyncJournalOptions _options;

        public long Revision { get; private set; }
        public long ResetFloor { get; private set; }

        public SyncJournal(SyncJournalState? state = null, SyncJournalOptions? options = null, TimeProvider? timeProvider = null)
        {
            _options = options ?? new SyncJournalOptions();
            _timeProvider = timeProvider ?? TimeProvider.System;

            if (state != null)
            {
                Revision = state.Revision;
                ResetFloor = state.ResetFloor;

                foreach (var entry in state.Entries)
                {
                    _entries[Key(entry.EntityType, entry.Id)] = entry;
                }
            }
        }

        public SyncJournalState ToState()
        {
            return new SyncJournalState
            {
                Revision = Revision,
                ResetFloor = ResetFloor,
                Entries = _entries.Values
                    .OrderBy(e => e.EntityType)
                    .ThenBy(e => e.Id)
                    .Select(CloneEntry)
                    .ToList()
            };
        }

        public SyncJournalEntry? GetEntry(SyncEntityType entityType, Guid id)
        {
            return _entries.TryGetValue(Key(entityType, id), out var entry) ? CloneEntry(entry) : null;
        }

        public int ActiveCount => _entries.Values.Count(e => !e.IsDeleted);

        /// <summary>
        /// Applies a full desktop snapshot and returns the revision assigned to each entity.
        /// </summary>
        public IReadOnlyDictionary<string, long> ApplySnapshot(SyncSnapshot snapshot)
        {
            var now = _timeProvider.GetUtcNow();
            var current = new Dictionary<string, SyncSnapshotEntity>(StringComparer.Ordinal);

            foreach (var entity in snapshot.Entities)
            {
                if (entity.Id == Guid.Empty)
                    throw new InvalidOperationException("Snapshot contains an empty entity id.");

                var key = Key(entity.EntityType, entity.Id);
                if (!current.TryAdd(key, entity))
                    throw new InvalidOperationException($"Snapshot contains duplicate entity '{key}'.");
            }

            var assigned = new Dictionary<string, long>(StringComparer.Ordinal);

            foreach (var pair in current)
            {
                var entity = pair.Value;
                var hash = ComputeHash(entity.Data);

                if (_entries.TryGetValue(pair.Key, out var existing))
                {
                    if (existing.IsDeleted || !string.Equals(existing.Hash, hash, StringComparison.Ordinal))
                    {
                        existing.IsDeleted = false;
                        existing.Hash = hash;
                        existing.Data = entity.Data.Clone();
                        existing.ModifiedUtc = now;
                        existing.Revision = ++Revision;
                    }
                }
                else
                {
                    var entry = new SyncJournalEntry
                    {
                        EntityType = entity.EntityType,
                        Id = entity.Id,
                        Revision = ++Revision,
                        Hash = hash,
                        ModifiedUtc = now,
                        IsDeleted = false,
                        Data = entity.Data.Clone()
                    };
                    _entries[pair.Key] = entry;
                }

                assigned[pair.Key] = _entries[pair.Key].Revision;
            }

            foreach (var pair in _entries.ToList())
            {
                if (pair.Value.IsDeleted || current.ContainsKey(pair.Key))
                    continue;

                pair.Value.IsDeleted = true;
                pair.Value.Data = null;
                pair.Value.Hash = string.Empty;
                pair.Value.ModifiedUtc = now;
                pair.Value.Revision = ++Revision;
            }

            PurgeExpiredTombstones(now);
            return assigned;
        }

        public IReadOnlyList<SyncEntityEnvelope> GetChangesSince(long sinceRevision, out bool resetRequired)
        {
            resetRequired = sinceRevision < ResetFloor || sinceRevision > Revision;

            if (resetRequired)
            {
                return _entries.Values
                    .Where(e => !e.IsDeleted)
                    .OrderBy(e => e.EntityType)
                    .ThenBy(e => e.Id)
                    .Select(ToEnvelope)
                    .ToList();
            }

            return _entries.Values
                .Where(e => e.Revision > sinceRevision)
                .OrderBy(e => e.Revision)
                .ThenBy(e => e.EntityType)
                .ThenBy(e => e.Id)
                .Select(ToEnvelope)
                .ToList();
        }

        private void PurgeExpiredTombstones(DateTimeOffset now)
        {
            if (_options.TombstoneRetentionDays <= 0)
                return;

            var cutoff = now.AddDays(-_options.TombstoneRetentionDays);
            long highestPurgedRevision = 0;

            foreach (var pair in _entries.ToList())
            {
                if (!pair.Value.IsDeleted || pair.Value.ModifiedUtc >= cutoff)
                    continue;

                highestPurgedRevision = Math.Max(highestPurgedRevision, pair.Value.Revision);
                _entries.Remove(pair.Key);
            }

            if (highestPurgedRevision > ResetFloor)
                ResetFloor = highestPurgedRevision;
        }

        private static string Key(SyncEntityType entityType, Guid id) => $"{entityType}:{id:N}";

        private static string ComputeHash(JsonElement data)
        {
            var bytes = Encoding.UTF8.GetBytes(data.GetRawText());
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        private static SyncEntityEnvelope ToEnvelope(SyncJournalEntry entry)
        {
            return new SyncEntityEnvelope
            {
                EntityType = entry.EntityType,
                Id = entry.Id,
                Revision = entry.Revision,
                IsDeleted = entry.IsDeleted,
                ModifiedUtc = entry.ModifiedUtc,
                Data = entry.Data?.Clone()
            };
        }

        private static SyncJournalEntry CloneEntry(SyncJournalEntry entry)
        {
            return new SyncJournalEntry
            {
                EntityType = entry.EntityType,
                Id = entry.Id,
                Revision = entry.Revision,
                Hash = entry.Hash,
                ModifiedUtc = entry.ModifiedUtc,
                IsDeleted = entry.IsDeleted,
                Data = entry.Data?.Clone()
            };
        }
    }
}
