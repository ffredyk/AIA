using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIA.Plugins.SDK;
using Xunit;

namespace AIA.Plugins.Tests;

public class SyncRecordStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateTime DueUtc = new(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AddOrUpdate_AddsNewRecord()
    {
        var store = CreateStore();
        var record = CreateRecord("github", "1");

        store.AddOrUpdate(record);

        Assert.Single(store.Records);
        Assert.Same(record, store.FindByAiaId(record.AiaId));
    }

    [Fact]
    public void AddOrUpdate_ReplacesRecordForSameSource()
    {
        var store = CreateStore();
        var first = CreateRecord("github", "1");
        var replacement = CreateRecord("GitHub", "1");

        store.AddOrUpdate(first);
        store.AddOrUpdate(replacement);

        Assert.Single(store.Records);
        Assert.Null(store.FindByAiaId(first.AiaId));
        Assert.Same(replacement, store.FindByAiaId(replacement.AiaId));
    }

    [Fact]
    public void FindBySource_FiltersByKeyAndId()
    {
        var store = CreateStore();
        var match = CreateRecord("github", "1");
        store.AddOrUpdate(match);
        store.AddOrUpdate(CreateRecord("github", "2"));
        store.AddOrUpdate(CreateRecord("jira", "1"));

        var found = store.FindBySource("GITHUB", "1").ToList();

        Assert.Single(found);
        Assert.Same(match, found[0]);
        Assert.Empty(store.FindBySource("github", "3"));
    }

    [Fact]
    public void RemoveByAiaId_RemovesMatchingRecord()
    {
        var store = CreateStore();
        var record = CreateRecord("github", "1");
        store.AddOrUpdate(record);

        Assert.True(store.RemoveByAiaId(record.AiaId));
        Assert.Empty(store.Records);
        Assert.False(store.RemoveByAiaId(record.AiaId));
    }

    [Fact]
    public void RemoveBySource_RemovesMatchingRecord()
    {
        var store = CreateStore();
        store.AddOrUpdate(CreateRecord("github", "1"));
        var other = CreateRecord("github", "2");
        store.AddOrUpdate(other);

        Assert.True(store.RemoveBySource("github", "1"));
        Assert.Single(store.Records);
        Assert.Same(other, store.Records[0]);
        Assert.False(store.RemoveBySource("github", "1"));
    }

    [Fact]
    public async Task SaveAsyncThenLoadAsync_RoundTripsRecords()
    {
        var storage = new FakePluginSettingsStorage();
        var original = CreateRecord("github", "1");
        original.LastSourceCompleted = true;
        original.LastAiaCompleted = null;
        original.LastSourceDueUtc = DueUtc;
        original.LastAiaDueUtc = null;
        original.TargetKind = SyncTargetKind.Reminder;
        original.SourceUrl = "https://example.com/items/1";
        original.LastSyncedUtc = DueUtc.AddMinutes(5);

        var savingStore = new SyncRecordStore(storage);
        savingStore.AddOrUpdate(original);
        await savingStore.SaveAsync(None);

        Assert.Equal(1, storage.SaveCount);

        var loadingStore = new SyncRecordStore(storage);
        await loadingStore.LoadAsync(None);

        Assert.Equal(1, storage.LoadCount);

        var loaded = Assert.Single(loadingStore.Records);
        Assert.Equal(original.SourceKey, loaded.SourceKey);
        Assert.Equal(original.SourceId, loaded.SourceId);
        Assert.Equal(original.SourceUrl, loaded.SourceUrl);
        Assert.Equal(original.TargetKind, loaded.TargetKind);
        Assert.Equal(original.AiaId, loaded.AiaId);
        Assert.Equal(original.LastSourceCompleted, loaded.LastSourceCompleted);
        Assert.Equal(original.LastAiaCompleted, loaded.LastAiaCompleted);
        Assert.Equal(original.LastSourceDueUtc, loaded.LastSourceDueUtc);
        Assert.Equal(original.LastAiaDueUtc, loaded.LastAiaDueUtc);
        Assert.Equal(original.LastSyncedUtc, loaded.LastSyncedUtc);
    }

    [Fact]
    public async Task LoadAsync_WithMalformedJson_ReturnsEmptyRecords()
    {
        var storage = new FakePluginSettingsStorage();
        storage.Set("sync.records", "this is not json {");

        var store = new SyncRecordStore(storage);

        await store.LoadAsync(None);

        Assert.Empty(store.Records);
    }

    [Fact]
    public async Task LoadAsync_WithMissingSetting_ReturnsEmptyRecords()
    {
        var storage = new FakePluginSettingsStorage();
        var store = new SyncRecordStore(storage);

        await store.LoadAsync(None);

        Assert.Empty(store.Records);
    }

    private static SyncRecordStore CreateStore() => new(new FakePluginSettingsStorage());

    private static SyncRecord CreateRecord(string sourceKey, string sourceId)
    {
        return new SyncRecord
        {
            SourceKey = sourceKey,
            SourceId = sourceId,
            SourceUrl = $"https://example.com/items/{sourceId}",
            TargetKind = SyncTargetKind.Task,
            AiaId = Guid.NewGuid(),
            LastSourceCompleted = false,
            LastAiaCompleted = false,
            LastSourceDueUtc = DueUtc,
            LastAiaDueUtc = DueUtc,
            LastSyncedUtc = DueUtc
        };
    }

    private sealed class FakePluginSettingsStorage : IPluginSettingsStorage
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public T? Get<T>(string key, T? defaultValue = default)
        {
            return _values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;
        }

        public void Set<T>(string key, T value)
        {
            _values[key] = value;
        }

        public Task SaveAsync()
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task LoadAsync()
        {
            LoadCount++;
            return Task.CompletedTask;
        }
    }
}
