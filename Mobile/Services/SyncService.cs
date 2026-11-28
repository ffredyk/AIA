using AIA.Sync.Contracts;
using AIA.Sync.Core;

namespace AIA.Mobile.Services
{
    public sealed class SyncService
    {
        private readonly SqliteSyncStore _store;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public SyncService(SqliteSyncStore store)
        {
            _store = store;
        }

        public DateTimeOffset? LastSyncUtc { get; private set; }
        public string? LastError { get; private set; }
        public int LastConflictCount { get; private set; }

        public async Task<SyncResponse?> SyncNowAsync(CancellationToken cancellationToken = default)
        {
            var url = MobileSettingsService.ServerUrl;
            var token = await MobileSettingsService.GetTokenAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Server URL is not configured.");
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Device is not paired.");

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _store.InitializeAsync().ConfigureAwait(false);
                var engine = new SyncClientEngine(_store, new HttpSyncTransport(url, token));
                var response = await engine.SyncAsync(cancellationToken).ConfigureAwait(false);

                LastSyncUtc = DateTimeOffset.UtcNow;
                LastError = null;
                LastConflictCount = response.Conflicts.Count;

                await MobileApp.DeviceIntegration.RefreshAsync(_store, cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                throw;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
