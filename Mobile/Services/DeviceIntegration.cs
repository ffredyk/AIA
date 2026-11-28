namespace AIA.Mobile.Services
{
    public interface IDeviceIntegration
    {
        Task RefreshAsync(SqliteSyncStore store, CancellationToken cancellationToken);
    }

    public sealed class NoopDeviceIntegration : IDeviceIntegration
    {
        public Task RefreshAsync(SqliteSyncStore store, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
