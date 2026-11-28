using System;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;

namespace AIA.Plugins.GoogleChat
{
    /// <summary>
    /// Sync adapter for Google Chat links.
    /// </summary>
    /// <remarks>
    /// Google Chat spaces and messages have no completion or due date fields, so the adapter only
    /// exposes identity and links and does not support any bidirectional synchronization.
    /// </remarks>
    public class GoogleChatSyncAdapter : ISyncAdapter
    {
        /// <summary>
        /// Plugin-specific source key for Google Chat items.
        /// </summary>
        public const string SourceKeyValue = "googlechat";

        /// <summary>
        /// Creates an adapter that does not hold a service reference.
        /// </summary>
        public GoogleChatSyncAdapter()
        {
        }

        /// <summary>
        /// Creates an adapter that keeps the service for callers that share it with the plugin tab.
        /// </summary>
        /// <param name="service">Google Chat service used by the plugin.</param>
        public GoogleChatSyncAdapter(GoogleChatService service)
        {
            Service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Google Chat service associated with the adapter, when one was supplied.
        /// </summary>
        public GoogleChatService? Service { get; }

        /// <inheritdoc />
        public string SourceKey => SourceKeyValue;

        /// <inheritdoc />
        public SyncCapabilities Capabilities => SyncCapabilities.None;

        /// <inheritdoc />
        public Task<ExternalSyncState?> GetStateAsync(string sourceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return Task.FromResult<ExternalSyncState?>(null);
            }

            var id = sourceId.Trim();
            var state = new ExternalSyncState
            {
                SourceId = id,
                SourceUrl = GoogleChatUrl.ForSpaceOrMessage(id),
                IsCompleted = null,
                DueUtc = null
            };

            return Task.FromResult<ExternalSyncState?>(state);
        }

        /// <inheritdoc />
        public Task SetCompletedAsync(string sourceId, bool completed, CancellationToken cancellationToken)
            => throw new NotSupportedException("Google Chat items do not have a completion state.");

        /// <inheritdoc />
        public Task SetDueDateAsync(string sourceId, DateTime? dueUtc, CancellationToken cancellationToken)
            => throw new NotSupportedException("Google Chat items do not have a due date.");
    }
}
