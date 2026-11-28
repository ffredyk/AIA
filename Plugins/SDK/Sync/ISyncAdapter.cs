using System;
using System.Threading;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Operations that a plugin sync adapter supports against its external system.
    /// </summary>
    [Flags]
    public enum SyncCapabilities
    {
        /// <summary>No synchronization support.</summary>
        None = 0,

        /// <summary>The adapter can read completion state from the source.</summary>
        ReadSourceCompletion = 1 << 0,

        /// <summary>The adapter can write completion state to the source.</summary>
        WriteSourceCompletion = 1 << 1,

        /// <summary>The adapter can read due dates from the source.</summary>
        ReadSourceDueDate = 1 << 2,

        /// <summary>The adapter can write due dates to the source.</summary>
        WriteSourceDueDate = 1 << 3
    }

    /// <summary>
    /// Snapshot of an external item that is relevant for synchronization.
    /// </summary>
    public class ExternalSyncState
    {
        /// <summary>
        /// Identifier of the item in the external system.
        /// </summary>
        public string SourceId { get; set; } = string.Empty;

        /// <summary>
        /// Web link to the item in the external system.
        /// </summary>
        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Title of the item.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Completion state of the item, or null when unknown.
        /// </summary>
        public bool? IsCompleted { get; set; }

        /// <summary>
        /// Due date of the item in UTC, or null when the item has no due date.
        /// </summary>
        public DateTime? DueUtc { get; set; }

        /// <summary>
        /// Error that prevented reading the item, or null on success.
        /// </summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// Adapter that lets a <see cref="SyncCoordinator"/> read and write a plugin's external system.
    /// </summary>
    public interface ISyncAdapter
    {
        /// <summary>
        /// Plugin-specific source key, for example "github".
        /// </summary>
        string SourceKey { get; }

        /// <summary>
        /// Operations supported against the external system.
        /// </summary>
        SyncCapabilities Capabilities { get; }

        /// <summary>
        /// Reads the current state of an external item.
        /// </summary>
        /// <param name="sourceId">Identifier of the item in the external system.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The current state, or null when the item no longer exists.</returns>
        Task<ExternalSyncState?> GetStateAsync(string sourceId, CancellationToken cancellationToken);

        /// <summary>
        /// Writes the completion state of an external item.
        /// </summary>
        /// <param name="sourceId">Identifier of the item in the external system.</param>
        /// <param name="completed">New completion state.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SetCompletedAsync(string sourceId, bool completed, CancellationToken cancellationToken);

        /// <summary>
        /// Writes the due date of an external item.
        /// </summary>
        /// <param name="sourceId">Identifier of the item in the external system.</param>
        /// <param name="dueUtc">New due date in UTC, or null to clear it.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SetDueDateAsync(string sourceId, DateTime? dueUtc, CancellationToken cancellationToken);
    }
}
