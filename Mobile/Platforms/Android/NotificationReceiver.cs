using Android.App;
using Android.Content;
using AndroidX.Core.App;
using AIA.Mobile.Platforms.Android;

namespace AIA.Mobile.Platforms.Android
{
    [BroadcastReceiver(Enabled = true, Exported = false)]
    public class NotificationReceiver : BroadcastReceiver
    {
        public const string ChannelId = "aia_reminders";

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null || intent == null)
                return;

            var title = intent.GetStringExtra("title") ?? "AIA";
            var message = intent.GetStringExtra("message") ?? string.Empty;
            var notificationId = intent.GetIntExtra("id", 0);

            EnsureChannel(context);

            var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);
            PendingIntent? contentIntent = null;
            if (launchIntent != null)
            {
                contentIntent = PendingIntent.GetActivity(
                    context,
                    notificationId,
                    launchIntent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            }

            var builder = new NotificationCompat.Builder(context, ChannelId)
                .SetContentTitle(title)
                .SetContentText(message)
                .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
                .SetAutoCancel(true)
                .SetPriority((int)NotificationPriority.Default);

            if (contentIntent != null)
                builder.SetContentIntent(contentIntent);

            try
            {
                NotificationManagerCompat.From(context).Notify(notificationId, builder.Build());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification could not be shown: {ex.Message}");
            }
        }

        private static void EnsureChannel(Context context)
        {
            if (global::Android.OS.Build.VERSION.SdkInt < global::Android.OS.BuildVersionCodes.O)
                return;

            var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            if (manager == null)
                return;

            var channel = new NotificationChannel(ChannelId, new Java.Lang.String("AIA reminders"), NotificationImportance.Default);
            manager.CreateNotificationChannel(channel);
        }
    }
}
