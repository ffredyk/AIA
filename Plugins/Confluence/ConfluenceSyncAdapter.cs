using System;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Confluence
{
    /// <summary>
    /// Read-only sync adapter for Confluence pages. Confluence pages have no completion or due date,
    /// so only a link to the page is exposed.
    /// </summary>
    public class ConfluenceSyncAdapter : ISyncAdapter
    {
        private readonly ConfluenceService _service;

        public ConfluenceSyncAdapter(ConfluenceService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string SourceKey => "confluence";

        public SyncCapabilities Capabilities => SyncCapabilities.None;

        public Task<ExternalSyncState?> GetStateAsync(string sourceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = new ExternalSyncState
            {
                SourceId = sourceId ?? string.Empty,
                SourceUrl = string.IsNullOrWhiteSpace(sourceId) ? string.Empty : _service.BuildPageUrl(sourceId),
                IsCompleted = null,
                DueUtc = null
            };

            return Task.FromResult<ExternalSyncState?>(state);
        }

        public Task SetCompletedAsync(string sourceId, bool completed, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Confluence sync adapter does not support writing completion state.");
        }

        public Task SetDueDateAsync(string sourceId, DateTime? dueUtc, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Confluence sync adapter does not support writing due dates.");
        }
    }
}
