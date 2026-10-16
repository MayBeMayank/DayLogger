using Android.App;
using Android.Content;
using AndroidX.Core.App;
using DayLogger.Services;

namespace DayLogger;

public class AndroidReminderPlatform : IReminderPlatform
{
    public const string ChannelId = "reminders";
    public const string ExtraKey = "key";

    static Context Ctx => Android.App.Application.Context;

    // Android fires through ReminderReceiver, never through this.
    public Func<string, Task>? Fired { get; set; }

    public async Task<bool> RequestPermissionAsync() =>
        await Permissions.RequestAsync<Permissions.PostNotifications>() == PermissionStatus.Granted;

    // Inexact-while-idle: reminders may land a few minutes late in Doze, and it needs no exact-alarm permission.
    public void Schedule(string key, DateTime at) =>
        Alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, new DateTimeOffset(at).ToUnixTimeMilliseconds(), AlarmIntent(key));

    public void Cancel(string key) => Alarms.Cancel(AlarmIntent(key));

    public void Show(string key, string title, string body)
    {
        EnsureChannel();
        var launch = Ctx.PackageManager!.GetLaunchIntentForPackage(Ctx.PackageName!);
        var tap = PendingIntent.GetActivity(Ctx, 0, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var notification = new NotificationCompat.Builder(Ctx, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_reminder)!
            .SetContentTitle(title)!
            .SetContentText(body)!
            .SetAutoCancel(true)!
            .SetContentIntent(tap)!
            .Build()!;
        NotificationManagerCompat.From(Ctx)!.Notify(StableId(key), notification);
    }

    static AlarmManager Alarms => (AlarmManager)Ctx.GetSystemService(Context.AlarmService)!;

    static PendingIntent AlarmIntent(string key)
    {
        // The data URI makes each key's PendingIntent distinct.
        var intent = new Intent(Ctx, typeof(ReminderReceiver))
            .SetData(Android.Net.Uri.Parse("daylogger://reminder/" + Uri.EscapeDataString(key)))!
            .PutExtra(ExtraKey, key)!;
        return PendingIntent.GetBroadcast(Ctx, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    static void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var manager = (NotificationManager)Ctx.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) != null) return;
        manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Reminders", NotificationImportance.Default)
        {
            Description = "Streak, meal and expense reminders",
        });
    }

    // string.GetHashCode is randomised per process, so the same reminder would stop replacing itself.
    static int StableId(string key)
    {
        unchecked
        {
            int h = 17;
            foreach (var c in key) h = h * 31 + c;
            return h;
        }
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public class ReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var key = intent?.GetStringExtra(AndroidReminderPlatform.ExtraKey);
        var service = IPlatformApplication.Current?.Services.GetService<ReminderService>();
        if (key == null || service == null) return;

        var pending = GoAsync();
        Task.Run(async () =>
        {
            try { await service.HandleFiredAsync(key); }
            finally { pending?.Finish(); }
        });
    }
}

/// <summary>Alarms don't survive a reboot or an app update, so plan everything again.</summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
public class ReminderBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var service = IPlatformApplication.Current?.Services.GetService<ReminderService>();
        if (service == null) return;

        var pending = GoAsync();
        Task.Run(async () =>
        {
            try { await service.RefreshAllAsync(); }
            finally { pending?.Finish(); }
        });
    }
}
