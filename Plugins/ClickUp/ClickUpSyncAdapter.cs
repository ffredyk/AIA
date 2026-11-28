using System;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.ClickUp
{
    /// <summary>
    /// Bridges <see cref="ClickUpService"/> to the AIA sync engine.
    /// </summary>
    public class ClickUpSyncAdapter : ISyncAdapter
    {
        private readonly ClickUpService _service;

        public ClickUpSyncAdapter(ClickUpService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string SourceKey => "clickup";

        public SyncCapabilities Capabilities =>
            SyncCapabilities.ReadSourceCompletion |
            SyncCapabilities.WriteSourceCompletion |
            SyncCapabilities.ReadSourceDueDate |
            SyncCapabilities.WriteSourceDueDate;

        public async Task<ExternalSyncState?> GetStateAsync(string sourceId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                return null;

            try
            {
                var (completed, due, url) = await _service
                    .GetTaskSyncStateAsync(sourceId, cancellationToken)
                    .ConfigureAwait(false);

                return new ExternalSyncState
                {
                    SourceId = sourceId,
                    SourceUrl = url,
                    IsCompleted = completed,
                    DueUtc = due
                };
            }
            catch (ClickUpApiException ex) when (ex.StatusCode == 404)
            {
                // The task no longer exists in ClickUp.
                return null;
            }
        }

        public Task SetCompletedAsync(string sourceId, bool completed, CancellationToken cancellationToken)
        {
            return _service.SetCompletedAsync(sourceId, completed, cancellationToken);
        }

        public Task SetDueDateAsync(string sourceId, DateTime? dueUtc, CancellationToken cancellationToken)
        {
            return _service.SetDueDateAsync(sourceId, dueUtc, cancellationToken);
        }
    }
}
