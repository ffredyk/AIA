using System;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.GoogleGmail
{
    /// <summary>
    /// Sync adapter that maps Gmail messages onto AIA tasks and reminders.
    /// </summary>
    public class GmailSyncAdapter : ISyncAdapter
    {
        private readonly GmailService _service;

        public GmailSyncAdapter(GmailService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string SourceKey => "gmail";

        public SyncCapabilities Capabilities => SyncCapabilities.ReadSourceCompletion | SyncCapabilities.WriteSourceCompletion;

        public Task<ExternalSyncState?> GetStateAsync(string sourceId, CancellationToken cancellationToken)
        {
            return GetStateCoreAsync(sourceId, cancellationToken);
        }

        public async Task SetCompletedAsync(string sourceId, bool completed, CancellationToken cancellationToken)
        {
            if (completed)
            {
                await _service.MarkReadAsync(sourceId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _service.MarkUnreadAsync(sourceId, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task SetDueDateAsync(string sourceId, DateTime? dueUtc, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Gmail messages do not support due dates.");
        }

        private async Task<ExternalSyncState?> GetStateCoreAsync(string sourceId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("Gmail message id is required.", nameof(sourceId));

            return await _service.GetSyncStateAsync(sourceId, cancellationToken).ConfigureAwait(false);
        }
    }
}
