namespace AIA.Mobile.Services
{
    public static class MobileApp
    {
        private static Task? _initialization;

        public static SqliteSyncStore Store { get; } = new();
        public static SyncService Sync { get; } = new(Store);
        public static IDeviceIntegration DeviceIntegration { get; set; } = new NoopDeviceIntegration();

        static MobileApp()
        {
#if ANDROID
            DeviceIntegration = new global::AIA.Mobile.Platforms.Android.AndroidDeviceIntegration(Store);
#endif
        }

        public static Task InitializeAsync()
        {
            _initialization ??= InitializeCoreAsync();
            return _initialization;
        }

        public static async Task<string> PairFromLinkAsync(string url, string code)
        {
            var normalized = HttpSyncTransport.NormalizeBaseUrl(url);
            var response = await HttpSyncTransport.PairAsync(normalized, code, MobileSettingsService.DeviceName).ConfigureAwait(false);

            await MobileSettingsService.SetTokenAsync(response.Token).ConfigureAwait(false);
            MobileSettingsService.ServerUrl = normalized;

            await InitializeAsync().ConfigureAwait(false);
            await Sync.SyncNowAsync().ConfigureAwait(false);
            return $"Paired as {MobileSettingsService.DeviceName}.";
        }

        private static async Task InitializeCoreAsync()
        {
            await Store.InitializeAsync().ConfigureAwait(false);
        }
    }
}
