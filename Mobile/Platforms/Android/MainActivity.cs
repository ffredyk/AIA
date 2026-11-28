using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AIA.Mobile.Services;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;

namespace AIA.Mobile.Platforms.Android
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                               ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [IntentFilter(new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = "aia", DataHost = "pair")]
    public class MainActivity : MauiAppCompatActivity
    {
        private const int PermissionRequestCode = 1001;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            CreateNotificationChannel();
            RequestRuntimePermissions();
            SyncJobService.Schedule();
            _ = MobileApp.InitializeAsync();
            HandlePairingLink(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            HandlePairingLink(intent);
        }

        private void HandlePairingLink(Intent? intent)
        {
            var data = intent?.Data;
            if (data == null || !string.Equals(data.Scheme, "aia", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(data.Host, "pair", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var url = data.GetQueryParameter("url");
            var code = data.GetQueryParameter("code");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(code))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var message = await MobileApp.PairFromLinkAsync(url, code);
                    ShowToast(message);
                }
                catch (Exception ex)
                {
                    ShowToast("Pairing failed: " + ex.Message);
                }
            });
        }

        private static void ShowToast(string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                global::Android.Widget.Toast.MakeText(global::Android.App.Application.Context, message,
                    global::Android.Widget.ToastLength.Long)?.Show();
            });
        }

        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return;

            var manager = (NotificationManager?)GetSystemService(NotificationService);
            if (manager == null)
                return;

            var channel = new NotificationChannel(NotificationReceiver.ChannelId, new Java.Lang.String("AIA reminders"), NotificationImportance.Default)
            {
                Description = "Task, reminder, and calendar notifications"
            };
            manager.CreateNotificationChannel(channel);
        }

        private void RequestRuntimePermissions()
        {
            var missing = new List<string>();

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
                ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                missing.Add(Manifest.Permission.PostNotifications);
            }

            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.ReadCalendar) != Permission.Granted)
                missing.Add(Manifest.Permission.ReadCalendar);

            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.WriteCalendar) != Permission.Granted)
                missing.Add(Manifest.Permission.WriteCalendar);

            if (missing.Count > 0)
                ActivityCompat.RequestPermissions(this, missing.ToArray(), PermissionRequestCode);
        }
    }
}
