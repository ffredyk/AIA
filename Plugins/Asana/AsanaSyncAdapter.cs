using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Asana
{
    /// <summary>
    /// Synchronizes AIA tasks and reminders with Asana tasks.
    /// </summary>
    public sealed class AsanaSyncAdapter : ISyncAdapter
    {
        private readonly AsanaService _service;

        public AsanaSyncAdapter(AsanaService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string SourceKey => "asana";

        public SyncCapabilities Capabilities =>
            SyncCapabilities.ReadSourceCompletion |
            SyncCapabilities.WriteSourceCompletion |
            SyncCapabilities.ReadSourceDueDate |
            SyncCapabilities.WriteSourceDueDate;

        public async Task<ExternalSyncState?> GetStateAsync(string sourceId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

            try
            {
                var (completed, due, url) = await _service.GetTaskSyncStateAsync(sourceId, cancellationToken).ConfigureAwait(false);

                return new ExternalSyncState
                {
                    SourceId = sourceId,
                    SourceUrl = url,
                    IsCompleted = completed,
                    DueUtc = due
                };
            }
            catch (AsanaApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public Task SetCompletedAsync(string sourceId, bool completed, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
            return _service.SetCompletedAsync(sourceId, completed, cancellationToken);
        }

        public Task SetDueDateAsync(string sourceId, DateTime? dueUtc, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
            return _service.SetDueDateAsync(sourceId, dueUtc, cancellationToken);
        }
    }
}
