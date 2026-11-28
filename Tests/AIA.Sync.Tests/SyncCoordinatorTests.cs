using System.Text.Json;
using AIA.Sync.Contracts;
using AIA.Sync.Core;

namespace AIA.Sync.Tests
{
    public class SyncCoordinatorTests
    {
        [Fact]
        public async Task PushNewTaskIsAcceptedAndReturned()
        {
            var backend = new FakeBackend();
            var coordinator = new SyncCoordinator(backend, new InMemoryJournalStore());
            var dto = TestData.Task();

            var response = await coordinator.SyncAsync(new SyncRequest
            {
                Changes = { TestData.Change(dto) }
            }, CancellationToken.None);

            Assert.Single(response.Accepted);
            Assert.Empty(response.Conflicts);
            var change = Assert.Single(response.Changes);
            Assert.False(change.IsDeleted);
            Assert.Equal(dto.Id, change.Id);
            Assert.Equal(1, backend.ApplyCount);
        }

        [Fact]
        public async Task StaleBaseRevisionProducesConflictAndKeepsServerVersion()
        {
            var backend = new FakeBackend();
            var coordinator = new SyncCoordinator(backend, new InMemoryJournalStore());
            var dto = TestData.Task("Desktop");

            var first = await coordinator.SyncAsync(new SyncRequest
            {
                Changes = { TestData.Change(dto) }
            }, CancellationToken.None);

            dto.Title = "Phone";
            var phoneChange = TestData.Change(dto, baseRevision: 0);

            var second = await coordinator.SyncAsync(new SyncRequest
            {
                SinceRevision = first.ServerRevision,
                Changes = { phoneChange }
            }, CancellationToken.None);

            var conflict = Assert.Single(second.Conflicts);
            Assert.NotNull(conflict.ServerVersion);
            Assert.Equal("Desktop", DeserializeTitle(conflict.ServerVersion!.Data));
            Assert.Empty(second.Accepted);
        }

        [Fact]
        public async Task DeleteUsesTombstone()
        {
            var backend = new FakeBackend();
            var coordinator = new SyncCoordinator(backend, new InMemoryJournalStore());
            var dto = TestData.Task();

            var first = await coordinator.SyncAsync(new SyncRequest
            {
                Changes = { TestData.Change(dto) }
            }, CancellationToken.None);

            var revision = first.Accepted.Single().Revision;
            var deleted = await coordinator.SyncAsync(new SyncRequest
            {
                SinceRevision = first.ServerRevision,
                Changes =
                {
                    new SyncEntityChange
                    {
                        EntityType = SyncEntityType.Task,
                        Id = dto.Id,
                        BaseRevision = revision,
                        IsDeleted = true
                    }
                }
            }, CancellationToken.None);

            Assert.Single(deleted.Accepted);
            var tombstone = Assert.Single(deleted.Changes);
            Assert.True(tombstone.IsDeleted);
            Assert.Empty(backend.Tasks);
        }

        [Fact]
        public async Task CursorAtServerRevisionReturnsNoChanges()
        {
            var backend = new FakeBackend();
            var coordinator = new SyncCoordinator(backend, new InMemoryJournalStore());
            var first = await coordinator.SyncAsync(new SyncRequest(), CancellationToken.None);

            var second = await coordinator.SyncAsync(new SyncRequest
            {
                SinceRevision = first.ServerRevision
            }, CancellationToken.None);

            Assert.Empty(second.Changes);
            Assert.False(second.ResetRequired);
        }

        private static string? DeserializeTitle(JsonElement? data)
        {
            if (data is null)
                return null;

            return data.Value.Deserialize<TaskDto>(SyncJson.Options)?.Title;
        }
    }
}
