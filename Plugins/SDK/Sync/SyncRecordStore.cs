using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Persists the mapping between external source items and AIA tasks or reminders.
    /// Records are stored as a JSON array inside the plugin settings.
    /// </summary>
    public class SyncRecordStore
    {
        private const string DefaultSettingsKey = "sync.records";

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        private readonly IPluginSettingsStorage _storage;
        private readonly string _settingsKey;
        private List<SyncRecord> _records = new List<SyncRecord>();

        /// <summary>
        /// Creates a store that persists records in the given plugin settings storage.
        /// </summary>
        /// <param name="storage">Plugin settings storage.</param>
        /// <param name="settingsKey">Settings key that holds the serialized records.</param>
        public SyncRecordStore(IPluginSettingsStorage storage, string settingsKey = DefaultSettingsKey)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _settingsKey = string.IsNullOrWhiteSpace(settingsKey) ? DefaultSettingsKey : settingsKey;
        }

        /// <summary>
        /// Gets the currently loaded records.
        /// </summary>
        public IReadOnlyList<SyncRecord> Records => _records;

        /// <summary>
        /// Loads records from settings. Malformed JSON yields an empty list instead of throwing.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _storage.LoadAsync().ConfigureAwait(false);
            _records = Deserialize(_storage.Get<string>(_settingsKey));
        }

        /// <summary>
        /// Serializes the records into settings and saves them.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _storage.Set(_settingsKey, JsonSerializer.Serialize(_records, SerializerOptions));
            await _storage.SaveAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Finds the record that links the given AIA item.
        /// </summary>
        /// <param name="id">Identifier of the AIA task or reminder.</param>
        /// <returns>The matching record, or null when the item is not linked.</returns>
        public SyncRecord? FindByAiaId(Guid id)
        {
            return _records.FirstOrDefault(r => r.AiaId == id);
        }

        /// <summary>
        /// Finds all records for the given external item.
        /// </summary>
        /// <param name="sourceKey">Plugin-specific source key.</param>
        /// <param name="sourceId">Identifier of the item in the external system.</param>
        public IEnumerable<SyncRecord> FindBySource(string sourceKey, string sourceId)
        {
            return _records.Where(r => MatchesSource(r, sourceKey, sourceId));
        }

        /// <summary>
        /// Adds a record, or replaces the existing record for the same external item.
        /// </summary>
        /// <param name="record">Record to add or update.</param>
        public void AddOrUpdate(SyncRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            var index = _records.FindIndex(r => MatchesSource(r, record.SourceKey, record.SourceId));
            if (index >= 0)
            {
                _records[index] = record;
            }
            else
            {
                _records.Add(record);
            }
        }

        /// <summary>
        /// Removes the record that links the given AIA item.
        /// </summary>
        /// <param name="id">Identifier of the AIA task or reminder.</param>
        /// <returns>True when a record was removed.</returns>
        public bool RemoveByAiaId(Guid id)
        {
            return _records.RemoveAll(r => r.AiaId == id) > 0;
        }

        /// <summary>
        /// Removes the records for the given external item.
        /// </summary>
        /// <param name="sourceKey">Plugin-specific source key.</param>
        /// <param name="sourceId">Identifier of the item in the external system.</param>
        /// <returns>True when a record was removed.</returns>
        public bool RemoveBySource(string sourceKey, string sourceId)
        {
            return _records.RemoveAll(r => MatchesSource(r, sourceKey, sourceId)) > 0;
        }

        private static bool MatchesSource(SyncRecord record, string sourceKey, string sourceId)
        {
            return string.Equals(record.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(record.SourceId, sourceId, StringComparison.Ordinal);
        }

        private static List<SyncRecord> Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<SyncRecord>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<SyncRecord>>(json, SerializerOptions) ?? new List<SyncRecord>();
            }
            catch (JsonException)
            {
                return new List<SyncRecord>();
            }
            catch (NotSupportedException)
            {
                return new List<SyncRecord>();
            }
        }
    }
}
