using AIA.Sync.Contracts;
using AIA.Sync.Core;

namespace AIA.Sync.Tests
{
    public class SyncClientEngineTests
    {
        [Fact]
        public async Task SendsPendingChangesAndAppliesResponse()
        {
            var store = new FakeClientStore();
            var dto = TestData.Task();
            store.Pending.Add(TestData.Change(dto));

            var response = new SyncResponse
            {
                ServerRevision = 7,
                Changes =
                {
                    new SyncEntityEnvelope
                    {
                        EntityType = SyncEntityType.Task,
                        Id = dto.Id,
                        Revision = 7,
                        ModifiedUtc = DateTimeOffset.UtcNow,
                        Data = System.Text.Json.JsonSerializer.SerializeToElement(dto, SyncJson.Options)
                    }
                },
                Accepted =
                {
                    new SyncAck { EntityType = SyncEntityType.Task, Id = dto.Id, Revision = 7 }
                }
            };

            var transport = new FakeTransport { Response = response };
            var engine = new SyncClientEngine(store, transport);

            var result = await engine.SyncAsync(CancellationToken.None);

            Assert.Single(transport.LastRequest!.Changes);
            Assert.Single(store.Applied);
            Assert.Single(store.Accepted);
            Assert.Equal(7, store.Revision);
            Assert.Equal(7, result.ServerRevision);
        }

        [Fact]
        public async Task ResetResponseClearsLocalState()
        {
            var store = new FakeClientStore();
            var transport = new FakeTransport
            {
                Response = new SyncResponse
                {
                    ServerRevision = 12,
                    ResetRequired = true,
                    Changes = new List<SyncEntityEnvelope>
                    {
                        new()
                        {
                            EntityType = SyncEntityType.Task,
                            Id = Guid.NewGuid(),
                            Revision = 12,
                            ModifiedUtc = DateTimeOffset.UtcNow
                        }
                    }
                }
            };

            var engine = new SyncClientEngine(store, transport);
            await engine.SyncAsync(CancellationToken.None);

            Assert.True(store.ResetApplied);
            Assert.Single(store.Applied);
            Assert.Equal(12, store.Revision);
        }
    }
}
