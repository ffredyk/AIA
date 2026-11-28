using System;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Kind of AIA item that a <see cref="SyncRecord"/> points at.
    /// </summary>
    public enum SyncTargetKind
    {
        /// <summary>AIA task.</summary>
        Task,

        /// <summary>AIA reminder.</summary>
        Reminder
    }

    /// <summary>
    /// Mapping between one external source item and one AIA task or reminder.
    /// The record also stores the last observed state on both sides, which is used
    /// as the synchronization baseline.
    /// </summary>
    public class SyncRecord
    {
        /// <summary>
        /// Plugin-specific source key, for example "github".
        /// </summary>
        public string SourceKey { get; set; } = string.Empty;

        /// <summary>
        /// Identifier of the item in the external system.
        /// </summary>
        public string SourceId { get; set; } = string.Empty;

        /// <summary>
        /// Web link to the item in the external system.
        /// </summary>
        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Kind of the linked AIA item.
        /// </summary>
        public SyncTargetKind TargetKind { get; set; }

        /// <summary>
        /// Identifier of the linked AIA task or reminder.
        /// </summary>
        public Guid AiaId { get; set; }

        /// <summary>
        /// Last observed completion state in the external system.
        /// </summary>
        public bool? LastSourceCompleted { get; set; }

        /// <summary>
        /// Last observed due date in the external system (UTC).
        /// </summary>
        public DateTime? LastSourceDueUtc { get; set; }

        /// <summary>
        /// Last observed completion state of the AIA item.
        /// </summary>
        public bool? LastAiaCompleted { get; set; }

        /// <summary>
        /// Last observed due date of the AIA item (UTC).
        /// </summary>
        public DateTime? LastAiaDueUtc { get; set; }

        /// <summary>
        /// Timestamp of the last successful synchronization (UTC).
        /// </summary>
        public DateTime LastSyncedUtc { get; set; }
    }
}
