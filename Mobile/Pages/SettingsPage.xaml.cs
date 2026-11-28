using AIA.Mobile.Services;

namespace AIA.Mobile.Pages
{
    public partial class SettingsPage : ContentPage
    {
        public SettingsPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            TxtServerUrl.Text = MobileSettingsService.ServerUrl;
            TxtDeviceName.Text = MobileSettingsService.DeviceName;
            SwCalendar.IsToggled = MobileSettingsService.CalendarMirrorEnabled;
            SwNotifications.IsToggled = MobileSettingsService.NotificationsEnabled;

            var token = await MobileSettingsService.GetTokenAsync();
            LblTokenStatus.Text = string.IsNullOrWhiteSpace(token) ? "Not paired" : "Device token stored";
            StatusLabel.Text = MobileApp.Sync.LastSyncUtc is { } last
                ? $"Last sync {last.ToLocalTime():g}"
                : string.Empty;
        }

        private async void BtnPair(object sender, EventArgs e)
        {
            try
            {
                var url = HttpSyncTransport.NormalizeBaseUrl(TxtServerUrl.Text);
                var code = TxtPairingCode.Text?.Trim() ?? string.Empty;
                var name = string.IsNullOrWhiteSpace(TxtDeviceName.Text) ? "Android device" : TxtDeviceName.Text.Trim();

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(code))
                {
                    StatusLabel.Text = "Enter server URL and pairing code.";
                    return;
                }

                StatusLabel.Text = "Pairing...";
                var response = await HttpSyncTransport.PairAsync(url, code, name);
                await MobileSettingsService.SetTokenAsync(response.Token);
                MobileSettingsService.ServerUrl = url;
                MobileSettingsService.DeviceName = name;
                TxtPairingCode.Text = string.Empty;
                LblTokenStatus.Text = "Device token stored";
                StatusLabel.Text = $"Paired as {name}.";
                await MobileApp.Sync.SyncNowAsync();
                StatusLabel.Text = "Paired and synced.";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = ex.Message;
            }
        }

        private async void BtnSave(object sender, EventArgs e)
        {
            try
            {
                var url = HttpSyncTransport.NormalizeBaseUrl(TxtServerUrl.Text);
                MobileSettingsService.ServerUrl = url;
                MobileSettingsService.DeviceName = string.IsNullOrWhiteSpace(TxtDeviceName.Text)
                    ? "Android device"
                    : TxtDeviceName.Text.Trim();

                if (!string.IsNullOrWhiteSpace(TxtToken.Text))
                {
                    await MobileSettingsService.SetTokenAsync(TxtToken.Text.Trim());
                    TxtToken.Text = string.Empty;
                }

                var token = await MobileSettingsService.GetTokenAsync();
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token))
                {
                    StatusLabel.Text = "Set server URL and pair first.";
                    return;
                }

                StatusLabel.Text = "Testing...";
                await MobileApp.Sync.SyncNowAsync();
                StatusLabel.Text = "Connected.";
                LblTokenStatus.Text = "Device token stored";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = ex.Message;
            }
        }

        private async void BtnSync(object sender, EventArgs e)
        {
            try
            {
                StatusLabel.Text = "Syncing...";
                var result = await MobileApp.Sync.SyncNowAsync();
                StatusLabel.Text = result == null
                    ? "Nothing to sync."
                    : $"Synced. {result.Changes.Count} server changes, {result.Conflicts.Count} conflicts.";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = ex.Message;
            }
        }

        private void SwCalendar_Toggled(object sender, ToggledEventArgs e)
        {
            MobileSettingsService.CalendarMirrorEnabled = e.Value;
            _ = MobileApp.DeviceIntegration.RefreshAsync(MobileApp.Store, CancellationToken.None);
        }

        private void SwNotifications_Toggled(object sender, ToggledEventArgs e)
        {
            MobileSettingsService.NotificationsEnabled = e.Value;
            _ = MobileApp.DeviceIntegration.RefreshAsync(MobileApp.Store, CancellationToken.None);
        }
    }
}
