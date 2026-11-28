using System;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Trello
{
    /// <summary>
    /// Sync adapter that links AIA items to Trello cards.
    /// </summary>
    public class TrelloSyncAdapter : ISyncAdapter
    {
        private readonly TrelloService _service;

        public TrelloSyncAdapter(TrelloService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <inheritdoc />
        public string SourceKey => "trello";

        /// <inheritdoc />
        public SyncCapabilities Capabilities =>
            SyncCapabilities.ReadSourceCompletion |
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
                var state = await _service.GetCardSyncStateAsync(sourceId, cancellationToken).ConfigureAwait(false);

                return new ExternalSyncState
                {
                    SourceId = sourceId,
                    SourceUrl = state.Url,
                    IsCompleted = state.Completed,
                    DueUtc = state.DueUtc
                };
            }
            catch (TrelloApiException ex) when (ex.StatusCode == 404)
            {
                // The card was deleted or is no longer accessible.
                return null;
            }
        }

        /// <inheritdoc />
        public Task SetCompletedAsync(string sourceId, bool completed, CancellationToken cancellationToken)
        {
            return _service.SetDueCompleteAsync(sourceId, completed, cancellationToken);
        }

        /// <inheritdoc />
        public Task SetDueDateAsync(string sourceId, DateTime? dueUtc, CancellationToken cancellationToken)
        {
            return _service.SetDueDateAsync(sourceId, dueUtc, cancellationToken);
        }
    }
}
