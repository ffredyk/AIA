using Microsoft.Maui.Storage;

namespace AIA.Mobile.Services
{
    public static class MobileSettingsService
    {
        private const string ServerUrlKey = "aia_server_url";
        private const string DeviceNameKey = "aia_device_name";
        private const string CalendarMirrorKey = "aia_calendar_mirror";
        private const string NotificationsKey = "aia_notifications";
        private const string TokenKey = "aia_device_token";

        public static string ServerUrl
        {
            get => Preferences.Default.Get(ServerUrlKey, string.Empty);
            set => Preferences.Default.Set(ServerUrlKey, value ?? string.Empty);
        }

        public static string DeviceName
        {
            get => Preferences.Default.Get(DeviceNameKey, $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}");
            set => Preferences.Default.Set(DeviceNameKey, value ?? string.Empty);
        }

        public static bool CalendarMirrorEnabled
        {
            get => Preferences.Default.Get(CalendarMirrorKey, true);
            set => Preferences.Default.Set(CalendarMirrorKey, value);
        }

        public static bool NotificationsEnabled
        {
            get => Preferences.Default.Get(NotificationsKey, true);
            set => Preferences.Default.Set(NotificationsKey, value);
        }

        public static Task<string?> GetTokenAsync() => SecureStorage.Default.GetAsync(TokenKey);

        public static Task SetTokenAsync(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                SecureStorage.Default.Remove(TokenKey);
                return Task.CompletedTask;
            }

            return SecureStorage.Default.SetAsync(TokenKey, token);
        }
    }
}
