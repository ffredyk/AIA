using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AIA.Services
{
    /// <summary>
    /// Serializes and atomically persists JSON files so concurrent writers can never
    /// truncate or corrupt user data.
    /// </summary>
    public static class JsonFileStore
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks =
            new(StringComparer.OrdinalIgnoreCase);

        public static SemaphoreSlim GetLock(string path) =>
            Locks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

        /// <summary>
        /// Writes JSON via a temporary file and an atomic replace.
        /// </summary>
        public static async Task WriteAtomicAsync(string path, string json)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = path + ".tmp";

            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, path, true);
        }

        public static async Task WriteAsync<T>(string path, T value, JsonSerializerOptions options)
        {
            var semaphore = GetLock(path);
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var json = JsonSerializer.Serialize(value, options);
                await WriteAtomicAsync(path, json).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        }

        public static async Task<T?> ReadAsync<T>(string path, JsonSerializerOptions options)
        {
            var semaphore = GetLock(path);
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                return JsonSerializer.Deserialize<T>(json, options);
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Renames a file that failed to parse so it is preserved for recovery instead of
        /// being silently overwritten.
        /// </summary>
        public static string Quarantine(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return path;

                var quarantinePath = $"{path}.corrupt-{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Move(path, quarantinePath, false);
                return quarantinePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to quarantine '{path}': {ex.Message}");
                return path;
            }
        }
    }

    /// <summary>
    /// Thrown when a persisted data file exists but cannot be read. Callers must not treat this
    /// as "no data" — doing so would overwrite user content.
    /// </summary>
    public class DataLoadException : Exception
    {
        public string FilePath { get; }
        public string? QuarantinedPath { get; }

        public DataLoadException(string filePath, string? quarantinedPath, Exception inner)
            : base($"Failed to load data from '{filePath}'", inner)
        {
            FilePath = filePath;
            QuarantinedPath = quarantinedPath;
        }
    }
}
