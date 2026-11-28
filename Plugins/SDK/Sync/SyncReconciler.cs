using System;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Action that a synchronization pass should take for one record.
    /// </summary>
    public enum SyncAction
    {
        /// <summary>Nothing changed, no work required.</summary>
        None,

        /// <summary>Copy changed source values to the AIA item.</summary>
        SourceToAia,

        /// <summary>Copy changed AIA values to the source.</summary>
        AiaToSource,

        /// <summary>Both sides changed. Conflicts always resolve in favour of AIA.</summary>
        ConflictAiaWins
    }

    /// <summary>
    /// Pure decision logic for bidirectional synchronization. No I/O is performed here.
    /// Conflicts are always resolved in favour of AIA: when both sides changed, the AIA
    /// values win and are written back to the source where the adapter supports it.
    /// </summary>
    public static class SyncReconciler
    {
        /// <summary>
        /// Decides which action a synchronization pass should take for one record.
        /// </summary>
        /// <param name="record">Record with the last known baselines for both sides.</param>
        /// <param name="source">Current source state, or null when it is unavailable.</param>
        /// <param name="aiaCompleted">Current completion state of the AIA item.</param>
        /// <param name="aiaDueUtc">Current due date of the AIA item in UTC.</param>
        /// <param name="capabilities">Operations supported by the adapter.</param>
        /// <returns>
        /// <see cref="SyncAction.SourceToAia"/> when only the source changed,
        /// <see cref="SyncAction.AiaToSource"/> when only AIA changed and the matching fields are writable,
        /// <see cref="SyncAction.ConflictAiaWins"/> when both sides changed, otherwise <see cref="SyncAction.None"/>.
        /// Unsupported write directions never produce <see cref="SyncAction.AiaToSource"/>.
        /// </returns>
        public static SyncAction Decide(
            SyncRecord record,
            ExternalSyncState? source,
            bool aiaCompleted,
            DateTime? aiaDueUtc,
            SyncCapabilities capabilities)
        {
            ArgumentNullException.ThrowIfNull(record);

            var readCompletion = capabilities.HasFlag(SyncCapabilities.ReadSourceCompletion);
            var readDue = capabilities.HasFlag(SyncCapabilities.ReadSourceDueDate);
            var writeCompletion = capabilities.HasFlag(SyncCapabilities.WriteSourceCompletion);
            var writeDue = capabilities.HasFlag(SyncCapabilities.WriteSourceDueDate);

            var sourceCompletionChanged = source != null
                && readCompletion
                && source.IsCompleted != record.LastSourceCompleted;

            var sourceDueChanged = source != null
                && readDue
                && Normalize(source.DueUtc) != Normalize(record.LastSourceDueUtc);

            var sourceChanged = sourceCompletionChanged || sourceDueChanged;

            var aiaCompletionChanged = aiaCompleted != record.LastAiaCompleted;
            var aiaDueChanged = Normalize(aiaDueUtc) != Normalize(record.LastAiaDueUtc);
            var aiaChanged = aiaCompletionChanged || aiaDueChanged;

            if (sourceChanged && aiaChanged)
            {
                return SyncAction.ConflictAiaWins;
            }

            var writableAiaChange = (aiaCompletionChanged && writeCompletion) || (aiaDueChanged && writeDue);
            if (writableAiaChange)
            {
                return SyncAction.AiaToSource;
            }

            if (sourceChanged)
            {
                return SyncAction.SourceToAia;
            }

            return SyncAction.None;
        }

        /// <summary>
        /// Normalizes a timestamp to UTC with minute precision so that small formatting
        /// differences between the source system and AIA do not look like changes.
        /// </summary>
        /// <param name="value">Timestamp to normalize.</param>
        /// <returns>The normalized UTC timestamp, or null when the input is null.</returns>
        public static DateTime? Normalize(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var utc = value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime();
            return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
        }
    }
}
