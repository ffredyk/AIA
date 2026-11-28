using AIA.Sync.Contracts;

namespace AIA.Sync.Core
{
    public sealed class SyncRequestException : Exception
    {
        public SyncRequestException(string message) : base(message) { }
    }

    /// <summary>
    /// Implements the pull/push protocol. One coordinator instance owns one desktop data set.
    /// </summary>
    public sealed class SyncCoordinator
    {
        private readonly ISyncBackend _backend;
        private readonly ISyncJournalStore _journalStore;
        private readonly SyncJournalOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private SyncJournal? _journal;

        public SyncCoordinator(
            ISyncBackend backend,
            ISyncJournalStore journalStore,
            SyncJournalOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
            _options = options ?? new SyncJournalOptions();
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<long> GetCurrentRevisionAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
                return _journal!.Revision;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<SyncResponse> SyncAsync(SyncRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
                throw new SyncRequestException("Request body is required.");

            var incoming = request.Changes ?? new List<SyncEntityChange>();
            if (incoming.Count > _options.MaxChangesPerRequest)
                throw new SyncRequestException($"Too many changes in one request (limit {_options.MaxChangesPerRequest}).");

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

                var snapshot = await _backend.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                _journal!.ApplySnapshot(snapshot);
                await _journalStore.SaveAsync(_journal.ToState(), cancellationToken).ConfigureAwait(false);

                var conflicts = new List<SyncConflict>();
                var acceptedChanges = new List<SyncEntityChange>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var change in incoming)
                {
                    if (change == null)
                        continue;

                    if (change.Id == Guid.Empty)
                    {
                        conflicts.Add(Conflict(change, "Entity id is required."));
                        continue;
                    }

                    var key = $"{change.EntityType}:{change.Id:N}";
                    if (!seen.Add(key))
                    {
                        conflicts.Add(Conflict(change, "Duplicate entity in the same request."));
                        continue;
                    }

                    var current = _journal.GetEntry(change.EntityType, change.Id);

                    if (current == null)
                    {
                        if (change.BaseRevision != 0)
                        {
                            conflicts.Add(Conflict(change, "Entity no longer exists on the server."));
                            continue;
                        }

                        if (change.IsDeleted)
                            continue;

                        if (change.Data == null)
                        {
                            conflicts.Add(Conflict(change, "Entity data is required."));
                            continue;
                        }

                        acceptedChanges.Add(change);
                        continue;
                    }

                    if (change.BaseRevision != current.Revision)
                    {
                        conflicts.Add(Conflict(change, "Entity changed on the server.", current));
                        continue;
                    }

                    if (current.IsDeleted)
                    {
                        if (change.IsDeleted)
                            continue;

                        conflicts.Add(Conflict(change, "Entity was deleted on the server.", current));
                        continue;
                    }

                    if (change.IsDeleted)
                    {
                        acceptedChanges.Add(change);
                        continue;
                    }

                    if (change.Data == null)
                    {
                        conflicts.Add(Conflict(change, "Entity data is required."));
                        continue;
                    }

                    acceptedChanges.Add(change);
                }

                if (acceptedChanges.Count > 0)
                {
                    await _backend.ApplyChangesAsync(acceptedChanges, cancellationToken).ConfigureAwait(false);

                    snapshot = await _backend.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                    _journal.ApplySnapshot(snapshot);
                    await _journalStore.SaveAsync(_journal.ToState(), cancellationToken).ConfigureAwait(false);
                }

                var accepted = new List<SyncAck>();
                foreach (var change in acceptedChanges)
                {
                    var entry = _journal.GetEntry(change.EntityType, change.Id);
                    if (entry != null)
                    {
                        accepted.Add(new SyncAck
                        {
                            EntityType = change.EntityType,
                            Id = change.Id,
                            Revision = entry.Revision
                        });
                    }
                }

                var changes = _journal.GetChangesSince(request.SinceRevision, out var resetRequired);

                return new SyncResponse
                {
                    ServerRevision = _journal.Revision,
                    ResetRequired = resetRequired,
                    Changes = changes.ToList(),
                    Accepted = accepted,
                    Conflicts = conflicts
                };
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (_journal != null)
                return;

            var state = await _journalStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var journal = new SyncJournal(state, _options, _timeProvider);

            var snapshot = await _backend.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            journal.ApplySnapshot(snapshot);

            _journal = journal;
            await _journalStore.SaveAsync(journal.ToState(), cancellationToken).ConfigureAwait(false);
        }

        private static SyncConflict Conflict(SyncEntityChange change, string reason, SyncJournalEntry? serverEntry = null)
        {
            return new SyncConflict
            {
                EntityType = change.EntityType,
                Id = change.Id,
                Reason = reason,
                ServerVersion = serverEntry == null
                    ? null
                    : new SyncEntityEnvelope
                    {
                        EntityType = serverEntry.EntityType,
                        Id = serverEntry.Id,
                        Revision = serverEntry.Revision,
                        IsDeleted = serverEntry.IsDeleted,
                        ModifiedUtc = serverEntry.ModifiedUtc,
                        Data = serverEntry.Data?.Clone()
                    }
            };
        }
    }
}
