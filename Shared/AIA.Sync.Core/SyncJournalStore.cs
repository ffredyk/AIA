using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIA.Sync.Core
{
    /// <summary>
    /// Atomic JSON persistence for the sync journal.
    /// </summary>
    public sealed class JsonSyncJournalStore : ISyncJournalStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _path;

        public JsonSyncJournalStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public async Task<SyncJournalState> LoadAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_path))
                return new SyncJournalState();

            try
            {
                await using var stream = File.OpenRead(_path);
                var state = await JsonSerializer.DeserializeAsync<SyncJournalState>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return state ?? new SyncJournalState();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                TryQuarantine(ex);
                return new SyncJournalState();
            }
        }

        public async Task SaveAsync(SyncJournalState state, CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = _path + ".tmp";

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _path, true);
        }

        private void TryQuarantine(Exception source)
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                var target = $"{_path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
                File.Move(_path, target, true);
                System.Diagnostics.Debug.WriteLine($"Sync journal could not be read ({source.Message}); preserved at '{target}'.");
            }
            catch
            {
                // Quarantine is best effort. A fresh journal still lets clients resync from scratch.
            }
        }
    }
}
