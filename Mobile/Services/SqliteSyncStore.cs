using System.Text.Json;
using AIA.Sync.Contracts;
using AIA.Sync.Core;
using Microsoft.Maui.Storage;
using SQLite;

namespace AIA.Mobile.Services
{
    [Table("entities")]
    public class LocalEntity
    {
        [PrimaryKey]
        public string Key { get; set; } = string.Empty;
        public int EntityType { get; set; }
        public string EntityId { get; set; } = string.Empty;
        public long Revision { get; set; }
        public long BaseRevision { get; set; }
        public bool Dirty { get; set; }
        public bool Deleted { get; set; }
        public string DataJson { get; set; } = string.Empty;
        public string ModifiedUtc { get; set; } = string.Empty;
    }

    [Table("meta")]
    public class MetaRow
    {
        [PrimaryKey]
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    [Table("calendar_map")]
    public class CalendarMapRow
    {
        [PrimaryKey]
        public string Key { get; set; } = string.Empty;
        public string DeviceEventId { get; set; } = string.Empty;
        public string CalendarId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
    }

    /// <summary>
    /// Local offline-first storage. Every entity keeps the server revision it was based on so a
    /// push can detect desktop edits.
    /// </summary>
    public sealed class SqliteSyncStore : ISyncLocalStore
    {
        private readonly SemaphoreSlim _initGate = new(1, 1);
        private SQLiteAsyncConnection? _connection;
        private string? _databasePath;

        public string DatabasePath => _databasePath ??= Path.Combine(FileSystem.AppDataDirectory, "aia-mobile.db");

        public async Task InitializeAsync()
        {
            if (_connection != null)
                return;

            await _initGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_connection != null)
                    return;

                Directory.CreateDirectory(FileSystem.AppDataDirectory);
                var connection = new SQLiteAsyncConnection(DatabasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
                await connection.CreateTableAsync<LocalEntity>().ConfigureAwait(false);
                await connection.CreateTableAsync<MetaRow>().ConfigureAwait(false);
                await connection.CreateTableAsync<CalendarMapRow>().ConfigureAwait(false);
                _connection = connection;
            }
            finally
            {
                _initGate.Release();
            }
        }

        private async Task<SQLiteAsyncConnection> GetConnectionAsync()
        {
            await InitializeAsync().ConfigureAwait(false);
            return _connection!;
        }

        public async Task<long> GetServerRevisionAsync(CancellationToken cancellationToken)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            var row = await connection.FindAsync<MetaRow>("serverRevision").ConfigureAwait(false);
            return row != null && long.TryParse(row.Value, out var revision) ? revision : 0;
        }

        public async Task SetServerRevisionAsync(long revision, CancellationToken cancellationToken)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            await connection.InsertOrReplaceAsync(new MetaRow { Key = "serverRevision", Value = revision.ToString() }).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<SyncEntityChange>> GetPendingChangesAsync(CancellationToken cancellationToken)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            var rows = await connection.Table<LocalEntity>().Where(e => e.Dirty).ToListAsync().ConfigureAwait(false);
            var changes = new List<SyncEntityChange>(rows.Count);

            foreach (var row in rows)
            {
                JsonElement? data = null;
                if (!row.Deleted && !string.IsNullOrWhiteSpace(row.DataJson))
                {
                    using var document = JsonDocument.Parse(row.DataJson);
                    data = document.RootElement.Clone();
                }

                changes.Add(new SyncEntityChange
                {
                    EntityType = (SyncEntityType)row.EntityType,
                    Id = Guid.Parse(row.EntityId),
                    BaseRevision = row.BaseRevision,
                    IsDeleted = row.Deleted,
                    Data = data
                });
            }

            return changes;
        }

        public async Task ApplyServerChangesAsync(IReadOnlyList<SyncEntityEnvelope> changes, CancellationToken cancellationToken)
        {
            if (changes.Count == 0)
                return;

            var connection = await GetConnectionAsync().ConfigureAwait(false);

            foreach (var change in changes)
            {
                var key = Key(change.EntityType, change.Id);
                var existing = await connection.FindAsync<LocalEntity>(key).ConfigureAwait(false);
                if (existing != null && existing.Revision > change.Revision)
                    continue;

                if (change.IsDeleted)
                {
                    await connection.DeleteAsync<LocalEntity>(key).ConfigureAwait(false);
                    await connection.DeleteAsync<CalendarMapRow>(key).ConfigureAwait(false);
                    continue;
                }

                var row = existing ?? new LocalEntity
                {
                    Key = key,
                    EntityType = (int)change.EntityType,
                    EntityId = change.Id.ToString("N")
                };

                row.Revision = change.Revision;
                row.BaseRevision = change.Revision;
                row.Dirty = false;
                row.Deleted = false;
                row.DataJson = change.Data?.GetRawText() ?? "{}";
                row.ModifiedUtc = change.ModifiedUtc.ToString("O");
                await connection.InsertOrReplaceAsync(row).ConfigureAwait(false);
            }
        }

        public async Task ResetFromServerAsync(IReadOnlyList<SyncEntityEnvelope> snapshot, CancellationToken cancellationToken)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            await connection.DeleteAllAsync<LocalEntity>().ConfigureAwait(false);
            await connection.DeleteAllAsync<CalendarMapRow>().ConfigureAwait(false);
            await ApplyServerChangesAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        public async Task MarkAcceptedAsync(IReadOnlyList<SyncAck> accepted, CancellationToken cancellationToken)
        {
            if (accepted.Count == 0)
                return;

            var connection = await GetConnectionAsync().ConfigureAwait(false);
            foreach (var ack in accepted)
            {
                var key = Key(ack.EntityType, ack.Id);
                var row = await connection.FindAsync<LocalEntity>(key).ConfigureAwait(false);
                if (row == null)
                    continue;

                row.Revision = ack.Revision;
                row.BaseRevision = ack.Revision;
                row.Dirty = false;
                await connection.UpdateAsync(row).ConfigureAwait(false);
            }
        }

        public async Task<List<TaskDto>> GetTasksAsync()
        {
            var rows = await GetEntitiesAsync(SyncEntityType.Task).ConfigureAwait(false);
            return rows.Select(r => JsonSerializer.Deserialize<TaskDto>(r.DataJson, SyncJson.Options))
                .Where(t => t != null)
                .Select(t => t!)
                .OrderBy(t => t.IsArchived)
                .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenBy(t => t.Title)
                .ToList();
        }

        public async Task<List<ReminderDto>> GetRemindersAsync()
        {
            var rows = await GetEntitiesAsync(SyncEntityType.Reminder).ConfigureAwait(false);
            return rows.Select(r => JsonSerializer.Deserialize<ReminderDto>(r.DataJson, SyncJson.Options))
                .Where(r => r != null)
                .Select(r => r!)
                .OrderBy(r => r.IsCompleted)
                .ThenBy(r => r.DueDate)
                .ToList();
        }

        public async Task<List<CalendarEventDto>> GetCalendarEventsAsync()
        {
            var rows = await GetEntitiesAsync(SyncEntityType.CalendarEvent).ConfigureAwait(false);
            return rows.Select(r => JsonSerializer.Deserialize<CalendarEventDto>(r.DataJson, SyncJson.Options))
                .Where(e => e != null)
                .Select(e => e!)
                .OrderBy(e => e.Start)
                .ToList();
        }

        private async Task<List<LocalEntity>> GetEntitiesAsync(SyncEntityType entityType)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            return await connection.Table<LocalEntity>()
                .Where(e => e.EntityType == (int)entityType && !e.Deleted)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task SaveTaskAsync(TaskDto task)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            var key = Key(SyncEntityType.Task, task.Id);
            var existing = await connection.FindAsync<LocalEntity>(key).ConfigureAwait(false);
            var baseRevision = existing == null ? 0 : existing.BaseRevision;

            await connection.InsertOrReplaceAsync(new LocalEntity
            {
                Key = key,
                EntityType = (int)SyncEntityType.Task,
                EntityId = task.Id.ToString("N"),
                Revision = existing?.Revision ?? 0,
                BaseRevision = baseRevision,
                Dirty = true,
                Deleted = false,
                DataJson = JsonSerializer.Serialize(task, SyncJson.Options),
                ModifiedUtc = DateTimeOffset.UtcNow.ToString("O")
            }).ConfigureAwait(false);
        }

        public async Task SaveReminderAsync(ReminderDto reminder)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            var key = Key(SyncEntityType.Reminder, reminder.Id);
            var existing = await connection.FindAsync<LocalEntity>(key).ConfigureAwait(false);
            var baseRevision = existing == null ? 0 : existing.BaseRevision;

            await connection.InsertOrReplaceAsync(new LocalEntity
            {
                Key = key,
                EntityType = (int)SyncEntityType.Reminder,
                EntityId = reminder.Id.ToString("N"),
                Revision = existing?.Revision ?? 0,
                BaseRevision = baseRevision,
                Dirty = true,
                Deleted = false,
                DataJson = JsonSerializer.Serialize(reminder, SyncJson.Options),
                ModifiedUtc = DateTimeOffset.UtcNow.ToString("O")
            }).ConfigureAwait(false);
        }

        public async Task SaveCalendarEventAsync(CalendarEventDto calendarEvent, bool markDirty = true)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            var key = Key(SyncEntityType.CalendarEvent, calendarEvent.Id);
            var existing = await connection.FindAsync<LocalEntity>(key).ConfigureAwait(false);
            var baseRevision = existing == null ? 0 : existing.BaseRevision;

            await connection.InsertOrReplaceAsync(new LocalEntity
            {
                Key = key,
                EntityType = (int)SyncEntityType.CalendarEvent,
                EntityId = calendarEvent.Id.ToString("N"),
                Revision = existing?.Revision ?? 0,
                BaseRevision = baseRevision,
                Dirty = markDirty,
                Deleted = false,
                DataJson = JsonSerializer.Serialize(calendarEvent, SyncJson.Options),
                ModifiedUtc = DateTimeOffset.UtcNow.ToString("O")
            }).ConfigureAwait(false);
        }

        public async Task DeleteAsync(SyncEntityType entityType, Guid id)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            var key = Key(entityType, id);
            var existing = await connection.FindAsync<LocalEntity>(key).ConfigureAwait(false);
            if (existing == null)
                return;

            if (existing.Revision == 0)
            {
                await connection.DeleteAsync<LocalEntity>(key).ConfigureAwait(false);
                await connection.DeleteAsync<CalendarMapRow>(key).ConfigureAwait(false);
                return;
            }

            existing.Deleted = true;
            existing.Dirty = true;
            existing.DataJson = string.Empty;
            existing.ModifiedUtc = DateTimeOffset.UtcNow.ToString("O");
            await connection.UpdateAsync(existing).ConfigureAwait(false);
        }

        public async Task<CalendarMapRow?> GetCalendarMappingAsync(string key)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            return await connection.FindAsync<CalendarMapRow>(key).ConfigureAwait(false);
        }

        public async Task<List<CalendarMapRow>> GetCalendarMappingsAsync(string kind)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            return await connection.Table<CalendarMapRow>().Where(m => m.Kind == kind).ToListAsync().ConfigureAwait(false);
        }

        public async Task SaveCalendarMappingAsync(CalendarMapRow mapping)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            await connection.InsertOrReplaceAsync(mapping).ConfigureAwait(false);
        }

        public async Task DeleteCalendarMappingAsync(string key)
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            await connection.DeleteAsync<CalendarMapRow>(key).ConfigureAwait(false);
        }

        public static string Key(SyncEntityType entityType, Guid id) => $"{entityType}:{id:N}";
    }
}
