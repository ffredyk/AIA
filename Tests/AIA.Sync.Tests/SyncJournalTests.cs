using System.Text.Json;
using AIA.Sync.Contracts;
using AIA.Sync.Core;

namespace AIA.Sync.Tests
{
    public class SyncJournalTests
    {
        [Fact]
        public void NewEntityGetsRevision()
        {
            var journal = new SyncJournal();
            var dto = TestData.Task();

            var assigned = journal.ApplySnapshot(Snapshot(dto));

            Assert.Equal(1, journal.Revision);
            Assert.Equal(1, assigned[$"Task:{dto.Id:N}"]);
            Assert.Equal(1, journal.ActiveCount);
        }

        [Fact]
        public void UnchangedSnapshotDoesNotBumpRevision()
        {
            var journal = new SyncJournal();
            var dto = TestData.Task();
            journal.ApplySnapshot(Snapshot(dto));
            var revision = journal.Revision;

            journal.ApplySnapshot(Snapshot(dto));

            Assert.Equal(revision, journal.Revision);
        }

        [Fact]
        public void ChangedEntityBumpsRevision()
        {
            var journal = new SyncJournal();
            var dto = TestData.Task();
            journal.ApplySnapshot(Snapshot(dto));

            dto.Title = "Changed";
            journal.ApplySnapshot(Snapshot(dto));

            Assert.Equal(2, journal.Revision);
        }

        [Fact]
        public void RemovedEntityBecomesTombstone()
        {
            var journal = new SyncJournal();
            var dto = TestData.Task();
            journal.ApplySnapshot(Snapshot(dto));

            journal.ApplySnapshot(new SyncSnapshot());

            var changes = journal.GetChangesSince(1, out var reset);
            Assert.False(reset);
            var tombstone = Assert.Single(changes);
            Assert.True(tombstone.IsDeleted);
            Assert.Equal(dto.Id, tombstone.Id);
        }

        [Fact]
        public void ResetIsRequiredBelowResetFloor()
        {
            var journal = new SyncJournal();
            var dto = TestData.Task();
            journal.ApplySnapshot(Snapshot(dto));
            journal.ApplySnapshot(new SyncSnapshot());

            var state = journal.ToState();
            state.ResetFloor = state.Revision;

            var reloaded = new SyncJournal(state);
            var changes = reloaded.GetChangesSince(0, out var reset);

            Assert.True(reset);
            Assert.Empty(changes);
        }

        private static SyncSnapshot Snapshot(TaskDto dto)
        {
            return new SyncSnapshot
            {
                Entities =
                {
                    new SyncSnapshotEntity
                    {
                        EntityType = SyncEntityType.Task,
                        Id = dto.Id,
                        Data = JsonSerializer.SerializeToElement(dto, SyncJson.Options)
                    }
                }
            };
        }
    }
}
