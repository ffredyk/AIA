using Android.App;
using Android.App.Job;
using Android.Content;
using AIA.Mobile.Services;

namespace AIA.Mobile.Platforms.Android
{
    [Service(Exported = false, Permission = "android.permission.BIND_JOB_SERVICE")]
    public class SyncJobService : JobService
    {
        private const int JobId = 4242;

        public static void Schedule()
        {
            var context = global::Android.App.Application.Context;
            var scheduler = (JobScheduler?)context.GetSystemService(Context.JobSchedulerService);
            if (scheduler == null)
                return;

            var job = new JobInfo.Builder(JobId, new ComponentName(context, Java.Lang.Class.FromType(typeof(SyncJobService))))
                .SetPeriodic(15 * 60 * 1000L)
                .SetRequiredNetworkType(NetworkType.Any)
                .SetPersisted(true)
                .Build();

            try
            {
                scheduler.Schedule(job);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sync job could not be scheduled: {ex.Message}");
            }
        }

        public override bool OnStartJob(JobParameters? parameters)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await MobileApp.InitializeAsync();
                    await MobileApp.Sync.SyncNowAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Background sync failed: {ex.Message}");
                }
                finally
                {
                    JobFinished(parameters, false);
                }
            });

            return true;
        }

        public override bool OnStopJob(JobParameters? parameters) => true;
    }
}
