using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Bidirectional synchronization engine between a plugin's external system and AIA tasks or reminders.
    /// </summary>
    /// <remarks>
    /// The coordinator keeps per-item baselines in a <see cref="SyncRecordStore"/> and lets
    /// <see cref="SyncReconciler"/> decide which side changed. Conflicts always resolve in favour of AIA.
    /// Host service calls are serialized with a semaphore, and AIA writes performed by the coordinator
    /// are suppressed so that the change events they raise do not feed back into the source system.
    /// The plugin must hold the Read/Write tasks and reminders permissions; when they are missing the
    /// guarded host services throw and the coordinator logs a warning without crashing.
    /// </remarks>
    public sealed class SyncCoordinator : IDisposable
    {
        private readonly IPluginContext _context;
        private readonly ISyncAdapter _adapter;
        private readonly SyncRecordStore _store;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<Guid, byte> _applying = new ConcurrentDictionary<Guid, byte>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private int _suppressEvents;
        private bool _started;
        private bool _disposed;

        /// <summary>
        /// Creates a coordinator for one adapter and one record store.
        /// </summary>
        /// <param name="context">Plugin context used to read and write AIA tasks and reminders.</param>
        /// <param name="adapter">Adapter for the external system.</param>
        /// <param name="store">Store that keeps the link records.</param>
        public SyncCoordinator(IPluginContext context, ISyncAdapter adapter, SyncRecordStore store)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Starts listening for AIA task and reminder changes so that AIA edits are pushed to the source.
        /// </summary>
        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _started = true;
            _context.Tasks.TasksChanged += OnTasksChanged;
            _context.Reminders.RemindersChanged += OnRemindersChanged;
        }

        /// <summary>
        /// Creates an AIA task for an external item and remembers the link.
        /// </summary>
        /// <param name="source">Current state of the external item.</param>
        /// <param name="title">Task title.</param>
        /// <param name="description">Task description.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task LinkTaskAsync(
            ExternalSyncState source,
            string title,
            string description,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(source);
            ObjectDisposedException.ThrowIf(_disposed, this);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await PluginUiThread.RunAsync(async () =>
                {
                Interlocked.Increment(ref _suppressEvents);
                try
                {
                    var task = _context.Tasks.CreateTask(title, description);
                    var url = source.SourceUrl ?? string.Empty;
                    task.Notes = string.IsNullOrWhiteSpace(task.Notes)
                        ? url
                        : task.Notes.TrimEnd() + Environment.NewLine + url;
                    task.DueDate = source.DueUtc?.ToLocalTime();
                    if (source.IsCompleted == true)
                    {
                        task.Status = TaskItemStatus.Completed;
                    }

                    await _context.Tasks.SaveAsync().ConfigureAwait(false);

                    _store.AddOrUpdate(new SyncRecord
                    {
                        SourceKey = _adapter.SourceKey,
                        SourceId = source.SourceId,
                        SourceUrl = url,
                        TargetKind = SyncTargetKind.Task,
                        AiaId = task.Id,
                        LastSourceCompleted = source.IsCompleted,
                        LastSourceDueUtc = SyncReconciler.Normalize(source.DueUtc),
                        LastAiaCompleted = task.Status == TaskItemStatus.Completed,
                        LastAiaDueUtc = SyncReconciler.Normalize(ToUtc(task.DueDate)),
                        LastSyncedUtc = DateTime.UtcNow
                    });
                    await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _suppressEvents);
                }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"Sync link failed for {_adapter.SourceKey}/{source.SourceId}: {ex.Message}");
                throw;
            }
            finally
            {
                ReleaseGate();
            }
        }

        /// <summary>
        /// Creates an AIA reminder for an external item and remembers the link.
        /// </summary>
        /// <param name="source">Current state of the external item.</param>
        /// <param name="title">Reminder title.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task LinkReminderAsync(
            ExternalSyncState source,
            string title,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(source);
            ObjectDisposedException.ThrowIf(_disposed, this);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await PluginUiThread.RunAsync(async () =>
                {
                Interlocked.Increment(ref _suppressEvents);
                try
                {
                    var due = source.DueUtc?.ToLocalTime() ?? DateTime.Now.AddHours(1);
                    var reminder = _context.Reminders.CreateReminder(title, due);
                    if (source.IsCompleted == true)
                    {
                        reminder.IsCompleted = true;
                    }

                    await _context.Reminders.SaveAsync().ConfigureAwait(false);

                    _store.AddOrUpdate(new SyncRecord
                    {
                        SourceKey = _adapter.SourceKey,
                        SourceId = source.SourceId,
                        SourceUrl = source.SourceUrl ?? string.Empty,
                        TargetKind = SyncTargetKind.Reminder,
                        AiaId = reminder.Id,
                        LastSourceCompleted = source.IsCompleted,
                        LastSourceDueUtc = SyncReconciler.Normalize(source.DueUtc),
                        LastAiaCompleted = reminder.IsCompleted,
                        LastAiaDueUtc = SyncReconciler.Normalize(ToUtc(reminder.DueDate)),
                        LastSyncedUtc = DateTime.UtcNow
                    });
                    await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _suppressEvents);
                }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"Sync link failed for {_adapter.SourceKey}/{source.SourceId}: {ex.Message}");
                throw;
            }
            finally
            {
                ReleaseGate();
            }
        }

        /// <summary>
        /// Reconciles all linked items against a snapshot of the external system.
        /// Pass the complete set of source items: records whose source item is absent are removed,
        /// because the item no longer exists in the external system.
        /// </summary>
        /// <param name="states">Current state of the external items.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ReconcileAsync(IEnumerable<ExternalSyncState> states, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(states);
            ObjectDisposedException.ThrowIf(_disposed, this);

            var bySourceId = new Dictionary<string, ExternalSyncState>(StringComparer.Ordinal);
            foreach (var state in states)
            {
                if (state == null || string.IsNullOrEmpty(state.SourceId))
                {
                    continue;
                }

                bySourceId[state.SourceId] = state;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await PluginUiThread.RunAsync(async () =>
                {
                var capabilities = _adapter.Capabilities;
                var readCompletion = capabilities.HasFlag(SyncCapabilities.ReadSourceCompletion);
                var readDue = capabilities.HasFlag(SyncCapabilities.ReadSourceDueDate);

                var records = _store.Records
                    .Where(r => string.Equals(r.SourceKey, _adapter.SourceKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var changed = false;

                foreach (var record in records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (!bySourceId.TryGetValue(record.SourceId, out var state))
                        {
                            changed |= _store.RemoveBySource(record.SourceKey, record.SourceId);
                            continue;
                        }

                        if (state.Error != null)
                        {
                            continue;
                        }

                        var (completed, dueUtc) = ReadAiaState(record.TargetKind, record.AiaId);
                        if (!completed.HasValue)
                        {
                            changed |= _store.RemoveBySource(record.SourceKey, record.SourceId);
                            continue;
                        }

                        var action = SyncReconciler.Decide(record, state, completed.Value, dueUtc, capabilities);
                        switch (action)
                        {
                            case SyncAction.SourceToAia:
                                ApplySourceToAia(record, state);
                                await SaveAiaAsync(record.TargetKind).ConfigureAwait(false);

                                var (postCompleted, postDue) = ReadAiaState(record.TargetKind, record.AiaId);
                                if (!postCompleted.HasValue)
                                {
                                    changed |= _store.RemoveBySource(record.SourceKey, record.SourceId);
                                    continue;
                                }

                                if (readCompletion)
                                {
                                    record.LastSourceCompleted = state.IsCompleted;
                                }

                                if (readDue)
                                {
                                    record.LastSourceDueUtc = SyncReconciler.Normalize(state.DueUtc);
                                }

                                record.LastAiaCompleted = postCompleted;
                                record.LastAiaDueUtc = SyncReconciler.Normalize(postDue);
                                break;

                            case SyncAction.AiaToSource:
                                await PushAiaToSourceAsync(record, state, completed.Value, dueUtc, true, cancellationToken)
                                    .ConfigureAwait(false);
                                break;

                            case SyncAction.ConflictAiaWins:
                                await PushAiaToSourceAsync(record, state, completed.Value, dueUtc, false, cancellationToken)
                                    .ConfigureAwait(false);
                                break;
                        }

                        record.LastSyncedUtc = DateTime.UtcNow;
                        changed = true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _context.Logger.Warning(
                            $"Sync reconcile failed for {record.SourceKey}/{record.SourceId}: {ex.Message}");
                    }
                }

                if (changed)
                {
                    await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
                }
                }).ConfigureAwait(false);
            }
            finally
            {
                ReleaseGate();
            }
        }

        /// <summary>
        /// Stops listening for changes and releases the coordinator resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_started)
            {
                _context.Tasks.TasksChanged -= OnTasksChanged;
                _context.Reminders.RemindersChanged -= OnRemindersChanged;
                _started = false;
            }

            _cts.Cancel();
            _cts.Dispose();
            _gate.Dispose();
        }

        private void OnTasksChanged(object? sender, TasksChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            switch (e.ChangeType)
            {
                case TaskChangeType.Reloaded:
                    return;

                case TaskChangeType.Deleted:
                    if (e.Task != null)
                    {
                        ScheduleRemoveLink(SyncTargetKind.Task, e.Task.Id);
                    }

                    return;

                default:
                    if (Volatile.Read(ref _suppressEvents) > 0)
                    {
                        return;
                    }

                    if (e.Task == null || _applying.ContainsKey(e.Task.Id))
                    {
                        return;
                    }

                    SchedulePush(SyncTargetKind.Task, e.Task.Id);
                    return;
            }
        }

        private void OnRemindersChanged(object? sender, RemindersChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            switch (e.ChangeType)
            {
                case ReminderChangeType.Reloaded:
                    return;

                case ReminderChangeType.Deleted:
                    if (e.Reminder != null)
                    {
                        ScheduleRemoveLink(SyncTargetKind.Reminder, e.Reminder.Id);
                    }

                    return;

                default:
                    if (Volatile.Read(ref _suppressEvents) > 0)
                    {
                        return;
                    }

                    if (e.Reminder == null || _applying.ContainsKey(e.Reminder.Id))
                    {
                        return;
                    }

                    SchedulePush(SyncTargetKind.Reminder, e.Reminder.Id);
                    return;
            }
        }

        private void SchedulePush(SyncTargetKind kind, Guid aiaId)
        {
            try
            {
                var token = _cts.Token;
                _ = Task.Run(() => PushAiaChangeAsync(kind, aiaId, token));
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void ScheduleRemoveLink(SyncTargetKind kind, Guid aiaId)
        {
            try
            {
                var token = _cts.Token;
                _ = Task.Run(() => RemoveLinkAsync(kind, aiaId, token));
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task PushAiaChangeAsync(SyncTargetKind kind, Guid aiaId, CancellationToken cancellationToken)
        {
            try
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                await PluginUiThread.RunAsync(async () =>
                {
                var records = _store.Records
                    .Where(r => r.AiaId == aiaId
                        && r.TargetKind == kind
                        && string.Equals(r.SourceKey, _adapter.SourceKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (records.Count == 0)
                {
                    return;
                }

                var (completed, dueUtc) = ReadAiaState(kind, aiaId);
                if (!completed.HasValue)
                {
                    return;
                }

                var capabilities = _adapter.Capabilities;
                var changed = false;

                foreach (var record in records)
                {
                    var completionChanged = completed != record.LastAiaCompleted;
                    var dueChanged = SyncReconciler.Normalize(dueUtc) != SyncReconciler.Normalize(record.LastAiaDueUtc);
                    var pushed = false;

                    if (completionChanged && capabilities.HasFlag(SyncCapabilities.WriteSourceCompletion))
                    {
                        await _adapter.SetCompletedAsync(record.SourceId, completed.Value, cancellationToken)
                            .ConfigureAwait(false);
                        record.LastSourceCompleted = completed;
                        pushed = true;
                    }

                    if (dueChanged && capabilities.HasFlag(SyncCapabilities.WriteSourceDueDate))
                    {
                        await _adapter.SetDueDateAsync(record.SourceId, SyncReconciler.Normalize(dueUtc), cancellationToken)
                            .ConfigureAwait(false);
                        record.LastSourceDueUtc = SyncReconciler.Normalize(dueUtc);
                        pushed = true;
                    }

                    if (pushed || completionChanged || dueChanged)
                    {
                        record.LastAiaCompleted = completed;
                        record.LastAiaDueUtc = SyncReconciler.Normalize(dueUtc);
                        record.LastSyncedUtc = DateTime.UtcNow;
                        changed = true;
                    }
                }

                if (changed)
                {
                    await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
                }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"Sync push failed for AIA item {aiaId}: {ex.Message}");
            }
            finally
            {
                ReleaseGate();
            }
        }

        private async Task RemoveLinkAsync(SyncTargetKind kind, Guid aiaId, CancellationToken cancellationToken)
        {
            try
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                var records = _store.Records
                    .Where(r => r.AiaId == aiaId && r.TargetKind == kind)
                    .ToList();

                if (records.Count == 0)
                {
                    return;
                }

                foreach (var record in records)
                {
                    _store.RemoveBySource(record.SourceKey, record.SourceId);
                }

                await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"Sync unlink failed for AIA item {aiaId}: {ex.Message}");
            }
            finally
            {
                ReleaseGate();
            }
        }

        private void ApplySourceToAia(SyncRecord record, ExternalSyncState state)
        {
            var capabilities = _adapter.Capabilities;
            var readCompletion = capabilities.HasFlag(SyncCapabilities.ReadSourceCompletion);
            var readDue = capabilities.HasFlag(SyncCapabilities.ReadSourceDueDate);

            _applying[record.AiaId] = 0;
            Interlocked.Increment(ref _suppressEvents);
            try
            {
                if (record.TargetKind == SyncTargetKind.Task)
                {
                    var task = _context.Tasks.GetTaskById(record.AiaId);
                    if (task == null)
                    {
                        return;
                    }

                    if (readCompletion && state.IsCompleted.HasValue)
                    {
                        task.Status = state.IsCompleted.Value ? TaskItemStatus.Completed : TaskItemStatus.NotStarted;
                    }

                    if (readDue)
                    {
                        task.DueDate = state.DueUtc?.ToLocalTime();
                    }
                }
                else
                {
                    var reminder = _context.Reminders.GetReminderById(record.AiaId);
                    if (reminder == null)
                    {
                        return;
                    }

                    if (readCompletion && state.IsCompleted.HasValue)
                    {
                        reminder.IsCompleted = state.IsCompleted.Value;
                    }

                    if (readDue && state.DueUtc.HasValue)
                    {
                        reminder.DueDate = state.DueUtc.Value.ToLocalTime();
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _suppressEvents);
                _applying.TryRemove(record.AiaId, out _);
            }
        }

        private async Task PushAiaToSourceAsync(
            SyncRecord record,
            ExternalSyncState state,
            bool completed,
            DateTime? dueUtc,
            bool changedFieldsOnly,
            CancellationToken cancellationToken)
        {
            var capabilities = _adapter.Capabilities;
            var writeCompletion = capabilities.HasFlag(SyncCapabilities.WriteSourceCompletion);
            var writeDue = capabilities.HasFlag(SyncCapabilities.WriteSourceDueDate);
            var readCompletion = capabilities.HasFlag(SyncCapabilities.ReadSourceCompletion);
            var readDue = capabilities.HasFlag(SyncCapabilities.ReadSourceDueDate);

            var completionChanged = completed != record.LastAiaCompleted;
            var normalizedDue = SyncReconciler.Normalize(dueUtc);
            var dueChanged = normalizedDue != SyncReconciler.Normalize(record.LastAiaDueUtc);

            var pushedCompletion = false;
            var pushedDue = false;

            if ((!changedFieldsOnly || completionChanged) && writeCompletion)
            {
                await _adapter.SetCompletedAsync(record.SourceId, completed, cancellationToken).ConfigureAwait(false);
                pushedCompletion = true;
            }

            if ((!changedFieldsOnly || dueChanged) && writeDue)
            {
                await _adapter.SetDueDateAsync(record.SourceId, normalizedDue, cancellationToken).ConfigureAwait(false);
                pushedDue = true;
            }

            if (pushedCompletion)
            {
                record.LastSourceCompleted = completed;
            }
            else if (readCompletion)
            {
                record.LastSourceCompleted = state.IsCompleted;
            }

            if (pushedDue)
            {
                record.LastSourceDueUtc = normalizedDue;
            }
            else if (readDue)
            {
                record.LastSourceDueUtc = SyncReconciler.Normalize(state.DueUtc);
            }

            record.LastAiaCompleted = completed;
            record.LastAiaDueUtc = normalizedDue;
            record.LastSyncedUtc = DateTime.UtcNow;
        }

        private (bool? Completed, DateTime? DueUtc) ReadAiaState(SyncTargetKind kind, Guid aiaId)
        {
            if (kind == SyncTargetKind.Task)
            {
                var task = _context.Tasks.GetTaskById(aiaId);
                return task == null
                    ? (null, null)
                    : (task.Status == TaskItemStatus.Completed, ToUtc(task.DueDate));
            }

            var reminder = _context.Reminders.GetReminderById(aiaId);
            return reminder == null
                ? (null, null)
                : (reminder.IsCompleted, ToUtc(reminder.DueDate));
        }

        private async Task SaveAiaAsync(SyncTargetKind kind)
        {
            if (kind == SyncTargetKind.Task)
            {
                await _context.Tasks.SaveAsync().ConfigureAwait(false);
            }
            else
            {
                await _context.Reminders.SaveAsync().ConfigureAwait(false);
            }
        }

        private void ReleaseGate()
        {
            try
            {
                _gate.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static DateTime? ToUtc(DateTime? value)
        {
            return value.HasValue ? value.Value.ToUniversalTime() : null;
        }

        private static DateTime? ToUtc(DateTime value)
        {
            return value.ToUniversalTime();
        }
    }
}
