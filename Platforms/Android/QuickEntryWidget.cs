using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace DayLogger;

/// <summary>Home-screen button that opens the quick-entry sheet (<see cref="QuickEntryActivity"/>) over the home screen.</summary>
[BroadcastReceiver(Label = "DayLogger quick entry", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_quick_entry_info")]
public class QuickEntryWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context == null || appWidgetManager == null || appWidgetIds == null) return;

        var open = new Intent(context, typeof(QuickEntryActivity)).AddFlags(ActivityFlags.NewTask);
        var pending = PendingIntent.GetActivity(context, 1, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_quick_entry);
        views.SetOnClickPendingIntent(Resource.Id.widget_entry, pending);
        appWidgetManager.UpdateAppWidget(appWidgetIds, views);
    }
}
