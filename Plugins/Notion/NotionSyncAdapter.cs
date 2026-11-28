using System;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Notion
{
    /// <summary>
    /// Sync adapter that links AIA items to Notion database pages.
    /// </summary>
    /// <remarks>
    /// Notion pages do not expose a generic completion or due date, so both are mapped onto
    /// configured database properties. When those properties are missing the adapter exposes
    /// no capabilities and refuses to write.
    /// </remarks>
    public sealed class NotionSyncAdapter : ISyncAdapter
    {
        private readonly NotionService _service;

        public NotionSyncAdapter(NotionService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <inheritdoc />
        public string SourceKey => "notion";

        /// <inheritdoc />
        public SyncCapabilities Capabilities =>
            !_service.IsSyncConfigured
                ? SyncCapabilities.None
                : SyncCapabilities.ReadSourceCompletion |
                  SyncCapabilities.WriteSourceCompletion |
                  SyncCapabilities.ReadSourceDueDate |
                  SyncCapabilities.WriteSourceDueDate;

        /// <inheritdoc />
        public async Task<ExternalSyncState?> GetStateAsync(string sourceId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                return null;

            try
            {
                var state = await _service.GetSyncStateAsync(sourceId, cancellationToken).ConfigureAwait(false);

                return new ExternalSyncState
                {
                    SourceId = sourceId,
                    SourceUrl = state.Url,
                    Title = string.Empty,
                    IsCompleted = state.Completed,
                    DueUtc = state.Due
                };
            }
            catch (NotionApiException ex) when (ex.StatusCode == 404)
            {
                // The page was deleted, archived or is no longer shared with the integration.
                return null;
            }
        }

        /// <inheritdoc />
        public Task SetCompletedAsync(string sourceId, bool completed, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
            EnsureConfigured();

            return _service.SetCompletedAsync(sourceId, completed, cancellationToken);
        }

        /// <inheritdoc />
        public Task SetDueDateAsync(string sourceId, DateTime? dueUtc, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
            EnsureConfigured();

            return _service.SetDueDateAsync(sourceId, dueUtc, cancellationToken);
        }

        private void EnsureConfigured()
        {
            if (!_service.IsSyncConfigured)
            {
                throw new NotSupportedException(
                    "Notion sync is not configured. Choose a completed property (checkbox or status) and a due date property in the Notion plugin settings.");
            }
        }
    }
}
