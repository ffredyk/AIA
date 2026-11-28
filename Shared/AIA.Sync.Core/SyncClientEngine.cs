using AIA.Sync.Contracts;

namespace AIA.Sync.Core
{
    public interface ISyncLocalStore
    {
        Task<long> GetServerRevisionAsync(CancellationToken cancellationToken);
        Task SetServerRevisionAsync(long revision, CancellationToken cancellationToken);
        Task<IReadOnlyList<SyncEntityChange>> GetPendingChangesAsync(CancellationToken cancellationToken);
        Task ApplyServerChangesAsync(IReadOnlyList<SyncEntityEnvelope> changes, CancellationToken cancellationToken);
        Task ResetFromServerAsync(IReadOnlyList<SyncEntityEnvelope> snapshot, CancellationToken cancellationToken);
        Task MarkAcceptedAsync(IReadOnlyList<SyncAck> accepted, CancellationToken cancellationToken);
    }

    public interface ISyncTransport
    {
        Task<SyncResponse> SendAsync(SyncRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Offline-first client algorithm. The server revision is the only clock. Server state wins
    /// every conflict.
    /// </summary>
    public sealed class SyncClientEngine
    {
        private readonly ISyncLocalStore _store;
        private readonly ISyncTransport _transport;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public SyncClientEngine(ISyncLocalStore store, ISyncTransport transport)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task<SyncResponse> SyncAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var since = await _store.GetServerRevisionAsync(cancellationToken).ConfigureAwait(false);
                var pending = await _store.GetPendingChangesAsync(cancellationToken).ConfigureAwait(false);

                var response = await _transport.SendAsync(new SyncRequest
                {
                    SinceRevision = since,
                    Changes = pending.ToList()
                }, cancellationToken).ConfigureAwait(false);

                if (response.ResetRequired)
                {
                    await _store.ResetFromServerAsync(response.Changes, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _store.ApplyServerChangesAsync(response.Changes, cancellationToken).ConfigureAwait(false);
                }

                var conflictVersions = response.Conflicts
                    .Where(c => c.ServerVersion != null)
                    .Select(c => c.ServerVersion!)
                    .ToList();

                if (conflictVersions.Count > 0)
                    await _store.ApplyServerChangesAsync(conflictVersions, cancellationToken).ConfigureAwait(false);

                await _store.MarkAcceptedAsync(response.Accepted, cancellationToken).ConfigureAwait(false);
                await _store.SetServerRevisionAsync(response.ServerRevision, cancellationToken).ConfigureAwait(false);

                return response;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
