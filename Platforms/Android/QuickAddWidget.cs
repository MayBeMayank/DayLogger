using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;
using DayLogger.Services;

namespace DayLogger;

/// <summary>Home-screen button that opens the new-expense form. It holds no data, so it never needs refreshing.</summary>
[BroadcastReceiver(Label = "DayLogger quick add", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_quick_add_info")]
public class QuickAddWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context == null || appWidgetManager == null || appWidgetIds == null) return;

        // The data URI is read back by MainActivity; SingleTop delivers it to the running app as a new intent.
        var open = new Intent(context, typeof(MainActivity))
            .SetAction(Intent.ActionView)!
            .SetData(Android.Net.Uri.Parse(QuickAdd.ExpenseUri))!
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        var pending = PendingIntent.GetActivity(context, 0, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_quick_add);
        views.SetOnClickPendingIntent(Resource.Id.widget_add, pending);
        appWidgetManager.UpdateAppWidget(appWidgetIds, views);
    }
}
