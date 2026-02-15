using Android.App;
using Android.Views;
using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.Android;

/// <summary>
/// Android implementation of screen wake service using WindowManager flags.
/// </summary>
public class AndroidScreenWakeService : IScreenWakeService
{
    private readonly Activity _activity;

    public AndroidScreenWakeService(Activity activity)
    {
        _activity = activity;
    }

    public void SetKeepScreenOn(bool keepOn)
    {
        _activity.RunOnUiThread(() =>
        {
            if (keepOn)
            {
                _activity.Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
            }
            else
            {
                _activity.Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
            }
        });
    }
}
